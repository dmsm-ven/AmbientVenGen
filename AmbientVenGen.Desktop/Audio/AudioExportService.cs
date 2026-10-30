using AmbientVenGen.Core.Composition;
using AmbientVenGen.Core.Export;
using NAudio.Wave;
using System.IO;

namespace AmbientVenGen.Desktop.Audio;

/// <summary>
/// Export to a file. Rendering itself is in Core (<see cref="ProjectRenderer"/>);
/// this class adds the Windows-only MP3 encoder and file handling.
/// </summary>
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
            ProjectRenderer.RenderWav(
                outputPath,
                spec,
                options,
                float32: options.WavSampleFormat == WavSampleFormat.Float32,
                progress,
                cancellationToken,
                progressScale: 1.0);

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
            ProjectRenderer.RenderWav(temporaryWav, spec, options, float32: false, progress, cancellationToken, progressScale: 0.9);
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
