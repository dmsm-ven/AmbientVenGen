using AmbientVenGen.Desktop.Presets;

namespace AmbientVenGen.Desktop.Services;

/// <summary>
/// Provides composition presets independently from the UI.
/// This can later be replaced with a JSON- or user-library-backed implementation.
/// </summary>
public interface IPresetService
{
    IReadOnlyList<AmbientPreset> GetPresets();
}
