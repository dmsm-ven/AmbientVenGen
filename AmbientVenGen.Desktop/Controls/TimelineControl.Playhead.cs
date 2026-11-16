using AmbientVenGen.Core.Composition;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace AmbientVenGen.Desktop.Controls;

/// <summary>The playhead, drawn on its own visual so that moving it does not redraw the clips.</summary>
public sealed partial class TimelineControl
{
    private void RenderPlayhead()
    {
        ViewChanged?.Invoke();

        using DrawingContext dc = _playheadVisual.RenderOpen();

        double width = ActualWidth;
        double height = ActualHeight;

        if (width <= 0 || height <= 0 || Project is null)
            return;

        double x = Math.Round(TimeToX(PlayheadSeconds)) + 0.5;

        if (x < HeaderWidth || x > width - RightPadding + 1)
            return;

        // Soft glow, crisp line, small marker on the ruler.
        dc.DrawRectangle(
            BrushOf(Color.FromArgb(0x26, 0x8B, 0x7C, 0xFF)),
            null,
            new Rect(x - 4, RulerHeight, 8, height - RulerHeight));

        dc.DrawLine(
            new Pen(BrushOf(Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF)), 1.5),
            new Point(x, 2),
            new Point(x, height));

        var marker = new StreamGeometry();
        using (StreamGeometryContext ctx = marker.Open())
        {
            ctx.BeginFigure(new Point(x - 6, 2), true, true);
            ctx.LineTo(new Point(x + 6, 2), true, false);
            ctx.LineTo(new Point(x, 13), true, false);
        }
        marker.Freeze();

        dc.DrawGeometry(BrushOf(Color.FromRgb(0xB7, 0xAF, 0xE9)), null, marker);
    }

    // The playhead on the bottom ruler: a line and a small triangle pointing up.
    internal void DrawPlayheadMarker(DrawingContext dc, double height, bool top = false)
    {
        if (Project is null)
            return;

        double x = Math.Round(TimeToX(PlayheadSeconds)) + 0.5;

        if (x < HeaderWidth || x > ActualWidth - RightPadding + 1)
            return;

        var markerPen = new Pen(BrushOf(Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF)), 1.5);
        var marker = new StreamGeometry();

        if (top)
        {
            // The pinned top ruler: the triangle points down, the line goes to the bottom edge.
            dc.DrawLine(markerPen, new Point(x, 2), new Point(x, height));
            using StreamGeometryContext topCtx = marker.Open();
            topCtx.BeginFigure(new Point(x - 6, 2), true, true);
            topCtx.LineTo(new Point(x + 6, 2), true, false);
            topCtx.LineTo(new Point(x, 13), true, false);
        }
        else
        {
            dc.DrawLine(markerPen, new Point(x, 0), new Point(x, height - 8));
            using StreamGeometryContext ctx = marker.Open();
            ctx.BeginFigure(new Point(x - 6, height - 2), true, true);
            ctx.LineTo(new Point(x + 6, height - 2), true, false);
            ctx.LineTo(new Point(x, height - 12), true, false);
        }
        marker.Freeze();

        dc.DrawGeometry(BrushOf(Color.FromRgb(0xB7, 0xAF, 0xE9)), null, marker);
    }
}
