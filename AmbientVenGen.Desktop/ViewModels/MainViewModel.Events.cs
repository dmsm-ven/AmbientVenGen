using AmbientVenGen.Core.Composition;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AmbientVenGen.Desktop.ViewModels;

/// <summary>The Events tab: one-shot effects of the selected layer.</summary>
public partial class MainViewModel
{
    /// <summary>The tab of the right-hand group: 0 = Oscillators, 1 = Events.</summary>
    [ObservableProperty]
    private int _sideTabIndex;

    private LayerEvent? _selectedEvent;

    /// <summary>A click on a marker of the timeline (or a drop): the layer, the Events tab and the event are picked.</summary>
    public void ShowEvent(Layer layer, LayerEvent item)
    {
        SelectedLayer = layer;
        SelectEvent(item);
        SideTabIndex = 1;
    }

    public void SelectEvent(LayerEvent? item)
    {
        if (ReferenceEquals(_selectedEvent, item))
            return;

        if (_selectedEvent is not null)
            _selectedEvent.IsSelected = false;

        _selectedEvent = item;

        if (item is not null)
            item.IsSelected = true;
    }

    /// <summary>A new event at the playhead (inside the clip of the selected layer), or at its start.</summary>
    [RelayCommand]
    private void AddEvent(string? type)
    {
        if (SelectedLayer is not { } layer || !Enum.TryParse(type, out EventType kind))
            return;

        double relative = PlayheadSeconds - layer.StartSeconds;
        double start = relative >= 0 && relative <= layer.TotalSeconds && layer.PeriodSeconds > 0
            ? relative % layer.PeriodSeconds
            : 0;

        LayerEvent item = LayerEvent.Create(kind, Math.Round(Math.Min(start, layer.DurationSeconds) * 20.0) / 20.0);
        layer.Events.Add(item);
        SelectEvent(item);
    }
}
