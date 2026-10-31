namespace AmbientVenGen.Core.Composition;

/// <summary>Sensible starting values for each layer type.</summary>
public static class LayerDefaults
{
    public static Layer Create(LayerType type, string name)
    {
        var layer = new Layer { Type = type, Name = name };

        switch (type)
        {
            case LayerType.Drone:
                layer.PitchHz = 41.2;
                layer.LevelFrom = layer.LevelTo = 0.75;
                layer.BrightnessFrom = layer.BrightnessTo = 0.25;
                layer.ModulationFrom = layer.ModulationTo = 0.30;
                break;

            case LayerType.Noise:
                layer.LevelFrom = layer.LevelTo = 0.45;
                layer.BrightnessFrom = layer.BrightnessTo = 0.30;
                layer.ModulationFrom = layer.ModulationTo = 0.35;
                break;

            case LayerType.Pad:
                layer.PitchHz = 82.4;
                layer.LevelFrom = layer.LevelTo = 0.50;
                layer.BrightnessFrom = layer.BrightnessTo = 0.40;
                layer.ModulationFrom = layer.ModulationTo = 0.40;
                break;

            case LayerType.FieldRecord:
                layer.LevelFrom = layer.LevelTo = 0.65;
                layer.BrightnessFrom = layer.BrightnessTo = 0.85;
                layer.ModulationFrom = layer.ModulationTo = 0.15;
                break;
        }

        return layer;
    }

    /// <summary>
    /// Gives the layer new random sound settings (levels, brightness, modulation, pitch, pan, fades and curves).
    /// Name, type, position, length, mute and the source file are kept. Ranges stay musical, so the result is usable.
    /// </summary>
    public static void Randomize(Layer layer, Random random)
    {
        double Range(double min, double max) => min + random.NextDouble() * (max - min);
        Easing AnyEasing() => (Easing)random.Next(Enum.GetValues<Easing>().Length);

        bool recording = layer.Type == LayerType.FieldRecord;

        layer.LevelFrom = Range(0.30, 0.90);
        layer.LevelTo = Range(0.30, 0.90);
        layer.BrightnessFrom = recording ? Range(0.35, 1.0) : Range(0.10, 0.80);
        layer.BrightnessTo = recording ? Range(0.35, 1.0) : Range(0.10, 0.80);
        layer.ModulationFrom = Range(0.05, 0.70);
        layer.ModulationTo = Range(0.05, 0.70);
        layer.LevelEasing = AnyEasing();
        layer.BrightnessEasing = AnyEasing();
        layer.ModulationEasing = AnyEasing();

        // Pitch: log-uniform inside a sensible band of the layer type (a recording has no pitch).
        switch (layer.Type)
        {
            case LayerType.Drone:
                layer.PitchHz = Math.Round(Math.Exp(Range(Math.Log(30), Math.Log(120))), 1);
                break;
            case LayerType.Pad:
                layer.PitchHz = Math.Round(Math.Exp(Range(Math.Log(55), Math.Log(330))), 1);
                break;
        }

        layer.Pan = Math.Round(Range(-0.6, 0.6), 2);

        double fade = Math.Max(1, Math.Round(layer.DurationSeconds * Range(0.08, 0.30)));
        layer.FadeInSeconds = Math.Min(fade, layer.DurationSeconds);
        layer.FadeOutSeconds = Math.Min(Math.Max(1, Math.Round(layer.DurationSeconds * Range(0.08, 0.30))), layer.DurationSeconds);
        layer.FadeEasing = AnyEasing();

        layer.Seed = random.Next(1, int.MaxValue);
    }

    /// <summary>Creates a layer and lets the caller adjust it, to keep templates compact.</summary>
    public static Layer Create(LayerType type, string name, Action<Layer> configure)
    {
        Layer layer = Create(type, name);
        configure(layer);
        return layer;
    }
}
