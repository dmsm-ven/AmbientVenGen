using System.Windows;
using System.Windows.Media;
using AmbientVenGen.Core.Composition;

namespace AmbientVenGen.Desktop.Controls;

/// <summary>
/// Preview icon of a noise type: a rounded rectangle in the colour of the noise with a soft left-to-right
/// gradient. Size it 16:9 (the dropdown uses 64x36).
/// </summary>
public sealed class NoiseColorIcon : FrameworkElement
{
    public static readonly DependencyProperty NoiseProperty = DependencyProperty.Register(
        nameof(Noise), typeof(NoiseType), typeof(NoiseColorIcon),
        new FrameworkPropertyMetadata(NoiseType.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public NoiseType Noise
    {
        get => (NoiseType)GetValue(NoiseProperty);
        set => SetValue(NoiseProperty, value);
    }

    /// <summary>Left and right colour of each noise type.</summary>
    public static (Color Left, Color Right) PaletteOf(NoiseType type) => type switch
    {
        NoiseType.White => (Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0xC4, 0xC6, 0xD2)),
        NoiseType.Pink => (Color.FromRgb(0xFF, 0xC4, 0xDE), Color.FromRgb(0xF2, 0x7C, 0xB0)),
        NoiseType.Brown => (Color.FromRgb(0xB0, 0x76, 0x48), Color.FromRgb(0x5E, 0x3B, 0x22)),
        NoiseType.Blue => (Color.FromRgb(0x8C, 0xBE, 0xFF), Color.FromRgb(0x2E, 0x68, 0xE6)),
        NoiseType.Violet => (Color.FromRgb(0xCF, 0xA4, 0xFF), Color.FromRgb(0x84, 0x3A, 0xE0)),
        NoiseType.Grey => (Color.FromRgb(0xD6, 0xD6, 0xD6), Color.FromRgb(0x6C, 0x6C, 0x72)),
        _ => (Colors.Gray, Colors.Gray)
    };

    protected override void OnRender(DrawingContext dc)
    {
        if (ActualWidth < 4 || ActualHeight < 4)
            return;

        (Color left, Color right) = PaletteOf(Noise);
        var brush = new LinearGradientBrush(left, right, new Point(0, 0.5), new Point(1, 0.5));
        var frame = new Pen(new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), 1);

        dc.DrawRoundedRectangle(brush, frame, new Rect(0.5, 0.5, ActualWidth - 1, ActualHeight - 1), 4, 4);
    }
}
