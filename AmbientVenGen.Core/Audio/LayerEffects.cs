namespace AmbientVenGen.Core.Audio;

/// <summary>
/// Per-layer effect chain applied after the layer level and fades: a resonant low-pass filter, then an echo (delay with
/// feedback). Keeps its own state, so it can be run block by block and survive project edits while playing.
/// </summary>
internal sealed class LayerEffects
{
    private const int SampleRate = ProjectMixer.SampleRate;
    private const double MaxDelaySeconds = 2.0;
    private const float DelayWet = 0.6f;
    private const float TailLevel = 1e-5f;

    private readonly Svf _filterL = new();
    private readonly Svf _filterR = new();
    private readonly float[] _delayL = new float[(int)(MaxDelaySeconds * SampleRate) + 4];
    private readonly float[] _delayR = new float[(int)(MaxDelaySeconds * SampleRate) + 4];
    private int _write;
    private double _delaySamples; // smoothed delay length, so moving the knob does not click
    private float _echoEnergy;

    /// <summary>True while an echo is still audible after the layer itself went silent.</summary>
    public bool HasTail => _echoEnergy > TailLevel;

    public void Process(float[] left, float[] right, int count, double cutoffHz, double resonance, double delaySeconds, double feedback)
    {
        bool filterOn = cutoffHz < 19000.0 || resonance > 0.001;
        bool delayOn = delaySeconds >= 0.01;

        if (filterOn)
        {
            _filterL.Configure(cutoffHz, resonance);
            _filterR.Configure(cutoffHz, resonance);

            for (int i = 0; i < count; i++)
            {
                left[i] = _filterL.Process(left[i]);
                right[i] = _filterR.Process(right[i]);
            }
        }

        if (!delayOn && _echoEnergy <= TailLevel)
        {
            _echoEnergy = 0f;
            return;
        }

        // Turning the delay off lets the echoes die out with the feedback unchanged (no sudden cut).
        double target = Math.Clamp(delaySeconds, 0.01, MaxDelaySeconds) * SampleRate;
        float fb = (float)Math.Clamp(feedback, 0.0, 0.92);
        int size = _delayL.Length;
        float energy = 0f;

        if (_delaySamples <= 0)
            _delaySamples = target;

        for (int i = 0; i < count; i++)
        {
            _delaySamples += (target - _delaySamples) * 0.0004;

            double readAt = _write - _delaySamples;
            if (readAt < 0)
                readAt += size;

            int index = (int)readAt;
            float frac = (float)(readAt - index);
            int next = index + 1 >= size ? 0 : index + 1;

            float echoL = _delayL[index] + (_delayL[next] - _delayL[index]) * frac;
            float echoR = _delayR[index] + (_delayR[next] - _delayR[index]) * frac;

            float inL = left[i];
            float inR = right[i];

            // Feed the echo back crosswise (L -> R -> L): a wider, ping-pong-like space.
            _delayL[_write] = inL + echoR * fb;
            _delayR[_write] = inR + echoL * fb;

            left[i] = inL + echoL * DelayWet;
            right[i] = inR + echoR * DelayWet;

            energy = Math.Max(energy, Math.Max(Math.Abs(echoL), Math.Abs(echoR)));

            if (++_write >= size)
                _write = 0;
        }

        _echoEnergy = energy;
    }

    /// <summary>Zero-delay-feedback state variable filter (low-pass output), stable up to Nyquist.</summary>
    private sealed class Svf
    {
        private float _ic1;
        private float _ic2;
        private float _a1, _a2, _a3;
        private float _makeup = 1f;
        private double _lastCutoff = -1;
        private double _lastResonance = -1;

        public void Configure(double cutoffHz, double resonance)
        {
            if (cutoffHz == _lastCutoff && resonance == _lastResonance)
                return;

            _lastCutoff = cutoffHz;
            _lastResonance = resonance;

            double fc = Math.Clamp(cutoffHz, 20.0, SampleRate * 0.45);
            double g = Math.Tan(Math.PI * fc / SampleRate);
            double k = 1.414 - Math.Clamp(resonance, 0.0, 1.0) * 1.35; // 1.414 = flat, 0.064 = strong peak
            double a1 = 1.0 / (1.0 + g * (g + k));

            _a1 = (float)a1;
            _a2 = (float)(g * a1);
            _a3 = (float)(g * g * a1);
            _makeup = (float)(1.0 / (1.0 + resonance * 1.5)); // keeps the peak from jumping out in volume
        }

        public float Process(float input)
        {
            float v3 = input - _ic2;
            float v1 = _a1 * _ic1 + _a2 * v3;
            float v2 = _ic2 + _a2 * _ic1 + _a3 * v3;
            _ic1 = 2f * v1 - _ic1;
            _ic2 = 2f * v2 - _ic2;
            return v2 * _makeup;
        }
    }
}
