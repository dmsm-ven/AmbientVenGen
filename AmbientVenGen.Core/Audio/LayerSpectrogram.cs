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

    public static SpectrogramData Compute(LayerSpec layer, int projectSeed, CancellationToken token = default)
    {
        LayerSpec one = Isolate(layer);
        double seconds = Math.Max(1, one.DurationSeconds);
        int columns = ColumnsFor(seconds);

        var mixer = new ProjectMixer(new ProjectSpec(ProjectSpec.CurrentVersion, seconds, projectSeed, 0.0, [one]));
        long totalFrames = (long)(seconds * ProjectMixer.SampleRate);

        float[] peaks = new float[columns];
        float[] chunk = new float[ChunkFrames * 2];
        long frame = 0;

        while (true)
        {
            token.ThrowIfCancellationRequested();

            int read = mixer.Read(chunk);
            if (read <= 0)
                break;

            for (int i = 0; i + 1 < read; i += 2, frame++)
            {
                int column = (int)Math.Min(columns - 1, frame * columns / totalFrames);
                float level = Math.Max(Math.Abs(chunk[i]), Math.Abs(chunk[i + 1]));

                if (level > peaks[column])
                    peaks[column] = level;
            }
        }

        for (int i = 0; i < peaks.Length; i++)
            peaks[i] = Math.Min(1f, peaks[i]);

        return new SpectrogramData(peaks);
    }
}
