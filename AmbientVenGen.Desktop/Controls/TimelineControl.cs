using AmbientVenGen.Core.Composition;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace AmbientVenGen.Desktop.Controls;

/// <summary>
/// The layer timeline: a time ruler, one lane per layer, translucent clips that show their
/// loudness envelope, and the playhead. Everything is drawn by hand for full control of the look.
/// <para>Mouse: drag a clip to move it, drag its edges to resize, drag the round handles to change
/// the fades, click the ruler to move the playhead, Ctrl+wheel zooms, Shift+wheel scrolls,
/// Delete removes the selected layer.</para>
/// </summary>
public sealed class TimelineControl : FrameworkElement
{
    private const double HeaderWidth = 172;
    private const double RulerHeight = 30;
    private const double LaneHeight = 68;
    private const double LanePadding = 7;
    private const double RightPadding = 16;
    private const double EdgeGrab = 7;
    private const double HandleRadius = 5;
    private const double HandleInset = 10;
    private const double MinClipSeconds = 2;
    private const double MinViewSeconds = 20;
    private const int MinVisibleLanes = 5;

    private static readonly double[] TickSteps = [1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600];
    private static readonly int[] TickDivisions = [1, 2, 5, 5, 3, 6, 6, 4, 5, 5, 3, 6, 6];

    private static readonly Typeface UiFont = new(
        new FontFamily("Segoe UI Variable, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    private static readonly Typeface UiFontBold = new(
        new FontFamily("Segoe UI Variable, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    private static readonly Dictionary<Color, SolidColorBrush> BrushCache = new();

    public static readonly DependencyProperty ProjectProperty = DependencyProperty.Register(
        nameof(Project), typeof(ProjectModel), typeof(TimelineControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure, OnProjectChanged));

    public static readonly DependencyProperty SelectedLayerProperty = DependencyProperty.Register(
        nameof(SelectedLayer), typeof(Layer), typeof(TimelineControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectionChanged));

    public static readonly DependencyProperty PlayheadSecondsProperty = DependencyProperty.Register(
        nameof(PlayheadSeconds), typeof(double), typeof(TimelineControl),
        new FrameworkPropertyMetadata(0.0, OnPlayheadChanged));

    /// <summary>Executed with the new playhead time (double) when the user clicks or drags on the ruler.</summary>
    public static readonly DependencyProperty SeekCommandProperty = DependencyProperty.Register(
        nameof(SeekCommand), typeof(ICommand), typeof(TimelineControl), new PropertyMetadata(null));

    /// <summary>Executed when Delete is pressed on the timeline.</summary>
    public static readonly DependencyProperty DeleteCommandProperty = DependencyProperty.Register(
        nameof(DeleteCommand), typeof(ICommand), typeof(TimelineControl), new PropertyMetadata(null));

    private enum DragMode { None, Move, ResizeLeft, ResizeRight, FadeIn, FadeOut, Scrub }

    private enum HitZone { None, Body, LeftEdge, RightEdge, FadeInHandle, FadeOutHandle }

    private readonly DrawingVisual _contentVisual = new();
    private readonly DrawingVisual _playheadVisual = new();

    private bool _contentQueued;
    private bool _fit = true;
    private double _viewStart;
    private double _viewSpan = 600;

    private DragMode _mode = DragMode.None;
    private Layer? _dragLayer;

    // Header hover: the row under the mouse and which arrow (-1 up, +1 down, 0 none) is under it.
    private int _hoverLane = -1;
    private int _hoverArrow;
    private double _dragStartTime;
    private double _originalStart;
    private double _originalDuration;

    public ProjectModel? Project
    {
        get => (ProjectModel?)GetValue(ProjectProperty);
        set => SetValue(ProjectProperty, value);
    }

    public Layer? SelectedLayer
    {
        get => (Layer?)GetValue(SelectedLayerProperty);
        set => SetValue(SelectedLayerProperty, value);
    }

    public double PlayheadSeconds
    {
        get => (double)GetValue(PlayheadSecondsProperty);
        set => SetValue(PlayheadSecondsProperty, value);
    }

    public ICommand? SeekCommand
    {
        get => (ICommand?)GetValue(SeekCommandProperty);
        set => SetValue(SeekCommandProperty, value);
    }

    public ICommand? DeleteCommand
    {
        get => (ICommand?)GetValue(DeleteCommandProperty);
        set => SetValue(DeleteCommandProperty, value);
    }

    public TimelineControl()
    {
        AddVisualChild(_contentVisual);
        AddVisualChild(_playheadVisual);

        Focusable = true;
        FocusVisualStyle = null;
        ClipToBounds = true;
        ToolTip = "Drag clips to move, drag edges to resize, drag the round handles to set fades.\n"
                  + "Click the ruler to move the playhead. Ctrl+wheel: zoom. Shift+wheel: scroll. Del: remove layer.\n"
                  + "Hover a layer name and use the arrows (or Alt+Up/Down) to change the order of layers.";
    }

    protected override int VisualChildrenCount => 2;

    protected override Visual GetVisualChild(int index) => index == 0 ? _contentVisual : _playheadVisual;

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

    // The two arrows (up and down) live where the mute badge is: 24 px wide, one above the other.
    private static Rect ArrowRect(int lane, int direction) => new(
        HeaderWidth - 36,
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
        int lanes = Math.Max(Project?.Layers.Count ?? 0, MinVisibleLanes);

        return new Size(width, RulerHeight + lanes * LaneHeight + 6);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);

        ClampView();
        InvalidateContent();
        RenderPlayhead();
    }

    // ---------------------------------------------------------------- dependency property callbacks

    private static void OnProjectChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (TimelineControl)d;

        if (e.OldValue is ProjectModel oldProject)
            oldProject.Changed -= control.OnProjectContentChanged;

        if (e.NewValue is ProjectModel newProject)
            newProject.Changed += control.OnProjectContentChanged;

        control._fit = true;
        control.ClampView();
        control.InvalidateContent();
    }

    private static void OnSelectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((TimelineControl)d).InvalidateContent();

    private static void OnPlayheadChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (TimelineControl)d;
        double time = (double)e.NewValue;

        // While zoomed in, keep the playhead visible.
        if (!control._fit && control._mode == DragMode.None
            && (time > control._viewStart + control._viewSpan || time < control._viewStart))
        {
            control._viewStart = Math.Max(0, time - control._viewSpan * 0.05);
            control.ClampView();
            control.InvalidateContent();
        }

        control.RenderPlayhead();
    }

