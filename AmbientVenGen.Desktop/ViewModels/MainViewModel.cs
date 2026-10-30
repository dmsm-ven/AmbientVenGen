using AmbientVenGen.Desktop.Audio;
using AmbientVenGen.Desktop.Composition;
using AmbientVenGen.Desktop.Models;
using AmbientVenGen.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace AmbientVenGen.Desktop.ViewModels;

/// <summary>
/// Coordinates the project, playback, export, files and the live meters.
/// The project itself (layers and master settings) lives in <see cref="Project"/>.
/// </summary>
public partial class MainViewModel : ObservableObject, IDisposable
{
    private const double MinLayerSeconds = 5;

    private readonly AudioPlaybackService _playbackService;
    private readonly AudioExportService _exportService;
    private readonly FileDialogService _fileDialogService;
    private readonly ExportOptionsDialogService _exportOptionsDialogService;
    private readonly ProjectFileService _projectFileService;
    private readonly MessageService _messageService;
    private readonly SpectrumProcessor _spectrum = new();
    private readonly DispatcherTimer _timer;

    private ExportOptions? _lastExportOptions;
    private CancellationTokenSource? _exportCts;
    private int _clipHoldTicks;
    private bool _loading;
    private bool _disposed;

    public ProjectModel Project { get; } = new();
    public NewLayerFormViewModel NewLayer { get; } = new();
    public IReadOnlyList<ProjectTemplate> Templates { get; }
    public ObservableCollection<Choice<double>> LengthOptions { get; }

