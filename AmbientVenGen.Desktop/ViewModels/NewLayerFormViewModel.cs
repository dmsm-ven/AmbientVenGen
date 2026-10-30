using AmbientVenGen.Core.Composition;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AmbientVenGen.Desktop.ViewModels;

/// <summary>
/// The "New layer" form: only the type is chosen here. Start, length, fades and the curves
/// get defaults and are edited in the "Selected layer" panel or on the timeline.
/// </summary>
public partial class NewLayerFormViewModel : ObservableObject
{
    private const double DefaultDurationSeconds = 180;
    private const double DefaultFadeSeconds = 30;

    [ObservableProperty]
    private LayerType _type = LayerType.Drone;

    public Layer CreateLayer(string name)
    {
        Layer layer = LayerDefaults.Create(Type, name);

        layer.StartSeconds = 0;
        layer.DurationSeconds = DefaultDurationSeconds;
        layer.FadeInSeconds = DefaultFadeSeconds;
        layer.FadeOutSeconds = DefaultFadeSeconds;
        layer.FadeEasing = Easing.Sine;

        return layer;
    }
}