    private void OnProjectContentChanged()
    {
        InvalidateMeasure();
        ClampView();
        InvalidateContent();
        RenderPlayhead();
    }

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
        int laneCount = Math.Max(layerCount, (int)Math.Ceiling((height - RulerHeight) / LaneHeight));
        var trackArea = new Rect(HeaderWidth, RulerHeight, width - HeaderWidth, Math.Max(0, height - RulerHeight));

        DrawLaneBackgrounds(dc, project, laneCount, width, height);
        DrawGridLines(dc, trackArea);
        DrawProjectEnd(dc, trackArea);

        dc.PushClip(new RectangleGeometry(trackArea));
        for (int lane = 0; lane < layerCount; lane++)
        {
            Layer layer = project.Layers[lane];
            DrawClip(dc, layer, ClipRect(layer, lane), ReferenceEquals(layer, SelectedLayer));
        }
        dc.Pop();

        DrawHeaders(dc, project, laneCount);

        if (layerCount == 0)
        {
            FormattedText hint = MakeText(
                "Add a layer above to start building your track",
                14,
                Color.FromArgb(0x90, 0xC9, 0xC8, 0xD6));

            dc.DrawText(hint, new Point(
                trackArea.Left + (trackArea.Width - hint.Width) / 2.0,
                RulerHeight + LaneHeight * 1.5 - hint.Height / 2.0));
        }

        DrawRuler(dc, width);
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

            FormattedText name = MakeText(layer.Name, 13, Color.FromArgb(0xF0, 0xEC, 0xEC, 0xF2), bold: true, maxWidth: HeaderWidth - 64);
            dc.DrawText(name, new Point(20, top + LaneHeight / 2.0 - 17));

            string detail = layer.Type switch
            {
                LayerType.Noise => "Noise",
                LayerType.FieldRecord => $"{layer.TypeLabel} · {layer.SourceName}",
                _ => $"{layer.TypeLabel} · {layer.PitchHz:0.#} Hz"
            };
            FormattedText sub = MakeText(detail, 10.5, Color.FromArgb(0xA0, 0xC9, 0xC8, 0xD6), maxWidth: HeaderWidth - 64);
            dc.DrawText(sub, new Point(20, top + LaneHeight / 2.0 + 2));

            dc.Pop();

