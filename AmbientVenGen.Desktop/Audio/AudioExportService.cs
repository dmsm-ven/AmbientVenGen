using AmbientVenGen.Desktop.Models;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.IO;

namespace AmbientVenGen.Desktop.Audio;

public sealed class AudioExportService
{
    public Task ExportAsync(
        string outputPath,
        CompositionSettings settings,
        TimeSpan duration,
        ExportOptions options)
    {
        return Task.Run(() => Export(outputPath, settings, duration, options));
    }

    private static void Export(
        string outputPath,
        CompositionSettings settings,
        TimeSpan duration,
        ExportOptions options)
    {
        if (options.Format == ExportFormat.Wav)
        {
            RenderWav(
                outputPath,
                settings,
                duration,
                options,
                float32: options.WavSampleFormat == WavSampleFormat.Float32);
            return;
        }

        string temporaryWav = Path.Combine(
            Path.GetTempPath(),
            $"{Guid.NewGuid():N}.wav");

        try
        {
            // The MP3 encoder takes a 16-bit PCM source.
            RenderWav(temporaryWav, settings, duration, options, float32: false);

            using var reader = new WaveFileReader(temporaryWav);
            MediaFoundationEncoder.EncodeToMp3(reader, outputPath, options.Mp3BitrateKbps * 1000);
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
        TimeSpan duration,
        ExportOptions options,
        bool float32)
    {
        var synth = new DroneSynthProvider(settings);
        ISampleProvider chain = new DurationSampleProvider(synth, duration);

        if (options.FadeInSeconds > 0 || options.FadeOutSeconds > 0)
        {
            chain = new FadeSampleProvider(
                chain,
                duration,
                TimeSpan.FromSeconds(options.FadeInSeconds),
                TimeSpan.FromSeconds(options.FadeOutSeconds));
        }

        if (float32)
            WaveFileWriter.CreateWaveFile(path, new SampleToWaveProvider(chain));
        else
            WaveFileWriter.CreateWaveFile16(path, chain);
    }
}
