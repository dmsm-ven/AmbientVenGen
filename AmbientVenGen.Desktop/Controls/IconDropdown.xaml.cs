using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace AmbientVenGen.Desktop.Controls;

/// <summary>
/// A square icon button that opens a list of icons with names. ItemsSource holds Choice&lt;T&gt; items;
/// the icon of an item is the name of its Value (see ModGlyph).
/// </summary>
public partial class IconDropdown : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(IconDropdown), new PropertyMetadata(null));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(object), typeof(IconDropdown),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public IconDropdown() => InitializeComponent();

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public object? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => Pop.IsOpen = false;
}
