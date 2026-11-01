using CommunityToolkit.Mvvm.ComponentModel;

namespace AmbientVenGen.Desktop.ViewModels;

/// <summary>A tag in the filter bar of the preset library: its name, how many presets have it, and whether it is switched on.</summary>
public sealed partial class TagFilterItem : ObservableObject
{
    private readonly Action _changed;

    public string Name { get; }
    public int Count { get; }

    public string Label => $"{Name} ({Count})";

    [ObservableProperty]
    private bool _isSelected;

    public TagFilterItem(string name, int count, bool isSelected, Action changed)
    {
        Name = name;
        Count = count;
        _isSelected = isSelected;
        _changed = changed;
    }

    partial void OnIsSelectedChanged(bool value) => _changed();
}
