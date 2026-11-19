using System.Collections.Generic;
using Stratum.Core;
using UnityEngine;

namespace Stratum.Game
{
    // All sound is synthesised at start-up: digging and steps per material, UI, splashes, wind, birds, crickets,
    // and a soft generative tune now and then.
    public sealed class Sfx : MonoBehaviour
    {
        public static Sfx I;
        const int Rate = 44100;
        readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        AudioSource[] _pool;
        int _next;
        AudioSource _wind, _cave, _music;
        float _birdTimer = 4, _cricketTimer = 2, _musicTimer = 40;
        public float Master = 1, Music = 0.6f;
        public float Daylight = 1, Depth;    // set by the game: 0 surface, 1 deep underground
        public bool InGame;
        System.Random _r = new System.Random(5);

        public static Sfx Create()
        {
            var go = new GameObject("Sfx");
            DontDestroyOnLoad(go);
            I = go.AddComponent<Sfx>();
            I.Build();
            return I;
        }

        void Build()
        {
            _pool = new AudioSource[24];
            for (int i = 0; i < _pool.Length; i++) { _pool[i] = gameObject.AddComponent<AudioSource>(); _pool[i].playOnAwake = false; }
            foreach (Sound s in System.Enum.GetValues(typeof(Sound)))
            {
                _clips["dig_" + s] = Material(s, 0.16f, 1f);
                _clips["break_" + s] = Material(s, 0.32f, 1.2f);
                _clips["place_" + s] = Material(s, 0.12f, 0.8f);
                _clips["step_" + s] = Material(s, 0.09f, 0.55f);
            }
            _clips["click"] = Tone(880, 0.05f, 0.25f, 3);
            _clips["craft"] = Chime(new[] { 660f, 990f }, 0.35f);
            _clips["pickup"] = Chime(new[] { 1200f }, 0.12f);
            _clips["hurt"] = Hurt();
            _clips["splash"] = Splash();
            _clips["break_tool"] = Chime(new[] { 300f, 210f }, 0.3f);
            _clips["levelup"] = Chime(new[] { 523f, 659f, 784f }, 0.6f);
            _wind = Loop(Wind(), 0.0f);
            _cave = Loop(CaveDrone(), 0.0f);
            _music = gameObject.AddComponent<AudioSource>(); _music.playOnAwake = false;
        }

        AudioSource Loop(AudioClip c, float vol)
        {
            var a = gameObject.AddComponent<AudioSource>();
            a.clip = c; a.loop = true; a.volume = vol; a.Play();
            return a;
        }

        public void Play(string name, float vol = 1, float pitch = 1)
        {
            if (!_clips.TryGetValue(name, out var c)) return;
            var a = _pool[_next]; _next = (_next + 1) % _pool.Length;
            a.pitch = pitch * (0.92f + (float)_r.NextDouble() * 0.16f);
            a.PlayOneShot(c, vol * Master);
        }

        public void Block(string kind, byte block, float vol = 1)
        {
            var s = Blocks.Get(block).Sound;
            Play(kind + "_" + s, vol);
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            float surface = 1 - Depth;
            _wind.volume = Mathf.MoveTowards(_wind.volume, InGame ? (0.10f + 0.06f * Mathf.Sin(Time.time * 0.13f)) * surface * Master : 0.06f * Master, dt * 0.2f);
            _cave.volume = Mathf.MoveTowards(_cave.volume, InGame ? 0.22f * Depth * Master : 0, dt * 0.2f);
            if (InGame && surface > 0.5f)
            {
                if (Daylight > 0.5f && (_birdTimer -= dt) < 0) { _birdTimer = 3 + (float)_r.NextDouble() * 9; PlayClip(Bird(), 0.16f); }
                if (Daylight < 0.3f && (_cricketTimer -= dt) < 0) { _cricketTimer = 1.5f + (float)_r.NextDouble() * 3; PlayClip(Cricket(), 0.08f); }
            }
            if ((_musicTimer -= dt) < 0 && Music > 0.01f && !_music.isPlaying)
            {
                _musicTimer = 150 + (float)_r.NextDouble() * 180;
                _music.clip = Tune();
                _music.volume = 0.32f * Music * Master;
                _music.Play();
            }
            _music.volume = 0.32f * Music * Master;
        }

        void PlayClip(AudioClip c, float vol)
        {
            var a = _pool[_next]; _next = (_next + 1) % _pool.Length;
            a.pitch = 1; a.PlayOneShot(c, vol * Master);
        }

        // ------------------------------------------------------------------ synthesis

        float Rand() => (float)(_r.NextDouble() * 2 - 1);

        static AudioClip Make(string name, float[] d)
        {
            var c = AudioClip.Create(name, d.Length, 1, Rate, false);
            c.SetData(d, 0);
            return c;
        }

        // A resonant low-pass (state-variable filter) step.
        struct Svf
        {
            float _lp, _bp;
            public float Low, Band, High;
            public void Step(float x, float cutoff, float q)
            {
                float f = 2 * Mathf.Sin(Mathf.PI * Mathf.Min(cutoff, Rate * 0.2f) / Rate);
                _lp += f * _bp;
                float hp = x - _lp - q * _bp;
                _bp += f * hp;
                Low = _lp; Band = _bp; High = hp;
            }
        }

