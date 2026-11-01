using AmbientVenGen.Core.Composition;

namespace AmbientVenGen.Core.Audio;

/// <summary>Parameters of one render block, already evaluated from the layer curves.</summary>
internal readonly struct BlockParams
{
    public readonly float Brightness;
    public readonly float Modulation;
    public readonly float PitchHz;

    /// <summary>Time since the layer started, at the start of the block (recordings play from it).</summary>
    public readonly double LocalSeconds;

    /// <summary>Playback speed of a recording (1 = original).</summary>
    public readonly float Speed;

    public BlockParams(float brightness, float modulation, float pitchHz, double localSeconds = 0, float speed = 1f)
    {
        LocalSeconds = localSeconds;
        Speed = speed;
        Brightness = brightness;
        Modulation = modulation;
        PitchHz = pitchHz;
    }
}

/// <summary>
/// A sound source. Renders raw stereo audio (about +-1); the mixer applies level, fades and pan.
/// Each generator keeps its own state, so it can be rendered block by block.
/// </summary>
internal abstract class LayerGenerator
{
    protected const int SampleRate = 44100;
    protected const double TwoPi = Math.PI * 2.0;

    /// <summary>Highest frequency a partial may have (a little below Nyquist, to stay clean).</summary>
    protected const double MaxPartialHz = SampleRate * 0.45;

    public abstract void Render(in BlockParams p, Span<float> left, Span<float> right);

    public static LayerGenerator Create(LayerType type, int seed, string? sourcePath = null, NoiseType noiseType = NoiseType.White) => type switch
    {
        LayerType.FieldRecord => new FieldRecordGenerator(seed, sourcePath),
        LayerType.Noise => new NoiseGenerator(seed, noiseType),
        LayerType.Pad => new PadGenerator(seed),
        _ => new DroneGenerator(seed)
    };

    protected static void Wrap(ref double phase)
    {
        if (phase >= TwoPi)
            phase -= TwoPi;
    }
}

/// <summary>Low detuned sines with a fifth and an octave, warmed up by soft saturation.</summary>
internal sealed class DroneGenerator : LayerGenerator
{
    private const int Partials = 4;
    private static readonly double[] Ratios = [1.0, 1.0045, 1.5, 2.0];

    private readonly double[] _phaseL = new double[Partials];
    private readonly double[] _phaseR = new double[Partials];
    private readonly double _lfoRate;
    private double _lfoPhase;

    public DroneGenerator(int seed)
    {
        var random = new Random(seed);

        for (int k = 0; k < Partials; k++)
        {
            _phaseL[k] = random.NextDouble() * TwoPi;
            _phaseR[k] = random.NextDouble() * TwoPi;
        }

        _lfoPhase = random.NextDouble() * TwoPi;
        _lfoRate = 0.03 + random.NextDouble() * 0.04;
    }

    public override void Render(in BlockParams p, Span<float> left, Span<float> right)
    {
        double pitch = Math.Max(10.0, p.PitchHz);
        double modulation = p.Modulation;
        double brightness = p.Brightness;

        double drive = 1.0 + brightness * 2.0;
        double driveNorm = 1.0 / Math.Tanh(drive);
        double spread = 0.0006 + modulation * 0.0016;

        Span<double> weights = stackalloc double[Partials];
        weights[0] = 0.50;
        weights[1] = 0.30;
        weights[2] = 0.14 + brightness * 0.06;
        weights[3] = 0.02 + brightness * 0.12;

        for (int n = 0; n < left.Length; n++)
        {
            _lfoPhase += TwoPi * _lfoRate / SampleRate;
            Wrap(ref _lfoPhase);
            double lfo = Math.Sin(_lfoPhase);

            double drift = 1.0 + lfo * modulation * 0.0012;
            double sumL = 0;
            double sumR = 0;

            for (int k = 0; k < Partials; k++)
            {
                double freq = pitch * Ratios[k] * drift;

                // Partials above the Nyquist limit would fold back as false tones: leave them out.
                if (freq >= MaxPartialHz)
                    continue;

                _phaseL[k] += TwoPi * freq * (1.0 + spread) / SampleRate;
                _phaseR[k] += TwoPi * freq * (1.0 - spread) / SampleRate;
                Wrap(ref _phaseL[k]);
                Wrap(ref _phaseR[k]);

                sumL += Math.Sin(_phaseL[k]) * weights[k];
                sumR += Math.Sin(_phaseR[k]) * weights[k];
            }

            double amplitude = 1.0 + lfo * modulation * 0.16;

            left[n] = (float)(Math.Tanh(sumL * drive) * driveNorm * amplitude);
            right[n] = (float)(Math.Tanh(sumR * drive) * driveNorm * amplitude);
        }
    }
}

