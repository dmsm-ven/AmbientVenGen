using AmbientVenGen.Desktop.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AmbientVenGen.Desktop.ViewModels;

/// <summary>Label/value pair for ComboBox items.</summary>
public sealed record Choice<T>(string Label, T Value);

public partial class ExportOptionsViewModel : ObservableObject
{
    public IReadOnlyList<Choice<ExportFormat>> Formats { get; } =
    [
        new("WAV (lossless)", ExportFormat.Wav),
        new("MP3", ExportFormat.Mp3)
    ];

    public IReadOnlyList<Choice<WavSampleFormat>> WavSampleFormats { get; } =
    [
        new("16-bit PCM", WavSampleFormat.Pcm16),
        new("32-bit float", WavSampleFormat.Float32)
    ];

    public IReadOnlyList<Choice<int>> Mp3Bitrates { get; } =
    [
        new("128 kbps", 128),
        new("192 kbps", 192),
        new("256 kbps", 256),
        new("320 kbps", 320)
    ];

    /// <summary>Track length is defined in the main window; shown here for information only.</summary>
    public int DurationMinutes { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWav))]
    [NotifyPropertyChangedFor(nameof(IsMp3))]
    private ExportFormat _format = ExportFormat.Wav;

    [ObservableProperty]
    private WavSampleFormat _wavSampleFormat = WavSampleFormat.Pcm16;

    [ObservableProperty]
    private int _mp3BitrateKbps = 192;

    [ObservableProperty]
    private double _fadeInSeconds = 5;

    [ObservableProperty]
    private double _fadeOutSeconds = 10;

    public bool IsWav => Format == ExportFormat.Wav;
    public bool IsMp3 => Format == ExportFormat.Mp3;

    public ExportOptionsViewModel(int durationMinutes, ExportOptions? previous = null)
    {
        DurationMinutes = durationMinutes;

        if (previous is null)
            return;

        Format = previous.Format;
        WavSampleFormat = previous.WavSampleFormat;
        Mp3BitrateKbps = previous.Mp3BitrateKbps;
        FadeInSeconds = previous.FadeInSeconds;
        FadeOutSeconds = previous.FadeOutSeconds;
    }

    public ExportOptions ToOptions() => new(
        Format,
        Mp3BitrateKbps,
        WavSampleFormat,
        FadeInSeconds,
        FadeOutSeconds);
}
