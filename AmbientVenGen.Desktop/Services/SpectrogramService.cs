using AmbientVenGen.Core.Audio;
using AmbientVenGen.Core.Composition;
using System.Text.Json;
using System.Windows;

namespace AmbientVenGen.Desktop.Services;

/// <summary>
/// Loudness bars of the layers for the timeline (the "spectrogram" button). A layer is analysed only after it has stopped changing for
/// <see cref="Debounce"/> (default 0.8 s), two at a time on background threads with a lower priority; a rough picture (1/16 of the bars) is shown first and the full one replaces it, and a result is
/// remembered by what the layer sounds like (not by where it sits), so moving, renaming or muting costs nothing.
/// Until the new bars are ready the previous one stays on the clip.
/// </summary>
public sealed class SpectrogramService : IDisposable
{
    private const int CacheLimit = 80;

    private readonly Dictionary<Guid, Entry> _entries = [];
    private readonly Dictionary<string, SpectrogramData> _cache = [];
    // Layers are analysed in parallel (a long one also uses several threads inside), but not all at once.
    private readonly SemaphoreSlim _worker = new(2, 2);
    private bool _disposed;

    public TimeSpan Debounce { get; set; } = TimeSpan.FromSeconds(0.8);

    /// <summary>Raised on the UI thread when a picture changed.</summary>
    public event Action? Changed;

    /// <summary>The newest ready bars of the layer (one play of it), or null.</summary>
    public SpectrogramData? GetData(Layer layer) =>
        _entries.TryGetValue(layer.Id, out Entry? entry) ? entry.Data : null;

    /// <summary>True while the bars of the layer are waiting for the analysis or being analysed.</summary>
    public bool IsPending(Layer layer) =>
        _entries.TryGetValue(layer.Id, out Entry? entry) && entry.PendingKey is not null;

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
            else if (_cache.TryGetValue(key, out SpectrogramData? known))
            {
                entry.CancelPending();
                entry.Key = key;
                entry.Data = known;
                changed = true;
            }
            else if (key != entry.PendingKey)
            {
                // The sound changed: the old bars no longer belong to it, so they go away at once
                // and the clip shows that the new ones are being prepared.
                if (entry.Data is not null)
                {
                    entry.Data = null;
                    entry.Key = null;
                }

                Schedule(entry, spec, key, projectSeed);
                changed = true;
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
                LayerSpectrogram.Prepared prepared = await Task.Run(() => LayerSpectrogram.Prepare(spec, projectSeed, token), token)
                    .ConfigureAwait(false);

                // The full picture is calculated at the same time as the rough one; when it is ready it replaces it.
                Task<SpectrogramData> full = Task.Run(() => Analyze(() => LayerSpectrogram.ComputeFull(prepared, token)), token);
                SpectrogramData quick = await Task.Run(() => Analyze(() => LayerSpectrogram.ComputeQuick(prepared, token)), token)
                    .ConfigureAwait(false);

                if (!full.IsCompleted)
                {
                    // Only the data: the key stays empty, so the layer is still "being analysed".
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (_disposed || token.IsCancellationRequested || entry.PendingKey != key)
                            return;

                        entry.Data = quick;
                        Changed?.Invoke();
                    });
                }

                SpectrogramData data = await full.ConfigureAwait(false);

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (_disposed || token.IsCancellationRequested)
                        return;

                    if (_cache.Count >= CacheLimit)
                        _cache.Clear();

                    _cache[key] = data;
                    entry.Key = key;
                    entry.Data = data;
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
            if (!token.IsCancellationRequested && Application.Current is { } app)
                _ = app.Dispatcher.InvokeAsync(() =>
                {
                    if (entry.PendingKey == key)
                        entry.PendingKey = null;

                    Changed?.Invoke();
                });
        }
    }

    private static SpectrogramData Analyze(Func<SpectrogramData> compute)
    {
        ThreadPriority previous = Thread.CurrentThread.Priority;
        Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;

        try
        {
            return compute();
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
        public SpectrogramData? Data { get; set; }
        public CancellationTokenSource? Cts { get; set; }

        public void CancelPending()
        {
            Cts?.Cancel();
            Cts = null;
            PendingKey = null;
        }
    }
}
