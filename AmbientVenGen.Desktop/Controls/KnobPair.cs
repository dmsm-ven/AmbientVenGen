using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace AmbientVenGen.Desktop.Controls;

/// <summary>
/// One control, or two dependent ones (Pitch + Pitch drift, Cutoff + Resonance, Fade in + Fade out...) shown side by
/// side in one rounded group with a chain link between them. A single control gets the same frame, so that every
/// group has the same gap. The look (frame, link, widths) is the template in Resources/CustomControls.xaml.
/// </summary>
[ContentProperty(nameof(Primary))]
public sealed class KnobPair : Control
{
    public static readonly DependencyProperty PrimaryProperty = DependencyProperty.Register(
        nameof(Primary), typeof(UIElement), typeof(KnobPair));

    public static readonly DependencyProperty SecondaryProperty = DependencyProperty.Register(
        nameof(Secondary), typeof(UIElement), typeof(KnobPair));

    public static readonly DependencyProperty LinkToolTipProperty = DependencyProperty.Register(
        nameof(LinkToolTip), typeof(string), typeof(KnobPair));

    public UIElement? Primary { get => (UIElement?)GetValue(PrimaryProperty); set => SetValue(PrimaryProperty, value); }
    public UIElement? Secondary { get => (UIElement?)GetValue(SecondaryProperty); set => SetValue(SecondaryProperty, value); }
    public string? LinkToolTip { get => (string?)GetValue(LinkToolTipProperty); set => SetValue(LinkToolTipProperty, value); }
}
