using AmbientVenGen.Desktop.Composition;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AmbientVenGen.Desktop.ViewModels;

/// <summary>Values of the "New layer" form.</summary>
public partial class NewLayerFormViewModel : ObservableObject
{
    [ObservableProperty]
    private LayerType _type = LayerType.Drone;

    [ObservableProperty]
    private double _startSeconds;

    [ObservableProperty]
    private double _durationSeconds = 180;

    [ObservableProperty]
    private double _fadeInSeconds = 30;

    [ObservableProperty]
    private double _fadeOutSeconds = 30;

    [ObservableProperty]
    private Easing _fadeEasing = Easing.Sine;

    public Layer CreateLayer(string name)
    {
        Layer layer = LayerDefaults.Create(Type, name);

        layer.StartSeconds = StartSeconds;
        layer.DurationSeconds = DurationSeconds;
        layer.FadeInSeconds = FadeInSeconds;
        layer.FadeOutSeconds = FadeOutSeconds;
        layer.FadeEasing = FadeEasing;

        return layer;
    }
}
