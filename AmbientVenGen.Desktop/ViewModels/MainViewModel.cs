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

    private ExportOptions? _lastExportOptions;
    private CancellationTokenSource? _exportCts;
    private int _clipHoldTicks;
    private bool _loading;

    // The project as it was when it was opened or saved: "unsaved changes" means "different from this snapshot",
    // so a change that was undone by hand (or a load that only normalises values) does not count.
    private string? _savedSnapshot;

    private string TakeSnapshot() => System.Text.Json.JsonSerializer.Serialize(Project.ToSpec());

    private void RefreshDirty() => IsDirty = _savedSnapshot is null || TakeSnapshot() != _savedSnapshot;
    private bool _disposed;

    public ProjectModel Project { get; } = new();

    /// <summary>Spectrogram pictures of the layers for the timeline (analysed in the background).</summary>
    public SpectrogramService Spectrograms { get; }

    /// <summary>Draw the spectrograms inside the clips (the button next to Play); remembered in the settings.</summary>
    [ObservableProperty]
    private bool _showSpectrogram;

    partial void OnShowSpectrogramChanged(bool value)
    {
        Settings.ShowSpectrogram = value;

        if (value)
            Spectrograms.Update(Project.Layers, Project.Seed);
        else
            Spectrograms.CancelPending();
    }

    [RelayCommand]
    private void ToggleSpectrogram() => ShowSpectrogram = !ShowSpectrogram;

    /// <summary>User settings (slider or knob controls).</summary>
    public AppSettings Settings { get; }
    public NewLayerFormViewModel NewLayer { get; } = new();
    public ObservableCollection<Choice<double>> LengthOptions { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedLayer))]
    [NotifyPropertyChangedFor(nameof(IsLayerSelectionEmpty))]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedLayerCommand))]
    [NotifyCanExecuteChangedFor(nameof(RandomizeSelectedLayerCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearSelectedLayerCommand))]
    [NotifyCanExecuteChangedFor(nameof(DuplicateSelectedLayerCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenIntervalPanelCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyIntervalCommand))]
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
        SettingsWindowService settingsWindowService,
        SpectrogramService spectrograms)
    {
        Spectrograms = spectrograms ?? throw new ArgumentNullException(nameof(spectrograms));

        // The stretched / transposed sounds are calculated in the background: the layer panel shows when it is done.
        SampleLibrary.StateChanged += () => Application.Current?.Dispatcher.InvokeAsync(RefreshProcessing);
        _showSpectrogram = settings.ShowSpectrogram;
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


        LengthOptions = new ObservableCollection<Choice<double>>(
            new[] { 3, 5, 10, 15, 20, 30, 45, 60 }
                .Select(minutes => new Choice<double>($"{minutes} min", minutes * 60.0)));

        Project.Changed += OnProjectChanged;
        WeakReferenceMessenger.Default.Register<MainViewModel, UseFieldPresetMessage>(
            this,
            static (viewModel, message) =>
            {
                if (message.ReplaceCurrent)
                    viewModel.ReplaceFieldRecordSource(message.Path, message.Name);
                else
                    viewModel.AddFieldRecordLayer(message.Path, message.Name);
            });
        WeakReferenceMessenger.Default.Register<MainViewModel, FieldRecordTargetRequest>(
            this,
            static (viewModel, request) => request.Reply(viewModel.FieldRecordTargetName));
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
