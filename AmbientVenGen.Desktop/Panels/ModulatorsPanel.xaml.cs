using AmbientVenGen.Core.Composition;
using AmbientVenGen.Desktop.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace AmbientVenGen.Desktop.Panels;

public partial class ModulatorsPanel : UserControl
{
    public ModulatorsPanel() => InitializeComponent();

    // A new modulator is switched on at once (it does something audible by default: it moves the level).
    private void OnAddModulatorClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel { SelectedLayer: { } layer })
            layer.Modulators.Add(new Modulator { Muted = false });
    }
}
