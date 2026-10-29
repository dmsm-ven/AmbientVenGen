using AmbientVenGen.Desktop.ViewModels;
using Wpf.Ui.Controls;

namespace AmbientVenGen.Desktop;

public partial class MainWindow : FluentWindow
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
