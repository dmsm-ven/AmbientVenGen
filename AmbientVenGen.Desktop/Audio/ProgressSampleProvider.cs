using NAudio.Wave;

namespace AmbientVenGen.Desktop.Audio;

/// <summary>
/// Reports how much of the track has been rendered and stops the render when cancelled
/// (by returning 0 samples, which ends the WAV writer cleanly).
/// </summary>
public sealed class ProgressSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly long _totalSamples;
    private readonly IProgress<double>? _progress;
    private readonly double _scale;
    private readonly CancellationToken _cancellationToken;
    private long _doneSamples;
    private double _lastReported = -1;

    public WaveFormat WaveFormat => _source.WaveFormat;

    /// <param name="scale">Progress is reported as 0..scale (leaves room for a later encoding step).</param>
    public ProgressSampleProvider(
        ISampleProvider source,
        TimeSpan duration,
        IProgress<double>? progress,
        CancellationToken cancellationToken,
        double scale = 1.0)
    {
        _source = source;
        _progress = progress;
        _cancellationToken = cancellationToken;
        _scale = scale;
        _totalSamples = (long)(duration.TotalSeconds * source.WaveFormat.SampleRate * source.WaveFormat.Channels);
    }

    public int Read(Span<float> buffer)
    {
        if (_cancellationToken.IsCancellationRequested)
            return 0;

        int read = _source.Read(buffer);
        _doneSamples += read;

        double fraction = _totalSamples > 0 ? Math.Min(1.0, _doneSamples / (double)_totalSamples) : 0.0;

        if (fraction - _lastReported >= 0.005 || read == 0)
        {
            _lastReported = fraction;
            _progress?.Report(fraction * _scale);
        }

        return read;
    }
}
