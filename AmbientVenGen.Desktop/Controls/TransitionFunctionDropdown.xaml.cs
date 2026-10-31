using System.Windows;
using System.Windows.Controls;
using AmbientVenGen.Core.Composition;
using AmbientVenGen.Desktop.ViewModels;

namespace AmbientVenGen.Desktop.Controls;

/// <summary>
/// Picker for a transition function. Closed it is a fixed 72x40 (16:9) field showing the curve; the name
/// appears on hover and while the list is open. The list shows a 16:9 curve, the name and - when the
/// "Show hints" switch is on - a short description of how the function grows.
/// </summary>
public partial class TransitionFunctionDropdown : UserControl
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(Easing), typeof(TransitionFunctionDropdown),
        new FrameworkPropertyMetadata(
            Easing.Linear,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, _) => ((TransitionFunctionDropdown)d).Sync()));

    /// <summary>Follows the window-wide "Show hints" switch (inherited attached property).</summary>
    public static readonly DependencyProperty ShowDescriptionsProperty =
        Hints.IsShownProperty.AddOwner(typeof(TransitionFunctionDropdown));

    public Easing Value
    {
        get => (Easing)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public bool ShowDescriptions
    {
        get => (bool)GetValue(ShowDescriptionsProperty);
        set => SetValue(ShowDescriptionsProperty, value);
    }

    public TransitionFunctionDropdown()
    {
        InitializeComponent();
        Sync();
    }

    private void Sync()
    {
        if (FieldIcon is null)
            return;

        FieldIcon.Easing = Value;
        FieldTitle.Text = Choices.EasingOptions.FirstOrDefault(o => o.Value == Value)?.Label ?? Value.ToString();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Picking an item closes the list.
        if (e.AddedItems.Count > 0)
            Pop.IsOpen = false;
    }
}
