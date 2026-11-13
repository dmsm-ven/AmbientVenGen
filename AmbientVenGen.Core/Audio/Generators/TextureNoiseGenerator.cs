using AmbientVenGen.Core.Composition;

namespace AmbientVenGen.Core.Audio;

/// <summary>
/// Noise textures made of events (clicks, drops, dropouts) and slow movement (gusts, tuning drift) on top of filtered
/// noise: Crackle, Vinyl, RadioStatic, Wind, Rain, Ocean, ElectricalHum, TapeHiss, SpaceRumble, Geiger, IceSinging, HighVoltage, EnergyShield, CrtWhine, PlasmaArc, GasBurner, Gong and WaterDrop. Every channel has its own random stream, so the left and the right
/// side are different (wide). Brightness sets the tone, Modulation the density or the amount of movement.
/// Everything is random-driven from the seed, so the sound is the same every time and can have any length.
/// </summary>
internal sealed class TextureNoiseGenerator : LayerGenerator
{
    private readonly Channel _left;
    private readonly Channel _right;

    public TextureNoiseGenerator(int seed, NoiseType type)
    {
        var random = new Random(seed);
        _left = CreateChannel(type, (uint)random.Next(1, int.MaxValue));
        _right = CreateChannel(type, (uint)random.Next(1, int.MaxValue));
    }

    public override void Render(in BlockParams p, Span<float> left, Span<float> right)
    {
        float brightness = Math.Clamp(p.Brightness, 0f, 1f);
        float modulation = Math.Clamp(p.Modulation, 0f, 1f);

        for (int n = 0; n < left.Length; n++)
        {
            left[n] = SoftLimit(_left.Next(brightness, modulation));
            right[n] = SoftLimit(_right.Next(brightness, modulation));
        }
    }

    // Keeps rare loud events inside +-1 without touching the quiet part much.
    private static float SoftLimit(float x) => MathF.Tanh(x);

    private static Channel CreateChannel(NoiseType type, uint seed) => type switch
    {
        NoiseType.Vinyl => new VinylChannel(seed),
        NoiseType.RadioStatic => new RadioChannel(seed),
        NoiseType.Wind => new WindChannel(seed),
        NoiseType.Rain => new RainChannel(seed),
        NoiseType.Ocean => new OceanChannel(seed),
        NoiseType.ElectricalHum => new HumChannel(seed),
        NoiseType.TapeHiss => new TapeChannel(seed),
        NoiseType.SpaceRumble => new RumbleChannel(seed),
        NoiseType.Geiger => new GeigerChannel(seed),
        NoiseType.IceSinging => new IceChannel(seed),
        NoiseType.HighVoltage => new HighVoltageChannel(seed),
        NoiseType.EnergyShield => new ShieldChannel(seed),
        NoiseType.CrtWhine => new WhineChannel(seed),
        NoiseType.PlasmaArc => new ArcChannel(seed),
        NoiseType.GasBurner => new GasBurnerChannel(seed),
        NoiseType.Gong => new GongChannel(seed),
        NoiseType.WaterDrop => new WaterDropChannel(seed),
        _ => new CrackleChannel(seed)
    };

    // ---------------------------------------------------------------- building blocks

    private const float Sr = SampleRate;

    private struct Rng(uint seed)
    {
        private uint _state = seed | 1u;

        /// <summary>0..1</summary>
        public float Next01()
        {
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;
            return (_state & 0xFFFFFF) / 16777216f;
        }

        /// <summary>-1..1</summary>
        public float Signed() => Next01() * 2f - 1f;

        /// <summary>True with the probability "perSecond / sample rate": a Poisson stream of events.</summary>
        public bool Event(float perSecond) => Next01() < perSecond / Sr;
    }

    /// <summary>Zavalishin's state-variable filter: stable while the frequency moves.</summary>
    private struct Svf
    {
        private float _k, _a1, _a2, _a3, _ic1, _ic2;

        public Svf(float hz, float q) : this() => Set(hz, q);

        public void Set(float hz, float q)
        {
            float g = MathF.Tan(MathF.PI * Math.Clamp(hz, 20f, Sr * 0.45f) / Sr);
            _k = 1f / Math.Max(q, 0.1f);
            _a1 = 1f / (1f + g * (g + _k));
            _a2 = g * _a1;
            _a3 = g * _a2;
        }

        /// <summary>Band is scaled to a peak gain of 1.</summary>
        public void Process(float x, out float low, out float band, out float high)
        {
            float v3 = x - _ic2;
            float v1 = _a1 * _ic1 + _a2 * v3;
            float v2 = _ic2 + _a2 * _ic1 + _a3 * v3;
            _ic1 = 2f * v1 - _ic1;
            _ic2 = 2f * v2 - _ic2;

            low = v2;
            band = _k * v1;
            high = x - _k * v1 - v2;
        }
    }

    /// <summary>A short noise burst through a band-pass: one click, pop or drop (a new one retunes it).</summary>
    private struct Clicker
    {
        private Svf _filter;
        private float _envelope;
        private float _decay;

        public void Trigger(float amplitude, float hz, float q, float decaySeconds)
        {
            _envelope = amplitude;
            _decay = MathF.Exp(-1f / (Math.Max(decaySeconds, 1e-4f) * Sr));
            _filter.Set(hz, q);
        }

        public float Next(float noise)
        {
            if (_envelope < 1e-4f)
                return 0f;

            _envelope *= _decay;
            _filter.Process(noise * _envelope, out _, out float band, out _);
            return band;
        }
    }

