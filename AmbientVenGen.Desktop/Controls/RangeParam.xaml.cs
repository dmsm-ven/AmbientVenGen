using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AmbientVenGen.Core.Composition;

namespace AmbientVenGen.Desktop.Controls;

/// <summary>
/// A parameter that can change over the layer: one value (Start), or two (Start and End) with the curve of the change.
/// Knob mode: a knob where a double click on the arc adds / removes the second value (only the arc between S and E is
/// filled, in either direction) with a small curve picker next to it. Slider mode: a slider, a "S / E" switch and,
/// when two values are on, the second slider and the curve picker.
/// With <see cref="AllowRange"/> off (fades) there is always one value and the curve picker is always shown.
/// </summary>
public partial class RangeParam : UserControl
{
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(string), typeof(RangeParam), new PropertyMetadata(string.Empty, OnChanged));

    public static readonly DependencyProperty HintProperty = DependencyProperty.Register(
        nameof(Hint), typeof(string), typeof(RangeParam), new PropertyMetadata(string.Empty, OnChanged));

    public static readonly DependencyProperty StartProperty = DependencyProperty.Register(
        nameof(Start), typeof(double), typeof(RangeParam),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnChanged));

    public static readonly DependencyProperty EndProperty = DependencyProperty.Register(
        nameof(End), typeof(double), typeof(RangeParam),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnChanged));

    public static readonly DependencyProperty EasingProperty = DependencyProperty.Register(
        nameof(Easing), typeof(Easing), typeof(RangeParam),
        new FrameworkPropertyMetadata(Easing.Sine, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(RangeParam), new PropertyMetadata(0.0, OnChanged));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(RangeParam), new PropertyMetadata(1.0, OnChanged));

    public static readonly DependencyProperty StepProperty = DependencyProperty.Register(
        nameof(Step), typeof(double), typeof(RangeParam), new PropertyMetadata(0.0, OnChanged));

    public static readonly DependencyProperty FormatProperty = DependencyProperty.Register(
        nameof(Format), typeof(string), typeof(RangeParam), new PropertyMetadata("{0:0.##}", OnChanged));

    /// <summary>False: always one value (Start) and the curve picker is always shown (fades).</summary>
    public static readonly DependencyProperty AllowRangeProperty = DependencyProperty.Register(
        nameof(AllowRange), typeof(bool), typeof(RangeParam), new PropertyMetadata(true, OnChanged));

    public static readonly DependencyProperty UseKnobsProperty = InputStyle.UseKnobsProperty.AddOwner(
        typeof(RangeParam), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits, OnChanged));

    private bool _syncing;
    private int _editHandle;

    public RangeParam()
    {
        InitializeComponent();

        RangeKnob.ValueChanged += OnKnobValueChanged;
        RangeKnob.ValueEndChanged += OnKnobEndChanged;
        RangeKnob.ExactValueRequested += BeginEdit;

        Sync();
    }

    public string Header { get => (string)GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    public string Hint { get => (string)GetValue(HintProperty); set => SetValue(HintProperty, value); }
    public double Start { get => (double)GetValue(StartProperty); set => SetValue(StartProperty, value); }
    public double End { get => (double)GetValue(EndProperty); set => SetValue(EndProperty, value); }
    public Easing Easing { get => (Easing)GetValue(EasingProperty); set => SetValue(EasingProperty, value); }
    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Step { get => (double)GetValue(StepProperty); set => SetValue(StepProperty, value); }
    public string Format { get => (string)GetValue(FormatProperty); set => SetValue(FormatProperty, value); }
    public bool AllowRange { get => (bool)GetValue(AllowRangeProperty); set => SetValue(AllowRangeProperty, value); }
    public bool UseKnobs { get => (bool)GetValue(UseKnobsProperty); set => SetValue(UseKnobsProperty, value); }

    /// <summary>Two different values are set.</summary>
    public bool IsRange => AllowRange && Math.Abs(End - Start) > 1e-9;

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((RangeParam)d).Sync();

    // ------------------------------------------------------------------ knob events

    private void OnKnobValueChanged(double value)
    {
        if (_syncing)
            return;

        bool wasRange = IsRange;
        SetCurrentValue(StartProperty, value);

        // One value: the end follows it, so that the parameter stays constant.
        if (!wasRange)
            SetCurrentValue(EndProperty, value);
    }

    private void OnKnobEndChanged(double value)
    {
        if (!_syncing && AllowRange)
            SetCurrentValue(EndProperty, value);
    }

    // ------------------------------------------------------------------ slider mode switch

    private void OnRangeChecked(object sender, RoutedEventArgs e)
    {
        if (_syncing || IsRange)
            return;

        // The second value starts a little aside from the first, so that the range exists.
        double nudge = Step > 0 ? Step : (Maximum - Minimum) * 0.02;
        double end = Start + nudge <= Maximum ? Start + nudge : Start - nudge;
        SetCurrentValue(EndProperty, end);
    }

    private void OnRangeUnchecked(object sender, RoutedEventArgs e)
    {
        if (!_syncing)
            SetCurrentValue(EndProperty, Start);
    }

    // ------------------------------------------------------------------ view

    private bool PercentFormat => Format.Contains(":P", StringComparison.OrdinalIgnoreCase);

    private string FormatValue(double value)
    {
        try
        {
            return string.Format(CultureInfo.CurrentCulture, Format, value);
        }
        catch (FormatException)
        {
            return value.ToString(CultureInfo.CurrentCulture);
        }
    }

    private void Sync()
    {
        if (RangeKnob is null)
            return;

        _syncing = true;
        try
        {
            bool range = IsRange;
            bool knob = UseKnobs;

            RangeKnob.Minimum = Minimum;
            RangeKnob.Maximum = Math.Max(Maximum, Minimum);
            RangeKnob.Step = Step;
            RangeKnob.AllowRange = AllowRange;
            RangeKnob.Value = Start;
            RangeKnob.ValueEnd = AllowRange ? End : Start;
            RangeKnob.Caption = Header;
            RangeKnob.ValueText = range ? $"{FormatValue(Start)} → {FormatValue(End)}" : FormatValue(Start);
            RangeKnob.ToolTip = string.IsNullOrEmpty(Hint) ? null : $"{Header}: {Hint}";

            RangeKnob.Visibility = knob ? Visibility.Visible : Visibility.Collapsed;
            KnobCurve.Visibility = knob && (range || !AllowRange) ? Visibility.Visible : Visibility.Collapsed;

            SliderPanel.Visibility = knob ? Visibility.Collapsed : Visibility.Visible;
            RangeCheck.Visibility = AllowRange ? Visibility.Visible : Visibility.Collapsed;
            RangeCheck.IsChecked = range;
            EndSlider.Visibility = range ? Visibility.Visible : Visibility.Collapsed;
            SliderCurve.Visibility = range || !AllowRange ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            _syncing = false;
        }
    }

    // ------------------------------------------------------------------ exact value of a handle

    private void BeginEdit(int handle)
    {
        _editHandle = AllowRange && IsRange ? handle : 0;

        double value = _editHandle == 1 ? End : Start;
        Editor.Text = (PercentFormat ? value * 100.0 : value).ToString("0.##", CultureInfo.CurrentCulture);
        Editor.ToolTip = IsRange ? (_editHandle == 1 ? "E (end)" : "S (start)") : null;

        // Centred over the knob (the value is written there).
        Editor.HorizontalAlignment = HorizontalAlignment.Left;
        Editor.Margin = new Thickness(Math.Max(0, (RangeKnob.ActualWidth - Editor.Width) / 2.0), 18, 0, 0);
        Editor.Visibility = Visibility.Visible;
        Editor.Focus();
        Editor.SelectAll();
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
        if (Editor.Visibility != Visibility.Visible)
            return;

        Editor.Visibility = Visibility.Collapsed;

        if (!commit)
            return;

        string text = Editor.Text.Trim().TrimEnd('%').Trim();
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double typed)
            && !double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out typed))
        {
            return;
        }

        if (PercentFormat)
            typed /= 100.0;

        if (Step > 0)
            typed = Math.Round(typed / Step) * Step;

        typed = Math.Clamp(Math.Round(typed, 4), Minimum, Math.Max(Maximum, Minimum));

        bool wasRange = IsRange;

        if (_editHandle == 1)
        {
            SetCurrentValue(EndProperty, typed);
        }
        else
        {
            SetCurrentValue(StartProperty, typed);
            if (!wasRange)
                SetCurrentValue(EndProperty, typed);
        }
    }
}
