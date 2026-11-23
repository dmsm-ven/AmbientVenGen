using AmbientVenGen.Core.Composition;
using AmbientVenGen.Desktop.Controls;
using AmbientVenGen.Desktop.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AmbientVenGen.Desktop.Panels;

public partial class EventsPanel : UserControl
{
    private Point _pressed;
    private bool _down;

    public EventsPanel() => InitializeComponent();

    private void OnTileDown(object sender, MouseButtonEventArgs e)
    {
        _pressed = e.GetPosition(this);
        _down = true;
    }

    // A click (no drag) adds the event at the playhead.
    private void OnTileUp(object sender, MouseButtonEventArgs e)
    {
        if (!_down)
            return;

        _down = false;

        if (sender is FrameworkElement { Tag: string type } && DataContext is MainViewModel viewModel)
            viewModel.AddEventCommand.Execute(type);
    }

    // A drag takes the type to the timeline (the layer under the cursor gets the event where it is dropped).
    private void OnTileMove(object sender, MouseEventArgs e)
    {
        if (!_down || e.LeftButton != MouseButtonState.Pressed || sender is not FrameworkElement { Tag: string type } tile)
            return;

        Point now = e.GetPosition(this);
        if (Math.Abs(now.X - _pressed.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(now.Y - _pressed.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _down = false;
        DragDrop.DoDragDrop(tile, new DataObject(TimelineControl.EventDragFormat, type), DragDropEffects.Copy);
    }
}
