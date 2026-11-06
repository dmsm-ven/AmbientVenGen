using AmbientVenGen.Core.Composition;

namespace AmbientVenGen.Core.Audio;

/// <summary>A spectrogram of one layer: <see cref="Bands"/> log-spaced frequency bands over <see cref="Columns"/> moments, values 0..1.</summary>
public sealed class SpectrogramData(int columns, int bands, float[] values)
{
    public int Columns { get; } = columns;
    public int Bands { get; } = bands;

    /// <summary>Band-major: <c>Values[band * Columns + column]</c>, band 0 is the lowest frequency.</summary>
    public float[] Values { get; } = values;
}

/// <summary>
/// Renders one layer alone (no reverb, one play, from time zero) with the same engine that plays the project and
/// reduces it to a small picture: the FFT is taken only at the moments that become columns, so the cost is the
/// cost of generating the sound, not of the analysis.
/// </summary>
public static class LayerSpectrogram
{
    public const int FftSize = 2048;
    public const double MinHz = 30;
    public const double MaxHz = 16000;
    private const int ChunkFrames = 1024;
    private const double SecondsPerColumn = 0.25;
    private const int MaxColumns = 1200;

    // Loudness range shown: a full-scale sine is 0 dB.
    private const double FloorDb = -96;
    private const double CeilingDb = -12;

    public static int ColumnsFor(double seconds) => Math.Clamp((int)Math.Ceiling(seconds / SecondsPerColumn), 16, MaxColumns);

    /// <summary>The spec that is rendered: the layer alone, one play, from the start, without reverb send.</summary>
    public static LayerSpec Isolate(LayerSpec layer) => layer with
    {
        StartSeconds = 0,
        Repeats = 1,
        RepeatGapSeconds = 0,
        Muted = false,
        ReverbSend = 0
    };

    public static SpectrogramData Compute(LayerSpec layer, int projectSeed, int bands = 48, CancellationToken token = default)
    {
        LayerSpec one = Isolate(layer);
        double seconds = Math.Max(1, one.DurationSeconds);
        int columns = ColumnsFor(seconds);

        var mixer = new ProjectMixer(new ProjectSpec(ProjectSpec.CurrentVersion, seconds, projectSeed, 0.0, [one]));
        long totalFrames = (long)(seconds * ProjectMixer.SampleRate);

        int[] bandStart = new int[bands];
        int[] bandEnd = new int[bands];
        BandEdges(bands, bandStart, bandEnd);

        double[] window = new double[FftSize];
        for (int i = 0; i < FftSize; i++)
            window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (FftSize - 1));

        float[] chunk = new float[ChunkFrames * 2];
        float[] ring = new float[FftSize];
        double[] re = new double[FftSize];
        double[] im = new double[FftSize];
        float[] values = new float[bands * columns];

        long frames = 0;
        int column = 0;

        while (column < columns)
        {
            token.ThrowIfCancellationRequested();

            int read = mixer.Read(chunk);
            if (read <= 0)
                break;

            int got = read / 2;

            // slide the new mono samples into the ring
            if (got >= FftSize)
            {
                for (int i = 0; i < FftSize; i++)
                    ring[i] = 0.5f * (chunk[(got - FftSize + i) * 2] + chunk[(got - FftSize + i) * 2 + 1]);
            }
            else
            {
                Array.Copy(ring, got, ring, 0, FftSize - got);
                for (int i = 0; i < got; i++)
                    ring[FftSize - got + i] = 0.5f * (chunk[i * 2] + chunk[i * 2 + 1]);
            }

            frames += got;

            // every column whose moment has been reached takes the window that ends here
            while (column < columns && frames >= (column + 1) * (double)totalFrames / columns)
            {
                Analyze(ring, window, re, im, bandStart, bandEnd, values, column, columns);
                column++;
            }
        }

        return new SpectrogramData(columns, bands, values);
    }

    // FFT bin range of every band (log spaced between MinHz and MaxHz, at least one bin each).
    private static void BandEdges(int bands, int[] start, int[] end)
    {
        double binHz = ProjectMixer.SampleRate / (double)FftSize;
        int lastBin = FftSize / 2 - 1;

        for (int b = 0; b < bands; b++)
        {
            double lo = MinHz * Math.Pow(MaxHz / MinHz, b / (double)bands);
            double hi = MinHz * Math.Pow(MaxHz / MinHz, (b + 1) / (double)bands);

            int first = Math.Clamp((int)Math.Round(lo / binHz), 1, lastBin);
            int last = Math.Clamp((int)Math.Round(hi / binHz) - 1, first, lastBin);

            start[b] = first;
            end[b] = last;
        }
    }

    private static void Analyze(
        float[] ring, double[] window, double[] re, double[] im, int[] bandStart, int[] bandEnd,
        float[] values, int column, int columns)
    {
        for (int i = 0; i < FftSize; i++)
        {
            re[i] = ring[i] * window[i];
            im[i] = 0;
        }

        Fft(re, im);

        // a sine of amplitude A gives |X| = A * N / 4 with the Hann window
        const double scale = 4.0 / FftSize;

        for (int b = 0; b < bandStart.Length; b++)
        {
            double power = 0;
            for (int k = bandStart[b]; k <= bandEnd[b]; k++)
            {
                double amplitude = Math.Sqrt(re[k] * re[k] + im[k] * im[k]) * scale;
                power = Math.Max(power, amplitude * amplitude);
            }

            double db = 10 * Math.Log10(power + 1e-12);
            values[b * columns + column] = (float)Math.Clamp((db - FloorDb) / (CeilingDb - FloorDb), 0, 1);
        }
    }

    /// <summary>In-place radix-2 FFT (the length must be a power of two).</summary>
    internal static void Fft(double[] re, double[] im)
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
            double angle = -2 * Math.PI / length;
            double wr = Math.Cos(angle);
            double wi = Math.Sin(angle);

            for (int i = 0; i < n; i += length)
            {
                double cr = 1, ci = 0;
                for (int k = 0; k < length / 2; k++)
                {
                    int a = i + k;
                    int c = i + k + length / 2;
                    double tr = re[c] * cr - im[c] * ci;
                    double ti = re[c] * ci + im[c] * cr;

                    re[c] = re[a] - tr;
                    im[c] = im[a] - ti;
                    re[a] += tr;
                    im[a] += ti;

                    double next = cr * wr - ci * wi;
                    ci = cr * wi + ci * wr;
                    cr = next;
                }
            }
        }
    }
}
