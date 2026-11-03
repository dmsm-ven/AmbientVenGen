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
    private const double DefaultWidth = 134;
    private const double DefaultHeight = 60; // another 15% lower (71); the gauge is a stretched half-ellipse
    private const double ArcThickness = 8;
    private const double CapRadius = 7;
    private const double MiddleCapRadius = 5;
    private const double CapHitRadius = 11;

    /// <summary>Seconds a full sweep (minimum to maximum) takes.</summary>
    private const double FullSweepSeconds = 0.15;

    private static readonly Brush TrackBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x2E, 0x2E, 0x3B)));
    private static readonly Brush FillBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x8B, 0x7C, 0xFF)));
    private static readonly Brush CapBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x23, 0x23, 0x2E)));
    private static readonly Brush CaptionBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xA9, 0xA7, 0xC2)));
    private static readonly Brush TextBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xEC, 0xEC, 0xF2)));
    private static readonly Brush HitBrush = Brushes.Transparent;
    private static readonly Brush ArrowBrush = Frozen(new SolidColorBrush(Color.FromArgb(0xB4, 0xFF, 0xFF, 0xFF)));

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

    /// <summary>
    /// The second value (E, "end"). With <see cref="AllowRange"/> and ValueEnd different from Value the knob is a range:
    /// only the part of the arc between S (Value) and E (ValueEnd) is filled, in either direction.
    /// </summary>
    public static readonly DependencyProperty ValueEndProperty = DependencyProperty.Register(
        nameof(ValueEnd), typeof(double), typeof(Knob),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueEndChanged, CoerceValue));

    /// <summary>A double click on the arc may add the second value.</summary>
    public static readonly DependencyProperty AllowRangeProperty = DependencyProperty.Register(
        nameof(AllowRange), typeof(bool), typeof(Knob),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DisplayedEndFractionProperty = DependencyProperty.Register(
        nameof(DisplayedEndFraction), typeof(double), typeof(Knob),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Where the arc is drawn now (0..1); it moves towards the value with an animation.</summary>
    public static readonly DependencyProperty DisplayedFractionProperty = DependencyProperty.Register(
        nameof(DisplayedFraction), typeof(double), typeof(Knob),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private bool _isDragging;
    private int _dragHandle;           // 0 = S (Value), 1 = E (ValueEnd)
    private double _valueBeforeClick;  // restored when a click turns out to be the first half of a double click
    private double _endBeforeClick;
    private Rect _labelRect;

    /// <summary>Raised when the value changed (by the user or from code).</summary>
    public event Action<double>? ValueChanged;

    /// <summary>Raised when the second value changed.</summary>
    public event Action<double>? ValueEndChanged;

    /// <summary>Right click on a handle: the owner shows an input for that value (0 = S / the only value, 1 = E).</summary>
    public event Action<int>? ExactValueRequested;

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

    public double ValueEnd
    {
        get => (double)GetValue(ValueEndProperty);
        set => SetValue(ValueEndProperty, value);
    }

    public bool AllowRange
    {
        get => (bool)GetValue(AllowRangeProperty);
        set => SetValue(AllowRangeProperty, value);
    }

    /// <summary>True when two different values are set (S and E).</summary>
    public bool IsRange => AllowRange && Math.Abs(ValueEnd - Value) > 1e-9;

    public double DisplayedEndFraction
    {
        get => (double)GetValue(DisplayedEndFractionProperty);
        set => SetValue(DisplayedEndFractionProperty, value);
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

    // The gauge is a half-ellipse (wider than high), not a half-circle: Radius.Width / Radius.Height are its semi-axes.
    private (Point Center, Size Radius) Layout()
    {
        // The base line of the arc is near the bottom edge (room for the round end buttons).
        double baseY = ActualHeight - CapRadius - 2;
        double rx = Math.Max(10, ActualWidth / 2.0 - CapRadius - 1);
        double ry = Math.Max(10, baseY - ArcThickness / 2.0 - 2);
        return (new Point(ActualWidth / 2.0, baseY), new Size(rx, ry));
    }

    private static Point OnArc(Point center, Size radius, double fraction)
    {
        // fraction 0 = left end, 0.5 = straight up, 1 = right end.
        double angle = Math.PI * (1.0 - Math.Clamp(fraction, 0.0, 1.0));
        return new Point(center.X + radius.Width * Math.Cos(angle), center.Y - radius.Height * Math.Sin(angle));
    }

    private static StreamGeometry Arc(Point center, Size radius, double fromFraction, double toFraction)
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
        (Point center, Size radius) = Layout();

        // An invisible background makes the whole area react to the mouse.
        dc.DrawRectangle(HitBrush, null, new Rect(0, 0, ActualWidth, ActualHeight));

        double fraction = Math.Clamp(DisplayedFraction, 0.0, 1.0);
        double endFraction = Math.Clamp(DisplayedEndFraction, 0.0, 1.0);
        bool range = IsRange;

        var trackPen = new Pen(TrackBrush, ArcThickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var fillPen = new Pen(FillBrush, ArcThickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

        dc.DrawGeometry(null, trackPen, Arc(center, radius, 0, 1));

        // One value: filled from the start up to it. Two values: only the part between S and E (either way round).
        double low = range ? Math.Min(fraction, endFraction) : 0.0;
        double high = range ? Math.Max(fraction, endFraction) : fraction;

        if (high - low > 0.002)
            dc.DrawGeometry(null, fillPen, Arc(center, radius, low, high));

        if (range && high - low > 0.06)
            DrawDirectionArrow(dc, center, radius, (low + high) / 2.0, endFraction >= fraction ? 1 : -1);

        // Minimum / maximum buttons at the ends of the arc: lit when a value is at that end.
        DrawCap(dc, OnArc(center, radius, 0), fraction <= 0.002 || (range && endFraction <= 0.002));
        DrawCap(dc, OnArc(center, radius, 1), fraction >= 0.998 || (range && endFraction >= 0.998));

        // The middle anchor (a smaller button at the top of the arc): a click sets exactly half of the range.
        DrawCap(dc, OnArc(center, radius, 0.5), Math.Abs(fraction - 0.5) <= 0.002 || (range && Math.Abs(endFraction - 0.5) <= 0.002), MiddleCapRadius);

        // The handles: one dot for a single value, S and E for a range (E is the filled one).
        Point tip = OnArc(center, radius, fraction);
        dc.DrawEllipse(TextBrush, new Pen(FillBrush, 2), tip, 5, 5);

        if (range)
        {
            dc.DrawEllipse(FillBrush, new Pen(TextBrush, 1.6), OnArc(center, radius, endFraction), 5, 5);
            DrawHandleLetter(dc, center, radius, fraction, "S");
            DrawHandleLetter(dc, center, radius, endFraction, "E");
        }

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

    // A very small, quiet arrowhead in the middle of the filled part: the direction from S to E.
    private static void DrawDirectionArrow(DrawingContext dc, Point center, Size radius, double fraction, int sign)
    {
        double angle = Math.PI * (1.0 - fraction);
        double dx = Math.PI * radius.Width * Math.Sin(angle);   // derivative of the arc by the fraction
        double dy = Math.PI * radius.Height * Math.Cos(angle);
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1e-6)
            return;

        dx = dx / length * sign;
        dy = dy / length * sign;

        Point at = OnArc(center, radius, fraction);
        var tip = new Point(at.X + dx * 3.5, at.Y + dy * 3.5);
        var left = new Point(at.X - dx * 2.5 - dy * 3.0, at.Y - dy * 2.5 + dx * 3.0);
        var right = new Point(at.X - dx * 2.5 + dy * 3.0, at.Y - dy * 2.5 - dx * 3.0);

        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(tip, true, true);
            ctx.LineTo(left, true, false);
            ctx.LineTo(right, true, false);
        }
        geometry.Freeze();

        dc.DrawGeometry(ArrowBrush, null, geometry);
    }

    // "S" / "E" next to a handle, just inside the arc.
    private void DrawHandleLetter(DrawingContext dc, Point center, Size radius, double fraction, string letter)
    {
        Point on = OnArc(center, radius, fraction);
        var at = new Point(center.X + (on.X - center.X) * 0.78, center.Y + (on.Y - center.Y) * 0.78);

        var text = new FormattedText(
            letter,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI Semibold"),
            8,
            CaptionBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        dc.DrawText(text, new Point(at.X - text.Width / 2.0, at.Y - text.Height / 2.0));
    }

    private static void DrawCap(DrawingContext dc, Point at, bool lit, double radius = CapRadius)
    {
        dc.DrawEllipse(
            lit ? FillBrush : CapBrush,
            new Pen(FillBrush, 1.6),
            at,
            radius,
            radius);
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
        (Point center, Size radius) = Layout();

        // The written value: a plain click on it does nothing (an exact number is typed from the right-click menu).
        if (_labelRect.Contains(point))
        {
            e.Handled = true;
            return;
        }

        // Remember the values before this click: if it is the first half of a double click they are put back.
        if (e.ClickCount == 1)
        {
            _valueBeforeClick = Value;
            _endBeforeClick = ValueEnd;
        }

        double? anchor = null;

        // The round buttons at the ends and in the middle set the exact minimum / maximum / half.
        if (Distance(point, OnArc(center, radius, 0)) <= CapHitRadius)
            anchor = 0.0;
        else if (Distance(point, OnArc(center, radius, 1)) <= CapHitRadius)
            anchor = 1.0;
        else if (Distance(point, OnArc(center, radius, 0.5)) <= MiddleCapRadius + 4)
            anchor = 0.5;

        if (anchor is { } fixedFraction)
        {
            SetHandle(NearestHandle(fixedFraction), Minimum + fixedFraction * (Maximum - Minimum));
            e.Handled = true;
            return;
        }

        // Below the base line and the dead spot in the middle (under the number) do not react.
        if (point.Y > center.Y + CapRadius + 2 || Distance(point, center) < 8)
            return;

        double clicked = FractionAt(point);

        if (e.ClickCount == 2 && AllowRange)
        {
            ToggleRange(clicked);
            e.Handled = true;
            return;
        }

        _dragHandle = NearestHandle(clicked);
        _isDragging = true;
        CaptureMouse();
        SetHandle(_dragHandle, Minimum + clicked * (Maximum - Minimum));
        e.Handled = true;
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);

        // Right click on the knob: type an exact value for the nearest handle.
        ExactValueRequested?.Invoke(NearestHandle(FractionAt(e.GetPosition(this))));
        e.Handled = true;
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        // Handled, so that no context menu opens over the exact-value input of a right click.
        base.OnMouseRightButtonUp(e);
        e.Handled = true;
    }

    // Double click: the first value becomes a range (E appears under the pointer), or - on E - the range goes away.
    private void ToggleRange(double clicked)
    {
        // The first click of the double click already moved something: put it back.
        Value = _valueBeforeClick;
        ValueEnd = _endBeforeClick;

        double Fraction(double value) => Maximum > Minimum ? (value - Minimum) / (Maximum - Minimum) : 0.0;

        if (!IsRange)
        {
            double target = Minimum + clicked * (Maximum - Minimum);

            if (Math.Abs(target - Value) < 1e-9)
            {
                // Same place as S: step aside so that the range exists.
                double nudge = Step > 0 ? Step : (Maximum - Minimum) * 0.02;
                target = Value + nudge <= Maximum ? Value + nudge : Value - nudge;
            }

            ValueEnd = target;
            return;
        }

        // A range: a double click close to E removes it (E = S, the single value stays where S is).
        if (Math.Abs(clicked - Fraction(ValueEnd)) <= 0.06)
            ValueEnd = Value;
    }

    // Which handle a position is closer to: 0 = S (the only value), 1 = E.
    private int NearestHandle(double fraction)
    {
        if (!IsRange)
            return 0;

        double range = Maximum - Minimum;
        if (range <= 0)
            return 0;

        double s = (Value - Minimum) / range;
        double end = (ValueEnd - Minimum) / range;
        return Math.Abs(fraction - end) < Math.Abs(fraction - s) ? 1 : 0;
    }

    private void SetHandle(int handle, double value)
    {
        if (handle == 1)
            ValueEnd = value;
        else
            Value = value;
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

        Cursor = Cursors.Hand;
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
    private double FractionAt(Point point)
    {
        (Point center, Size radius) = Layout();

        // Normalised to the ellipse, so the angle is measured as on a circle.
        double dx = (point.X - center.X) / radius.Width;
        double dy = (center.Y - point.Y) / radius.Height;
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

        return Math.Clamp(fraction, 0.0, 1.0);
    }

    // CoerceValue clamps the result and applies the step.
    private void SetFromPoint(Point point) =>
        SetHandle(_dragHandle, Minimum + FractionAt(point) * (Maximum - Minimum));

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
        knob.InvalidateVisual(); // IsRange may have changed
    }

    private static void OnValueEndChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var knob = (Knob)d;
        knob.MoveArc();
        knob.ValueEndChanged?.Invoke((double)e.NewValue);
        knob.InvalidateVisual();
    }

    private static void OnLimitsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        d.CoerceValue(ValueProperty);
        d.CoerceValue(ValueEndProperty);
        ((Knob)d).MoveArc();
    }

    private double FractionOf(double value)
    {
        double range = Maximum - Minimum;
        return range > 0 ? Math.Clamp((value - Minimum) / range, 0.0, 1.0) : 0.0;
    }

    // The arc glides to the new value: a full sweep takes FullSweepSeconds, a shorter move proportionally less.
    private void MoveArc()
    {
        Glide(DisplayedFractionProperty, FractionOf(Value));
        Glide(DisplayedEndFractionProperty, FractionOf(ValueEnd));
    }

    private void Glide(DependencyProperty property, double target)
    {
        double from = (double)GetValue(property);
        double seconds = FullSweepSeconds * Math.Abs(target - from);

        if (!IsLoaded || seconds < 0.004)
        {
            BeginAnimation(property, null);
            SetValue(property, target);
            return;
        }

        BeginAnimation(
            property,
            new DoubleAnimation(from, target, TimeSpan.FromSeconds(seconds)) { FillBehavior = FillBehavior.HoldEnd });
    }

    private static Brush Frozen(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }
}
