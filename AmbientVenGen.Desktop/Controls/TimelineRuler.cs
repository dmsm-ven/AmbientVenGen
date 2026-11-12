using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace AmbientVenGen.Desktop.Controls;

/// <summary>
/// The second (bottom) time ruler of the timeline. The timeline scrolls vertically when there are many layers, and
/// its own ruler goes out of sight; this one is always visible and moves the playhead the same way.
/// </summary>
public sealed class TimelineRuler : FrameworkElement
{
    public static readonly DependencyProperty TimelineProperty = DependencyProperty.Register(
        nameof(Timeline), typeof(TimelineControl), typeof(TimelineRuler),
        new PropertyMetadata(null, (d, e) => ((TimelineRuler)d).OnTimelineChanged((TimelineControl?)e.OldValue, (TimelineControl?)e.NewValue)));

    public TimelineControl? Timeline
    {
        get => (TimelineControl?)GetValue(TimelineProperty);
        set => SetValue(TimelineProperty, value);
    }

    private bool _scrubbing;

    public TimelineRuler()
    {
        Cursor = Cursors.Hand;
    }

    private void OnTimelineChanged(TimelineControl? old, TimelineControl? value)
    {
        if (old is not null)
            old.ViewChanged -= InvalidateVisual;

        if (value is not null)
            value.ViewChanged += InvalidateVisual;

        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, TimelineControl.RulerStripHeight);

    protected override void OnRender(DrawingContext dc)
    {
        if (ActualWidth <= 0)
            return;

        // Transparent background: the whole strip takes the mouse, not only the ticks.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        Timeline?.DrawRuler(dc, ActualWidth, bottom: true);
        Timeline?.DrawPlayheadMarker(dc, ActualHeight);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        if (Timeline is null)
            return;

        _scrubbing = true;
        CaptureMouse();
        Timeline.BeginScrub(e.GetPosition(this).X);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_scrubbing)
            Timeline?.ContinueScrub(e.GetPosition(this).X);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        Finish();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        Finish();
    }

    private void Finish()
    {
        if (!_scrubbing)
            return;

        _scrubbing = false;
        Timeline?.EndScrub();

        if (IsMouseCaptured)
            ReleaseMouseCapture();
    }
}