    /// <summary>A tone that falls quickly from a high to a low pitch (a "laser shot", a crack running through ice).</summary>
    private struct Chirp
    {
        private float _envelope, _decay, _amplitude, _low, _span, _sweep, _sweepDecay;
        private float _phase;

        public void Trigger(float amplitude, float fromHz, float toHz, float sweepSeconds, float ringSeconds)
        {
            _amplitude = amplitude;
            _low = toHz;
            _span = fromHz - toHz;
            _sweep = 1f;
            _sweepDecay = MathF.Exp(-1f / (Math.Max(sweepSeconds, 1e-3f) * Sr));
            _envelope = 1f;
            _decay = MathF.Exp(-1f / (Math.Max(ringSeconds, 1e-3f) * Sr));
            _attack = 0f;
        }

        private float _attack;

        public float Next()
        {
            if (_envelope < 1e-4f)
                return 0f;

            float hz = _low + _span * _sweep;
            _sweep *= _sweepDecay;
            _envelope *= _decay;
            _attack = Math.Min(1f, _attack + 1f / (0.001f * Sr));

            _phase += hz / Sr;
            if (_phase >= 1f)
                _phase -= 1f;

            float x = MathF.Sin(_phase * MathF.Tau) + 0.2f * MathF.Sin(_phase * 2f * MathF.Tau);
            return x * _envelope * _attack * _amplitude;
        }
    }

    /// <summary>A slow random value 0..1: smooth steps between random targets.</summary>
    private struct Wander
    {
        private Rng _rng;
        private float _from, _to, _position;

        public Wander(uint seed)
        {
            _rng = new Rng(seed);
            _from = _rng.Next01();
            _to = _rng.Next01();
            _position = _rng.Next01();
        }

        public float Next(float periodSeconds)
        {
            _position += 1f / (Math.Max(periodSeconds, 0.05f) * Sr);

            if (_position >= 1f)
            {
                _position -= 1f;
                _from = _to;
                _to = _rng.Next01();
            }

            float s = _position * _position * (3f - 2f * _position);
            return _from + (_to - _from) * s;
        }
    }

    /// <summary>Slowly evens out the loudness of a filtered noise, so moving the filter does not change the volume.</summary>
    private struct Leveler
    {
        private float _power;

        public float Next(float x)
        {
            _power += (x * x - _power) * 2e-5f;
            return x / MathF.Sqrt(_power + 1e-7f);
        }
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private abstract class Channel
    {
        public abstract float Next(float brightness, float modulation);
    }

    // ---------------------------------------------------------------- textures

    /// <summary>Fire: random snaps and pops over a low flickering rumble.</summary>
    private sealed class CrackleChannel : Channel
    {
        private Rng _rng;
        private Clicker _snap, _pop;
        private Wander _density, _flicker;
        private Svf _bed = new(160f, 0.7f);

        public CrackleChannel(uint seed)
        {
            _rng = new Rng(seed);
            _density = new Wander(seed ^ 0x9E3779B9u);
            _flicker = new Wander(seed ^ 0x85EBCA6Bu);
        }

        public override float Next(float brightness, float modulation)
        {
            // The crackling comes in waves: the density wanders between a third and 1.7 times the set rate.
            float density = 0.35f + 1.35f * _density.Next(5f);
            float rate = (6f + 40f * modulation) * density;

            if (_rng.Event(rate))
            {
                float loudness = _rng.Next01();
                float hz = (900f + 4500f * brightness) * (0.6f + 0.8f * _rng.Next01());
                _snap.Trigger(0.05f + 1.2f * loudness * loudness * loudness, hz, 1.5f, 0.0015f + 0.004f * _rng.Next01());
            }

            // Rare bigger, lower pops
            if (_rng.Event(rate * 0.06f))
                _pop.Trigger(0.5f + 0.5f * _rng.Next01(), 350f + 900f * brightness, 1.2f, 0.012f);

            _bed.Process(_rng.Signed(), out float rumble, out _, out _);
            float bed = rumble * 2.2f * (0.5f + 0.5f * _flicker.Next(1.3f)) * (0.6f + 0.4f * density);

            return (_snap.Next(_rng.Signed()) + _pop.Next(_rng.Signed())) * 2f + bed;
        }
    }

    /// <summary>Old record: fine surface hiss, a stream of tiny ticks and a few bigger pops.</summary>
    private sealed class VinylChannel : Channel
    {
        private Rng _rng;
        private Clicker _tick, _dust, _pop;
        private Svf _hiss = new(1500f, 0.7f);

        public VinylChannel(uint seed) => _rng = new Rng(seed);

        public override float Next(float brightness, float modulation)
        {
            if (_rng.Event(2f + 10f * modulation))
            {
                float loudness = _rng.Next01();
                _tick.Trigger(0.15f + 0.85f * loudness * loudness, (2500f + 5000f * brightness) * (0.7f + 0.6f * _rng.Next01()), 0.9f,
                    0.0004f + 0.0008f * _rng.Next01());
            }

            // Fine dust: many quiet ticks
            if (_rng.Event(30f + 60f * modulation))
            {
                float loudness = _rng.Next01();
                _dust.Trigger(0.04f + 0.2f * loudness * loudness, 3000f + 5000f * _rng.Next01(), 1.2f, 0.0003f);
            }

            // A scratch: a lower, longer pop about every two seconds
            if (_rng.Event(0.3f + 0.6f * modulation))
                _pop.Trigger(0.6f + 0.4f * _rng.Next01(), 600f + 900f * _rng.Next01(), 1f, 0.006f);

            _hiss.Process(_rng.Signed(), out _, out _, out float high);
            float hiss = high * 0.07f * (0.4f + brightness);

            return (_tick.Next(_rng.Signed()) + _pop.Next(_rng.Signed())) * 2.2f + _dust.Next(_rng.Signed()) * 2f + hiss;
        }
    }

