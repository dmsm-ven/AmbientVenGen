using CommunityToolkit.Mvvm.ComponentModel;

namespace AmbientVenGen.Core.Composition;

/// <summary>What happens at a moment of a layer (a one-shot effect, unlike an oscillator, which never stops).</summary>
public enum EventType
{
    /// <summary>The layer is thrown into an echo for a moment, then (optionally) goes silent and only the echo rings out.</summary>
    EchoThrow,

    /// <summary>The last fraction of a second is grabbed and repeated several times (a glitch repeat), the layer itself is silent meanwhile.</summary>
    Stutter,

    /// <summary>A one-shot low-pass sweep: the layer gets darker over Length, then opens again over Tail.</summary>
    FilterSweep,

    /// <summary>The last moment of the layer is looped into a held, endless pad for Length seconds, then released.</summary>
    Freeze,

    /// <summary>The layer slides down in pitch (Amount x 24 semitones) over Length, then climbs back over Tail.</summary>
    PitchDive,

    /// <summary>The level moves: Length to get there, Time held, Tail to come back. Amount below 0 = duck, above 0 = swell.</summary>
    SwellDuck,

    /// <summary>The sound is crushed (fewer bits, lower sample rate) for Length seconds, then goes back over Tail.</summary>
    BitcrushBurst
}

/// <summary>
/// One event of a layer: a moment (seconds from the start of the layer, in every play of it) and the settings of the effect.
/// Length: EchoThrow = how long the layer feeds the echo; Stutter = the length of the grabbed slice.
/// Time: EchoThrow = delay time. Amount: EchoThrow = feedback; Stutter = how much each repeat is quieter.
/// Fade: EchoThrow = how long the layer takes to come back after the silence (a smooth return instead of a jump).
/// Count: Stutter = number of repeats. Tail: EchoThrow = how long the layer stays silent after the throw (0 = it plays on).
/// </summary>
public sealed record EventSpec(
    EventType Type,
    double StartSeconds,
    double Length,
    double Time,
    double Amount,
    int Count,
    double Tail,
    bool Muted = false,
    double Fade = 1.0)
{
    public const double MaxEchoHold = 4.0;
    public const double MaxSweep = 20.0;
    public const double MaxFade = 10.0;
    public const double MinSlice = 0.02;
    public const double MaxSlice = 1.0;
    public const double MinDelay = 0.05;
    public const double MaxDelay = 1.5;
    public const double MaxFeedback = 0.92;
    public const double MaxTail = 20.0;
    public const int MinRepeats = 2;
    public const int MaxRepeats = 32;
}

