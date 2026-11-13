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
            "Almost nothing for a long time, then it blooms. The last 10% of the time makes half of the whole change."),
        new(Easing.ExponentialDecay, "Exponential decay",
            "The mirror of Exponential: a very fast start that settles ever more slowly. Half of the whole change happens in the first 10% of the time.")
    ];

    /// <summary>Musical intervals for "Pitch interval": the new pitch is the source pitch times the ratio.</summary>
    public static IReadOnlyList<PitchInterval> PitchIntervals { get; } = new PitchInterval[]
    {
        new("Unison", 1.0, "Same pitch"),
        new("Octave", 2.0, "Twice as high"),
        new("Fifth", 1.5, "Open and stable"),
        new("Tritone (√2)", Math.Sqrt(2.0), "Tense, unresolved"),
        new("Fourth", 4.0 / 3.0, "Suspended, calm"),
        new("Major third", 1.25, "Bright, warm"),
        new("Minor third", 1.2, "Dark, sad"),
        new("Minor seventh", 1.78, "Soft tension"),
        new("Major seventh", 1.89, "Dreamy, floating")
    }.OrderBy(interval => interval.Ratio).ToArray();

    /// <summary>Second group of the interval table: mathematical and "harmonic series" ratios, smallest first.</summary>
    public static IReadOnlyList<PitchInterval> MathIntervals { get; } = new PitchInterval[]
    {
        new("π²⁄₈", Math.PI * Math.PI / 8.0, "Almost a major third, slightly flat and strange"),
        new("2π⁄₅", 2.0 * Math.PI / 5.0, "Between the minor and major third"),
        new("2¹⁄³", Math.Pow(2.0, 1.0 / 3.0), "Soft, slightly \"wrong\" major colour; the octave split in 3"),
        new("4⁄π", 4.0 / Math.PI, "A narrow, uneasy third"),
        new("e⁄₂", Math.E / 2.0, "Warm, a little alive"),
        new("¹¹⁄₈ (undecimal)", 11.0 / 8.0, "An unusual fourth / third"),
        new("π⁄₂", Math.PI / 2.0, "Strange, \"mathematical\""),
        new("2²⁄³", Math.Pow(2.0, 2.0 / 3.0), "Between a fifth and a minor sixth"),
        new("Golden ratio (φ)", (1.0 + Math.Sqrt(5.0)) / 2.0, "Organic, \"natural\", slightly dissonant"),
        new("¹³⁄₈ (tridecimal)", 13.0 / 8.0, "Very close to the golden ratio"),
        new("√3", Math.Sqrt(3.0), "More open and bright than the tritone"),
        new("7/4 (septimal)", 7.0 / 4.0, "A soft, \"natural\" minor seventh"),
        new("√π", Math.Sqrt(Math.PI), "Bright, a little restless"),
        new("√5", Math.Sqrt(5.0), "Wide and spacious (an octave lower it is 1.118)")
    }.OrderBy(interval => interval.Ratio).ToArray();

    public static IReadOnlyList<NoiseOption> NoiseOptions { get; } =
    [
        new(NoiseType.White, "White noise",
            "Equal energy at every frequency: a bright hiss, like static or rain on a roof.",
            "Sounds like: static on an untuned radio or TV, a hissing steam pipe, heavy rain on a metal roof."),
        new(NoiseType.Pink, "Pink noise",
            "Energy falls 3 dB per octave: balanced and natural, like steady rain or a waterfall.",
            "Sounds like: steady rain on leaves, a distant waterfall, a soft rushing of the sea."),
        new(NoiseType.Brown, "Brown (red) noise",
            "Energy falls 6 dB per octave: a deep rumble, like thunder far away or a strong wind.",
            "Sounds like: far-away thunder, a low wind in a chimney, the rumble inside an airplane cabin."),
        new(NoiseType.Blue, "Blue noise",
            "Energy rises 3 dB per octave: thin and airy, mostly high frequencies.",
            "Sounds like: a thin hiss of air from a spray can, a light breeze in thin grass, sparkling steam."),
        new(NoiseType.Violet, "Violet noise",
            "Energy rises 6 dB per octave: very sharp and hissy, almost only the highest frequencies.",
            "Sounds like: a piercing sharp hiss, like a mosquito-thin whistle of escaping air or a sizzling frying pan."),
        new(NoiseType.Grey, "Grey noise",
            "White noise corrected for the ear: lows and highs are lifted, so every part of the spectrum sounds equally loud.",
            "Sounds like: a balanced, even wash of air, like a quiet fan or an air conditioner in a calm room."),

        // Textures: a divider is drawn above the first of them.
        new(NoiseType.Crackle, "Crackle (fire)",
            "Random snaps and pops over a low, flickering rumble. Modulation = how often it crackles.",
            "Sounds like: a campfire or a fireplace, dry wood popping and embers settling.", StartsGroup: true),
        new(NoiseType.Vinyl, "Vinyl dust",
            "A fine surface hiss, tiny ticks and a few bigger pops. Modulation = how worn the record is.",
            "Sounds like: an old record before the music starts, dust on the needle."),
        new(NoiseType.RadioStatic, "Radio static",
            "Band-limited static with a drifting tuning, short dropouts and a faint whistle. Modulation = how much it drifts.",
            "Sounds like: an old radio between stations, a distant shortwave broadcast, a walkie-talkie."),
        new(NoiseType.Wind, "Wind",
            "A resonant band of noise whose pitch and loudness swell in slow gusts. Modulation = how strong the gusts are.",
            "Sounds like: wind in a chimney or in the trees, a storm outside the window."),
        new(NoiseType.Rain, "Rain",
            "A dense fine hiss with many separate drops. Modulation = how heavy the rain is.",
            "Sounds like: rain on a window or on leaves, a drizzle under an awning."),
        new(NoiseType.Ocean, "Ocean waves",
            "Brown noise swelling in waves with foam on the crests.",
            "Sounds like: sea waves rolling onto a beach, foam hissing at each crest."),
        new(NoiseType.ElectricalHum, "Electrical hum",
            "Mains hum with harmonics and a light buzz.",
            "Sounds like: a power transformer or a fluorescent lamp, a steady mains hum."),
        new(NoiseType.TapeHiss, "Tape hiss",
            "High hiss whose level and tone float slowly.",
            "Sounds like: an old cassette between songs, the tape hiss slowly floating."),
        new(NoiseType.SpaceRumble, "Deep space rumble",
            "Very low rumble with rare slow shifts.",
            "Sounds like: the engine room of a starship, a very low rumble that slowly shifts."),
        new(NoiseType.Geiger, "Geiger / sparks",
            "Rare hard clicks at random intervals.",
            "Sounds like: a Geiger counter near a source, or sparks from a faulty cable."),
        new(NoiseType.IceSinging, "Ice singing",
            "Cracks running through ice: quick falling tones.",
            "Sounds like: cracks running through the ice of a frozen lake, a ringing sci-fi laser shot."),
        new(NoiseType.EnergyShield, "Energy shield",
            "Detuned hum through a breathing filter.",
            "Sounds like: a sci-fi force field, a detuned hum that slowly breathes."),
        // HighVoltage and PlasmaArc are finished in the generator but are not offered for now.
        new(NoiseType.CrtWhine, "CRT / coil whine",
            "A thin high tone with rare ticks.",
            "Sounds like: an old TV or a transformer in a quiet room, a thin high whine."),
        new(NoiseType.GasBurner, "Gas burner",
            "A steady blue-flame roar with a flutter, a low rumble under it and a soft whoomf now and then. Modulation = how much the flame flutters.",
            "Sounds like: a gas stove ring, a camping burner, a blowtorch from a distance."),
        new(NoiseType.Gong, "Gong",
            "Rare strokes of a big gong: inharmonic partials that ring for seconds and slowly beat. Modulation = how often it is struck.",
            "Sounds like: a temple gong, a big tam-tam, a slow ceremonial stroke fading in the hall."),
        new(NoiseType.WaterDrop, "Water drop",
            "A drop falling from a height into water: an impact tick, a rising \"bloop\" and quiet ripples. Modulation = how often drops fall.",
            "Sounds like: a drop falling into a still pond, a cave, a leaking tap above a full bucket.")
    ];

    /// <summary>Three-layer relations (the arithmetic lives in Core: <see cref="PitchRelations"/>).</summary>
    public static IReadOnlyList<ChordPreset> ChordPresets => PitchRelations.Chords;

    public static IReadOnlyList<Choice<ModShape>> ModShapes { get; } =
    [
        new("Sine", ModShape.Sine),
        new("Triangle", ModShape.Triangle),
        new("Random", ModShape.Random)
    ];

    public static IReadOnlyList<Choice<ModTarget>> ModTargets { get; } =
    [
        new("Level", ModTarget.Level),
        new("Cutoff", ModTarget.Cutoff),
        new("Pan", ModTarget.Pan),
        new("Reverb send", ModTarget.ReverbSend),
        new("Brightness", ModTarget.Brightness),
        new("Modulation", ModTarget.Modulation)
    ];

    public static IReadOnlyList<Choice<LayerType>> LayerTypes { get; } =
    [
        new("Drone", LayerType.Drone),
        new("Noise (texture)", LayerType.Noise),
        new("Pad (choir)", LayerType.Pad),
        new("Field record", LayerType.FieldRecord)
    ];
}

