using AmbientVenGen.Core.Composition;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace AmbientVenGen.Desktop.Controls;

/// <summary>Hit testing, dragging, hover, scrubbing and the cursor.</summary>
public sealed partial class TimelineControl
{
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

        // The repeated plays belong to the layer too: a click selects it and a drag moves the whole group.
        for (int play = 1; play < Math.Max(1, layer.Repeats); play++)
        {
            Rect repeat = PlayRect(layer, lane, play);
            if (point.X >= repeat.Left && point.X <= repeat.Right && point.Y >= repeat.Top && point.Y <= repeat.Bottom)
                return (layer, lane, HitZone.Body);
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

            if (HandleAddSlotClick(point))
            {
                e.Handled = true;
                return;
            }

            if (point.Y >= RulerHeight && lane >= 0 && lane < project.Layers.Count)
            {
                Layer layer = project.Layers[lane];
                int direction = ArrowAt(point, lane);

                SetCurrentValue(SelectedLayerProperty, layer);

                if (MuteRect(lane).Contains(point))
                {
                    layer.Muted = !layer.Muted;
                }
                else if (direction != 0)
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
                layer.StartSeconds = Math.Clamp(start, 0, Math.Max(0, total - layer.TotalSeconds));
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

                // With repeats the whole group must still fit: Duration * Repeats + Gap * (Repeats - 1) <= space.
                int plays = Math.Max(1, layer.Repeats);
                double room = (Math.Max(total, minEnd) - _originalStart - layer.RepeatGapSeconds * (plays - 1)) / plays;
                layer.DurationSeconds = Math.Max(MinClipSeconds, Math.Min(end - _originalStart, room));
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
        SetAddHover(false);

        if (_hoverLane != -1 || _hoverArrow != 0 || _hoverMute)
        {
            _hoverLane = -1;
            _hoverArrow = 0;
            _hoverMute = false;
            InvalidateContent();
        }
    }

    private void UpdateHover(Point point)
    {
        SetAddHover(InAddHeader(point));

        int lane = -1;
        int arrow = 0;
        bool mute = false;

        if (Project is { } project && point.X >= 0 && point.X < HeaderWidth && point.Y >= RulerHeight)
        {
            int candidate = (int)Math.Floor((point.Y - RulerHeight) / LaneHeight);

            if (candidate >= 0 && candidate < project.Layers.Count)
            {
                lane = candidate;
                arrow = ArrowAt(point, lane);
                mute = MuteRect(lane).Contains(point);
            }
        }

        if (lane != _hoverLane || arrow != _hoverArrow || mute != _hoverMute)
        {
            _hoverLane = lane;
            _hoverArrow = arrow;
            _hoverMute = mute;
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
            Cursor = _hoverArrow != 0 || _hoverMute || IsOverAddControl(point) ? Cursors.Hand : Cursors.Arrow;
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
