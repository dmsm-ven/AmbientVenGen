using NAudio.Wave;

namespace AmbientVenGen.Core.Audio;

/// <summary>
/// Applies a fade-in at the start and a fade-out at the end of a track of known length.
/// Fades use a sine ease, so they start and end smoothly without an audible kink.
/// </summary>
public sealed class FadeSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _channels;
    private readonly long _totalFrames;
    private readonly long _fadeInFrames;
    private readonly long _fadeOutFrames;
    private long _framePosition;

    public WaveFormat WaveFormat => _source.WaveFormat;

    public FadeSampleProvider(
        ISampleProvider source,
        TimeSpan duration,
        TimeSpan fadeIn,
        TimeSpan fadeOut)
    {
        _source = source;
        _channels = source.WaveFormat.Channels;

        int sampleRate = source.WaveFormat.SampleRate;
        _totalFrames = (long)(duration.TotalSeconds * sampleRate);

        // Fades can never overlap: each is limited to half of the track.
        long maxFade = _totalFrames / 2;
        _fadeInFrames = Math.Min((long)(fadeIn.TotalSeconds * sampleRate), maxFade);
        _fadeOutFrames = Math.Min((long)(fadeOut.TotalSeconds * sampleRate), maxFade);
    }

    public int Read(Span<float> buffer)
    {
        int read = _source.Read(buffer);
        int frames = read / _channels;

        for (int i = 0; i < frames; i++)
        {
            long position = _framePosition + i;
            double gain = 1.0;

            if (position < _fadeInFrames)
                gain *= EaseSine(position / (double)_fadeInFrames);

            long framesLeft = _totalFrames - position;
            if (framesLeft < _fadeOutFrames)
                gain *= EaseSine(Math.Max(0, framesLeft) / (double)_fadeOutFrames);

            if (gain < 1.0)
            {
                for (int c = 0; c < _channels; c++)
                    buffer[i * _channels + c] *= (float)gain;
            }
        }

        _framePosition += frames;
        return read;
    }

    // 0..1 -> 0..1, sine ease-in-out.
    private static double EaseSine(double t) => 0.5 - 0.5 * Math.Cos(Math.PI * Math.Clamp(t, 0.0, 1.0));
}
