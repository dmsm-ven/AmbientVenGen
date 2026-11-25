using System.Windows;
using System.Windows.Media;

namespace AmbientVenGen.Desktop.Controls;

/// <summary>
/// A small line icon of a modulator option (a shape, a target or "Mute"), drawn from vector paths in a 20x20 box.
/// Key is the name of the enum value (Sine, Level, ReverbSend ...) or "Mute".
/// </summary>
public sealed class ModGlyph : FrameworkElement
{
    public static readonly DependencyProperty KeyProperty = DependencyProperty.Register(
        nameof(Key), typeof(object), typeof(ModGlyph),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(ModGlyph),
        new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(0xE6, 0xE4, 0xFA)),
            FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Dictionary<string, Geometry> Paths = Build(new Dictionary<string, string>
    {
        ["Sine"] = "M1,10 C4,1 7,1 10,10 C13,19 16,19 19,10",
        ["Triangle"] = "M1,10 L5.5,3 L14.5,17 L19,10",
        ["Random"] = "M1,12 C4,12 5,5 8,5 C11,5 11,15 14,15 C16,15 16,8 19,8",
        ["Square"] = "M1,15 L1,5 L7,5 L7,15 L13,15 L13,5 L19,5",
        ["DelayInput"] = "M1,10 L7,10 M4.5,7 L7.5,10 L4.5,13 M11,4 L11,16 M14.5,6.5 L14.5,13.5 M18,8.5 L18,11.5",
        ["EchoThrow"] = "M8.5,10 A1.5,1.5 0 1 1 11.5,10 A1.5,1.5 0 1 1 8.5,10 M5,10 A5,5 0 1 1 15,10 A5,5 0 1 1 5,10 M2,10 A8,8 0 1 1 18,10 A8,8 0 1 1 2,10",
        ["PitchDive"] = "M10,3 L10,15 M5,10 L10,16 L15,10",
        ["SwellDuck"] = "M2,6 C6,6 7,15 10,15 C13,15 14,6 18,6",
        ["Freeze"] = "M10,2 L10,18 M3.5,6 L16.5,14 M3.5,14 L16.5,6",
        ["FilterSweep"] = "M1,5 L8,5 C13,5 14,15 19,15",
        ["Stutter"] = "M4,13 L4,7 M8,16 L8,4 M12,13 L12,7 M16,15 L16,5",
        ["Level"] = "M3,17 L3,13 M7,17 L7,10 M11,17 L11,7 M15,17 L15,3",
        ["Cutoff"] = "M1,6 L9,6 C13,6 14,14 19,16",
        ["Pan"] = "M2,10 L18,10 M2,10 L6,6 M2,10 L6,14 M18,10 L14,6 M18,10 L14,14",
        ["ReverbSend"] = "M8,10 A2,2 0 1 1 12,10 A2,2 0 1 1 8,10 M5,10 A5,5 0 1 1 15,10 A5,5 0 1 1 5,10 M2,10 A8,8 0 1 1 18,10 A8,8 0 1 1 2,10",
        ["Brightness"] = "M7,10 A3,3 0 1 1 13,10 A3,3 0 1 1 7,10 M10,2 L10,4.5 M10,15.5 L10,18 M2,10 L4.5,10 M15.5,10 L18,10 M4.3,4.3 L6,6 M14,14 L15.7,15.7 M15.7,4.3 L14,6 M6,14 L4.3,15.7",
        ["Modulation"] = "M3,10 A7,7 0 1 1 17,10 A7,7 0 1 1 3,10 M10,10 L13,5",
        ["Fade"] = "M2,17 L18,3 M2,17 L18,17 M18,17 L18,3",
        ["Pitch"] = "M3,15 A2.5,2.5 0 1 1 8,15 A2.5,2.5 0 1 1 3,15 M8,15 L8,3 L16,6",
        ["Delay"] = "M3,4 L3,16 M8,6.5 L8,13.5 M13,8.5 L13,11.5 M17.5,9.3 L17.5,10.7",
        ["Speed"] = "M2,15 A8,8 0 1 1 18,15 M10,15 L14,8",
        ["Mute"] = "M10,2 L10,9 M5.5,5 A7,7 0 1 0 14.5,5"
    });

    public object? Key
    {
        get => GetValue(KeyProperty);
        set => SetValue(KeyProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    private static Dictionary<string, Geometry> Build(Dictionary<string, string> source)
    {
        var result = new Dictionary<string, Geometry>();

        foreach ((string name, string data) in source)
        {
            Geometry geometry = Geometry.Parse(data);
            geometry.Freeze();
            result[name] = geometry;
        }

        return result;
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (Key?.ToString() is not { } name || !Paths.TryGetValue(name, out Geometry? geometry))
            return;

        double scale = Math.Min(ActualWidth, ActualHeight) / 20.0;

        if (scale <= 0)
            return;

        var pen = new Pen(Stroke, 1.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        dc.PushTransform(new TranslateTransform((ActualWidth - 20 * scale) / 2, (ActualHeight - 20 * scale) / 2));
        dc.PushTransform(new ScaleTransform(scale, scale));
        dc.DrawGeometry(null, pen, geometry);
        dc.Pop();
        dc.Pop();
    }
}
