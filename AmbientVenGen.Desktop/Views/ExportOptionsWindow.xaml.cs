using AmbientVenGen.Desktop.ViewModels;
using System.Windows;
using Wpf.Ui.Controls;

namespace AmbientVenGen.Desktop.Views;

public partial class ExportOptionsWindow : FluentWindow
{
    public ExportOptionsWindow()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (DataContext is ExportOptionsViewModel vm)
                vm.Completed += () => DialogResult = true;
        };

        // Closing the window (the cross, Alt+F4) while the export runs cancels it.
        Closing += (_, _) => (DataContext as ExportOptionsViewModel)?.CancelExport();
    }

    // While the export runs "Cancel" stops it (the window stays); otherwise it closes the window.
    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is ExportOptionsViewModel vm && vm.CancelExport())
            return;

        DialogResult = false;
    }
}
