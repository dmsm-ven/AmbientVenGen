namespace AmbientVenGen.Core.Audio;

/// <summary>
/// Extreme time stretching (the "Paulstretch" method): the recording is cut into long overlapping windows, only the
/// magnitudes of their spectra are kept, the phases are replaced with random ones, and the windows are overlapped
/// again further apart. The result is a smooth, endless-sounding drone of the same pitch. A transposition is made on
/// top of it by stretching more (or less) and then reading the result faster (or slower), so the length stays as
/// set. The two channels are processed with different random phases, which makes the result wide.
/// </summary>
public static class PaulStretch
{
    private const int SampleRate = ProjectMixer.SampleRate;
    private const int MaxWindow = 1 << 15;
    private const int MinWindow = 1 << 10;

    /// <summary>Stretch 1..50, pitch in semitones. The result is never longer than <see cref="SampleLibrary.MaxSeconds"/>.</summary>
    public static DecodedSample Process(DecodedSample input, double stretch, double pitchSemitones, CancellationToken token = default,
        Action<double>? progress = null)
    {
        stretch = Math.Max(1.0, stretch);
        double ratio = Math.Pow(2.0, pitchSemitones / 12.0);
        double effective = Math.Max(0.1, stretch * ratio);

        // Only the beginning is used when the result would be longer than the limit.
        long maxFrames = (long)(SampleLibrary.MaxSeconds * SampleRate);
        int used = (int)Math.Min(input.Frames, Math.Max(MinWindow, maxFrames / Math.Max(effective, stretch)));

        int window = MaxWindow;
        while (window > MinWindow && window > used)
            window >>= 1;

        int outFrames = Math.Max(window, (int)(used * effective));
        var left = new float[outFrames];
        var right = new float[outFrames];

        // progress: all the windows of both channels
        long total = 2L * (outFrames / (window / 2) + 1);
        long done = 0;

        void Step()
        {
            long now = Interlocked.Increment(ref done);

            if ((now & 3) == 0)
                progress?.Invoke(Math.Min(1.0, (double)now / total));
        }

        Parallel.Invoke(
            () => Run(input.Left, used, left, window, effective, 0x1234567u, token, Step),
            () => Run(input.Right, used, right, window, effective, 0x7654321u, token, Step));

        token.ThrowIfCancellationRequested();

        if (Math.Abs(ratio - 1.0) > 1e-6)
        {
            left = Resample(left, ratio);
            right = Resample(right, ratio);
        }

        MatchLoudness(input, used, left, right);
        return new DecodedSample(left, right);
    }

    private static void Run(float[] source, int used, float[] output, int window, double stretch, uint seed, CancellationToken token, Action step)
    {
        int half = window / 2;
        var fft = new Fft(window);
        var re = new float[window];
        var im = new float[window];
        var shape = new float[window];

        for (int i = 0; i < window; i++)
        {
            double x = 2.0 * i / window - 1.0;
            shape[i] = (float)Math.Pow(Math.Max(0.0, 1.0 - x * x), 1.25);
        }

        uint state = seed;
        double step = half / stretch;       // how far the reading position moves for every window
        int frames = output.Length / half + 1;

        for (int frame = 0; frame < frames; frame++)
        {
            if ((frame & 7) == 0)
                token.ThrowIfCancellationRequested();

            step();

            int start = (int)(frame * step);

            for (int i = 0; i < window; i++)
            {
                int index = start + i;
                re[i] = index < used ? source[index] * shape[i] : 0f;
                im[i] = 0f;
            }

            fft.Transform(re, im, inverse: false);

            // Keep the magnitudes, give every bin a random phase (symmetric, so that the result is real).
            for (int k = 0; k <= half; k++)
            {
                float magnitude = MathF.Sqrt(re[k] * re[k] + im[k] * im[k]);

                if (k == 0 || k == half)
                {
                    re[k] = magnitude;
                    im[k] = 0f;
                    continue;
                }

                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                float phase = (state & 0xFFFFFF) / 16777216f * MathF.Tau;

                re[k] = magnitude * MathF.Cos(phase);
                im[k] = magnitude * MathF.Sin(phase);
                re[window - k] = re[k];
                im[window - k] = -im[k];
            }

            fft.Transform(re, im, inverse: true);

            int write = frame * half;
            for (int i = 0; i < window && write + i < output.Length; i++)
                output[write + i] += re[i] * shape[i];
        }
    }