    /// <summary>Radio between stations: band-limited static, drifting tuning, dropouts, a faint whistle.</summary>
    private sealed class RadioChannel : Channel
    {
        private Rng _rng;
        private Svf _band = new(1500f, 1.4f);
        private Clicker _pops;
        private Wander _tuning, _whistleGate, _whistlePitch;
        private double _whistlePhase;
        private float _gain = 1f;
        private float _targetGain = 1f;
        private int _dropoutLeft;
        private int _tick;
        private float _center = 1500f;

        public RadioChannel(uint seed)
        {
            _rng = new Rng(seed);
            _tuning = new Wander(seed ^ 0x9E3779B9u);
            _whistleGate = new Wander(seed ^ 0x85EBCA6Bu);
            _whistlePitch = new Wander(seed ^ 0xC2B2AE35u);
        }

        public override float Next(float brightness, float modulation)
        {
            float tuning = _tuning.Next(7f);

            // The tuning wanders up to +-2 octaves (with Modulation) around the base frequency.
            if ((_tick++ & 15) == 0)
            {
                _center = (400f + 3600f * brightness) * MathF.Pow(2f, (tuning - 0.5f) * 4f * modulation);
                _band.Set(_center, 1.4f);
            }

            // Dropouts: the signal sinks for a moment and comes back.
            if (_dropoutLeft > 0)
            {
                if (--_dropoutLeft == 0)
                    _targetGain = 1f;
            }
            else if (_rng.Event(0.05f + 0.3f * modulation))
            {
                _dropoutLeft = (int)((0.05f + 0.35f * _rng.Next01()) * Sr);
                _targetGain = 0.08f;
            }

            float seconds = _targetGain < _gain ? 0.004f : 0.015f;
            _gain += (_targetGain - _gain) * (1f - MathF.Exp(-1f / (seconds * Sr)));

            float white = _rng.Signed();
            _band.Process(white, out _, out float band, out _);

            if (_rng.Event(5f + 10f * modulation))
                _pops.Trigger(0.25f, 1500f + 3000f * _rng.Next01(), 1f, 0.002f);

            // Heterodyne whistle: a thin tone that appears now and then and slides slowly.
            float gate = Math.Clamp((_whistleGate.Next(8f) - 0.7f) * 4f, 0f, 1f);
            float whistleHz = 600f + 2400f * _whistlePitch.Next(3f);
            _whistlePhase += TwoPi * whistleHz / SampleRate;
            Wrap(ref _whistlePhase);
            float whistle = (float)Math.Sin(_whistlePhase) * gate * 0.05f * modulation;

            return (band * 2.2f + white * 0.04f + _pops.Next(_rng.Signed()) * 2f) * _gain + whistle * _gain;
        }
    }

    /// <summary>Wind: a resonant band of pink noise whose pitch and loudness swell in slow gusts, over a low rumble.</summary>
    private sealed class WindChannel : Channel
    {
        private Rng _rng;
        private readonly NoiseColor _pink = new(NoiseType.Pink);
        private Svf _band = new(500f, 2.5f);
        private Svf _rumble = new(120f, 0.7f);
        private Wander _gustSlow, _gustFast;
        private Leveler _leveler;
        private int _tick;

        public WindChannel(uint seed)
        {
            _rng = new Rng(seed);
            _gustSlow = new Wander(seed ^ 0x9E3779B9u);
            _gustFast = new Wander(seed ^ 0x85EBCA6Bu);
        }

        public override float Next(float brightness, float modulation)
        {
            float gust = 0.65f * _gustSlow.Next(6f) + 0.35f * _gustFast.Next(2.3f);

            if ((_tick++ & 15) == 0)
                _band.Set((180f + 900f * brightness) * (0.6f + 1.1f * gust), 2.5f);

            float depth = 0.3f + 0.7f * modulation;
            float gain = (1f - depth) + depth * 1.3f * MathF.Pow(gust, 1.5f);

            _band.Process(_pink.Next(_rng.Signed()), out _, out float band, out _);
            _rumble.Process(_rng.Signed(), out float low, out _, out _);

            return _leveler.Next(band) * 0.3f * gain + low * 1.5f * (0.3f + 0.7f * gust);
        }
    }

    /// <summary>Rain: a dense fine hiss with many separate drops.</summary>
    private sealed class RainChannel : Channel
    {
        private Rng _rng;
        private readonly NoiseColor _pink = new(NoiseType.Pink);
        private Svf _highPass = new(400f, 0.7f);
        private Svf _lowPass = new(6000f, 0.7f);
        private Clicker _drop0, _drop1, _drop2;
        private int _next;
        private Wander _intensity;
        private Leveler _leveler;
        private int _tick;

        public RainChannel(uint seed)
        {
            _rng = new Rng(seed);
            _intensity = new Wander(seed ^ 0x9E3779B9u);
        }

