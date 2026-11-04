using CommunityToolkit.Mvvm.ComponentModel;

namespace AmbientVenGen.Core.Composition;

/// <summary>
/// UI-only state of a layer (not saved, not part of the sound): which hidden second knobs are expanded.
/// Kept apart from <see cref="Layer"/> so that the model holds only what the engine and the file need.
/// </summary>
public sealed partial class LayerViewState : ObservableObject
{
    [ObservableProperty]
    private bool _pitchPairOpen;

    [ObservableProperty]
    private bool _panPairOpen;

    [ObservableProperty]
    private bool _filterPairOpen;

    [ObservableProperty]
    private bool _delayPairOpen;

    /// <summary>Opens the second knob of every pair that already has a non-default value.</summary>
    public static LayerViewState For(Layer layer) => new()
    {
        PitchPairOpen = layer.PitchDrift > 0,
        PanPairOpen = layer.PanDrift > 0,
        FilterPairOpen = layer.FilterResonance > 0,
        DelayPairOpen = layer.DelayFeedback > 0 && layer.DelaySeconds > 0
    };
}
