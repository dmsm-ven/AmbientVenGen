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

    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(string), typeof(KnobPair));

    /// <summary>Name of the glyph (see <see cref="ModGlyph"/>) drawn over the top-left corner of the group.</summary>
    public string? Icon { get => (string?)GetValue(IconProperty); set => SetValue(IconProperty, value); }

    public UIElement? Primary { get => (UIElement?)GetValue(PrimaryProperty); set => SetValue(PrimaryProperty, value); }
    public UIElement? Secondary { get => (UIElement?)GetValue(SecondaryProperty); set => SetValue(SecondaryProperty, value); }
    public string? LinkToolTip { get => (string?)GetValue(LinkToolTipProperty); set => SetValue(LinkToolTipProperty, value); }
}
