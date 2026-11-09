using AmbientVenGen.Core.Composition;
using AmbientVenGen.Desktop.ViewModels;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace AmbientVenGen.Desktop.Controls;

/// <summary>
/// The "add layer" slot: the first free lane under the layers. Its header holds a big "+" and a type chooser.
/// The slot is 60% opaque; it becomes fully opaque while the mouse is over its header and fades back in 0.5 s.
/// </summary>
public sealed partial class TimelineControl
{
    private const double RestOpacity = 0.6;
    private const double FadeBackSeconds = 0.5;

    /// <summary>The type of the layer that "+" adds.</summary>
    public static readonly DependencyProperty NewLayerTypeProperty = DependencyProperty.Register(
        nameof(NewLayerType), typeof(LayerType), typeof(TimelineControl),
        new FrameworkPropertyMetadata(LayerType.Drone, (d, _) => ((TimelineControl)d).RenderAddSlot()));

    public LayerType NewLayerType
    {
        get => (LayerType)GetValue(NewLayerTypeProperty);
        set => SetValue(NewLayerTypeProperty, value);
    }

    /// <summary>"+" was clicked: add a layer of the chosen type.</summary>
    public event Action? AddLayerRequested;

    /// <summary>The type chooser was clicked; the argument is its rectangle in the coordinates of this control.</summary>
    public event Action<Rect>? NewLayerTypeMenuRequested;

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

    private static Rect TypeRect(int lane) => new(64, LaneTop(lane) + (LaneHeight - 30) / 2.0, HeaderWidth - 64 - 14, 30);

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
        else if (TypeRect(lane).Contains(point))
            NewLayerTypeMenuRequested?.Invoke(TypeRect(lane));

        return true;
    }

    private bool IsOverAddControl(Point point) =>
        InAddHeader(point) && (PlusRect(AddSlotLane).Contains(point) || TypeRect(AddSlotLane).Contains(point));

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

    private string NewLayerTypeLabel =>
        Choices.LayerTypes.FirstOrDefault(c => c.Value == NewLayerType)?.Label ?? NewLayerType.ToString();

    private void RenderAddSlot()
    {
        using DrawingContext dc = _addVisual.RenderOpen();

        if (Project is null || ActualWidth <= 0)
            return;

        int lane = AddSlotLane;
        Rect plus = PlusRect(lane);
        Rect type = TypeRect(lane);

        dc.PushOpacity(_addOpacity);

        // Big "+" in a soft rounded square.
        dc.DrawRoundedRectangle(
            BrushOf(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
            new Pen(BrushOf(Color.FromArgb(0x55, 0xB7, 0xAF, 0xE9)), 1),
            plus, 8, 8);

        var pen = new Pen(BrushOf(Color.FromRgb(0xE6, 0xE4, 0xFA)), 2.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        Point c = new(plus.X + plus.Width / 2, plus.Y + plus.Height / 2);
        dc.DrawLine(pen, new Point(c.X - 10, c.Y), new Point(c.X + 10, c.Y));
        dc.DrawLine(pen, new Point(c.X, c.Y - 10), new Point(c.X, c.Y + 10));

        // Combobox-like type chooser.
        dc.DrawRoundedRectangle(
            BrushOf(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)),
            new Pen(BrushOf(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)), 1),
            type, 6, 6);

        FormattedText label = MakeText(NewLayerTypeLabel, 12, Color.FromRgb(0xF2, 0xF2, 0xF8), false, type.Width - 32);
        dc.DrawText(label, new Point(type.X + 10, type.Y + (type.Height - label.Height) / 2));

        var arrow = new StreamGeometry();
        using (StreamGeometryContext ctx = arrow.Open())
        {
            double ax = type.Right - 16;
            double ay = type.Y + type.Height / 2 - 2;
            ctx.BeginFigure(new Point(ax - 4, ay), true, true);
            ctx.LineTo(new Point(ax + 4, ay), true, false);
            ctx.LineTo(new Point(ax, ay + 5), true, false);
        }
        arrow.Freeze();
        dc.DrawGeometry(BrushOf(Color.FromRgb(0xA9, 0xA7, 0xC2)), null, arrow);

        dc.Pop();
    }
}
