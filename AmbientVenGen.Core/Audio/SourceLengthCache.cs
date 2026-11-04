using NAudio.Wave;

namespace AmbientVenGen.Core.Audio;

/// <summary>
/// Length of an audio file, read once from its header and remembered. Used to show the length of the
/// original next to the file name of a recording layer.
/// </summary>
public static class SourceLengthCache
{
    private static readonly Dictionary<string, double> Known = new(StringComparer.OrdinalIgnoreCase);

    public static bool TryGet(string path, out double seconds)
    {
        lock (Known)
            return Known.TryGetValue(path, out seconds);
    }

    /// <summary>Reads the header (off the calling thread when awaited from the UI); 0 when the file cannot be read.</summary>
    public static Task<double> ReadAsync(string path) => Task.Run(() =>
    {
        try
        {
            using var reader = new AudioFileReader(path);
            double seconds = reader.TotalTime.TotalSeconds;

            lock (Known)
                Known[path] = seconds;

            return seconds;
        }
        catch
        {
            // an unreadable file simply has no length shown
            return 0.0;
        }
    });
}
