using AmbientVenGen.Core.Composition;

namespace AmbientVenGen.Core.Audio;

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
