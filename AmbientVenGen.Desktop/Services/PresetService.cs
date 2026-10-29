using AmbientVenGen.Desktop.Presets;

namespace AmbientVenGen.Desktop.Services;

public sealed class PresetService : IPresetService
{
    private static readonly AmbientPreset[] BuiltInPresets =
    [
        new("Deep Space", 0.65, 0.25, 0.35, 0.35),
        new("Dark Drone", 0.80, 0.15, 0.15, 0.15),
        new("Distant Atmosphere", 0.35, 0.60, 0.65, 0.70)
    ];

    public IReadOnlyList<AmbientPreset> GetPresets() => BuiltInPresets;
}
