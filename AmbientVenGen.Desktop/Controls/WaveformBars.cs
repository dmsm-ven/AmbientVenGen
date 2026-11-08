using System.Windows;
using System.Windows.Media;

namespace AmbientVenGen.Desktop.Controls;

/// <summary>
/// The waveform of the active recording of the preset library as stepped bars (the same bar and step width as the rest
/// of the application). Bars that have been played are lighter; while playing, a thin line marks the position.
/// </summary>
public sealed class WaveformBars : FrameworkElement
{
    private const double Step = 2;

    public static readonly DependencyProperty PeaksProperty = DependencyProperty.Register(
        nameof(Peaks), typeof(float[]), typeof(WaveformBars),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(WaveformBars),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsPlayingProperty = DependencyProperty.Register(
        nameof(IsPlaying), typeof(bool), typeof(WaveformBars),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Brush BarBrush = Frozen(Color.FromRgb(0x5B, 0x9B, 0xFF));
    private static readonly Brush PlayedBrush = Frozen(Color.FromRgb(0xBF, 0xD9, 0xFF));
    private static readonly Brush LineBrush = Frozen(Color.FromRgb(0xF2, 0xF2, 0xF8));

    /// <summary>Peak of every moment, 0..1 (any count: the bars are made from them for the width).</summary>
    public float[]? Peaks
    {
        get => (float[]?)GetValue(PeaksProperty);
        set => SetValue(PeaksProperty, value);
    }

    /// <summary>Playing position 0..1.</summary>
    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public bool IsPlaying
    {
        get => (bool)GetValue(IsPlayingProperty);
        set => SetValue(IsPlayingProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        float[]? peaks = Peaks;
        double width = ActualWidth;
        double height = ActualHeight;

        if (peaks is not { Length: > 0 } || width < Step || height < 4)
            return;

        int columns = (int)(width / Step);
        double maxHeight = height - 4;
        double progress = IsPlaying ? Math.Clamp(Progress, 0, 1) : -1;

        for (int c = 0; c < columns; c++)
        {
            int from = (int)((long)c * peaks.Length / columns);
            int to = Math.Max(from + 1, (int)((long)(c + 1) * peaks.Length / columns));

            float peak = 0;
            for (int i = from; i < to && i < peaks.Length; i++)
                peak = Math.Max(peak, peaks[i]);

            double barHeight = Math.Clamp(peak * maxHeight, 2, maxHeight);
            Brush brush = (c + 0.5) / columns <= progress ? PlayedBrush : BarBrush;

            dc.DrawRectangle(brush, null, new Rect(c * Step, height - 2 - barHeight, Step - 0.5, barHeight));
        }

        if (progress >= 0)
            dc.DrawRectangle(LineBrush, null, new Rect(Math.Min(progress * columns * Step, width - 2), 0, 2, height));
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
