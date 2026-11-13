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
        NoiseType.Crackle => (Color.FromRgb(0xFF, 0xB4, 0x5A), Color.FromRgb(0xC2, 0x38, 0x1C)),
        NoiseType.Vinyl => (Color.FromRgb(0x8A, 0x8F, 0x98), Color.FromRgb(0x2A, 0x2D, 0x33)),
        NoiseType.RadioStatic => (Color.FromRgb(0xB8, 0xE9, 0x86), Color.FromRgb(0x3C, 0x8D, 0x4E)),
        NoiseType.Wind => (Color.FromRgb(0xE3, 0xF1, 0xFA), Color.FromRgb(0x93, 0xB4, 0xCC)),
        NoiseType.Rain => (Color.FromRgb(0x9C, 0xC7, 0xD6), Color.FromRgb(0x3A, 0x6F, 0x86)),
        NoiseType.Ocean => (Color.FromRgb(0x6F, 0xD0, 0xE0), Color.FromRgb(0x1F, 0x4E, 0x8C)),
        NoiseType.ElectricalHum => (Color.FromRgb(0xFF, 0xE6, 0x6B), Color.FromRgb(0x8A, 0x6B, 0x1A)),
        NoiseType.TapeHiss => (Color.FromRgb(0xC9, 0xA7, 0x7C), Color.FromRgb(0x5A, 0x3F, 0x2A)),
        NoiseType.SpaceRumble => (Color.FromRgb(0x6A, 0x4C, 0xB5), Color.FromRgb(0x14, 0x0F, 0x2E)),
        NoiseType.Geiger => (Color.FromRgb(0xD8, 0xFF, 0x6A), Color.FromRgb(0x3F, 0x6B, 0x14)),
        NoiseType.IceSinging => (Color.FromRgb(0xF0, 0xFC, 0xFF), Color.FromRgb(0x7F, 0xB6, 0xE0)),
        NoiseType.HighVoltage => (Color.FromRgb(0xBF, 0xC8, 0xFF), Color.FromRgb(0x3B, 0x3F, 0xA0)),
        NoiseType.EnergyShield => (Color.FromRgb(0x7C, 0xFF, 0xD9), Color.FromRgb(0x1B, 0x7F, 0x6B)),
        NoiseType.CrtWhine => (Color.FromRgb(0xB8, 0xC4, 0xD8), Color.FromRgb(0x3A, 0x45, 0x58)),
        NoiseType.PlasmaArc => (Color.FromRgb(0xFF, 0x9A, 0xF0), Color.FromRgb(0x8A, 0x2A, 0x8F)),
        NoiseType.GasBurner => (Color.FromRgb(0x7F, 0xC8, 0xFF), Color.FromRgb(0x1F, 0x3F, 0xC8)),
        NoiseType.Gong => (Color.FromRgb(0xE8, 0xC2, 0x5A), Color.FromRgb(0x7A, 0x4A, 0x14)),
        NoiseType.WaterDrop => (Color.FromRgb(0xA8, 0xE6, 0xF2), Color.FromRgb(0x1D, 0x7F, 0xA6)),
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
