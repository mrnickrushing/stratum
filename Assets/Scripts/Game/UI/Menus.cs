using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Stratum.Game
{
    // Title screen, world list, new world, settings, pause, the blackout screen and the loading screen.
    public sealed partial class Menus : MonoBehaviour
    {
        public Action<WorldMeta> Play;
        public Action<string, string, bool, int, Core.WorldKind, bool> Create;   // name, seed text, creative, difficulty, kind, keep things on death
        public Func<WorldMeta, List<WorldStore.BackupInfo>> ListBackups;
        public Func<WorldMeta, WorldStore.BackupInfo, bool> Restore;
        public Action<WorldMeta> Delete;
        public Action Resume, SaveAndQuit, Respawn, Quit, SettingsChanged;
        public Func<List<WorldMeta>> ListWorlds;

        Canvas _canvas;
        RectTransform _root;
        GameObject _main, _worlds, _new, _settings, _pause, _death, _loading, _join;
        RectTransform _worldList;
        Image _loadBar; Text _loadText, _loadTip;
        InputField _newName, _newSeed;
        bool _newCreative;
        int _newDifficulty = 2;
        Button _diffBtn;
        Button _modeBtn;
        Action _settingsBack;
        Image _shade;
        readonly List<Texture2D> _thumbs = new List<Texture2D>();

        Text _pauseInfo, _deathSub;
        Button _quitBtn;

        public bool AnyOpen => _main.activeSelf || _worlds.activeSelf || _new.activeSelf || _settings.activeSelf || _pause.activeSelf || _death.activeSelf || _loading.activeSelf || _join.activeSelf;
        public bool PauseOpen => _pause.activeSelf || _settings.activeSelf && _settingsBack == (Action)ShowPause;

        static readonly string[] Tips =
        {
            "Punch a tree for logs, turn logs into planks, planks into a Workbench.",
            "Coal and a stick make torches. Light keeps caves friendly.",
            "Hold Ctrl to sneak. Sneaking keeps you from walking off ledges.",
            "Ore deep in the slate and basalt needs a stronger pickaxe.",
            "A Furnace turns ore into ingots, sand into glass, cobblestone back into stone.",
            "Sunstone glows on its own. Four shards make a lamp.",
            "Press F3 to see where you are, F2 to take a screenshot.",
        };

        public static Menus Create_()
        {
            var go = new GameObject("Menus");
            var m = go.AddComponent<Menus>();
            m.Build();
            return m;
        }

        void Build()
        {
            _canvas = UIKit.Root("MenuCanvas", 20);
            _canvas.transform.SetParent(transform, false);
            _root = (RectTransform)_canvas.transform;
            _shade = UIKit.Panel(_root, "Shade", new Color(0, 0, 0, 0.45f), UIKit.White); _shade.rectTransform.Fill();

            BuildMain(); BuildWorlds(); BuildNew(); BuildSettings(); BuildPause(); BuildDeath(); BuildLoading(); BuildJoin(); BuildBackups(); BuildExtras(); BuildControls();
            HideAll();
        }

        GameObject Screen(string name)
        {
            var r = UIKit.Rect(_root, name); r.Fill();
            return r.gameObject;
        }

        Image Card(GameObject screen, float w, float h, string title)
        {
            var p = UIKit.Panel(screen.transform, "Card", UIKit.PanelCol);
            p.rectTransform.Place(0.5f, 0.5f, 0, 0, w, h);
            if (title != null)
            {
                var t = UIKit.Label(p.transform, title, 46, UIKit.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
                t.rectTransform.Place(0.5f, 1, 0, -26, w - 80, 60);
            }
            return p;
        }

        void BuildMain()
        {
            _main = Screen("Main");
            var title = UIKit.Label(_main.transform, "STRATUM", 168, new Color(1f, 0.93f, 0.80f), TextAnchor.MiddleLeft, FontStyle.Bold).Shadowed(6);
            title.rectTransform.Place(0, 0.5f, 140, 250, 1200, 200);
            var o = title.gameObject.AddComponent<Outline>(); o.effectColor = new Color(0.25f, 0.15f, 0.05f, 0.6f); o.effectDistance = new Vector2(2, -2);
            var sub = UIKit.Label(_main.transform, "An endless world of stone, timber and light", 34, UIKit.Accent, TextAnchor.MiddleLeft, FontStyle.Italic).Shadowed(3);
            sub.rectTransform.Place(0, 0.5f, 148, 142, 1200, 50);

            float y = 20;
            foreach (var (label, act, primary) in new (string, Action, bool)[]
                     { ("Play", ShowWorlds, true), ("Multiplayer", () => ShowJoin(null), false), ("Extras", ShowExtras, false), ("Settings", () => ShowSettings(ShowMain), false), ("Quit", () => Quit?.Invoke(), false) })
            {
                var b = UIKit.Button(_main.transform, label, act, 34, primary);
                ((RectTransform)b.transform).Place(0, 0.5f, 148, y, 420, 76);
                y -= 92;
            }
            // Shown once the site says a newer version is out: it downloads the installer in the browser.
            _updateBtn = UIKit.Button(_main.transform, "Update", () => Application.OpenURL(DefaultServer.DownloadUrl), 30, true);
            ((RectTransform)_updateBtn.transform).Place(0, 0.5f, 600, 20, 440, 76);
            _updateNote = UIKit.Label(_main.transform, "", 22, new Color(1, 1, 1, 0.85f), TextAnchor.UpperLeft).Shadowed(2);
            _updateNote.rectTransform.Place(0, 0.5f, 644, -75, 520, 90);
            _updateBtn.gameObject.SetActive(false); _updateNote.gameObject.SetActive(false);
            var foot = UIKit.Label(_main.transform, $"v{Application.version}   ·   Rushing Technologies", 22, new Color(1, 1, 1, 0.6f), TextAnchor.LowerLeft).Shadowed(2);
            foot.rectTransform.Place(0, 0, 40, 30, 800, 40);
            var keys = UIKit.Label(_main.transform, "Keys can be changed in Settings › Controls  ·  Esc pause", 22, new Color(1, 1, 1, 0.6f), TextAnchor.LowerRight).Shadowed(2);
            keys.rectTransform.Place(1, 0, -40, 30, 1100, 40);
        }

        Button _updateBtn; Text _updateNote;

        // A newer version is out (the site said so).
        public void UpdateOut(string version)
        {
            if (_updateBtn == null) return;
            _updateBtn.SetText($"Update to {version}");
            _updateNote.text = "Downloads the new version. Close Stratum, then open\nthe file. Your worlds and logins are kept.";
            _updateBtn.gameObject.SetActive(true); _updateNote.gameObject.SetActive(true);
        }

        void BuildWorlds()
        {
            _worlds = Screen("Worlds");
            var card = Card(_worlds, 1100, 820, "Your Worlds");
            var scroll = UIKit.Scroll(card.transform, out _worldList);
            var sr = (RectTransform)scroll.transform; sr.Fill(); sr.offsetMin = new Vector2(40, 130); sr.offsetMax = new Vector2(-40, -110);
            var vl = _worldList.gameObject.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 10; vl.childControlHeight = false; vl.childControlWidth = true; vl.childForceExpandHeight = false;
            var newB = UIKit.Button(card.transform, "New World", ShowNew, 30, true);
            ((RectTransform)newB.transform).Place(0.5f, 0, 170, 36, 320, 72);
            var back = UIKit.Button(card.transform, "Back", ShowMain, 30);
            ((RectTransform)back.transform).Place(0.5f, 0, -170, 36, 320, 72);
        }

        void FillWorlds()
        {
            foreach (Transform t in _worldList) Destroy(t.gameObject);
            foreach (var t in _thumbs) Destroy(t);
            _thumbs.Clear();
            var list = ListWorlds?.Invoke() ?? new List<WorldMeta>();
            if (list.Count == 0)
            {
                var l = UIKit.Label(_worldList, "No worlds yet. Make one with New World.", 28, UIKit.Dim, TextAnchor.MiddleCenter);
                l.rectTransform.sizeDelta = new Vector2(0, 120);
            }
            foreach (var m in list)
            {
                var row = UIKit.Panel(_worldList, "World", new Color(0.15f, 0.16f, 0.2f, 0.95f), UIKit.RoundSmall);
                row.rectTransform.sizeDelta = new Vector2(0, 110);
                // The view from where the world was left (saved with it), else a plain tile.
                var pic = new GameObject("Thumb", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
                pic.transform.SetParent(row.transform, false);
                ((RectTransform)pic.transform).Place(0, 0.5f, 12, 0, 156, 88);
                pic.color = new Color(0.22f, 0.24f, 0.28f, 1);
                var thumbPath = m.Folder != null ? System.IO.Path.Combine(m.Folder, "thumb.jpg") : null;
                if (thumbPath != null && System.IO.File.Exists(thumbPath))
                {
                    var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
                    if (tex.LoadImage(System.IO.File.ReadAllBytes(thumbPath))) { pic.texture = tex; pic.color = Color.white; _thumbs.Add(tex); }
                    else Destroy(tex);
                }
                var name = UIKit.Label(row.transform, m.name, 32, UIKit.Ink, TextAnchor.UpperLeft, FontStyle.Bold);
                name.rectTransform.Place(0, 1, 186, -12, 410, 40);
                var when = new DateTime(Math.Max(m.lastPlayed, 1), DateTimeKind.Utc).ToLocalTime();
                var info = UIKit.Label(row.transform, $"{(m.kind > 0 && m.kind < (int)Core.WorldKind.Count ? Core.WorldGen.KindNames[m.kind] + "  ·  " : "")}{(m.creative ? "Creative" : "Survival")}  ·  day {m.day}  ·  {Math.Round(m.minutesPlayed)} min played", 20, UIKit.Dim, TextAnchor.UpperLeft);
                info.rectTransform.Place(0, 1, 186, -54, 410, 26);
                var last = UIKit.Label(row.transform, $"Last played {when:d MMM yyyy, HH:mm}", 18, UIKit.Dim * 0.85f, TextAnchor.UpperLeft);
                last.rectTransform.Place(0, 1, 186, -80, 410, 24);
                var mm = m;
                var play = UIKit.Button(row.transform, "Play", () => Play?.Invoke(mm), 26, true);
                ((RectTransform)play.transform).Place(1, 0.5f, -20, 0, 130, 64);
                var bk = UIKit.Button(row.transform, "Backups", () => ShowBackups(mm), 24);
                ((RectTransform)bk.transform).Place(1, 0.5f, -160, 0, 130, 64);
                Button del = null;
                bool armed = false;
                del = UIKit.Button(row.transform, "Delete", () =>
                {
                    if (!armed) { armed = true; del.SetText("Sure?"); return; }
                    Delete?.Invoke(mm); FillWorlds();
                }, 24);
                ((RectTransform)del.transform).Place(1, 0.5f, -300, 0, 110, 64);
            }
        }

        // ------------------------------------------------------------------ backups
        GameObject _backups; Transform _backupList; Text _backupNote; WorldMeta _backupOf;

        void BuildBackups()
        {
            _backups = Screen("Backups");
            var card = Card(_backups, 900, 720, "Backups");
            _backupNote = UIKit.Label(card.transform, "", 22, UIKit.Dim, TextAnchor.UpperCenter);
            _backupNote.rectTransform.Place(0.5f, 1, 0, -96, 800, 60);
            _backupList = UIKit.Rect(card.transform, "List").Place(0.5f, 1, 0, -196, 800, 420);
            var back = UIKit.Button(card.transform, "Back", ShowWorlds, 30);
            ((RectTransform)back.transform).Place(0.5f, 0, 0, 36, 320, 72);
        }

        public void ShowBackups(WorldMeta m)
        {
            _backupOf = m;
            Show(_backups, true);
            foreach (Transform t in _backupList) Destroy(t.gameObject);
            var list = ListBackups?.Invoke(m) ?? new List<WorldStore.BackupInfo>();
            _backupNote.text = list.Count == 0
                ? $"No backups of \"{m.name}\" yet. One is saved each time you open the world."
                : $"\"{m.name}\" is copied each time you open it, and the newest {WorldStore.KeepBackups} copies are kept.\nRestoring puts the world back as it was then. The world as it is now is backed up first.";
            float y = 0;
            foreach (var b in list)
            {
                var row = UIKit.Panel(_backupList, "Backup", new Color(0.15f, 0.16f, 0.2f, 0.95f), UIKit.RoundSmall);
                row.rectTransform.Place(0.5f, 1, 0, y, 800, 72);
                var l = UIKit.Label(row.transform, $"{b.When:d MMM yyyy, HH:mm}   ·   day {b.Day}", 26, UIKit.Ink, TextAnchor.MiddleLeft);
                l.rectTransform.Place(0, 0.5f, 24, 0, 520, 40);
                var bb = b; Button rb = null; bool armed = false;
                rb = UIKit.Button(row.transform, "Restore", () =>
                {
                    if (!armed) { armed = true; rb.SetText("Sure?"); return; }
                    bool ok = Restore != null && Restore(_backupOf, bb);
                    ShowBackups(_backupOf);
                    _backupNote.text = ok ? $"\"{_backupOf.name}\" is back as it was on {bb.When:d MMM, HH:mm}." : "That backup couldn't be restored. Nothing was changed.";
                }, 24);
                ((RectTransform)rb.transform).Place(1, 0.5f, -16, 0, 160, 52);
                y -= 82;
            }
        }

        void BuildNew()
        {
            _new = Screen("NewWorld");
            var card = Card(_new, 900, 806, "New World");
            var l1 = UIKit.Label(card.transform, "Name", 24, UIKit.Dim); l1.rectTransform.Place(0, 1, 70, -120, 400, 30);
            _newName = UIKit.Field(card.transform, "New World", "New World"); ((RectTransform)_newName.transform).Place(0, 1, 70, -156, 760, 64);
            var l2 = UIKit.Label(card.transform, "Seed (leave blank for a random world)", 24, UIKit.Dim); l2.rectTransform.Place(0, 1, 70, -246, 760, 30);
            _newSeed = UIKit.Field(card.transform, "random", ""); ((RectTransform)_newSeed.transform).Place(0, 1, 70, -282, 760, 64);
            // The sort of world: click to go through them (right-click or Shift goes back).
            _kindBtn = UIKit.Button(card.transform, "World: Normal", () =>
            {
                // The single-biome worlds share one stop here; the button beside it picks the biome.
                // After those come the four hand-built adventure worlds.
                int first = (int)Core.WorldGen.FirstOnly, builds = (int)Core.WorldKind.Town - (int)Core.WorldKind.Castle + 1, n = first + 1 + builds;
                bool back = UnityEngine.InputSystem.Keyboard.current?.shiftKey.isPressed == true;
                int at = Core.WorldGen.IsOnly(_newKind) ? first : Core.Builds.Is(_newKind) ? first + 1 + ((int)_newKind - (int)Core.WorldKind.Castle) : (int)_newKind;
                at = (at + (back ? n - 1 : 1)) % n;
                _newKind = at == first ? _lastOnly : at > first ? (Core.WorldKind)((int)Core.WorldKind.Castle + at - first - 1) : (Core.WorldKind)at;
                UpdateMode();
            }, 28);
            ((RectTransform)_kindBtn.transform).Place(0, 1, 70, -378, 760, 68);
            _biomeBtn = UIKit.Button(card.transform, "Biome", () =>
            {
                int first = (int)Core.WorldGen.FirstOnly, n = (int)Core.WorldGen.LastOnly - first + 1;
                bool back = UnityEngine.InputSystem.Keyboard.current?.shiftKey.isPressed == true;
                _newKind = _lastOnly = (Core.WorldKind)(first + ((int)_newKind - first + (back ? n - 1 : 1)) % n);
                UpdateMode();
            }, 28);
            ((RectTransform)_biomeBtn.transform).Place(0, 1, 458, -378, 372, 68);
            _modeBtn = UIKit.Button(card.transform, "Mode: Survival", () => { _newCreative = !_newCreative; UpdateMode(); }, 28);
            ((RectTransform)_modeBtn.transform).Place(0, 1, 70, -462, 372, 68);
            _diffBtn = UIKit.Button(card.transform, "Difficulty: Normal", () => { _newDifficulty = (_newDifficulty + 1) % 5; UpdateMode(); }, 28);
            ((RectTransform)_diffBtn.transform).Place(0, 1, 458, -462, 372, 68);
            _keepBtn = UIKit.Button(card.transform, "", () => { _newKeep = !_newKeep; UpdateMode(); }, 28);
            ((RectTransform)_keepBtn.transform).Place(0, 1, 70, -546, 760, 68);
            var hint = UIKit.Label(card.transform, "", 21, UIKit.Dim, TextAnchor.UpperLeft); hint.rectTransform.Place(0, 1, 74, -630, 760, 60);
            hint.name = "ModeHint";
            var create = UIKit.Button(card.transform, "Create World", () => Create?.Invoke(_newName.text, _newSeed.text, _newCreative, _newDifficulty, _newKind, _newKeep && !_newCreative), 30, true);
            ((RectTransform)create.transform).Place(0.5f, 0, 170, 36, 320, 72);
            var back = UIKit.Button(card.transform, "Back", ShowWorlds, 30);
            ((RectTransform)back.transform).Place(0.5f, 0, -170, 36, 320, 72);
            UpdateMode();
        }

        Button _kindBtn, _biomeBtn, _keepBtn; bool _newKeep;
        Core.WorldKind _newKind, _lastOnly = Core.WorldGen.FirstOnly;

        void UpdateMode()
        {
            bool one = Core.WorldGen.IsOnly(_newKind);
            _kindBtn.SetText(one ? "World:  One Biome  ›" : "World:  " + Core.WorldGen.KindName(_newKind) + "    ›");
            ((RectTransform)_kindBtn.transform).Place(0, 1, 70, -378, one ? 372 : 760, 68);
            _biomeBtn.gameObject.SetActive(one);
            if (one) _biomeBtn.SetText("Biome:  " + Core.WorldGen.BiomeNames[(int)Core.WorldGen.OnlyBiome(_newKind)] + "  ›");
            _modeBtn.SetText(_newCreative ? "Mode: Creative" : "Mode: Survival");
            _diffBtn.SetText("Difficulty: " + GameRoot.DifficultyNames[_newDifficulty]);
            _diffBtn.gameObject.SetActive(!_newCreative);
            _keepBtn.SetText(_newKeep ? "When you fall: keep your things" : "When you fall: things drop in a satchel");
            _keepBtn.gameObject.SetActive(!_newCreative && _newDifficulty != 4);
            var hint = _new.transform.Find("Card/ModeHint")?.GetComponent<Text>();
            if (hint != null) hint.text = Core.WorldGen.KindHints[(int)_newKind] + "\n" + (_newCreative ? "Every block in the palette, instant breaking, flight. Nothing can hurt you." :
                _newDifficulty == 0 ? "Gather, craft and build. No monsters, and you never go hungry." :
                _newDifficulty == 1 ? "Monsters come out at night but hit softly. Hunger won't take you below half health." :
                _newDifficulty == 3 ? "More monsters, and they hit half as hard again. Hunger can finish you." :
                _newDifficulty == 4 ? "Hard, with one life: fall once and the world can only be looked at, never played again." :
                "Monsters at night and in the dark. Keep fed, build a shelter, sleep in a bed.");
        }

        Image _micMeter; Button _micBtn; float _micRefresh;

        void SettingsUpdate()
        {
            if (_settings == null) return;
            if (_settings.activeSelf)
            {
                float lv = MicChoice.TestLevel();
                if (_micMeter != null) _micMeter.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(lv), 1);
                // A headset plugged in while the screen is open shows up.
                if ((_micRefresh -= Time.unscaledDeltaTime) <= 0) { _micRefresh = 1; _micBtn?.SetText("Microphone: " + MicChoice.Label()); }
            }
            else if (!VoiceChat.Active) MicChoice.StopTest();
        }

        Text _viewLabel, _fovLabel, _sensLabel, _volLabel, _musicLabel;

        void BuildSettings()
        {
            _settings = Screen("Settings");
            var card = Card(_settings, 1180, 1010, "Settings");
            float y = -120;
            void Row(string name, out Text label, float min, float max, float val, bool whole, Action<float> set, Func<float, string> fmt, float x)
            {
                var l = UIKit.Label(card.transform, name, 24, UIKit.Dim); l.rectTransform.Place(0, 1, x, y, 300, 30);
                var v = UIKit.Label(card.transform, fmt(val), 24, UIKit.Ink, TextAnchor.MiddleRight, FontStyle.Bold); v.rectTransform.Place(0, 1, x + 200, y, 300, 30);
                var lab = v;
                var s = UIKit.Slider(card.transform, min, max, val, whole, f => { set(f); lab.text = fmt(f); SettingsChanged?.Invoke(); });
                ((RectTransform)s.transform).Place(0, 1, x, y - 36, 500, 40);
                label = v;
            }
            Row("Render distance", out _viewLabel, 4, 20, Settings.ViewDistance, true, f => Settings.ViewDistance = (int)f, f => $"{(int)f} chunks", 70);
            Row("Field of view", out _fovLabel, 55, 110, Settings.Fov, true, f => Settings.Fov = f, f => $"{(int)f}°", 610);
            y -= 110;
            Row("Mouse sensitivity", out _sensLabel, 0.1f, 4, Settings.Sensitivity, false, f => Settings.Sensitivity = f, f => $"{f:0.00}×", 70);
            Row("Sound", out _volLabel, 0, 1, Settings.Volume, false, f => Settings.Volume = f, f => $"{Mathf.RoundToInt(f * 100)}%", 610);
            y -= 110;
            Row("Music", out _musicLabel, 0, 1, Settings.Music, false, f => Settings.Music = f, f => $"{Mathf.RoundToInt(f * 100)}%", 70);
            Row("Voice chat volume", out _, 0, 2, Settings.VoiceVolume, false, f => Settings.VoiceVolume = f, f => $"{Mathf.RoundToInt(f * 100)}%", 610);
            y -= 110;
            Row("Microphone boost", out _, 0.25f, 4, Settings.MicGain, false, f => Settings.MicGain = f, f => $"{f:0.0}×", 70);
            // Which microphone (a headset's), with a meter that moves when you speak into it.
            Button micBtn = null;
            micBtn = UIKit.Button(card.transform, "Microphone: " + MicChoice.Label(), () => { MicChoice.Next(); micBtn.SetText("Microphone: " + MicChoice.Label()); }, 22);
            ((RectTransform)micBtn.transform).Place(0, 1, 610, y + 4, 500, 52);
            _micBtn = micBtn;
            var meterBack = UIKit.Panel(card.transform, "MicMeter", new Color(0.05f, 0.05f, 0.06f, 0.8f), UIKit.RoundSmall);
            meterBack.rectTransform.Place(0, 1, 610, y - 58, 500, 16);
            _micMeter = UIKit.Panel(meterBack.transform, "Level", new Color(0.4f, 0.9f, 0.5f), UIKit.RoundSmall);
            _micMeter.rectTransform.anchorMin = Vector2.zero; _micMeter.rectTransform.anchorMax = new Vector2(0, 1);
            _micMeter.rectTransform.offsetMin = new Vector2(2, 2); _micMeter.rectTransform.offsetMax = new Vector2(-2, -2);
            var micHint = UIKit.Label(card.transform, "Talk: the bar should move. A headset stops echo.", 17, UIKit.Dim);
            micHint.rectTransform.Place(0, 1, 610, y - 76, 520, 26);
            y -= 120;
            float x = 70; int n = 0;
            void Toggle(string name, Func<string> state, Action flip)
            {
                Button b = null;
                b = UIKit.Button(card.transform, name + ": " + state(), () => { flip(); b.SetText(name + ": " + state()); SettingsChanged?.Invoke(); }, 24);
                ((RectTransform)b.transform).Place(0, 1, x + (n % 3) * 350, y - (n / 3) * 84, 330, 68);
                n++;
            }
            Toggle("Shadows", () => new[] { "Off", "Low", "High" }[Settings.Shadows], () => Settings.Shadows = (Settings.Shadows + 1) % 3);
            Toggle("V-Sync", () => Settings.VSync ? "On" : "Off", () => Settings.VSync = !Settings.VSync);
            Toggle("Fullscreen", () => Settings.Fullscreen ? "On" : "Off", () => Settings.Fullscreen = !Settings.Fullscreen);
            Toggle("Invert mouse", () => Settings.InvertY ? "On" : "Off", () => Settings.InvertY = !Settings.InvertY);
            Toggle("View bobbing", () => Settings.ViewBob ? "On" : "Off", () => Settings.ViewBob = !Settings.ViewBob);
            Toggle("Show FPS", () => Settings.ShowFps ? "On" : "Off", () => Settings.ShowFps = !Settings.ShowFps);
            Toggle("Voice chat", () => new[] { "Off", "Hold V", "Open mic" }[Settings.VoiceMode], () => Settings.VoiceMode = (Settings.VoiceMode + 1) % 3);
            var done = UIKit.Button(card.transform, "Done", () => { PlayerPrefs.Save(); _settingsBack?.Invoke(); }, 30, true);
            ((RectTransform)done.transform).Place(0.5f, 0, 180, 36, 340, 72);
            var ctl = UIKit.Button(card.transform, "Controls, size, captions", ShowControls, 26);
            ((RectTransform)ctl.transform).Place(0.5f, 0, -180, 36, 340, 72);
        }

        public Action ChangeWorld;                 // online, on a server with several worlds
        Button _worldBtn;
        Image _pauseCard;
        int _worldCount = 1;

        void BuildPause()
        {
            _pause = Screen("Pause");
            var card = _pauseCard = Card(_pause, 620, 500, "Paused");
            _pauseInfo = UIKit.Label(card.transform, "", 22, UIKit.Dim, TextAnchor.MiddleCenter);
            _pauseInfo.rectTransform.Place(0.5f, 1, 0, -92, 560, 30);
            float y = -150;
            foreach (var (label, act, primary) in new (string, Action, bool)[]
                     { ("Back to Game", () => Resume?.Invoke(), true), ("Settings", () => ShowSettings(ShowPause), false),
                       ("Change World", () => ChangeWorld?.Invoke(), false), ("Save and Quit to Title", () => SaveAndQuit?.Invoke(), false) })
            {
                var b = UIKit.Button(card.transform, label, act, 30, primary);
                ((RectTransform)b.transform).Place(0.5f, 1, 0, y, 460, 76);
                y -= 96;
                if (label == "Change World") _worldBtn = b;
                _quitBtn = b;
            }
            LayPause(false);
        }

        // The Change World button only shows online on a server with more than one world.
        void LayPause(bool worlds)
        {
            _worldBtn.gameObject.SetActive(worlds);
            ((RectTransform)_quitBtn.transform).anchoredPosition = new Vector2(0, worlds ? -438 : -342);
            _pauseCard.rectTransform.sizeDelta = new Vector2(620, worlds ? 596 : 500);
        }

        public void SetWorldCount(int n)
        {
            if (n == _worldCount) return;
            _worldCount = n;
            LayPause(_online && n > 1);
        }

        bool _online;

        void BuildDeath()
        {
            _death = Screen("Death");
            var t = UIKit.Label(_death.transform, "You died!", 96, new Color(1f, 0.78f, 0.72f), TextAnchor.MiddleCenter, FontStyle.Bold).Shadowed(5);
            t.rectTransform.Place(0.5f, 0.5f, 0, 120, 1400, 140);
            var s = _deathSub = UIKit.Label(_death.transform, "Everything you carried is in a satchel where you fell.", 30, UIKit.Ink, TextAnchor.MiddleCenter).Shadowed(2);
            s.rectTransform.Place(0.5f, 0.5f, 0, 30, 1400, 50);
            var b = UIKit.Button(_death.transform, "Respawn", () => Respawn?.Invoke(), 32, true);
            ((RectTransform)b.transform).Place(0.5f, 0.5f, 0, -80, 380, 80);
        }

        public void SetDeathText(string s) { if (_deathSub != null) _deathSub.text = s.Length > 0 ? s : "Respawn to carry on."; }

        void BuildLoading()
        {
            _loading = Screen("Loading");
            var bg = UIKit.Panel(_loading.transform, "Bg", new Color(0.04f, 0.045f, 0.06f, 1), UIKit.White); bg.rectTransform.Fill();
            var t = UIKit.Label(_loading.transform, "STRATUM", 120, new Color(1f, 0.93f, 0.80f), TextAnchor.MiddleCenter, FontStyle.Bold);
            t.rectTransform.Place(0.5f, 0.5f, 0, 160, 1200, 160);
            _loadText = UIKit.Label(_loading.transform, "Shaping the land", 32, UIKit.Ink, TextAnchor.MiddleCenter);
            _loadText.rectTransform.Place(0.5f, 0.5f, 0, 30, 1000, 50);
            var back = UIKit.Panel(_loading.transform, "BarBack", new Color(0.15f, 0.16f, 0.19f, 1), UIKit.RoundSmall);
            back.rectTransform.Place(0.5f, 0.5f, 0, -40, 700, 22);
            _loadBar = UIKit.Panel(back.transform, "Bar", UIKit.Accent, UIKit.RoundSmall);
            _loadBar.rectTransform.anchorMin = Vector2.zero; _loadBar.rectTransform.anchorMax = new Vector2(0, 1); _loadBar.rectTransform.offsetMin = _loadBar.rectTransform.offsetMax = Vector2.zero;
            _loadTip = UIKit.Label(_loading.transform, "", 26, UIKit.Dim, TextAnchor.MiddleCenter, FontStyle.Italic);
            _loadTip.rectTransform.Place(0.5f, 0, 0, 90, 1400, 40);
        }

        // ------------------------------------------------------------------ multiplayer

        // Pause and blackout read a little differently on a server.
        public void SetOnline(bool online, string info)
        {
            _quitBtn.SetText(online ? "Disconnect" : "Save and Quit to Title");
            _online = online;
            if (!online) _worldCount = 1;
            LayPause(online && _worldCount > 1);
            _pauseInfo.text = online ? info : "";
            _deathSub.text = online ? "You wake at the server's spawn, your pack still with you." : "Everything you carried is in a satchel where you fell.";
        }

        public void HideAll()
        {
            foreach (var g in new[] { _main, _worlds, _new, _settings, _pause, _death, _loading, _join, _backups, _extras, _controls, _maker }) g.SetActive(false);
            _shade.gameObject.SetActive(false);
        }

        void Show(GameObject g, bool shade)
        {
            HideAll();
            g.SetActive(true);
            _shade.gameObject.SetActive(shade);
            // UI blends in linear light, where 0.5 black only darkens the screen by about a quarter.
            _shade.color = g == _main ? new Color(0, 0, 0, 0.25f) : g == _death ? new Color(0.18f, 0.01f, 0.01f, 0.88f) : new Color(0, 0, 0, 0.72f);
        }

        public void ShowMain() => Show(_main, true);
        public void ShowWorlds() { Show(_worlds, true); FillWorlds(); }
        public void ShowNew()
        {
            Show(_new, true);
            int n = (ListWorlds?.Invoke()?.Count ?? 0) + 1;
            _newName.text = n == 1 ? "My World" : "World " + n;
            _newSeed.text = "";
        }
        public void ShowSettings(Action back) { _settingsBack = back; Show(_settings, true); }
        public void ShowPause() => Show(_pause, true);
        public void ShowDeath() => Show(_death, true);
        public void ShowLoading()
        {
            Show(_loading, false);
            _loadTip.text = Tips[UnityEngine.Random.Range(0, Tips.Length)];
            SetLoading(0, "Shaping the land");
        }
        public void SetLoading(float p, string text)
        {
            _loadBar.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(p), 1);
            if (text != null) _loadText.text = text;
        }
        public bool OnMainOrSub => _main.activeSelf || _worlds.activeSelf || _new.activeSelf || _join.activeSelf || _settings.activeSelf && _settingsBack == (Action)ShowMain;
        public void Back()
        {
            if (_maker != null && _maker.activeSelf) { CustomLook.SaveMine(); _extraTab = 2; ShowExtras(); }
            else if (_controls.activeSelf) { if (_binding >= 0) { _binding = -1; RefreshControls(); } else ShowSettings(_settingsBack); }
            else if (_extras.activeSelf || _backups.activeSelf) { if (_backups.activeSelf) ShowWorlds(); else ShowMain(); }
            else if (_settings.activeSelf) { PlayerPrefs.Save(); _settingsBack?.Invoke(); }
            else if (_new.activeSelf) ShowWorlds();
            else if (_worlds.activeSelf) ShowMain();
            else if (_join.activeSelf) { if (_joining) CancelJoin?.Invoke(); _joining = false; ShowMain(); }
            else if (_pause.activeSelf) Resume?.Invoke();
        }
    }
}
