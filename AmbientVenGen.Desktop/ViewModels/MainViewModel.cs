using AmbientVenGen.Desktop.Audio;
using AmbientVenGen.Desktop.Models;
using AmbientVenGen.Desktop.Presets;
using AmbientVenGen.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace AmbientVenGen.Desktop.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly AudioPlaybackService _playbackService = new();
    private readonly AudioExportService _exportService = new();
    private readonly FileDialogService _fileDialogService = new();

    private int _seed = 12345;

    public ObservableCollection<AmbientPreset> Presets { get; }
    public IReadOnlyList<int> DurationOptions { get; } = [5, 10, 15];

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

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private bool _isExporting;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    public bool IsNotExporting => !IsExporting;

    public MainViewModel()
    {
        Presets = new ObservableCollection<AmbientPreset>
        {
            new("Deep Space", 0.65, 0.25, 0.35, 0.35),
            new("Dark Drone", 0.80, 0.15, 0.15, 0.15),
            new("Distant Atmosphere", 0.35, 0.60, 0.65, 0.70)
        };

        SelectedPreset = Presets[0];
    }

    partial void OnSelectedPresetChanged(AmbientPreset? value)
    {
        if (value is null)
            return;

        DroneLevel = value.DroneLevel;
        AtmosphereLevel = value.AtmosphereLevel;
        Brightness = value.Brightness;
        Modulation = value.Modulation;
        ApplyLiveSettings();
        StatusMessage = $"Preset selected: {value.Name}";
    }

    partial void OnDroneLevelChanged(double value) => ApplyLiveSettings();
    partial void OnAtmosphereLevelChanged(double value) => ApplyLiveSettings();
    partial void OnBrightnessChanged(double value) => ApplyLiveSettings();
    partial void OnModulationChanged(double value) => ApplyLiveSettings();

    partial void OnIsExportingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotExporting));
        ExportWavCommand.NotifyCanExecuteChanged();
        ExportMp3Command.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void Play()
    {
        try
        {
            _playbackService.Start(CreateSettings());
            IsPlaying = true;
            StatusMessage = $"Playing · Seed: {_seed}";
        }
        catch (Exception ex)
        {
            IsPlaying = false;
            StatusMessage = $"Playback error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Stop()
    {
        _playbackService.Stop();
        IsPlaying = false;
        StatusMessage = "Stopped";
    }

    [RelayCommand]
    private void Regenerate()
    {
        _seed = Random.Shared.Next(1, int.MaxValue);

        if (IsPlaying)
        {
            Play();
            return;
        }

        StatusMessage = $"New composition seed: {_seed}";
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private Task ExportWavAsync() => ExportAsync(mp3: false);

    [RelayCommand(CanExecute = nameof(CanExport))]
    private Task ExportMp3Async() => ExportAsync(mp3: true);

    private bool CanExport() => !IsExporting;

    private async Task ExportAsync(bool mp3)
    {
        string? path = _fileDialogService.ChooseExportPath(mp3, _seed);
        if (string.IsNullOrWhiteSpace(path))
            return;

        IsExporting = true;
        StatusMessage = "Rendering composition...";

        try
        {
            await _exportService.ExportAsync(
                path,
                CreateSettings(),
                TimeSpan.FromMinutes(SelectedDurationMinutes),
                mp3);

            StatusMessage = $"Export completed: {path}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export error: {ex.Message}";
        }
        finally
        {
            IsExporting = false;
        }
    }

    private void ApplyLiveSettings()
    {
        if (!IsPlaying)
            return;

        _playbackService.Update(CreateSettings());
    }

    private CompositionSettings CreateSettings() => new(
        (float)DroneLevel,
        (float)AtmosphereLevel,
        (float)Brightness,
        (float)Modulation,
        _seed);

    public void Dispose()
    {
        _playbackService.Dispose();
        IsPlaying = false;
    }
}