/// <summary>Editable event of a layer (see <see cref="EventSpec"/>).</summary>
public sealed partial class LayerEvent : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyPropertyChangedFor(nameof(IsEcho))]
    [NotifyPropertyChangedFor(nameof(IsStutter))]
    [NotifyPropertyChangedFor(nameof(IsSweep))]
    [NotifyPropertyChangedFor(nameof(IsFreeze))]
    [NotifyPropertyChangedFor(nameof(IsDive))]
    [NotifyPropertyChangedFor(nameof(IsSwellDuck))]
    [NotifyPropertyChangedFor(nameof(IsCrush))]
    private EventType _type = EventType.EchoThrow;

    /// <summary>Seconds from the start of the layer (of every play of it).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private double _startSeconds;

    [ObservableProperty]
    private double _length = 0.5;

    [ObservableProperty]
    private double _time = 0.35;

    [ObservableProperty]
    private double _amount = 0.7;

    [ObservableProperty]
    private int _count = 8;

    [ObservableProperty]
    private double _tail = 4.0;

    /// <summary>Echo throw: seconds the layer takes to come back after the silence.</summary>
    [ObservableProperty]
    private double _fade = 1.0;

    [ObservableProperty]
    private bool _muted;

    /// <summary>Only for the screen (the card shows its settings or only its header); not saved.</summary>
    [ObservableProperty]
    private bool _isExpanded = true;

    /// <summary>Only for the screen (the event picked in the Events tab or on the timeline); not saved.</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Raised when the picked event changes (the timeline redraws the frame of its marker).</summary>
    public static event Action? SelectionChanged;

    partial void OnIsSelectedChanged(bool value) => SelectionChanged?.Invoke();

    public bool IsEcho => Type == EventType.EchoThrow;

    public bool IsStutter => Type == EventType.Stutter;

    public bool IsSweep => Type == EventType.FilterSweep;

    public bool IsFreeze => Type == EventType.Freeze;

    public bool IsDive => Type == EventType.PitchDive;

    public bool IsSwellDuck => Type == EventType.SwellDuck;

    public bool IsCrush => Type == EventType.BitcrushBurst;

    /// <summary>"Echo throw | 0:12.5": the kind and the moment (short, so it never widens the panel).</summary>
    public string Title => $"{TypeName(Type)} | {TimeText(StartSeconds)}";

    public static string TypeName(EventType type) => type switch
    {
        EventType.EchoThrow => "Echo throw",
        EventType.Stutter => "Stutter",
        EventType.FilterSweep => "Filter sweep",
        EventType.Freeze => "Freeze",
        EventType.PitchDive => "Pitch dive",
        EventType.SwellDuck => "Swell / Duck",
        EventType.BitcrushBurst => "Bitcrush burst",
        _ => type.ToString()
    };

    public static string TimeText(double seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return $"{(int)span.TotalMinutes}:{span.Seconds:00}.{span.Milliseconds / 100}";
    }

    /// <summary>A new event of the type with its usual settings.</summary>
    public static LayerEvent Create(EventType type, double startSeconds)
    {
        var item = new LayerEvent { Type = type, StartSeconds = Math.Max(0, startSeconds) };

        if (type == EventType.Stutter)
        {
            item.Length = 0.12;
            item.Amount = 0.1;
            item.Count = 8;
        }

        if (type == EventType.FilterSweep)
        {
            item.Length = 4.0;
            item.Amount = 0.8;
            item.Tail = 4.0;
        }

        if (type == EventType.Freeze)
        {
            item.Length = 6.0;
            item.Time = 0.5;
            item.Amount = 1.0;
            item.Tail = 4.0;
        }

        if (type == EventType.PitchDive)
        {
            item.Length = 5.0;
            item.Amount = 0.5;
            item.Tail = 5.0;
        }

        if (type == EventType.SwellDuck)
        {
            item.Length = 2.0;
            item.Time = 1.0;
            item.Amount = -0.9;
            item.Tail = 3.0;
        }

        if (type == EventType.BitcrushBurst)
        {
            item.Length = 2.0;
            item.Amount = 0.6;
            item.Tail = 1.0;
        }

        return item;
    }

    public static LayerEvent From(EventSpec spec)
    {
        var item = new LayerEvent();
        item.Apply(spec);
        return item;
    }

    public EventSpec ToSpec() => new(Type, StartSeconds, Length, Time, Amount, Count, Tail, Muted, Fade);

    public void Apply(EventSpec spec)
    {
        Type = Enum.IsDefined(spec.Type) ? spec.Type : EventType.EchoThrow;
        StartSeconds = double.IsFinite(spec.StartSeconds) ? Math.Max(0, spec.StartSeconds) : 0;
        Length = Type switch
        {
            EventType.Stutter => Math.Clamp(spec.Length, EventSpec.MinSlice, EventSpec.MaxSlice),
            EventType.FilterSweep => Math.Clamp(spec.Length, 0.05, EventSpec.MaxSweep),
            EventType.Freeze or EventType.PitchDive => Math.Clamp(spec.Length, 0.1, EventSpec.MaxSweep),
            EventType.SwellDuck or EventType.BitcrushBurst => Math.Clamp(spec.Length, 0.05, EventSpec.MaxSweep),
            _ => Math.Clamp(spec.Length, EventSpec.MinSlice, EventSpec.MaxEchoHold)
        };
        Time = Type switch
        {
            EventType.Freeze => Math.Clamp(spec.Time, 0.1, 1.0),
            EventType.SwellDuck => Math.Clamp(spec.Time, 0, EventSpec.MaxSweep),
            _ => Math.Clamp(spec.Time, EventSpec.MinDelay, EventSpec.MaxDelay)
        };
        Amount = Type switch
        {
            EventType.Stutter => Math.Clamp(spec.Amount, 0, 0.9),
            EventType.FilterSweep or EventType.Freeze or EventType.PitchDive or EventType.BitcrushBurst => Math.Clamp(spec.Amount, 0, 1),
            EventType.SwellDuck => Math.Clamp(spec.Amount, -1, 1),
            _ => Math.Clamp(spec.Amount, 0, EventSpec.MaxFeedback)
        };
        Count = Math.Clamp(spec.Count, EventSpec.MinRepeats, EventSpec.MaxRepeats);
        Tail = Math.Clamp(spec.Tail, 0, EventSpec.MaxTail);
        Fade = double.IsFinite(spec.Fade) ? Math.Clamp(spec.Fade, 0, EventSpec.MaxFade) : 1.0;
        Muted = spec.Muted;
    }
}
