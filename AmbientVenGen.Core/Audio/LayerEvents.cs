using AmbientVenGen.Core.Composition;

namespace AmbientVenGen.Core.Audio;

/// <summary>
/// The one-shot events of a layer (see <see cref="EventSpec"/>), run after the layer's level, filter and echo.
/// Echo throw: the layer feeds a dedicated echo for a moment, then (optionally) goes silent so only the echo rings.
/// Stutter: the last slice of the layer's sound is grabbed and repeated, the layer itself is silent meanwhile.
/// Keeps its own state, so it runs block by block; a jump of the playback position starts it clean.
/// </summary>
internal sealed class LayerEvents
{
    private const int SampleRate = ProjectMixer.SampleRate;
    private const float EchoWet = 0.8f;
    private const float TailLevel = 1e-4f;

    // what the layer sounded like just before (for the stutter to grab): 1 s of the slice at most
    private const int RingSize = SampleRate * 2;
    private readonly float[] _ringL = new float[RingSize];
    private readonly float[] _ringR = new float[RingSize];
    private int _ringWrite;
    private int _recorded;

    // echo throw
    private readonly float[] _echoL = new float[(int)(EventSpec.MaxDelay * SampleRate) + 8];
    private readonly float[] _echoR = new float[(int)(EventSpec.MaxDelay * SampleRate) + 8];
    private int _echoWrite;
    private int _echoDelay = SampleRate / 3;
    private float _echoFeedback;
    private bool _echoOn;
    private bool _muteDry;
    private int _hold;
    private int _tail;
    private float _feed;
    private float _dryGain = 1f;

    // stutter
    private readonly float[] _stutterL = new float[(int)(EventSpec.MaxSlice * SampleRate) + 8];
    private readonly float[] _stutterR = new float[(int)(EventSpec.MaxSlice * SampleRate) + 8];
    private int _stutterLength;
    private int _stutterPosition;
    private int _stutterRepeat;
    private int _stutterRepeats;
    private float _stutterDecay;
    private float _stutterGain;
    private bool _stutterOn;
    private float _stutterMix;

    // filter sweep: a low-pass (state-variable filter) whose cutoff falls and rises once
    private bool _sweepOn;
    private int _sweepPosition;
    private int _sweepDown;
    private int _sweepUp;
    private double _sweepLow;
    private float _s1L, _s2L, _s1R, _s2R;

    // freeze: a captured slice looped with a crossfade, held and released
    private readonly float[] _frzL = new float[SampleRate + 8];
    private readonly float[] _frzR = new float[SampleRate + 8];
    private bool _frzOn;
    private int _frzLength;
    private int _frzFade;
    private int _frzPos;
    private int _frzHold;
    private float _frzGain;
    private float _frzMix;
    private float _frzAttack;
    private float _frzRelease;

    private double _expected = double.NaN;
    private int _lastPlay = -1;
    private readonly List<(int Offset, EventSpec Event)> _pending = [];

    /// <summary>True while an event still sounds (the layer must keep being processed even when it is silent).</summary>
    public bool IsActive => _echoOn || _frzOn || _sweepOn || _stutterOn || _stutterMix > 1e-4f;

