using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

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
    /// 20 Hz..1 kHz would otherwise leave the low notes squeezed into the first few pixels.
    /// </summary>
    public static readonly DependencyProperty IsLogarithmicProperty = DependencyProperty.Register(
        nameof(IsLogarithmic), typeof(bool), typeof(LabeledSlider),
        new PropertyMetadata(false, OnPropertyChanged));

    /// <summary>Click on the value to type an exact number instead of dragging.</summary>
    public static readonly DependencyProperty IsValueEditableProperty = DependencyProperty.Register(
        nameof(IsValueEditable), typeof(bool), typeof(LabeledSlider),
        new PropertyMetadata(false, OnPropertyChanged));

    /// <summary>Follows the window-wide "Slider / Knob" setting (inherited attached property).</summary>
    public static readonly DependencyProperty UseKnobsProperty = InputStyle.UseKnobsProperty.AddOwner(
        typeof(LabeledSlider),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits, OnPropertyChanged));

    /// <summary>In knob mode the caption is shown above the knob; switch it off when the meaning is clear from the layout.</summary>
    public static readonly DependencyProperty KnobHeaderVisibleProperty = DependencyProperty.Register(
        nameof(KnobHeaderVisible), typeof(bool), typeof(LabeledSlider),
        new PropertyMetadata(true, OnPropertyChanged));

    private bool _syncing;
    private bool _editing;

    public bool KnobHeaderVisible
    {
        get => (bool)GetValue(KnobHeaderVisibleProperty);
        set => SetValue(KnobHeaderVisibleProperty, value);
    }

    /// <summary>True when the window-wide setting asks for knobs instead of sliders.</summary>
    public bool UseKnobs
    {
        get => (bool)GetValue(UseKnobsProperty);
        set => SetValue(UseKnobsProperty, value);
    }

    public bool IsValueEditable
    {
        get => (bool)GetValue(IsValueEditableProperty);
        set => SetValue(IsValueEditableProperty, value);
    }

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
        InnerKnob.ValueChanged += OnInnerKnobValueChanged;
        Sync();
    }

    private static void OnPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((LabeledSlider)d).Sync();

    private void OnInnerValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => ApplyInnerValue(e.NewValue);

    private void OnInnerKnobValueChanged(double value) => ApplyInnerValue(value);

    // The slider and the knob work in the same (linear or logarithmic) scale; this turns their value into ours.
    private void ApplyInnerValue(double raw)
    {
        if (_syncing)
            return;

        double value = raw;

        if (IsLogarithmic && Minimum > 0)
        {
            value = Math.Exp(value);

            if (Step > 0)
                value = Math.Round(value / Step) * Step;

            value = Math.Clamp(Math.Round(value, 4), Minimum, Math.Max(Maximum, Minimum));
        }

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

            // The knob mirrors the slider (same scale, same value); its label shows our formatted value.
            InnerKnob.Minimum = InnerSlider.Minimum;
            InnerKnob.Maximum = InnerSlider.Maximum;
            InnerKnob.Step = log ? 0 : Step;
            InnerKnob.Value = InnerSlider.Value;
            InnerKnob.ValueText = FormatValue(Value);

            bool knob = UseKnobs;
            InnerSlider.Visibility = knob ? Visibility.Collapsed : Visibility.Visible;
            InnerKnob.Visibility = knob ? Visibility.Visible : Visibility.Collapsed;
            HeaderPanel.HorizontalAlignment = knob ? HorizontalAlignment.Center : HorizontalAlignment.Left;
            HeaderPanel.Visibility = knob && !KnobHeaderVisible ? Visibility.Collapsed : Visibility.Visible;

            // The knob writes its value itself; the separate value text stays only where it can be clicked to type.
            if (!_editing)
                ValueText.Visibility = knob && !IsValueEditable ? Visibility.Collapsed : Visibility.Visible;

            ValueText.Cursor = IsValueEditable ? Cursors.IBeam : Cursors.Arrow;
            ValueText.ToolTip = IsValueEditable ? "Click to type an exact value" : null;

            HeaderText.Text = Header;
            HeaderHint.Text = Hint;
            ValueText.Text = FormatValue(Value);
        }
        finally
        {
            _syncing = false;
        }
    }

    private void OnValueTextClick(object sender, MouseButtonEventArgs e)
    {
        if (!IsValueEditable || _editing)
            return;

        _editing = true;
        ValueEditor.Text = Value.ToString("0.##", CultureInfo.CurrentCulture);
        ValueText.Visibility = Visibility.Collapsed;
        ValueEditor.Visibility = Visibility.Visible;
        ValueEditor.Focus();
        ValueEditor.SelectAll();
        e.Handled = true;
    }

    private void OnEditorKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            EndEdit(commit: true);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            EndEdit(commit: false);
            e.Handled = true;
        }
    }

    private void OnEditorLostFocus(object sender, RoutedEventArgs e) => EndEdit(commit: true);

    private void EndEdit(bool commit)
    {
        if (!_editing)
            return;

        _editing = false;

        if (commit && TryParseNumber(ValueEditor.Text, out double typed))
        {
            double step = Step;
            if (step > 0)
                typed = Math.Round(typed / step) * step;

            typed = Math.Round(typed, 4);
            SetCurrentValue(ValueProperty, Math.Clamp(typed, Minimum, Math.Max(Maximum, Minimum)));
        }

        ValueEditor.Visibility = Visibility.Collapsed;
        ValueText.Visibility = Visibility.Visible;
        Sync();
    }

    // Accepts "82,4", "82.4" and a trailing "Hz".
    private static bool TryParseNumber(string? text, out double number)
    {
        number = 0;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        string cleaned = text.Trim();
        if (cleaned.EndsWith("Hz", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned[..^2].Trim();
        else if (cleaned.EndsWith('x') || cleaned.EndsWith('X') || cleaned.EndsWith('\u00D7'))
            cleaned = cleaned[..^1].Trim();

        return double.TryParse(cleaned, NumberStyles.Float, CultureInfo.CurrentCulture, out number)
            || double.TryParse(cleaned.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out number);
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