/// <summary>Two-pole low-passed noise with a moving cutoff and a slow breathing level.</summary>
internal sealed class NoiseGenerator : LayerGenerator
{
    private uint _stateL;
    private uint _stateR;
    private float _l1L, _l2L, _l1R, _l2R;
    private float _powerL = 4e-4f;
    private float _powerR = 4e-4f;
    private readonly double _lfoRate;
    private double _lfoPhase;

    private readonly NoiseColor _colorL;
    private readonly NoiseColor _colorR;

    public NoiseGenerator(int seed, NoiseType type = NoiseType.White)
    {
        _colorL = new NoiseColor(type);
        _colorR = new NoiseColor(type);

        var random = new Random(seed);

        _stateL = (uint)random.Next(1, int.MaxValue) | 1u;
        _stateR = (uint)random.Next(1, int.MaxValue) | 1u;
        _lfoPhase = random.NextDouble() * TwoPi;
        _lfoRate = 0.02 + random.NextDouble() * 0.04;
    }

    public override void Render(in BlockParams p, Span<float> left, Span<float> right)
    {
        // Cutoff moves exponentially from ~70 Hz (dark) to ~7 kHz (bright).
        double cutoff = 70.0 * Math.Pow(7000.0 / 70.0, Math.Clamp(p.Brightness, 0f, 1f));
        float alpha = (float)(1.0 - Math.Exp(-TwoPi * cutoff / SampleRate));
        float modulation = p.Modulation;

        for (int n = 0; n < left.Length; n++)
        {
            _lfoPhase += TwoPi * _lfoRate / SampleRate;
            Wrap(ref _lfoPhase);
            float breathing = 1f + (float)Math.Sin(_lfoPhase) * modulation * 0.35f;

            left[n] = Process(_colorL.Next(NextWhite(ref _stateL)), alpha, ref _l1L, ref _l2L, ref _powerL) * breathing;
            right[n] = Process(_colorR.Next(NextWhite(ref _stateR)), alpha, ref _l1R, ref _l2R, ref _powerR) * breathing;
        }
    }

    // Filters the noise, then evens out its loudness so the cutoff does not change the volume.
    private static float Process(float white, float alpha, ref float l1, ref float l2, ref float power)
    {
        l1 += alpha * (white - l1);
        l2 += alpha * (l1 - l2);

        float square = l2 * l2;
        power += (square - power) * (square > power ? 1e-3f : 2e-5f);

        return l2 / MathF.Sqrt(power + 1e-9f) * 0.25f;
    }

    private static float NextWhite(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return (state & 0xFFFFFF) / 8388607.5f - 1f;
    }
}

/// <summary>
/// Turns white noise into a coloured one. Levels differ between colours; the generator evens them out afterwards,
/// so only the spectral shape matters here.
/// </summary>
internal sealed class NoiseColor
{
    private readonly NoiseType _type;
    private float _b0, _b1, _b2;      // pink filter state
    private float _brown;             // integrator
    private float _previous;          // previous input (differentiators)
    private float _low, _high;        // grey: two one-pole filters

    public NoiseColor(NoiseType type) => _type = type;