            if (lane == _hoverLane)
            {
                // Reorder arrows appear only while the mouse is over the header.
                DrawArrow(dc, lane, -1);
                DrawArrow(dc, lane, 1);
            }
            else
            {
                // The "M" mark is always there: amber when the layer is muted, a quiet outline otherwise.
                // (The mute switch itself is in the "Selected layer" panel.)
                var badge = new Rect(HeaderWidth - 36, top + (LaneHeight - 20) / 2.0, 24, 20);
                Color fill = layer.Muted ? Color.FromArgb(0xE0, 0xFF, 0xC1, 0x5C) : Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF);
                Color text = layer.Muted ? Color.FromRgb(0x20, 0x16, 0x08) : Color.FromArgb(0xB0, 0xC9, 0xC8, 0xD6);

                dc.DrawRoundedRectangle(
                    BrushOf(fill),
                    new Pen(BrushOf(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), 1),
                    badge,
                    5,
                    5);

                FormattedText m = MakeText("M", 11, text, bold: true);
                dc.DrawText(m, new Point(badge.Left + (badge.Width - m.Width) / 2.0, badge.Top + (badge.Height - m.Height) / 2.0));
            }
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

    private void DrawRuler(DrawingContext dc, double width)
    {
        dc.DrawRectangle(BrushOf(Color.FromArgb(0x58, 0x0C, 0x0C, 0x12)), null, new Rect(0, 0, width, RulerHeight));

        dc.DrawLine(
            new Pen(BrushOf(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), 1),
            new Point(0, RulerHeight - 0.5),
            new Point(width, RulerHeight - 0.5));

        FormattedText caption = MakeText("LAYERS", 10, Color.FromArgb(0x80, 0xC9, 0xC8, 0xD6), bold: true);
        dc.DrawText(caption, new Point(20, (RulerHeight - caption.Height) / 2.0));

        GetTicks(out double major, out double minor, out int divisions);

        var majorPen = new Pen(BrushOf(Color.FromArgb(0x90, 0xC9, 0xC8, 0xD6)), 1);
        var minorPen = new Pen(BrushOf(Color.FromArgb(0x48, 0xC9, 0xC8, 0xD6)), 1);

        dc.PushClip(new RectangleGeometry(new Rect(HeaderWidth, 0, width - HeaderWidth, RulerHeight)));

        long first = (long)Math.Ceiling(_viewStart / minor);
        long last = (long)Math.Floor((_viewStart + _viewSpan) / minor);

        for (long i = first; i <= last; i++)
        {
            double time = i * minor;
            double x = Math.Round(TimeToX(time)) + 0.5;
            bool isMajor = i % divisions == 0;

            if (isMajor)
            {
                dc.DrawLine(majorPen, new Point(x, RulerHeight - 11), new Point(x, RulerHeight - 1));

                FormattedText label = MakeText(FormatTime(time), 10.5, Color.FromArgb(0xB8, 0xC9, 0xC8, 0xD6));
                dc.DrawText(label, new Point(x + 4, 5));
            }
            else if (minor * PixelsPerSecond >= 8)
            {
                dc.DrawLine(minorPen, new Point(x, RulerHeight - 6), new Point(x, RulerHeight - 1));
            }
        }

        dc.Pop();
    }

    private void DrawClip(DrawingContext dc, Layer layer, Rect rect, bool selected)
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
        DrawEnvelope(dc, layer, rect, accent);

        double textX = Math.Max(rect.Left, HeaderWidth) + 10;
        double textWidth = rect.Right - textX - 8;

        if (textWidth > 24)
        {
            FormattedText title = MakeText(layer.Name, 12, Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF), bold: true, maxWidth: textWidth);
            dc.DrawText(title, new Point(textX, rect.Top + 5));
        }

        dc.Pop();

        if (selected)
            DrawFadeHandles(dc, layer, rect, accent);

        dc.Pop();
    }

    // The loudness envelope (level curve x fades) drawn as a translucent filled shape.
    private void DrawEnvelope(DrawingContext dc, Layer layer, Rect rect, Color accent)
    {
        double duration = layer.DurationSeconds;
        if (duration <= 0)
            return;

        double visibleFrom = Math.Max(0, _viewStart - layer.StartSeconds);
        double visibleTo = Math.Min(duration, _viewStart + _viewSpan - layer.StartSeconds);

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

            points.Add(new Point(TimeToX(layer.StartSeconds + t), baseline - gain * usable));
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

    private void RenderPlayhead()
    {
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

    // ---------------------------------------------------------------- ruler helpers

    private void GetTicks(out double major, out double minor, out int divisions)
    {
        int index = TickSteps.Length - 1;

        for (int i = 0; i < TickSteps.Length; i++)
        {
            if (TickSteps[i] * PixelsPerSecond >= 90)
            {
                index = i;
                break;
            }
        }

        major = TickSteps[index];
        divisions = TickDivisions[index];
        minor = major / divisions;
    }

    private static string FormatTime(double seconds)
    {
        int total = (int)Math.Round(seconds);

        return total >= 3600
            ? $"{total / 3600}:{total % 3600 / 60:00}:{total % 60:00}"
            : $"{total / 60}:{total % 60:00}";
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

    // ---------------------------------------------------------------- mouse / keyboard

    private (Layer? Layer, int Lane, HitZone Zone) HitTest(Point point)
    {
        ProjectModel? project = Project;

        if (project is null || point.X < HeaderWidth || point.Y < RulerHeight)
            return (null, -1, HitZone.None);

        int lane = (int)Math.Floor((point.Y - RulerHeight) / LaneHeight);
        if (lane < 0 || lane >= project.Layers.Count)
            return (null, -1, HitZone.None);

        Layer layer = project.Layers[lane];
        Rect rect = ClipRect(layer, lane);

        if (ReferenceEquals(layer, SelectedLayer))
        {
            if (Distance(point, FadeInHandle(layer, rect)) <= HandleRadius + 4)
                return (layer, lane, HitZone.FadeInHandle);

            if (Distance(point, FadeOutHandle(layer, rect)) <= HandleRadius + 4)
                return (layer, lane, HitZone.FadeOutHandle);
        }

        if (point.X < rect.Left - 3 || point.X > rect.Right + 3 || point.Y < rect.Top || point.Y > rect.Bottom)
            return (null, lane, HitZone.None);

        if (rect.Width >= 24)
        {
            if (Math.Abs(point.X - rect.Left) <= EdgeGrab)
                return (layer, lane, HitZone.LeftEdge);

            if (Math.Abs(point.X - rect.Right) <= EdgeGrab)
                return (layer, lane, HitZone.RightEdge);
        }

        return (layer, lane, HitZone.Body);
    }

    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static double Snap(double seconds) =>
        Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) ? seconds : Math.Round(seconds);

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        ProjectModel? project = Project;
        if (project is null)
            return;

        Focus();
        Point point = e.GetPosition(this);

        // Ruler: move the playhead.
        if (point.Y < RulerHeight && point.X >= HeaderWidth)
        {
            _mode = DragMode.Scrub;
            CaptureMouse();
            ScrubTo(point.X);
            e.Handled = true;
            return;
        }

        // Layer header: select; the arrows (visible on hover) move the layer up or down.
        if (point.X < HeaderWidth)
        {
            int lane = (int)Math.Floor((point.Y - RulerHeight) / LaneHeight);

            if (point.Y >= RulerHeight && lane >= 0 && lane < project.Layers.Count)
            {
                Layer layer = project.Layers[lane];
                int direction = ArrowAt(point, lane);

                SetCurrentValue(SelectedLayerProperty, layer);

                if (direction != 0)
                {
                    project.MoveLayer(layer, direction);
                    UpdateHover(point);
                }
            }
            else
            {
                SetCurrentValue(SelectedLayerProperty, null);
            }

            e.Handled = true;
            return;
        }

        var hit = HitTest(point);

        if (hit.Layer is null)
        {
            SetCurrentValue(SelectedLayerProperty, null);
            return;
        }

        SetCurrentValue(SelectedLayerProperty, hit.Layer);

        _dragLayer = hit.Layer;
        _dragStartTime = XToTime(point.X);
        _originalStart = hit.Layer.StartSeconds;
        _originalDuration = hit.Layer.DurationSeconds;

        _mode = hit.Zone switch
        {
            HitZone.LeftEdge => DragMode.ResizeLeft,
            HitZone.RightEdge => DragMode.ResizeRight,
            HitZone.FadeInHandle => DragMode.FadeIn,
            HitZone.FadeOutHandle => DragMode.FadeOut,
            _ => DragMode.Move
        };

        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        Point point = e.GetPosition(this);

        if (_mode == DragMode.None)
        {
            UpdateHover(point);
            UpdateCursor(point);
            return;
        }

        if (_mode == DragMode.Scrub)
        {
            ScrubTo(point.X);
            return;
        }

        Layer? layer = _dragLayer;
        if (layer is null)
            return;

        double total = Total;
        double time = Snap(XToTime(point.X));

        switch (_mode)
        {
            case DragMode.Move:
            {
                double start = Snap(_originalStart + (XToTime(point.X) - _dragStartTime));
                layer.StartSeconds = Math.Clamp(start, 0, Math.Max(0, total - _originalDuration));
                break;
            }

            case DragMode.ResizeLeft:
            {
                double end = _originalStart + _originalDuration;
                double start = Math.Clamp(time, 0, Math.Max(0, end - MinClipSeconds));

                layer.StartSeconds = start;
                layer.DurationSeconds = end - start;
                break;
            }

            case DragMode.ResizeRight:
            {
                double minEnd = _originalStart + MinClipSeconds;
                double end = Math.Clamp(time, minEnd, Math.Max(total, minEnd));

                layer.DurationSeconds = end - _originalStart;
                break;
            }

            case DragMode.FadeIn:
            {
                double limit = Math.Max(0, layer.DurationSeconds - layer.FadeOutSeconds);
                layer.FadeInSeconds = Math.Clamp(time - layer.StartSeconds, 0, limit);
                break;
            }

            case DragMode.FadeOut:
            {
                double limit = Math.Max(0, layer.DurationSeconds - layer.FadeInSeconds);
                layer.FadeOutSeconds = Math.Clamp(layer.StartSeconds + layer.DurationSeconds - time, 0, limit);
                break;
            }
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        EndDrag();
        UpdateCursor(e.GetPosition(this));
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);

        if (_hoverLane != -1 || _hoverArrow != 0)
        {
            _hoverLane = -1;
            _hoverArrow = 0;
            InvalidateContent();
        }
    }

    private void UpdateHover(Point point)
    {
        int lane = -1;
        int arrow = 0;

        if (Project is { } project && point.X >= 0 && point.X < HeaderWidth && point.Y >= RulerHeight)
        {
            int candidate = (int)Math.Floor((point.Y - RulerHeight) / LaneHeight);

            if (candidate >= 0 && candidate < project.Layers.Count)
            {
                lane = candidate;
                arrow = ArrowAt(point, lane);
            }
        }

        if (lane != _hoverLane || arrow != _hoverArrow)
        {
            _hoverLane = lane;
            _hoverArrow = arrow;
            InvalidateContent();
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _mode = DragMode.None;
        _dragLayer = null;
    }

    private void EndDrag()
    {
        _mode = DragMode.None;
        _dragLayer = null;

        if (IsMouseCaptured)
            ReleaseMouseCapture();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        if (Project is null)
            return;

        ModifierKeys modifiers = Keyboard.Modifiers;

        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            // Zoom around the mouse position.
            double total = Total;
            double minSpan = Math.Min(MinViewSeconds, total);
            double mouseX = Math.Max(e.GetPosition(this).X, HeaderWidth);
            double anchorTime = XToTime(mouseX);
            double fraction = (mouseX - HeaderWidth) / TrackWidth;
            double newSpan = Math.Clamp(_viewSpan * (e.Delta > 0 ? 0.8 : 1.25), minSpan, total);

            _fit = newSpan >= total - 1e-6;
            _viewSpan = newSpan;
            _viewStart = anchorTime - fraction * newSpan;

            ClampView();
            InvalidateContent();
            RenderPlayhead();
            e.Handled = true;
        }
        else if (modifiers.HasFlag(ModifierKeys.Shift) && !_fit)
        {
            _viewStart += (e.Delta > 0 ? -1 : 1) * _viewSpan * 0.1;

            ClampView();
            InvalidateContent();
            RenderPlayhead();
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (Keyboard.Modifiers == ModifierKeys.Alt
            && (key == Key.Up || key == Key.Down)
            && Project is { } project
            && SelectedLayer is { } selected)
        {
            project.MoveLayer(selected, key == Key.Up ? -1 : 1);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete && DeleteCommand is { } command && command.CanExecute(null))
        {
            command.Execute(null);
            e.Handled = true;
        }
    }

    private void ScrubTo(double x)
    {
        double time = Math.Clamp(XToTime(x), 0, Total);

        if (SeekCommand is { } command && command.CanExecute(time))
            command.Execute(time);
    }

    private void UpdateCursor(Point point)
    {
        if (point.Y < RulerHeight && point.X >= HeaderWidth)
        {
            Cursor = Cursors.Hand;
            return;
        }

        if (point.X < HeaderWidth)
        {
            Cursor = _hoverArrow != 0 ? Cursors.Hand : Cursors.Arrow;
            return;
        }

        Cursor = HitTest(point).Zone switch
        {
            HitZone.LeftEdge or HitZone.RightEdge or HitZone.FadeInHandle or HitZone.FadeOutHandle => Cursors.SizeWE,
            HitZone.Body => Cursors.Hand,
            _ => Cursors.Arrow
        };
    }
}