        AudioClip Material(Sound s, float len, float weight)
        {
            int n = (int)(Rate * len);
            var d = new float[n];
            var f = new Svf(); var g = new Svf();
            float cut, q, body, bodyHz, ring = 0;
            switch (s)
            {
                case Sound.Stone: cut = 2400; q = 0.6f; body = 0.6f; bodyHz = 160; break;
                case Sound.Dirt: cut = 700; q = 0.9f; body = 0.5f; bodyHz = 90; break;
                case Sound.Grass: cut = 1900; q = 1.2f; body = 0.2f; bodyHz = 120; break;
                case Sound.Sand: cut = 4200; q = 1.4f; body = 0.1f; bodyHz = 100; break;
                case Sound.Wood: cut = 900; q = 0.25f; body = 0.9f; bodyHz = 210; break;
                case Sound.Leaves: cut = 3200; q = 1.5f; body = 0f; bodyHz = 100; break;
                case Sound.Glass: cut = 5000; q = 0.3f; body = 0.2f; bodyHz = 1800; ring = 1; break;
                case Sound.Snow: cut = 1200; q = 1.3f; body = 0.15f; bodyHz = 90; break;
                case Sound.Metal: cut = 3000; q = 0.2f; body = 0.6f; bodyHz = 620; ring = 0.7f; break;
                default: cut = 600; q = 1.2f; body = 0.3f; bodyHz = 110; break;
            }
            float grains = s == Sound.Grass || s == Sound.Leaves || s == Sound.Snow || s == Sound.Sand ? 1 : 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate, u = i / (float)n;
                float env = Mathf.Exp(-u * (5f / weight)) * Mathf.Min(1, t * 400);
                float noise = Rand();
                if (grains > 0) noise *= 0.4f + 0.6f * Mathf.Abs(Mathf.Sin(t * 260 + Mathf.Sin(t * 47) * 3));
                f.Step(noise, cut * (1 - u * 0.4f), q);
                float x = s == Sound.Sand || s == Sound.Leaves ? f.Band * 1.4f : f.Low;
                float thump = Mathf.Sin(2 * Mathf.PI * bodyHz * t * (1 - u * 0.3f)) * Mathf.Exp(-u * 9) * body;
                float rg = 0;
                if (ring > 0) rg = (Mathf.Sin(2 * Mathf.PI * bodyHz * 2.76f * t) * 0.5f + Mathf.Sin(2 * Mathf.PI * bodyHz * 5.4f * t) * 0.3f) * Mathf.Exp(-u * 4) * ring;
                g.Step(x + thump + rg, 6000, 0.7f);
                d[i] = Mathf.Clamp(g.Low * env * 0.8f, -1, 1);
            }
            return Make("mat_" + s, d);
        }

        AudioClip Tone(float hz, float len, float vol, float decay)
        {
            int n = (int)(Rate * len); var d = new float[n];
            for (int i = 0; i < n; i++) { float t = i / (float)Rate; d[i] = Mathf.Sin(2 * Mathf.PI * hz * t) * Mathf.Exp(-t / len * decay) * vol * Mathf.Min(1, t * 2000); }
            return Make("tone", d);
        }

        AudioClip Chime(float[] notes, float len)
        {
            int n = (int)(Rate * len); var d = new float[n];
            for (int k = 0; k < notes.Length; k++)
            {
                int start = (int)(k * Rate * 0.07f);
                for (int i = start; i < n; i++)
                {
                    float t = (i - start) / (float)Rate;
                    float e = Mathf.Exp(-t * 7) * Mathf.Min(1, t * 1500);
                    d[i] += (Mathf.Sin(2 * Mathf.PI * notes[k] * t) + 0.3f * Mathf.Sin(2 * Mathf.PI * notes[k] * 2.01f * t)) * e * 0.22f;
                }
            }
            return Make("chime", d);
        }

