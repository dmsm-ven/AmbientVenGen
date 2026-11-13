using CommunityToolkit.Mvvm.ComponentModel;

namespace AmbientVenGen.Core.Composition;

/// <summary>Editable modulator of a layer (see <see cref="ModulatorSpec"/>).</summary>
public sealed partial class Modulator : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private ModShape _shape = ModShape.Sine;

    /// <summary>Cycles per second.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private double _rateHz = 0.1;

    /// <summary>0..1: how far the parameter moves.</summary>
    [ObservableProperty]
    private double _depth = 0.5;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private ModTarget _target = ModTarget.Level;

    /// <summary>The modulator is switched off (a new one starts muted).</summary>
    [ObservableProperty]
    private bool _muted = true;

    /// <summary>"Level Modulator | Sine | 4.00 sec": what it moves, how, and the length of one cycle.</summary>
    public string Title => $"{TargetName(Target)} Modulator | {Shape} | {PeriodText(RateHz)}";

    public static string TargetName(ModTarget target) => target switch
    {
        ModTarget.ReverbSend => "Reverb send",
        ModTarget.Cutoff => "Cutoff",
        ModTarget.None => "Level",
        _ => target.ToString()
    };

    /// <summary>The length of one cycle in seconds ("4.00 sec").</summary>
    public static string PeriodText(double rateHz)
    {
        double seconds = 1.0 / Math.Max(rateHz, ModulatorSpec.MinRateHz);
        string format = seconds < 10 ? "0.00" : seconds < 100 ? "0.0" : "0";

        return seconds.ToString(format, System.Globalization.CultureInfo.InvariantCulture) + " sec";
    }

    /// <summary>A modulator made from a saved spec (values are clamped).</summary>
    public static Modulator From(ModulatorSpec spec)
    {
        var modulator = new Modulator();
        modulator.Apply(spec);
        return modulator;
    }

    public ModulatorSpec ToSpec() => new(Shape, RateHz, Depth, Target, Muted);

    public void Apply(ModulatorSpec? spec)
    {
        if (spec is null)
        {
            Reset();
            return;
        }

        Shape = Enum.IsDefined(spec.Shape) ? spec.Shape : ModShape.Sine;
        RateHz = Math.Clamp(spec.RateHz, ModulatorSpec.MinRateHz, ModulatorSpec.MaxRateHz);
        Depth = Math.Clamp(spec.Depth, 0, 1);

        // A file of the first version has "no target" for a modulator that is off.
        bool off = spec.Target == ModTarget.None || !Enum.IsDefined(spec.Target);
        Target = off ? ModTarget.Level : spec.Target;
        Muted = off || spec.Muted;
    }

    public void Reset()
    {
        Shape = ModShape.Sine;
        RateHz = 0.1;
        Depth = 0.5;
        Target = ModTarget.Level;
        Muted = true;
    }
}
