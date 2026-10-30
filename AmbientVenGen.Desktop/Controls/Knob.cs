using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AmbientVenGen.Desktop.Controls;

/// <summary>
/// Rotary knob. Press and drag up/down to change the value: the drag distance is measured
/// from the point of the click. The look is defined in Resources/CustomControls.xaml.
/// Property names match <see cref="LabeledSlider"/> (Minimum, Maximum, Step, Format, Value),
/// so the two can be swapped in XAML.
/// </summary>
public class Knob : Control
{
    private const double MinAngle = -135.0;
    private const double MaxAngle = 135.0;

    /// <summary>Mouse travel (in pixels) that corresponds to the whole value range.</summary>
    private const double DragRangePixels = 150.0;

    private static readonly DependencyPropertyKey AngleKey = DependencyProperty.RegisterReadOnly(
        nameof(Angle), typeof(double), typeof(Knob), new PropertyMetadata(MinAngle));

    private static readonly DependencyPropertyKey DisplayTextKey = DependencyProperty.RegisterReadOnly(
        nameof(DisplayText), typeof(string), typeof(Knob), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(Knob),
        new FrameworkPropertyMetadata(
            0.0,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnValueChanged,
            CoerceValue));

    // Changing the range or the step re-applies the limits to the current value,
    // so the order of attributes in XAML (Value before Maximum) does not matter.
    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(Knob), new PropertyMetadata(0.0, OnLimitsChanged));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(Knob), new PropertyMetadata(100.0, OnLimitsChanged));

    /// <summary>Snap step; 0 means continuous.</summary>
    public static readonly DependencyProperty StepProperty = DependencyProperty.Register(
        nameof(Step), typeof(double), typeof(Knob), new PropertyMetadata(0.0, OnLimitsChanged));

    /// <summary>Composite format ("{0:F0}") or a plain suffix (" Hz") for the value text.</summary>
    public static readonly DependencyProperty FormatProperty = DependencyProperty.Register(
        nameof(Format), typeof(string), typeof(Knob), new PropertyMetadata("{0:F0}", OnFormatChanged));

    public static readonly DependencyProperty AngleProperty = AngleKey.DependencyProperty;

    public static readonly DependencyProperty DisplayTextProperty = DisplayTextKey.DependencyProperty;

    private Point _startMousePosition;
    private double _startValue;
    private bool _isDragging;

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

    /// <summary>Rotation of the indicator in degrees (read-only, used by the template).</summary>
    public double Angle => (double)GetValue(AngleProperty);

    /// <summary>The value formatted with <see cref="Format"/> (read-only, used by the template).</summary>
    public string DisplayText => (string)GetValue(DisplayTextProperty);

    static Knob()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(Knob), new FrameworkPropertyMetadata(typeof(Knob)));
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        UpdateVisuals();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        _isDragging = true;
        _startMousePosition = e.GetPosition(this);
        _startValue = Value;

        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            ReleaseMouseCapture();
            e.Handled = true;
        }

        base.OnMouseLeftButtonUp(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (!_isDragging)
            return;

        // Up is plus, down is minus, measured from the point of the click.
        double deltaY = _startMousePosition.Y - e.GetPosition(this).Y;
        double range = Maximum - Minimum;

        // CoerceValue clamps the result and applies the step.
        Value = _startValue + deltaY / DragRangePixels * range;

        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _isDragging = false;
    }

    private static object CoerceValue(DependencyObject d, object baseValue)
    {
        var knob = (Knob)d;
        double value = Math.Clamp((double)baseValue, knob.Minimum, Math.Max(knob.Minimum, knob.Maximum));

        if (knob.Step > 0)
        {
            double steps = Math.Round((value - knob.Minimum) / knob.Step);
            value = Math.Clamp(knob.Minimum + steps * knob.Step, knob.Minimum, Math.Max(knob.Minimum, knob.Maximum));
        }

        return value;
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((Knob)d).UpdateVisuals();

    private static void OnLimitsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        d.CoerceValue(ValueProperty);
        ((Knob)d).UpdateVisuals();
    }

    private static void OnFormatChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((Knob)d).UpdateDisplayText();

    private void UpdateVisuals()
    {
        double range = Maximum - Minimum;
        double fraction = range > 0 ? (Value - Minimum) / range : 0.0;

        SetValue(AngleKey, MinAngle + Math.Clamp(fraction, 0.0, 1.0) * (MaxAngle - MinAngle));
        UpdateDisplayText();
    }

    private void UpdateDisplayText()
    {
        string format = Format ?? "{0:F0}";

        try
        {
            // Without a placeholder the format is a suffix, e.g. " Hz".
            SetValue(DisplayTextKey, format.Contains("{0") ? string.Format(format, Value) : $"{Value} {format}");
        }
        catch (FormatException)
        {
            SetValue(DisplayTextKey, Value.ToString());
        }
    }
}
