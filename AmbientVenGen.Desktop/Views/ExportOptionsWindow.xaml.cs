using System.Windows;
using Wpf.Ui.Controls;

namespace AmbientVenGen.Desktop.Views;

public partial class ExportOptionsWindow : FluentWindow
{
    public ExportOptionsWindow()
    {
        InitializeComponent();
    }

    private void OnExportClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