    /// <param name="centerTime">Seconds inside the current play at the middle of the block (negative before the layer starts; below -1e8 = no events now).</param>
    public void Process(float[] left, float[] right, int count, double centerTime, int play, IReadOnlyList<EventSpec>? events)
    {
        bool hasEvents = events is { Count: > 0 };

        if (!hasEvents && !IsActive)
        {
            _expected = double.NaN;
            return;
        }

        bool valid = centerTime > -1e8;
        double start = centerTime - count * 0.5 / SampleRate;

        if (valid)
        {
            // A jump of the position (a seek) cleans the state; a new play carries on (an echo rings over the change).
            if (play != _lastPlay)
            {
                if (start > 0.1)
                    Reset();

                _lastPlay = play;
            }
            else if (double.IsNaN(_expected) ? start > 0.1 : Math.Abs(start - _expected) > 0.06)
            {
                Reset();
            }

            _expected = start + count / (double)SampleRate;
        }
        else
        {
            _expected = double.NaN;
        }

        _pending.Clear();

        if (valid && hasEvents)
        {
            long blockStart = (long)Math.Round(start * SampleRate);

            foreach (EventSpec item in events!)
            {
                if (item.Muted)
                    continue;

                long at = (long)Math.Round(item.StartSeconds * SampleRate);

                if (at >= blockStart && at < blockStart + count)
                    _pending.Add(((int)(at - blockStart), item));
            }

            _pending.Sort((a, b) => a.Offset.CompareTo(b.Offset));
        }

        int next = 0;
        float energy = 0f;
        int echoSize = _echoL.Length;

        for (int i = 0; i < count; i++)
        {
            while (next < _pending.Count && _pending[next].Offset <= i)
                Fire(_pending[next++].Event);

            float inL = left[i];
            float inR = right[i];

            // echo throw
            float echoOutL = 0f;
            float echoOutR = 0f;

            if (_echoOn)
            {
                int read = _echoWrite - _echoDelay;
                if (read < 0)
                    read += echoSize;

                float echoL = _echoL[read];
                float echoR = _echoR[read];

                _feed += ((_hold > 0 ? 1f : 0f) - _feed) * 0.003f;
                _echoL[_echoWrite] = inL * _feed + echoR * _echoFeedback;
                _echoR[_echoWrite] = inR * _feed + echoL * _echoFeedback;

                if (++_echoWrite >= echoSize)
                    _echoWrite = 0;

                echoOutL = echoL * EchoWet;
                echoOutR = echoR * EchoWet;
                energy = Math.Max(energy, Math.Max(Math.Abs(echoL), Math.Abs(echoR)));

                if (_hold > 0)
                    _hold--;
                else if (_tail > 0)
                    _tail--;
            }

            bool muteNow = _echoOn && _muteDry && _hold <= 0 && _tail > 0;
            _dryGain += ((muteNow ? 0f : 1f) - _dryGain) * 0.003f;

            // stutter: the grabbed slice instead of the layer
            float stutterL = 0f;
            float stutterR = 0f;

            if (_stutterOn)
            {
                stutterL = _stutterL[_stutterPosition] * _stutterGain;
                stutterR = _stutterR[_stutterPosition] * _stutterGain;

                if (++_stutterPosition >= _stutterLength)
                {
                    _stutterPosition = 0;

                    if (++_stutterRepeat >= _stutterRepeats)
                        _stutterOn = false;
                    else
                        _stutterGain *= 1f - _stutterDecay;
                }
            }

            _stutterMix += ((_stutterOn ? 1f : 0f) - _stutterMix) * 0.01f;

            // what the layer sounded like (for the next stutter)
            _ringL[_ringWrite] = inL;
            _ringR[_ringWrite] = inR;

            if (++_ringWrite >= RingSize)
                _ringWrite = 0;

            if (_recorded < RingSize)
                _recorded++;

            float dry = _dryGain * (1f - _stutterMix);
            float mixL = inL * dry + stutterL * _stutterMix;
            float mixR = inR * dry + stutterR * _stutterMix;

            if (_frzOn)
                Freeze(ref mixL, ref mixR);

            if (_sweepOn)
                SweepFilter(ref mixL, ref mixR);

            left[i] = mixL + echoOutL;
            right[i] = mixR + echoOutR;
        }

        if (_echoOn && _hold <= 0 && _tail <= 0 && energy < TailLevel)
        {
            _echoOn = false;
            Array.Clear(_echoL);
            Array.Clear(_echoR);
        }
    }

    private void Freeze(ref float l, ref float r)
    {
        float fl = _frzL[_frzPos];
        float fr = _frzR[_frzPos];
        int start = _frzLength - _frzFade;

        if (_frzPos >= start)
        {
            // the end of the slice fades out while its beginning fades in (equal power): no click at the loop point
            double k = (_frzPos - start) / (double)_frzFade * Math.PI / 2;
            float outGain = (float)Math.Cos(k);
            float inGain = (float)Math.Sin(k);
            int other = _frzPos - start;
            fl = fl * outGain + _frzL[other] * inGain;
            fr = fr * outGain + _frzR[other] * inGain;
        }

        if (++_frzPos >= _frzLength)
            _frzPos = _frzFade;

        bool holding = _frzHold > 0;
        if (holding)
            _frzHold--;

        _frzGain += ((holding ? 1f : 0f) - _frzGain) * (holding ? _frzAttack : _frzRelease);
        float m = _frzGain * _frzMix;
        l = l * (1f - m) + fl * m;
        r = r * (1f - m) + fr * m;

        if (!holding && _frzGain < 1e-3f)
            _frzOn = false;
    }

    // Cutoff in log scale from 18 kHz down to _sweepLow (smoothstep), then back up.
    private void SweepFilter(ref float l, ref float r)
    {
        double t = _sweepPosition < _sweepDown
            ? _sweepPosition / (double)_sweepDown
            : 1.0 - (_sweepPosition - _sweepDown) / (double)Math.Max(1, _sweepUp);

        t = Math.Clamp(t, 0.0, 1.0);
        double smooth = t * t * (3 - 2 * t);
        double cutoff = 18000.0 * Math.Pow(_sweepLow / 18000.0, smooth);

        // TPT state-variable low-pass, Q about 1.2
        double g = Math.Tan(Math.PI * Math.Min(cutoff, SampleRate * 0.45) / SampleRate);
        double k = 1.0 / 1.2;
        double a1 = 1.0 / (1.0 + g * (g + k));
        double a2 = g * a1;
        double a3 = g * a2;

        float outL = Svf(l, ref _s1L, ref _s2L, a1, a2, a3);
        float outR = Svf(r, ref _s1R, ref _s2R, a1, a2, a3);

        // fully open at the very start and end: the filter fades in and out with no jump
        float blend = (float)Math.Min(1.0, smooth * 40.0);
        l += (outL - l) * blend;
        r += (outR - r) * blend;

        if (++_sweepPosition >= _sweepDown + _sweepUp)
        {
            _sweepOn = false;
            _s1L = _s2L = _s1R = _s2R = 0f;
        }
    }

