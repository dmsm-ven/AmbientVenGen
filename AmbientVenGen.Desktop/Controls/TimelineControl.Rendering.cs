using AmbientVenGen.Core.Composition;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace AmbientVenGen.Desktop.Controls;

/// <summary>Drawing of the lanes, grid, headers and the text / colour helpers.</summary>
public sealed partial class TimelineControl
{
    // ---------------------------------------------------------------- rendering

    // Several quick changes (dragging, loading a template) are drawn once.
    private void InvalidateContent()
    {
        if (_contentQueued)
            return;

        _contentQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            _contentQueued = false;
            RenderContent();
        }));
    }

    private void RenderContent()
    {
        using DrawingContext dc = _contentVisual.RenderOpen();

        double width = ActualWidth;
        double height = ActualHeight;

        if (width <= 0 || height <= 0)
            return;

        // An invisible full-size rectangle so that empty areas receive mouse input.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));

        ProjectModel? project = Project;
        if (project is null)
            return;

        int layerCount = project.Layers.Count;
        int laneCount = Math.Max(layerCount + 1, (int)Math.Ceiling((height - RulerHeight) / LaneHeight));
        var trackArea = new Rect(HeaderWidth, RulerHeight, width - HeaderWidth, Math.Max(0, height - RulerHeight));

        DrawLaneBackgrounds(dc, project, laneCount, width, height);
        DrawGridLines(dc, trackArea);
        DrawProjectEnd(dc, trackArea);

        dc.PushClip(new RectangleGeometry(trackArea));
        for (int lane = 0; lane < layerCount; lane++)
        {
            Layer layer = project.Layers[lane];
            DrawLayerClips(dc, layer, lane, ReferenceEquals(layer, SelectedLayer));
        }
        dc.Pop();

        DrawHeaders(dc, project, laneCount);

        if (layerCount == 0)
        {
            FormattedText hint = MakeText(
                "Add a layer with the + button on the left to start building your track",
                14,
                Color.FromArgb(0x90, 0xC9, 0xC8, 0xD6));

            dc.DrawText(hint, new Point(
                trackArea.Left + (trackArea.Width - hint.Width) / 2.0,
                RulerHeight + LaneHeight * 1.5 - hint.Height / 2.0));
        }

        DrawRuler(dc, width);
        RenderAddSlot();
    }

    private void DrawLaneBackgrounds(DrawingContext dc, ProjectModel project, int laneCount, double width, double height)
    {
        for (int lane = 0; lane < laneCount; lane++)
        {
            double top = LaneTop(lane);
            bool selected = lane < project.Layers.Count && ReferenceEquals(project.Layers[lane], SelectedLayer);

            if (selected)
            {
                // The selected row: a bright tinted band that fades to the right, with accent edges.
                Color accent = TypeColor(project.Layers[lane].Type);

                var band = new LinearGradientBrush(WithAlpha(accent, 0x60), WithAlpha(accent, 0x26), 0);
                band.Freeze();
                dc.DrawRectangle(band, null, new Rect(0, top, width, LaneHeight));

                var edge = new Pen(BrushOf(WithAlpha(accent, 0xA6)), 1);
                dc.DrawLine(edge, new Point(0, top + 0.5), new Point(width, top + 0.5));
                dc.DrawLine(edge, new Point(0, top + LaneHeight - 0.5), new Point(width, top + LaneHeight - 0.5));
                continue;
            }

            Color color = Color.FromArgb(lane % 2 == 0 ? (byte)0x0C : (byte)0x05, 0xFF, 0xFF, 0xFF);
            dc.DrawRectangle(BrushOf(color), null, new Rect(0, top, width, LaneHeight));

            // Thin separator under the lane.
            dc.DrawLine(
                new Pen(BrushOf(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)), 1),
                new Point(0, top + LaneHeight - 0.5),
                new Point(width, top + LaneHeight - 0.5));
        }
    }

    private void DrawGridLines(DrawingContext dc, Rect trackArea)
    {
        GetTicks(out double major, out _, out _);
        var pen = new Pen(BrushOf(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF)), 1);

        long first = (long)Math.Ceiling(_viewStart / major);
        long last = (long)Math.Floor((_viewStart + _viewSpan) / major);

        for (long i = first; i <= last; i++)
        {
            double x = Math.Round(TimeToX(i * major)) + 0.5;
            if (x >= trackArea.Left && x <= trackArea.Right)
                dc.DrawLine(pen, new Point(x, trackArea.Top), new Point(x, trackArea.Bottom));
        }
    }

    // Everything after the end of the project is dimmed.
    private void DrawProjectEnd(DrawingContext dc, Rect trackArea)
    {
        double endX = TimeToX(Total);

        if (endX < trackArea.Right)
        {
            double left = Math.Max(endX, trackArea.Left);
            dc.DrawRectangle(
                BrushOf(Color.FromArgb(0x50, 0x00, 0x00, 0x00)),
                null,
                new Rect(left, trackArea.Top, trackArea.Right - left, trackArea.Height));
        }

        if (endX >= trackArea.Left && endX <= trackArea.Right)
        {
            dc.DrawLine(
                new Pen(BrushOf(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)), 1),
                new Point(Math.Round(endX) + 0.5, trackArea.Top),
                new Point(Math.Round(endX) + 0.5, trackArea.Bottom));
        }
    }

    private void DrawHeaders(DrawingContext dc, ProjectModel project, int laneCount)
    {
        for (int lane = 0; lane < laneCount; lane++)
        {
            double top = LaneTop(lane);

            Layer? laneLayer = lane < project.Layers.Count ? project.Layers[lane] : null;
            bool selected = laneLayer is not null && ReferenceEquals(laneLayer, SelectedLayer);

            // The selected row keeps its bright band; the other headers get a dark backing.
            if (!selected)
            {
                dc.DrawRectangle(
                    BrushOf(Color.FromArgb(0x30, 0x10, 0x10, 0x16)),
                    null,
                    new Rect(0, top, HeaderWidth, LaneHeight));

                if (lane == _hoverLane)
                {
                    dc.DrawRectangle(
                        BrushOf(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
                        null,
                        new Rect(0, top, HeaderWidth, LaneHeight));
                }
            }

            if (laneLayer is null)
                continue;

            Layer layer = laneLayer;
            Color accent = TypeColor(layer.Type);
            double opacity = layer.Muted ? 0.45 : 1.0;

            dc.PushOpacity(opacity);

            // Accent strip on the left.
            dc.DrawRoundedRectangle(
                BrushOf(WithAlpha(accent, selected ? (byte)0xFF : (byte)0xB0)),
                null,
                new Rect(8, top + (selected ? 8 : 14), selected ? 4 : 3, LaneHeight - (selected ? 16 : 28)),
                2,
                2);

            FormattedText name = MakeText(layer.Name, 13, Color.FromArgb(0xF0, 0xEC, 0xEC, 0xF2), bold: true, maxWidth: HeaderWidth - 96);
            dc.DrawText(name, new Point(20, top + LaneHeight / 2.0 - 17));

            string detail = layer.Type switch
            {
                LayerType.Noise => "Noise",
                LayerType.FieldRecord => layer.TypeLabel,
                _ => $"{layer.TypeLabel} · {layer.PitchHz:0.#} Hz"
            };
            FormattedText sub = MakeText(detail, 10.5, Color.FromArgb(0xA0, 0xC9, 0xC8, 0xD6), maxWidth: HeaderWidth - 96);
            dc.DrawText(sub, new Point(20, top + LaneHeight / 2.0 + 2));

            dc.Pop();

            if (lane == _hoverLane)
            {
                // Reorder arrows appear only while the mouse is over the header.
                DrawArrow(dc, lane, -1);
                DrawArrow(dc, lane, 1);
            }

            // The mute button ("M") is always there and always clickable: amber when the layer is muted.
            bool muteHot = lane == _hoverLane && _hoverMute;
            Rect badge = MuteRect(lane);
            Color fill = layer.Muted
                ? Color.FromArgb(muteHot ? (byte)0xFF : (byte)0xE0, 0xFF, 0xC1, 0x5C)
                : Color.FromArgb(muteHot ? (byte)0x40 : (byte)0x16, 0xFF, 0xFF, 0xFF);
            Color text = layer.Muted ? Color.FromRgb(0x20, 0x16, 0x08) : Color.FromArgb(0xB0, 0xC9, 0xC8, 0xD6);

            dc.DrawRoundedRectangle(
                BrushOf(fill),
                new Pen(BrushOf(Color.FromArgb(muteHot ? (byte)0x80 : (byte)0x30, 0xFF, 0xFF, 0xFF)), 1),
                badge,
                5,
                5);

            FormattedText m = MakeText("M", 11, text, bold: true);
            dc.DrawText(m, new Point(badge.Left + (badge.Width - m.Width) / 2.0, badge.Top + (badge.Height - m.Height) / 2.0));
        }

        // Vertical line between the headers and the track.
        dc.DrawLine(
            new Pen(BrushOf(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF)), 1),
            new Point(HeaderWidth - 0.5, RulerHeight),
            new Point(HeaderWidth - 0.5, ActualHeight));
    }

    private void DrawArrow(DrawingContext dc, int lane, int direction)
    {
        bool enabled = CanMove(lane, direction);
        bool hot = enabled && _hoverArrow == direction;
        Rect rect = ArrowRect(lane, direction);

        dc.PushOpacity(enabled ? 1.0 : 0.3);

        dc.DrawRoundedRectangle(
            BrushOf(Color.FromArgb(hot ? (byte)0x50 : (byte)0x1C, 0xFF, 0xFF, 0xFF)),
            new Pen(BrushOf(Color.FromArgb(hot ? (byte)0xC0 : (byte)0x38, 0xFF, 0xFF, 0xFF)), 1),
            rect,
            4,
            4);

        double cx = rect.Left + rect.Width / 2.0;
        double cy = rect.Top + rect.Height / 2.0;
        double apex = direction < 0 ? cy - 2.5 : cy + 2.5;
        double ends = direction < 0 ? cy + 2.5 : cy - 2.5;

        var chevron = new StreamGeometry();
        using (StreamGeometryContext ctx = chevron.Open())
        {
            ctx.BeginFigure(new Point(cx - 4, ends), false, false);
            ctx.LineTo(new Point(cx, apex), true, true);
            ctx.LineTo(new Point(cx + 4, ends), true, true);
        }
        chevron.Freeze();

        var pen = new Pen(BrushOf(Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF)), 1.6)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };

        dc.DrawGeometry(null, pen, chevron);
        dc.Pop();
    }

    // ---------------------------------------------------------------- text / color helpers

    private FormattedText MakeText(string text, double size, Color color, bool bold = false, double maxWidth = double.PositiveInfinity)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            bold ? UiFontBold : UiFont,
            size,
            BrushOf(color),
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        if (!double.IsInfinity(maxWidth))
        {
            formatted.MaxTextWidth = Math.Max(1, maxWidth);
            formatted.MaxLineCount = 1;
            formatted.Trimming = TextTrimming.CharacterEllipsis;
        }

        return formatted;
    }

    private static Color TypeColor(LayerType type) => type switch
    {
        LayerType.Noise => Color.FromRgb(0x46, 0xD2, 0xC8),
        LayerType.Pad => Color.FromRgb(0xF0, 0x8B, 0xC8),
        LayerType.FieldRecord => Color.FromRgb(0x8F, 0xD1, 0x6A),
        _ => Color.FromRgb(0x8B, 0x7C, 0xFF)
    };

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    private static SolidColorBrush BrushOf(Color color)
    {
        if (!BrushCache.TryGetValue(color, out SolidColorBrush? brush))
        {
            brush = new SolidColorBrush(color);
            brush.Freeze();
            BrushCache[color] = brush;
        }

        return brush;
    }
}
