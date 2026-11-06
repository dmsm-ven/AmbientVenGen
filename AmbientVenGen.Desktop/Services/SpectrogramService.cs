using AmbientVenGen.Core.Audio;
using AmbientVenGen.Core.Composition;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AmbientVenGen.Desktop.Services;

/// <summary>
/// Spectrograms of the layers for the timeline. A layer is analysed only after it has stopped changing for
/// <see cref="Debounce"/> (default 1.5 s), one at a time on a background thread with a lower priority, and a result is
/// remembered by what the layer sounds like (not by where it sits), so moving, renaming or muting costs nothing.
/// Until the new picture is ready the previous one stays on the clip.
/// </summary>
public sealed class SpectrogramService : IDisposable
{
    private const int CacheLimit = 80;

    private readonly Dictionary<Guid, Entry> _entries = [];
    private readonly Dictionary<string, BitmapSource> _cache = [];
    private readonly SemaphoreSlim _worker = new(1, 1);
    private bool _disposed;

    public TimeSpan Debounce { get; set; } = TimeSpan.FromSeconds(1.5);

    /// <summary>Raised on the UI thread when a picture changed.</summary>
    public event Action? Changed;

    /// <summary>The newest ready picture of the layer (one play of it), or null.</summary>
    public BitmapSource? GetBitmap(Layer layer) =>
        _entries.TryGetValue(layer.Id, out Entry? entry) ? entry.Bitmap : null;

    /// <summary>Call on the UI thread whenever the project changes: only layers whose sound changed are (re)analysed.</summary>
    public void Update(IReadOnlyList<Layer> layers, int projectSeed)
    {
        if (_disposed)
            return;

        bool changed = false;
        var present = new HashSet<Guid>();

        foreach (Layer layer in layers)
        {
            present.Add(layer.Id);

            if (!_entries.TryGetValue(layer.Id, out Entry? entry))
                _entries[layer.Id] = entry = new Entry();

            LayerSpec spec = layer.ToSpec();
            string key = KeyOf(spec, projectSeed);

            if (key == entry.Key)
            {
                entry.CancelPending();
            }
            else if (_cache.TryGetValue(key, out BitmapSource? known))
            {
                entry.CancelPending();
                entry.Key = key;
                entry.Bitmap = known;
                changed = true;
            }
            else if (key != entry.PendingKey)
            {
                Schedule(entry, spec, key, projectSeed);
            }
        }

        foreach (Guid gone in _entries.Keys.Where(id => !present.Contains(id)).ToList())
        {
            _entries[gone].CancelPending();
            _entries.Remove(gone);
            changed = true;
        }

        if (changed)
            Changed?.Invoke();
    }

    /// <summary>Stops all waiting and running analyses (the spectrogram was switched off).</summary>
    public void CancelPending()
    {
        foreach (Entry entry in _entries.Values)
            entry.CancelPending();
    }

    private void Schedule(Entry entry, LayerSpec spec, string key, int projectSeed)
    {
        entry.CancelPending();
        entry.PendingKey = key;
        var cts = new CancellationTokenSource();
        entry.Cts = cts;

        _ = RunAsync(entry, spec, key, projectSeed, cts.Token);
    }

    private async Task RunAsync(Entry entry, LayerSpec spec, string key, int projectSeed, CancellationToken token)
    {
        try
        {
            await Task.Delay(Debounce, token).ConfigureAwait(false);
            await _worker.WaitAsync(token).ConfigureAwait(false);

            try
            {
                BitmapSource bitmap = await Task.Run(() => Analyze(spec, projectSeed, token), token).ConfigureAwait(false);

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (_disposed || token.IsCancellationRequested)
                        return;

                    if (_cache.Count >= CacheLimit)
                        _cache.Clear();

                    _cache[key] = bitmap;
                    entry.Key = key;
                    entry.Bitmap = bitmap;
                    entry.PendingKey = null;
                    Changed?.Invoke();
                });
            }
            finally
            {
                _worker.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // the layer changed again (or the spectrogram was switched off): a newer request replaces this one
        }
        catch (Exception)
        {
            // an analysis that fails (for example an unreadable recording) simply leaves the clip without a picture
        }
    }

    private static BitmapSource Analyze(LayerSpec spec, int projectSeed, CancellationToken token)
    {
        ThreadPriority previous = Thread.CurrentThread.Priority;
        Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;

        try
        {
            SpectrogramData data = LayerSpectrogram.Compute(spec, projectSeed, token: token);
            return ToBitmap(data);
        }
        finally
        {
            Thread.CurrentThread.Priority = previous;
        }
    }

    // What makes the layer sound the way it does: everything except where it sits on the timeline, its name, mute,
    // pan, repeats and the reverb send.
    internal static string KeyOf(LayerSpec spec, int projectSeed)
    {
        LayerSpec sound = LayerSpectrogram.Isolate(spec) with { Id = Guid.Empty, Name = string.Empty, Pan = 0, PanDrift = 0 };
        return projectSeed + "|" + JsonSerializer.Serialize(sound);
    }

    // Low frequencies at the bottom; transparent where it is quiet, so the clip's own colour shows through.
    private static BitmapSource ToBitmap(SpectrogramData data)
    {
        int width = data.Columns;
        int height = data.Bands;
        byte[] pixels = new byte[width * height * 4];

        for (int band = 0; band < height; band++)
        {
            int row = height - 1 - band;

            for (int column = 0; column < width; column++)
            {
                float v = data.Values[band * width + column];
                (byte r, byte g, byte b) = Ramp(v);
                int alpha = (int)Math.Clamp(v * 1.5f * 255f, 0, 255);

                int offset = (row * width + column) * 4;
                pixels[offset] = (byte)(b * alpha / 255);     // premultiplied BGRA
                pixels[offset + 1] = (byte)(g * alpha / 255);
                pixels[offset + 2] = (byte)(r * alpha / 255);
                pixels[offset + 3] = (byte)alpha;
            }
        }

        BitmapSource bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    // dark violet -> blue -> cyan -> white
    private static (byte R, byte G, byte B) Ramp(float v)
    {
        (float t, byte r, byte g, byte b)[] stops =
        [
            (0.00f, 20, 10, 60),
            (0.35f, 70, 50, 190),
            (0.65f, 60, 170, 255),
            (1.00f, 255, 255, 255)
        ];

        for (int i = 1; i < stops.Length; i++)
        {
            if (v <= stops[i].t)
            {
                float k = (v - stops[i - 1].t) / (stops[i].t - stops[i - 1].t);
                return (
                    (byte)(stops[i - 1].r + (stops[i].r - stops[i - 1].r) * k),
                    (byte)(stops[i - 1].g + (stops[i].g - stops[i - 1].g) * k),
                    (byte)(stops[i - 1].b + (stops[i].b - stops[i - 1].b) * k));
            }
        }

        return (255, 255, 255);
    }

    public void Dispose()
    {
        _disposed = true;
        CancelPending();
        _worker.Dispose();
    }

    private sealed class Entry
    {
        public string? Key { get; set; }
        public string? PendingKey { get; set; }
        public BitmapSource? Bitmap { get; set; }
        public CancellationTokenSource? Cts { get; set; }

        public void CancelPending()
        {
            Cts?.Cancel();
            Cts = null;
            PendingKey = null;
        }
    }
}
