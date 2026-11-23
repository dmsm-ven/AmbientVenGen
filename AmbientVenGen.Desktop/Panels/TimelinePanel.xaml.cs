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
        Timeline.EventActivated += (layer, item) => (DataContext as MainViewModel)?.ShowEvent(layer, item);
        Timeline.EventAdded += (layer, item) => (DataContext as MainViewModel)?.ShowEvent(layer, item);
        Timeline.RepeatPropertiesRequested += (layer, play) => RepeatPropertiesWindow.ShowFor(layer, play);
    }
}
