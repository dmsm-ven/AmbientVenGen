using System.Windows;
using System.Windows.Media;

namespace AmbientVenGen.Desktop.Controls;

/// <summary>Stereo level bars (dB scale) with a clip lamp.</summary>
public sealed class LevelMeterControl : FrameworkElement
{
    private const double MinDb = -60.0;

    public static readonly DependencyProperty LevelLeftProperty = DependencyProperty.Register(
        nameof(LevelLeft), typeof(double), typeof(LevelMeterControl),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LevelRightProperty = DependencyProperty.Register(
        nameof(LevelRight), typeof(double), typeof(LevelMeterControl),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsClippingProperty = DependencyProperty.Register(
        nameof(IsClipping), typeof(bool), typeof(LevelMeterControl),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Brush PanelBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush TrackBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush ClipOnBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0x4D, 0x6D)));
    private static readonly Brush ClipOffBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0x4D, 0x6D)));

    /// <summary>Linear amplitude 0..1 of the left channel.</summary>
    public double LevelLeft
    {
        get => (double)GetValue(LevelLeftProperty);
        set => SetValue(LevelLeftProperty, value);
    }

    /// <summary>Linear amplitude 0..1 of the right channel.</summary>
    public double LevelRight
    {
        get => (double)GetValue(LevelRightProperty);
        set => SetValue(LevelRightProperty, value);
    }

    public bool IsClipping
    {
        get => (bool)GetValue(IsClippingProperty);
        set => SetValue(IsClippingProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        double width = ActualWidth;
        double height = ActualHeight;

        if (width <= 0 || height <= 0)
            return;

        drawingContext.DrawRoundedRectangle(PanelBrush, null, new Rect(0, 0, width, height), 6, 6);

        const double padding = 6;
        const double barHeight = 5;
        const double gap = 4;
        const double lampArea = 16;

        double barsWidth = Math.Max(10, width - 2 * padding - lampArea);
        double top = (height - (2 * barHeight + gap)) / 2.0;

        var fill = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(padding, 0),
            EndPoint = new Point(padding + barsWidth, 0)
        };
        fill.GradientStops.Add(new GradientStop(Color.FromRgb(0x46, 0xD2, 0xC8), 0.0));
        fill.GradientStops.Add(new GradientStop(Color.FromRgb(0x8B, 0x7C, 0xFF), 0.75));
        fill.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0x4D, 0x6D), 1.0));
        fill.Freeze();

        DrawBar(drawingContext, fill, padding, top, barsWidth, barHeight, LevelLeft);
        DrawBar(drawingContext, fill, padding, top + barHeight + gap, barsWidth, barHeight, LevelRight);

        drawingContext.DrawEllipse(
            IsClipping ? ClipOnBrush : ClipOffBrush,
            null,
            new Point(width - padding - 4, height / 2.0),
            4,
            4);
    }

    private static void DrawBar(DrawingContext dc, Brush fill, double x, double y, double width, double height, double level)
    {
        dc.DrawRoundedRectangle(TrackBrush, null, new Rect(x, y, width, height), 2.5, 2.5);

        double db = 20.0 * Math.Log10(Math.Max(level, 1e-5));
        double fraction = Math.Clamp((db - MinDb) / -MinDb, 0.0, 1.0);
        double filled = width * fraction;

        if (filled >= 1.0)
            dc.DrawRoundedRectangle(fill, null, new Rect(x, y, filled, height), 2.5, 2.5);
    }

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
