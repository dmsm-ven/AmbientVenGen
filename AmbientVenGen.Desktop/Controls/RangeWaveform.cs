using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace AmbientVenGen.Desktop.Controls;

/// <summary>
/// The whole recording as stepped bars (no repeats, no stretch) with a marked range that the user drags out with the
/// mouse. Start and end are in seconds of the file. A press near an edge of the range moves that edge, any other
/// press starts a new range.
/// </summary>
public sealed class RangeWaveform : FrameworkElement
{
    private const double Step = 2;
    private const double EdgeGrab = 6;

    public static readonly DependencyProperty PeaksProperty = DependencyProperty.Register(
        nameof(Peaks), typeof(float[]), typeof(RangeWaveform),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Length of the whole recording, seconds.</summary>
    public static readonly DependencyProperty DurationProperty = DependencyProperty.Register(
        nameof(Duration), typeof(double), typeof(RangeWaveform),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Start of the range, seconds (NaN: nothing marked).</summary>
    public static readonly DependencyProperty RangeStartProperty = DependencyProperty.Register(
        nameof(RangeStart), typeof(double), typeof(RangeWaveform),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>End of the range, seconds (NaN: nothing marked).</summary>
    public static readonly DependencyProperty RangeEndProperty = DependencyProperty.Register(
        nameof(RangeEnd), typeof(double), typeof(RangeWaveform),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    private static readonly Brush BarBrush = Frozen(Color.FromRgb(0x5B, 0x9B, 0xFF));
    private static readonly Brush InsideBrush = Frozen(Color.FromRgb(0xFF, 0xC1, 0x5C));
    private static readonly Brush ShadeBrush = Frozen(Color.FromArgb(0x2E, 0xFF, 0xC1, 0x5C));

    private enum Drag { None, New, Start, End }

    private Drag _drag;
    private double _anchor;

    public float[]? Peaks { get => (float[]?)GetValue(PeaksProperty); set => SetValue(PeaksProperty, value); }

    public double Duration { get => (double)GetValue(DurationProperty); set => SetValue(DurationProperty, value); }

    public double RangeStart { get => (double)GetValue(RangeStartProperty); set => SetValue(RangeStartProperty, value); }

    public double RangeEnd { get => (double)GetValue(RangeEndProperty); set => SetValue(RangeEndProperty, value); }

    private bool HasRange => !double.IsNaN(RangeStart) && !double.IsNaN(RangeEnd) && RangeEnd > RangeStart;

    private double ToX(double seconds) => seconds / Math.Max(0.001, Duration) * ActualWidth;

    private double ToSeconds(double x) => Math.Round(Math.Clamp(x / Math.Max(1, ActualWidth), 0, 1) * Duration, 2);

    protected override void OnRender(DrawingContext dc)
    {
        double width = ActualWidth;
        double height = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));

        if (width < Step || height < 4)
            return;

        float[]? peaks = Peaks;
        int columns = (int)(width / Step);
        double maxHeight = height - 4;
        double from = HasRange ? ToX(RangeStart) : double.NaN;
        double to = HasRange ? ToX(RangeEnd) : double.NaN;

        if (HasRange)
            dc.DrawRectangle(ShadeBrush, null, new Rect(from, 0, Math.Max(1, to - from), height));

        if (peaks is { Length: > 0 })
        {
            for (int c = 0; c < columns; c++)
            {
                int a = (int)((long)c * peaks.Length / columns);
                int b = Math.Max(a + 1, (int)((long)(c + 1) * peaks.Length / columns));

                float peak = 0;
                for (int i = a; i < b && i < peaks.Length; i++)
                    peak = Math.Max(peak, peaks[i]);

                double barHeight = Math.Clamp(peak * maxHeight, 2, maxHeight);
                double x = c * Step;
                bool inside = HasRange && x + Step / 2 >= from && x + Step / 2 <= to;

                dc.DrawRectangle(inside ? InsideBrush : BarBrush, null, new Rect(x, height - 2 - barHeight, Step - 0.5, barHeight));
            }
        }

        if (HasRange)
        {
            var pen = new Pen(InsideBrush, 2);
            dc.DrawLine(pen, new Point(from, 0), new Point(from, height));
            dc.DrawLine(pen, new Point(to, 0), new Point(to, height));
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        double x = e.GetPosition(this).X;

        if (HasRange && Math.Abs(x - ToX(RangeStart)) <= EdgeGrab)
            _drag = Drag.Start;
        else if (HasRange && Math.Abs(x - ToX(RangeEnd)) <= EdgeGrab)
            _drag = Drag.End;
        else
        {
            _drag = Drag.New;
            _anchor = ToSeconds(x);
            RangeStart = RangeEnd = _anchor;
        }

        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        double x = e.GetPosition(this).X;

        if (_drag == Drag.None)
        {
            Cursor = HasRange && (Math.Abs(x - ToX(RangeStart)) <= EdgeGrab || Math.Abs(x - ToX(RangeEnd)) <= EdgeGrab)
                ? Cursors.SizeWE
                : Cursors.Cross;
            return;
        }

        double t = ToSeconds(x);

        switch (_drag)
        {
            case Drag.New:
                RangeStart = Math.Min(_anchor, t);
                RangeEnd = Math.Max(_anchor, t);
                break;
            case Drag.Start:
                RangeStart = Math.Min(t, RangeEnd);
                break;
            case Drag.End:
                RangeEnd = Math.Max(t, RangeStart);
                break;
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _drag = Drag.None;
        ReleaseMouseCapture();
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
