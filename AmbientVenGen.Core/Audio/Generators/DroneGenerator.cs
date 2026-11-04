using AmbientVenGen.Core.Composition;

namespace AmbientVenGen.Core.Audio;

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
