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

    public const string Repeats =
        "How many times the layer is played one after another (1 = no repeat). Every play restarts the curves and fades.";

    public const string RepeatGap =
        "Silence between two plays. Whole length = Duration x Repeats + Gap x (Repeats - 1).";

    public const string FadeIn =
        "Time the layer takes to rise from silence at its start.";

    public const string FadeInOut =
        "Fade in: time to rise from silence at the start. Fade out: time to sink into silence at the end. Both follow the curve between the knobs.";

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

    public const string Cutoff =
        "Low-pass filter: frequencies above this are cut. Lower makes the layer dull, muffled and distant; fully right (20 kHz) the filter is off. Works on every layer type.";

    public const string Resonance =
        "Emphasis of the sound right at the cutoff. A little adds character, a lot gives a whistling, vowel-like peak.";

    public const string Delay =
        "Echo time of this layer, in seconds (0 = no echo). Long times make distant, spacious repeats.";

    public const string Feedback =
        "How much of the echo is fed back into the delay: more gives more repeats that fade away slower.";

    public const string ReverbSend =
        "How much of this layer goes into the project reverb (the Space slider on the timeline). 100% = as before, 0% = this layer stays dry.";

    public const string PitchDrift =
        "Slow random wandering of the pitch (a recording: of its speed), up to about half a semitone. Makes tones breathe and feel alive.";

    public const string PanDrift =
        "Slow random wandering of the layer across the stereo field around the Pan position.";

    public const string Speed =
        "Playback speed of the recording, like a tape: below 1 it is slower and lower, above 1 faster and higher. Click the value to type it.";

    public const string Source =
        "The audio file this layer plays. It is looped (the end fades into the beginning), so any length works. Brightness softens the high frequencies.";
}
