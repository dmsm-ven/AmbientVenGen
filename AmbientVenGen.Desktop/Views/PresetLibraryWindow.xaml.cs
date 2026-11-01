using AmbientVenGen.Desktop.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace AmbientVenGen.Desktop.Views;

public partial class PresetLibraryWindow : FluentWindow
{
    public PresetLibraryWindow(PresetLibraryViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        Closed += (_, _) => viewModel.Shutdown();
    }

    private PresetLibraryViewModel? ViewModel => DataContext as PresetLibraryViewModel;

    // ---- tags

    // A click on the tags cell opens the tag box of that row.
    private void OnTagCellMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FieldPresetItem item })
            item.IsTagEditing = true;
    }

    private async void OnTagKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not ComboBox box)
            return;

        e.Handled = true;
        await CommitTagAsync(box, box.Text);
    }

    private async void OnTagSelected(object sender, SelectionChangedEventArgs e)
    {
        // Only a pick from the open list counts; typing must never select (and insert) an existing tag by itself.
        if (sender is ComboBox { IsDropDownOpen: true } box && e.AddedItems.Count > 0 && e.AddedItems[0] is string tag)
            await CommitTagAsync(box, tag);
    }

    private async Task CommitTagAsync(ComboBox box, string? text)
    {
        if (box.DataContext is FieldPresetItem item && ViewModel is { } viewModel)
            await viewModel.AddTagAsync(item, text);

        box.SelectedItem = null;
        box.Text = string.Empty;
    }

    private async void OnRemoveTagClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: string tag, Tag: FieldPresetItem item } && ViewModel is { } viewModel)
            await viewModel.RemoveTagAsync(item, tag);
    }

    // Raised only for the preview cells that are actually created (visible rows).
    private void OnPreviewCellLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FieldPresetItem item })
            item.RequestThumbnail();
    }
}
