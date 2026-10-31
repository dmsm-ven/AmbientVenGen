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

    public Guid Id { get; } = Guid.NewGuid();

    [ObservableProperty]
    private string _name = "Layer";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TypeLabel))]
    private LayerType _type;

    [ObservableProperty]
    private double _startSeconds;

    [ObservableProperty]
    private double _durationSeconds = 120;

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
        Speed);

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
            Speed = Math.Clamp(spec.Speed <= 0 ? 1.0 : spec.Speed, MinSpeed, MaxSpeed)
        };
    }

    private static double Unit(double value) => Math.Clamp(value, 0, 1);
}
