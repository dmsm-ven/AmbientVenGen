using NAudio.Wave;

namespace AmbientVenGen.Desktop.Audio;

/// <summary>
/// Plays one library file at a time, for listening before use. Independent of the project playback.
/// Must be used from the UI thread (the wave-out callbacks arrive there).
/// </summary>
public sealed class PresetPreviewService : IDisposable
{
    private WaveOut? _player;
    private AudioFileReader? _reader;

    /// <summary>Repeat the file when it ends. Can be changed while playing.</summary>
    public bool Loop { get; set; }

    /// <summary>The file being played, or null.</summary>
    public string? CurrentPath { get; private set; }

    /// <summary>Raised when playback ended by itself (not on Stop and not while looping).</summary>
    public event Action? Ended;

    /// <summary>Position inside the file, 0..1.</summary>
    public double Progress
    {
        get
        {
            AudioFileReader? reader = _reader;
            if (reader is null || reader.TotalTime <= TimeSpan.Zero)
                return 0;

            return Math.Clamp(reader.CurrentTime.TotalSeconds / reader.TotalTime.TotalSeconds, 0, 1);
        }
    }

    /// <summary>Starts the file from the beginning (stopping the previous one). Throws if it cannot be played.</summary>
    public void Play(string path)
    {
        Stop();

        var reader = new AudioFileReader(path);
        var player = new WaveOut();

        try
        {
            player.Init(new SampleToWaveProvider16(new LoopingSampleProvider(reader, () => Loop)));
            player.PlaybackStopped += OnPlaybackStopped;
            player.Play();
        }
        catch
        {
            player.PlaybackStopped -= OnPlaybackStopped;
            player.Dispose();
            reader.Dispose();
            throw;
        }

        _reader = reader;
        _player = player;
        CurrentPath = path;
    }

    public void Stop()
    {
        WaveOut? player = _player;
        AudioFileReader? reader = _reader;

        _player = null;
        _reader = null;
        CurrentPath = null;

        if (player is not null)
        {
            player.PlaybackStopped -= OnPlaybackStopped;
            player.Stop();
            player.Dispose();
        }

        reader?.Dispose();
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        // Ignore the events of players we stopped ourselves.
        if (!ReferenceEquals(sender, _player))
            return;

        Stop();
        Ended?.Invoke();
    }

    public void Dispose() => Stop();

    /// <summary>Starts over at the end of the file while looping is on.</summary>
    private sealed class LoopingSampleProvider(AudioFileReader reader, Func<bool> loop) : ISampleProvider
    {
        public WaveFormat WaveFormat => reader.WaveFormat;

        public int Read(Span<float> buffer)
        {
            int read = reader.Read(buffer);

            if (read == 0 && loop())
            {
                reader.Position = 0;
                read = reader.Read(buffer);
            }

            return read;
        }
    }
}
