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

    // Results of the processing (reverse / stretch / pitch), by recording and settings.
    private static readonly ConcurrentDictionary<string, Task<DecodedSample?>> Processed =
        new(StringComparer.OrdinalIgnoreCase);

    // The heavy part (stretch / pitch) is calculated one at a time: it uses all the cores.
    private static readonly SemaphoreSlim HeavySlot = new(1, 1);

    // The newest heavy request for every recording. While a knob is dragged many settings are asked for in a row;
    // after a short wait only the last of them is calculated.
    private static readonly ConcurrentDictionary<string, string> LatestHeavy = new(StringComparer.OrdinalIgnoreCase);

    private const int SettleMilliseconds = 700;

    // Progress (0..1) of every heavy job that has not finished yet; a job that waits has 0.
    private static readonly ConcurrentDictionary<string, double> HeavyProgress = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How many stretch / pitch results are being calculated (or wait for it) and how far they are, 0..1 on average.</summary>
    public static (int Jobs, double Progress) GetProcessing()
    {
        int jobs = 0;
        double sum = 0;

        foreach (double value in HeavyProgress.Values)
        {
            jobs++;
            sum += value;
        }

        return (jobs, jobs == 0 ? 0 : sum / jobs);
    }

    // Recordings that could not be read (the file was moved or deleted, an unsupported format).
    private static readonly ConcurrentDictionary<string, bool> Missing = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True when the recording was tried and cannot be read (the layer then stays silent).</summary>
    public static bool IsMissing(string? path) => !string.IsNullOrEmpty(path) && Missing.ContainsKey(path);

    /// <summary>Raised (on any thread) when a processed recording became ready or failed.</summary>
    public static event Action? StateChanged;

    /// <summary>The decoded recording as it is, or null while it is still loading (or cannot be read).</summary>
    public static DecodedSample? TryGet(string path) =>
        Cache.TryGetValue(path, out Task<DecodedSample?>? task) && task.IsCompletedSuccessfully
            ? task.Result
            : null;

    /// <summary>The recording with the transform applied, or null while it is being prepared (or cannot be).</summary>
    public static DecodedSample? TryGet(string path, SampleTransform transform)
    {
        transform = transform.ForProcessing();

        if (transform.IsIdentity)
            return TryGet(path);

        return Processed.TryGetValue(ProcessedKey(path, transform), out Task<DecodedSample?>? task) && task.IsCompletedSuccessfully
            ? task.Result
            : null;
    }

    /// <summary>Starts decoding in the background if it has not been started yet.</summary>
    public static void Request(string path) =>
        Cache.GetOrAdd(path, static p => Task.Run(() => Decode(p)));

    /// <summary>Starts decoding and processing in the background if it has not been started yet.</summary>
    public static Task<DecodedSample?> Request(string path, SampleTransform transform, bool force = false)
    {
        transform = transform.ForProcessing();

        if (transform.IsIdentity)
            return Cache.GetOrAdd(path, static p => Task.Run(() => Decode(p)));

        string key = ProcessedKey(path, transform);

        return Processed.GetOrAdd(key, _ =>
        {
            if (transform.IsHeavy)
            {
                LatestHeavy[path] = key;
                HeavyProgress[key] = 0;
            }

            return Task.Run(() => Process(path, transform, key, force));
        });
    }

    /// <summary>True while the stretch / pitch result of the recording is being calculated.</summary>
    public static bool IsPending(string path, SampleTransform transform) =>
        (transform = transform.ForProcessing()).IsHeavy
        && Processed.TryGetValue(ProcessedKey(path, transform), out Task<DecodedSample?>? task)
        && !task.IsCompleted;

    /// <summary>Starts decoding (and processing) of every recording used by the project.</summary>
    public static void RequestAll(ProjectSpec spec)
    {
        foreach ((string path, SampleTransform transform) in SourcesOf(spec))
            Request(path, transform);
    }

    /// <summary>Decodes and processes every recording used by the project and waits for the result.</summary>
    public static void Preload(ProjectSpec spec)
    {
        var sources = SourcesOf(spec).ToList();

        // Start everything at once (decoding runs in parallel), then wait; a result that was replaced by a newer
        // request in the meantime is asked for again, this time for sure.
        foreach ((string path, SampleTransform transform) in sources)
            Request(path, transform, force: true);

        foreach ((string path, SampleTransform transform) in sources)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                Task<DecodedSample?> task = Request(path, transform, force: true);
                task.Wait();

                if (task.Result is not null || !transform.IsHeavy)
                    break;
            }
        }
    }

    private static IEnumerable<(string Path, SampleTransform Transform)> SourcesOf(ProjectSpec spec) => spec.Layers
        .Where(layer => layer.Type == LayerType.FieldRecord && !layer.Muted && !string.IsNullOrEmpty(layer.SourcePath))
        .Select(layer => (layer.SourcePath!, layer.Transform.ForProcessing()))
        .Distinct();

    private static string ProcessedKey(string path, SampleTransform t) =>
        FormattableString.Invariant($"{path}|{(t.Reverse ? 1 : 0)}|{t.Stretch:0.####}|{t.PitchSemitones:0.####}|{t.LoopStart:0.###}|{t.LoopDuration:0.###}");

    private static DecodedSample? Process(string path, SampleTransform transform, string processedKey, bool force)
    {
        DecodedSample? result = null;
        CleanLegacyCache();

        try
        {
            DecodedSample? source = Request(path, SampleTransform.None).GetAwaiter().GetResult();

            if (source is not null)
            {
                DecodedSample stretched = source;

                if (transform.IsHeavy)
                {
                    // only the chosen piece is stretched / transposed
                    source = Trimmed(source, transform);

                    // wait for the settings to settle; a newer request for this recording replaces this one
                    Thread.Sleep(SettleMilliseconds);

                    if (!force && (!LatestHeavy.TryGetValue(path, out string? latest) || latest != processedKey))
                    {
                        Processed.TryRemove(processedKey, out _);
                        StateChanged?.Invoke();
                        return null;
                    }

                    DecodedSample stored = Calculate(source, transform, processedKey);

                    stretched = stored;
                }

                result = transform.Reverse ? Reversed(stretched) : stretched;
            }
        }
        catch (Exception)
        {
            // out of memory, a failed calculation: the layer stays silent
            result = null;
        }
        finally
        {
            HeavyProgress.TryRemove(processedKey, out _);
        }

        StateChanged?.Invoke();
        return result;
    }

    private static DecodedSample Calculate(DecodedSample source, SampleTransform transform, string key)
    {
        HeavySlot.Wait();

        try
        {
            return PaulStretch.Process(source, transform.Stretch, transform.PitchSemitones,
                progress: value => HeavyProgress[key] = value);
        }
        finally
        {
            HeavySlot.Release();
        }
    }

    // The first version kept the results on disk; they are not kept any more, the old folder is removed once.
    private static int _legacyCleaned;

    private static void CleanLegacyCache()
    {
        if (Interlocked.Exchange(ref _legacyCleaned, 1) != 0)
            return;

        try
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AmbientForge", "cache");

            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
        catch
        {
            // nothing to do about it
        }
    }

    private static DecodedSample Trimmed(DecodedSample sample, SampleTransform transform)
    {
        (int begin, int end) = SampleTransform.PieceOf(sample.Frames, transform.LoopStart, transform.LoopDuration, SampleRate);

        if (begin == 0 && end == sample.Frames)
            return sample;

        return new DecodedSample(sample.Left[begin..end], sample.Right[begin..end]);
    }

    private static DecodedSample Reversed(DecodedSample sample)
    {
        float[] left = (float[])sample.Left.Clone();
        float[] right = (float[])sample.Right.Clone();
        Array.Reverse(left);
        Array.Reverse(right);
        return new DecodedSample(left, right);
    }

    private static DecodedSample? Decode(string path)
    {
        DecodedSample? result = DecodeCore(path);

        if (result is null)
            Missing[path] = true;
        else
            Missing.TryRemove(path, out _);

        StateChanged?.Invoke();
        return result;
    }

    private static DecodedSample? DecodeCore(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

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
