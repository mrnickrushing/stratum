using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Stratum.Game
{
    // Settings > Controls: change a key (click it, then press the new one), make everything on screen bigger or
    // smaller, and turn on captions for sounds.
    public sealed partial class Menus
    {
        GameObject _controls; readonly List<Button> _keyBtns = new List<Button>(); int _binding = -1; Text _controlsNote;

        void BuildControls()
        {
            _controls = Screen("Controls");
            var card = Card(_controls, 1180, 990, "Controls");
            _controlsNote = UIKit.Label(card.transform, "", 22, UIKit.Dim, TextAnchor.UpperCenter);
            _controlsNote.rectTransform.Place(0.5f, 1, 0, -92, 1000, 30);
            int n = Keys.Names.Length;
            for (int i = 0; i < n; i++)
            {
                int idx = i; int col = i % 2, row = i / 2;
                var l = UIKit.Label(card.transform, Keys.Names[i], 26, UIKit.Ink, TextAnchor.MiddleLeft);
                l.rectTransform.Place(0, 1, 70 + col * 530, -150 - row * 84, 270, 64);
                var b = UIKit.Button(card.transform, "", () => { _binding = idx; RefreshControls(); }, 26);
                ((RectTransform)b.transform).Place(0, 1, 340 + col * 530, -150 - row * 84, 230, 64);
                _keyBtns.Add(b);
            }
            float y = -150 - (n + 1) / 2 * 84 - 16;
            var reset = UIKit.Button(card.transform, "Put the keys back", () => { Keys.Reset(); _binding = -1; RefreshControls(); }, 24);
            ((RectTransform)reset.transform).Place(0, 1, 70, y, 330, 60);
            var fixedKeys = UIKit.Label(card.transform, "Always the same: mouse to look, left click to dig or hit, right click to place or use,\n1 to 9 pick a hotbar slot, Esc pauses, F1 hides the screen, F2 takes a picture, F3 and F4 show how the game is running.", 19, UIKit.Dim, TextAnchor.UpperLeft);
            fixedKeys.rectTransform.Place(0, 1, 70, y - 76, 1040, 60);
            y -= 160;
            var sl = UIKit.Label(card.transform, "Size of menus and text", 24, UIKit.Dim); sl.rectTransform.Place(0, 1, 70, y, 400, 30);
            var sv = UIKit.Label(card.transform, "", 24, UIKit.Ink, TextAnchor.MiddleRight); sv.rectTransform.Place(0, 1, 420, y, 150, 30);
            sv.text = Mathf.RoundToInt(Settings.UiScale * 100) + "%";
            // The size changes when the slider is let go, or the slider would slide out from under the pointer.
            float pending = Settings.UiScale;
            var s = UIKit.Slider(card.transform, 0.8f, 1.3f, Settings.UiScale, false, f => { pending = Mathf.Round(f * 20) / 20f; sv.text = Mathf.RoundToInt(pending * 100) + "%"; });
            ((RectTransform)s.transform).Place(0, 1, 70, y - 36, 500, 36);
            var apply = UIKit.Button(card.transform, "Use this size", () => { Settings.UiScale = pending; SettingsChanged?.Invoke(); }, 24);
            ((RectTransform)apply.transform).Place(0, 1, 600, y - 24, 240, 60);
            Button subs = null;
            subs = UIKit.Button(card.transform, "", () => { Settings.Subtitles = !Settings.Subtitles; subs.SetText("Sound captions: " + (Settings.Subtitles ? "On" : "Off")); SettingsChanged?.Invoke(); }, 24);
            subs.SetText("Sound captions: " + (Settings.Subtitles ? "On" : "Off"));
            ((RectTransform)subs.transform).Place(0, 1, 870, y - 24, 260, 60);
            var done = UIKit.Button(card.transform, "Done", () => { _binding = -1; PlayerPrefs.Save(); ShowSettings(_settingsBack); }, 30, true);
            ((RectTransform)done.transform).Place(0.5f, 0, 0, 36, 340, 72);
        }

        public void ShowControls() { _binding = -1; Show(_controls, true); RefreshControls(); }

        void RefreshControls()
        {
            for (int i = 0; i < _keyBtns.Count; i++)
                _keyBtns[i].SetText(i == _binding ? "press a key..." : Keys.Label((Act)i));
            _controlsNote.text = _binding >= 0 ? $"Press the key for \"{Keys.Names[_binding]}\" (Esc to leave it as it is)" : "Click a key to change it.";
        }

        // Called every frame while the Controls screen is up.
        void ControlsUpdate()
        {
            if (_controls == null || !_controls.activeSelf || _binding < 0) return;
            var k = Keys.Captured();            // (Esc backs out: Menus.Back)
            if (k == Key.None) return;
            // A key already in use swaps with the one being changed, so no two actions share a key.
            for (int i = 0; i < Keys.Names.Length; i++)
                if (i != _binding && Keys.Get((Act)i) == k) Keys.Set((Act)i, Keys.Get((Act)_binding));
            Keys.Set((Act)_binding, k);
            _binding = -1;
            RefreshControls();
        }

        public bool Binding => _binding >= 0;
    }
}
