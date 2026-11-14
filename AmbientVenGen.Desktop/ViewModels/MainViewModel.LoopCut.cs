using AmbientVenGen.Core.Audio;
using AmbientVenGen.Core.Composition;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AmbientVenGen.Desktop.ViewModels;

/// <summary>
/// Scissors of the Recording group: the loop piece is marked on the waveform of the whole file (no repeats, no stretch),
/// so the marked seconds are seconds of the file and go straight into Loop start and Loop duration.
/// </summary>
public partial class MainViewModel
{
    private const int PeakCount = 900;

    /// <summary>The scissors are on: the waveform of the file is shown at the bottom of the Recording group.</summary>
    [ObservableProperty]
    private bool _isLoopCutMode;

    /// <summary>The marked range in seconds of the file (NaN: nothing is marked).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoopCutText))]
    private double _loopCutStart = double.NaN;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoopCutText))]
    private double _loopCutEnd = double.NaN;

    /// <summary>Peaks of the whole file for the waveform.</summary>
    [ObservableProperty]
    private float[]? _sourcePeaks;

    public string LoopCutText => !double.IsNaN(LoopCutStart) && LoopCutEnd > LoopCutStart
        ? $"{FormatClockTenths(LoopCutStart)} - {FormatClockTenths(LoopCutEnd)}  ({LoopCutEnd - LoopCutStart:0.0} s)"
        : "Drag on the waveform to mark the piece to loop";

    private static string FormatClockTenths(double seconds)
    {
        var span = TimeSpan.FromSeconds(seconds);
        string tenths = (span.Milliseconds / 100).ToString();
        return (span.TotalHours >= 1 ? span.ToString(@"h\:mm\:ss") : span.ToString(@"m\:ss")) + "." + tenths;
    }

    private void CancelLoopCut()
    {
        IsLoopCutMode = false;
        SourcePeaks = null;
        LoopCutStart = LoopCutEnd = double.NaN;
    }

    /// <summary>First click: show the waveform of the file. Second click: put the marked range into Loop start and duration.</summary>
    [RelayCommand]
    private void ToggleLoopCut()
    {
        if (SelectedLayer is not { Type: LayerType.FieldRecord } layer)
        {
            CancelLoopCut();
            return;
        }

        if (!IsLoopCutMode)
        {
            DecodedSample? sample = layer.SourcePath is { } path ? SampleLibrary.TryGet(path) : null;

            if (sample is null || sample.Frames < 2)
            {
                _messageService.ShowError("The recording is not loaded (yet), so it cannot be shown.");
                return;
            }

            SourcePeaks = PeaksOf(sample);

            // the range starts as the piece that loops now
            double total = sample.Frames / (double)ProjectMixer.SampleRate;
            LoopCutStart = Math.Min(layer.LoopStart, total);
            LoopCutEnd = layer.LoopDuration > 0 ? Math.Min(total, layer.LoopStart + layer.LoopDuration) : total;
            IsLoopCutMode = true;
            return;
        }

        double start = LoopCutStart;
        double end = LoopCutEnd;
        bool marked = !double.IsNaN(start) && end > start;
        CancelLoopCut();

        if (!marked)
            return;

        if (end - start < SampleTransform.MinPieceSeconds)
        {
            _messageService.ShowError($"The marked piece is too short: a loop is at least {SampleTransform.MinPieceSeconds:0.#} s.");
            return;
        }

        layer.LoopStart = Math.Round(start, 2);
        layer.LoopDuration = Math.Round(end - start, 2);
    }

    // The loudest moment of every part of the file, 0..1 (long files are looked at with a step, it is only a picture).
    private static float[] PeaksOf(DecodedSample sample)
    {
        var peaks = new float[PeakCount];
        float max = 0;

        for (int p = 0; p < PeakCount; p++)
        {
            int from = (int)((long)p * sample.Frames / PeakCount);
            int to = Math.Max(from + 1, (int)((long)(p + 1) * sample.Frames / PeakCount));
            int step = Math.Max(1, (to - from) / 2000);

            float peak = 0;
            for (int i = from; i < to && i < sample.Frames; i += step)
                peak = Math.Max(peak, Math.Max(Math.Abs(sample.Left[i]), Math.Abs(sample.Right[i])));

            peaks[p] = peak;
            max = Math.Max(max, peak);
        }

        if (max > 0)
        {
            for (int p = 0; p < PeakCount; p++)
                peaks[p] /= max;
        }

        return peaks;
    }
}
