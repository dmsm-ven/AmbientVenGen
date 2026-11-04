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
        foreach (IntervalLayerItem item in IntervalLayers)
            item.RefreshText();

        if (IsChordMode)
        {
            RebuildChordRows();
            return;
        }

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

    /// <summary>The layers of the panel: the selected one and the other Drone / Pad layers with checkboxes.</summary>
    public ObservableCollection<IntervalLayerItem> IntervalLayers { get; } = [];

    /// <summary>Three-layer relations (shown when two other layers are ticked).</summary>
    public ObservableCollection<ChordRow> ChordRows { get; } = [];

    /// <summary>One layer ticked: the classic table (the pitch is taken from it).</summary>
    [ObservableProperty]
    private bool _isSingleMode;

    /// <summary>Two layers ticked: with the selected one they make three, related as a chord around the root.</summary>
    [ObservableProperty]
    private bool _isChordMode;

    private bool _intervalUpdating;

    // Needs a Drone / Pad selected and at least one other Drone / Pad to take the pitch from.
    private bool CanOpenIntervalPanel() =>
        SelectedLayer is { Type: LayerType.Drone or LayerType.Pad } selected
        && Project.Layers.Any(l => !ReferenceEquals(l, selected) && l.Type is LayerType.Drone or LayerType.Pad);

    partial void OnSelectedLayerChanged(Layer? value)
    {
        // The panel is built for one selected layer.
        if (IsIntervalPanelOpen)
            IsIntervalPanelOpen = false;
    }

    /// <summary>Opens the mini panel; the layer below the selected one is ticked by default.</summary>
    [RelayCommand(CanExecute = nameof(CanOpenIntervalPanel))]
    private void OpenIntervalPanel()
    {
        if (SelectedLayer is not { } target)
            return;

        foreach (IntervalLayerItem old in IntervalLayers)
            old.PropertyChanged -= OnIntervalItemChanged;

        IntervalLayers.Clear();
        foreach (Layer layer in Project.Layers.Where(l => l.Type is LayerType.Drone or LayerType.Pad))
            IntervalLayers.Add(new IntervalLayerItem(layer, ReferenceEquals(layer, target)));

        List<IntervalLayerItem> sources = IntervalLayers.Where(i => !i.IsTarget).ToList();
        int index = Project.Layers.IndexOf(target);
        IntervalLayerItem? first = sources.FirstOrDefault(i => Project.Layers.IndexOf(i.Layer) > index) ?? sources.LastOrDefault();
        if (first is not null)
            first.IsChecked = true;

        foreach (IntervalLayerItem item in IntervalLayers)
            item.PropertyChanged += OnIntervalItemChanged;

        UpdateIntervalMode();
        IsIntervalPanelOpen = true;
    }

    private void OnIntervalItemChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!_intervalUpdating && e.PropertyName == nameof(IntervalLayerItem.IsChecked))
            UpdateIntervalMode();
    }

    // Looks at the ticks: none - a hint; one - the classic table; two - the three-layer table with the Root mark.
    private void UpdateIntervalMode()
    {
        _intervalUpdating = true;

        List<IntervalLayerItem> sources = IntervalLayers.Where(i => !i.IsTarget).ToList();
        List<IntervalLayerItem> chosen = sources.Where(i => i.IsChecked).ToList();

        foreach (IntervalLayerItem item in sources)
            item.CanCheck = chosen.Count < 2 || item.IsChecked;

        bool chord = chosen.Count == 2;
        IsSingleMode = chosen.Count == 1;
        IsChordMode = chord;

        List<IntervalLayerItem> trio = chord ? IntervalLayers.Where(i => i.IsTarget || i.IsChecked).ToList() : [];

        foreach (IntervalLayerItem item in IntervalLayers)
        {
            item.ShowRoot = trio.Contains(item);
            if (!item.ShowRoot)
                item.IsRoot = false;
        }

        if (chord && !trio.Any(i => i.IsRoot))
            chosen[0].IsRoot = true;

        _intervalUpdating = false;

        IntervalSource = IsSingleMode ? new IntervalSource(chosen[0].Layer, chosen[0].Text) : null;
        RebuildChordRows();
    }

    /// <summary>Makes the layer the root of the chord (the other two are set relative to it).</summary>
    [RelayCommand]
    private void SetIntervalRoot(IntervalLayerItem? item)
    {
        if (item is null || !IsChordMode || !item.ShowRoot)
            return;

        foreach (IntervalLayerItem other in IntervalLayers)
            other.IsRoot = ReferenceEquals(other, item);

        RebuildChordRows();
    }

    // The root and the two other voices, the lower of them (by the current pitch) first.
    private (IntervalLayerItem Root, IntervalLayerItem Low, IntervalLayerItem High)? ChordVoices()
    {
        List<IntervalLayerItem> trio = IntervalLayers.Where(i => i.ShowRoot).ToList();
        IntervalLayerItem? root = trio.FirstOrDefault(i => i.IsRoot);
        if (!IsChordMode || trio.Count != 3 || root is null)
            return null;

        List<IntervalLayerItem> others = trio.Where(i => !ReferenceEquals(i, root)).OrderBy(i => i.Layer.PitchHz).ToList();
        return (root, others[0], others[1]);
    }

    // A pitch outside the allowed range is folded by octaves instead of being cut.
    private static double FoldPitch(double hz)
    {
        while (hz > Layer.MaxPitchHz)
            hz /= 2.0;

        while (hz < Layer.MinPitchHz)
            hz *= 2.0;

        return Math.Round(hz, 1);
    }

    private void RebuildChordRows()
    {
        ChordRows.Clear();

        if (ChordVoices() is not { } voices)
            return;

        double root = voices.Root.Layer.PitchHz;
        double lowNow = voices.Low.Layer.PitchHz;
        double highNow = voices.High.Layer.PitchHz;

        foreach (ChordPreset preset in Choices.ChordPresets)
        {
            double low = FoldPitch(root * Math.Min(preset.RatioA, preset.RatioB));
            double high = FoldPitch(root * Math.Max(preset.RatioA, preset.RatioB));

            var row = new ChordRow(preset, low, high, root);
            double[] now = [lowNow, highNow];
            Array.Sort(now);
            double[] mine = [low, high];
            Array.Sort(mine);
            row.IsCurrent = Math.Abs(now[0] - mine[0]) < 0.05 && Math.Abs(now[1] - mine[1]) < 0.05;
            ChordRows.Add(row);
        }
    }

    /// <summary>Sets the pitch of the two non-root layers of the chord; the root keeps its pitch. The panel stays open.</summary>
    [RelayCommand]
    private void ApplyChord(ChordRow? row)
    {
        if (row is null || ChordVoices() is not { } voices)
            return;

        voices.Low.Layer.PitchHz = row.LowHz;
        voices.High.Layer.PitchHz = row.HighHz;

        StatusMessage = $"{row.Preset.Name} on {voices.Root.Layer.Name} ({voices.Root.Layer.PitchHz:0.#} Hz): "
            + $"{voices.Low.Layer.Name} {row.LowHz:0.#} Hz, {voices.High.Layer.Name} {row.HighHz:0.#} Hz";
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
