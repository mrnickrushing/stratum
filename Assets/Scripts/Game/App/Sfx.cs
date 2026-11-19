using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
        AudioClip[] _birds, _crickets;
        AudioClip _tuneClip;
        Task<float[]> _tuneJob;

        public static Sfx Create()
        {
            HookDeviceChanges();
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
            _clips["xp"] = Chime(new[] { 1760f, 2350f }, 0.09f);
            _clips["levelup"] = Chime(new[] { 784f, 988f, 1175f, 1568f }, 0.7f);
            _clips["enchant"] = Chime(new[] { 523f, 659f, 784f, 1046f, 1318f }, 1.1f);
            _clips["anvil"] = Chime(new[] { 1900f, 2650f }, 0.35f);
            _clips["hurt"] = Hurt();
            _clips["splash"] = Splash();
            _clips["break_tool"] = Chime(new[] { 300f, 210f }, 0.3f);
            _clips["levelup"] = Chime(new[] { 523f, 659f, 784f }, 0.6f);
            // Creatures and fighting.
            _clips["moo"] = Voice(0.9f, 140, 95, 0.9f, 2.2f, 0.5f);
            _clips["oink"] = Grunt(0.28f, 260, 0.9f, 3);
            _clips["baa"] = Voice(0.7f, 330, 300, 0.6f, 18, 0.35f);
            _clips["cluck"] = Grunt(0.12f, 900, 0.55f, 2);
            _clips["groan"] = Voice(1.1f, 120, 85, 1.1f, 5, 0.6f);
            _clips["rattle"] = Rattle();
            _clips["hiss"] = Hiss(0.6f);
            _clips["imp"] = Voice(0.5f, 420, 600, 0.5f, 25, 0.3f);
            _clips["roar"] = Voice(1.8f, 70, 50, 1.6f, 7, 0.9f);
            _clips["villager"] = Voice(0.35f, 210, 180, 0.6f, 4, 0.2f);
            _clips["hit"] = Thud(0.18f, 120, 0.9f);
            _clips["hit_hard"] = Thud(0.25f, 90, 1.2f);
            _clips["swish"] = Swish(0.16f);
            _clips["bow"] = Twang();
            _clips["arrow"] = Thud(0.1f, 600, 0.5f);
            _clips["eat"] = Crunch();
            _clips["burp"] = Grunt(0.3f, 140, 0.6f, 1);
            _clips["chest_open"] = Creak(0.45f, true);
            _clips["chest_close"] = Creak(0.3f, false);
            _clips["sizzle"] = Hiss(0.9f);
            _clips["door_open"] = Creak(0.35f, true);
            _clips["door_close"] = Thud(0.18f, 180, 0.9f);
            // A small set of calls, reused (a new clip every chirp would pile up in memory).
            _birds = new AudioClip[8]; for (int i = 0; i < _birds.Length; i++) _birds[i] = Bird();
            _crickets = new AudioClip[3]; for (int i = 0; i < _crickets.Length; i++) _crickets[i] = Cricket();
            _wind = Loop(Wind(), 0.0f);
            _cave = Loop(CaveDrone(), 0.0f);
            _rain = Loop(RainNoise(), 0.0f);
            _clips["thunder"] = Thunder();
            _music = gameObject.AddComponent<AudioSource>(); _music.playOnAwake = false;
        }

        // Plugging in a headset (or pulling it out) changes the system's sound output. Unity keeps playing to the
        // old device unless the audio is reset, and a reset stops every source, so the loops are started again.
        public static event Action AudioRestarted;
        static bool _hooked;

        static void HookDeviceChanges()
        {
            if (_hooked) return;
            _hooked = true;
            AudioSettings.OnAudioConfigurationChanged += changed =>
            {
                if (!changed) return;
                Debug.Log("[Stratum] sound output changed; switching to " + AudioSettings.driverCapabilities);
                AudioSettings.Reset(AudioSettings.GetConfiguration());
                if (I != null) foreach (var a in I.GetComponents<AudioSource>()) if (a.loop && a.clip != null) a.Play();
                AudioRestarted?.Invoke();
            };
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
            _rain.volume = Mathf.MoveTowards(_rain.volume, InGame ? RainLevel * 0.42f * Master : 0, dt * 0.5f);
            _cave.volume = Mathf.MoveTowards(_cave.volume, InGame ? 0.22f * Depth * Master : 0, dt * 0.2f);
            if (InGame && surface > 0.5f)
            {
                if (Daylight > 0.5f && (_birdTimer -= dt) < 0) { _birdTimer = 3 + (float)_r.NextDouble() * 9; PlayClip(_birds[_r.Next(_birds.Length)], 0.16f); }
                if (Daylight < 0.3f && (_cricketTimer -= dt) < 0) { _cricketTimer = 1.5f + (float)_r.NextDouble() * 3; PlayClip(_crickets[_r.Next(_crickets.Length)], 0.08f); }
            }
            // A new phrase is written on a worker thread (about a million samples), then played.
            if (_tuneJob == null && (_musicTimer -= dt) < 0 && Music > 0.01f && !_music.isPlaying)
            {
                _musicTimer = 150 + (float)_r.NextDouble() * 180;
                int seed = _r.Next();
                _tuneJob = Task.Run(() => TuneData(new System.Random(seed)));
            }
            if (_tuneJob != null && _tuneJob.IsCompleted)
            {
                var job = _tuneJob; _tuneJob = null;
                if (job.Status == TaskStatus.RanToCompletion)
                {
                    _music.Stop();
                    if (_tuneClip != null) Destroy(_tuneClip);
                    _tuneClip = Make("tune", job.Result);
                    _music.clip = _tuneClip;
                    _music.volume = 0.32f * Music * Master;
                    _music.Play();
                }
            }
            _music.volume = 0.32f * Music * Master;
        }

        // A sound out in the world, quieter with distance.
        readonly AudioSource[] _at = new AudioSource[16];
        int _nextAt;
        public void PlayAt(string name, Vector3 pos, float vol = 1, float pitch = 1)
        {
            if (!_clips.TryGetValue(name, out var c)) return;
            var a = _at[_nextAt];
            if (a == null)
            {
                var go = new GameObject("Sfx3D"); go.transform.SetParent(transform, false);
                a = _at[_nextAt] = go.AddComponent<AudioSource>();
                a.playOnAwake = false; a.spatialBlend = 1; a.rolloffMode = AudioRolloffMode.Linear; a.minDistance = 3; a.maxDistance = 40; a.dopplerLevel = 0;
            }
            _nextAt = (_nextAt + 1) % _at.Length;
            a.transform.position = pos;
            a.pitch = pitch * (0.92f + (float)_r.NextDouble() * 0.16f);
            a.volume = vol * Master;
            a.clip = c;
            a.Play();
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

        AudioSource _rain;
        public float RainLevel;          // 0..1: how loud the rain is where the listener is (quieter under a roof)

        // Rain: bright hiss with a patter of drops on top.
        AudioClip RainNoise()
        {
            int n = Rate * 6; var d = new float[n]; var f = new Svf(); var g = new Svf();
            float drop = 0, dropF = 0;
            for (int i = 0; i < n; i++)
            {
                f.Step(Rand(), 2600, 0.9f); g.Step(Rand(), 700, 1.2f);
                if (_r.NextDouble() < 0.0016) { drop = 0.5f + (float)_r.NextDouble() * 0.5f; dropF = 1500 + (float)_r.NextDouble() * 2500; }
                drop *= 0.996f;
                float t = i / (float)Rate;
                d[i] = f.Band * 0.35f + g.Band * 0.25f + Mathf.Sin(2 * Mathf.PI * dropF * t) * drop * 0.08f;
            }
            return Make("rain", Crossfade(d, Rate / 2));
        }

        // Thunder: a crack, then a long low roll.
        AudioClip Thunder()
        {
            int n = Rate * 5; var d = new float[n]; var f = new Svf(); var g = new Svf();
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                f.Step(Rand(), 90 + 60 * Mathf.Sin(t * 3), 1.8f);
                g.Step(Rand(), 1800, 1.0f);
                float crack = t < 0.25f ? Mathf.Exp(-t * 18) : 0;
                float roll = Mathf.Exp(-t * 0.7f) * (0.6f + 0.4f * Mathf.Sin(t * 5.3f) * Mathf.Sin(t * 2.1f));
                d[i] = g.Band * crack * 0.9f + f.Low * roll * 2.6f;
            }
            return Make("thunder", d);
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

        // A voiced call: a pulse train through two formants, gliding from one pitch to another.
        AudioClip Voice(float len, float hz0, float hz1, float formant, float vibrato, float breath)
        {
            int n = (int)(Rate * len); var d = new float[n]; var f1 = new Svf(); var f2 = new Svf();
            float ph = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate, u = i / (float)n;
                float hz = Mathf.Lerp(hz0, hz1, u) * (1 + 0.04f * Mathf.Sin(t * vibrato * 6.283f));
                ph += hz / Rate; ph -= Mathf.Floor(ph);
                float src = (ph < 0.5f ? 1 - ph * 4 : -1 + (ph - 0.5f) * 4) + Rand() * breath;
                f1.Step(src, 600 * formant + 200 * Mathf.Sin(u * 3.1f), 0.5f);
                f2.Step(src, 1500 * formant, 0.7f);
                float env = Mathf.Min(1, t * 20) * Mathf.Min(1, (len - t) * 8);
                d[i] = (f1.Band * 0.8f + f2.Band * 0.4f) * env * 0.7f;
            }
            return Make("voice", Norm(d, 0.8f));
        }

        AudioClip Grunt(float len, float hz, float formant, int bursts)
        {
            int n = (int)(Rate * len * bursts * 1.2f); var d = new float[n]; var f = new Svf();
            float ph = 0;
            for (int b = 0; b < bursts; b++)
            {
                int s0 = (int)(b * Rate * len * 1.15f);
                for (int i = 0; i < (int)(Rate * len) && s0 + i < n; i++)
                {
                    float t = i / (float)Rate, u = t / len;
                    ph += hz * (1.1f - u * 0.3f) / Rate; ph -= Mathf.Floor(ph);
                    float src = (ph * 2 - 1) + Rand() * 0.6f;
                    f.Step(src, 900 * formant, 0.6f);
                    d[s0 + i] += f.Band * Mathf.Sin(u * Mathf.PI) * 0.8f;
                }
            }
            return Make("grunt", Norm(d, 0.75f));
        }

        AudioClip Rattle()
        {
            int n = (int)(Rate * 0.5f); var d = new float[n]; var f = new Svf();
            for (int k = 0; k < 9; k++)
            {
                int s0 = (int)(Rate * (k * 0.045f + (float)_r.NextDouble() * 0.02f));
                float hz = 1400 + (float)_r.NextDouble() * 900;
                for (int i = 0; i < Rate * 0.04f && s0 + i < n; i++)
                {
                    float t = i / (float)Rate;
                    d[s0 + i] += (Mathf.Sin(6.283f * hz * t) * 0.6f + Rand() * 0.5f) * Mathf.Exp(-t * 90);
                }
            }
            for (int i = 0; i < n; i++) { f.Step(d[i], 3000, 0.8f); d[i] = f.Band; }
            return Make("rattle", Norm(d, 0.7f));
        }

        AudioClip Hiss(float len)
        {
            int n = (int)(Rate * len); var d = new float[n]; var f = new Svf();
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate, u = i / (float)n;
                f.Step(Rand(), 4200 + 1200 * Mathf.Sin(u * 9), 1.2f);
                d[i] = f.Band * Mathf.Min(1, t * 15) * Mathf.Exp(-u * 2.5f);
            }
            return Make("hiss", Norm(d, 0.6f));
        }

        AudioClip Thud(float len, float hz, float weight)
        {
            int n = (int)(Rate * len); var d = new float[n]; var f = new Svf();
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate, u = i / (float)n;
                f.Step(Rand(), 1400 * weight, 0.9f);
                d[i] = (Mathf.Sin(6.283f * hz * (1 - u * 0.5f) * t) * 0.9f + f.Low * 0.6f) * Mathf.Exp(-u * 7) * Mathf.Min(1, t * 900);
            }
            return Make("thud", Norm(d, 0.85f));
        }

        AudioClip Swish(float len)
        {
            int n = (int)(Rate * len); var d = new float[n]; var f = new Svf();
            for (int i = 0; i < n; i++)
            {
                float u = i / (float)n;
                f.Step(Rand(), 900 + 3500 * Mathf.Sin(u * Mathf.PI), 1.3f);
                d[i] = f.Band * Mathf.Sin(u * Mathf.PI);
            }
            return Make("swish", Norm(d, 0.45f));
        }

        AudioClip Twang()
        {
            int n = (int)(Rate * 0.45f); var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float hz = 180 + 260 * Mathf.Exp(-t * 30);
                d[i] = (Mathf.Sin(6.283f * hz * t) + 0.4f * Mathf.Sin(6.283f * hz * 2.02f * t)) * Mathf.Exp(-t * 9) * Mathf.Min(1, t * 2000) + Rand() * 0.2f * Mathf.Exp(-t * 60);
            }
            return Make("twang", Norm(d, 0.7f));
        }

        AudioClip Crunch()
        {
            int n = (int)(Rate * 0.22f); var d = new float[n]; var f = new Svf();
            for (int i = 0; i < n; i++)
            {
                float u = i / (float)n;
                float grit = Mathf.Abs(Mathf.Sin(u * 70 + Mathf.Sin(u * 13) * 4));
                f.Step(Rand() * grit, 2200, 0.9f);
                d[i] = f.Band * Mathf.Sin(u * Mathf.PI);
            }
            return Make("crunch", Norm(d, 0.6f));
        }

        AudioClip Creak(float len, bool rising)
        {
            int n = (int)(Rate * len); var d = new float[n]; var f = new Svf();
            float ph = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate, u = i / (float)n;
                float hz = rising ? 90 + 70 * u : 150 - 70 * u;
                ph += hz / Rate; ph -= Mathf.Floor(ph);
                float stick = ph < 0.08f ? 1 : 0;                 // stick-slip clicks
                f.Step(stick + Rand() * 0.1f, 1300, 0.4f);
                d[i] = f.Band * Mathf.Sin(u * Mathf.PI) + (u > 0.85f && !rising ? Mathf.Sin(6.283f * 80 * t) * (u - 0.85f) * 4 : 0);
            }
            return Make("creak", Norm(d, 0.6f));
        }

        static float[] Norm(float[] d, float peak)
        {
            float m = 0.0001f; foreach (var v in d) m = Mathf.Max(m, Mathf.Abs(v));
            for (int i = 0; i < d.Length; i++) d[i] = d[i] / m * peak;
            return d;
        }

        // A short pentatonic phrase on a soft plucked voice with a long tail.
        static float[] TuneData(System.Random _r)
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
            return d;
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
