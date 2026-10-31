using AmbientVenGen.Core.Composition;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Collections.Concurrent;

namespace AmbientVenGen.Core.Audio;

/// <summary>A recording decoded to 44.1 kHz stereo float.</summary>
public sealed class DecodedSample
{
    public float[] Left { get; }
    public float[] Right { get; }
    public int Frames => Left.Length;

    public DecodedSample(float[] left, float[] right)
    {
        Left = left;
        Right = right;
    }
}

/// <summary>
/// Decodes recordings once and keeps them in memory (shared by playback and export).
/// Decoding runs on a background thread, so the audio thread never waits for a file.
/// </summary>
public static class SampleLibrary
{
    private const int SampleRate = ProjectMixer.SampleRate;

    /// <summary>Longer files are cut: a stereo float copy of 10 minutes takes about 100 MB.</summary>
    public const double MaxSeconds = 600;

    private static readonly ConcurrentDictionary<string, Task<DecodedSample?>> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The decoded recording, or null while it is still loading (or cannot be read).</summary>
    public static DecodedSample? TryGet(string path) =>
        Cache.TryGetValue(path, out Task<DecodedSample?>? task) && task.IsCompletedSuccessfully
            ? task.Result
            : null;

    /// <summary>Starts decoding in the background if it has not been started yet.</summary>
    public static void Request(string path) =>
        Cache.GetOrAdd(path, static p => Task.Run(() => Decode(p)));

    /// <summary>Starts decoding of every recording used by the project.</summary>
    public static void RequestAll(ProjectSpec spec)
    {
        foreach (string path in PathsOf(spec))
            Request(path);
    }

    /// <summary>Decodes every recording used by the project and waits for the result.</summary>
    public static void Preload(ProjectSpec spec)
    {
        Task[] tasks = PathsOf(spec)
            .Select(path => (Task)Cache.GetOrAdd(path, static p => Task.Run(() => Decode(p))))
            .ToArray();

        Task.WaitAll(tasks);
    }

    private static IEnumerable<string> PathsOf(ProjectSpec spec) => spec.Layers
        .Where(layer => layer.Type == LayerType.FieldRecord && !layer.Muted && !string.IsNullOrEmpty(layer.SourcePath))
        .Select(layer => layer.SourcePath!)
        .Distinct(StringComparer.OrdinalIgnoreCase);

    private static DecodedSample? Decode(string path)
    {
        try
        {
            using var reader = new AudioFileReader(path);

            ISampleProvider source = reader;
            if (reader.WaveFormat.SampleRate != SampleRate)
                source = new WdlResamplingSampleProvider(source, SampleRate);

            int channels = source.WaveFormat.Channels;
            int maxFrames = (int)(MaxSeconds * SampleRate);
            int guess = (int)Math.Min(maxFrames, reader.TotalTime.TotalSeconds * SampleRate + SampleRate);

            var left = new List<float>(Math.Max(guess, 1024));
            var right = new List<float>(Math.Max(guess, 1024));
            var buffer = new float[4096 * channels];

            while (left.Count < maxFrames)
            {
                int read = source.Read(buffer.AsSpan());
                if (read <= 0)
                    break;

                int frames = read / channels;

                for (int i = 0; i < frames && left.Count < maxFrames; i++)
                {
                    float l = buffer[i * channels];
                    left.Add(l);
                    right.Add(channels > 1 ? buffer[i * channels + 1] : l);
                }
            }

            return left.Count < 2 ? null : new DecodedSample(left.ToArray(), right.ToArray());
        }
        catch (Exception)
        {
            // Missing file, unsupported format, codec missing: the layer simply stays silent.
            return null;
        }
    }
}
