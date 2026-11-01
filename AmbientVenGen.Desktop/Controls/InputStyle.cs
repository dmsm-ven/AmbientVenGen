using System.Windows;

namespace AmbientVenGen.Desktop.Controls;

/// <summary>
/// The "Slider or Knob" setting. Set <see cref="UseKnobsProperty"/> once on the window: it is inherited
/// by everything below, and every <see cref="LabeledSlider"/> shows a slider or a gauge knob accordingly.
/// </summary>
public static class InputStyle
{
    public static readonly DependencyProperty UseKnobsProperty = DependencyProperty.RegisterAttached(
        "UseKnobs",
        typeof(bool),
        typeof(InputStyle),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    public static bool GetUseKnobs(DependencyObject element) => (bool)element.GetValue(UseKnobsProperty);

    public static void SetUseKnobs(DependencyObject element, bool value) => element.SetValue(UseKnobsProperty, value);
}
