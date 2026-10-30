using AmbientVenGen.Desktop.Audio;
using AmbientVenGen.Desktop.Models;
using AmbientVenGen.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AmbientVenGen.Desktop.ViewModels;

/// <summary>
/// Coordinates playback and export commands. Composition parameters live in Settings.
/// </summary>
public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly AudioPlaybackService _playbackService;
    private readonly AudioExportService _exportService;
    private readonly FileDialogService _fileDialogService;
    private readonly ExportOptionsDialogService _exportOptionsDialogService;
    private ExportOptions? _lastExportOptions;
    private int _seed = 12345;
    private bool _disposed;

    public CompositionSettingsViewModel Settings { get; }

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private bool _isExporting;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    public MainViewModel(
        CompositionSettingsViewModel settings,
        AudioPlaybackService playbackService,
        AudioExportService exportService,
        FileDialogService fileDialogService,
        ExportOptionsDialogService exportOptionsDialogService)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _playbackService = playbackService ?? throw new ArgumentNullException(nameof(playbackService));
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
        _fileDialogService = fileDialogService ?? throw new ArgumentNullException(nameof(fileDialogService));
        _exportOptionsDialogService = exportOptionsDialogService ?? throw new ArgumentNullException(nameof(exportOptionsDialogService));

        Settings.SettingsChanged += OnSettingsChanged;
        Settings.PropertyChanged += OnSettingsPropertyChanged;
    }

    partial void OnIsExportingChanged(bool value) => ExportCommand.NotifyCanExecuteChanged();

    /// <summary>Single Play/Stop button: the icon in the view follows <see cref="IsPlaying"/>.</summary>
    [RelayCommand]
    private void TogglePlay()
    {
        if (IsPlaying)
            Stop();
        else
            Play();
    }

    private void Play()
    {
        try
        {
            _playbackService.Start(Settings.CreateSettings(_seed));
            IsPlaying = true;
            StatusMessage = $"Playing · Seed: {_seed}";
        }
        catch (Exception ex)
        {
            IsPlaying = false;
            StatusMessage = $"Playback error: {ex.Message}";
        }
    }

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

    private bool CanExport() => !IsExporting;

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        ExportOptions? options = _exportOptionsDialogService.Show(
            Settings.SelectedDurationMinutes,
            _lastExportOptions);

        if (options is null)
            return;

        _lastExportOptions = options;

        string? path = _fileDialogService.ChooseExportPath(options.Format == ExportFormat.Mp3, _seed);
        if (string.IsNullOrWhiteSpace(path))
            return;

        IsExporting = true;
        StatusMessage = "Rendering composition...";

        try
        {
            await _exportService.ExportAsync(
                path,
                Settings.CreateSettings(_seed),
                TimeSpan.FromMinutes(Settings.SelectedDurationMinutes),
                options);

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

    private void OnSettingsChanged()
    {
        if (IsPlaying)
            _playbackService.Update(Settings.CreateSettings(_seed));
    }

    private void OnSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CompositionSettingsViewModel.SelectedPreset) && Settings.SelectedPreset is not null)
            StatusMessage = $"Preset selected: {Settings.SelectedPreset.Name}";
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Settings.SettingsChanged -= OnSettingsChanged;
        Settings.PropertyChanged -= OnSettingsPropertyChanged;
        _playbackService.Stop();
        IsPlaying = false;
    }
}
