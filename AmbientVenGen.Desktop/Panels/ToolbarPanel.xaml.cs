using AmbientVenGen.Desktop.ViewModels;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace AmbientVenGen.Desktop.Panels;

public partial class ToolbarPanel : UserControl
{
    public ToolbarPanel() => InitializeComponent();

    // "New / Open": New Project on top, then the recent projects, and below a separator "Open file..." (the file dialog).
    private void OnOpenMenuClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || sender is not System.Windows.FrameworkElement button)
            return;

        var menu = new System.Windows.Controls.ContextMenu
        {
            PlacementTarget = button,
            Placement = PlacementMode.Bottom
        };

        menu.Items.Add(new System.Windows.Controls.MenuItem { Header = "New Project", Command = viewModel.NewProjectCommand });
        menu.Items.Add(new System.Windows.Controls.Separator());

        foreach (string path in viewModel.Settings.RecentProjects)
        {
            var item = new System.Windows.Controls.MenuItem
            {
                Header = System.IO.Path.GetFileNameWithoutExtension(path),
                ToolTip = path,
                Command = viewModel.OpenRecentCommand,
                CommandParameter = path
            };

            menu.Items.Add(item);
        }

        if (menu.Items.Count == 0)
            menu.Items.Add(new System.Windows.Controls.MenuItem { Header = "No recent projects", IsEnabled = false });

        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(new System.Windows.Controls.MenuItem { Header = "Open file...", Command = viewModel.OpenProjectCommand });

        menu.IsOpen = true;
    }
}
