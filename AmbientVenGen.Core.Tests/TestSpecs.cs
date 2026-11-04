using AmbientVenGen.Core.Composition;

namespace AmbientVenGen.Core.Tests;

/// <summary>Builders of small specs for the tests.</summary>
internal static class TestSpecs
{
    public static CurveSpec Flat(double value) => new(value, value, Easing.Linear);

    public static LayerSpec Layer(
        LayerType type = LayerType.Pad,
        double start = 0,
        double duration = 60,
        double fadeIn = 0,
        double fadeOut = 0,
        Easing fadeEasing = Easing.Linear,
        Easing? fadeOutEasing = null,
        double pitchHz = 220,
        int repeats = 1,
        double gap = 0,
        double delaySeconds = 0,
        double delayFeedback = 0.4,
        bool muted = false,
        int seed = 1234) => new(
            Guid.NewGuid(),
            "Test",
            type,
            start,
            duration,
            fadeIn,
            fadeOut,
            fadeEasing,
            Flat(0.8),
            Flat(0.5),
            Flat(0.3),
            pitchHz,
            0,
            muted,
            seed,
            Repeats: repeats,
            RepeatGapSeconds: gap,
            DelaySeconds: delaySeconds,
            DelayFeedback: delayFeedback,
            FadeOutEasing: fadeOutEasing);

    public static ProjectSpec Project(double duration, params LayerSpec[] layers) =>
        new(ProjectSpec.CurrentVersion, duration, 777, 0.3, layers);
}
