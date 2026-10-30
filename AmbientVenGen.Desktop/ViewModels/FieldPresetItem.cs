using AmbientVenGen.Desktop.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Globalization;
using System.Windows.Media;

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

    /// <summary>Loaded in the background after the row appears.</summary>
    [ObservableProperty]
    private ImageSource? _thumbnail;

    /// <summary>True when the file is gone or cannot be read any more.</summary>
    [ObservableProperty]
    private bool _isUnavailable;

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
        AddedAt = DateTimeOffset.FromUnixTimeSeconds(preset.AddedAtUnix).LocalDateTime;
        Duration = TimeSpan.FromSeconds(preset.DurationSeconds);
        SizeBytes = preset.SizeBytes;
    }
}
