using AmbientVenGen.Desktop.ViewModels;
using Wpf.Ui.Controls;

namespace AmbientVenGen.Desktop;

public partial class MainWindow : FluentWindow
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is IDisposable disposable)
            disposable.Dispose();

        base.OnClosed(e);
    }
}