    // Reads the sound "ratio" times faster (a higher pitch, a shorter sound), with a cubic interpolation.
    private static float[] Resample(float[] data, double ratio)
    {
        int length = Math.Max(2, (int)(data.Length / ratio));
        var result = new float[length];

        for (int i = 0; i < length; i++)
        {
            double position = i * ratio;
            int index = (int)position;
            float t = (float)(position - index);

            float p0 = data[Math.Max(index - 1, 0)];
            float p1 = data[Math.Min(index, data.Length - 1)];
            float p2 = data[Math.Min(index + 1, data.Length - 1)];
            float p3 = data[Math.Min(index + 2, data.Length - 1)];

            result[i] = p1 + 0.5f * t * (p2 - p0 + t * (2f * p0 - 5f * p1 + 4f * p2 - p3 + t * (3f * (p1 - p2) + p3 - p0)));
        }

        return result;
    }

    // The stretched sound is as loud as the part of the recording it was made from (and never clips).
    private static void MatchLoudness(DecodedSample input, int used, float[] left, float[] right)
    {
        double inputPower = 0;
        for (int i = 0; i < used; i++)
            inputPower += input.Left[i] * (double)input.Left[i] + input.Right[i] * (double)input.Right[i];

        double outputPower = 0;
        float peak = 0f;
        for (int i = 0; i < left.Length; i++)
        {
            outputPower += left[i] * (double)left[i] + right[i] * (double)right[i];
            peak = Math.Max(peak, Math.Max(Math.Abs(left[i]), Math.Abs(right[i])));
        }

        if (outputPower < 1e-12 || inputPower < 1e-12)
            return;

        double gain = Math.Sqrt(inputPower / used / (outputPower / left.Length));
        gain = Math.Clamp(gain, 0.02, 50.0);

        if (peak * gain > 0.98)
            gain = 0.98 / peak;

        float g = (float)gain;
        for (int i = 0; i < left.Length; i++)
        {
            left[i] *= g;
            right[i] *= g;
        }
    }

    /// <summary>Radix-2 complex FFT (in place), sizes that are powers of two.</summary>
    private sealed class Fft
    {
        private readonly int _size;
        private readonly float[] _cos;
        private readonly float[] _sin;
        private readonly int[] _reverse;

        public Fft(int size)
        {
            _size = size;
            _cos = new float[size / 2];
            _sin = new float[size / 2];
            _reverse = new int[size];

            for (int i = 0; i < size / 2; i++)
            {
                _cos[i] = (float)Math.Cos(2.0 * Math.PI * i / size);
                _sin[i] = (float)Math.Sin(2.0 * Math.PI * i / size);
            }

            int bits = (int)Math.Log2(size);
            for (int i = 0; i < size; i++)
            {
                int r = 0;
                for (int b = 0; b < bits; b++)
                    if ((i & (1 << b)) != 0)
                        r |= 1 << (bits - 1 - b);

                _reverse[i] = r;
            }
        }

        public void Transform(float[] re, float[] im, bool inverse)
        {
            for (int i = 0; i < _size; i++)
            {
                int j = _reverse[i];
                if (j > i)
                {
                    (re[i], re[j]) = (re[j], re[i]);
                    (im[i], im[j]) = (im[j], im[i]);
                }
            }

            float sign = inverse ? 1f : -1f;

            for (int length = 2; length <= _size; length <<= 1)
            {
                int halfLength = length / 2;
                int stride = _size / length;

                for (int start = 0; start < _size; start += length)
                {
                    for (int k = 0; k < halfLength; k++)
                    {
                        float wr = _cos[k * stride];
                        float wi = sign * _sin[k * stride];

                        int a = start + k;
                        int b = a + halfLength;

                        float tr = re[b] * wr - im[b] * wi;
                        float ti = re[b] * wi + im[b] * wr;

                        re[b] = re[a] - tr;
                        im[b] = im[a] - ti;
                        re[a] += tr;
                        im[a] += ti;
                    }
                }
            }

            if (inverse)
            {
                float scale = 1f / _size;
                for (int i = 0; i < _size; i++)
                {
                    re[i] *= scale;
                    im[i] *= scale;
                }
            }
        }
    }
}
