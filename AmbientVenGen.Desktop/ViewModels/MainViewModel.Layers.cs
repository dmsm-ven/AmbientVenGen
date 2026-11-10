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

/// <summary>Layers: add, delete, duplicate, undo, randomize, source and templates.</summary>
public partial class MainViewModel
{
    // ---------------------------------------------------------------- layers and templates

    [RelayCommand]
    private void AddLayer()
    {
        double total = Project.DurationSeconds;
        int number = Project.Layers.Count(layer => layer.Type == NewLayer.Type) + 1;

        if (NewLayer.Type == LayerType.FieldRecord)
        {
            // A recording layer needs a file: choose it in the preset library ("Add new Layer" there adds the layer).
            _presetLibraryWindowService.Show();
            StatusMessage = "Choose a recording in the library and press \"Add new Layer\"";
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

    /// <summary>Back to the sound settings of a newly created layer (name, mute, position, length, repeats and file stay).</summary>
    [RelayCommand(CanExecute = nameof(HasSelectedLayer))]
    private void ClearSelectedLayer()
    {
        if (SelectedLayer is not { } layer)
            return;

        LayerDefaults.Reset(layer);
        StatusMessage = $"Cleared layer: {layer.Name}";
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
    private void AddFieldRecordLayer(string path, string name, double fileSeconds)
    {
        double total = Project.DurationSeconds;

        Layer layer = LayerDefaults.Create(LayerType.FieldRecord, name);
        layer.SourcePath = path;

        // The layer is as long as the recording itself (not longer than the project); the whole file is looped.
        double length = fileSeconds > 0 ? fileSeconds : 180;
        layer.LoopDuration = fileSeconds > 0 ? fileSeconds : 0;
        layer.DurationSeconds = Math.Clamp(length, MinLayerSeconds, total);
        layer.FadeInSeconds = layer.FadeOutSeconds = Math.Min(30, layer.DurationSeconds / 2);

        Project.Layers.Add(layer);
        SelectedLayer = layer;
        StatusMessage = $"Added layer: {layer.Name}";
    }

    /// <summary>The name of the selected layer when it is a Field record layer (the library can replace its file), else null.</summary>
    private string? FieldRecordTargetName => SelectedLayer is { Type: LayerType.FieldRecord } layer ? layer.Name : null;

    /// <summary>Puts another recording (from the preset library) into the selected Field record layer.</summary>
    private void ReplaceFieldRecordSource(string path, string name)
    {
        if (SelectedLayer is not { Type: LayerType.FieldRecord } layer)
        {
            StatusMessage = "Select a Field record layer first";
            return;
        }

        // A layer that carries the name of its old file (the default for a recording) follows the new file;
        // a name the user typed himself is kept.
        string? oldPath = layer.SourcePath;
        bool autoNamed = !string.IsNullOrEmpty(oldPath)
            && (string.Equals(layer.Name, Path.GetFileNameWithoutExtension(oldPath), StringComparison.OrdinalIgnoreCase)
                || string.Equals(layer.Name, Path.GetFileName(oldPath), StringComparison.OrdinalIgnoreCase));

        layer.SourcePath = path;
        layer.LoopStart = 0;
        layer.LoopDuration = 0; // the whole new file; the real length is filled in when it is known
        if (autoNamed)
            layer.Name = name;

        StatusMessage = $"{layer.Name}: {Path.GetFileName(path)}";
    }

    /// <summary>Lets the user pick another audio file for the selected Field record layer.</summary>
    [RelayCommand]
    private void ChangeSource()
    {
        if (SelectedLayer is not { Type: LayerType.FieldRecord } layer)
            return;

        _presetLibraryWindowService.Show();
        StatusMessage = "Choose a recording in the library and press \"Replace in current Layer\"";
    }

    // Layers of a replaced project cannot be restored into the new one.
    private void ClearDeleted()
    {
        _deletedLayers.Clear();
        UndoDeleteCommand.NotifyCanExecuteChanged();
    }

    /// <summary>True while the stretched / transposed sound of the selected Field record layer is being calculated.</summary>
    [ObservableProperty]
    private bool _isSelectedSourceProcessing;

    /// <summary>Stretch / pitch results are being calculated: shown as a progress bar in the status bar.</summary>
    [ObservableProperty]
    private bool _isRebuilding;

    [ObservableProperty]
    private double _rebuildProgress;

    [ObservableProperty]
    private string _rebuildText = "";

    private System.Windows.Threading.DispatcherTimer? _rebuildTimer;

    private void UpdateRebuild()
    {
        (int jobs, double progress) = SampleLibrary.GetProcessing();
        IsRebuilding = jobs > 0;
        RebuildProgress = progress;
        RebuildText = jobs > 1 ? $"Processing recordings ({jobs})  {progress:P0}" : $"Processing recording  {progress:P0}";

        // The progress changes inside the calculation, so it is polled while there is work.
        if (jobs > 0 && _rebuildTimer is null)
        {
            _rebuildTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _rebuildTimer.Tick += (_, _) => UpdateRebuild();
            _rebuildTimer.Start();
        }
        else if (jobs == 0 && _rebuildTimer is not null)
        {
            _rebuildTimer.Stop();
            _rebuildTimer = null;
        }
    }

    private void RefreshProcessing()
    {
        OnPropertyChanged(nameof(SelectedLayerTimeText));
        UpdateRebuild();
        RefreshSelectedProcessing();
    }

    /// <summary>Length of the selected recording (for the trim sliders); at least 1 s.</summary>
    [ObservableProperty]
    private double _selectedSourceSeconds = 1;

    /// <summary>"Piece 1:32 of 5:00" for the selected recording.</summary>
    [ObservableProperty]
    private string _selectedPieceText = "";

    private void RefreshSelectedProcessing()
    {
        IsSelectedSourceProcessing = SelectedLayer is { Type: LayerType.FieldRecord, SourcePath: { Length: > 0 } path } layer
            && SampleLibrary.IsPending(path, layer.Transform);

        RefreshPiece();
    }

    private void RefreshPiece()
    {
        if (SelectedLayer is not { Type: LayerType.FieldRecord, SourcePath: { Length: > 0 } path } layer)
        {
            SelectedPieceText = "";
            return;
        }

        DecodedSample? sample = SampleLibrary.TryGet(path);

        if (sample is null)
        {
            // not decoded yet: ask again when it is
            SampleLibrary.Request(path, SampleTransform.None).ContinueWith(
                _ => System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(RefreshPiece)),
                TaskScheduler.Default);
            SelectedPieceText = "";
            return;
        }

        double total = sample.Frames / (double)ProjectMixer.SampleRate;
        SelectedSourceSeconds = Math.Max(1, total);

        // "0" means "up to the end of the file": show (and keep) the real length, without making the project "changed".
        if (layer.LoopDuration <= 0)
        {
            bool wasDirty = IsDirty;
            layer.LoopDuration = Math.Round(Math.Max(SampleTransform.MinPieceSeconds, total - layer.LoopStart), 3);

            if (!wasDirty)
            {
                _savedSnapshot = TakeSnapshot();
                IsDirty = false;
            }
        }

        (int begin, int end) = SampleTransform.PieceOf(sample.Frames, layer.LoopStart, layer.LoopDuration, ProjectMixer.SampleRate);
        SelectedPieceText = $"Loop {FormatClock(begin / (double)ProjectMixer.SampleRate)} - {FormatClock(end / (double)ProjectMixer.SampleRate)} of {FormatClock(total)}";
    }

    private static string FormatClock(double seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Round(seconds));
        return span.TotalHours >= 1 ? span.ToString(@"h\:mm\:ss") : span.ToString(@"m\:ss");
    }

    private void OnProjectChanged()
    {
        OpenIntervalPanelCommand.NotifyCanExecuteChanged();

        if (IsIntervalPanelOpen)
            RefreshCurrentInterval();

        // Start decoding recordings as soon as a layer uses them, so playback does not wait.
        foreach (Layer layer in Project.Layers)
        {
            if (layer.Type == LayerType.FieldRecord && !layer.Muted && !string.IsNullOrEmpty(layer.SourcePath))
                SampleLibrary.Request(layer.SourcePath, layer.Transform);
        }

        RefreshProcessing();

        // Spectrograms: only layers whose sound changed are analysed again, 1.5 s after the last change.
        if (ShowSpectrogram)
            Spectrograms.Update(Project.Layers, Project.Seed);

        if (!_loading)
            RefreshDirty();

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
}
