using System.Windows;
using System.Windows.Controls;

namespace AmbientVenGen.Desktop.Controls;

/// <summary>
/// A small "?" placed after a label. It is visible only while hints are switched on
/// (<see cref="Hints.IsShownProperty"/>) and the <see cref="Text"/> is not empty;
/// hovering it shows the explanation.
/// </summary>
public partial class HintIcon : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(HintIcon),
        new PropertyMetadata(string.Empty, OnChanged));

    // The same inherited property as Hints.IsShown, with a callback so the icon can react to it.
    public static readonly DependencyProperty IsShownProperty = Hints.IsShownProperty.AddOwner(
        typeof(HintIcon),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits, OnChanged));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public bool IsShown
    {
        get => (bool)GetValue(IsShownProperty);
        set => SetValue(IsShownProperty, value);
    }

    public HintIcon()
    {
        InitializeComponent();
    }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((HintIcon)d).Refresh();

    private void Refresh()
    {
        if (TipText is null)
            return;

        TipText.Text = Text;
        Visibility = IsShown && !string.IsNullOrWhiteSpace(Text) ? Visibility.Visible : Visibility.Collapsed;
    }
}
