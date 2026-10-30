namespace AmbientVenGen.Desktop.Models;

public enum ExportFormat
{
    Wav,
    Mp3
}

public enum WavSampleFormat
{
    Pcm16,
    Float32
}

/// <summary>
/// User-selected export parameters (everything except the composition itself).
/// </summary>
public sealed record ExportOptions(
    ExportFormat Format,
    int Mp3BitrateKbps,
    WavSampleFormat WavSampleFormat,
    double FadeInSeconds,
    double FadeOutSeconds);