    public float Next(float white)
    {
        switch (_type)
        {
            case NoiseType.Pink:
                return Pink(white);

            case NoiseType.Brown:
                // Leaky integrator: -6 dB per octave.
                _brown = (_brown + 0.02f * white) / 1.02f;
                return _brown * 3.5f;

            case NoiseType.Blue:
            {
                // Differentiated pink: +3 dB per octave.
                float pink = Pink(white);
                float blue = pink - _previous;
                _previous = pink;
                return blue;
            }

            case NoiseType.Violet:
            {
                // Differentiated white: +6 dB per octave.
                float violet = white - _previous;
                _previous = white;
                return violet * 0.5f;
            }

            case NoiseType.Grey:
                // White with the lows and highs lifted and the ear-sensitive middle pulled down
                // (roughly the inverse of the equal-loudness contour), so it sounds evenly loud.
                _low += 0.035f * (white - _low);
                _high += 0.55f * (white - _high);
                return 0.35f * white + 2.2f * _low + 0.9f * (white - _high);

            default:
                return white;
        }
    }

    // Three-pole approximation of -3 dB per octave (Paul Kellet's economy filter).
    private float Pink(float white)
    {
        _b0 = 0.99765f * _b0 + white * 0.0990460f;
        _b1 = 0.96300f * _b1 + white * 0.2965164f;
        _b2 = 0.57000f * _b2 + white * 1.0526913f;
        return (_b0 + _b1 + _b2 + white * 0.1848f) * 0.25f;
    }
}

/// <summary>Soft additive pad: harmonics that slowly swell and fade, with gentle vibrato.</summary>
internal sealed class PadGenerator : LayerGenerator
{
    private const int Partials = 6;
    private const int ControlBlock = 64;
    private static readonly double[] Ratios = [1.0, 2.0, 3.0, 4.0, 5.0, 7.0];

    private readonly double[] _phaseL = new double[Partials];
    private readonly double[] _phaseR = new double[Partials];
    private readonly double[] _breathPhase = new double[Partials];
    private readonly double[] _breathRate = new double[Partials];
    private readonly double[] _amp = new double[Partials];
    private double _vibratoPhase;

    public PadGenerator(int seed)
    {
        var random = new Random(seed);

        for (int k = 0; k < Partials; k++)
        {
            _phaseL[k] = random.NextDouble() * TwoPi;
            _phaseR[k] = random.NextDouble() * TwoPi;
            _breathPhase[k] = random.NextDouble() * TwoPi;
            _breathRate[k] = 0.03 + random.NextDouble() * 0.08;
        }

        _vibratoPhase = random.NextDouble() * TwoPi;
    }

    public override void Render(in BlockParams p, Span<float> left, Span<float> right)
    {
        double pitch = Math.Max(10.0, p.PitchHz);
        double brightness = p.Brightness;
        double modulation = p.Modulation;

        double rolloff = 1.9 - brightness * 1.3;
        double spread = 0.0012 + modulation * 0.003;
        double vibratoDepth = modulation * 0.0015;

        double weightSum = 0;
        for (int k = 0; k < Partials; k++)
            weightSum += 1.0 / Math.Pow(k + 1, rolloff);

        for (int start = 0; start < left.Length; start += ControlBlock)
        {
            int count = Math.Min(ControlBlock, left.Length - start);

            // Control rate: slow "breathing" of every partial.
            for (int k = 0; k < Partials; k++)
            {
                _breathPhase[k] += TwoPi * _breathRate[k] * count / SampleRate;
                Wrap(ref _breathPhase[k]);

                double breath = 0.55 + 0.45 * Math.Sin(_breathPhase[k]);
                _amp[k] = breath / Math.Pow(k + 1, rolloff) / weightSum;
            }

            for (int n = start; n < start + count; n++)
            {
                _vibratoPhase += TwoPi * 4.8 / SampleRate;
                Wrap(ref _vibratoPhase);
                double vibrato = 1.0 + Math.Sin(_vibratoPhase) * vibratoDepth;

                double sumL = 0;
                double sumR = 0;

                for (int k = 0; k < Partials; k++)
                {
                    double freq = pitch * Ratios[k] * vibrato;

                    if (freq >= MaxPartialHz)
                        continue;

                    _phaseL[k] += TwoPi * freq * (1.0 + spread) / SampleRate;
                    _phaseR[k] += TwoPi * freq * (1.0 - spread) / SampleRate;
                    Wrap(ref _phaseL[k]);
                    Wrap(ref _phaseR[k]);

                    sumL += Math.Sin(_phaseL[k]) * _amp[k];
                    sumR += Math.Sin(_phaseR[k]) * _amp[k];
                }

                left[n] = (float)sumL;
                right[n] = (float)sumR;
            }
        }
    }
}

