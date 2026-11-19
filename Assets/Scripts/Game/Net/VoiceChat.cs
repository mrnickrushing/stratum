using System;
using System.Collections.Generic;
using Stratum.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Stratum.Game
{
    // Voice chat on a server. The microphone is read at 16 kHz, cut into 40 ms frames and sent while V is held
    // (or, in open-mic mode, while it hears speech). Each other player's voice plays from their figure: partly
    // placed in the world, so you can tell where they are, but never so quiet that a friend far away is lost.
    public sealed class VoiceChat : MonoBehaviour
    {
        public Action<byte[]> Send;
        public Func<bool> CanTalk;                       // false while typing in chat, paused, in menus...
        public Avatars Avatars;
        public Transform Listener;
        public bool Transmitting { get; private set; }
        public int FramesSent, FramesHeard;
        public float HeardLevel;                         // loudness of the last voice heard (0..1)

        // Tests: a made-up voice instead of the microphone ("-voicetone").
        public static bool TestTone;

        const float SpatialBlend = 0.6f;
        const int Jitter = 2 * Voice.FrameSamples;      // buffered ahead before playing (80 ms)

        // ------------------------------------------------------------------ microphone
        int _mode = -1;
        string _device;
        AudioClip _mic;
        int _micRate, _readPos;
        float[] _chunk = new float[0];
        readonly List<float> _pending = new List<float>(4096);
        double _rsPos; float _rsPrev;
        float _gate, _level, _tonePhase, _toneTime;
        readonly Voice.Encoder _enc = new Voice.Encoder();
        readonly float[] _frame = new float[Voice.FrameSamples];
        string _micProblem;
        float _problemShown;

        // ------------------------------------------------------------------ speakers
        sealed class Speaker
        {
            public int Id;
            public AudioSource Src;
            public AudioClip Clip;
            public long Played, Write, Cleared;          // absolute sample counts
            public int LastPos;
            public float LastHeard = -10;
        }
        readonly Dictionary<int, Speaker> _speakers = new Dictionary<int, Speaker>();
        readonly float[] _decoded = new float[Voice.FrameSamples];
        static readonly float[] Silence = new float[4 * Voice.FrameSamples];

        // ------------------------------------------------------------------ indicator
        RectTransform _ui;
        Image _dot;
        Text _status, _talkers;

        public static VoiceChat Create(Avatars avatars, Transform listener)
        {
            var go = new GameObject("Voice Chat");
            var v = go.AddComponent<VoiceChat>();
            v.Avatars = avatars;
            v.Listener = listener;
            v.BuildUi();
            return v;
        }

        void BuildUi()
        {
            var canvas = UIKit.Root("VoiceCanvas", 6);
            canvas.transform.SetParent(transform, false);
            _ui = UIKit.Rect(canvas.transform, "Voice").Place(0, 1, 16, -16, 420, 300);
            var pill = UIKit.Panel(_ui, "Pill", new Color(0, 0, 0, 0.45f), UIKit.RoundSmall);
            pill.rectTransform.Place(0, 1, 0, 0, 230, 44);
            _dot = UIKit.Panel(pill.transform, "Dot", new Color(0.95f, 0.30f, 0.25f), UIKit.Round);
            _dot.rectTransform.Place(0, 0.5f, 14, 0, 18, 18);
            _status = UIKit.Label(pill.transform, "Talking", 24, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold).Shadowed(1);
            _status.rectTransform.Place(0, 0.5f, 42, 0, 180, 40);
            _talkers = UIKit.Label(_ui, "", 23, new Color(0.62f, 0.95f, 0.66f), TextAnchor.UpperLeft, FontStyle.Bold).Shadowed(1.5f);
            _talkers.rectTransform.Place(0, 1, 4, -52, 420, 240);
            _talkers.supportRichText = false;
            foreach (var g in _ui.GetComponentsInChildren<Graphic>()) g.raycastTarget = false;
        }

        void OnDestroy()
        {
            StopMic();
            foreach (var s in _speakers.Values) if (s.Clip != null) Destroy(s.Clip);
        }

        void Update()
        {
            int mode = TestTone ? 2 : Settings.VoiceMode;
            if (mode != _mode) { _mode = mode; if (mode == 0) StopMic(); else if (!TestTone) StartMic(); }
            PumpMic();
            UpdateSpeakers();
            UpdateUi();
        }

        // ------------------------------------------------------------------ sending

        void StartMic()
        {
            StopMic();
            _micProblem = null;
            try
            {
                if (Microphone.devices.Length == 0) { _micProblem = "No microphone found"; return; }
                _device = null;                                          // the system's default microphone
                Microphone.GetDeviceCaps(_device, out int min, out int max);
                _micRate = min == 0 && max == 0 ? Voice.Rate : Mathf.Clamp(Voice.Rate, min, max);
                _mic = Microphone.Start(_device, true, 1, _micRate);
                if (_mic == null) { _micProblem = "Couldn't open the microphone"; return; }
                _micRate = _mic.frequency;
                _readPos = 0; _rsPos = 0; _rsPrev = 0;
                _pending.Clear();
            }
            catch (Exception e) { _micProblem = "Couldn't open the microphone"; Debug.LogWarning("[Stratum] microphone: " + e.Message); }
        }

        void StopMic()
        {
            if (_mic != null) { try { Microphone.End(_device); } catch { } Destroy(_mic); }
            _mic = null;
            Transmitting = false;
        }

        void PumpMic()
        {
            bool wantPush = _mode == 1 && Keyboard.current != null && Keyboard.current.vKey.isPressed && (CanTalk?.Invoke() ?? true);
            if (_mode == 1 && wantPush && _micProblem != null) _problemShown = 3;

            if (TestTone) { MakeTone(); }
            else
            {
                if (_mic == null) { Transmitting = false; return; }
                int pos = Microphone.GetPosition(_device);
                int len = _mic.samples;
                if (pos < 0 || pos >= len) return;
                int n = (pos - _readPos + len) % len;
                if (n == 0) return;
                if (_chunk.Length != n) _chunk = new float[n];
                if (_readPos + n <= len) _mic.GetData(_chunk, _readPos);
                else
                {
                    var a = new float[len - _readPos]; var b = new float[n - a.Length];
                    _mic.GetData(a, _readPos); _mic.GetData(b, 0);
                    Array.Copy(a, _chunk, a.Length); Array.Copy(b, 0, _chunk, a.Length, b.Length);
                }
                _readPos = pos;
                Resample(_chunk, n);
            }

            float gain = Settings.MicGain;
            while (_pending.Count >= Voice.FrameSamples)
            {
                for (int i = 0; i < Voice.FrameSamples; i++) _frame[i] = Mathf.Clamp(_pending[i] * gain, -1, 1);
                _pending.RemoveRange(0, Voice.FrameSamples);
                float rms = Voice.Rms(_frame, 0, Voice.FrameSamples);
                _level = Mathf.Max(rms * 4, _level * 0.85f);
                bool send;
                if (_mode == 1) send = wantPush;
                else
                {
                    // Open mic: speech opens the gate, and it stays open a moment so words don't get clipped.
                    if (rms > 0.012f) _gate = 0.45f;
                    send = _gate > 0 && (CanTalk?.Invoke() ?? true);
                    _gate -= Voice.FrameSamples / (float)Voice.Rate;
                }
                Transmitting = send;
                var bytes = _enc.Encode(_frame);                        // always, so the coder keeps up with the sound
                if (send) { Send?.Invoke(bytes); FramesSent++; }
            }
        }

        void Resample(float[] x, int n)
        {
            if (_micRate == Voice.Rate) { for (int i = 0; i < n; i++) _pending.Add(x[i]); return; }
            double step = _micRate / (double)Voice.Rate;
            while (_rsPos <= n - 1)
            {
                int i = (int)Math.Floor(_rsPos); float f = (float)(_rsPos - i);
                float a = i < 0 ? _rsPrev : x[i], b = x[Math.Min(i + 1, n - 1)];
                _pending.Add(a + (b - a) * f);
                _rsPos += step;
            }
            _rsPos -= n;
            _rsPrev = x[n - 1];
        }

        // A made-up voice for tests: a buzzing vowel that comes and goes, at the speed time passes.
        void MakeTone()
        {
            _toneTime += Time.unscaledDeltaTime;
            int want = Mathf.FloorToInt(_toneTime * Voice.Rate) - _pending.Count;
            _toneTime -= want / (float)Voice.Rate;
            for (int i = 0; i < want; i++)
            {
                _tonePhase += 1f / Voice.Rate;
                float env = Mathf.Clamp01(Mathf.Sin(_tonePhase * Mathf.PI * 0.8f) * 3);
                float f0 = 180 + 30 * Mathf.Sin(_tonePhase * 2);
                float s = Mathf.Sin(_tonePhase * f0 * Mathf.PI * 2) + 0.4f * Mathf.Sin(_tonePhase * f0 * 2 * Mathf.PI * 2) + 0.2f * Mathf.Sin(_tonePhase * f0 * 3 * Mathf.PI * 2);
                _pending.Add(0.18f * env * s);
            }
        }

        // ------------------------------------------------------------------ hearing

        public void Heard(int id, byte[] frame)
        {
            if (!Voice.Decode(frame, _decoded)) return;
            FramesHeard++;
            HeardLevel = Voice.Rms(_decoded, 0, Voice.FrameSamples);
            var s = SpeakerFor(id);
            SyncPlayhead(s);
            long ahead = s.Write - s.Played;
            // Ran dry (or fell far behind): start again a little ahead of what's playing.
            if (ahead < Voice.FrameSamples / 2 || ahead > Voice.Rate * 2 / 5) s.Write = s.Played + Jitter;
            Put(s, _decoded, Voice.FrameSamples, s.Write);
            s.Write += Voice.FrameSamples;
            Put(s, Silence, Silence.Length, s.Write);                // if nothing follows, what plays next is quiet
            s.Cleared = s.Write + Silence.Length;
            s.LastHeard = Time.unscaledTime;
        }

        public void Remove(int id)
        {
            if (!_speakers.TryGetValue(id, out var s)) return;
            _speakers.Remove(id);
            if (s.Src != null) Destroy(s.Src.gameObject);
            if (s.Clip != null) Destroy(s.Clip);
        }

        Speaker SpeakerFor(int id)
        {
            if (_speakers.TryGetValue(id, out var s)) return s;
            var go = new GameObject("Voice " + id);
            go.transform.SetParent(transform, false);
            s = new Speaker { Id = id };
            s.Clip = AudioClip.Create("voice " + id, Voice.Rate, 1, Voice.Rate, false);   // a 1 s loop written ahead of the playhead
            s.Src = go.AddComponent<AudioSource>();
            s.Src.clip = s.Clip;
            s.Src.loop = true;
            s.Src.playOnAwake = false;
            s.Src.spatialBlend = SpatialBlend;
            s.Src.rolloffMode = AudioRolloffMode.Linear;
            s.Src.minDistance = 8; s.Src.maxDistance = 90;
            s.Src.dopplerLevel = 0;
            s.Src.priority = 16;
            s.Src.volume = Settings.VoiceVolume;
            s.Src.Play();
            s.LastPos = 0; s.Played = 0; s.Write = Jitter;
            _speakers[id] = s;
            return s;
        }

        static void SyncPlayhead(Speaker s)
        {
            int pos = s.Src.timeSamples;
            s.Played += (pos - s.LastPos + s.Clip.samples) % s.Clip.samples;
            s.LastPos = pos;
        }

        static void Put(Speaker s, float[] data, int count, long at)
        {
            int len = s.Clip.samples, start = (int)(at % len);
            if (start + count <= len) { Set(s.Clip, data, 0, count, start); return; }
            int first = len - start;
            Set(s.Clip, data, 0, first, start);
            Set(s.Clip, data, first, count - first, 0);
        }

        static readonly Dictionary<int, float[]> Scratch = new Dictionary<int, float[]>();
        static void Set(AudioClip clip, float[] data, int from, int count, int at)
        {
            if (count <= 0) return;
            if (from == 0 && count == data.Length) { clip.SetData(data, at); return; }
            if (!Scratch.TryGetValue(count, out var buf)) Scratch[count] = buf = new float[count];
            Array.Copy(data, from, buf, 0, count);
            clip.SetData(buf, at);
        }

        void UpdateSpeakers()
        {
            float vol = Settings.VoiceVolume;
            foreach (var s in _speakers.Values)
            {
                SyncPlayhead(s);
                // The clip is a loop: keep wiping what's ahead of the playhead, so old words never come round again.
                while (s.Cleared < s.Played + Voice.Rate / 3)
                {
                    long at = Math.Max(s.Cleared, s.Played);
                    Put(s, Silence, Silence.Length, at);
                    s.Cleared = at + Silence.Length;
                }
                bool talking = Time.unscaledTime - s.LastHeard < 0.3f;
                Avatars?.SetTalking(s.Id, talking);
                s.Src.volume = vol;
                // From the figure's head if it's here; otherwise from right beside us.
                if (Avatars != null && Avatars.TryGet(s.Id, out var p)) { s.Src.transform.position = p + Vector3.up * 1.6f; s.Src.spatialBlend = SpatialBlend; }
                else if (Listener != null) { s.Src.transform.position = Listener.position; s.Src.spatialBlend = 0; }
            }
        }

        void UpdateUi()
        {
            if (_ui == null) return;
            _problemShown -= Time.unscaledDeltaTime;
            bool problem = _problemShown > 0 && _micProblem != null;
            var pill = _dot.transform.parent.gameObject;
            pill.SetActive(Transmitting || problem);
            if (Transmitting)
            {
                _status.text = "Talking";
                float pulse = 0.55f + 0.45f * Mathf.Clamp01(_level * 3);
                _dot.color = new Color(0.95f, 0.30f, 0.25f, pulse);
            }
            else if (problem) { _status.text = _micProblem; _dot.color = new Color(0.5f, 0.5f, 0.5f); }
            ((RectTransform)pill.transform).sizeDelta = new Vector2(_status.preferredWidth + 60, 44);

            var names = "";
            foreach (var s in _speakers.Values)
                if (Time.unscaledTime - s.LastHeard < 0.3f)
                    names += (names == "" ? "" : "\n") + "• " + (Avatars?.NameOf(s.Id) ?? "Someone") + " is talking";
            _talkers.text = names;
            _talkers.rectTransform.anchoredPosition = new Vector2(4, pill.activeSelf ? -52 : 0);
        }
    }
}
