using AmbientVenGen.Core.Composition;

namespace AmbientVenGen.Core.Audio;

/// <summary>
/// The "loudness bars" of one layer: the peak level of the layer in every moment (<see cref="Columns"/> of them over one
/// play), 0..1. The timeline draws them as the same stepped bars as the thumbnails of the preset library.
/// </summary>
public sealed class SpectrogramData(float[] peaks)
{
    public int Columns => Peaks.Length;

    /// <summary>Peak level of every column, 0..1 (full scale = 1).</summary>
    public float[] Peaks { get; } = peaks;

    /// <summary>The highest peak: the bars are scaled by it, so a quiet layer is still readable.</summary>
    public float Max { get; } = peaks.Length == 0 ? 0f : peaks.Max();
}

/// <summary>
/// Renders one layer alone (no reverb, one play, from time zero) with the same engine that plays the project and keeps
/// the peak of every small time slice. The cost is the cost of generating the sound.
/// </summary>
public static class LayerSpectrogram
{
    private const int ChunkFrames = 1024;
    private const double SecondsPerColumn = 0.25;
    private const int MaxColumns = 1200;

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

    // A long layer is rendered by several threads, every one its own part of the time line; each part starts a little
    // earlier (preroll) so that its filters and fades are already running where the part begins.
    private const double ParallelFromSeconds = 40;
    private const double PrerollSeconds = 1.0;

    // The quick picture has 1/16 of the columns and every one is taken from a short piece of the sound.
    private const int QuickDivider = 16;
    private const double QuickPrerollSeconds = 0.1;
    private const double QuickSliceSeconds = 0.15;

    /// <summary>A layer ready to be rendered: isolated, and its recording (and the stretched version) loaded.</summary>
    public sealed record Prepared(ProjectSpec Spec, double Seconds, int Columns);

    public static Prepared Prepare(LayerSpec layer, int projectSeed, CancellationToken token = default)
    {
        LayerSpec one = Isolate(layer);
        double seconds = Math.Max(1, one.DurationSeconds);
        var spec = new ProjectSpec(ProjectSpec.CurrentVersion, seconds, projectSeed, 0.0, [one]);

        // A recording (and its stretched / transposed version) must be ready, or the bars would be empty.
        SampleLibrary.Preload(spec);
        token.ThrowIfCancellationRequested();

        return new Prepared(spec, seconds, ColumnsFor(seconds));
    }

    public static SpectrogramData Compute(LayerSpec layer, int projectSeed, CancellationToken token = default) =>
        ComputeFull(Prepare(layer, projectSeed, token), token);

    /// <summary>The rough picture: 1/16 of the columns, a short piece of the sound for each. Takes a small part of a second.</summary>
    public static SpectrogramData ComputeQuick(Prepared prepared, CancellationToken token = default)
    {
        int columns = Math.Max(8, prepared.Columns / QuickDivider);
        float[] peaks = new float[columns];
        var mixer = new ProjectMixer(prepared.Spec);
        int preroll = (int)(QuickPrerollSeconds * ProjectMixer.SampleRate);
        int slice = (int)(QuickSliceSeconds * ProjectMixer.SampleRate);
        float[] chunk = new float[(preroll + slice) * 2];

        for (int column = 0; column < columns; column++)
        {
            token.ThrowIfCancellationRequested();

            double middle = (column + 0.5) * prepared.Seconds / columns;
            mixer.Seek(Math.Max(0, middle - QuickSliceSeconds / 2 - QuickPrerollSeconds));

            int read = mixer.Read(chunk);
            float peak = 0;

            for (int i = preroll * 2; i + 1 < read; i += 2)
                peak = Math.Max(peak, Math.Max(Math.Abs(chunk[i]), Math.Abs(chunk[i + 1])));

            peaks[column] = Math.Min(1f, peak);
        }

        return new SpectrogramData(peaks);
    }

    /// <summary>The full picture; a long layer is rendered in parts by several threads.</summary>
    public static SpectrogramData ComputeFull(Prepared prepared, CancellationToken token = default)
    {
        int columns = prepared.Columns;
        long totalFrames = (long)(prepared.Seconds * ProjectMixer.SampleRate);
        float[] peaks = new float[columns];

        int parts = prepared.Seconds >= ParallelFromSeconds
            ? Math.Clamp(Environment.ProcessorCount - 1, 1, 4)
            : 1;

        if (parts == 1)
        {
            RenderColumns(prepared.Spec, columns, totalFrames, 0, columns, peaks, token);
        }
        else
        {
            var threads = new List<Thread>();
            Exception? failure = null;

            for (int part = 0; part < parts; part++)
            {
                int from = part * columns / parts;
                int to = (part + 1) * columns / parts;

                var thread = new Thread(() =>
                {
                    try
                    {
                        RenderColumns(prepared.Spec, columns, totalFrames, from, to, peaks, token);
                    }
                    catch (Exception exception)
                    {
                        failure ??= exception;
                    }
                })
                {
                    IsBackground = true,
                    Priority = ThreadPriority.BelowNormal
                };

                threads.Add(thread);
                thread.Start();
            }

            foreach (Thread thread in threads)
                thread.Join();

            token.ThrowIfCancellationRequested();

            if (failure is not null)
                throw failure;
        }

        for (int i = 0; i < peaks.Length; i++)
            peaks[i] = Math.Min(1f, peaks[i]);

        return new SpectrogramData(peaks);
    }

    // Columns [firstColumn, endColumn) of the picture; the parts write to different columns of the same array.
    private static void RenderColumns(ProjectSpec spec, int columns, long totalFrames, int firstColumn, int endColumn,
        float[] peaks, CancellationToken token)
    {
        long startFrame = firstColumn * totalFrames / columns;
        long endFrame = endColumn == columns ? totalFrames : endColumn * totalFrames / columns;
        long preroll = Math.Min(startFrame, (long)(PrerollSeconds * ProjectMixer.SampleRate));

        var mixer = new ProjectMixer(spec);
        mixer.Seek((startFrame - preroll) / (double)ProjectMixer.SampleRate);

        float[] chunk = new float[ChunkFrames * 2];
        long frame = startFrame - preroll;

        while (frame < endFrame)
        {
            token.ThrowIfCancellationRequested();

            int read = mixer.Read(chunk);
            if (read <= 0)
                break;

            for (int i = 0; i + 1 < read && frame < endFrame; i += 2, frame++)
            {
                if (frame < startFrame)
                    continue;

                int column = (int)Math.Min(columns - 1, frame * columns / totalFrames);
                float level = Math.Max(Math.Abs(chunk[i]), Math.Abs(chunk[i + 1]));

                if (level > peaks[column])
                    peaks[column] = level;
            }
        }
    }
}
