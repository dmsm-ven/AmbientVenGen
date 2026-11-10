using AmbientVenGen.Core.Composition;

namespace AmbientVenGen.Core.Audio;

/// <summary>
/// Plays a decoded recording in a loop (a chosen piece of it; the end is cross-faded into the beginning) at an adjustable speed
/// (like a tape: slower is also lower). The read position is kept as a running value, so changing the speed
/// while playing is smooth; after a seek it is recomputed from the layer time.
/// Brightness is a low-pass filter, modulation a slow breathing of the level.
/// </summary>
internal sealed class FieldRecordGenerator : LayerGenerator
{
    private readonly string? _path;
    private readonly SampleTransform _transform;
    private readonly double _breathRate;
    private double _breathPhase;
    private float _lowL;
    private float _lowR;
    private DecodedSample? _sample;
    private double _position;
    private double _expectedLocalSeconds = double.NaN;

    public FieldRecordGenerator(int seed, string? path, SampleTransform transform = default)
    {
        _transform = transform.Stretch < 1 ? transform with { Stretch = 1.0, PitchSemitones = 0.0 } : transform;
        var random = new Random(seed);

        _path = path;
        _breathPhase = random.NextDouble() * TwoPi;
        _breathRate = 0.04 + random.NextDouble() * 0.06;
    }

    public override void Render(in BlockParams p, Span<float> left, Span<float> right)
    {
        if (_sample is null && !string.IsNullOrEmpty(_path))
        {
            // The file is decoded (and stretched / transposed) on a background thread; until it is ready the layer is silent.
            _sample = SampleLibrary.TryGet(_path, _transform);

            if (_sample is null)
                SampleLibrary.Request(_path, _transform);
        }

        DecodedSample? sample = _sample;

        if (sample is null || sample.Frames < 2)
        {
            left.Clear();
            right.Clear();
            return;
        }

        // The piece that is looped. A stretched / transposed recording was cut before that and is played whole;
        // otherwise the piece is a window over the file (seen from the end when the file is played backwards).
        int begin = 0;
        int end = sample.Frames;

        if (!_transform.IsHeavy)
        {
            (int pieceBegin, int pieceEnd) = SampleTransform.PieceOf(sample.Frames,
                _transform.Reverse ? _transform.TrimEnd : _transform.TrimStart,
                _transform.Reverse ? _transform.TrimStart : _transform.TrimEnd, SampleRate);
            begin = pieceBegin;
            end = pieceEnd;
        }

        int frames = end - begin;

        if (frames < 2)
        {
            left.Clear();
            right.Clear();
            return;
        }

        int crossfade = (int)Math.Min(Math.Clamp(_transform.Crossfade, 0, SampleTransform.MaxCrossfade) * SampleRate, frames / 2);
        int period = frames - crossfade;
        double speed = Math.Clamp(p.Speed, 0.05f, 8f);

        // Continue from where the last block ended; jump only after a seek (time did not follow on).
        if (double.IsNaN(_expectedLocalSeconds) || Math.Abs(p.LocalSeconds - _expectedLocalSeconds) > 0.02)
            _position = Math.Max(0.0, p.LocalSeconds) * SampleRate * speed % period;

        _expectedLocalSeconds = p.LocalSeconds + left.Length / (double)SampleRate;

        // Brightness 0..1 -> low-pass cutoff about 300 Hz .. 19 kHz.
        double cutoff = Math.Min(300.0 * Math.Pow(2.0, Math.Clamp(p.Brightness, 0f, 1f) * 6.0), SampleRate * 0.45);
        float alpha = (float)(1.0 - Math.Exp(-TwoPi * cutoff / SampleRate));
        double depth = Math.Clamp(p.Modulation, 0f, 1f) * 0.35;

        for (int n = 0; n < left.Length; n++)
        {
            int index = (int)_position;
            float fraction = (float)(_position - index);

            float l = Interpolate(sample.Left, begin + index, fraction, end);
            float r = Interpolate(sample.Right, begin + index, fraction, end);

            if (crossfade > 0 && index < crossfade)
            {
                // Beginning blended with the tail that follows the loop point (equal power).
                double angle = _position / crossfade * Math.PI / 2.0;
                float fadeIn = (float)Math.Sin(angle);
                float fadeOut = (float)Math.Cos(angle);

                l = l * fadeIn + Interpolate(sample.Left, begin + period + index, fraction, end) * fadeOut;
                r = r * fadeIn + Interpolate(sample.Right, begin + period + index, fraction, end) * fadeOut;
            }

            _position += speed;
            if (_position >= period)
                _position -= period;

            _lowL += alpha * (l - _lowL);
            _lowR += alpha * (r - _lowR);

            _breathPhase += TwoPi * _breathRate / SampleRate;
            Wrap(ref _breathPhase);
            float breath = (float)(1.0 - depth * 0.5 * (1.0 + Math.Sin(_breathPhase)));

            left[n] = _lowL * breath;
            right[n] = _lowR * breath;
        }
    }

    private static float Interpolate(float[] data, int index, float fraction, int frames)
    {
        int next = Math.Min(index + 1, frames - 1);
        return data[index] + (data[next] - data[index]) * fraction;
    }
}
