using NAudio.Wave;
using System.IO;

namespace AmbientVenGen.Desktop.Services;

public sealed record AudioFileInfo(TimeSpan Duration, long SizeBytes, string Extension);

/// <summary>
/// Reads metadata and a loudness outline of an audio/video file (flac, mp3, wav, m4a, mp4...).
/// Contains no UI code, so it is safe to call from a background thread.
/// </summary>
public sealed class AudioFileAnalyzer
{
    private const int ChunkSamples = 16384;

    /// <summary>Returns null if the file cannot be opened as audio.</summary>
    public AudioFileInfo? TryReadInfo(string path)
    {
        try
        {
            var file = new FileInfo(path);

            using var reader = new AudioFileReader(path);
            return new AudioFileInfo(reader.TotalTime, file.Length, file.Extension.ToLowerInvariant());
        }
        catch (Exception)
        {
            // NAudio/Media Foundation throw many unrelated exception types for unsupported
            // or missing files; for the caller they all mean "cannot read this".
            return null;
        }
    }

    /// <summary>
    /// Peak level of each of <paramref name="columns"/> equal parts of the whole file, scaled so that
    /// the loudest part is 1. Returns null if the file cannot be read.
    /// </summary>
    public float[]? TryReadPeaks(string path, int columns)
    {
        try
        {
            using var reader = new AudioFileReader(path);

            int channels = reader.WaveFormat.Channels;

            // AudioFileReader produces 32-bit float, so Length is in bytes of float samples.
            long totalSamples = reader.Length / sizeof(float);
            long samplesPerColumn = Math.Max(channels, totalSamples / columns);
            samplesPerColumn -= samplesPerColumn % channels;

            var peaks = new float[columns];
            var buffer = new float[ChunkSamples - ChunkSamples % channels];

            bool ended = false;

            for (int column = 0; column < columns && !ended; column++)
            {
                long remaining = samplesPerColumn;
                float peak = 0f;

                while (remaining > 0)
                {
                    int wanted = (int)Math.Min(buffer.Length, remaining);
                    int read = reader.Read(buffer.AsSpan(0, wanted));

                    if (read == 0)
                    {
                        ended = true; // the file ended early: leave the remaining columns empty
                        break;
                    }

                    for (int i = 0; i < read; i++)
                        peak = Math.Max(peak, Math.Abs(buffer[i]));

                    remaining -= read;
                }

                peaks[column] = peak;
            }

            // Quiet field recordings would be a flat line, so show the shape relative to the loudest part.
            float max = peaks.Max();
            if (max > 0f)
            {
                for (int i = 0; i < peaks.Length; i++)
                    peaks[i] /= max;
            }

            return peaks;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
