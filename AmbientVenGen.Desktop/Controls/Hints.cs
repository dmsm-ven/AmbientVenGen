using System.Windows;

namespace AmbientVenGen.Desktop.Controls;

/// <summary>
/// The "Show hints" switch. Set <see cref="IsShownProperty"/> once on the window: the value is inherited
/// by every element below it (including the content of data templates), and each
/// <see cref="HintIcon"/> shows or hides itself accordingly.
/// </summary>
public static class Hints
{
    public static readonly DependencyProperty IsShownProperty = DependencyProperty.RegisterAttached(
        "IsShown",
        typeof(bool),
        typeof(Hints),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    public static bool GetIsShown(DependencyObject element) => (bool)element.GetValue(IsShownProperty);

    public static void SetIsShown(DependencyObject element, bool value) => element.SetValue(IsShownProperty, value);
}
