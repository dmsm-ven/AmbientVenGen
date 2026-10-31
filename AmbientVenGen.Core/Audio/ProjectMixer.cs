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
    private readonly Dictionary<Guid, LayerRuntime> _runtimes = new();
    private readonly float[] _scratchLeft = new float[BlockFrames];
    private readonly float[] _scratchRight = new float[BlockFrames];

    private float[] _mixLeft = new float[4096];
    private float[] _mixRight = new float[4096];
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

        LayerSpec[] layers = spec.Layers;

        for (int done = 0; done < frames; done += BlockFrames)
        {
            int count = Math.Min(BlockFrames, frames - done);
            double blockTime = (position + done + count * 0.5) / SampleRate;

            for (int i = 0; i < layers.Length; i++)
                MixLayer(layers[i], spec.Seed, blockTime, count, done);
        }

        _reverb.Process(_mixLeft, _mixRight, frames, (float)spec.ReverbMix);

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
            || runtime.SourcePath != layer.SourcePath)
        {
            runtime = new LayerRuntime(
                layer.Type,
                layer.SourcePath,
                LayerGenerator.Create(layer.Type, MixSeed(projectSeed, layer.Seed), layer.SourcePath));
            _runtimes[layer.Id] = runtime;
        }

        double local = blockTime - layer.StartSeconds;
        bool active = !layer.Muted && local >= 0 && local <= layer.DurationSeconds;
        float target = active ? (float)layer.GainAt(local) : 0f;

        if (target <= 0f && runtime.Gain < 1e-5f)
        {
            runtime.Gain = 0f;
            return;
        }

        double curveTime = Math.Clamp(local, 0, layer.DurationSeconds);
        var block = new BlockParams(
            (float)layer.BrightnessAt(curveTime),
            (float)layer.ModulationAt(curveTime),
            (float)layer.PitchHz,
            Math.Max(0.0, local - count * 0.5 / SampleRate),
            (float)layer.Speed);

        var scratchLeft = _scratchLeft.AsSpan(0, count);
        var scratchRight = _scratchRight.AsSpan(0, count);
        runtime.Generator.Render(in block, scratchLeft, scratchRight);

        // Constant-power pan; the centre position keeps unity gain.
        float angle = (float)((Math.Clamp(layer.Pan, -1.0, 1.0) + 1.0) * Math.PI / 4.0);
        float panLeft = MathF.Cos(angle) * 1.4142f;
        float panRight = MathF.Sin(angle) * 1.4142f;

        float gain = runtime.Gain;

        for (int i = 0; i < count; i++)
        {
            gain += (target - gain) * GainSmoothing;
            float scaled = gain * LayerScale;

            _mixLeft[offset + i] += scratchLeft[i] * scaled * panLeft;
            _mixRight[offset + i] += scratchRight[i] * scaled * panRight;
        }

        runtime.Gain = gain;
    }

    private void EnsureCapacity(int frames)
    {
        if (_mixLeft.Length >= frames)
            return;

        _mixLeft = new float[frames];
        _mixRight = new float[frames];
    }

    // Deterministic (string/Hash randomization would break "same seed, same sound").
    private static int MixSeed(int projectSeed, int layerSeed) =>
        unchecked(projectSeed * 397 ^ layerSeed * 16777619);

    private sealed class LayerRuntime
    {
        public LayerType Type { get; }
        public string? SourcePath { get; }
        public LayerGenerator Generator { get; }
        public float Gain { get; set; }

        public LayerRuntime(LayerType type, string? sourcePath, LayerGenerator generator)
        {
            Type = type;
            SourcePath = sourcePath;
            Generator = generator;
        }
    }
}
