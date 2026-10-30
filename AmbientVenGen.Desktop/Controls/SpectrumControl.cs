using System.Windows;
using System.Windows.Media;

namespace AmbientVenGen.Desktop.Controls;

/// <summary>Small live spectrum analyzer: translucent bars from violet to cyan.</summary>
public sealed class SpectrumControl : FrameworkElement
{
    public static readonly DependencyProperty BandsProperty = DependencyProperty.Register(
        nameof(Bands), typeof(double[]), typeof(SpectrumControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Brush PanelBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)));

    private static readonly Brush BarBrush = CreateBarBrush();

    public double[]? Bands
    {
        get => (double[]?)GetValue(BandsProperty);
        set => SetValue(BandsProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        double width = ActualWidth;
        double height = ActualHeight;

        if (width <= 0 || height <= 0)
            return;

        drawingContext.DrawRoundedRectangle(PanelBrush, null, new Rect(0, 0, width, height), 6, 6);

        double[]? bands = Bands;
        if (bands is null || bands.Length == 0)
            return;

        const double padding = 4;
        const double gap = 2;

        double barWidth = Math.Max(1, (width - 2 * padding - gap * (bands.Length - 1)) / bands.Length);
        double usableHeight = height - 2 * padding;

        for (int i = 0; i < bands.Length; i++)
        {
            double value = Math.Clamp(bands[i], 0.0, 1.0);
            double barHeight = Math.Max(1.5, value * usableHeight);

            var rect = new Rect(
                padding + i * (barWidth + gap),
                height - padding - barHeight,
                barWidth,
                barHeight);

            drawingContext.DrawRoundedRectangle(BarBrush, null, rect, 1.5, 1.5);
        }
    }

    // Violet at the bottom, cyan at the top.
    private static Brush CreateBarBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 1),
            EndPoint = new Point(0, 0)
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0xE0, 0x7C, 0x6C, 0xFF), 0.0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0xE0, 0x46, 0xD2, 0xC8), 1.0));
        brush.Freeze();
        return brush;
    }

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
