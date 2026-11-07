using AmbientVenGen.Core.Audio;
using AmbientVenGen.Core.Composition;
using AmbientVenGen.Desktop.Services;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace AmbientVenGen.Desktop.Controls;

/// <summary>Clips with repeats, loudness envelopes and the fade handles.</summary>
public sealed partial class TimelineControl
{
    // A layer with repeats: the plays one after another, with a dashed "loop" link and a repeat sign in every gap.
    private void DrawLayerClips(DrawingContext dc, Layer layer, int lane, bool selected)
    {
        int plays = Math.Max(1, layer.Repeats);

        if (plays > 1)
        {
            Color accent = TypeColor(layer.Type);
            var linkPen = new Pen(BrushOf(WithAlpha(accent, 0xB0)), 1.4) { DashStyle = new DashStyle([3, 3], 0) };
            dc.PushOpacity(layer.Muted ? 0.35 : 1.0);

            for (int play = 0; play < plays - 1; play++)
            {
                Rect left = PlayRect(layer, lane, play);
                Rect right = PlayRect(layer, lane, play + 1);
                double gap = right.Left - left.Right;
                double y = left.Top + left.Height / 2.0;

                if (right.Left < HeaderWidth || left.Right > ActualWidth)
                    continue;

                if (gap >= 4)
                    dc.DrawLine(linkPen, new Point(left.Right, y), new Point(right.Left, y));

                // The sign sits in the gap when it fits, otherwise on the joint of the two plays.
                FormattedText sign = MakeText("\u21BB", 15, Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF), bold: true);
                double cx = gap >= sign.Width + 6 ? left.Right + gap / 2.0 : left.Right;
                var badge = new Rect(cx - sign.Width / 2.0 - 3, y - sign.Height / 2.0 - 1, sign.Width + 6, sign.Height + 2);
                dc.DrawRoundedRectangle(BrushOf(WithAlpha(accent, 0xE0)), null, badge, 5, 5);
                dc.DrawText(sign, new Point(cx - sign.Width / 2.0, y - sign.Height / 2.0));
            }

            dc.Pop();

            for (int play = plays - 1; play >= 1; play--)
                DrawClip(dc, layer, PlayRect(layer, lane, play), selected, play);
        }

