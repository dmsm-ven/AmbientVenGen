namespace AmbientVenGen.Core.Composition;

public enum LayerType
{
    Drone,
    Noise,
    Pad,

    /// <summary>A recorded sound (wav, mp3, flac...) looped under the other layers.</summary>
    FieldRecord
}

/// <summary>Kind of a Noise layer: a colour (how the energy is spread over the frequencies) or a texture.</summary>
public enum NoiseType
{
    White,
    Pink,
    Brown,
    Blue,
    Violet,
    Grey,

    // Textures: noise with events and slow movement, made by TextureNoiseGenerator.
    Crackle,
    Vinyl,
    RadioStatic,
    Wind,
    Rain,
    Ocean,
    ElectricalHum,
    TapeHiss,
    SpaceRumble,
    Geiger,
    IceSinging,
    HighVoltage,
    EnergyShield,
    CrtWhine,
    PlasmaArc,
    Gong,
    Heartbeat,
    CardiacMonitor
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
    double RepeatGapSeconds = 0,
    double FilterCutoffHz = 20000,
    double FilterResonance = 0,
    double DelaySeconds = 0,
    double DelayFeedback = 0.4,
    double ReverbSend = 1.0,
    double PitchDrift = 0,
    double PanDrift = 0,
    Easing? FadeOutEasing = null,
    ModulatorSpec? Mod1 = null,
    ModulatorSpec? Mod2 = null,
    bool Reverse = false,
    double Stretch = 1.0,
    double PitchShift = 0.0,
    double LoopStart = 0.0,
    double LoopDuration = 0.0,
    double LoopCrossfade = SampleTransform.DefaultCrossfade,
    IReadOnlyList<ModulatorSpec>? Modulators = null)
{
    /// <summary>What is done to the recording of a Field record layer (nothing for the other types).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public SampleTransform Transform => Type == LayerType.FieldRecord
        ? new SampleTransform(Reverse, Math.Clamp(Stretch < 1 ? 1 : Stretch, 1, SampleTransform.MaxStretch),
            Math.Clamp(PitchShift, -SampleTransform.MaxPitchSemitones, SampleTransform.MaxPitchSemitones),
            Math.Max(0, LoopStart), Math.Max(0, LoopDuration), Math.Clamp(LoopCrossfade, 0, SampleTransform.MaxCrossfade))
        : SampleTransform.None;

    /// <summary>Curve of the fade-out; projects saved before it was separate use the fade-in curve for both.</summary>
    public Easing FadeOutCurve => FadeOutEasing ?? FadeEasing;

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
            gain *= EasingFunctions.Apply(FadeOutCurve, (duration - localSeconds) / fadeOut);

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
    LayerSpec[] Layers,
    double Shimmer = 0.0,
    int ShimmerSemitones = 12)
{
    public const int CurrentVersion = 1;

    public static ProjectSpec Empty { get; } = new(CurrentVersion, 600, 12345, 0.35, []);
}