        public override float Next(float brightness, float modulation)
        {
            float intensity = 0.75f + 0.5f * _intensity.Next(8f);

            if ((_tick++ & 63) == 0)
                _lowPass.Set(3500f + 6500f * brightness, 0.7f);

            if (_rng.Event((30f + 220f * modulation) * intensity))
            {
                float loudness = _rng.Next01();
                float hz = 2000f + 6000f * _rng.Next01() * (0.4f + 0.6f * brightness);
                float amplitude = 0.15f + 0.85f * loudness * loudness;
                float decay = 0.0008f + 0.002f * _rng.Next01();

                // Three voices in turn, so that close drops do not cut each other off
                switch (_next++ % 3)
                {
                    case 0: _drop0.Trigger(amplitude, hz, 2f, decay); break;
                    case 1: _drop1.Trigger(amplitude, hz, 2f, decay); break;
                    default: _drop2.Trigger(amplitude, hz, 2f, decay); break;
                }
            }

            _highPass.Process(_pink.Next(_rng.Signed()), out _, out _, out float high);
            _lowPass.Process(high, out float bed, out _, out _);

            float drops = _drop0.Next(_rng.Signed()) + _drop1.Next(_rng.Signed()) + _drop2.Next(_rng.Signed());
            return _leveler.Next(bed) * 0.16f * intensity + drops * 1.8f;
        }
    }

    /// <summary>Sea: brown noise swelling in waves (period 6-12 s, two waves of different length), foam hisses on the crests.</summary>
    private sealed class OceanChannel : Channel
    {
        private Rng _rng;
        private readonly NoiseColor _brown = new(NoiseType.Brown);
        private Svf _body = new(500f, 0.7f);
        private Svf _foam = new(1500f, 0.7f);
        private Svf _foamLow = new(6000f, 0.7f);
        private Wander _periodA, _periodB;
        private float _phaseA, _phaseB;
        private Leveler _leveler;
        private int _tick;

        public OceanChannel(uint seed)
        {
            _rng = new Rng(seed);
            _periodA = new Wander(seed ^ 0x9E3779B9u);
            _periodB = new Wander(seed ^ 0x85EBCA6Bu);
            _phaseA = _rng.Next01();
            _phaseB = _rng.Next01();
        }

        private static float Swell(float phase)
        {
            float w = 0.5f - 0.5f * MathF.Cos(phase * MathF.Tau);
            return w * MathF.Sqrt(w);   // a longer calm, a sharper crest
        }

        public override float Next(float brightness, float modulation)
        {
            _phaseA = (_phaseA + 1f / ((6f + 6f * _periodA.Next(20f)) * Sr)) % 1f;
            _phaseB = (_phaseB + 1f / ((4.5f + 4f * _periodB.Next(15f)) * Sr)) % 1f;

            float wave = 0.7f * Swell(_phaseA) + 0.3f * Swell(_phaseB);

            if ((_tick++ & 31) == 0)
            {
                _body.Set(250f + 1000f * brightness, 0.7f);
                _foamLow.Set(3000f + 7000f * brightness, 0.7f);
            }

            float depth = 0.35f + 0.65f * modulation;
            float gain = (1f - depth) + depth * wave;

            _body.Process(_brown.Next(_rng.Signed()), out float lowBody, out _, out _);
            float body = _leveler.Next(lowBody) * 0.33f * gain;

            // Foam: bright hiss that comes with the top of the wave.
            float crest = MathF.Pow(wave, 3f);
            _foam.Process(_rng.Signed(), out _, out _, out float high);
            _foamLow.Process(high, out float foam, out _, out _);

            return body + foam * 0.5f * crest * (0.4f + 0.6f * brightness);
        }
    }

    /// <summary>Mains hum: 50 Hz and its harmonics, a slow drift of the frequency and a light buzz.</summary>
    private sealed class HumChannel : Channel
    {
        private const int Harmonics = 10;
        private Rng _rng;
        private double _phase;
        private Wander _drift, _flicker;
        private Svf _buzz = new(320f, 6f);

        public HumChannel(uint seed)
        {
            _rng = new Rng(seed);
            _drift = new Wander(seed ^ 0x9E3779B9u);
            _flicker = new Wander(seed ^ 0x85EBCA6Bu);
            _phase = _rng.Next01();
        }

        public override float Next(float brightness, float modulation)
        {
            // +-0.2 % of 50 Hz: slow beating against other hum layers
            double hz = 50.0 * (1.0 + 0.004 * (_drift.Next(25f) - 0.5));
            _phase = (_phase + hz / Sr) % 1.0;

            // Brightness: from the fundamental with a soft second harmonic to a rich, harsh hum.
            float rolloff = 2.2f - 1.5f * brightness;
            float sum = 0f, norm = 0f;
            for (int k = 1; k <= Harmonics; k++)
            {
                float amplitude = 1f / MathF.Pow(k, rolloff);
                sum += amplitude * (float)Math.Sin(_phase * k * Math.PI * 2.0);
                norm += amplitude;
            }

            // Buzz: a narrow band of noise around 300 Hz, flickering
            _buzz.Process(_rng.Signed(), out _, out float band, out _);
            float buzz = band * 0.5f * modulation * (0.4f + 0.6f * _flicker.Next(1.5f));

            return sum / norm * 0.35f + buzz;
        }
    }

    /// <summary>Tape: hiss (high-passed noise) whose level and tone float slowly, with a faint flutter.</summary>
    private sealed class TapeChannel : Channel
    {
        private Rng _rng;
        private Svf _hiss = new(3000f, 0.7f);
        private Svf _bed = new(700f, 0.7f);
        private Wander _wow;
        private float _flutterPhase;
        private int _tick;

        public TapeChannel(uint seed)
        {
            _rng = new Rng(seed);
            _wow = new Wander(seed ^ 0x9E3779B9u);
            _flutterPhase = _rng.Next01();
        }