/// <summary>One entry of the transition function list: value, title and a short explanation.</summary>
/// <summary>A pitch interval: name, frequency ratio and a short character description.</summary>
public sealed record PitchInterval(string Name, double Ratio, string Description)
{
    public string Display => $"{Name}  \u00D7{Ratio:0.###}";
}

/// <summary>One row of the interval table: the interval and the pitch the layer would get from it.</summary>
public sealed partial class IntervalRow : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public IntervalRow(PitchInterval interval, double hz)
    {
        Interval = interval;
        Hz = hz;
    }

    public PitchInterval Interval { get; }
    public double Hz { get; }
    public string HzText => $"{Hz:0.#} Hz";

    /// <summary>True when the selected layer has exactly this pitch now (the row is outlined).</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _isCurrent;
}

/// <summary>A layer offered as the source of an interval, with its pitch shown in the list.</summary>
public sealed record IntervalSource(Layer Layer, string Text);

/// <summary>One entry of the noise type list: value, title and a short explanation.</summary>
public sealed record NoiseOption(NoiseType Value, string Label, string Description, string Likeness, bool StartsGroup = false);

public sealed record EasingOption(Easing Value, string Label, string Description);

/// <summary>One row of the three-layer table: the relation and the pitches the lower and the higher non-root layer would get.</summary>
public sealed partial class ChordRow : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public ChordRow(ChordPreset preset, double lowHz, double highHz, double rootHz)
    {
        Preset = preset;
        LowHz = lowHz;
        HighHz = highHz;

        // All three pitches, lowest first; the root is marked so that it can be shown in another colour.
        Voices = new[] { (Hz: rootHz, IsRoot: true), (Hz: lowHz, IsRoot: false), (Hz: highHz, IsRoot: false) }
            .OrderBy(v => v.Hz)
            .Select(v => new ChordVoice($"{v.Hz:0.#}", v.IsRoot))
            .ToArray();
    }

    public ChordPreset Preset { get; }
    public double LowHz { get; }
    public double HighHz { get; }
    public IReadOnlyList<ChordVoice> Voices { get; }

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _isCurrent;
}

/// <summary>One pitch in a three-layer row; the root one is drawn in another colour.</summary>
public sealed record ChordVoice(string Text, bool IsRoot);

/// <summary>A Drone / Pad layer in the interval panel: a checkbox (up to two), the Root mark and its pitch.</summary>
public sealed partial class IntervalLayerItem : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public IntervalLayerItem(Layer layer, bool isTarget)
    {
        Layer = layer;
        IsTarget = isTarget;
    }

    public Layer Layer { get; }

    /// <summary>The layer selected in the inspector: it is always one of the three and cannot be unticked.</summary>
    public bool IsTarget { get; }

    public bool ShowCheck => !IsTarget;

    public string Text => $"{Layer.Name}  ({Layer.PitchHz:0.#} Hz){(IsTarget ? "  - this layer" : string.Empty)}";

    public void RefreshText() => OnPropertyChanged(nameof(Text));

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _isChecked;

    /// <summary>False while two other layers are ticked and this one is not.</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _canCheck = true;

    /// <summary>The Root button is shown for the three layers of a chord.</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _showRoot;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _isRoot;
}
