using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AmbientVenGen.Desktop.Controls;

/// <summary>
/// Gauge-style rotary control: a half-circle arc that is empty at the minimum and filled from left to
/// right (180 degrees) at the maximum, with the value written under the arc.
/// Click or drag anywhere on the gauge to set the value by the angle (straight up = the middle);
/// the two round buttons at the ends of the arc set the minimum / maximum directly.
/// The arc follows the value with a short animation (about 0.15 s for the whole arc).
/// Property names match <see cref="LabeledSlider"/> (Minimum, Maximum, Step, Format, Value).
/// </summary>
public sealed class Knob : FrameworkElement
{
    private const double DefaultWidth = 120;
    private const double DefaultHeight = 71; // 15% lower than before (84): the value is written inside the arc
    private const double ArcThickness = 8;
    private const double CapRadius = 7;
    private const double CapHitRadius = 11;

    /// <summary>Seconds a full sweep (minimum to maximum) takes.</summary>
    private const double FullSweepSeconds = 0.15;

    private static readonly Brush TrackBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x2E, 0x2E, 0x3B)));
    private static readonly Brush FillBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x8B, 0x7C, 0xFF)));
    private static readonly Brush CapBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x23, 0x23, 0x2E)));
    private static readonly Brush CaptionBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xA9, 0xA7, 0xC2)));
    private static readonly Brush TextBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xEC, 0xEC, 0xF2)));
    private static readonly Brush HitBrush = Brushes.Transparent;

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
        nameof(Format), typeof(string), typeof(Knob),
        new FrameworkPropertyMetadata("{0:F0}", FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>When not empty, shown instead of the formatted value (e.g. when the scale is logarithmic).</summary>
    public static readonly DependencyProperty ValueTextProperty = DependencyProperty.Register(
        nameof(ValueText), typeof(string), typeof(Knob),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The name of the parameter, written inside the arc under the value (e.g. "Pitch").</summary>
    public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(
        nameof(Caption), typeof(string), typeof(Knob),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>When true a click on the written value raises <see cref="LabelClicked"/> (to type an exact number).</summary>
    public static readonly DependencyProperty IsLabelClickableProperty = DependencyProperty.Register(
        nameof(IsLabelClickable), typeof(bool), typeof(Knob), new PropertyMetadata(false));

    /// <summary>Where the arc is drawn now (0..1); it moves towards the value with an animation.</summary>
    public static readonly DependencyProperty DisplayedFractionProperty = DependencyProperty.Register(
        nameof(DisplayedFraction), typeof(double), typeof(Knob),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private bool _isDragging;
    private Rect _labelRect;

    /// <summary>Raised when the value changed (by the user or from code).</summary>
    public event Action<double>? ValueChanged;

    /// <summary>Raised when the written value (inside the arc) was clicked and <see cref="IsLabelClickable"/> is on.</summary>
    public event Action? LabelClicked;

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

    public string Caption
    {
        get => (string)GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }

    public bool IsLabelClickable
    {
        get => (bool)GetValue(IsLabelClickableProperty);
        set => SetValue(IsLabelClickableProperty, value);
    }

    public string ValueText
    {
        get => (string)GetValue(ValueTextProperty);
        set => SetValue(ValueTextProperty, value);
    }

    public double DisplayedFraction
    {
        get => (double)GetValue(DisplayedFractionProperty);
        set => SetValue(DisplayedFractionProperty, value);
    }

    static Knob()
    {
        // The default size is twice the old knob (which was 60 wide).
        WidthProperty.OverrideMetadata(typeof(Knob), new FrameworkPropertyMetadata(DefaultWidth));
        HeightProperty.OverrideMetadata(typeof(Knob), new FrameworkPropertyMetadata(DefaultHeight));
        CursorProperty.OverrideMetadata(typeof(Knob), new FrameworkPropertyMetadata(Cursors.Hand));
        FocusableProperty.OverrideMetadata(typeof(Knob), new FrameworkPropertyMetadata(false));
    }

    // ------------------------------------------------------------------ geometry

    private (Point Center, double Radius) Layout()
    {
        // The base line of the arc is near the bottom edge (room for the round end buttons).
        double baseY = ActualHeight - CapRadius - 2;
        double radius = Math.Max(10, Math.Min(ActualWidth / 2.0 - CapRadius - 1, baseY - ArcThickness / 2.0 - 2));
        return (new Point(ActualWidth / 2.0, baseY), radius);
    }

    private static Point OnArc(Point center, double radius, double fraction)
    {
        // fraction 0 = left end, 0.5 = straight up, 1 = right end.
        double angle = Math.PI * (1.0 - Math.Clamp(fraction, 0.0, 1.0));
        return new Point(center.X + radius * Math.Cos(angle), center.Y - radius * Math.Sin(angle));
    }

    private static StreamGeometry Arc(Point center, double radius, double fromFraction, double toFraction)
    {
        var geometry = new StreamGeometry();

        using (StreamGeometryContext ctx = geometry.Open())
        {
            int steps = Math.Max(2, (int)Math.Ceiling((toFraction - fromFraction) * 72));
            ctx.BeginFigure(OnArc(center, radius, fromFraction), false, false);

            for (int i = 1; i <= steps; i++)
                ctx.LineTo(OnArc(center, radius, fromFraction + (toFraction - fromFraction) * i / steps), true, true);
        }

        geometry.Freeze();
        return geometry;
    }

    // ------------------------------------------------------------------ drawing

    protected override void OnRender(DrawingContext dc)
    {
        (Point center, double radius) = Layout();

        // An invisible background makes the whole area react to the mouse.
        dc.DrawRectangle(HitBrush, null, new Rect(0, 0, ActualWidth, ActualHeight));

        double fraction = Math.Clamp(DisplayedFraction, 0.0, 1.0);

        var trackPen = new Pen(TrackBrush, ArcThickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var fillPen = new Pen(FillBrush, ArcThickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

        dc.DrawGeometry(null, trackPen, Arc(center, radius, 0, 1));

        if (fraction > 0.002)
            dc.DrawGeometry(null, fillPen, Arc(center, radius, 0, fraction));

        // Minimum / maximum buttons at the ends of the arc: lit when the value is at that end.
        DrawCap(dc, OnArc(center, radius, 0), fraction <= 0.002);
        DrawCap(dc, OnArc(center, radius, 1), fraction >= 0.998);

        // A dot at the end of the filled part.
        Point tip = OnArc(center, radius, fraction);
        dc.DrawEllipse(TextBrush, new Pen(FillBrush, 2), tip, 5, 5);

        // The value, in the middle of the arc (inside the half circle, above its base line).
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        var text = new FormattedText(
            DisplayText(),
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI Semibold"),
            13,
            TextBrush,
            dpi);

        // The caption (parameter name) takes the place of the old value line; the value sits just above it.
        double bottom = center.Y - 3;
        Rect labelRect = Rect.Empty;

        if (!string.IsNullOrEmpty(Caption))
        {
            var caption = new FormattedText(
                Caption,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                10.5,
                CaptionBrush,
                dpi);

            var captionAt = new Point(center.X - caption.Width / 2.0, bottom - caption.Height);
            dc.DrawText(caption, captionAt);
            labelRect = new Rect(captionAt, new Size(caption.Width, caption.Height));
            bottom = captionAt.Y - 1;
        }

        var valueAt = new Point(center.X - text.Width / 2.0, bottom - text.Height);
        dc.DrawText(text, valueAt);

        var valueRect = new Rect(valueAt, new Size(text.Width, text.Height));
        labelRect.Union(valueRect);
        labelRect.Inflate(4, 2);
        _labelRect = labelRect;
    }

    private static void DrawCap(DrawingContext dc, Point at, bool lit)
    {
        dc.DrawEllipse(
            lit ? FillBrush : CapBrush,
            new Pen(FillBrush, 1.6),
            at,
            CapRadius,
            CapRadius);
    }

    private string DisplayText()
    {
        if (!string.IsNullOrEmpty(ValueText))
            return ValueText;

        string format = Format ?? "{0:F0}";

        try
        {
            // Without a placeholder the format is a suffix, e.g. " Hz".
            return format.Contains("{0") ? string.Format(CultureInfo.CurrentCulture, format, Value) : $"{Value} {format}";
        }
        catch (FormatException)
        {
            return Value.ToString(CultureInfo.CurrentCulture);
        }
    }

    // ------------------------------------------------------------------ mouse

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        Point point = e.GetPosition(this);
        (Point center, double radius) = Layout();

        // The written value: it types an exact number when that is allowed, and never moves the knob.
        if (_labelRect.Contains(point))
        {
            if (IsLabelClickable)
                LabelClicked?.Invoke();

            e.Handled = true;
            return;
        }

        // The round buttons at the ends set the exact minimum / maximum.
        if (Distance(point, OnArc(center, radius, 0)) <= CapHitRadius)
        {
            Value = Minimum;
            e.Handled = true;
            return;
        }

        if (Distance(point, OnArc(center, radius, 1)) <= CapHitRadius)
        {
            Value = Maximum;
            e.Handled = true;
            return;
        }

        // Below the base line and the dead spot in the middle (under the number) do not react.
        if (point.Y > center.Y + CapRadius + 2 || Distance(point, center) < 8)
            return;

        _isDragging = true;
        CaptureMouse();
        SetFromPoint(point);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_isDragging)
        {
            SetFromPoint(e.GetPosition(this));
            e.Handled = true;
            return;
        }

        Cursor = IsLabelClickable && _labelRect.Contains(e.GetPosition(this)) ? Cursors.IBeam : Cursors.Hand;
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

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _isDragging = false;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        double range = Maximum - Minimum;
        double step = Step > 0 ? Step : range / 50.0;

        Value += e.Delta > 0 ? step : -step;
        e.Handled = true;
    }

    // Angle of the point as seen from the centre of the arc: left = minimum, up = middle, right = maximum.
    private void SetFromPoint(Point point)
    {
        (Point center, _) = Layout();

        double dx = point.X - center.X;
        double dy = center.Y - point.Y;
        double fraction;

        if (dy <= 0)
        {
            // Below the baseline while dragging: stick to the nearer end.
            fraction = dx < 0 ? 0.0 : 1.0;
        }
        else
        {
            fraction = 1.0 - Math.Atan2(dy, dx) / Math.PI;
        }

        // CoerceValue clamps the result and applies the step.
        Value = Minimum + Math.Clamp(fraction, 0.0, 1.0) * (Maximum - Minimum);
    }

    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    // ------------------------------------------------------------------ value

    private static object CoerceValue(DependencyObject d, object baseValue)
    {
        var knob = (Knob)d;
        double max = Math.Max(knob.Minimum, knob.Maximum);
        double value = Math.Clamp((double)baseValue, knob.Minimum, max);

        if (knob.Step > 0)
        {
            double steps = Math.Round((value - knob.Minimum) / knob.Step);
            value = Math.Clamp(knob.Minimum + steps * knob.Step, knob.Minimum, max);
        }

        return value;
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var knob = (Knob)d;
        knob.MoveArc();
        knob.ValueChanged?.Invoke((double)e.NewValue);
    }

    private static void OnLimitsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        d.CoerceValue(ValueProperty);
        ((Knob)d).MoveArc();
    }

    private double TargetFraction()
    {
        double range = Maximum - Minimum;
        return range > 0 ? Math.Clamp((Value - Minimum) / range, 0.0, 1.0) : 0.0;
    }

    // The arc glides to the new value: a full sweep takes FullSweepSeconds, a shorter move proportionally less.
    private void MoveArc()
    {
        double target = TargetFraction();

        if (!IsLoaded)
        {
            BeginAnimation(DisplayedFractionProperty, null);
            SetValue(DisplayedFractionProperty, target);
            return;
        }

        double from = DisplayedFraction;
        double seconds = FullSweepSeconds * Math.Abs(target - from);

        if (seconds < 0.004)
        {
            BeginAnimation(DisplayedFractionProperty, null);
            SetValue(DisplayedFractionProperty, target);
            return;
        }

        BeginAnimation(
            DisplayedFractionProperty,
            new DoubleAnimation(from, target, TimeSpan.FromSeconds(seconds)) { FillBehavior = FillBehavior.HoldEnd });
    }

    private static Brush Frozen(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }
}
