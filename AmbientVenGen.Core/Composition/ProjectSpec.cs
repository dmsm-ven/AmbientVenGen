namespace AmbientVenGen.Core.Composition;

public enum LayerType
{
    Drone,
    Noise,
    Pad,

    /// <summary>A recorded sound (wav, mp3, flac...) looped under the other layers.</summary>
    FieldRecord
}

/// <summary>Colour of a Noise layer: how the energy is spread over the frequencies.</summary>
public enum NoiseType
{
    White,
    Pink,
    Brown,
    Blue,
    Violet,
    Grey
}

/// <summary>A parameter that moves from one value to another over the length of a layer.</summary>
public sealed record CurveSpec(double From, double To, Easing Easing)
{
    public double At(double progress) => From + (To - From) * EasingFunctions.Apply(Easing, progress);
}

/// <summary>
/// Immutable description of one layer. Used by the audio engine (thread-safe snapshot)
/// and as the project file format.
/// </summary>
public sealed record LayerSpec(
    Guid Id,
    string Name,
    LayerType Type,
    double StartSeconds,
    double DurationSeconds,
    double FadeInSeconds,
    double FadeOutSeconds,
    Easing FadeEasing,
    CurveSpec Level,
    CurveSpec Brightness,
    CurveSpec Modulation,
    double PitchHz,
    double Pan,
    bool Muted,
    int Seed,
    string? SourcePath = null,
    double Speed = 1.0,
    NoiseType NoiseType = NoiseType.White,
    int Repeats = 1,
    double RepeatGapSeconds = 0)
{
    /// <summary>Number of plays (never less than 1).</summary>
    public int PlayCount => Math.Max(1, Repeats);

    /// <summary>Whole length on the timeline: Duration * Repeats + Gap * (Repeats - 1).</summary>
    public double TotalSeconds => DurationSeconds * PlayCount + Math.Max(0, RepeatGapSeconds) * (PlayCount - 1);

    /// <summary>
    /// Maps time since the layer start to the position inside one play. Returns false inside a gap
    /// (or outside the layer); <paramref name="iteration"/> is the zero-based number of the play.
    /// </summary>
    public bool TryGetPlayTime(double localSeconds, out double withinSeconds, out int iteration)
    {
        withinSeconds = localSeconds;
        iteration = 0;

        if (localSeconds < 0 || localSeconds > TotalSeconds)
            return false;

        double period = DurationSeconds + Math.Max(0, RepeatGapSeconds);
        if (period <= 0)
            return false;

        iteration = (int)Math.Min(PlayCount - 1, Math.Floor(localSeconds / period));
        withinSeconds = localSeconds - iteration * period;
        return withinSeconds <= DurationSeconds;
    }

    public double Progress(double localSeconds) =>
        DurationSeconds > 0 ? Math.Clamp(localSeconds / DurationSeconds, 0.0, 1.0) : 0.0;

    /// <summary>Gain of the fade-in / fade-out transitions at the given time inside the layer.</summary>
    public double FadeGainAt(double localSeconds)
    {
        double duration = DurationSeconds;
        if (duration <= 0)
            return 0;

        double fadeIn = Math.Min(Math.Max(FadeInSeconds, 0), duration);
        double fadeOut = Math.Min(Math.Max(FadeOutSeconds, 0), duration - fadeIn);
        double gain = 1.0;

        if (fadeIn > 0 && localSeconds < fadeIn)
            gain *= EasingFunctions.Apply(FadeEasing, localSeconds / fadeIn);

        if (fadeOut > 0 && localSeconds > duration - fadeOut)
            gain *= EasingFunctions.Apply(FadeEasing, (duration - localSeconds) / fadeOut);

        return gain;
    }

    public double LevelAt(double localSeconds) => Level.At(Progress(localSeconds));
    public double BrightnessAt(double localSeconds) => Brightness.At(Progress(localSeconds));
    public double ModulationAt(double localSeconds) => Modulation.At(Progress(localSeconds));

    /// <summary>Level multiplied by the fades: what the listener actually hears.</summary>
    public double GainAt(double localSeconds) => LevelAt(localSeconds) * FadeGainAt(localSeconds);
}

public sealed record ProjectSpec(
    int Version,
    double DurationSeconds,
    int Seed,
    double ReverbMix,
    LayerSpec[] Layers)
{
    public const int CurrentVersion = 1;

    public static ProjectSpec Empty { get; } = new(CurrentVersion, 600, 12345, 0.35, []);
}
