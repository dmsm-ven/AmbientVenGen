using System.Windows;
using System.Windows.Media;
using AmbientVenGen.Core.Composition;

namespace AmbientVenGen.Desktop.Controls;

/// <summary>
/// Small plot of an easing function (progress 0..1 on both axes). Drawn from the very same
/// <see cref="EasingFunctions.Apply"/> the audio engine uses, so the icon can never disagree with the sound.
/// Size it with Width/Height; the dropdown keeps it 16:9.
/// </summary>
public sealed class EasingCurveIcon : FrameworkElement
{
    public static readonly DependencyProperty EasingProperty = DependencyProperty.Register(
        nameof(Easing), typeof(Easing), typeof(EasingCurveIcon),
        new FrameworkPropertyMetadata(Easing.Linear, FrameworkPropertyMetadataOptions.AffectsRender));

    public Easing Easing
    {
        get => (Easing)GetValue(EasingProperty);
        set => SetValue(EasingProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        double h = ActualHeight;

        if (w < 8 || h < 8)
            return;

        var back = new SolidColorBrush(Color.FromArgb(0x38, 0x00, 0x00, 0x00));
        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF)), 1);
        var frame = new Pen(new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), 1);

        dc.DrawRoundedRectangle(back, frame, new Rect(0.5, 0.5, w - 1, h - 1), 4, 4);

        double padX = Math.Max(5, w * 0.1);
        double padY = Math.Max(4, h * 0.14);
        var plot = new Rect(padX, padY, w - 2 * padX, h - 2 * padY);

        // Light guides: the diagonal (linear reference) and the midlines.
        dc.DrawLine(gridPen, new Point(plot.Left, plot.Top + plot.Height / 2), new Point(plot.Right, plot.Top + plot.Height / 2));
        dc.DrawLine(gridPen, new Point(plot.Left + plot.Width / 2, plot.Top), new Point(plot.Left + plot.Width / 2, plot.Bottom));

        var curve = new StreamGeometry();
        using (StreamGeometryContext ctx = curve.Open())
        {
            const int steps = 40;

            for (int i = 0; i <= steps; i++)
            {
                double t = i / (double)steps;
                double v = EasingFunctions.Apply(Easing, t);
                var point = new Point(plot.Left + t * plot.Width, plot.Bottom - v * plot.Height);

                if (i == 0)
                    ctx.BeginFigure(point, false, false);
                else
                    ctx.LineTo(point, true, true);
            }
        }
        curve.Freeze();

        var pen = new Pen(new SolidColorBrush(Color.FromRgb(0xB7, 0xAF, 0xE9)), 1.8)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };

        dc.DrawGeometry(null, pen, curve);
    }
}
