using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace AmbientVenGen.Desktop.Controls;

/// <summary>
/// Two dependent controls (Pitch + Pitch drift, Cutoff + Resonance, Fade in + Fade out...) that are always shown side
/// by side in one rounded group with a chain link between them: the second one depends on the first.
/// The pair never wraps apart inside a wrapping container because it is a single element.
/// </summary>
[ContentProperty(nameof(Primary))]
public sealed class KnobPair : Control
{
    public static readonly DependencyProperty PrimaryProperty = DependencyProperty.Register(
        nameof(Primary), typeof(UIElement), typeof(KnobPair), new PropertyMetadata(null, OnContentChanged));

    public static readonly DependencyProperty SecondaryProperty = DependencyProperty.Register(
        nameof(Secondary), typeof(UIElement), typeof(KnobPair), new PropertyMetadata(null, OnContentChanged));

    public static readonly DependencyProperty LinkToolTipProperty = DependencyProperty.Register(
        nameof(LinkToolTip), typeof(string), typeof(KnobPair), new PropertyMetadata(null, OnContentChanged));

    public UIElement? Primary { get => (UIElement?)GetValue(PrimaryProperty); set => SetValue(PrimaryProperty, value); }
    public UIElement? Secondary { get => (UIElement?)GetValue(SecondaryProperty); set => SetValue(SecondaryProperty, value); }
    public string? LinkToolTip { get => (string?)GetValue(LinkToolTipProperty); set => SetValue(LinkToolTipProperty, value); }

    private static void OnContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((KnobPair)d).Rebuild();

    private Border? _root;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        Rebuild();
    }

    private void Rebuild()
    {
        if (_root is null)
        {
            _root = new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0x8F, 0x8D, 0xB0)),
                Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
                Padding = new Thickness(10, 6, 10, 4)
            };
            AddVisualChild(_root);
        }

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        if (Primary is not null)
            Add(grid, Primary, 0);

        var link = new SymbolIcon
        {
            Symbol = SymbolRegular.Link24,
            FontSize = 16,
            Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0x8D, 0xB0)),
            Margin = new Thickness(8, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = LinkToolTip
        };
        Grid.SetColumn(link, 1);
        grid.Children.Add(link);

        if (Secondary is not null)
            Add(grid, Secondary, 2);

        _root.Child = grid;
        InvalidateMeasure();
    }

    private static void Add(Grid grid, UIElement element, int column)
    {
        if (VisualTreeHelper.GetParent(element) is Panel panel)
            panel.Children.Remove(element);
        else if (VisualTreeHelper.GetParent(element) is Border border && border.Child == element)
            border.Child = null;

        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    protected override int VisualChildrenCount => _root is null ? 0 : 1;

    protected override Visual GetVisualChild(int index) => _root!;

    protected override Size MeasureOverride(Size constraint)
    {
        if (_root is null)
            Rebuild();
        _root!.Measure(constraint);
        return _root.DesiredSize;
    }

    protected override Size ArrangeOverride(Size arrangeBounds)
    {
        _root?.Arrange(new Rect(arrangeBounds));
        return arrangeBounds;
    }
}
