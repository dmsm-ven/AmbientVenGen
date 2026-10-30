using AmbientVenGen.Desktop.Models;
using AmbientVenGen.Desktop.ViewModels;
using AmbientVenGen.Desktop.Views;
using System.Windows;

namespace AmbientVenGen.Desktop.Services;

public sealed class ExportOptionsDialogService
{
    /// <summary>
    /// Shows the export options dialog. Returns null when the user cancels.
    /// </summary>
    public ExportOptions? Show(int durationMinutes, ExportOptions? previous)
    {
        var viewModel = new ExportOptionsViewModel(durationMinutes, previous);

        var window = new ExportOptionsWindow
        {
            DataContext = viewModel,
            Owner = Application.Current.MainWindow
        };

        return window.ShowDialog() == true ? viewModel.ToOptions() : null;
    }
}
