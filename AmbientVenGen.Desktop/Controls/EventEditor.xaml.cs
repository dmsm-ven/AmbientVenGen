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
        if (DataContext is LayerEvent item && Host()?.DataContext is MainViewModel viewModel)
            viewModel.SelectEvent(item);
    }
}