    [ObservableProperty]
    private ProjectTemplate? _selectedTemplate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedLayer))]
    [NotifyPropertyChangedFor(nameof(IsLayerSelectionEmpty))]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedLayerCommand))]
    private Layer? _selectedLayer;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private bool _isExporting;

    [ObservableProperty]
    private double _exportProgress;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private double _playheadSeconds;

    [ObservableProperty]
    private double _levelLeft;

    [ObservableProperty]
    private double _levelRight;

    [ObservableProperty]
    private bool _isClipping;

    [ObservableProperty]
    private double[]? _spectrumBands;

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private string? _currentFilePath;

    public bool HasSelectedLayer => SelectedLayer is not null;
    public bool IsLayerSelectionEmpty => SelectedLayer is null;

    public string ProjectName => CurrentFilePath is null ? "Untitled" : Path.GetFileNameWithoutExtension(CurrentFilePath);
    public string WindowTitle => $"Ambient Forge - {ProjectName}{(IsDirty ? " *" : string.Empty)}";
    public string TimeText => $"{FormatTime(PlayheadSeconds)} / {FormatTime(Project.DurationSeconds)}";

    public MainViewModel(
        AudioPlaybackService playbackService,
        AudioExportService exportService,
        FileDialogService fileDialogService,
        ExportOptionsDialogService exportOptionsDialogService,
        ProjectFileService projectFileService,
        MessageService messageService,
        TemplateService templateService)
    {
        _playbackService = playbackService ?? throw new ArgumentNullException(nameof(playbackService));
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
        _fileDialogService = fileDialogService ?? throw new ArgumentNullException(nameof(fileDialogService));
        _exportOptionsDialogService = exportOptionsDialogService ?? throw new ArgumentNullException(nameof(exportOptionsDialogService));
        _projectFileService = projectFileService ?? throw new ArgumentNullException(nameof(projectFileService));
        _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
        ArgumentNullException.ThrowIfNull(templateService);

        Templates = templateService.Templates;
        SelectedTemplate = templateService.Default;

        LengthOptions = new ObservableCollection<Choice<double>>(
            new[] { 3, 5, 10, 15, 20, 30, 45, 60 }
                .Select(minutes => new Choice<double>($"{minutes} min", minutes * 60.0)));

        Project.Changed += OnProjectChanged;
        _playbackService.PlaybackEnded += OnPlaybackEnded;

        // Start with a ready-made composition so there is something to hear and see right away.
        LoadSpec(templateService.Default.Build(), null);

        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += OnTimerTick;
        _timer.Start();
    }

    partial void OnIsExportingChanged(bool value) => ExportCommand.NotifyCanExecuteChanged();
    partial void OnIsDirtyChanged(bool value) => OnPropertyChanged(nameof(WindowTitle));

    partial void OnCurrentFilePathChanged(string? value)
    {
        OnPropertyChanged(nameof(ProjectName));
        OnPropertyChanged(nameof(WindowTitle));
    }

    partial void OnPlayheadSecondsChanged(double value) => OnPropertyChanged(nameof(TimeText));

    // ---------------------------------------------------------------- playback

    /// <summary>Single Play/Stop button: the icon in the view follows <see cref="IsPlaying"/>.</summary>
    [RelayCommand]
    private void TogglePlay()
    {
        if (IsPlaying)
            StopPlayback();
        else
            StartPlayback();
    }

    private void StartPlayback()
    {
        try
        {
            double start = PlayheadSeconds >= Project.DurationSeconds - 0.05 ? 0 : PlayheadSeconds;
            PlayheadSeconds = start;

            _playbackService.Start(Project.ToSpec(), start);
            IsPlaying = true;
            StatusMessage = $"Playing - seed {Project.Seed}";
        }
        catch (Exception ex)
        {
            IsPlaying = false;
            StatusMessage = $"Playback error: {ex.Message}";
        }
    }

    private void StopPlayback()
    {
        StopPlaybackSilently();
        StatusMessage = "Stopped";
    }

    // Stops playback and keeps the playhead where it was (works like a pause).
    private void StopPlaybackSilently()
    {
        if (IsPlaying)
            PlayheadSeconds = Math.Min(_playbackService.PositionSeconds, Project.DurationSeconds);

        _playbackService.Stop();
        IsPlaying = false;
    }

    private void OnPlaybackEnded()
    {
        IsPlaying = false;
        PlayheadSeconds = Project.DurationSeconds;
        StatusMessage = "Finished";
    }

    [RelayCommand]
    private void Seek(double seconds)
    {
        PlayheadSeconds = Math.Clamp(seconds, 0, Project.DurationSeconds);

        if (IsPlaying)
            _playbackService.Seek(PlayheadSeconds);
    }

    [RelayCommand]
    private void Regenerate()
    {
        Project.Seed = Random.Shared.Next(1, int.MaxValue);

        foreach (Layer layer in Project.Layers)
            layer.Seed = Random.Shared.Next(1, int.MaxValue);

        if (IsPlaying)
        {
            PlayheadSeconds = _playbackService.PositionSeconds;
            StartPlayback();
            return;
        }

        StatusMessage = $"New composition seed: {Project.Seed}";
    }

    // ---------------------------------------------------------------- layers and templates

    [RelayCommand]
    private void AddLayer()
    {
        double total = Project.DurationSeconds;
        int number = Project.Layers.Count(layer => layer.Type == NewLayer.Type) + 1;

        Layer created = NewLayer.CreateLayer($"{NewLayer.Type} {number}");
        created.DurationSeconds = Math.Clamp(created.DurationSeconds, MinLayerSeconds, total);
        created.StartSeconds = Math.Clamp(created.StartSeconds, 0, Math.Max(0, total - created.DurationSeconds));

        Project.Layers.Add(created);
        SelectedLayer = created;
        StatusMessage = $"Added layer: {created.Name}";
    }

    [RelayCommand(CanExecute = nameof(HasSelectedLayer))]
    private void DeleteSelectedLayer()
    {
        Layer? layer = SelectedLayer;
        if (layer is null)
            return;

        SelectedLayer = null;
        Project.Layers.Remove(layer);
        StatusMessage = $"Removed layer: {layer.Name}";
    }

    [RelayCommand]
    private void ApplyTemplate()
    {
        ProjectTemplate? template = SelectedTemplate;
        if (template is null)
            return;

        if (Project.Layers.Count > 0
            && !_messageService.Confirm($"Replace the current layers with the \"{template.Name}\" template?"))
        {
            return;
        }

        ProjectSpec spec = template.Build();

        StopPlaybackSilently();
        SelectedLayer = null;
        EnsureLengthOption(spec.DurationSeconds);
        Project.Load(spec);
        PlayheadSeconds = 0;

        StatusMessage = $"Template applied: {template.Name}";
    }

    private void OnProjectChanged()
    {
        if (!_loading)
            IsDirty = true;

        if (PlayheadSeconds > Project.DurationSeconds)
            PlayheadSeconds = Project.DurationSeconds;

        OnPropertyChanged(nameof(TimeText));

        if (IsPlaying)
            _playbackService.Update(Project.ToSpec());
    }

    // Makes sure the "Length" ComboBox has an entry for the given length (e.g. from a loaded file).
    private void EnsureLengthOption(double seconds)
    {
        if (LengthOptions.Any(option => Math.Abs(option.Value - seconds) < 1e-6))
            return;

        int index = 0;
        while (index < LengthOptions.Count && LengthOptions[index].Value < seconds)
            index++;

        LengthOptions.Insert(index, new Choice<double>($"{seconds / 60.0:0.#} min", seconds));
    }

    // ---------------------------------------------------------------- project files

    [RelayCommand]
    private void NewProject()
    {
        if (!ConfirmDiscardChanges())
            return;

        LoadSpec(ProjectSpec.Empty with { Seed = Random.Shared.Next(1, int.MaxValue) }, null);
        StatusMessage = "New project";
    }

    [RelayCommand]
    private void OpenProject()
    {
        if (!ConfirmDiscardChanges())
            return;

        string? path = _fileDialogService.ChooseProjectToOpen();
        if (path is null)
            return;

        try
        {
            ProjectSpec spec = _projectFileService.Load(path);
            LoadSpec(spec, path);
            StatusMessage = $"Opened {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            _messageService.ShowError($"Cannot open the project:\n{ex.Message}");
        }
    }

    [RelayCommand]
    private void SaveProject() => TrySave(saveAs: false);

    [RelayCommand]
    private void SaveProjectAs() => TrySave(saveAs: true);

    private bool TrySave(bool saveAs)
    {
        string? path = CurrentFilePath;

        if (saveAs || path is null)
        {
            path = _fileDialogService.ChooseProjectSavePath(ProjectName);
            if (path is null)
                return false;
        }

        try
        {
            _projectFileService.Save(path, Project.ToSpec());
            CurrentFilePath = path;
            IsDirty = false;
            StatusMessage = $"Saved {Path.GetFileName(path)}";
            return true;
        }
        catch (Exception ex)
        {
            _messageService.ShowError($"Cannot save the project:\n{ex.Message}");
            return false;
        }
    }

    /// <summary>Asks to save unsaved changes. Returns false if the user cancels.</summary>
    public bool ConfirmDiscardChanges()
    {
        if (!IsDirty)
            return true;

        return _messageService.AskSaveChanges(ProjectName) switch
        {
            MessageBoxResult.Yes => TrySave(saveAs: false),
            MessageBoxResult.No => true,
            _ => false
        };
    }

    private void LoadSpec(ProjectSpec spec, string? path)
    {
        StopPlaybackSilently();
        SelectedLayer = null;
        EnsureLengthOption(spec.DurationSeconds);

        _loading = true;
        try
        {
            Project.Load(spec);
        }
        finally
        {
            _loading = false;
        }

        PlayheadSeconds = 0;
        CurrentFilePath = path;
        IsDirty = false;
        OnPropertyChanged(nameof(TimeText));
    }

    // ---------------------------------------------------------------- export

    private bool CanExport() => !IsExporting;

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        if (Project.Layers.Count == 0)
        {
            StatusMessage = "Nothing to export: add at least one layer";
            return;
        }

        ExportOptions? options = _exportOptionsDialogService.Show(
            FormatTime(Project.DurationSeconds),
            _lastExportOptions);

        if (options is null)
            return;

        _lastExportOptions = options;

        string? path = _fileDialogService.ChooseExportPath(options.Format == ExportFormat.Mp3, Project.Seed);
        if (string.IsNullOrWhiteSpace(path))
            return;

        using var cancellation = new CancellationTokenSource();
        _exportCts = cancellation;

        ExportProgress = 0;
        IsExporting = true;
        StatusMessage = "Rendering composition...";

        var progress = new Progress<double>(value =>
        {
            if (IsExporting)
                ExportProgress = value;
        });

        try
        {
            await _exportService.ExportAsync(path, Project.ToSpec(), options, progress, cancellation.Token);
            StatusMessage = $"Export completed: {path}";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Export canceled";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export error: {ex.Message}";
        }
        finally
        {
            _exportCts = null;
            IsExporting = false;
            ExportProgress = 0;
        }
    }

    [RelayCommand]
    private void CancelExport() => _exportCts?.Cancel();

    // ---------------------------------------------------------------- live meters (30 fps)

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (IsPlaying)
            PlayheadSeconds = Math.Min(_playbackService.PositionSeconds, Project.DurationSeconds);

        _playbackService.Analyzer.TakePeaks(out float left, out float right, out float over);

        LevelLeft = Fall(left, LevelLeft);
        LevelRight = Fall(right, LevelRight);

        if (over > 1.0f)
            _clipHoldTicks = 45;
        else if (_clipHoldTicks > 0)
            _clipHoldTicks--;

        IsClipping = _clipHoldTicks > 0;
        SpectrumBands = _spectrum.Update(_playbackService.Analyzer, IsPlaying);
    }

    // Peak meter ballistics: jumps up instantly, falls back smoothly.
    private static double Fall(double newPeak, double current)
    {
        double value = Math.Max(newPeak, current * 0.88);
        return value < 1e-5 ? 0 : value;
    }

    private static string FormatTime(double seconds)
    {
        int total = (int)Math.Round(Math.Max(0, seconds));

        return total >= 3600
            ? $"{total / 3600}:{total % 3600 / 60:00}:{total % 60:00}"
            : $"{total / 60}:{total % 60:00}";
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _timer.Stop();
        _timer.Tick -= OnTimerTick;
        Project.Changed -= OnProjectChanged;
        _playbackService.PlaybackEnded -= OnPlaybackEnded;

        _exportCts?.Cancel();
        _playbackService.Stop();
    }
}