        DrawClip(dc, layer, ClipRect(layer, lane), selected, 0);
    }

    private void DrawClip(DrawingContext dc, Layer layer, Rect rect, bool selected, int play = 0)
    {
        if (rect.Right < HeaderWidth || rect.Left > ActualWidth)
            return;

        Color accent = TypeColor(layer.Type);

        dc.PushOpacity(layer.Muted ? 0.35 : 1.0);

        if (selected)
        {
            // Soft glow around the selected clip.
            dc.DrawRoundedRectangle(
                null,
                new Pen(BrushOf(WithAlpha(accent, 0x48)), 6),
                new Rect(rect.X - 1, rect.Y - 1, rect.Width + 2, rect.Height + 2),
                10,
                10);
        }

        var fill = new LinearGradientBrush(WithAlpha(accent, 0x78), WithAlpha(accent, 0x22), 90);
        fill.Freeze();

        var border = new Pen(BrushOf(WithAlpha(accent, selected ? (byte)0xFF : (byte)0xA8)), selected ? 1.6 : 1.0);
        dc.DrawRoundedRectangle(fill, border, rect, 8, 8);

        dc.PushClip(new RectangleGeometry(rect, 8, 8));

        // Loudness bars of one play, under the envelope; repeats show the same bars.
        if (ShowSpectrogram && Spectrograms is { } spectrograms)
        {
            if (spectrograms.GetData(layer) is { } bars)
                DrawBars(dc, bars, rect);
            else if (spectrograms.IsPending(layer))
                DrawAnalysing(dc, rect);
        }

        DrawEnvelope(dc, layer, rect, accent, play * layer.PeriodSeconds);

        double textX = Math.Max(rect.Left, HeaderWidth) + 10;
        double textWidth = rect.Right - textX - 8;

        if (textWidth > 24)
        {
            string caption = play == 0
                ? (layer.Repeats > 1 ? $"{layer.Name}  \u21BB {layer.Repeats}\u00D7" : layer.Name)
                : $"\u21BB {play + 1}/{layer.Repeats}";
            FormattedText title = MakeText(caption, 12, Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF), bold: true, maxWidth: textWidth);
            dc.DrawText(title, new Point(textX, rect.Top + 5));
        }

        dc.Pop();

        if (selected && play == 0)
            DrawFadeHandles(dc, layer, rect, accent);

        dc.Pop();
    }

    // The same stepped bars as the thumbnails of the preset library: the same width of a bar and of the step,
    // the colour of the bars, bottom aligned, at least 2 px high. The height follows the loudest moment of the layer.
    private Brush _barBrush = CreateBarBrush(Color.FromRgb(0x5B, 0x9B, 0xFF));

    private static Brush CreateBarBrush(Color color)
    {
        var brush = new SolidColorBrush(Color.FromArgb(0xB0, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    private void OnSpectrogramColorChanged()
    {
        _barBrush = CreateBarBrush(SpectrogramColor);
        InvalidateContent();
    }

    // Shown while the bars of a changed layer are being prepared (they can take a while).
    private void DrawAnalysing(DrawingContext dc, Rect rect)
    {
        if (rect.Width < 90)
            return;

        FormattedText text = MakeText("analysing\u2026", 11, WithAlpha(SpectrogramColor, 0xE0));
        dc.DrawText(text, new Point(rect.Left + 8, rect.Bottom - text.Height - 4));
    }

    private void DrawBars(DrawingContext dc, SpectrogramData data, Rect rect)
    {
        if (data.Columns == 0 || data.Max <= 0 || rect.Width <= 0)
            return;

        const double step = WaveformRenderer.StepWidth;
        double maxHeight = rect.Height - 4;
        double from = Math.Max(rect.Left, HeaderWidth);
        double to = Math.Min(rect.Right, ActualWidth);

        // bars are placed on a fixed grid so that they do not shimmer while the view scrolls
        for (double x = Math.Floor((from - rect.Left) / step) * step + rect.Left; x < to; x += step)
        {
            int first = (int)Math.Clamp((x - rect.Left) / rect.Width * data.Columns, 0, data.Columns - 1);
            int last = (int)Math.Clamp((x + step - rect.Left) / rect.Width * data.Columns, first, data.Columns - 1);

            float peak = 0;
            for (int i = first; i <= last; i++)
                peak = Math.Max(peak, data.Peaks[i]);

            double height = Math.Clamp(peak / data.Max * maxHeight, 2, maxHeight);
            dc.DrawRectangle(_barBrush, null, new Rect(x, rect.Bottom - 2 - height, step - 0.5, height));
        }
    }

    // The loudness envelope (level curve x fades) drawn as a translucent filled shape.
    private void DrawEnvelope(DrawingContext dc, Layer layer, Rect rect, Color accent, double offsetSeconds = 0)
    {
        double start = layer.StartSeconds + offsetSeconds;
        double duration = layer.DurationSeconds;
        if (duration <= 0)
            return;

        double visibleFrom = Math.Max(0, _viewStart - start);
        double visibleTo = Math.Min(duration, _viewStart + _viewSpan - start);

        if (visibleTo <= visibleFrom)
            return;

        LayerSpec spec = layer.ToSpec();
        double baseline = rect.Bottom - 5;
        double usable = rect.Height - 24;
        int segments = (int)Math.Clamp((visibleTo - visibleFrom) * PixelsPerSecond / 3.0, 16, 600);

        var points = new List<Point>(segments + 1);

        for (int i = 0; i <= segments; i++)
        {
            double t = visibleFrom + (visibleTo - visibleFrom) * i / segments;
            double gain = Math.Clamp(spec.GainAt(t), 0.0, 1.0);

            points.Add(new Point(TimeToX(start + t), baseline - gain * usable));
        }

        var area = new StreamGeometry();
        using (StreamGeometryContext ctx = area.Open())
        {
            ctx.BeginFigure(new Point(points[0].X, baseline), true, true);
            ctx.PolyLineTo(points, false, false);
            ctx.LineTo(new Point(points[points.Count - 1].X, baseline), false, false);
        }
        area.Freeze();

        var line = new StreamGeometry();
        using (StreamGeometryContext ctx = line.Open())
        {
            ctx.BeginFigure(points[0], false, false);
            ctx.PolyLineTo(points, true, true);
        }
        line.Freeze();

        var areaBrush = new LinearGradientBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF), 90);
        areaBrush.Freeze();

        dc.DrawGeometry(areaBrush, null, area);
        dc.DrawGeometry(null, new Pen(BrushOf(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)), 1.4), line);
    }

    private void DrawFadeHandles(DrawingContext dc, Layer layer, Rect rect, Color accent)
    {
        var pen = new Pen(BrushOf(WithAlpha(accent, 0xFF)), 1.6);
        Brush fill = BrushOf(Color.FromArgb(0xF5, 0xFF, 0xFF, 0xFF));

        Point fadeIn = FadeInHandle(layer, rect);
        Point fadeOut = FadeOutHandle(layer, rect);

        if (fadeIn.X >= HeaderWidth)
            dc.DrawEllipse(fill, pen, fadeIn, HandleRadius, HandleRadius);

        if (fadeOut.X >= HeaderWidth)
            dc.DrawEllipse(fill, pen, fadeOut, HandleRadius, HandleRadius);
    }

    private Point FadeInHandle(Layer layer, Rect rect) =>
        new(rect.Left + layer.FadeInSeconds * PixelsPerSecond, rect.Top + HandleInset);

    private Point FadeOutHandle(Layer layer, Rect rect) =>
        new(rect.Right - layer.FadeOutSeconds * PixelsPerSecond, rect.Top + HandleInset);
}
