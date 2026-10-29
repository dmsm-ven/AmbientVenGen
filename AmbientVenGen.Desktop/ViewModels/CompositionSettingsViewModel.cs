using AmbientVenGen.Desktop.Models;
using AmbientVenGen.Desktop.Presets;
using AmbientVenGen.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace AmbientVenGen.Desktop.ViewModels;

/// <summary>
/// Owns all user-editable composition settings and preset selection.
/// Keeps parameter-related state and behavior out of MainViewModel.
/// </summary>
public partial class CompositionSettingsViewModel : ObservableObject
{
    private bool _isApplyingPreset;

    public ObservableCollection<AmbientPreset> Presets { get; }
    public IReadOnlyList<int> DurationOptions { get; } = [5, 10, 15];

    /// <summary>
    /// Raised when a setting changes and live playback should receive the new values.
    /// </summary>
    public event Action? SettingsChanged;

    [ObservableProperty]
    private AmbientPreset? _selectedPreset;

    [ObservableProperty]
    private double _droneLevel = 0.65;

    [ObservableProperty]
    private double _atmosphereLevel = 0.25;

    [ObservableProperty]
    private double _brightness = 0.35;

    [ObservableProperty]
    private double _modulation = 0.35;

    [ObservableProperty]
    private int _selectedDurationMinutes = 10;

    public CompositionSettingsViewModel(IPresetService presetService)
    {
        ArgumentNullException.ThrowIfNull(presetService);

        Presets = new ObservableCollection<AmbientPreset>(presetService.GetPresets());

        if (Presets.Count > 0)
            SelectedPreset = Presets[0];
        else
            NotifySettingsChanged();
    }

    partial void OnSelectedPresetChanged(AmbientPreset? value)
    {
        if (value is null)
            return;

        _isApplyingPreset = true;
        try
        {
            DroneLevel = value.DroneLevel;
            AtmosphereLevel = value.AtmosphereLevel;
            Brightness = value.Brightness;
            Modulation = value.Modulation;
        }
        finally
        {
            _isApplyingPreset = false;
        }

        NotifySettingsChanged();
    }

    partial void OnDroneLevelChanged(double value) => OnParameterChanged();
    partial void OnAtmosphereLevelChanged(double value) => OnParameterChanged();
    partial void OnBrightnessChanged(double value) => OnParameterChanged();
    partial void OnModulationChanged(double value) => OnParameterChanged();

    public CompositionSettings CreateSettings(int seed) => new(
        (float)DroneLevel,
        (float)AtmosphereLevel,
        (float)Brightness,
        (float)Modulation,
        seed);

    private void OnParameterChanged()
    {
        if (!_isApplyingPreset)
            NotifySettingsChanged();
    }

    private void NotifySettingsChanged() => SettingsChanged?.Invoke();
}
