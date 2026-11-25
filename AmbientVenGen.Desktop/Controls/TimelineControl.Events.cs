using AmbientVenGen.Core.Composition;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace AmbientVenGen.Desktop.Controls;

/// <summary>Event markers (one-shot effects) on the clips: drawing, click, moving and drag&amp;drop from the Events tab.</summary>
public sealed partial class TimelineControl
{
    /// <summary>The data format of a drag from the Events tab (the value is the name of the <see cref="EventType"/>).</summary>
    public const string EventDragFormat = "AmbientForge.EventType";

    /// <summary>A marker was clicked: the layer and its event (the Events tab shows it).</summary>
    public event Action<Layer, LayerEvent>? EventActivated;

    /// <summary>An event was dropped on a layer from the Events tab.</summary>
    public event Action<Layer, LayerEvent>? EventAdded;

    private LayerEvent? _eventDrag;
    private Layer? _eventDragLayer;
    private int _eventDragPlay;
    private (int Lane, double X)? _dropPreview;

    private static Color EventColor(EventType type) => type switch
    {
        EventType.Stutter => Color.FromRgb(0xFF, 0xB0, 0x70),
        EventType.FilterSweep => Color.FromRgb(0x8F, 0xE3, 0xA0),
        EventType.Freeze => Color.FromRgb(0xC3, 0xA6, 0xFF),
        EventType.PitchDive => Color.FromRgb(0xFF, 0x8F, 0xA3),
        EventType.SwellDuck => Color.FromRgb(0xFF, 0xE0, 0x8A),
        _ => Color.FromRgb(0x7F, 0xD1, 0xFF)
    };

    private Rect EventMarkerRect(Rect play, LayerEvent item) =>
        new(play.Left + item.StartSeconds * PixelsPerSecond - 7, play.Top + 3, 14, 14);

    // The markers lie over the clip (not clipped by it: an event at 0 s sits on the edge); a line goes down from each.
    private void DrawEventMarkers(DrawingContext dc, Layer layer, int lane)
    {
        int plays = Math.Max(1, layer.Repeats);
        dc.PushOpacity(layer.Muted ? 0.35 : 1.0);

        foreach (LayerEvent item in layer.Events)
        {
            Color color = EventColor(item.Type);
            double alpha = item.Muted ? 0.4 : 1.0;

            for (int play = 0; play < plays; play++)
            {
                Rect clip = PlayRect(layer, lane, play);
                Rect marker = EventMarkerRect(clip, item);

                if (marker.Right < HeaderWidth || marker.Left > ActualWidth)
                    continue;

                double cx = marker.Left + marker.Width / 2.0;
                var linePen = new Pen(BrushOf(WithAlpha(color, (byte)(0x70 * alpha))), 1);
                dc.DrawLine(linePen, new Point(cx, marker.Bottom), new Point(cx, clip.Bottom - 1));

                var frame = item.IsSelected ? new Pen(Brushes.White, 1.8) : new Pen(BrushOf(Color.FromArgb(0xC0, 0x10, 0x10, 0x18)), 1);
                dc.DrawRoundedRectangle(BrushOf(WithAlpha(color, (byte)(0xF0 * alpha))), frame, marker, 4, 4);

                // the glyph: rings = an echo, bars = a stutter
                var ink = new Pen(BrushOf(Color.FromArgb(0xE0, 0x18, 0x18, 0x24)), 1.3);
                var center = new Point(cx, marker.Top + marker.Height / 2.0);

                if (item.Type == EventType.Stutter)
                {
                    dc.DrawLine(ink, new Point(cx - 3.5, center.Y - 2.5), new Point(cx - 3.5, center.Y + 2.5));
                    dc.DrawLine(ink, new Point(cx, center.Y - 4), new Point(cx, center.Y + 4));
                    dc.DrawLine(ink, new Point(cx + 3.5, center.Y - 2.5), new Point(cx + 3.5, center.Y + 2.5));
                }
                else if (item.Type == EventType.PitchDive)
                {
                    dc.DrawLine(ink, new Point(cx, center.Y - 4.5), new Point(cx, center.Y + 4.5));
                    dc.DrawLine(ink, new Point(cx - 3.5, center.Y + 1), new Point(cx, center.Y + 4.5));
                    dc.DrawLine(ink, new Point(cx + 3.5, center.Y + 1), new Point(cx, center.Y + 4.5));
                }
                else if (item.Type == EventType.SwellDuck)
                {
                    dc.DrawLine(ink, new Point(cx - 4.5, center.Y - 3), new Point(cx, center.Y + 3));
                    dc.DrawLine(ink, new Point(cx, center.Y + 3), new Point(cx + 4.5, center.Y - 3));
                }
                else if (item.Type == EventType.Freeze)
                {
                    dc.DrawLine(ink, new Point(cx, center.Y - 4.5), new Point(cx, center.Y + 4.5));
                    dc.DrawLine(ink, new Point(cx - 4, center.Y - 2.3), new Point(cx + 4, center.Y + 2.3));
                    dc.DrawLine(ink, new Point(cx - 4, center.Y + 2.3), new Point(cx + 4, center.Y - 2.3));
                }
                else if (item.Type == EventType.FilterSweep)
                {
                    dc.DrawLine(ink, new Point(cx - 4.5, center.Y - 3), new Point(cx - 1, center.Y - 3));
                    dc.DrawLine(ink, new Point(cx - 1, center.Y - 3), new Point(cx + 4.5, center.Y + 3));
                }
                else
                {
                    dc.DrawEllipse(null, ink, center, 1.6, 1.6);
                    dc.DrawEllipse(null, ink, center, 4.6, 4.6);
                }
            }
        }

        // where a dragged event would land
        if (_dropPreview is { } drop && drop.Lane == lane)
        {
            var ghost = new Pen(BrushOf(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF)), 1.4) { DashStyle = new DashStyle([3, 3], 0) };
            Rect clip = ClipRect(layer, lane);
            dc.DrawLine(ghost, new Point(drop.X, clip.Top), new Point(drop.X, clip.Bottom));
        }

