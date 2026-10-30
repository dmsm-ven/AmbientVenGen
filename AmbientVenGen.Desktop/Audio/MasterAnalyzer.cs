namespace AmbientVenGen.Desktop.Audio;

/// <summary>
/// Collects what the audio thread plays so the UI can draw the level meter and the spectrum.
/// The audio thread only writes, the UI thread only reads; small tearing is harmless for display.
/// </summary>
public sealed class MasterAnalyzer
{
    public const int FftSize = 2048;
    private const int RingSize = 8192;

    private readonly float[] _ring = new float[RingSize];
    private long _writePosition;
    private float _peakLeft;
    private float _peakRight;
    private float _overPeak;

    /// <summary>Called from the audio thread with the final interleaved stereo block.</summary>
    internal void Push(ReadOnlySpan<float> interleaved, int frames, float peakLeft, float peakRight, float overPeak)
    {
        long write = Volatile.Read(ref _writePosition);

        for (int i = 0; i < frames; i++)
            _ring[(int)((write + i) & (RingSize - 1))] = 0.5f * (interleaved[i * 2] + interleaved[i * 2 + 1]);

        Volatile.Write(ref _writePosition, write + frames);

        _peakLeft = Math.Max(_peakLeft, peakLeft);
        _peakRight = Math.Max(_peakRight, peakRight);
        _overPeak = Math.Max(_overPeak, overPeak);
    }

    /// <summary>Returns the peaks since the previous call. overPeak is measured before the limiter.</summary>
    public void TakePeaks(out float left, out float right, out float overPeak)
    {
        left = Interlocked.Exchange(ref _peakLeft, 0f);
        right = Interlocked.Exchange(ref _peakRight, 0f);
        overPeak = Interlocked.Exchange(ref _overPeak, 0f);
    }

    /// <summary>Copies the latest <see cref="FftSize"/> mono samples (oldest first).</summary>
    public void CopyLatest(float[] destination)
    {
        long end = Volatile.Read(ref _writePosition);
        long start = end - FftSize;

        for (int i = 0; i < FftSize; i++)
        {
            long index = start + i;
            destination[i] = index < 0 ? 0f : _ring[(int)(index & (RingSize - 1))];
        }
    }
}
