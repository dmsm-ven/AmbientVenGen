using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AmbientVenGen.Desktop.Controls;

public enum CaptionButtonKind { Minimize, Maximize, Close }

/// <summary>
/// Opaque caption button (minimize / maximize-restore / close). An ordinary WPF button, so the click always
/// arrives, unlike the hit-tested WPF-UI caption buttons. Style: Resources/Styles.xaml.
/// </summary>
public sealed class WindowCaptionButton : Button
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(CaptionButtonKind), typeof(WindowCaptionButton),
        new PropertyMetadata(CaptionButtonKind.Close, (d, _) => ((WindowCaptionButton)d).UpdateGlyph()));

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(Geometry), typeof(WindowCaptionButton));

    public static readonly DependencyProperty IsCloseProperty = DependencyProperty.Register(
        nameof(IsClose), typeof(bool), typeof(WindowCaptionButton), new PropertyMetadata(true));

    private static readonly Geometry MinimizeGlyph = Geometry.Parse("M0,5 L10,5");
    private static readonly Geometry MaximizeGlyph = Geometry.Parse("M0.5,0.5 L9.5,0.5 L9.5,9.5 L0.5,9.5 Z");
    private static readonly Geometry RestoreGlyph = Geometry.Parse("M2.5,2.5 L2.5,0.5 L9.5,0.5 L9.5,7.5 L7.5,7.5 M0.5,2.5 L7.5,2.5 L7.5,9.5 L0.5,9.5 Z");
    private static readonly Geometry CloseGlyph = Geometry.Parse("M0,0 L10,10 M10,0 L0,10");

    private Window? _window;

    static WindowCaptionButton()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(WindowCaptionButton), new FrameworkPropertyMetadata(typeof(WindowCaptionButton)));
    }

    public WindowCaptionButton()
    {
        Loaded += (_, _) =>
        {
            _window = Window.GetWindow(this);
            if (_window is not null)
                _window.StateChanged += (_, _) => UpdateGlyph();
            UpdateGlyph();
        };
    }

    public CaptionButtonKind Kind
    {
        get => (CaptionButtonKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public Geometry? Glyph => (Geometry?)GetValue(GlyphProperty);

    /// <summary>True for the close button (red on hover).</summary>
    public bool IsClose => (bool)GetValue(IsCloseProperty);

    private void UpdateGlyph()
    {
        SetValue(IsCloseProperty, Kind == CaptionButtonKind.Close);
        SetValue(GlyphProperty, Kind switch
        {
            CaptionButtonKind.Minimize => MinimizeGlyph,
            CaptionButtonKind.Maximize => _window?.WindowState == WindowState.Maximized ? RestoreGlyph : MaximizeGlyph,
            _ => CloseGlyph
        });
    }

    protected override void OnClick()
    {
        base.OnClick();

        Window? window = Window.GetWindow(this);
        if (window is null)
            return;

        switch (Kind)
        {
            case CaptionButtonKind.Minimize:
                window.WindowState = WindowState.Minimized;
                break;
            case CaptionButtonKind.Maximize:
                window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                break;
            default:
                window.Close();
                break;
        }
    }
}