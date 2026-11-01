using AmbientVenGen.Core.Audio;
using AmbientVenGen.Core.Composition;
using AmbientVenGen.Core.Export;
using AmbientVenGen.Core.Projects;
using AmbientVenGen.Desktop.Audio;
using AmbientVenGen.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
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
    private readonly PresetLibraryWindowService _presetLibraryWindowService;
    private readonly SettingsWindowService _settingsWindowService;
    private readonly ProjectFileService _projectFileService;
    private readonly MessageService _messageService;
    private readonly SpectrumProcessor _spectrum = new();
    private readonly DispatcherTimer _timer;

    private readonly Stack<DeletedLayer> _deletedLayers = new();

    // Set while the library is open to choose a new file for an existing Field record layer.
    private Layer? _layerAwaitingSource;

    private ExportOptions? _lastExportOptions;
    private CancellationTokenSource? _exportCts;
    private int _clipHoldTicks;
    private bool _loading;
    private bool _disposed;

    public ProjectModel Project { get; } = new();

    /// <summary>User settings (slider or knob controls).</summary>
    public AppSettings Settings { get; }
    public NewLayerFormViewModel NewLayer { get; } = new();
    public IReadOnlyList<ProjectTemplate> Templates { get; }
    public ObservableCollection<Choice<double>> LengthOptions { get; }

    [ObservableProperty]
    private ProjectTemplate? _selectedTemplate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedLayer))]
    [NotifyPropertyChangedFor(nameof(IsLayerSelectionEmpty))]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedLayerCommand))]
    [NotifyCanExecuteChangedFor(nameof(RandomizeSelectedLayerCommand))]
    [NotifyCanExecuteChangedFor(nameof(DuplicateSelectedLayerCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenIntervalPanelCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyIntervalCommand))]
    [NotifyPropertyChangedFor(nameof(IntervalResultText))]
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

    /// <summary>Shows the "?" hint icons next to parameter captions (the toggle in the tool panel).</summary>
    [ObservableProperty]
    private bool _showHints;

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
        TemplateService templateService,
        PresetLibraryWindowService presetLibraryWindowService,
        AppSettings settings,
        SettingsWindowService settingsWindowService)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _settingsWindowService = settingsWindowService ?? throw new ArgumentNullException(nameof(settingsWindowService));
        _playbackService = playbackService ?? throw new ArgumentNullException(nameof(playbackService));
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
        _fileDialogService = fileDialogService ?? throw new ArgumentNullException(nameof(fileDialogService));
        _exportOptionsDialogService = exportOptionsDialogService ?? throw new ArgumentNullException(nameof(exportOptionsDialogService));
        _projectFileService = projectFileService ?? throw new ArgumentNullException(nameof(projectFileService));
        _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
        ArgumentNullException.ThrowIfNull(templateService);
        _presetLibraryWindowService = presetLibraryWindowService ?? throw new ArgumentNullException(nameof(presetLibraryWindowService));

        Templates = templateService.Templates;
        SelectedTemplate = templateService.Default;

        LengthOptions = new ObservableCollection<Choice<double>>(
            new[] { 3, 5, 10, 15, 20, 30, 45, 60 }
                .Select(minutes => new Choice<double>($"{minutes} min", minutes * 60.0)));

        Project.Changed += OnProjectChanged;
        _presetLibraryWindowService.WindowClosed += () => _layerAwaitingSource = null;
        WeakReferenceMessenger.Default.Register<MainViewModel, UseFieldPresetMessage>(
            this,
            static (viewModel, message) => viewModel.AddFieldRecordLayer(message.Path, message.Name));
        _playbackService.PlaybackEnded += OnPlaybackEnded;

        // Start with a ready-made composition so there is something to hear and see right away.
        LoadSpec(templateService.Default.Build(), null);
        TryOpenLastProject();

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

        if (NewLayer.Type == LayerType.FieldRecord)
        {
            // A recording layer needs a file: choose it in the preset library ("Add to project" there adds the layer).
            _layerAwaitingSource = null;
            _presetLibraryWindowService.Show();
            StatusMessage = "Choose a recording in the library and press \"Add to project\"";
            return;
        }

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

        int index = Project.Layers.IndexOf(layer);

        SelectedLayer = null;
        Project.Layers.Remove(layer);

        _deletedLayers.Push(new DeletedLayer(layer, index));
        UndoDeleteCommand.NotifyCanExecuteChanged();
        StatusMessage = $"Removed layer: {layer.Name}";
    }

    // ---------------------------------------------------------------- pitch interval

    public ObservableCollection<IntervalSource> IntervalSources { get; } = [];

    [ObservableProperty]
    private bool _isIntervalPanelOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IntervalResultText))]
    [NotifyCanExecuteChangedFor(nameof(ApplyIntervalCommand))]
    private IntervalSource? _intervalSource;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IntervalResultText))]
    private PitchInterval _selectedInterval = Choices.PitchIntervals[2];

    private bool CanOpenIntervalPanel() => SelectedLayer is { Type: LayerType.Drone or LayerType.Pad };

    /// <summary>The pitch the layer would get: the source pitch times the interval ratio, inside the allowed range.</summary>
    private double? IntervalResult => IntervalSource is null
        ? null
        : Math.Round(Math.Clamp(IntervalSource.Layer.PitchHz * SelectedInterval.Ratio, Layer.MinPitchHz, Layer.MaxPitchHz), 1);

    public string IntervalResultText => IntervalResult is { } hz
        ? $"{(SelectedLayer?.PitchHz ?? 0):0.#} Hz \u2192 {hz:0.#} Hz"
        : "No layer with a pitch to take as the source";

    /// <summary>Opens the mini panel; the source is the layer below the selected one by default.</summary>
    [RelayCommand(CanExecute = nameof(CanOpenIntervalPanel))]
    private void OpenIntervalPanel()
    {
        if (SelectedLayer is not { } target)
            return;

        IntervalSources.Clear();
        foreach (Layer layer in Project.Layers.Where(l => !ReferenceEquals(l, target) && l.Type is LayerType.Drone or LayerType.Pad))
            IntervalSources.Add(new IntervalSource(layer, $"{layer.Name}  ({layer.PitchHz:0.#} Hz)"));

        int index = Project.Layers.IndexOf(target);
        IntervalSource = IntervalSources.FirstOrDefault(s => Project.Layers.IndexOf(s.Layer) > index) ?? IntervalSources.LastOrDefault();
        IsIntervalPanelOpen = true;
    }

    [RelayCommand(CanExecute = nameof(CanApplyInterval))]
    private void ApplyInterval()
    {
        if (SelectedLayer is not { } target || IntervalResult is not { } hz)
            return;

        target.PitchHz = hz;
        IsIntervalPanelOpen = false;
        StatusMessage = $"{target.Name}: {SelectedInterval.Name} above {IntervalSource!.Layer.Name} = {hz:0.#} Hz";
    }

    private bool CanApplyInterval() => IntervalSource is not null && SelectedLayer is not null;

    /// <summary>Copies the selected layer (Ctrl+D); the copy is inserted right below it and selected.</summary>
    [RelayCommand(CanExecute = nameof(HasSelectedLayer))]
    private void DuplicateSelectedLayer()
    {
        if (SelectedLayer is not { } source)
            return;

        Layer copy = Layer.FromSpec(source.ToSpec());
        copy.Name = $"{source.Name} copy";
        copy.Seed = Random.Shared.Next(1, int.MaxValue);

        int index = Project.Layers.IndexOf(source);
        Project.Layers.Insert(index < 0 ? Project.Layers.Count : index + 1, copy);
        SelectedLayer = copy;
        StatusMessage = $"Duplicated layer: {copy.Name}";
    }

    private bool CanUndoDelete() => _deletedLayers.Count > 0;

    /// <summary>Brings back the layer removed last (repeat to go further back), at its old place.</summary>
    [RelayCommand(CanExecute = nameof(CanUndoDelete))]
    private void UndoDelete()
    {
        if (!_deletedLayers.TryPop(out DeletedLayer? deleted))
            return;

        Project.Layers.Insert(Math.Clamp(deleted.Index, 0, Project.Layers.Count), deleted.Layer);
        SelectedLayer = deleted.Layer;
        UndoDeleteCommand.NotifyCanExecuteChanged();
        StatusMessage = $"Restored layer: {deleted.Layer.Name}";
    }

    /// <summary>New random sound settings for the selected layer (name, position, length and file stay).</summary>
    [RelayCommand(CanExecute = nameof(HasSelectedLayer))]
    private void RandomizeSelectedLayer()
    {
        if (SelectedLayer is not { } layer)
            return;

        LayerDefaults.Randomize(layer, Random.Shared);
        StatusMessage = $"Randomized layer: {layer.Name}";
    }

    /// <summary>Adds a recording (from the preset library) as a new Field record layer.</summary>
    private void AddFieldRecordLayer(string path, string name)
    {
        // The library was opened from "Change file": the recording replaces the file of that layer.
        if (_layerAwaitingSource is { } waiting)
        {
            _layerAwaitingSource = null;

            if (Project.Layers.Contains(waiting))
            {
                waiting.SourcePath = path;
                SelectedLayer = waiting;
                StatusMessage = $"{waiting.Name}: {Path.GetFileName(path)}";
                return;
            }
        }

        double total = Project.DurationSeconds;

        Layer layer = LayerDefaults.Create(LayerType.FieldRecord, name);
        layer.SourcePath = path;
        layer.DurationSeconds = Math.Clamp(180, MinLayerSeconds, total);
        layer.FadeInSeconds = layer.FadeOutSeconds = Math.Min(30, layer.DurationSeconds / 2);

        Project.Layers.Add(layer);
        SelectedLayer = layer;
        StatusMessage = $"Added layer: {layer.Name}";
    }

    /// <summary>Lets the user pick another audio file for the selected Field record layer.</summary>
    [RelayCommand]
    private void ChangeSource()
    {
        if (SelectedLayer is not { Type: LayerType.FieldRecord } layer)
            return;

        _layerAwaitingSource = layer;
        _presetLibraryWindowService.Show();
        StatusMessage = "Choose a recording in the library and press \"Add to project\" to use it for this layer";
    }

    [RelayCommand]
    private void ApplyTemplate()
    {
        ProjectTemplate? template = SelectedTemplate;
        if (template is null)
            return;

        if (Project.Layers.Count > 0
            && !_messageService.Confirm(
                $"Replace the current layers with the \"{template.Name}\" template?",
                "Replace",
                "Cancel"))
        {
            return;
        }

        ProjectSpec spec = template.Build();

        StopPlaybackSilently();
        SelectedLayer = null;
        ClearDeleted();
        EnsureLengthOption(spec.DurationSeconds);
        Project.Load(spec);
        PlayheadSeconds = 0;

        StatusMessage = $"Template applied: {template.Name}";
    }

    // Layers of a replaced project cannot be restored into the new one.
    private void ClearDeleted()
    {
        _deletedLayers.Clear();
        UndoDeleteCommand.NotifyCanExecuteChanged();
    }

    private void OnProjectChanged()
    {
        // Start decoding recordings as soon as a layer uses them, so playback does not wait.
        foreach (Layer layer in Project.Layers)
        {
            if (layer.Type == LayerType.FieldRecord && !layer.Muted && !string.IsNullOrEmpty(layer.SourcePath))
                SampleLibrary.Request(layer.SourcePath);
        }

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
            Settings.LastProjectPath = path;
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

    /// <summary>Opens a project given by path (started from Explorer by a double click). Errors are shown to the user.</summary>
    public void OpenProjectFile(string path)
    {
        try
        {
            LoadSpec(_projectFileService.Load(path), path);
            StatusMessage = $"Opened {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            _messageService.ShowError($"Cannot open the project:\n{ex.Message}");
        }
    }

    // Reopens the project that was used last time; a missing or broken file silently leaves the default composition.
    private void TryOpenLastProject()
    {
        string? path = Settings.LastProjectPath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return;

        try
        {
            LoadSpec(_projectFileService.Load(path), path);
            StatusMessage = $"Opened {Path.GetFileName(path)}";
        }
        catch (Exception)
        {
            // Keep the default composition.
        }
    }

    private void LoadSpec(ProjectSpec spec, string? path)
    {
        StopPlaybackSilently();
        SelectedLayer = null;
        ClearDeleted();
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
        if (path is not null)
            Settings.LastProjectPath = path;
        OnPropertyChanged(nameof(TimeText));
    }

    // ---------------------------------------------------------------- preset library

    [RelayCommand]
    private void OpenSettings() => _settingsWindowService.Show();

    [RelayCommand]
    private void OpenPresetLibrary() => _presetLibraryWindowService.Show();

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

/// <summary>A removed layer and the position it had, for "undo delete".</summary>
internal sealed record DeletedLayer(Layer Layer, int Index);
