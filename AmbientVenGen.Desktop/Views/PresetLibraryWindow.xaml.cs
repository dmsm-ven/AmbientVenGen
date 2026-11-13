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

        viewModel.ScrollToTopRequested += OnScrollToTopRequested;
        Closed += (_, _) =>
        {
            viewModel.ScrollToTopRequested -= OnScrollToTopRequested;
            viewModel.Shutdown();
        };
    }

    // A new record sits at the top of the table: show the top, with the new row in view.
    private void OnScrollToTopRequested()
    {
        if (PresetsGrid.Items.Count == 0)
            return;

        PresetsGrid.UpdateLayout();
        PresetsGrid.ScrollIntoView(PresetsGrid.Items[0]);
    }

    // Several rows can be selected (to remove them at once); the view model keeps the whole selection.
    private void OnPresetsSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ViewModel?.SetSelection(PresetsGrid.SelectedItems.OfType<FieldPresetItem>().ToList());

    // ---- waveform: a click (or a drag) moves the playing position

    private bool _seeking;

    private void SeekTo(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { ActualWidth: > 0 } element)
            ViewModel?.SeekPreview(e.GetPosition(element).X / element.ActualWidth);
    }

    private void OnWaveformMouseDown(object sender, MouseButtonEventArgs e)
    {
        _seeking = true;
        ((UIElement)sender).CaptureMouse();
        SeekTo(sender, e);
    }

    private void OnWaveformMouseMove(object sender, MouseEventArgs e)
    {
        if (_seeking)
            SeekTo(sender, e);
    }

    private void OnWaveformMouseUp(object sender, MouseButtonEventArgs e)
    {
        _seeking = false;
        ((UIElement)sender).ReleaseMouseCapture();
    }

    // ---- context menu of the table

    // A right click on a row that is not selected selects it first (a click inside the selection keeps it).
    private void OnGridRightClick(object sender, MouseButtonEventArgs e)
    {
        DependencyObject? source = e.OriginalSource as DependencyObject;
        while (source is not null and not DataGridRow)
            source = System.Windows.Media.VisualTreeHelper.GetParent(source);

        if (source is DataGridRow { IsSelected: false } row)
        {
            PresetsGrid.SelectedItems.Clear();
            row.IsSelected = true;
        }
    }

    private void OnRowMenuOpened(object sender, RoutedEventArgs e) =>
        RevealMenuItem.IsEnabled = PresetsGrid.SelectedItems.Count == 1;

    private void OnRevealClick(object sender, RoutedEventArgs e) => ViewModel?.RevealInExplorer();

    private async void OnAddTagClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
            await viewModel.AddTagToSelectionAsync();
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
}