        public override float Next(float brightness, float modulation)
        {
            float wow = (_wow.Next(3f) - 0.5f) * 2f;                           // -1..1
            _flutterPhase = (_flutterPhase + 6.3f / Sr) % 1f;
            float flutter = MathF.Sin(_flutterPhase * MathF.Tau);

            // The tone floats with the wow, like a tape sweep
            if ((_tick++ & 31) == 0)
                _hiss.Set((1500f + 5000f * brightness) * (1f + 0.35f * modulation * wow), 0.7f);

            float level = Math.Max(0.15f, 1f + modulation * (0.55f * wow + 0.08f * flutter));

            _hiss.Process(_rng.Signed(), out _, out _, out float high);
            _bed.Process(_rng.Signed(), out float low, out _, out _);

            return (high * 0.22f + low * 0.35f) * level;
        }
    }

    /// <summary>Space rumble: very low brown noise (below 80 Hz) that shifts now and then, with a trace of saturation to be audible.</summary>
    private sealed class RumbleChannel : Channel
    {
        private Rng _rng;
        private readonly NoiseColor _brown = new(NoiseType.Brown);
        private Svf _lowA = new(50f, 0.7f);
        private Svf _lowB = new(50f, 0.7f);
        private Wander _level;
        private float _swell;
        private float _swellTarget;
        private int _swellLeft;
        private Leveler _leveler;
        private int _tick;

        public RumbleChannel(uint seed)
        {
            _rng = new Rng(seed);
            _level = new Wander(seed ^ 0x9E3779B9u);
        }

        public override float Next(float brightness, float modulation)
        {
            // A "shift": the rumble swells and climbs a little for a few seconds, then sinks back.
            if (_swellLeft > 0)
            {
                if (--_swellLeft == 0)
                    _swellTarget = 0f;
            }
            else if (_rng.Event(0.03f + 0.1f * modulation))
            {
                _swellLeft = (int)((3f + 4f * _rng.Next01()) * Sr);
                _swellTarget = 0.6f + 0.4f * _rng.Next01();
            }

            _swell += (_swellTarget - _swell) * (1f - MathF.Exp(-1f / (2.5f * Sr)));

            if ((_tick++ & 63) == 0)
            {
                float hz = (35f + 45f * brightness) * (1f + 0.3f * _swell);
                _lowA.Set(hz, 0.7f);
                _lowB.Set(hz, 0.7f);
            }

            _lowA.Process(_brown.Next(_rng.Signed()), out float a, out _, out _);
            _lowB.Process(a, out float b, out _, out _);

            float level = 0.45f + 0.35f * _level.Next(12f) + modulation * 0.7f * _swell;
            return MathF.Tanh(_leveler.Next(b) * level * 1.2f) * 0.5f;
        }
    }

    /// <summary>Geiger counter / sparks: rare hard clicks at random (exponentially spread) intervals, no background.</summary>
    private sealed class GeigerChannel : Channel
    {
        private Rng _rng;
        private Clicker _click, _spark;
        private int _echoLeft;

        public GeigerChannel(uint seed) => _rng = new Rng(seed);

        public override float Next(float brightness, float modulation)
        {
            if (_rng.Event(0.5f + 14f * modulation))
            {
                _click.Trigger(0.7f + 0.3f * _rng.Next01(), (3000f + 5000f * brightness) * (0.8f + 0.4f * _rng.Next01()), 0.6f,
                    0.0002f + 0.0004f * _rng.Next01());

                // sometimes a quick double click
                if (_rng.Next01() < 0.25f)
                    _echoLeft = (int)((0.005f + 0.035f * _rng.Next01()) * Sr);
            }

            if (_echoLeft > 0 && --_echoLeft == 0)
                _click.Trigger(0.6f, 4500f, 0.6f, 0.0003f);

            // Sparks: a rarer, louder crack with a short tail
            if (_rng.Event(0.1f + 1.2f * modulation))
                _spark.Trigger(1f, 1500f + 3500f * brightness, 0.8f, 0.003f + 0.004f * _rng.Next01());

            return (_click.Next(_rng.Signed()) * 3f + _spark.Next(_rng.Signed()) * 2.5f);
        }
    }

    /// <summary>
    /// Singing ice: rare cracks, each a quick falling tone (a "blaster shot"), now and then with an echo from the far side
    /// of the lake and a low thump; a very faint creak below.
    /// </summary>
    private sealed class IceChannel : Channel
    {
        private Rng _rng;
        private Chirp _crack, _echo;
        private Clicker _thump;
        private Svf _creak = new(260f, 1.2f);
        private Wander _creakLevel;
        private int _echoLeft;
        private float _echoAmplitude, _echoFrom, _echoTo, _echoSweep, _echoRing;

        public IceChannel(uint seed)
        {
            _rng = new Rng(seed);
            _creakLevel = new Wander(seed ^ 0x9E3779B9u);
        }

        public override float Next(float brightness, float modulation)
        {
            if (_rng.Event(0.15f + 1.2f * modulation))
            {
                // thin ice rings high, thick ice lower
                float from = (2500f + 5500f * brightness) * (0.75f + 0.5f * _rng.Next01());
                float to = from * (0.06f + 0.09f * _rng.Next01());
                float length = 0.08f + 0.32f * _rng.Next01();
                float amplitude = 0.4f + 0.6f * _rng.Next01();

                _crack.Trigger(amplitude, from, to, length / 3f, length * 0.8f);

                if (_rng.Next01() < 0.5f)
                {
                    _echoLeft = (int)((0.03f + 0.09f * _rng.Next01()) * Sr);
                    _echoAmplitude = amplitude * 0.35f;
                    _echoFrom = from * 0.8f;
                    _echoTo = to * 0.9f;
                    _echoSweep = length / 2.5f;
                    _echoRing = length;
                }

                if (_rng.Next01() < 0.4f)
                    _thump.Trigger(0.8f, 90f, 1f, 0.06f);
            }

            if (_echoLeft > 0 && --_echoLeft == 0)
                _echo.Trigger(_echoAmplitude, _echoFrom, _echoTo, _echoSweep, _echoRing);

            _creak.Process(_rng.Signed(), out float creak, out _, out _);

            return (_crack.Next() + _echo.Next()) * 0.45f
                + _thump.Next(_rng.Signed()) * 1.5f
                + creak * 0.1f * (0.3f + 0.7f * _creakLevel.Next(9f));
        }
    }

