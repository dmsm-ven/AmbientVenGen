namespace AmbientVenGen.Desktop.Audio;

/// <summary>Turns the latest audio into smoothed, log-spaced spectrum bands (0..1) for drawing.</summary>
public sealed class SpectrumProcessor
{
    public const int BandCount = 40;
    private const int N = MasterAnalyzer.FftSize;
    private const double SampleRate = 44100.0;
    private const double MinFrequency = 30.0;
    private const double MaxFrequency = 16000.0;
    private const double FloorDb = -90.0;

    private readonly float[] _samples = new float[N];
    private readonly double[] _real = new double[N];
    private readonly double[] _imag = new double[N];
    private readonly double[] _window = new double[N];
    private readonly int[] _bandStart = new int[BandCount];
    private readonly int[] _bandEnd = new int[BandCount];
    private readonly double[] _smoothed = new double[BandCount];

    public SpectrumProcessor()
    {
        for (int i = 0; i < N; i++)
            _window[i] = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / (N - 1));

        double ratio = MaxFrequency / MinFrequency;

        for (int b = 0; b < BandCount; b++)
        {
            double low = MinFrequency * Math.Pow(ratio, b / (double)BandCount);
            double high = MinFrequency * Math.Pow(ratio, (b + 1) / (double)BandCount);

            int startBin = (int)Math.Floor(low * N / SampleRate);
            int endBin = (int)Math.Ceiling(high * N / SampleRate);

            _bandStart[b] = Math.Clamp(startBin, 1, N / 2 - 1);
            _bandEnd[b] = Math.Clamp(Math.Max(endBin, _bandStart[b] + 1), 2, N / 2);
        }
    }

    /// <summary>Returns a fresh array of band heights. When not active the bands just fall to zero.</summary>
    public double[] Update(MasterAnalyzer analyzer, bool active)
    {
        if (active)
            ComputeBands(analyzer);
        else
            Decay();

        return (double[])_smoothed.Clone();
    }

    private void ComputeBands(MasterAnalyzer analyzer)
    {
        analyzer.CopyLatest(_samples);

        for (int i = 0; i < N; i++)
        {
            _real[i] = _samples[i] * _window[i];
            _imag[i] = 0;
        }

        Fft(_real, _imag);

        for (int b = 0; b < BandCount; b++)
        {
            double peak = 0;

            for (int bin = _bandStart[b]; bin < _bandEnd[b]; bin++)
            {
                double magnitude = Math.Sqrt(_real[bin] * _real[bin] + _imag[bin] * _imag[bin]) * 4.0 / N;
                peak = Math.Max(peak, magnitude);
            }

            double db = 20.0 * Math.Log10(Math.Max(peak, 1e-6));
            double value = Math.Clamp((db - FloorDb) / -FloorDb, 0.0, 1.0);

            double rate = value > _smoothed[b] ? 0.6 : 0.12;
            _smoothed[b] += (value - _smoothed[b]) * rate;
        }
    }

    private void Decay()
    {
        for (int b = 0; b < BandCount; b++)
            _smoothed[b] *= 0.85;
    }

    // In-place iterative radix-2 FFT.
    private static void Fft(double[] re, double[] im)
    {
        int n = re.Length;

        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
                j ^= bit;
            j ^= bit;

            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        for (int length = 2; length <= n; length <<= 1)
        {
            double angle = -2.0 * Math.PI / length;
            double stepRe = Math.Cos(angle);
            double stepIm = Math.Sin(angle);

            for (int start = 0; start < n; start += length)
            {
                double wRe = 1.0;
                double wIm = 0.0;

                for (int k = 0; k < length / 2; k++)
                {
                    int a = start + k;
                    int b = start + k + length / 2;

                    double tRe = re[b] * wRe - im[b] * wIm;
                    double tIm = re[b] * wIm + im[b] * wRe;

                    re[b] = re[a] - tRe;
                    im[b] = im[a] - tIm;
                    re[a] += tRe;
                    im[a] += tIm;

                    double nextRe = wRe * stepRe - wIm * stepIm;
                    wIm = wRe * stepIm + wIm * stepRe;
                    wRe = nextRe;
                }
            }
        }
    }
}
