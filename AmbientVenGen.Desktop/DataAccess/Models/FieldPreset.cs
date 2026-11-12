namespace AmbientVenGen.Desktop.DataAccess.Models;

/// <summary>
/// A field-recording preset: a reference to an audio/video file in the user's library.
/// Stored as raw values (no display formatting), so the database does not depend on the UI culture.
/// </summary>
public sealed class FieldPreset
{
    public long Id { get; set; }
    public string FilePath { get; set; } = string.Empty;

    /// <summary>The file name without extension. Not stored: it is the end of <see cref="FilePath"/>.</summary>
    public string Name => System.IO.Path.GetFileNameWithoutExtension(FilePath);

    /// <summary>Lower-case extension with a dot, e.g. ".flac". Not stored: it is the end of <see cref="FilePath"/>.</summary>
    public string Extension => System.IO.Path.GetExtension(FilePath).ToLowerInvariant();

    /// <summary>When the preset was added, in Unix seconds (UTC).</summary>
    public long AddedAtUnix { get; set; }

    public double DurationSeconds { get; set; }
    public long SizeBytes { get; set; }

    /// <summary>How many times the preset was put into a project (Add new Layer / Replace in current Layer).</summary>
    public long ViewsCount { get; set; }
}