    /// <summary>Power line in damp weather: a faint 50 Hz hum, a hiss and crackles that come on the peaks of the voltage.</summary>
    private sealed class HighVoltageChannel : Channel
    {
        private Rng _rng;
        private float _phase;
        private Clicker _crackle;
        private Svf _hiss = new(2500f, 0.7f);
        private Wander _humidity;

        public HighVoltageChannel(uint seed)
        {
            _rng = new Rng(seed);
            _humidity = new Wander(seed ^ 0x9E3779B9u);
            _phase = _rng.Next01();
        }

        public override float Next(float brightness, float modulation)
        {
            _phase = (_phase + 50f / Sr) % 1f;
            float sine = MathF.Sin(_phase * MathF.Tau);
            float peak = sine * sine;             // 100 Hz: both peaks of the voltage
            float peak3 = peak * peak * peak;

            // The discharge happens at the peaks. The damper the air, the more of it.
            float humidity = 0.5f + _humidity.Next(18f);
            if (_rng.Event((40f + 500f * modulation) * humidity * peak3 * 3f))
            {
                float loudness = _rng.Next01();
                _crackle.Trigger(0.1f + 0.9f * loudness * loudness, (2500f + 5500f * brightness) * (0.6f + 0.8f * _rng.Next01()), 0.8f,
                    0.0003f + 0.0008f * _rng.Next01());
            }

            _hiss.Process(_rng.Signed(), out _, out _, out float high);
            float hiss = high * 0.05f * (0.2f + 0.8f * peak) * (0.5f + 0.5f * humidity);

            float hum = 0.05f * sine + 0.025f * MathF.Sin(_phase * 3f * MathF.Tau);

            return _crackle.Next(_rng.Signed()) * 2.5f + hiss + hum;
        }
    }

    /// <summary>Force field: three detuned humming voices through a resonant filter that slowly breathes, with a shiver now and then.</summary>
    private sealed class ShieldChannel : Channel
    {
        private const int Harmonics = 4;
        private Rng _rng;
        private readonly float[] _phase = new float[3];
        private Svf _filter = new(400f, 4f);
        private Wander _sweep, _shiverGate;
        private float _lfo;
        private Leveler _leveler;
        private int _tick;

        public ShieldChannel(uint seed)
        {
            _rng = new Rng(seed);
            _sweep = new Wander(seed ^ 0x9E3779B9u);
            _shiverGate = new Wander(seed ^ 0x85EBCA6Bu);

            for (int i = 0; i < _phase.Length; i++)
                _phase[i] = _rng.Next01();
        }

        private static readonly float[] Detune = [1f, 1.0045f, 0.9915f];

        public override float Next(float brightness, float modulation)
        {
            float raw = 0f;
            for (int v = 0; v < _phase.Length; v++)
            {
                _phase[v] = (_phase[v] + 62f * Detune[v] / Sr) % 1f;

                for (int k = 1; k <= Harmonics; k++)
                    raw += MathF.Sin(_phase[v] * k * MathF.Tau) / k;
            }

            _lfo = (_lfo + 0.13f / Sr) % 1f;
            float breath = 0.5f * _sweep.Next(7f) + 0.5f * (0.5f + 0.5f * MathF.Sin(_lfo * MathF.Tau));

            if ((_tick++ & 31) == 0)
                _filter.Set((150f + 1800f * brightness) * (0.5f + 1.5f * breath * (0.4f + 0.6f * modulation)), 4f);

            _filter.Process(raw, out _, out float band, out _);
            float tone = _leveler.Next(band * 0.8f + raw * 0.1f);

            // Shiver: the field trembles for a moment
            float shiverGate = Math.Clamp((_shiverGate.Next(6f) - 0.75f) * 5f, 0f, 1f);
            float shiver = 1f - 0.5f * modulation * shiverGate * (0.5f + 0.5f * MathF.Sin(_lfo * 55f * MathF.Tau));

            return tone * 0.28f * shiver;
        }
    }

    /// <summary>Old electronics in a silent room: a thin high whine with a slight wobble and rare soft ticks.</summary>
    private sealed class WhineChannel : Channel
    {
        private Rng _rng;
        private float _phase;
        private Wander _drift, _flicker;
        private Clicker _tick;
        private Svf _air = new(6000f, 0.7f);

        public WhineChannel(uint seed)
        {
            _rng = new Rng(seed);
            _drift = new Wander(seed ^ 0x9E3779B9u);
            _flicker = new Wander(seed ^ 0x85EBCA6Bu);
            _phase = _rng.Next01();
        }

        public override float Next(float brightness, float modulation)
        {
            // From 8 kHz to 16 kHz; with Modulation the pitch wobbles a little
            float hz = (8000f + 8000f * brightness) * (1f + 0.01f * modulation * (_drift.Next(4f) - 0.5f));
            _phase = (_phase + hz / Sr) % 1f;

            float level = 1f - 0.6f * modulation * _flicker.Next(1.2f);
            float tone = MathF.Sin(_phase * MathF.Tau) * 0.07f * level;

            if (_rng.Event(0.5f + 4f * modulation))
                _tick.Trigger(0.15f, 3000f + 3000f * _rng.Next01(), 1f, 0.0008f);

            _air.Process(_rng.Signed(), out _, out _, out float high);

            return tone + high * 0.01f + _tick.Next(_rng.Signed()) * 2f;
        }
    }

