using AmbientVenGen.Desktop.ViewModels;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace AmbientVenGen.Desktop.Panels;

public partial class TimelinePanel : UserControl
{
    public TimelinePanel()
    {
        InitializeComponent();

        Timeline.AddLayerRequested += () => (DataContext as MainViewModel)?.AddLayerCommand.Execute(null);
        Timeline.NewLayerTypeMenuRequested += rect =>
        {
            if (DataContext is MainViewModel viewModel)
                ShowLayerTypeMenu(viewModel, rect);
        };
    }

    // The type of the layer that "+" adds is chosen here (a list under the chooser in the free row of the timeline).
    private void ShowLayerTypeMenu(MainViewModel viewModel, System.Windows.Rect rect)
    {
        var menu = new System.Windows.Controls.ContextMenu
        {
            PlacementTarget = Timeline,
            PlacementRectangle = rect,
            Placement = PlacementMode.Bottom
        };

        foreach (Choice<Core.Composition.LayerType> choice in Choices.LayerTypes)
        {
            Core.Composition.LayerType type = choice.Value;
            var item = new System.Windows.Controls.MenuItem
            {
                Header = choice.Label,
                IsChecked = viewModel.NewLayer.Type == type
            };
            item.Click += (_, _) => viewModel.NewLayer.Type = type;
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }
}
