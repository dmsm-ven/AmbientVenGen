namespace AmbientVenGen.Desktop.DataAccess.Models;

/// <summary>
/// A field-recording preset: a reference to an audio/video file in the user's library.
/// Stored as raw values (no display formatting), so the database does not depend on the UI culture.
/// </summary>
public sealed class FieldPreset
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;

    /// <summary>When the preset was added, in Unix seconds (UTC).</summary>
    public long AddedAtUnix { get; set; }

    public double DurationSeconds { get; set; }
    public long SizeBytes { get; set; }

    /// <summary>Lower-case extension with a dot, e.g. ".flac".</summary>
    public string Extension { get; set; } = string.Empty;
}
