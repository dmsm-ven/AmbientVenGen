using AmbientVenGen.Core.Composition;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AmbientVenGen.Desktop.ViewModels;

/// <summary>"Mute all except the selected layer": a switch that remembers the Mute state of every layer.</summary>
public partial class MainViewModel
{
    private readonly Dictionary<Layer, bool> _soloSaved = [];
    private bool _soloResetting;

    [ObservableProperty]
    private bool _isSoloActive;

    [RelayCommand]
    private void ToggleSolo() => IsSoloActive = !IsSoloActive;

    partial void OnIsSoloActiveChanged(bool value)
    {
        if (_soloResetting)
            return;

        if (value)
            ApplySolo();
        else
            RestoreSolo();
    }

    // Every layer except the selected one is muted; the selected one is audible.
    private void ApplySolo()
    {
        foreach (Layer layer in Project.Layers)
        {
            _soloSaved.TryAdd(layer, layer.Muted);
            layer.Muted = !ReferenceEquals(layer, SelectedLayer);
        }
    }

    // Every layer gets its earlier Mute state back (layers that were added meanwhile keep theirs).
    private void RestoreSolo()
    {
        foreach (Layer layer in Project.Layers)
        {
            if (_soloSaved.TryGetValue(layer, out bool muted))
                layer.Muted = muted;
        }

        _soloSaved.Clear();
    }

    // Another layer is selected while the switch is on: the solo moves to it.
    private void MoveSolo()
    {
        if (IsSoloActive && SelectedLayer is not null)
            ApplySolo();
    }

    // A new or opened project: the old layers are gone, the switch just goes off.
    private void ResetSolo()
    {
        _soloSaved.Clear();
        _soloResetting = true;
        IsSoloActive = false;
        _soloResetting = false;
    }
}
