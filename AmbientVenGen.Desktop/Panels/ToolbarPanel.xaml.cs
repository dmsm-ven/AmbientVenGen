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
                Header = RecentHeader(path, viewModel.Settings.RecentProjectDate(path)),
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

    // "Project 1 | 4 day(s) ago": the name and, in grey, when the project was last opened or saved.
    private static object RecentHeader(string path, DateTime? date)
    {
        var panel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = System.IO.Path.GetFileNameWithoutExtension(path) });

        if (date is { } stamp)
        {
            int days = Math.Max(0, (int)(DateTime.Now.Date - stamp.Date).TotalDays);
            string age = days == 0 ? "today" : $"{days} day(s) ago";

            panel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = $"  |  {age}",
                Foreground = System.Windows.Media.Brushes.Gray
            });
        }

        return panel;
    }
}
