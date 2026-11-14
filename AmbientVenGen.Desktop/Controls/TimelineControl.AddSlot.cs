using AmbientVenGen.Core.Composition;
using AmbientVenGen.Desktop.ViewModels;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace AmbientVenGen.Desktop.Controls;

/// <summary>
/// The "add layer" slot: the first free lane under the layers. Its header holds a big "+".
/// The slot is 25% opaque and while the mouse is over its header it is fully opaque, and fades back in 0.15 s.
/// </summary>
public sealed partial class TimelineControl
{
    private const double RestOpacity = 0.25;
    private const double FadeBackSeconds = 0.15;

    /// <summary>"+" was clicked: ask for the type of the new layer.</summary>
    public event Action? AddLayerRequested;

    private readonly DrawingVisual _addVisual = new();
    private readonly DispatcherTimer _addTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _addClock = new();

    private bool _hoverAddSlot;
    private double _addOpacity = RestOpacity;
    private double _lastAddTick;

    private void InitAddSlot()
    {
        AddVisualChild(_addVisual);
        _addTimer.Tick += (_, _) => AnimateAddSlot();
    }

    private int AddSlotLane => Project?.Layers.Count ?? 0;

    private static Rect PlusRect(int lane) => new(14, LaneTop(lane) + (LaneHeight - 40) / 2.0, 40, 40);

    private bool InAddHeader(Point point) =>
        Project is not null
        && point.X >= 0 && point.X < HeaderWidth
        && point.Y >= LaneTop(AddSlotLane) && point.Y < LaneTop(AddSlotLane) + LaneHeight;

    /// <summary>Mouse click in the header of the free lane; returns true when it was handled.</summary>
    private bool HandleAddSlotClick(Point point)
    {
        if (!InAddHeader(point))
            return false;

        int lane = AddSlotLane;

        if (PlusRect(lane).Contains(point))
            AddLayerRequested?.Invoke();

        return true;
    }

    private bool IsOverAddControl(Point point) =>
        InAddHeader(point) && PlusRect(AddSlotLane).Contains(point);

    private void SetAddHover(bool hover)
    {
        if (hover == _hoverAddSlot)
            return;

        _hoverAddSlot = hover;

        if (hover)
            _addOpacity = 1.0;

        _lastAddTick = 0;
        _addClock.Restart();
        RenderAddSlot();

        if (!hover && _addOpacity > RestOpacity)
            _addTimer.Start();
        else
            _addTimer.Stop();
    }

    // Linear fade from full to rest opacity during FadeBackSeconds.
    private void AnimateAddSlot()
    {
        double now = _addClock.Elapsed.TotalSeconds;
        double dt = now - _lastAddTick;
        _lastAddTick = now;

        _addOpacity = Math.Max(RestOpacity, _addOpacity - (1.0 - RestOpacity) * dt / FadeBackSeconds);

        if (_addOpacity <= RestOpacity + 1e-6)
            _addTimer.Stop();

        RenderAddSlot();
    }

    private void RenderAddSlot()
    {
        using DrawingContext dc = _addVisual.RenderOpen();

        if (Project is null || ActualWidth <= 0)
            return;

        int lane = AddSlotLane;
        Rect plus = PlusRect(lane);

        dc.PushOpacity(_addOpacity);

        // A big "+" without a frame.
        var pen = new Pen(BrushOf(Color.FromRgb(0xE6, 0xE4, 0xFA)), 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        Point c = new(plus.X + plus.Width / 2, plus.Y + plus.Height / 2);
        dc.DrawLine(pen, new Point(c.X - 14, c.Y), new Point(c.X + 14, c.Y));
        dc.DrawLine(pen, new Point(c.X, c.Y - 14), new Point(c.X, c.Y + 14));
        dc.Pop();
    }
}
