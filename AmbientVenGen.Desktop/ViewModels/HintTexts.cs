namespace AmbientVenGen.Desktop.ViewModels;

/// <summary>Explanations shown by the "?" icons when hints are switched on (used from XAML through x:Static).</summary>
public static class HintTexts
{
    public const string LayerType =
        "Drone: low sustained tones. Noise: a filtered noise texture (wind, dust). Pad: a soft harmonic 'choir'. Field record: your own audio file (wav, mp3, flac...), looped.";

    public const string Level =
        "Loudness of the layer. 'From' is the level at the start of the layer, 'To' at its end; the curve on the right sets how it moves between them.";

    public const string Brightness =
        "How much high-frequency content the sound has: low is dark and muffled, high is open and airy. For noise it moves the filter.";

    public const string Modulation =
        "Depth of the slow movement: gentle pitch drift, volume 'breathing' and stereo width. Low is still, high is alive and shifting.";

    public const string Pitch =
        "Base note of the layer in Hz. Lower is deeper and heavier (41 Hz is a low E); higher is lighter. Click the value to type an exact frequency (20-1000 Hz).";

    public const string Pan =
        "Position in the stereo field: -1 is fully left, 0 is the centre, +1 is fully right.";

    public const string Start =
        "Where the layer begins on the timeline (hh:mm:ss). You can also drag the clip on the timeline.";

    public const string Duration =
        "How long the layer lasts (hh:mm:ss). You can also drag the edges of the clip on the timeline.";

    public const string FadeIn =
        "Time the layer takes to rise from silence at its start.";

    public const string FadeOut =
        "Time the layer takes to sink into silence at its end.";

    public const string TransitionCurve =
        "Shape of the fades. Linear: constant speed. Sine and Ease in-out: soft start and end. Exponential: slow, then sudden.";

    public const string Reverb =
        "Amount of long reverb on the whole mix. More makes the sound vaster and more distant.";

    public const string Template =
        "A ready-made starting point. Applying it replaces all current layers.";

    public const string TrackLength =
        "Total length of the track. Layers can be placed anywhere inside it.";

    public const string Source =
        "The audio file this layer plays. It is looped (the end fades into the beginning), so any length works. Brightness softens the high frequencies.";
}
