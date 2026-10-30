using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AmbientVenGen.Desktop.Services;

/// <summary>Draws the stepped "bars" thumbnail from peak values. Must run on the UI thread.</summary>
public static class WaveformRenderer
{
    public const int StepWidth = 2;
    public const int Width = 250;
    public const int Height = 40;

    /// <summary>How many peak values <see cref="Render"/> expects.</summary>
    public const int ColumnCount = Width / StepWidth;

    private static readonly Brush BarBrush = CreateBarBrush();

    public static ImageSource Render(IReadOnlyList<float> peaks)
    {
        var visual = new DrawingVisual();

        using (DrawingContext dc = visual.RenderOpen())
        {
            // Transparent background so the bars sit on whatever the row looks like.
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, Width, Height));

            int count = Math.Min(peaks.Count, ColumnCount);

            for (int i = 0; i < count; i++)
            {
                double barHeight = Math.Clamp(peaks[i] * Height, 2, Height);

                dc.DrawRectangle(
                    BarBrush,
                    null,
                    new Rect(i * StepWidth, Height - barHeight, StepWidth - 0.5, barHeight));
            }
        }

        var bitmap = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    // A lighter blue than Brushes.Blue: pure blue is hard to see on the dark theme.
    private static Brush CreateBarBrush()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0x5B, 0x9B, 0xFF));
        brush.Freeze();
        return brush;
    }
}
