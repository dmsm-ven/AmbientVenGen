using System.Windows;
using System.Windows.Controls;

namespace AmbientVenGen.Desktop.Controls;

/// <summary>
/// Close button of a window caption. The built-in WPF-UI caption buttons are hit-tested through the window
/// frame and flicker (and sometimes cannot be pressed) over a translucent Mica window; this one is an
/// ordinary opaque WPF button, so the click always arrives. Style: Resources/Styles.xaml.
/// </summary>
public sealed class WindowCloseButton : Button
{
    static WindowCloseButton()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(WindowCloseButton), new FrameworkPropertyMetadata(typeof(WindowCloseButton)));
    }

    protected override void OnClick()
    {
        base.OnClick();
        Window.GetWindow(this)?.Close();
    }
}