    /// <summary>Plasma arc: the arc strikes with a zap, sizzles with dense crackle for a moment and goes out.</summary>
    private sealed class ArcChannel : Channel
    {
        private Rng _rng;
        private Clicker _zap, _crackle;
        private Svf _sizzle = new(2000f, 0.7f);
        private Svf _buzz = new(240f, 5f);
        private float _gain, _flutter = 0.5f;
        private int _onLeft;

        public ArcChannel(uint seed) => _rng = new Rng(seed);

        public override float Next(float brightness, float modulation)
        {
            if (_onLeft > 0)
                _onLeft--;
            else if (_rng.Event(0.3f + 1.5f * modulation))
            {
                _onLeft = (int)((0.1f + 1.1f * _rng.Next01()) * Sr);
                _zap.Trigger(1f, 800f + 1200f * _rng.Next01(), 0.8f, 0.008f);
            }

            bool on = _onLeft > 0;
            float seconds = on ? 0.003f : 0.04f;
            _gain += ((on ? 1f : 0f) - _gain) * (1f - MathF.Exp(-1f / (seconds * Sr)));

            if (_gain < 1e-3f)
                return _zap.Next(_rng.Signed()) * 2f;

            // quick random swell of the arc (about 15 Hz)
            _flutter += (_rng.Next01() - _flutter) * 0.003f;

            if (_rng.Event(400f * _gain))
                _crackle.Trigger(0.2f + 0.8f * _rng.Next01(), (2500f + 5000f * brightness) * (0.6f + 0.8f * _rng.Next01()), 0.8f, 0.0005f);

            if (((int)(_flutter * 1000f) & 15) == 0)
                _sizzle.Set(1200f + 3000f * brightness, 0.7f);

            _sizzle.Process(_rng.Signed(), out _, out _, out float high);
            _buzz.Process(_rng.Signed(), out _, out float band, out _);

            float arc = high * 0.3f * (0.4f + 1.2f * _flutter) + band * 0.35f * _flutter;
            return (arc + _crackle.Next(_rng.Signed()) * 2.5f) * _gain + _zap.Next(_rng.Signed()) * 2f;
        }
    }

    /// <summary>
    /// Gas burner: a steady blue-flame roar (band-passed noise with a slow flutter and fast flamelet wobble), a low
    /// rumble under it, a hiss above and now and then a soft "whoomf". Brightness tunes the roar, Modulation the flutter.
    /// </summary>
    private sealed class GasBurnerChannel : Channel
    {
        private Rng _rng;
        private Svf _roar = new(900f, 0.8f), _rumble = new(110f, 0.9f), _hiss = new(5500f, 0.7f), _wobble = new(11f, 0.7f);
        private Wander _flutter, _gust;
        private Clicker _whoomf;
        private int _counter;

        public GasBurnerChannel(uint seed)
        {
            _rng = new Rng(seed);
            _flutter = new Wander(seed ^ 0x9E3779B9u);
            _gust = new Wander(seed ^ 0x85EBCA6Bu);
        }

        public override float Next(float brightness, float modulation)
        {
            if ((_counter++ & 31) == 0)
                _roar.Set(380f + 1900f * brightness * (0.85f + 0.3f * _gust.Next(2.5f)), 0.9f);

            _wobble.Process(_rng.Signed(), out float slow, out _, out _);
            float flutter = 1f + (0.35f * (_flutter.Next(0.6f) - 0.5f) + 5f * slow) * (0.3f + 0.9f * modulation);

            _roar.Process(_rng.Signed(), out _, out float band, out _);
            _rumble.Process(_rng.Signed(), out float low, out _, out _);
            _hiss.Process(_rng.Signed(), out _, out _, out float high);

            if (_rng.Event(0.05f + 0.5f * modulation))
                _whoomf.Trigger(0.8f, 140f + 120f * _rng.Next01(), 0.8f, 0.09f);

            return band * 1.5f * flutter + low * 1.6f + high * 0.12f * (0.3f + brightness) + _whoomf.Next(_rng.Signed()) * 3f;
        }
    }

    /// <summary>
    /// Gong: now and then a stroke. A stroke is a set of inharmonic partials that ring for seconds, every one slowly
    /// beating, started by a soft thud. Brightness sets the pitch and how much the upper partials ring; Modulation how
    /// often the gong is struck. Strokes can overlap.
    /// </summary>
    private sealed class GongChannel : Channel
    {
        private static readonly float[] Ratios = [1f, 1.52f, 2.07f, 2.43f, 3.16f, 4.1f, 5.3f, 6.6f];
        private const int Voices = 3;

        private Rng _rng;
        private readonly GongVoice[] _voices = new GongVoice[Voices];
        private Clicker _thud;
        private int _next;

        public GongChannel(uint seed)
        {
            _rng = new Rng(seed);

            for (int i = 0; i < Voices; i++)
                _voices[i] = new GongVoice();
        }

