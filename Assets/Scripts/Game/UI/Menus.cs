using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Stratum.Game
{
    // Title screen, world list, new world, settings, pause, the blackout screen and the loading screen.
    public sealed class Menus : MonoBehaviour
    {
        public Action<WorldMeta> Play;
        public Action<string, string, bool> Create;   // name, seed text, creative
        public Action<WorldMeta> Delete;
        public Action Resume, SaveAndQuit, Respawn, Quit, SettingsChanged;
        public Func<List<WorldMeta>> ListWorlds;

        Canvas _canvas;
        RectTransform _root;
        GameObject _main, _worlds, _new, _settings, _pause, _death, _loading;
        RectTransform _worldList;
        Image _loadBar; Text _loadText, _loadTip;
        InputField _newName, _newSeed;
        bool _newCreative;
        Button _modeBtn;
        Action _settingsBack;
        Image _shade;

        public bool AnyOpen => _main.activeSelf || _worlds.activeSelf || _new.activeSelf || _settings.activeSelf || _pause.activeSelf || _death.activeSelf || _loading.activeSelf;
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

            BuildMain(); BuildWorlds(); BuildNew(); BuildSettings(); BuildPause(); BuildDeath(); BuildLoading();
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
                     { ("Play", ShowWorlds, true), ("Settings", () => ShowSettings(ShowMain), false), ("Quit", () => Quit?.Invoke(), false) })
            {
                var b = UIKit.Button(_main.transform, label, act, 34, primary);
                ((RectTransform)b.transform).Place(0, 0.5f, 148, y, 420, 76);
                y -= 92;
            }
            var foot = UIKit.Label(_main.transform, $"v{Application.version}   ·   Rushing Technologies", 22, new Color(1, 1, 1, 0.6f), TextAnchor.LowerLeft).Shadowed(2);
            foot.rectTransform.Place(0, 0, 40, 30, 800, 40);
            var keys = UIKit.Label(_main.transform, "WASD move · Space jump · Shift sprint · Ctrl sneak · E backpack · Esc pause", 22, new Color(1, 1, 1, 0.6f), TextAnchor.LowerRight).Shadowed(2);
            keys.rectTransform.Place(1, 0, -40, 30, 1100, 40);
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
                var name = UIKit.Label(row.transform, m.name, 32, UIKit.Ink, TextAnchor.UpperLeft, FontStyle.Bold);
                name.rectTransform.Place(0, 1, 26, -16, 560, 40);
                var when = new DateTime(Math.Max(m.lastPlayed, 1), DateTimeKind.Utc).ToLocalTime();
                var info = UIKit.Label(row.transform, $"{(m.creative ? "Creative" : "Survival")}  ·  day {m.day}  ·  {Math.Round(m.minutesPlayed)} min played  ·  last played {when:d MMM yyyy, HH:mm}", 20, UIKit.Dim, TextAnchor.UpperLeft);
                info.rectTransform.Place(0, 1, 26, -62, 640, 30);
                var mm = m;
                var play = UIKit.Button(row.transform, "Play", () => Play?.Invoke(mm), 26, true);
                ((RectTransform)play.transform).Place(1, 0.5f, -20, 0, 150, 64);
                Button del = null;
                bool armed = false;
                del = UIKit.Button(row.transform, "Delete", () =>
                {
                    if (!armed) { armed = true; del.SetText("Sure?"); return; }
                    Delete?.Invoke(mm); FillWorlds();
                }, 24);
                ((RectTransform)del.transform).Place(1, 0.5f, -186, 0, 140, 64);
            }
        }

        void BuildNew()
        {
            _new = Screen("NewWorld");
            var card = Card(_new, 900, 620, "New World");
            var l1 = UIKit.Label(card.transform, "Name", 24, UIKit.Dim); l1.rectTransform.Place(0, 1, 70, -120, 400, 30);
            _newName = UIKit.Field(card.transform, "New World", "New World"); ((RectTransform)_newName.transform).Place(0, 1, 70, -156, 760, 64);
            var l2 = UIKit.Label(card.transform, "Seed (leave blank for a random world)", 24, UIKit.Dim); l2.rectTransform.Place(0, 1, 70, -246, 760, 30);
            _newSeed = UIKit.Field(card.transform, "random", ""); ((RectTransform)_newSeed.transform).Place(0, 1, 70, -282, 760, 64);
            _modeBtn = UIKit.Button(card.transform, "Mode: Survival", () => { _newCreative = !_newCreative; UpdateMode(); }, 28);
            ((RectTransform)_modeBtn.transform).Place(0, 1, 70, -378, 760, 68);
            var hint = UIKit.Label(card.transform, "", 21, UIKit.Dim, TextAnchor.UpperLeft); hint.rectTransform.Place(0, 1, 74, -456, 760, 60);
            hint.name = "ModeHint";
            var create = UIKit.Button(card.transform, "Create World", () => Create?.Invoke(_newName.text, _newSeed.text, _newCreative), 30, true);
            ((RectTransform)create.transform).Place(0.5f, 0, 170, 36, 320, 72);
            var back = UIKit.Button(card.transform, "Back", ShowWorlds, 30);
            ((RectTransform)back.transform).Place(0.5f, 0, -170, 36, 320, 72);
            UpdateMode();
        }

        void UpdateMode()
        {
            _modeBtn.SetText(_newCreative ? "Mode: Creative" : "Mode: Survival");
            var hint = _new.transform.Find("Card/ModeHint")?.GetComponent<Text>();
            if (hint != null) hint.text = _newCreative ? "Every block in the palette, instant breaking, flight. Nothing can hurt you." :
                "Gather, craft tools and dig for ore. Falls and deep water hurt.";
        }

        Text _viewLabel, _fovLabel, _sensLabel, _volLabel, _musicLabel;

        void BuildSettings()
        {
            _settings = Screen("Settings");
            var card = Card(_settings, 1180, 860, "Settings");
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
            var done = UIKit.Button(card.transform, "Done", () => { PlayerPrefs.Save(); _settingsBack?.Invoke(); }, 30, true);
            ((RectTransform)done.transform).Place(0.5f, 0, 0, 36, 340, 72);
        }

        void BuildPause()
        {
            _pause = Screen("Pause");
            var card = Card(_pause, 620, 520, "Paused");
            float y = -130;
            foreach (var (label, act, primary) in new (string, Action, bool)[]
                     { ("Back to Game", () => Resume?.Invoke(), true), ("Settings", () => ShowSettings(ShowPause), false), ("Save and Quit to Title", () => SaveAndQuit?.Invoke(), false) })
            {
                var b = UIKit.Button(card.transform, label, act, 30, primary);
                ((RectTransform)b.transform).Place(0.5f, 1, 0, y, 460, 76);
                y -= 96;
            }
        }

        void BuildDeath()
        {
            _death = Screen("Death");
            var t = UIKit.Label(_death.transform, "You blacked out", 96, new Color(1f, 0.78f, 0.72f), TextAnchor.MiddleCenter, FontStyle.Bold).Shadowed(5);
            t.rectTransform.Place(0.5f, 0.5f, 0, 120, 1400, 140);
            var s = UIKit.Label(_death.transform, "You wake where you first came ashore, your pack still with you.", 30, UIKit.Ink, TextAnchor.MiddleCenter).Shadowed(2);
            s.rectTransform.Place(0.5f, 0.5f, 0, 30, 1400, 50);
            var b = UIKit.Button(_death.transform, "Get Up", () => Respawn?.Invoke(), 32, true);
            ((RectTransform)b.transform).Place(0.5f, 0.5f, 0, -80, 380, 80);
        }

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

        public void HideAll()
        {
            foreach (var g in new[] { _main, _worlds, _new, _settings, _pause, _death, _loading }) g.SetActive(false);
            _shade.gameObject.SetActive(false);
        }

        void Show(GameObject g, bool shade)
        {
            HideAll();
            g.SetActive(true);
            _shade.gameObject.SetActive(shade);
            _shade.color = new Color(0, 0, 0, g == _main ? 0.15f : 0.5f);
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
        public bool OnMainOrSub => _main.activeSelf || _worlds.activeSelf || _new.activeSelf || _settings.activeSelf && _settingsBack == (Action)ShowMain;
        public void Back()
        {
            if (_settings.activeSelf) { PlayerPrefs.Save(); _settingsBack?.Invoke(); }
            else if (_new.activeSelf) ShowWorlds();
            else if (_worlds.activeSelf) ShowMain();
            else if (_pause.activeSelf) Resume?.Invoke();
        }
    }
}
