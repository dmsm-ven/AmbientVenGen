namespace AmbientVenGen.Core.Composition;

/// <summary>Shape of a smooth change between two values over time.</summary>
public enum Easing
{
    Linear,
    Sine,
    EaseIn,
    EaseOut,
    EaseInOut,
    Exponential,

    /// <summary>Mirror of <see cref="Exponential"/>: a fast start that settles ever more slowly (appended last: file compatibility).</summary>
    ExponentialDecay
}

public static class EasingFunctions
{
    /// <summary>Maps progress 0..1 to eased progress 0..1.</summary>
    public static double Apply(Easing easing, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);

        return easing switch
        {
            Easing.Linear => t,
            Easing.Sine => 0.5 - 0.5 * Math.Cos(Math.PI * t),
            Easing.EaseIn => t * t,
            Easing.EaseOut => 1.0 - (1.0 - t) * (1.0 - t),
            Easing.EaseInOut => t < 0.5
                ? 2.0 * t * t
                : 1.0 - Math.Pow(-2.0 * t + 2.0, 2.0) / 2.0,
            Easing.Exponential => t <= 0.0 ? 0.0 : Math.Pow(2.0, 10.0 * t - 10.0),
            Easing.ExponentialDecay => (1.0 - Math.Pow(2.0, -10.0 * t)) / (1.0 - Math.Pow(2.0, -10.0)),
            _ => t
        };
    }
}
