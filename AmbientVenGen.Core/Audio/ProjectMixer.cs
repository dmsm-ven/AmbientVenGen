using AmbientVenGen.Core.Composition;
using NAudio.Wave;

namespace AmbientVenGen.Core.Audio;

/// <summary>
/// Renders a whole project: every layer by time, then reverb and a soft limiter.
/// The same class feeds live playback and offline export. The project can be swapped
/// while playing (<see cref="SetSpec"/>); layer sound state survives the swap.
/// </summary>
public sealed class ProjectMixer : ISampleProvider
{
    public const int SampleRate = 44100;
    private const int Channels = 2;
    private const int BlockFrames = 128;
    private const float LayerScale = 0.35f;
    private const float GainSmoothing = 0.0023f; // ~10 ms

    private readonly MasterAnalyzer? _analyzer;
    private readonly MasterReverb _reverb = new();
    private readonly MasterReverb _shimmerReverb = new();   // its own room: only the layers with a shimmer send feed it
    private readonly Dictionary<Guid, LayerRuntime> _runtimes = new();
    private readonly float[] _scratchLeft = new float[BlockFrames];
    private readonly float[] _scratchRight = new float[BlockFrames];

    private float[] _mixLeft = new float[4096];
    private float[] _mixRight = new float[4096];
    private float[] _sendLeft = new float[4096];   // what the layers send to the reverb
    private float[] _sendRight = new float[4096];
    private float[] _shimmerLeft = new float[4096];  // what the layers send to the shimmer reverb
    private float[] _shimmerRight = new float[4096];
    private ProjectSpec _spec;
    private long _positionFrames;

    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels);

    public double PositionSeconds => Volatile.Read(ref _positionFrames) / (double)SampleRate;

    public ProjectMixer(ProjectSpec spec, MasterAnalyzer? analyzer = null)
    {
        _spec = spec;
        _analyzer = analyzer;
    }

    public void SetSpec(ProjectSpec spec) => Volatile.Write(ref _spec, spec);

    public void Seek(double seconds) =>
        Volatile.Write(ref _positionFrames, (long)(Math.Max(0, seconds) * SampleRate));

    public int Read(Span<float> buffer)
    {
        ProjectSpec spec = Volatile.Read(ref _spec);
        long position = Volatile.Read(ref _positionFrames);
        long totalFrames = (long)(spec.DurationSeconds * SampleRate);
        long remaining = totalFrames - position;

        if (remaining <= 0)
            return 0;

        int frames = (int)Math.Min(buffer.Length / Channels, remaining);
        if (frames <= 0)
            return 0;

        EnsureCapacity(frames);
        Array.Clear(_mixLeft, 0, frames);
        Array.Clear(_mixRight, 0, frames);
        Array.Clear(_sendLeft, 0, frames);
        Array.Clear(_sendRight, 0, frames);
        Array.Clear(_shimmerLeft, 0, frames);
        Array.Clear(_shimmerRight, 0, frames);

        LayerSpec[] layers = spec.Layers;

        for (int done = 0; done < frames; done += BlockFrames)
        {
            int count = Math.Min(BlockFrames, frames - done);
            double blockTime = (position + done + count * 0.5) / SampleRate;

            for (int i = 0; i < layers.Length; i++)
                MixLayer(layers[i], spec.Seed, blockTime, count, done);
        }

        // Two rooms: the plain reverb (Reverb send) and the shimmer reverb (Shimmer send, lifted by the shimmer interval).
        float reverbMix = (float)spec.ReverbMix;
        if (reverbMix > 0.0001f)
        {
            float dry = 1f - reverbMix * 0.45f;

            for (int i = 0; i < frames; i++)
            {
                _mixLeft[i] *= dry;
                _mixRight[i] *= dry;
            }

            _reverb.AddWet(_mixLeft, _mixRight, _sendLeft, _sendRight, frames, reverbMix);
            _shimmerReverb.AddWet(_mixLeft, _mixRight, _shimmerLeft, _shimmerRight, frames, reverbMix, (float)spec.Shimmer, spec.ShimmerSemitones);
        }

        float peakLeft = 0f;
        float peakRight = 0f;
        float overPeak = 0f;

        for (int i = 0; i < frames; i++)
        {
            float rawLeft = _mixLeft[i];
            float rawRight = _mixRight[i];

            overPeak = Math.Max(overPeak, Math.Max(Math.Abs(rawLeft), Math.Abs(rawRight)));

            float left = MathF.Tanh(rawLeft);
            float right = MathF.Tanh(rawRight);

            buffer[i * 2] = left;
            buffer[i * 2 + 1] = right;

            peakLeft = Math.Max(peakLeft, Math.Abs(left));
            peakRight = Math.Max(peakRight, Math.Abs(right));
        }

        _analyzer?.Push(buffer, frames, peakLeft, peakRight, overPeak);

        // If Seek() was called while this block was rendering, keep the new position.
        Interlocked.CompareExchange(ref _positionFrames, position + frames, position);

        return frames * Channels;
    }

    private void MixLayer(LayerSpec layer, int projectSeed, double blockTime, int count, int offset)
    {
        if (!_runtimes.TryGetValue(layer.Id, out LayerRuntime? runtime)
            || runtime.Type != layer.Type
            || runtime.SourcePath != layer.SourcePath
            || runtime.NoiseType != layer.NoiseType)
        {
            runtime = new LayerRuntime(
                layer.Type,
                layer.SourcePath,
                LayerGenerator.Create(layer.Type, MixSeed(projectSeed, layer.Seed), layer.SourcePath, layer.NoiseType, layer.TransformFor(0)),
                layer.NoiseType,
                MixSeed(projectSeed, layer.Seed),
                layer.TransformFor(0));
            _runtimes[layer.Id] = runtime;
        }

        double local = blockTime - layer.StartSeconds;

        // Repeats: time inside the current play (curves, fades and recordings restart with every play); a gap is silent.
        bool inPlay = layer.TryGetPlayTime(local, out double within, out int play);
        bool active = !layer.Muted && inPlay;

        // The processing of a recording may differ from play to play: the generator is changed at the start of the play
        // (the effects stay, so an echo still rings out over the change).
        SampleTransform transform = layer.TransformFor(play);
        if (runtime.Transform != transform)
        {
            runtime.Generator = LayerGenerator.Create(layer.Type, runtime.Seed, layer.SourcePath, layer.NoiseType, transform);
            runtime.Transform = transform;
        }

        // The modulators move the level, the filter, the pan, the reverb send and the generator knobs around their set values.
        ModOffsets mod = ModOffsets.Evaluate(layer, runtime.Seed, blockTime);

        float target = active ? (float)(layer.GainAt(within, layer.OverrideOf(play, RepeatParam.Level)) * mod.LevelMultiplier) : 0f;

        bool silent = target <= 0f && runtime.Gain < 1e-5f;

        if (silent)
        {
            runtime.Gain = 0f;

            // Nothing to play, but an echo of the layer may still be ringing out.
            if (!runtime.Effects.HasTail && !runtime.Events.IsActive)
                return;

            Array.Clear(_scratchLeft, 0, count);
            Array.Clear(_scratchRight, 0, count);
        }
        else
        {
            double curveTime = Math.Clamp(inPlay ? within : local, 0, layer.DurationSeconds);

            // Pitch drift: a slow wandering of the pitch (of the speed, for a recording); Noise has no pitch.
            double drift = layer.Type == LayerType.Noise ? 0.0 : layer.PitchDrift * 0.5 * runtime.PitchWander.At(blockTime);
            float pitchFactor = (float)Math.Pow(2.0, drift / 12.0);

            var block = new BlockParams(
                (float)Math.Clamp((layer.OverrideOf(play, RepeatParam.Brightness) ?? layer.BrightnessAt(curveTime)) + mod.Brightness, 0.0, 1.0),
                (float)Math.Clamp((layer.OverrideOf(play, RepeatParam.Modulation) ?? layer.ModulationAt(curveTime)) + mod.Modulation, 0.0, 1.0),
                (float)(layer.OverrideOf(play, RepeatParam.Pitch) ?? layer.PitchHz) * pitchFactor,
                Math.Max(0.0, (inPlay ? within : local) - count * 0.5 / SampleRate),
                (float)(layer.OverrideOf(play, RepeatParam.Speed) ?? layer.Speed) * pitchFactor);

            runtime.Generator.Render(in block, _scratchLeft.AsSpan(0, count), _scratchRight.AsSpan(0, count));

            float level = runtime.Gain;

            for (int i = 0; i < count; i++)
            {
                level += (target - level) * GainSmoothing;
                float scaled = level * LayerScale;
                _scratchLeft[i] *= scaled;
                _scratchRight[i] *= scaled;
            }

            runtime.Gain = level;
        }

        // Filter and echo come after the level, so the echo outlives the layer.
        runtime.Effects.Process(
            _scratchLeft,
            _scratchRight,
            count,
            Math.Clamp((layer.OverrideOf(play, RepeatParam.Cutoff) ?? layer.FilterCutoffHz) * mod.CutoffFactor, Layer.MinCutoffHz, Layer.MaxCutoffHz),
            layer.OverrideOf(play, RepeatParam.Resonance) ?? layer.FilterResonance,
            layer.OverrideOf(play, RepeatParam.DelayTime) ?? layer.DelaySeconds,
            layer.OverrideOf(play, RepeatParam.DelayFeedback) ?? layer.DelayFeedback,
            mod.DelayInput);

        // One-shot events (echo throw, stutter) work on what the layer sounds like now; no events in a muted layer.
        // Before the layer starts the time is negative (an event at its very start is not lost by a block that straddles it).
        runtime.Events.Process(
            _scratchLeft,
            _scratchRight,
            count,
            inPlay ? within : local < 0 ? local : -1e9,
            play,
            layer.Muted ? null : layer.Events);

        // Constant-power pan (with a slow random wandering); the centre position keeps unity gain.
        double pan = Math.Clamp((layer.OverrideOf(play, RepeatParam.Pan) ?? layer.Pan) + mod.Pan + layer.PanDrift * 0.6 * runtime.PanWander.At(blockTime), -1.0, 1.0);
        float angle = (float)((pan + 1.0) * Math.PI / 4.0);
        float panLeft = MathF.Cos(angle) * 1.4142f;
        float panRight = MathF.Sin(angle) * 1.4142f;
        float send = (float)Math.Clamp((layer.OverrideOf(play, RepeatParam.ReverbSend) ?? layer.ReverbSend) + mod.ReverbSend, 0.0, 1.0);
        float shimmerSend = (float)Math.Clamp(layer.OverrideOf(play, RepeatParam.ShimmerSend) ?? layer.ShimmerSendValue, 0.0, 1.0);

        for (int i = 0; i < count; i++)
        {
            float outLeft = _scratchLeft[i] * panLeft;
            float outRight = _scratchRight[i] * panRight;

            _mixLeft[offset + i] += outLeft;
            _mixRight[offset + i] += outRight;
            _sendLeft[offset + i] += outLeft * send;
            _sendRight[offset + i] += outRight * send;
            _shimmerLeft[offset + i] += outLeft * shimmerSend;
            _shimmerRight[offset + i] += outRight * shimmerSend;
        }
    }

    private void EnsureCapacity(int frames)
    {
        if (_mixLeft.Length >= frames)
            return;

        _mixLeft = new float[frames];
        _mixRight = new float[frames];
        _sendLeft = new float[frames];
        _sendRight = new float[frames];
        _shimmerLeft = new float[frames];
        _shimmerRight = new float[frames];
    }

    // Deterministic (string/Hash randomization would break "same seed, same sound").
    private static int MixSeed(int projectSeed, int layerSeed) =>
        unchecked(projectSeed * 397 ^ layerSeed * 16777619);

    /// <summary>A slow, smooth, random-looking value in -1..1 (two sines with unrelated slow rates), a function of time only.</summary>
    private sealed class Wander
    {
        private readonly double _phase1, _phase2, _rate1, _rate2;

        public Wander(Random random)
        {
            _phase1 = random.NextDouble() * Math.PI * 2;
            _phase2 = random.NextDouble() * Math.PI * 2;
            _rate1 = 0.02 + random.NextDouble() * 0.03;
            _rate2 = 0.05 + random.NextDouble() * 0.06;
        }

        public double At(double seconds) =>
            0.6 * Math.Sin(Math.PI * 2 * _rate1 * seconds + _phase1) + 0.4 * Math.Sin(Math.PI * 2 * _rate2 * seconds + _phase2);
    }

    private sealed class LayerRuntime
    {
        public LayerType Type { get; }
        public int Seed { get; }
        public SampleTransform Transform { get; set; }
        public string? SourcePath { get; }
        public LayerGenerator Generator { get; set; }
        public float Gain { get; set; }

        public NoiseType NoiseType { get; }
        public LayerEffects Effects { get; } = new();
        public LayerEvents Events { get; } = new();
        public Wander PitchWander { get; }
        public Wander PanWander { get; }

        public LayerRuntime(LayerType type, string? sourcePath, LayerGenerator generator, NoiseType noiseType, int seed, SampleTransform transform)
        {
            Seed = seed;
            Transform = transform;
            var random = new Random(seed ^ 0x5bd1e995);
            PitchWander = new Wander(random);
            PanWander = new Wander(random);
            NoiseType = noiseType;
            Type = type;
            SourcePath = sourcePath;
            Generator = generator;
        }
    }

    /// <summary>What the two modulators of a layer add to its parameters in one block.</summary>
    private readonly struct ModOffsets
    {
        public double LevelMultiplier { get; init; }
        public double CutoffFactor { get; init; }
        public double Pan { get; init; }
        public double ReverbSend { get; init; }
        public double Brightness { get; init; }
        public double Modulation { get; init; }
        public double DelayInput { get; init; }

        public static ModOffsets Evaluate(LayerSpec layer, int seed, double seconds)
        {
            double delayInput = 1.0, level = 1.0, octaves = 0, pan = 0, send = 0, brightness = 0, modulation = 0;

            void Add(ModulatorSpec? spec, int i)
            {
                if (spec is null || spec.Muted || spec.Target == ModTarget.None || spec.Depth <= 0)
                    return;

                double depth = Math.Clamp(spec.Depth, 0.0, 1.0);
                double value = spec.ValueAt(seconds, unchecked(seed + i * 7919));

                switch (spec.Target)
                {
                    case ModTarget.Level:
                        level *= 1.0 - depth * (1.0 - value) * 0.5;   // from the set level down: never louder than set
                        break;
                    case ModTarget.Cutoff:
                        octaves += depth * value * 3.0;               // up to +-3 octaves
                        break;
                    case ModTarget.Pan:
                        pan += depth * value;
                        break;
                    case ModTarget.ReverbSend:
                        send += depth * value * 0.5;
                        break;
                    case ModTarget.Brightness:
                        brightness += depth * value * 0.5;
                        break;
                    case ModTarget.Modulation:
                        modulation += depth * value * 0.5;
                        break;
                    case ModTarget.DelayInput:
                        delayInput *= 1.0 - depth * (1.0 - value) * 0.5;   // 1 = all of the layer feeds the echo, 0 = nothing: only the tail rings
                        break;
                }
            }

            if (layer.Modulators is { } list)
            {
                for (int i = 0; i < list.Count; i++)
                    Add(list[i], i);
            }
            else
            {
                // a spec of the older version: two fixed modulators
                Add(layer.Mod1, 0);
                Add(layer.Mod2, 1);
            }

            return new ModOffsets
            {
                LevelMultiplier = level,
                CutoffFactor = Math.Pow(2.0, octaves),
                Pan = pan,
                ReverbSend = send,
                Brightness = brightness,
                Modulation = modulation,
                DelayInput = delayInput
            };
        }
    }
}
