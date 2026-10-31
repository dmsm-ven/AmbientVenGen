using AmbientVenGen.Core.Composition;

namespace AmbientVenGen.Desktop.ViewModels;

/// <summary>Fixed lists for ComboBoxes (used from XAML through x:Static).</summary>
public static class Choices
{
    public static IReadOnlyList<EasingOption> EasingOptions { get; } =
    [
        new(Easing.Linear, "Linear",
            "Constant speed. Level 0% to 80% over 10 s rises exactly 8% every second."),
        new(Easing.Sine, "Sine",
            "Soft S-curve: gentle start and end, the middle is the steepest. Natural-sounding swells; 50% is reached exactly halfway."),
        new(Easing.EaseIn, "Ease in",
            "Starts slowly, then speeds up (t\u00B2). After half of the time only 25% of the change has happened."),
        new(Easing.EaseOut, "Ease out",
            "Starts fast, then settles. After half of the time 75% of the change has already happened."),
        new(Easing.EaseInOut, "Ease in-out",
            "Slow start, fast middle, slow end - a sharper S than Sine. 0 to 100 over 10 s: 2 at 2 s, 50 at 5 s, 98 at 8 s."),
        new(Easing.Exponential, "Exponential",
            "Almost nothing for a long time, then it blooms. The last 10% of the time makes half of the whole change.")
    ];

    public static IReadOnlyList<Choice<LayerType>> LayerTypes { get; } =
    [
        new("Drone", LayerType.Drone),
        new("Noise (texture)", LayerType.Noise),
        new("Pad (choir)", LayerType.Pad)
    ];
}

/// <summary>One entry of the transition function list: value, title and a short explanation.</summary>
public sealed record EasingOption(Easing Value, string Label, string Description);
