namespace AmbientVenGen.Desktop.Audio;


using NAudio.Wave;
public sealed class DurationSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private long _remainingSamples;

    public WaveFormat WaveFormat => _source.WaveFormat;

    public DurationSampleProvider(
        ISampleProvider source,
        TimeSpan duration)
    {
        _source = source;

        _remainingSamples = (long)(
            duration.TotalSeconds *
            WaveFormat.SampleRate *
            WaveFormat.Channels);
    }

    public int Read(Span<float> buffer)
    {
        if (_remainingSamples <= 0)
            return 0;

        int count = (int)Math.Min(
            buffer.Length,
            _remainingSamples);

        // Preserve complete stereo frames.
        count -= count % WaveFormat.Channels;

        if (count == 0)
            return 0;

        int read = _source.Read(buffer[..count]);

        _remainingSamples -= read;

        return read;
    }
}
