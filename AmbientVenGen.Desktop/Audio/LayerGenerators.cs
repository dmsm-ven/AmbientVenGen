using AmbientVenGen.Desktop.Composition;

namespace AmbientVenGen.Desktop.Audio;

/// <summary>Parameters of one render block, already evaluated from the layer curves.</summary>
internal readonly struct BlockParams
{
    public readonly float Brightness;
    public readonly float Modulation;
    public readonly float PitchHz;

    public BlockParams(float brightness, float modulation, float pitchHz)
    {
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

    public abstract void Render(in BlockParams p, Span<float> left, Span<float> right);

    public static LayerGenerator Create(LayerType type, int seed) => type switch
    {
        LayerType.Noise => new NoiseGenerator(seed),
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

    public NoiseGenerator(int seed)
    {
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

            left[n] = Process(NextWhite(ref _stateL), alpha, ref _l1L, ref _l2L, ref _powerL) * breathing;
            right[n] = Process(NextWhite(ref _stateR), alpha, ref _l1R, ref _l2R, ref _powerR) * breathing;
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
