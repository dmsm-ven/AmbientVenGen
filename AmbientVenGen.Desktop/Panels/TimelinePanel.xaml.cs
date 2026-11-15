using AmbientVenGen.Desktop.ViewModels;
using AmbientVenGen.Desktop.Views;
using System.Windows.Controls;

namespace AmbientVenGen.Desktop.Panels;

public partial class TimelinePanel : UserControl
{
    public TimelinePanel()
    {
        InitializeComponent();

        Timeline.AddLayerRequested += () => (DataContext as MainViewModel)?.AddLayerCommand.Execute(null);
        Timeline.RepeatPropertiesRequested += (layer, play) => RepeatPropertiesWindow.ShowFor(layer, play);
    }
}
