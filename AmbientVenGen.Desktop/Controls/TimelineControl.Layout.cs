using AmbientVenGen.Core.Composition;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace AmbientVenGen.Desktop.Controls;

/// <summary>Timeline geometry: view, lanes, rectangles of clips, mute buttons, arrows.</summary>
public sealed partial class TimelineControl
{
    // ---------------------------------------------------------------- state / layout

    private double Total => Project?.DurationSeconds ?? 600;

    private double TrackWidth => Math.Max(1, ActualWidth - HeaderWidth - RightPadding);

    private double PixelsPerSecond => TrackWidth / _viewSpan;

    private double TimeToX(double seconds) => HeaderWidth + (seconds - _viewStart) * PixelsPerSecond;

    private double XToTime(double x) => _viewStart + (x - HeaderWidth) / PixelsPerSecond;

    private static double LaneTop(int lane) => RulerHeight + lane * LaneHeight;

    private Rect ClipRect(Layer layer, int lane) => new(
        TimeToX(layer.StartSeconds),
        LaneTop(lane) + LanePadding,
        Math.Max(2, layer.DurationSeconds * PixelsPerSecond),
        LaneHeight - 2 * LanePadding);

    // The rectangle of one play of a repeated layer (play 0 is ClipRect).
    private Rect PlayRect(Layer layer, int lane, int play)
    {
        Rect first = ClipRect(layer, lane);
        return new Rect(first.X + play * layer.PeriodSeconds * PixelsPerSecond, first.Y, first.Width, first.Height);
    }

    // The mute button is at the right edge of the header; the reorder arrows (shown on hover) are next to it.
    private static Rect MuteRect(int lane) => new(
        HeaderWidth - 36,
        LaneTop(lane) + (LaneHeight - 20) / 2.0,
        24,
        20);

    // The two arrows (up and down): 24 px wide, one above the other, left of the mute button.
    private static Rect ArrowRect(int lane, int direction) => new(
        HeaderWidth - 66,
        LaneTop(lane) + (LaneHeight - 30) / 2.0 + (direction > 0 ? 16 : 0),
        24,
        14);

    private bool CanMove(int lane, int direction)
    {
        int target = lane + direction;
        return Project is { } project && target >= 0 && target < project.Layers.Count;
    }

    /// <summary>-1 / +1 when the point is on an enabled arrow of the lane, otherwise 0.</summary>
    private int ArrowAt(Point point, int lane)
    {
        foreach (int direction in new[] { -1, 1 })
        {
            if (CanMove(lane, direction) && ArrowRect(lane, direction).Contains(point))
                return direction;
        }

        return 0;
    }

    private void ClampView()
    {
        double total = Total;
        double minSpan = Math.Min(MinViewSeconds, total);

        if (_fit)
        {
            _viewStart = 0;
            _viewSpan = total;
            return;
        }

        _viewSpan = Math.Clamp(_viewSpan, minSpan, total);
        _viewStart = Math.Clamp(_viewStart, 0, Math.Max(0, total - _viewSpan));

        if (_viewSpan >= total - 1e-6)
            _fit = true;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? 900 : availableSize.Width;
        // one free lane under the layers holds the "add layer" slot
        int lanes = Math.Max((Project?.Layers.Count ?? 0) + 1, MinVisibleLanes);

        return new Size(width, RulerHeight + lanes * LaneHeight + 6);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);

        ClampView();
        InvalidateContent();
        RenderPlayhead();
    }
}
