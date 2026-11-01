using CommunityToolkit.Mvvm.ComponentModel;
using System.IO;

namespace AmbientVenGen.Core.Composition;

/// <summary>Editable (observable) layer shown on the timeline and in the inspector.</summary>
public sealed partial class Layer : ObservableObject
{
    public const double MinPitchHz = 20;
    public const double MaxPitchHz = 1000;
    public const double MinSpeed = 0.25;
    public const double MaxSpeed = 4.0;
    public const int MaxRepeats = 99;
    public const double MaxGapSeconds = 3600;

    public Guid Id { get; } = Guid.NewGuid();

    [ObservableProperty]
    private string _name = "Layer";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TypeLabel))]
    private LayerType _type;

    [ObservableProperty]
    private double _startSeconds;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalSeconds))]
    [NotifyPropertyChangedFor(nameof(PeriodSeconds))]
    private double _durationSeconds = 120;

    /// <summary>How many times the layer (Start..Start+Duration) is played one after another; 1 = no repeat.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalSeconds))]
    private int _repeats = 1;

    /// <summary>Silence between two repeats, seconds.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalSeconds))]
    [NotifyPropertyChangedFor(nameof(PeriodSeconds))]
    private double _repeatGapSeconds;

    /// <summary>Whole length on the timeline: Duration * Repeats + Gap * (Repeats - 1).</summary>
    public double TotalSeconds => DurationSeconds * Math.Max(1, Repeats) + Math.Max(0, RepeatGapSeconds) * (Math.Max(1, Repeats) - 1);

    /// <summary>Distance between the starts of two neighbouring plays.</summary>
    public double PeriodSeconds => DurationSeconds + Math.Max(0, RepeatGapSeconds);

    partial void OnRepeatsChanged(int value)
    {
        int clamped = Math.Clamp(value, 1, MaxRepeats);
        if (clamped != value)
            Repeats = clamped;
    }

    partial void OnRepeatGapSecondsChanged(double value)
    {
        double clamped = Math.Clamp(value, 0, MaxGapSeconds);
        if (Math.Abs(clamped - value) > 1e-9)
            RepeatGapSeconds = clamped;
    }

    [ObservableProperty]
    private double _fadeInSeconds = 20;

    [ObservableProperty]
    private double _fadeOutSeconds = 20;

    [ObservableProperty]
    private Easing _fadeEasing = Easing.Sine;

    [ObservableProperty]
    private double _levelFrom = 0.7;

    [ObservableProperty]
    private double _levelTo = 0.7;

    [ObservableProperty]
    private Easing _levelEasing = Easing.Sine;

    [ObservableProperty]
    private double _brightnessFrom = 0.3;

    [ObservableProperty]
    private double _brightnessTo = 0.3;

    [ObservableProperty]
    private Easing _brightnessEasing = Easing.Sine;

    [ObservableProperty]
    private double _modulationFrom = 0.3;

    [ObservableProperty]
    private double _modulationTo = 0.3;

    [ObservableProperty]
    private Easing _modulationEasing = Easing.Sine;

    [ObservableProperty]
    private double _pitchHz = 55;

    [ObservableProperty]
    private double _pan;

    [ObservableProperty]
    private bool _muted;

    [ObservableProperty]
    private int _seed = Random.Shared.Next(1, int.MaxValue);

    /// <summary>Audio file of a field-record layer (null for synthesized layers).</summary>
    /// <summary>Playback speed of a field recording: 0.5 is half speed (and an octave lower), 2 is double.</summary>
    [ObservableProperty]
    private double _speed = 1.0;

    /// <summary>Colour of the noise (Noise layers only).</summary>
    [ObservableProperty]
    private NoiseType _noiseType = NoiseType.White;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SourceName))]
    private string? _sourcePath;

    public string TypeLabel => Type == LayerType.FieldRecord ? "Field record" : Type.ToString();

    public string SourceName => string.IsNullOrEmpty(SourcePath) ? "(no file)" : Path.GetFileName(SourcePath);

    public LayerSpec ToSpec() => new(
        Id,
        Name,
        Type,
        StartSeconds,
        DurationSeconds,
        FadeInSeconds,
        FadeOutSeconds,
        FadeEasing,
        new CurveSpec(LevelFrom, LevelTo, LevelEasing),
        new CurveSpec(BrightnessFrom, BrightnessTo, BrightnessEasing),
        new CurveSpec(ModulationFrom, ModulationTo, ModulationEasing),
        PitchHz,
        Pan,
        Muted,
        Seed,
        SourcePath,
        Speed,
        NoiseType,
        Repeats,
        RepeatGapSeconds);

    /// <summary>Creates a layer from a spec, clamping values read from a file to sane ranges.</summary>
    public static Layer FromSpec(LayerSpec spec)
    {
        double duration = Math.Max(1, spec.DurationSeconds);

        return new Layer
        {
            Name = string.IsNullOrWhiteSpace(spec.Name) ? "Layer" : spec.Name,
            Type = spec.Type,
            StartSeconds = Math.Max(0, spec.StartSeconds),
            DurationSeconds = duration,
            FadeInSeconds = Math.Clamp(spec.FadeInSeconds, 0, duration),
            FadeOutSeconds = Math.Clamp(spec.FadeOutSeconds, 0, duration),
            FadeEasing = spec.FadeEasing,
            LevelFrom = Unit(spec.Level.From),
            LevelTo = Unit(spec.Level.To),
            LevelEasing = spec.Level.Easing,
            BrightnessFrom = Unit(spec.Brightness.From),
            BrightnessTo = Unit(spec.Brightness.To),
            BrightnessEasing = spec.Brightness.Easing,
            ModulationFrom = Unit(spec.Modulation.From),
            ModulationTo = Unit(spec.Modulation.To),
            ModulationEasing = spec.Modulation.Easing,
            PitchHz = Math.Clamp(spec.PitchHz, MinPitchHz, MaxPitchHz),
            Pan = Math.Clamp(spec.Pan, -1, 1),
            Muted = spec.Muted,
            Seed = spec.Seed,
            SourcePath = spec.SourcePath,
            Speed = Math.Clamp(spec.Speed <= 0 ? 1.0 : spec.Speed, MinSpeed, MaxSpeed),
            NoiseType = Enum.IsDefined(spec.NoiseType) ? spec.NoiseType : NoiseType.White,
            Repeats = Math.Clamp(spec.Repeats, 1, MaxRepeats),
            RepeatGapSeconds = Math.Clamp(spec.RepeatGapSeconds, 0, MaxGapSeconds)
        };
    }

    private static double Unit(double value) => Math.Clamp(value, 0, 1);
}
