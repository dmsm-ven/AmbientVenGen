using AmbientVenGen.Core.Export;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Diagnostics;

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
    public string TrackLengthText { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWav))]
    [NotifyPropertyChangedFor(nameof(IsMp3))]
    private ExportFormat _format = ExportFormat.Mp3;

    [ObservableProperty]
    private WavSampleFormat _wavSampleFormat = WavSampleFormat.Pcm16;

    [ObservableProperty]
    private int _mp3BitrateKbps = 320;

    [ObservableProperty]
    private double _fadeInSeconds = 1;

    [ObservableProperty]
    private double _fadeOutSeconds = 3;

    /// <summary>The file ends where the last layer ends (the fade-out is counted to that moment).</summary>
    [ObservableProperty]
    private bool _truncateToLastLayer = true;

    // ---- the export itself runs in this window: the options, then the progress, then the window closes

    private readonly Func<ExportOptions, ExportOptionsViewModel, CancellationToken, Task<bool>>? _runner;
    private CancellationTokenSource? _cts;

    /// <summary>Raised when the export has finished successfully: the window closes.</summary>
    public event Action? Completed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private bool _isBusy;

    public bool IsNotBusy => !IsBusy;

    /// <summary>0..1</summary>
    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _progressText = "";

    /// <summary>Called by the export with the share done (0..1); the remaining time is estimated from the speed so far.</summary>
    public void ReportProgress(double value, Stopwatch elapsed)
    {
        Progress = Math.Clamp(value, 0, 1);
        string percent = $"{Progress * 100:0}%";

        // too early the estimate jumps wildly
        if (Progress < 0.03 || elapsed.Elapsed.TotalSeconds < 1.5)
        {
            ProgressText = percent;
            return;
        }

        var left = TimeSpan.FromSeconds(elapsed.Elapsed.TotalSeconds * (1 - Progress) / Progress);
        ProgressText = $"{percent}  ·  about {(left.TotalHours >= 1 ? $"{(int)left.TotalHours}:{left.Minutes:00}:{left.Seconds:00}" : $"{left.Minutes}:{left.Seconds:00}")} left";
    }

    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private async Task StartAsync()
    {
        if (_runner is null)
            return;

        _cts = new CancellationTokenSource();
        IsBusy = true;
        Progress = 0;
        ProgressText = "Choose the file...";

        try
        {
            if (await _runner(ToOptions(), this, _cts.Token))
                Completed?.Invoke();
        }
        finally
        {
            IsBusy = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    /// <summary>Stops the running export (false when none is running).</summary>
    public bool CancelExport()
    {
        if (_cts is null)
            return false;

        _cts.Cancel();
        return true;
    }

    public bool IsWav => Format == ExportFormat.Wav;
    public bool IsMp3 => Format == ExportFormat.Mp3;

    public ExportOptionsViewModel(
        string trackLengthText,
        ExportOptions? previous = null,
        Func<ExportOptions, ExportOptionsViewModel, CancellationToken, Task<bool>>? runner = null)
    {
        TrackLengthText = trackLengthText;
        _runner = runner;

        if (previous is null)
            return;

        Format = previous.Format;
        WavSampleFormat = previous.WavSampleFormat;
        Mp3BitrateKbps = previous.Mp3BitrateKbps;
        FadeInSeconds = previous.FadeInSeconds;
        FadeOutSeconds = previous.FadeOutSeconds;
        TruncateToLastLayer = previous.TruncateToLastLayer;
    }

    public ExportOptions ToOptions() => new(
        Format,
        Mp3BitrateKbps,
        WavSampleFormat,
        FadeInSeconds,
        FadeOutSeconds,
        TruncateToLastLayer);
}
