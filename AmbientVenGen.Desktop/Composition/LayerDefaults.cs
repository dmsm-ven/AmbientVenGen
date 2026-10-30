namespace AmbientVenGen.Desktop.Composition;

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
        }

        return layer;
    }

    /// <summary>Creates a layer and lets the caller adjust it, to keep templates compact.</summary>
    public static Layer Create(LayerType type, string name, Action<Layer> configure)
    {
        Layer layer = Create(type, name);
        configure(layer);
        return layer;
    }
}
