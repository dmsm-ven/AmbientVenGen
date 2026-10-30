using AmbientVenGen.Desktop.Composition;
using AmbientVenGen.Desktop.Models;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.IO;

namespace AmbientVenGen.Desktop.Audio;

public sealed class AudioExportService
{
    /// <summary>Renders the project to a file. Throws <see cref="OperationCanceledException"/> when cancelled.</summary>
    public Task ExportAsync(
        string outputPath,
        ProjectSpec spec,
        ExportOptions options,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        return Task.Run(() => Export(outputPath, spec, options, progress, cancellationToken), CancellationToken.None);
    }

    private static void Export(
        string outputPath,
        ProjectSpec spec,
        ExportOptions options,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        if (options.Format == ExportFormat.Wav)
        {
            RenderWav(
                outputPath,
                spec,
                options,
                float32: options.WavSampleFormat == WavSampleFormat.Float32,
                progress,
                cancellationToken,
                scale: 1.0);

            if (cancellationToken.IsCancellationRequested)
            {
                TryDelete(outputPath);
                throw new OperationCanceledException(cancellationToken);
            }

            return;
        }

        string temporaryWav = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.wav");

        try
        {
            // Rendering is the long part: 0..90%. The MP3 encoder takes a 16-bit PCM source.
            RenderWav(temporaryWav, spec, options, float32: false, progress, cancellationToken, scale: 0.9);
            cancellationToken.ThrowIfCancellationRequested();

            progress?.Report(0.92);

            using (var reader = new WaveFileReader(temporaryWav))
            {
                MediaFoundationEncoder.EncodeToMp3(reader, outputPath, options.Mp3BitrateKbps * 1000);
            }

            progress?.Report(1.0);
        }
        finally
        {
            TryDelete(temporaryWav);
        }
    }

    private static void RenderWav(
        string path,
        ProjectSpec spec,
        ExportOptions options,
        bool float32,
        IProgress<double>? progress,
        CancellationToken cancellationToken,
        double scale)
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

        chain = new ProgressSampleProvider(chain, duration, progress, cancellationToken, scale);

        if (float32)
            WaveFileWriter.CreateWaveFile(path, new SampleToWaveProvider(chain));
        else
            WaveFileWriter.CreateWaveFile16(path, chain);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // A leftover file must not mask the export result.
        }
        catch (UnauthorizedAccessException)
        {
            // A leftover file must not mask the export result.
        }
    }
}
