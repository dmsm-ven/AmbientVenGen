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

    /// <summary>Explanation shown by the "?" icon after the caption (when hints are switched on).</summary>
    public static readonly DependencyProperty HintProperty = DependencyProperty.Register(
        nameof(Hint), typeof(string), typeof(LabeledSlider),
        new PropertyMetadata(string.Empty, OnPropertyChanged));

    /// <summary>
    /// The slider moves along a logarithmic scale (equal distance = equal ratio). Good for pitch:
    /// 20 Hz..5 kHz would otherwise leave the low notes squeezed into the first few pixels.
    /// </summary>
    public static readonly DependencyProperty IsLogarithmicProperty = DependencyProperty.Register(
        nameof(IsLogarithmic), typeof(bool), typeof(LabeledSlider),
        new PropertyMetadata(false, OnPropertyChanged));

    private bool _syncing;

    public bool IsLogarithmic
    {
        get => (bool)GetValue(IsLogarithmicProperty);
        set => SetValue(IsLogarithmicProperty, value);
    }

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public string Hint
    {
        get => (string)GetValue(HintProperty);
        set => SetValue(HintProperty, value);
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

        double value = e.NewValue;

        if (IsLogarithmic && Minimum > 0)
            value = Math.Clamp(Math.Round(Math.Exp(value), 1), Minimum, Math.Max(Maximum, Minimum));

        // SetCurrentValue keeps (and pushes through) a two-way binding.
        SetCurrentValue(ValueProperty, value);
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

            bool log = IsLogarithmic && minimum > 0;

            InnerSlider.Minimum = log ? Math.Log(minimum) : minimum;
            InnerSlider.Maximum = log ? Math.Log(maximum) : maximum;

            if (log)
            {
                InnerSlider.IsSnapToTickEnabled = false;
            }
            else if (Step > 0)
            {
                InnerSlider.TickFrequency = Step;
                InnerSlider.IsSnapToTickEnabled = true;
            }
            else
            {
                InnerSlider.IsSnapToTickEnabled = false;
            }

            InnerSlider.Value = log ? Math.Log(Math.Clamp(Value, minimum, maximum)) : Value;

            HeaderText.Text = Header;
            HeaderHint.Text = Hint;
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