        public override float Next(float brightness, float modulation)
        {
            if (_rng.Event(0.06f + 0.4f * modulation))
            {
                float baseHz = (60f + 150f * brightness) * (0.92f + 0.16f * _rng.Next01());
                _voices[_next].Strike(baseHz, brightness, 0.6f + 0.4f * _rng.Next01(), ref _rng);
                _next = (_next + 1) % Voices;
                _thud.Trigger(0.6f, 180f + 200f * brightness, 0.8f, 0.05f);
            }

            float sum = 0f;
            for (int i = 0; i < Voices; i++)
                sum += _voices[i].Next();

            return sum * 0.5f + _thud.Next(_rng.Signed()) * 1.5f;
        }

        private sealed class GongVoice
        {
            private readonly float[] _phase = new float[Ratios.Length];
            private readonly float[] _beat = new float[Ratios.Length];
            private readonly float[] _beatStep = new float[Ratios.Length];
            private readonly float[] _step = new float[Ratios.Length];
            private readonly float[] _amplitude = new float[Ratios.Length];
            private readonly float[] _decay = new float[Ratios.Length];
            private float _attack = 1f;
            private bool _active;

            public void Strike(float baseHz, float brightness, float loudness, ref Rng rng)
            {
                for (int i = 0; i < Ratios.Length; i++)
                {
                    _step[i] = baseHz * Ratios[i] * (1f + 0.003f * rng.Signed()) / Sr;
                    _phase[i] = rng.Next01();
                    _beat[i] = rng.Next01();
                    _beatStep[i] = (0.35f + 0.4f * i) / Sr;
                    _amplitude[i] = loudness * (i == 0 ? 1f : (0.25f + 1.1f * brightness) / (1f + 0.55f * i));
                    float seconds = (9f - 0.9f * i) * (0.8f + 0.4f * rng.Next01());
                    _decay[i] = MathF.Exp(-1f / (seconds * Sr));
                }

                _attack = 0f;
                _active = true;
            }

            public float Next()
            {
                if (!_active)
                    return 0f;

                _attack = Math.Min(1f, _attack + 1f / (0.004f * Sr));

                float sum = 0f;
                float loudest = 0f;

                for (int i = 0; i < Ratios.Length; i++)
                {
                    _phase[i] += _step[i];
                    if (_phase[i] >= 1f)
                        _phase[i] -= 1f;

                    _beat[i] += _beatStep[i];
                    if (_beat[i] >= 1f)
                        _beat[i] -= 1f;

                    _amplitude[i] *= _decay[i];
                    loudest = Math.Max(loudest, _amplitude[i]);

                    float beating = 0.7f + 0.3f * MathF.Sin(_beat[i] * MathF.Tau);
                    sum += MathF.Sin(_phase[i] * MathF.Tau) * _amplitude[i] * beating;
                }

                if (loudest < 1e-4f)
                    _active = false;

                return sum * _attack * 0.35f;
            }
        }
    }

    /// <summary>
    /// A drop falling from a height into water: a tiny impact click, then the "bloop" of the air bubble it traps (a
    /// short tone that rises in pitch while it fades), often a smaller second bubble and a quiet ripple. Brightness sets the
    /// pitch (a small or a big drop); Modulation how often drops fall.
    /// </summary>
    private sealed class WaterDropChannel : Channel
    {
        private Rng _rng;
        private Bubble _main, _second;
        private Clicker _impact, _ripple;
        private int _secondDelay;
        private float _secondHz, _secondGain;

        public WaterDropChannel(uint seed) => _rng = new Rng(seed);

        public override float Next(float brightness, float modulation)
        {
            if (_rng.Event(0.25f + 3.5f * modulation))
            {
                float hz = (300f + 1100f * brightness) * (0.8f + 0.4f * _rng.Next01());
                float loudness = 0.5f + 0.5f * _rng.Next01();

                _main.Trigger(loudness, hz, 0.07f + 0.05f * (1f - brightness));
                _impact.Trigger(0.5f * loudness, 2200f + 2500f * brightness, 1.2f, 0.0012f);
                _ripple.Trigger(0.12f * loudness, 1600f + 1500f * brightness, 1f, 0.18f);

                if (_rng.Next01() < 0.55f)
                {
                    _secondDelay = (int)((0.015f + 0.07f * _rng.Next01()) * Sr);
                    _secondHz = hz * (1.4f + 0.5f * _rng.Next01());
                    _secondGain = loudness * (0.25f + 0.3f * _rng.Next01());
                }
            }

            if (_secondDelay > 0 && --_secondDelay == 0)
                _second.Trigger(_secondGain, _secondHz, 0.04f);

            return (_main.Next() + _second.Next()) * 1.4f
                + (_impact.Next(_rng.Signed()) + _ripple.Next(_rng.Signed())) * 1.5f;
        }

        /// <summary>The bubble: a sine whose pitch climbs quickly toward 2.2x the start while the tone dies out.</summary>
        private struct Bubble
        {
            private float _hz, _amplitude, _decay, _time, _attack, _phase;

            public void Trigger(float amplitude, float hz, float decaySeconds)
            {
                _amplitude = amplitude;
                _hz = hz;
                _decay = MathF.Exp(-1f / (Math.Max(decaySeconds, 5e-3f) * Sr));
                _time = 0f;
                _attack = 0f;
            }

            public float Next()
            {
                if (_amplitude < 1e-4f)
                    return 0f;

                _time += 1f / Sr;
                _amplitude *= _decay;
                _attack = Math.Min(1f, _attack + 1f / (0.0012f * Sr));

                float hz = _hz * (1f + 1.2f * (1f - MathF.Exp(-_time / 0.035f)));
                _phase += hz / Sr;
                if (_phase >= 1f)
                    _phase -= 1f;

                return MathF.Sin(_phase * MathF.Tau) * _amplitude * _attack;
            }
        }
    }
}
