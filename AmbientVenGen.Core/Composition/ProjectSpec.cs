namespace AmbientVenGen.Core.Composition;

public enum LayerType
{
    Drone,
    Noise,
    Pad
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
    int Seed)
{
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
