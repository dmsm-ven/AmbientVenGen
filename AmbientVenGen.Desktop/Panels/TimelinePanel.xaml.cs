using AmbientVenGen.Desktop.ViewModels;
using System.Windows.Controls;

namespace AmbientVenGen.Desktop.Panels;

public partial class TimelinePanel : UserControl
{
    public TimelinePanel()
    {
        InitializeComponent();

        Timeline.AddLayerRequested += () => (DataContext as MainViewModel)?.AddLayerCommand.Execute(null);
    }
}
