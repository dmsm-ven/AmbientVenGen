using System.Windows;
using System.Windows.Controls;

namespace AmbientVenGen.Desktop.Controls;

/// <summary>The settings of one modulator of a layer (target, shape, rate, depth). The DataContext is a Modulator.</summary>
public partial class ModulatorEditor : UserControl
{
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(string), typeof(ModulatorEditor), new PropertyMetadata("Modulator"));

    public ModulatorEditor() => InitializeComponent();

    // The editor is an item of the list of modulators: removing it takes its modulator out of that list.
    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        DependencyObject? parent = this;
        while (parent is not null and not ItemsControl)
            parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);

        if (parent is ItemsControl { ItemsSource: System.Collections.IList list } && DataContext is not null)
            list.Remove(DataContext);
    }

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }
}
