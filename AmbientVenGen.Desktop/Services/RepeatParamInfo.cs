using AmbientVenGen.Core.Composition;

namespace AmbientVenGen.Desktop.Services;

/// <summary>How one property of a layer is shown and limited in the Repeat properties window.</summary>
public sealed record RepeatParamInfo(
    RepeatParam Param,
    string Label,
    string Unit,
    double Min,
    double Max,
    double Scale,
    int Decimals,
    Func<Layer, double> Base,
    Func<Layer, bool> AppliesTo)
{
    private static bool Always(Layer l) => true;
    private static bool Tonal(Layer l) => l.Type is LayerType.Drone or LayerType.Pad;
    private static bool Recording(Layer l) => l.Type == LayerType.FieldRecord;

    /// <summary>All the properties a play may have its own value of, in the order of the rows.</summary>
    public static IReadOnlyList<RepeatParamInfo> All { get; } =
    [
        new(RepeatParam.Level, "Level", "%", 0, 1, 100, 0, l => l.LevelFrom, Always),
        new(RepeatParam.Brightness, "Brightness", "%", 0, 1, 100, 0, l => l.BrightnessFrom, Always),
        new(RepeatParam.Modulation, "Modulation", "%", 0, 1, 100, 0, l => l.ModulationFrom, Always),
        new(RepeatParam.Pitch, "Pitch", "Hz", Layer.MinPitchHz, Layer.MaxPitchHz, 1, 1, l => l.PitchHz, Tonal),
        new(RepeatParam.PitchShift, "Pitch shift", "st", -SampleTransform.MaxPitchSemitones, SampleTransform.MaxPitchSemitones, 1, 1, l => l.PitchShift, Recording),
        new(RepeatParam.Speed, "Speed", "x", Layer.MinSpeed, Layer.MaxSpeed, 1, 2, l => l.Speed, Recording),
        new(RepeatParam.Stretch, "Stretch", "x", 1, SampleTransform.MaxStretch, 1, 2, l => l.Stretch, Recording),
        new(RepeatParam.Pan, "Pan", "%", -1, 1, 100, 0, l => l.Pan, Always),
        new(RepeatParam.Cutoff, "Cutoff", "Hz", Layer.MinCutoffHz, Layer.MaxCutoffHz, 1, 0, l => l.FilterCutoffHz, Always),
        new(RepeatParam.Resonance, "Resonance", "%", 0, 1, 100, 0, l => l.FilterResonance, Always),
        new(RepeatParam.DelayTime, "Delay", "s", 0, Layer.MaxDelaySeconds, 1, 2, l => l.DelaySeconds, Always),
        new(RepeatParam.DelayFeedback, "Feedback", "%", 0, Layer.MaxFeedback, 100, 0, l => l.DelayFeedback, Always),
        new(RepeatParam.ReverbSend, "Reverb send", "%", 0, 1, 100, 0, l => l.ReverbSend, Always)
    ];

    public string Format(double value) =>
        (value * Scale).ToString("F" + Decimals, System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>Reads what the user typed (in the shown unit); null when it is not a number.</summary>
    public double? Parse(string text)
    {
        text = text.Trim().TrimEnd('%').Trim();

        if (!double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out double shown)
            && !double.TryParse(text.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out shown))
            return null;

        return Math.Clamp(shown / Scale, Min, Max);
    }
}
