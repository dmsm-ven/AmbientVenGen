using CommunityToolkit.Mvvm.ComponentModel;

namespace AmbientVenGen.Core.Composition;

/// <summary>Editable modulator of a layer (see <see cref="ModulatorSpec"/>).</summary>
public sealed partial class Modulator : ObservableObject
{
    [ObservableProperty]
    private ModShape _shape = ModShape.Sine;

    /// <summary>Cycles per second.</summary>
    [ObservableProperty]
    private double _rateHz = 0.1;

    /// <summary>0..1: how far the parameter moves.</summary>
    [ObservableProperty]
    private double _depth = 0.5;

    [ObservableProperty]
    private ModTarget _target = ModTarget.None;

    public ModulatorSpec ToSpec() => new(Shape, RateHz, Depth, Target);

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
        Target = Enum.IsDefined(spec.Target) ? spec.Target : ModTarget.None;
    }

    public void Reset()
    {
        Shape = ModShape.Sine;
        RateHz = 0.1;
        Depth = 0.5;
        Target = ModTarget.None;
    }
}
