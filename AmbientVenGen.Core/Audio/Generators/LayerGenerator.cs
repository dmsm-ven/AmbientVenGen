using AmbientVenGen.Core.Composition;

namespace AmbientVenGen.Core.Audio;

/// <summary>Parameters of one render block, already evaluated from the layer curves.</summary>
internal readonly struct BlockParams
{
    public readonly float Brightness;
    public readonly float Modulation;
    public readonly float PitchHz;

    /// <summary>Time since the layer started, at the start of the block (recordings play from it).</summary>
    public readonly double LocalSeconds;

    /// <summary>Playback speed of a recording (1 = original).</summary>
    public readonly float Speed;

    public BlockParams(float brightness, float modulation, float pitchHz, double localSeconds = 0, float speed = 1f)
    {
        LocalSeconds = localSeconds;
        Speed = speed;
        Brightness = brightness;
        Modulation = modulation;
        PitchHz = pitchHz;
    }
}

/// <summary>
/// A sound source. Renders raw stereo audio (about +-1); the mixer applies level, fades and pan.
/// Each generator keeps its own state, so it can be rendered block by block.
/// </summary>
internal abstract class LayerGenerator
{
    protected const int SampleRate = 44100;
    protected const double TwoPi = Math.PI * 2.0;

    /// <summary>Highest frequency a partial may have (a little below Nyquist, to stay clean).</summary>
    protected const double MaxPartialHz = SampleRate * 0.45;

    public abstract void Render(in BlockParams p, Span<float> left, Span<float> right);

    public static LayerGenerator Create(LayerType type, int seed, string? sourcePath = null, NoiseType noiseType = NoiseType.White) => type switch
    {
        LayerType.FieldRecord => new FieldRecordGenerator(seed, sourcePath),
        LayerType.Noise => new NoiseGenerator(seed, noiseType),
        LayerType.Pad => new PadGenerator(seed),
        _ => new DroneGenerator(seed)
    };

    protected static void Wrap(ref double phase)
    {
        if (phase >= TwoPi)
            phase -= TwoPi;
    }
}