/// <summary>
/// Plays a decoded recording in a loop (the end is cross-faded into the beginning) at an adjustable speed
/// (like a tape: slower is also lower). The read position is kept as a running value, so changing the speed
/// while playing is smooth; after a seek it is recomputed from the layer time.
/// Brightness is a low-pass filter, modulation a slow breathing of the level.
/// </summary>
internal sealed class FieldRecordGenerator : LayerGenerator
{
    private const double MaxCrossfadeSeconds = 2.0;

    private readonly string? _path;
    private readonly double _breathRate;
    private double _breathPhase;
    private float _lowL;
    private float _lowR;
    private DecodedSample? _sample;
    private double _position;
    private double _expectedLocalSeconds = double.NaN;

    public FieldRecordGenerator(int seed, string? path)
    {
        var random = new Random(seed);

        _path = path;
        _breathPhase = random.NextDouble() * TwoPi;
        _breathRate = 0.04 + random.NextDouble() * 0.06;
    }

    public override void Render(in BlockParams p, Span<float> left, Span<float> right)
    {
        if (_sample is null && !string.IsNullOrEmpty(_path))
        {
            // The file is decoded on a background thread; until it is ready the layer is silent.
            _sample = SampleLibrary.TryGet(_path);

            if (_sample is null)
                SampleLibrary.Request(_path);
        }

        DecodedSample? sample = _sample;

        if (sample is null || sample.Frames < 2)
        {
            left.Clear();
            right.Clear();
            return;
        }

        int frames = sample.Frames;
        int crossfade = (int)Math.Min(MaxCrossfadeSeconds * SampleRate, frames / 4);
        int period = frames - crossfade;
        double speed = Math.Clamp(p.Speed, 0.05f, 8f);

        // Continue from where the last block ended; jump only after a seek (time did not follow on).
        if (double.IsNaN(_expectedLocalSeconds) || Math.Abs(p.LocalSeconds - _expectedLocalSeconds) > 0.02)
            _position = Math.Max(0.0, p.LocalSeconds) * SampleRate * speed % period;

        _expectedLocalSeconds = p.LocalSeconds + left.Length / (double)SampleRate;

        // Brightness 0..1 -> low-pass cutoff about 300 Hz .. 19 kHz.
        double cutoff = Math.Min(300.0 * Math.Pow(2.0, Math.Clamp(p.Brightness, 0f, 1f) * 6.0), SampleRate * 0.45);
        float alpha = (float)(1.0 - Math.Exp(-TwoPi * cutoff / SampleRate));
        double depth = Math.Clamp(p.Modulation, 0f, 1f) * 0.35;

        for (int n = 0; n < left.Length; n++)
        {
            int index = (int)_position;
            float fraction = (float)(_position - index);

            float l = Interpolate(sample.Left, index, fraction, frames);
            float r = Interpolate(sample.Right, index, fraction, frames);

            if (index < crossfade)
            {
                // Beginning blended with the tail that follows the loop point (equal power).
                double angle = _position / crossfade * Math.PI / 2.0;
                float fadeIn = (float)Math.Sin(angle);
                float fadeOut = (float)Math.Cos(angle);

                l = l * fadeIn + Interpolate(sample.Left, period + index, fraction, frames) * fadeOut;
                r = r * fadeIn + Interpolate(sample.Right, period + index, fraction, frames) * fadeOut;
            }

            _position += speed;
            if (_position >= period)
                _position -= period;

            _lowL += alpha * (l - _lowL);
            _lowR += alpha * (r - _lowR);

            _breathPhase += TwoPi * _breathRate / SampleRate;
            Wrap(ref _breathPhase);
            float breath = (float)(1.0 - depth * 0.5 * (1.0 + Math.Sin(_breathPhase)));

            left[n] = _lowL * breath;
            right[n] = _lowR * breath;
        }
    }

    private static float Interpolate(float[] data, int index, float fraction, int frames)
    {
        int next = Math.Min(index + 1, frames - 1);
        return data[index] + (data[next] - data[index]) * fraction;
    }
}
