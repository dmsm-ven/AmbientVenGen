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

/// <summary>The pitch interval panel.</summary>
public partial class MainViewModel
{
    // ---------------------------------------------------------------- pitch interval

    public ObservableCollection<IntervalSource> IntervalSources { get; } = [];

    /// <summary>The interval table: every interval with the pitch it would give (follows the chosen source layer).</summary>
    public ObservableCollection<IntervalRow> IntervalRows { get; } = [];

    /// <summary>The second group of the table: mathematical ratios (smallest first).</summary>
    public ObservableCollection<IntervalRow> MathIntervalRows { get; } = [];

    partial void OnIntervalSourceChanged(IntervalSource? value)
    {
        IntervalRows.Clear();
        MathIntervalRows.Clear();
        if (value is null)
            return;

        static double Result(Layer layer, PitchInterval interval) =>
            Math.Round(Math.Clamp(layer.PitchHz * interval.Ratio, Layer.MinPitchHz, Layer.MaxPitchHz), 1);

        foreach (PitchInterval interval in Choices.PitchIntervals)
            IntervalRows.Add(new IntervalRow(interval, Result(value.Layer, interval)));

        foreach (PitchInterval interval in Choices.MathIntervals)
            MathIntervalRows.Add(new IntervalRow(interval, Result(value.Layer, interval)));

        RefreshCurrentInterval();
    }

    // Outlines the row whose pitch equals the pitch of the selected layer (none when it matches nothing).
    private void RefreshCurrentInterval()
    {
        double? current = SelectedLayer?.PitchHz;

        foreach (IntervalRow row in IntervalRows.Concat(MathIntervalRows))
            row.IsCurrent = current is { } hz && Math.Abs(row.Hz - hz) < 0.05;
    }

    [RelayCommand]
    private void CloseIntervalPanel() => IsIntervalPanelOpen = false;

    [ObservableProperty]
    private bool _isIntervalPanelOpen;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyIntervalCommand))]
    private IntervalSource? _intervalSource;

    // Needs a Drone / Pad selected and at least one other Drone / Pad to take the pitch from.
    private bool CanOpenIntervalPanel() =>
        SelectedLayer is { Type: LayerType.Drone or LayerType.Pad } selected
        && Project.Layers.Any(l => !ReferenceEquals(l, selected) && l.Type is LayerType.Drone or LayerType.Pad);

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

    /// <summary>Sets the pitch of the selected layer to the value of the clicked row. The panel stays open.</summary>
    [RelayCommand(CanExecute = nameof(CanApplyInterval))]
    private void ApplyInterval(IntervalRow? row)
    {
        if (row is null || SelectedLayer is not { } target || IntervalSource is not { } source)
            return;

        target.PitchHz = row.Hz;
        StatusMessage = $"{target.Name}: {row.Interval.Name} above {source.Layer.Name} = {row.Hz:0.#} Hz";
    }

    private bool CanApplyInterval(IntervalRow? row) => IntervalSource is not null && SelectedLayer is not null;

    /// <summary>Copies the selected layer (Ctrl+D); the copy is inserted right below it and selected.</summary>
}