        dc.Pop();
    }

    private (Layer Layer, LayerEvent Event, int Play)? EventMarkerAt(Point point)
    {
        if (Project is not { } project || point.X < HeaderWidth)
            return null;

        int lane = (int)Math.Floor((point.Y - RulerHeight) / LaneHeight);
        if (lane < 0 || lane >= project.Layers.Count)
            return null;

        Layer layer = project.Layers[lane];
        int plays = Math.Max(1, layer.Repeats);

        // the last one in the list is on top
        for (int index = layer.Events.Count - 1; index >= 0; index--)
        {
            for (int play = 0; play < plays; play++)
            {
                Rect marker = EventMarkerRect(PlayRect(layer, lane, play), layer.Events[index]);
                marker.Inflate(2, 2);

                if (marker.Contains(point))
                    return (layer, layer.Events[index], play);
            }
        }

        return null;
    }

    // A click on a marker picks the event (the Events tab shows it); a drag moves it along the clip.
    private bool TryBeginEventDrag(Point point)
    {
        if (EventMarkerAt(point) is not { } marker)
            return false;

        SetCurrentValue(SelectedLayerProperty, marker.Layer);
        EventActivated?.Invoke(marker.Layer, marker.Event);

        _eventDrag = marker.Event;
        _eventDragLayer = marker.Layer;
        _eventDragPlay = marker.Play;
        _mode = DragMode.MoveEvent;
        CaptureMouse();
        return true;
    }

    private void MoveEventTo(double x)
    {
        if (_eventDrag is null || _eventDragLayer is not { } layer)
            return;

        double local = XToTime(x) - layer.StartSeconds - _eventDragPlay * layer.PeriodSeconds;
        _eventDrag.StartSeconds = Math.Round(Math.Clamp(local, 0, layer.DurationSeconds) * 20.0) / 20.0;
    }

    private void EndEventDrag()
    {
        _eventDrag = null;
        _eventDragLayer = null;
    }

    // ---- drag&drop from the Events tab

    private int LaneAt(Point point)
    {
        if (Project is not { } project || point.X < HeaderWidth || point.Y < RulerHeight)
            return -1;

        int lane = (int)Math.Floor((point.Y - RulerHeight) / LaneHeight);
        return lane >= 0 && lane < project.Layers.Count ? lane : -1;
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);

        Point point = e.GetPosition(this);
        int lane = e.Data.GetDataPresent(EventDragFormat) ? LaneAt(point) : -1;

        e.Effects = lane >= 0 ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;

        var preview = lane >= 0 ? ((int Lane, double X)?)(lane, point.X) : null;
        if (_dropPreview != preview)
        {
            _dropPreview = preview;
            InvalidateContent();
        }
    }

    protected override void OnDragLeave(DragEventArgs e)
    {
        base.OnDragLeave(e);
        ClearDropPreview();
    }

    protected override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        ClearDropPreview();

        Point point = e.GetPosition(this);
        int lane = LaneAt(point);

        if (lane < 0
            || Project is not { } project
            || e.Data.GetData(EventDragFormat) is not string name
            || !Enum.TryParse(name, out EventType type))
            return;

        Layer layer = project.Layers[lane];

        // the moment inside a play of the layer (a drop on a repeat counts for that repeat)
        double relative = Math.Max(0, XToTime(point.X) - layer.StartSeconds);
        double period = layer.PeriodSeconds;
        double within = period > 0 ? relative % period : relative;
        within = Math.Round(Math.Min(within, layer.DurationSeconds) * 20.0) / 20.0;

        LayerEvent item = LayerEvent.Create(type, within);
        layer.Events.Add(item);

        SetCurrentValue(SelectedLayerProperty, layer);
        EventAdded?.Invoke(layer, item);
        e.Handled = true;
    }

    private void ClearDropPreview()
    {
        if (_dropPreview is null)
            return;

        _dropPreview = null;
        InvalidateContent();
    }
}
