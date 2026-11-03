using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AmbientVenGen.Desktop.Controls;

/// <summary>
/// A main control with a hidden second one (Pitch + Pitch drift, Cutoff + Resonance...). A small 8x8 marker at the
/// top-right corner of the main control opens the second one right next to it; the state is bound to the layer.
/// </summary>
[ContentProperty(nameof(Primary))]
public sealed class KnobPair : Control
{
    public static readonly DependencyProperty PrimaryProperty = DependencyProperty.Register(
        nameof(Primary), typeof(UIElement), typeof(KnobPair), new PropertyMetadata(null, OnContentChanged));

    public static readonly DependencyProperty SecondaryProperty = DependencyProperty.Register(
        nameof(Secondary), typeof(UIElement), typeof(KnobPair), new PropertyMetadata(null, OnContentChanged));

    public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register(
        nameof(IsOpen), typeof(bool), typeof(KnobPair),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnOpenChanged));

    public static readonly DependencyProperty MarkerToolTipProperty = DependencyProperty.Register(
        nameof(MarkerToolTip), typeof(string), typeof(KnobPair), new PropertyMetadata(null));


    public UIElement? Primary { get => (UIElement?)GetValue(PrimaryProperty); set => SetValue(PrimaryProperty, value); }
    public UIElement? Secondary { get => (UIElement?)GetValue(SecondaryProperty); set => SetValue(SecondaryProperty, value); }
    public bool IsOpen { get => (bool)GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }
    public string? MarkerToolTip { get => (string?)GetValue(MarkerToolTipProperty); set => SetValue(MarkerToolTipProperty, value); }

    private static void OnContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((KnobPair)d).Rebuild();
    private static void OnOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((KnobPair)d).Rebuild();

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        Rebuild();
    }

    private Grid? _root;
    private Border? _marker;

    private void Rebuild()
    {
        if (_root is null)
        {
            _root = new Grid();
            _root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _marker = new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(2),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 2, 0),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
            _marker.MouseLeftButtonDown += (_, e) =>
            {
                IsOpen = !IsOpen;
                e.Handled = true;
            };
            AddVisualChild(_root);
        }

        _root.Children.Clear();

        if (Primary is not null)
        {
            DetachFromParent(Primary);
            Grid.SetColumn(Primary, 0);
            _root.Children.Add(Primary);
        }

        if (_marker is not null)
        {
            _marker.Background = IsOpen
                ? new SolidColorBrush(Color.FromRgb(0x6C, 0x9B, 0xFF))
                : new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
            _marker.BorderBrush = new SolidColorBrush(Color.FromRgb(0x8F, 0x8D, 0xB0));
            _marker.ToolTip = MarkerToolTip;
            Grid.SetColumn(_marker, 0);
            _root.Children.Add(_marker);
        }

        if (Secondary is not null && IsOpen)
        {
            DetachFromParent(Secondary);
            Grid.SetColumn(Secondary, 1);
            _root.Children.Add(Secondary);
        }

        InvalidateMeasure();
    }

    private void DetachFromParent(UIElement element)
    {
        if (VisualTreeHelper.GetParent(element) is Panel panel && !ReferenceEquals(panel, _root))
            panel.Children.Remove(element);
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
