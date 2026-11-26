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
    HighVoltage,
    EnergyShield,
    CrtWhine,
    PlasmaArc,
    Gong,
    Heartbeat,
    CardiacMonitor,
    Fog,
    InsectSwarm
}

/// <summary>A property of a layer that can get its own value for a repeat (see <see cref="RepeatOverrideSpec"/>).</summary>
public enum RepeatParam
{
    Level,
    Brightness,
    Modulation,
    Pan,
    Cutoff,
    Resonance,
    ReverbSend,
    DelayTime,
    DelayFeedback,

    /// <summary>The pitch of a Drone or Pad, Hz.</summary>
    Pitch,

    // Field record only
    Speed,
    PitchShift,
    Stretch,

    /// <summary>How much of the layer goes to the shimmer reverb.</summary>
    ShimmerSend
}

/// <summary>A value of one property for one play of a layer (zero-based): the plays that have none use the layer's own value.</summary>
public sealed record RepeatOverrideSpec(int Repeat, RepeatParam Param, double Value);

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
    IReadOnlyList<ModulatorSpec>? Modulators = null,
    IReadOnlyList<RepeatOverrideSpec>? RepeatOverrides = null,
    double? ShimmerSend = null,
    IReadOnlyList<EventSpec>? Events = null)
{
    /// <summary>
    /// How much of the layer goes to the shimmer reverb. Projects saved before the shimmer had its own send fed it from the
    /// reverb send of every layer, so for them the reverb send is the shimmer send.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public double ShimmerSendValue => ShimmerSend ?? ReverbSend;

    /// <summary>The own value of a property for the given play, or null when that play uses the value of the layer.</summary>
    public double? OverrideOf(int play, RepeatParam param)
    {
        if (RepeatOverrides is not { Count: > 0 } list)
            return null;

        for (int i = 0; i < list.Count; i++)
        {
            RepeatOverrideSpec item = list[i];
            if (item.Repeat == play && item.Param == param)
                return item.Value;
        }

        return null;
    }

    /// <summary>What is done to the recording in the given play (the repeats may have their own speed-independent settings).</summary>
    public SampleTransform TransformFor(int play)
    {
        SampleTransform t = Transform;

        if (Type != LayerType.FieldRecord || RepeatOverrides is not { Count: > 0 })
            return t;

        double stretch = OverrideOf(play, RepeatParam.Stretch) ?? Stretch;
        double shift = OverrideOf(play, RepeatParam.PitchShift) ?? PitchShift;

        return t with
        {
            Stretch = Math.Clamp(stretch < 1 ? 1 : stretch, 1, SampleTransform.MaxStretch),
            PitchSemitones = Math.Clamp(shift, -SampleTransform.MaxPitchSemitones, SampleTransform.MaxPitchSemitones)
        };
    }

    /// <summary>Every different processing of the recording over all plays (to be prepared before playing or exporting).</summary>
    public IEnumerable<SampleTransform> AllTransforms()
    {
        var seen = new HashSet<SampleTransform>();

        for (int play = 0; play < PlayCount; play++)
        {
            if (seen.Add(TransformFor(play)))
                yield return TransformFor(play);
        }
    }

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

    /// <summary>The same with a level of its own for this play (a fixed level instead of the curve).</summary>
    public double GainAt(double localSeconds, double? levelOverride) =>
        (levelOverride ?? LevelAt(localSeconds)) * FadeGainAt(localSeconds);
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
