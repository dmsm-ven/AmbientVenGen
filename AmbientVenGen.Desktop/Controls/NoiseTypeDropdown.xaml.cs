using System.Windows;
using System.Windows.Controls;
using AmbientVenGen.Core.Composition;
using AmbientVenGen.Desktop.ViewModels;

namespace AmbientVenGen.Desktop.Controls;

/// <summary>
/// Picker for the colour of a Noise layer. Closed: a small colour icon and the name. The list shows a 16:9
/// gradient icon, the name and, while "Show hints" is on, what the noise sounds like.
/// </summary>
public partial class NoiseTypeDropdown : UserControl
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(NoiseType), typeof(NoiseTypeDropdown),
        new FrameworkPropertyMetadata(
            NoiseType.White,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, _) => ((NoiseTypeDropdown)d).Sync()));

    public NoiseType Value
    {
        get => (NoiseType)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public NoiseTypeDropdown()
    {
        InitializeComponent();
        Sync();
    }

    private void Sync()
    {
        if (FieldIcon is null)
            return;

        FieldIcon.Noise = Value;
        FieldTitle.Text = Choices.NoiseOptions.FirstOrDefault(o => o.Value == Value)?.Label ?? Value.ToString();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0)
            Pop.IsOpen = false;
    }
}