    private static float Svf(float input, ref float ic1, ref float ic2, double a1, double a2, double a3)
    {
        double v3 = input - ic2;
        double v1 = a1 * ic1 + a2 * v3;
        double v2 = ic2 + a2 * ic1 + a3 * v3;
        ic1 = (float)(2 * v1 - ic1);
        ic2 = (float)(2 * v2 - ic2);
        return (float)v2;
    }

    private void Fire(EventSpec item)
    {
        if (item.Type == EventType.Freeze)
        {
            int length = Math.Min((int)(Math.Clamp(item.Time, 0.1, 1.0) * SampleRate), _recorded);
            length = Math.Min(length, _frzL.Length - 4);

            if (length < 1024)
                return;

            int from = _ringWrite - length;
            if (from < 0)
                from += RingSize;

            for (int i = 0; i < length; i++)
            {
                int index = from + i >= RingSize ? from + i - RingSize : from + i;
                _frzL[i] = _ringL[index];
                _frzR[i] = _ringR[index];
            }

            _frzLength = length;
            _frzFade = length / 3;
            _frzPos = 0;
            _frzHold = Math.Max(1, (int)(Math.Clamp(item.Length, 0.1, EventSpec.MaxSweep) * SampleRate));
            _frzGain = 0f;
            _frzMix = (float)Math.Clamp(item.Amount, 0.0, 1.0);
            _frzAttack = (float)(1 - Math.Exp(-1.0 / (0.15 * SampleRate)));
            double release = Math.Max(0.05, Math.Clamp(item.Tail, 0.0, EventSpec.MaxTail) / 3.0);
            _frzRelease = (float)(1 - Math.Exp(-1.0 / (release * SampleRate)));
            _frzOn = true;
            return;
        }

        if (item.Type == EventType.FilterSweep)
        {
            // depth 0 = no effect, 1 = down to about 100 Hz
            _sweepLow = 18000.0 * Math.Pow(100.0 / 18000.0, Math.Clamp(item.Amount, 0.0, 1.0));
            _sweepDown = Math.Max(1, (int)(Math.Clamp(item.Length, 0.05, EventSpec.MaxSweep) * SampleRate));
            _sweepUp = Math.Max(1, (int)(Math.Clamp(item.Tail, 0.0, EventSpec.MaxTail) * SampleRate));
            _sweepPosition = 0;
            _s1L = _s2L = _s1R = _s2R = 0f;
            _sweepOn = true;
            return;
        }

        if (item.Type == EventType.EchoThrow)
        {
            // a new throw starts a new echo
            Array.Clear(_echoL);
            Array.Clear(_echoR);
            _echoWrite = 0;
            _echoDelay = Math.Clamp((int)(item.Time * SampleRate), (int)(EventSpec.MinDelay * SampleRate), _echoL.Length - 4);
            _echoFeedback = (float)Math.Clamp(item.Amount, 0.0, EventSpec.MaxFeedback);
            _hold = Math.Max(1, (int)(Math.Clamp(item.Length, EventSpec.MinSlice, EventSpec.MaxEchoHold) * SampleRate));
            _tail = (int)(Math.Clamp(item.Tail, 0.0, EventSpec.MaxTail) * SampleRate);
            _muteDry = item.Tail > 0.001;
            _feed = 0f;
            _echoOn = true;
            return;
        }

        // Stutter: grab the last slice (its edges get a tiny fade: no clicks at the joins)
        int length = (int)(Math.Clamp(item.Length, EventSpec.MinSlice, EventSpec.MaxSlice) * SampleRate);
        length = Math.Min(length, _recorded);

        if (length < 64)
            return;

        int from = _ringWrite - length;
        if (from < 0)
            from += RingSize;

        for (int i = 0; i < length; i++)
        {
            int index = from + i >= RingSize ? from + i - RingSize : from + i;
            _stutterL[i] = _ringL[index];
            _stutterR[i] = _ringR[index];
        }

        int edge = Math.Min(72, length / 4);

        for (int i = 0; i < edge; i++)
        {
            float g = (i + 1f) / edge;
            _stutterL[i] *= g;
            _stutterR[i] *= g;
            _stutterL[length - 1 - i] *= g;
            _stutterR[length - 1 - i] *= g;
        }

        _stutterLength = length;
        _stutterPosition = 0;
        _stutterRepeat = 0;
        _stutterRepeats = Math.Clamp(item.Count, EventSpec.MinRepeats, EventSpec.MaxRepeats);
        _stutterDecay = (float)Math.Clamp(item.Amount, 0.0, 0.9);
        _stutterGain = 1f;
        _stutterOn = true;
    }

    private void Reset()
    {
        _echoOn = false;
        _hold = 0;
        _tail = 0;
        _feed = 0f;
        _dryGain = 1f;
        _stutterOn = false;
        _stutterMix = 0f;
        _frzOn = false;
        _sweepOn = false;
        _s1L = _s2L = _s1R = _s2R = 0f;
        _recorded = 0;
        _ringWrite = 0;
        Array.Clear(_echoL);
        Array.Clear(_echoR);
    }
}
