namespace AmbientVenGen.Core.Composition;

/// <summary>A relation of three layers: the ratios of the two other voices to the root (the root itself is x1).</summary>
public sealed record ChordPreset(string Name, double RatioA, double RatioB, string RatioText, string Description);

/// <summary>Pure pitch arithmetic of the interval panel (no UI), kept here so that it can be tested.</summary>
public static class PitchRelations
{
    public static IReadOnlyList<ChordPreset> Chords { get; } =
    [
        new("Open fifth", 1.5, 2.0, "1 : 3/2 : 2", "Root, fifth and octave: empty, wide, \"cosmic\""),
        new("Sus4", 4.0 / 3.0, 1.5, "1 : 4/3 : 3/2", "Suspended: neither major nor minor, a classic for ambient"),
        new("Major triad", 1.25, 1.5, "1 : 5/4 : 3/2", "Bright and warm"),
        new("Minor triad", 1.2, 1.5, "1 : 6/5 : 3/2", "Dark and soft"),
        new("Diminished", 1.2, 64.0 / 45.0, "1 : 6/5 : 64/45", "Tense, unstable, mysterious"),
        new("Octave spread", 0.5, 2.0, "1/2 : 1 : 2", "Sub, middle and top of one note: density without harmony (the root is the middle voice)"),
        new("Beating cluster", 0.9955, 1.0045, "1 : 0.9955 : 1.0045", "Almost unison (about 8 cents apart): slow beating, a chorus effect")
    ];

    /// <summary>A pitch outside the allowed range is folded by octaves instead of being cut. Rounded to 0.1 Hz.</summary>
    public static double Fold(double hz)
    {
        if (double.IsNaN(hz) || double.IsInfinity(hz) || hz <= 0)
            return Layer.MinPitchHz;

        while (hz > Layer.MaxPitchHz)
            hz /= 2.0;

        while (hz < Layer.MinPitchHz)
            hz *= 2.0;

        return Math.Round(hz, 1);
    }

    /// <summary>Pitches of the lower and of the higher non-root voice of a chord built on the root.</summary>
    public static (double Low, double High) ChordPitches(double rootHz, ChordPreset preset) =>
        (Fold(rootHz * Math.Min(preset.RatioA, preset.RatioB)),
         Fold(rootHz * Math.Max(preset.RatioA, preset.RatioB)));
}
