using AmbientVenGen.Desktop.DataAccess.Models;
using AmbientVenGen.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Globalization;

namespace AmbientVenGen.Desktop.ViewModels;

/// <summary>One row of the preset library: a <see cref="FieldPreset"/> plus what is shown for it.</summary>
public sealed partial class FieldPresetItem : ObservableObject
{
    public long Id { get; }
    public string Name { get; }
    public string FilePath { get; }
    public string Extension { get; }
    public DateTime AddedAt { get; }
    public TimeSpan Duration { get; }
    public long SizeBytes { get; }

    /// <summary>Peaks of the waveform (loaded when the file is played the first time).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WaveformHint))]
    private float[]? _peaks;

    /// <summary>Tags of this preset (kept in the database).</summary>
    public ObservableCollection<string> Tags { get; } = [];

    /// <summary>The tag box is shown only for the clicked (or just added) row.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowTagHint))]
    private bool _isTagEditing;

    /// <summary>A quiet "+ tag" text for rows without tags while the tag box is hidden.</summary>
    public bool ShowTagHint => Tags.Count == 0 && !IsTagEditing;

    /// <summary>True while this file is being previewed.</summary>
    [ObservableProperty]
    private bool _isPlaying;

    /// <summary>Preview position 0..1 (drawn as a vertical line over the waveform).</summary>
    [ObservableProperty]
    private double _playProgress;

    /// <summary>How many times the preset was put into a project.</summary>
    [ObservableProperty]
    private long _viewsCount;

    /// <summary>True when the file is gone or cannot be read any more.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WaveformHint))]
    private bool _isUnavailable;

    /// <summary>True while the waveform is being prepared (a spinner is shown instead).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WaveformHint))]
    private bool _isThumbnailLoading;

    /// <summary>What the waveform panel says while there is no waveform to show (empty when there is one).</summary>
    public string WaveformHint =>
        IsUnavailable ? "The file was moved, deleted or cannot be read"
        : Peaks is not null || IsThumbnailLoading ? string.Empty
        : "Press play to see the waveform";

    private bool _thumbnailRequested;

    /// <summary>Set by the library; builds the thumbnail of this row.</summary>
    internal Func<FieldPresetItem, Task>? ThumbnailLoader { get; set; }

    /// <summary>
    /// Called the first time the row is played: only played files get a waveform, and only until the window closes
    /// (every opening of the library starts with a fresh list, so nothing is drawn or kept in advance).
    /// </summary>
    public void RequestThumbnail()
    {
        if (_thumbnailRequested || ThumbnailLoader is null)
            return;

        _thumbnailRequested = true;
        IsThumbnailLoading = true;
        _ = ThumbnailLoader(this);
    }

    public bool IsVideo => Extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase);

    public string AddedText => AddedAt.ToString("dd.MM.yyyy", CultureInfo.CurrentCulture);

    public string DurationText => Duration.ToString(Duration.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture);

    public string SizeText => string.Create(CultureInfo.CurrentCulture, $"{SizeBytes / (1024.0 * 1024.0):0.0} MB");

    public FieldPresetItem(FieldPreset preset)
    {
        Id = preset.Id;
        Name = preset.Name;
        FilePath = preset.FilePath;
        Extension = preset.Extension;

        Tags.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ShowTagHint));
        AddedAt = DateTimeOffset.FromUnixTimeSeconds(preset.AddedAtUnix).LocalDateTime;
        Duration = TimeSpan.FromSeconds(preset.DurationSeconds);
        SizeBytes = preset.SizeBytes;
        _viewsCount = preset.ViewsCount;
    }
}
