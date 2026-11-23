using AmbientVenGen.Core.Composition;
using AmbientVenGen.Desktop.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AmbientVenGen.Desktop.Controls;

/// <summary>The settings of one event of a layer. The DataContext is a LayerEvent; the editor is an item of the Events list.</summary>
public partial class EventEditor : UserControl
{
    public EventEditor() => InitializeComponent();

    private ItemsControl? Host()
    {
        DependencyObject? parent = this;
        while (parent is not null and not ItemsControl)
            parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);

        return parent as ItemsControl;
    }

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (Host() is { ItemsSource: System.Collections.IList list } && DataContext is not null)
            list.Remove(DataContext);
    }

    // A click anywhere on the event picks it: its marker on the timeline gets a bright frame.
    private void OnPickEvent(object sender, MouseButtonEventArgs e)
    {
        // the card takes the keyboard focus (Space then jumps to the event), unless a field is being typed in
        if (e.OriginalSource is not System.Windows.Controls.Primitives.TextBoxBase)
            Focus();

        if (DataContext is LayerEvent item && Host()?.DataContext is MainViewModel viewModel)
            viewModel.SelectEvent(item);
    }

    private void OnGoToClick(object sender, RoutedEventArgs e) => GoTo();

    /// <summary>Moves the playhead of the timeline to this event (also what Space does inside the card).</summary>
    public void GoTo()
    {
        if (DataContext is LayerEvent item && Host()?.DataContext is MainViewModel viewModel)
            viewModel.GoToEvent(item);
    }
}
