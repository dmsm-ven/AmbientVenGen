using AmbientVenGen.Core.Export;
using AmbientVenGen.Desktop.ViewModels;
using AmbientVenGen.Desktop.Views;
using System.Windows;

namespace AmbientVenGen.Desktop.Services;

public sealed class ExportOptionsDialogService
{
    /// <summary>
    /// Shows the export window: the options first, then (after "Export...") the progress of <paramref name="runner"/>.
    /// The window closes by itself when the runner returns true.
    /// </summary>
    public void Show(
        string trackLengthText,
        ExportOptions? previous,
        Func<ExportOptions, ExportOptionsViewModel, CancellationToken, Task<bool>> runner)
    {
        var viewModel = new ExportOptionsViewModel(trackLengthText, previous, runner);

        var window = new ExportOptionsWindow
        {
            DataContext = viewModel,
            Owner = Application.Current.MainWindow
        };

        window.ShowDialog();
    }
}
