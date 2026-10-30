using AmbientVenGen.Core.Audio;
using AmbientVenGen.Core.Composition;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AmbientVenGen.Core.Export;

/// <summary>Renders a whole project to a WAV file (offline, faster than real time).</summary>
public static class ProjectRenderer
{
    /// <param name="float32">32-bit float samples instead of 16-bit PCM.</param>
    /// <param name="progress">Reports 0..<paramref name="progressScale"/> (leaves room for a later encoding step).</param>
    /// <remarks>
    /// When <paramref name="cancellationToken"/> is cancelled the render stops early and the file is
    /// left incomplete: the caller must check the token and delete it.
    /// </remarks>
    public static void RenderWav(
        string path,
        ProjectSpec spec,
        ExportOptions options,
        bool float32,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default,
        double progressScale = 1.0)
    {
        var duration = TimeSpan.FromSeconds(spec.DurationSeconds);
        ISampleProvider chain = new ProjectMixer(spec);

        if (options.FadeInSeconds > 0 || options.FadeOutSeconds > 0)
        {
            chain = new FadeSampleProvider(
                chain,
                duration,
                TimeSpan.FromSeconds(options.FadeInSeconds),
                TimeSpan.FromSeconds(options.FadeOutSeconds));
        }

        chain = new ProgressSampleProvider(chain, duration, progress, cancellationToken, progressScale);

        if (float32)
            WaveFileWriter.CreateWaveFile(path, new SampleToWaveProvider(chain));
        else
            WaveFileWriter.CreateWaveFile16(path, chain);
    }
}
