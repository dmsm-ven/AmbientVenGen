namespace AmbientVenGen.Core.Audio;

/// <summary>
/// Compact Freeverb-style reverb (8 combs + 4 all-passes per channel) with a long tail,
/// tuned for ambient. Operates in place on a stereo block.
/// </summary>
internal sealed class MasterReverb
{
    private const float Feedback = 0.965f;
    private const float Damping = 0.45f;
    private const float AllPassFeedback = 0.5f;
    private const int StereoSpread = 23;

    private const float MaxShimmerGain = 0.55f;
    private const float ShimmerLowpass = 0.5f;

    private readonly OctaveShifter _shifter = new();
    private float _lastWet;
    private float _shimmerLow;
    private float _dcState;

    private static readonly int[] CombLengths = [1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617];
    private static readonly int[] AllPassLengths = [556, 441, 341, 225];

    private readonly Comb[] _combsL = new Comb[CombLengths.Length];
    private readonly Comb[] _combsR = new Comb[CombLengths.Length];
    private readonly AllPass[] _allPassL = new AllPass[AllPassLengths.Length];
    private readonly AllPass[] _allPassR = new AllPass[AllPassLengths.Length];

    public MasterReverb()
    {
        for (int i = 0; i < CombLengths.Length; i++)
        {
            _combsL[i] = new Comb(CombLengths[i]);
            _combsR[i] = new Comb(CombLengths[i] + StereoSpread);
        }

        for (int i = 0; i < AllPassLengths.Length; i++)
        {
            _allPassL[i] = new AllPass(AllPassLengths[i]);
            _allPassR[i] = new AllPass(AllPassLengths[i] + StereoSpread);
        }
    }

    /// <summary>
    /// Mixes the reverberated signal into the block. mix is 0..1. The reverb input is the sum of the layers'
    /// reverb sends (a layer with send 1 feeds it in full, with 0 it stays dry).
    /// </summary>
    public void Process(float[] left, float[] right, float[] sendLeft, float[] sendRight, int count, float mix, float shimmer = 0f)
    {
        if (mix <= 0.0001f)
            return;

        float dry = 1f - mix * 0.45f;
        float wet = mix * 0.9f;

        // Shimmer: the tail of the reverb is shifted an octave up and goes back to its input, so every round adds
        // one more octave of high harmonics. The loop gain stays well below 1 and the signal is soft-clipped.
        float shimmerGain = Math.Clamp(shimmer, 0f, 1f) * MaxShimmerGain;

        for (int i = 0; i < count; i++)
        {
            float input = (sendLeft[i] + sendRight[i]) * 0.015f;

            if (shimmerGain > 0f)
            {
                float shifted = _shifter.Process(_lastWet);

                // Keep the loop bright but not shrill (one-pole low-pass), and without DC.
                _shimmerLow += ShimmerLowpass * (shifted - _shimmerLow);
                float band = _shimmerLow - _dcState;
                _dcState += 0.0005f * (_shimmerLow - _dcState);

                input += MathF.Tanh(band) * shimmerGain * 0.03f;
            }
            float outL = 0f;
            float outR = 0f;

            for (int c = 0; c < _combsL.Length; c++)
            {
                outL += _combsL[c].Process(input);
                outR += _combsR[c].Process(input);
            }

            for (int a = 0; a < _allPassL.Length; a++)
            {
                outL = _allPassL[a].Process(outL);
                outR = _allPassR[a].Process(outR);
            }

            _lastWet = (outL + outR) * 0.5f;

            left[i] = left[i] * dry + outL * wet;
            right[i] = right[i] * dry + outR * wet;
        }
    }

    private sealed class Comb
    {
        private readonly float[] _buffer;
        private int _index;
        private float _filterStore;

        public Comb(int length) => _buffer = new float[length];

        public float Process(float input)
        {
            float output = _buffer[_index];
            _filterStore = output * (1f - Damping) + _filterStore * Damping;
            _buffer[_index] = input + _filterStore * Feedback;

            if (++_index >= _buffer.Length)
                _index = 0;

            return output;
        }
    }

    private sealed class AllPass
    {
        private readonly float[] _buffer;
        private int _index;

        public AllPass(int length) => _buffer = new float[length];

        public float Process(float input)
        {
            float buffered = _buffer[_index];
            float output = buffered - input;
            _buffer[_index] = input + buffered * AllPassFeedback;

            if (++_index >= _buffer.Length)
                _index = 0;

            return output;
        }
    }

    /// <summary>
    /// Real-time pitch shifter, one octave up: two read taps run through a short delay line twice as fast as it is
    /// written, and cross-fade with a Hann window. The small "grain" it gives is part of the shimmer sound.
    /// </summary>
    private sealed class OctaveShifter
    {
        private const int Window = 2048;

        private readonly float[] _buffer = new float[Window * 2];
        private int _write;
        private float _phase;

        public float Process(float input)
        {
            _buffer[_write] = input;

            float output = 0f;

            for (int tap = 0; tap < 2; tap++)
            {
                float phase = _phase + tap * 0.5f;
                if (phase >= 1f)
                    phase -= 1f;

                // The delay shrinks by one sample per sample, so the tap reads twice as fast = +12 semitones.
                float delay = Window * (1f - phase);
                float position = _write - delay;
                if (position < 0)
                    position += _buffer.Length;

                int index = (int)position;
                float fraction = position - index;
                float a = _buffer[index];
                float b = _buffer[(index + 1) % _buffer.Length];

                float gain = 0.5f - 0.5f * MathF.Cos(MathF.Tau * phase);
                output += (a + (b - a) * fraction) * gain;
            }

            _phase += 1f / Window;
            if (_phase >= 1f)
                _phase -= 1f;

            if (++_write >= _buffer.Length)
                _write = 0;

            return output;
        }
    }
}
