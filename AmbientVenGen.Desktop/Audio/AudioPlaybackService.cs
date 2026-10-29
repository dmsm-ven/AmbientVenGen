using AmbientVenGen.Desktop.Models;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AmbientVenGen.Desktop.Audio;

public sealed class AudioPlaybackService : IDisposable
{
    private WaveOut? _player;
    private DroneSynthProvider? _synth;

    public bool IsPlaying => _player is not null;

    public void Start(CompositionSettings settings)
    {
        Stop();

        var synth = new DroneSynthProvider(settings);
        var player = new WaveOut();

        try
        {
            player.Init(new SampleToWaveProvider16(synth));
            player.Play();

            _synth = synth;
            _player = player;
        }
        catch
        {
            player.Dispose();
            throw;
        }
    }

    public void Update(CompositionSettings settings)
    {
        if (_synth is null)
            return;

        _synth.DroneLevel = settings.DroneLevel;
        _synth.AtmosphereLevel = settings.AtmosphereLevel;
        _synth.Brightness = settings.Brightness;
        _synth.Modulation = settings.Modulation;
    }

    public void Stop()
    {
        var player = _player;
        _player = null;
        _synth = null;

        if (player is null)
            return;

        player.Stop();
        player.Dispose();
    }

    public void Dispose() => Stop();
}
