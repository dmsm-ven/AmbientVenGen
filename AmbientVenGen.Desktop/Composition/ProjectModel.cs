using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace AmbientVenGen.Desktop.Composition;

/// <summary>
/// The track being edited: a list of layers on a timeline plus a few master settings.
/// Raises <see cref="Changed"/> for any change, including changes inside a layer.
/// </summary>
public sealed partial class ProjectModel : ObservableObject
{
    private bool _suspend;

    public ObservableCollection<Layer> Layers { get; } = [];

    [ObservableProperty]
    private double _durationSeconds = 600;

    [ObservableProperty]
    private int _seed = 12345;

    /// <summary>Master reverb amount, 0..1.</summary>
    [ObservableProperty]
    private double _reverbMix = 0.35;

    public event Action? Changed;

    public ProjectModel()
    {
        Layers.CollectionChanged += OnLayersChanged;
    }

    partial void OnDurationSecondsChanged(double value) => RaiseChanged();
    partial void OnSeedChanged(int value) => RaiseChanged();
    partial void OnReverbMixChanged(double value) => RaiseChanged();

    public ProjectSpec ToSpec() => new(
        ProjectSpec.CurrentVersion,
        DurationSeconds,
        Seed,
        ReverbMix,
        Layers.Select(layer => layer.ToSpec()).ToArray());

    /// <summary>Replaces the whole project content and raises a single <see cref="Changed"/>.</summary>
    public void Load(ProjectSpec spec)
    {
        _suspend = true;
        try
        {
            foreach (Layer layer in Layers.ToList())
                layer.PropertyChanged -= OnLayerPropertyChanged;

            Layers.Clear();

            DurationSeconds = Math.Clamp(spec.DurationSeconds, 10, 7200);
            Seed = spec.Seed;
            ReverbMix = Math.Clamp(spec.ReverbMix, 0, 1);

            foreach (LayerSpec layerSpec in spec.Layers)
                Layers.Add(Layer.FromSpec(layerSpec));
        }
        finally
        {
            _suspend = false;
        }

        RaiseChanged();
    }

    private void OnLayersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (Layer layer in e.OldItems)
                layer.PropertyChanged -= OnLayerPropertyChanged;
        }

        if (e.NewItems is not null)
        {
            foreach (Layer layer in e.NewItems)
                layer.PropertyChanged += OnLayerPropertyChanged;
        }

        RaiseChanged();
    }

    private void OnLayerPropertyChanged(object? sender, PropertyChangedEventArgs e) => RaiseChanged();

    private void RaiseChanged()
    {
        if (!_suspend)
            Changed?.Invoke();
    }
}
