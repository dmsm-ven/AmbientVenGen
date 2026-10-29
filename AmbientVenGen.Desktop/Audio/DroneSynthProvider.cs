using AmbientVenGen.Desktop.Models;
using NAudio.Wave;

namespace AmbientVenGen.Desktop.Audio;

public sealed class DroneSynthProvider : ISampleProvider
{
    private const int SampleRate = 44100;
    private const int Channels = 2;
    private const double TwoPi = Math.PI * 2.0;

    private double _phase1;
    private double _phase2;
    private double _phase3;
    private double _phase4;
    private long _samplePosition;

    private uint _noiseState;
    private float _filteredNoise;

    private volatile float _droneLevel;
    private volatile float _atmosphereLevel;
    private volatile float _brightness;
    private volatile float _modulation;

    public WaveFormat WaveFormat { get; } =
        WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels);

    public float DroneLevel
    {
        get => _droneLevel;
        set => _droneLevel = Math.Clamp(value, 0f, 1f);
    }

    public float AtmosphereLevel
    {
        get => _atmosphereLevel;
        set => _atmosphereLevel = Math.Clamp(value, 0f, 1f);
    }

    public float Brightness
    {
        get => _brightness;
        set => _brightness = Math.Clamp(value, 0f, 1f);
    }

    public float Modulation
    {
        get => _modulation;
        set => _modulation = Math.Clamp(value, 0f, 1f);
    }

    public DroneSynthProvider(CompositionSettings settings)
    {
        _noiseState = unchecked((uint)settings.Seed);

        if (_noiseState == 0)
            _noiseState = 1;

        DroneLevel = settings.DroneLevel;
        AtmosphereLevel = settings.AtmosphereLevel;
        Brightness = settings.Brightness;
        Modulation = settings.Modulation;
    }

    public int Read(Span<float> buffer)
    {
        int frames = buffer.Length / Channels;

        float droneLevel = DroneLevel;
        float atmosphereLevel = AtmosphereLevel;
        float brightness = Brightness;
        float modulation = Modulation;

        float noiseAlpha = 0.002f + brightness * 0.055f;

        for (int i = 0; i < frames; i++)
        {
            double time = _samplePosition / (double)SampleRate;

            // Slow, slightly detuned oscillators.
            double lfo = Math.Sin(TwoPi * 0.045 * time);
            double pitchDrift = lfo * modulation * 0.025;

            _phase1 += TwoPi * (42.0 + pitchDrift) / SampleRate;
            _phase2 += TwoPi * (42.13 + pitchDrift * 1.1) / SampleRate;
            _phase3 += TwoPi * (63.7 + pitchDrift * 0.7) / SampleRate;
            _phase4 += TwoPi * 86.2 / SampleRate;

            NormalizePhase(ref _phase1);
            NormalizePhase(ref _phase2);
            NormalizePhase(ref _phase3);
            NormalizePhase(ref _phase4);

            double drone =
                Math.Sin(_phase1) * 0.50 +
                Math.Sin(_phase2) * 0.30 +
                Math.Sin(_phase3) * 0.14 +
                Math.Sin(_phase4) * (0.02 + brightness * 0.10);

            // Deterministic pseudo-random noise.
            _noiseState ^= _noiseState << 13;
            _noiseState ^= _noiseState >> 17;
            _noiseState ^= _noiseState << 5;

            float white =
                (_noiseState & 0xFFFF) / 32767.5f - 1f;

            // Simple one-pole low-pass filter.
            _filteredNoise +=
                noiseAlpha * (white - _filteredNoise);

            double volumeLfo =
                1.0 + lfo * modulation * 0.16;

            float sample = (float)(
                (drone * droneLevel +
                 _filteredNoise * atmosphereLevel)
                * volumeLfo * 0.35);

            // Initial prototype: centered stereo.
            buffer[i * 2] = sample;
            buffer[i * 2 + 1] = sample;

            _samplePosition++;
        }

        return frames * Channels;
    }

    private static void NormalizePhase(ref double phase)
    {
        if (phase >= TwoPi)
            phase -= TwoPi;
    }
}
