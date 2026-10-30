using AmbientVenGen.Desktop.ViewModels;
using Wpf.Ui.Controls;

namespace AmbientVenGen.Desktop.Views;

public partial class PresetLibraryWindow : FluentWindow
{
    public PresetLibraryWindow(PresetLibraryViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
