using AmbientVenGen.Desktop.Services;
using Wpf.Ui.Controls;

namespace AmbientVenGen.Desktop.Views;

public partial class SettingsWindow : FluentWindow
{
    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        DataContext = settings;
    }
}
