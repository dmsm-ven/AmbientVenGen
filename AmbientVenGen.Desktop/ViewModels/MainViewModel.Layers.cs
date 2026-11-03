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
                // A layer that carries the name of its old file (the default for a recording) follows the new file;
                // a name the user typed himself is kept.
                string? oldPath = waiting.SourcePath;
                bool autoNamed = !string.IsNullOrEmpty(oldPath)
                    && (string.Equals(waiting.Name, Path.GetFileNameWithoutExtension(oldPath), StringComparison.OrdinalIgnoreCase)
                        || string.Equals(waiting.Name, Path.GetFileName(oldPath), StringComparison.OrdinalIgnoreCase));

                waiting.SourcePath = path;
                if (autoNamed)
                    waiting.Name = name;

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
        OpenIntervalPanelCommand.NotifyCanExecuteChanged();

        if (IsIntervalPanelOpen)
            RefreshCurrentInterval();

        // Start decoding recordings as soon as a layer uses them, so playback does not wait.
        foreach (Layer layer in Project.Layers)
        {
            if (layer.Type == LayerType.FieldRecord && !layer.Muted && !string.IsNullOrEmpty(layer.SourcePath))
                SampleLibrary.Request(layer.SourcePath);
        }

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
