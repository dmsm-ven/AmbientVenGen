using AmbientVenGen.Core.Composition;

namespace AmbientVenGen.Core.Audio;

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
