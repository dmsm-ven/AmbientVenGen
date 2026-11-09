using AmbientVenGen.Core.Audio;
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
    public const double MinCutoffHz = 20;
    public const double MaxCutoffHz = 20000;
    public const double MaxDelaySeconds = 2;
    public const double MaxFeedback = 0.92;
    public const int MaxRepeats = 99;
    public const double MaxGapSeconds = 3600;

    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>UI-only state (expanded second knobs); never saved and never heard.</summary>
    public LayerViewState View { get; private set; } = new();

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

    // The fades keep their share of the layer: 60 s with fades 10 / 12 s cut to 30 s gives fades 5 / 6 s.
    partial void OnDurationSecondsChanged(double oldValue, double newValue)
    {
        if (oldValue <= 0 || newValue <= 0 || Math.Abs(oldValue - newValue) < 1e-9)
            return;

        double ratio = newValue / oldValue;
        FadeInSeconds = Math.Min(FadeInSeconds * ratio, newValue);
        FadeOutSeconds = Math.Min(FadeOutSeconds * ratio, newValue);
    }

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

    /// <summary>Curve of the fade-in.</summary>
    [ObservableProperty]
    private Easing _fadeEasing = Easing.Sine;

    /// <summary>Curve of the fade-out (separate from the fade-in).</summary>
    [ObservableProperty]
    private Easing _fadeOutEasing = Easing.Sine;

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

    /// <summary>Low-pass filter cutoff; 20 kHz = open (filter off).</summary>
    [ObservableProperty]
    private double _filterCutoffHz = MaxCutoffHz;

    /// <summary>Filter resonance 0..1 (a peak at the cutoff).</summary>
    [ObservableProperty]
    private double _filterResonance;

    /// <summary>Echo time in seconds; 0 = no echo.</summary>
    [ObservableProperty]
    private double _delaySeconds;

    /// <summary>How much of the echo is fed back (0..0.92): more repeats.</summary>
    [ObservableProperty]
    private double _delayFeedback = 0.4;

    /// <summary>How much of the layer goes to the project reverb (1 = as before, 0 = dry).</summary>
    [ObservableProperty]
    private double _reverbSend = 1.0;

    /// <summary>Slow random wandering of the pitch (Drone, Pad) or the speed (recording), 0..1.</summary>
    [ObservableProperty]
    private double _pitchDrift;

    /// <summary>Slow random wandering of the pan position, 0..1.</summary>
    [ObservableProperty]
    private double _panDrift;

    /// <summary>Colour of the noise (Noise layers only).</summary>
    [ObservableProperty]
    private NoiseType _noiseType = NoiseType.White;

    /// <summary>Two slow modulators that move parameters of the layer around their set values.</summary>
    public Modulator Mod1 { get; } = new();

    public Modulator Mod2 { get; } = new();

    /// <summary>Field record: play the recording backwards.</summary>
    [ObservableProperty]
    private bool _reverse;

    /// <summary>Field record: stretch the recording without changing its pitch (Paulstretch), 1 = as is.</summary>
    [ObservableProperty]
    private double _stretch = 1.0;

    /// <summary>Field record: transpose the recording without changing its length, in semitones.</summary>
    [ObservableProperty]
    private double _pitchShift;

    /// <summary>What is done to the recording before it is played.</summary>
    public SampleTransform Transform => Type == LayerType.FieldRecord
        ? new SampleTransform(Reverse, Math.Clamp(Stretch, 1, SampleTransform.MaxStretch),
            Math.Clamp(PitchShift, -SampleTransform.MaxPitchSemitones, SampleTransform.MaxPitchSemitones))
        : SampleTransform.None;

    public Layer()
    {
        // A change of a modulator is a change of the layer (the project is told, the engine gets the new spec).
        Mod1.PropertyChanged += (_, _) => OnPropertyChanged(nameof(Mod1));
        Mod2.PropertyChanged += (_, _) => OnPropertyChanged(nameof(Mod2));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SourceName))]
    private string? _sourcePath;

    public string TypeLabel => Type == LayerType.FieldRecord ? "Field record" : Type.ToString();

    /// <summary>Length of the original audio file (0 = unknown / not a recording). UI only, read in the background.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SourceTimeText))]
    private double _sourceSeconds;

    public string SourceTimeText => SourceSeconds > 0 ? FormatClock(SourceSeconds) : string.Empty;

    /// <summary>The whole length with repeats and gaps, as hh:mm:ss.</summary>
    public string TotalTimeText => FormatClock(TotalSeconds);

    private static string FormatClock(double seconds)
    {
        long total = (long)Math.Max(0, Math.Round(seconds));
        return $"{total / 3600:00}:{total / 60 % 60:00}:{total % 60:00}";
    }

    partial void OnSourcePathChanged(string? value)
    {
        SourceSeconds = 0;

        if (Type != LayerType.FieldRecord || string.IsNullOrEmpty(value))
            return;

        if (SourceLengthCache.TryGet(value, out double known))
        {
            SourceSeconds = known;
            return;
        }

        _ = ReadSourceLengthAsync(value);
    }

    private async Task ReadSourceLengthAsync(string path)
    {
        double seconds = await SourceLengthCache.ReadAsync(path).ConfigureAwait(false);

        // The file may have been changed while it was being read.
        if (SourcePath == path)
            SourceSeconds = seconds;
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName == nameof(TotalSeconds))
            OnPropertyChanged(nameof(TotalTimeText));
    }

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
        RepeatGapSeconds,
        FilterCutoffHz,
        FilterResonance,
        DelaySeconds,
        DelayFeedback,
        ReverbSend,
        PitchDrift,
        PanDrift,
        FadeOutEasing,
        Mod1.ToSpec(),
        Mod2.ToSpec(),
        Reverse,
        Stretch,
        PitchShift);

    /// <summary>Creates a layer from a spec, clamping values read from a file to sane ranges.</summary>
    public static Layer FromSpec(LayerSpec spec)
    {
        double duration = Math.Max(1, spec.DurationSeconds);

        var layer = new Layer
        {
            Name = string.IsNullOrWhiteSpace(spec.Name) ? "Layer" : spec.Name,
            Type = spec.Type,
            StartSeconds = Math.Max(0, spec.StartSeconds),
            DurationSeconds = duration,
            FadeInSeconds = Math.Clamp(spec.FadeInSeconds, 0, duration),
            FadeOutSeconds = Math.Clamp(spec.FadeOutSeconds, 0, duration),
            FadeEasing = spec.FadeEasing,
            FadeOutEasing = spec.FadeOutCurve,
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
            RepeatGapSeconds = Math.Clamp(spec.RepeatGapSeconds, 0, MaxGapSeconds),
            FilterCutoffHz = Math.Clamp(spec.FilterCutoffHz <= 0 ? MaxCutoffHz : spec.FilterCutoffHz, MinCutoffHz, MaxCutoffHz),
            FilterResonance = Unit(spec.FilterResonance),
            DelaySeconds = Math.Clamp(spec.DelaySeconds, 0, MaxDelaySeconds),
            DelayFeedback = Math.Clamp(spec.DelayFeedback, 0, MaxFeedback),
            ReverbSend = Unit(spec.ReverbSend),
            PitchDrift = Unit(spec.PitchDrift),
            PanDrift = Unit(spec.PanDrift),
            Reverse = spec.Reverse,
            Stretch = Math.Clamp(spec.Stretch < 1 ? 1 : spec.Stretch, 1, SampleTransform.MaxStretch),
            PitchShift = Math.Clamp(spec.PitchShift, -SampleTransform.MaxPitchSemitones, SampleTransform.MaxPitchSemitones)
        };
        layer.Mod1.Apply(spec.Mod1);
        layer.Mod2.Apply(spec.Mod2);
        layer.View = LayerViewState.For(layer);
        return layer;
    }

    private static double Unit(double value) => Math.Clamp(value, 0, 1);
}