        AudioClip Hurt()
        {
            int n = (int)(Rate * 0.25f); var d = new float[n]; var f = new Svf();
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate, u = i / (float)n;
                float hz = 220 - u * 110;
                float v = Mathf.Sin(2 * Mathf.PI * hz * t) * 0.6f + Rand() * 0.25f;
                f.Step(v, 900, 0.8f);
                d[i] = f.Low * Mathf.Exp(-u * 4) * Mathf.Min(1, t * 300) * 0.9f;
            }
            return Make("hurt", d);
        }

        AudioClip Splash()
        {
            int n = (int)(Rate * 0.6f); var d = new float[n]; var f = new Svf();
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate, u = i / (float)n;
                f.Step(Rand(), 400 + 2500 * Mathf.Exp(-u * 3), 1.1f);
                d[i] = f.Band * Mathf.Exp(-u * 4) * Mathf.Min(1, t * 200) * 0.9f;
            }
            return Make("splash", d);
        }

        AudioClip Wind()
        {
            int n = Rate * 8; var d = new float[n]; var f = new Svf();
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float gust = 0.55f + 0.45f * Mathf.Sin(t * Mathf.PI * 2 / 8f) * Mathf.Sin(t * 0.9f + 1);
                f.Step(Rand(), 300 + 500 * gust, 1.4f);
                d[i] = f.Band * gust * 0.8f;
            }
            d = Crossfade(d, Rate / 2);
            return Make("wind", d);
        }

        AudioClip CaveDrone()
        {
            int n = Rate * 10; var d = new float[n]; var f = new Svf();
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                f.Step(Rand(), 120, 1.6f);
                float hum = Mathf.Sin(2 * Mathf.PI * 55 * t) * 0.15f + Mathf.Sin(2 * Mathf.PI * 82.5f * t) * 0.08f;
                // Occasional drip.
                float drip = 0;
                float ph = (t * 0.37f) % 1;
                if (ph < 0.02f) drip = Mathf.Sin(2 * Mathf.PI * (1800 - ph * 30000) * t) * (1 - ph / 0.02f) * 0.4f;
                d[i] = f.Low * 0.9f + hum * (0.6f + 0.4f * Mathf.Sin(t * 0.6f)) + drip;
            }
            d = Crossfade(d, Rate);
            return Make("cave", d);
        }

        static float[] Crossfade(float[] d, int fade)
        {
            for (int i = 0; i < fade; i++)
            {
                float k = i / (float)fade;
                d[i] = d[i] * k + d[d.Length - fade + i] * (1 - k);
            }
            System.Array.Resize(ref d, d.Length - fade);
            return d;
        }

        AudioClip Bird()
        {
            int notes = 2 + _r.Next(5);
            float baseHz = 2200 + (float)_r.NextDouble() * 1800;
            int n = (int)(Rate * (0.11f * notes + 0.1f)); var d = new float[n];
            float phase = 0;
            for (int k = 0; k < notes; k++)
            {
                int s0 = (int)(k * Rate * 0.11f), len = (int)(Rate * (0.05f + (float)_r.NextDouble() * 0.05f));
                float sweep = (float)_r.NextDouble() * 1600 - 600;
                for (int i = 0; i < len && s0 + i < n; i++)
                {
                    float u = i / (float)len;
                    float hz = baseHz + sweep * u + Mathf.Sin(u * 40) * 120;
                    phase += 2 * Mathf.PI * hz / Rate;
                    d[s0 + i] += Mathf.Sin(phase) * Mathf.Sin(u * Mathf.PI) * 0.5f;
                }
            }
            return Make("bird", d);
        }

        AudioClip Cricket()
        {
            int n = (int)(Rate * 0.6f); var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float pulse = Mathf.Max(0, Mathf.Sin(t * 2 * Mathf.PI * 30));
                float burst = (t % 0.3f) < 0.15f ? 1 : 0;
                d[i] = Mathf.Sin(2 * Mathf.PI * 4600 * t) * pulse * burst * 0.4f * Mathf.Sin(t / 0.6f * Mathf.PI);
            }
            return Make("cricket", d);
        }

        // A short pentatonic phrase on a soft plucked voice with a long tail.
        AudioClip Tune()
        {
            float[] scale = { 0, 2, 4, 7, 9, 12, 14, 16, 19 };
            float root = 220f * Mathf.Pow(2, _r.Next(-3, 3) / 12f);
            int bars = 8; float beat = 0.62f;
            int n = (int)(Rate * (bars * 4 * beat + 4)); var d = new float[n];
            int deg = 4;
            for (int b = 0; b < bars * 4; b++)
            {
                if (_r.NextDouble() < 0.3) continue;
                deg = Mathf.Clamp(deg + _r.Next(-2, 3), 0, scale.Length - 1);
                float hz = root * Mathf.Pow(2, scale[deg] / 12f);
                Pluck(d, (int)(b * beat * Rate), hz, 0.22f);
                if (b % 8 == 0) Pluck(d, (int)(b * beat * Rate), root / 2 * Mathf.Pow(2, scale[_r.Next(0, 4)] / 12f), 0.16f);
            }
            // Simple echo for space.
            int delay = (int)(Rate * beat * 0.75f);
            for (int i = delay; i < n; i++) d[i] += d[i - delay] * 0.32f;
            float peak = 0.001f; foreach (var v in d) peak = Mathf.Max(peak, Mathf.Abs(v));
            for (int i = 0; i < n; i++) d[i] = d[i] / peak * 0.7f;
            return Make("tune", d);
        }

        static void Pluck(float[] d, int start, float hz, float vol)
        {
            int len = Rate * 3;
            for (int i = 0; i < len && start + i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float e = Mathf.Exp(-t * 1.6f) * Mathf.Min(1, t * 300);
                float v = Mathf.Sin(2 * Mathf.PI * hz * t) + 0.35f * Mathf.Sin(2 * Mathf.PI * hz * 2 * t) * Mathf.Exp(-t * 3) +
                          0.12f * Mathf.Sin(2 * Mathf.PI * hz * 3.01f * t) * Mathf.Exp(-t * 5);
                d[start + i] += v * e * vol;
            }
        }
    }
}
