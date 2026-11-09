namespace AmbientVenGen.Core.Composition;

/// <summary>Shape of a modulator: how its value moves in time.</summary>
public enum ModShape
{
    Sine,
    Triangle,

    /// <summary>Smooth random wandering (a new random target every cycle), the same every time for the same seed.</summary>
    Random
}

/// <summary>What a modulator moves.</summary>
public enum ModTarget
{
    None,
    Level,
    Cutoff,
    Pan,
    ReverbSend,
    Brightness,
    Modulation
}

/// <summary>
/// A slow oscillator that moves one parameter of a layer around its set value. The value depends only on time (and the
/// seed), so playback, seeking and export all give the same result.
/// </summary>
public sealed record ModulatorSpec(ModShape Shape, double RateHz, double Depth, ModTarget Target, bool Muted = false)
{
    public const double MinRateHz = 0.005;
    public const double MaxRateHz = 2.0;

    /// <summary>Value -1..1 at the given project time.</summary>
    public double ValueAt(double seconds, int seed)
    {
        // every modulator starts at its own point of the cycle
        double phase = unchecked((uint)seed * 2654435761u) / 4294967296.0;
        double x = Math.Max(0, RateHz) * seconds + phase;

        switch (Shape)
        {
            case ModShape.Triangle:
            {
                double f = x - Math.Floor(x);
                return 4.0 * Math.Abs(f - 0.5) - 1.0;
            }

            case ModShape.Random:
            {
                long k = (long)Math.Floor(x);
                double f = x - k;
                double s = f * f * (3.0 - 2.0 * f);
                double a = Hash(seed, k);
                double b = Hash(seed, k + 1);
                return a + (b - a) * s;
            }

            default:
                return Math.Sin(Math.PI * 2.0 * x);
        }
    }

    // -1..1, deterministic
    private static double Hash(int seed, long k)
    {
        unchecked
        {
            uint h = (uint)seed * 374761393u + (uint)k * 668265263u;
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return h / 2147483648.0 - 1.0;
        }
    }
}

/// <summary>
/// What is done to a recording before it is played: reversed, stretched (Paulstretch: longer without changing the pitch)
/// and/or transposed (without changing the length).
/// </summary>
public readonly record struct SampleTransform(bool Reverse, double Stretch, double PitchSemitones)
{
    public const double MaxStretch = 50;
    public const double MaxPitchSemitones = 24;

    public static SampleTransform None => new(false, 1.0, 0.0);

    /// <summary>Stretch or pitch is set: the sound has to be calculated (it takes a while), a reversal alone is instant.</summary>
    public bool IsHeavy => Stretch > 1.001 || Math.Abs(PitchSemitones) > 0.001;

    public bool IsIdentity => !Reverse && !IsHeavy;
}
