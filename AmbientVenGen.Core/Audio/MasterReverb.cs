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

    /// <summary>Mixes the reverberated signal into the block. mix is 0..1.</summary>
    public void Process(float[] left, float[] right, int count, float mix)
    {
        if (mix <= 0.0001f)
            return;

        float dry = 1f - mix * 0.45f;
        float wet = mix * 0.9f;

        for (int i = 0; i < count; i++)
        {
            float input = (left[i] + right[i]) * 0.015f;
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
}
