using System.Windows;
using System.Windows.Controls;

namespace AmbientVenGen.Desktop.Controls;

/// <summary>The settings of one modulator of a layer (target, shape, rate, depth). The DataContext is a Modulator.</summary>
public partial class ModulatorEditor : UserControl
{
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(string), typeof(ModulatorEditor), new PropertyMetadata("Modulator"));

    public ModulatorEditor() => InitializeComponent();

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }
}
