using AmbientVenGen.Core.Audio;
using AmbientVenGen.Core.Composition;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AmbientVenGen.Desktop.Audio;

public sealed class AudioPlaybackService : IDisposable
{
    private WaveOut? _player;
    private ProjectMixer? _mixer;

    public MasterAnalyzer Analyzer { get; } = new();

    public bool IsPlaying => _player is not null;

    /// <summary>Current playback position (zero when stopped).</summary>
    public double PositionSeconds => _mixer?.PositionSeconds ?? 0;

    /// <summary>Raised when the track played to its end by itself.</summary>
    public event Action? PlaybackEnded;

    public void Start(ProjectSpec spec, double startSeconds)
    {
        Stop();
        SampleLibrary.RequestAll(spec);

        var mixer = new ProjectMixer(spec, Analyzer);
        mixer.Seek(startSeconds);

        var player = new WaveOut();

        try
        {
            player.Init(new SampleToWaveProvider16(mixer));
            player.PlaybackStopped += OnPlaybackStopped;
            player.Play();

            _mixer = mixer;
            _player = player;
        }
        catch
        {
            player.PlaybackStopped -= OnPlaybackStopped;
            player.Dispose();
            throw;
        }
    }

    /// <summary>Applies an edited project to the running playback.</summary>
    public void Update(ProjectSpec spec)
    {
        SampleLibrary.RequestAll(spec);
        _mixer?.SetSpec(spec);
    }

    public void Seek(double seconds) => _mixer?.Seek(seconds);

    public void Stop()
    {
        var player = _player;
        _player = null;
        _mixer = null;

        if (player is null)
            return;

        player.PlaybackStopped -= OnPlaybackStopped;
        player.Stop();
        player.Dispose();
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        // Ignore events of players we already stopped ourselves.
        if (!ReferenceEquals(sender, _player))
            return;

        var player = _player;
        _player = null;
        _mixer = null;

        if (player is not null)
        {
            player.PlaybackStopped -= OnPlaybackStopped;
            player.Dispose();
        }

        PlaybackEnded?.Invoke();
    }

    public void Dispose() => Stop();
}
