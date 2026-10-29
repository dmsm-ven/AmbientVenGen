using AmbientVenGen.Desktop.Models;
using NAudio.Wave;
using System.IO;

namespace AmbientVenGen.Desktop.Audio;

public sealed class AudioExportService
{
    public Task ExportAsync(
        string outputPath,
        CompositionSettings settings,
        TimeSpan duration,
        bool mp3)
    {
        return Task.Run(() => Export(outputPath, settings, duration, mp3));
    }

    private static void Export(
        string outputPath,
        CompositionSettings settings,
        TimeSpan duration,
        bool mp3)
    {
        if (!mp3)
        {
            RenderWav(outputPath, settings, duration);
            return;
        }

        string temporaryWav = Path.Combine(
            Path.GetTempPath(),
            $"{Guid.NewGuid():N}.wav");

        try
        {
            RenderWav(temporaryWav, settings, duration);

            using var reader = new WaveFileReader(temporaryWav);
            MediaFoundationEncoder.EncodeToMp3(reader, outputPath, 192_000);
        }
        finally
        {
            try
            {
                File.Delete(temporaryWav);
            }
            catch (IOException)
            {
                // A leftover temporary file must not mask the export result.
            }
            catch (UnauthorizedAccessException)
            {
                // A leftover temporary file must not mask the export result.
            }
        }
    }

    private static void RenderWav(
        string path,
        CompositionSettings settings,
        TimeSpan duration)
    {
        var synth = new DroneSynthProvider(settings);
        var limited = new DurationSampleProvider(synth, duration);
        WaveFileWriter.CreateWaveFile16(path, limited);
    }
}
