using AmbientVenGen.Core.Composition;

namespace AmbientVenGen.Desktop.ViewModels;

/// <summary>Fixed lists for ComboBoxes (used from XAML through x:Static).</summary>
public static class Choices
{
    public static IReadOnlyList<Choice<Easing>> Easings { get; } =
    [
        new("Linear", Easing.Linear),
        new("Sine", Easing.Sine),
        new("Ease in", Easing.EaseIn),
        new("Ease out", Easing.EaseOut),
        new("Ease in-out", Easing.EaseInOut),
        new("Exponential", Easing.Exponential)
    ];

    public static IReadOnlyList<Choice<LayerType>> LayerTypes { get; } =
    [
        new("Drone", LayerType.Drone),
        new("Noise (texture)", LayerType.Noise),
        new("Pad (choir)", LayerType.Pad)
    ];
}
