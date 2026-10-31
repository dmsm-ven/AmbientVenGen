using AmbientVenGen.Desktop.ViewModels;
using System.Windows;
using Wpf.Ui.Controls;

namespace AmbientVenGen.Desktop.Views;

public partial class PresetLibraryWindow : FluentWindow
{
    public PresetLibraryWindow(PresetLibraryViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    // Raised only for the preview cells that are actually created (visible rows).
    private void OnPreviewCellLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FieldPresetItem item })
            item.RequestThumbnail();
    }
}
