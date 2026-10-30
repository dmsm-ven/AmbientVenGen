using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace AmbientVenGen.Desktop.Controls;

/// <summary>
/// Slider with a caption and a formatted value. The inner slider is driven from code (not bound),
/// so the order in which Minimum/Maximum/Value are set can never clamp the bound value by accident.
/// </summary>
public partial class LabeledSlider : UserControl
{
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(string), typeof(LabeledSlider),
        new PropertyMetadata(string.Empty, OnPropertyChanged));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(LabeledSlider),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPropertyChanged));

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(LabeledSlider),
        new PropertyMetadata(0.0, OnPropertyChanged));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(LabeledSlider),
        new PropertyMetadata(1.0, OnPropertyChanged));

    /// <summary>Snap step; 0 means continuous.</summary>
    public static readonly DependencyProperty StepProperty = DependencyProperty.Register(
        nameof(Step), typeof(double), typeof(LabeledSlider),
        new PropertyMetadata(0.0, OnPropertyChanged));

    /// <summary>Composite format for the value text, e.g. "{0:P0}" or "{0:0} s".</summary>
    public static readonly DependencyProperty FormatProperty = DependencyProperty.Register(
        nameof(Format), typeof(string), typeof(LabeledSlider),
        new PropertyMetadata("{0:0.##}", OnPropertyChanged));

    private bool _syncing;

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double Step
    {
        get => (double)GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    public string Format
    {
        get => (string)GetValue(FormatProperty);
        set => SetValue(FormatProperty, value);
    }

    public LabeledSlider()
    {
        InitializeComponent();
        Sync();
    }

    private static void OnPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((LabeledSlider)d).Sync();

    private void OnInnerValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncing)
            return;

        // SetCurrentValue keeps (and pushes through) a two-way binding.
        SetCurrentValue(ValueProperty, e.NewValue);
    }

    private void Sync()
    {
        if (InnerSlider is null)
            return;

        _syncing = true;
        try
        {
            double minimum = Minimum;
            double maximum = Math.Max(Maximum, minimum);

            InnerSlider.Minimum = minimum;
            InnerSlider.Maximum = maximum;

            if (Step > 0)
            {
                InnerSlider.TickFrequency = Step;
                InnerSlider.IsSnapToTickEnabled = true;
            }
            else
            {
                InnerSlider.IsSnapToTickEnabled = false;
            }

            InnerSlider.Value = Value;

            HeaderText.Text = Header;
            ValueText.Text = FormatValue(Value);
        }
        finally
        {
            _syncing = false;
        }
    }

    private string FormatValue(double value)
    {
        try
        {
            return string.Format(CultureInfo.CurrentCulture, Format, value);
        }
        catch (FormatException)
        {
            return value.ToString("0.##", CultureInfo.CurrentCulture);
        }
    }
}
