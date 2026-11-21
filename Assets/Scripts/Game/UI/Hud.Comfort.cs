using System;
using System.Collections.Generic;
using Stratum.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Stratum.Game
{
    // Sound captions, the creative palette's search box, and dragging a held stack across several slots.
    public sealed partial class Hud
    {
        // ------------------------------------------------------------------ captions
        readonly List<(string text, float until, int side)> _captions = new List<(string, float, int)>();
        Image _capBack; Text _capText;

        // A sound worth knowing about, and where it came from (so it can say which side).
        public void Caption(string text, Vector3? at = null)
        {
            if (!Settings.Subtitles || string.IsNullOrEmpty(text)) return;
            int side = 0;
            if (at != null)
            {
                var to = at.Value - P.Feet; to.y = 0;
                if (to.sqrMagnitude > 4)
                {
                    float yaw = P.Yaw * Mathf.Deg2Rad;
                    var right = new Vector3(Mathf.Cos(yaw), 0, -Mathf.Sin(yaw));
                    float d = Vector3.Dot(right, to.normalized);
                    side = d > 0.35f ? 1 : d < -0.35f ? -1 : 0;
                }
            }
            for (int i = 0; i < _captions.Count; i++)
                if (_captions[i].text == text) { _captions[i] = (text, Time.unscaledTime + 3, side); return; }
            if (_captions.Count >= 4) _captions.RemoveAt(0);
            _captions.Add((text, Time.unscaledTime + 3, side));
        }

        void UpdateCaptions(bool show)
        {
            if (_capBack == null)
            {
                _capBack = UIKit.Panel(_root, "Captions", new Color(0.04f, 0.04f, 0.05f, 0.78f), UIKit.RoundSmall);
                _capBack.raycastTarget = false;
                _capBack.rectTransform.Place(1, 0, -24, 150, 340, 40);
                _capText = UIKit.Label(_capBack.transform, "", 21, Color.white, TextAnchor.MiddleCenter);
                _capText.raycastTarget = false; _capText.rectTransform.Fill(8);
            }
            _captions.RemoveAll(c => Time.unscaledTime > c.until);
            bool any = show && Settings.Subtitles && _captions.Count > 0;
            if (_capBack.gameObject.activeSelf != any) _capBack.gameObject.SetActive(any);
            if (!any) return;
            var sb = new System.Text.StringBuilder();
            foreach (var (text, _, side) in _captions) { if (sb.Length > 0) sb.Append('\n'); sb.Append(side < 0 ? "‹ " : "").Append(text).Append(side > 0 ? " ›" : ""); }
            string t = sb.ToString();
            if (_capText.text != t) { _capText.text = t; _capBack.rectTransform.sizeDelta = new Vector2(340, 16 + 28 * _captions.Count); }
        }

        // ------------------------------------------------------------------ creative palette search
        InputField _paletteSearch; string _paletteFilter = "";

        // True while a text box in the backpack has the keyboard, so typing "e" doesn't close the backpack.
        public bool Typing => _paletteSearch != null && _paletteSearch.isFocused;

        void FillPalette(float S, float G)
        {
            if (_paletteSearch == null)
            {
                _paletteSearch = UIKit.Field(_palette.parent, "Search the palette...", "", 24);
                ((RectTransform)_paletteSearch.transform).Place(0, 1, 44, -96, 792, 50);
                _paletteSearch.onValueChanged.AddListener(v => { _paletteFilter = (v ?? "").Trim(); foreach (Transform t in _palette) Destroy(t.gameObject); FillPalette(80, 8); });
            }
            _paletteSearch.gameObject.SetActive(true);
            _palette.Place(0, 1, 44, -156, 800, 460);
            var sc = UIKit.Scroll(_palette, out var content);
            ((RectTransform)sc.transform).Fill();
            var grid = content.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(S, S); grid.spacing = new Vector2(G, G); grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = 9;
            int shown = 0;
            foreach (var it in Items.Order)
            {
                if (!it.Creative) continue;
                if (_paletteFilter != "" && it.Name.IndexOf(_paletteFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                shown++;
                var v = SlotView.Make(content, S);
                var st = new Stack(it.Id, 1);
                v.Show(st);
                v.Click = b => { _cursor = _cursor.Empty ? new Stack(it.Id, b == PointerEventData.InputButton.Right ? 1 : it.MaxStack) : Stack.None; Sfx.I?.Play("click", 0.3f); };
                v.Hover = on => Tip(on ? st : Stack.None);
            }
            if (shown == 0)
            {
                var none = UIKit.Label(_palette, $"Nothing called \"{_paletteFilter}\".", 24, UIKit.Dim, TextAnchor.UpperCenter);
                none.rectTransform.Place(0.5f, 1, 0, -30, 700, 40);
            }
        }

        // ------------------------------------------------------------------ dragging a stack across slots
        // Hold the left button with a stack on the pointer and sweep over several slots: the stack is shared out
        // evenly between them (what's left over stays on the pointer).
        readonly List<(int id, Func<Stack> get, Action<Stack> set)> _drag = new List<(int, Func<Stack>, Action<Stack>)>();
        (int id, Func<Stack> get, Action<Stack> set) _over; bool _hasOver, _dragging; float _swallowClicks;

        void DragOver(bool on, int id, Func<Stack> get, Action<Stack> set)
        {
            if (!on) { if (_hasOver && _over.id == id) _hasOver = false; return; }
            _over = (id, get, set); _hasOver = true;
            if (_dragging) DragAdd();
        }

        void DragAdd()
        {
            if (!_hasOver || _cursor.Empty) return;
            foreach (var d in _drag) if (d.id == _over.id) return;
            var s = _over.get();
            int max = Items.Get(_cursor.Item)?.MaxStack ?? 64;
            if (s.Empty || s.Item == _cursor.Item && s.Wear == 0 && _cursor.Wear == 0 && s.Count < max) _drag.Add(_over);
        }

        // A click during or just after a sweep belongs to the sweep.
        bool ClickIsDrag => _dragging && _drag.Count >= 2 || Time.unscaledTime < _swallowClicks;

        void UpdateDrag()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            if (mouse.leftButton.wasPressedThisFrame && !_cursor.Empty && _cursor.Wear == 0) { _dragging = true; _drag.Clear(); DragAdd(); }
            if (!mouse.leftButton.isPressed && _dragging)
            {
                _dragging = false;
                if (_drag.Count >= 2 && !_cursor.Empty)
                {
                    int max = Items.Get(_cursor.Item)?.MaxStack ?? 64, each = Mathf.Max(1, _cursor.Count / _drag.Count);
                    foreach (var d in _drag)
                    {
                        if (_cursor.Empty) break;
                        var s = d.get();
                        int have = s.Empty ? 0 : s.Count, n = Mathf.Min(each, Mathf.Min(max - have, _cursor.Count));
                        if (n <= 0) continue;
                        d.set(new Stack(_cursor.Item, have + n));
                        _cursor.Count -= n; if (_cursor.Count <= 0) _cursor = Stack.None;
                    }
                    _swallowClicks = Time.unscaledTime + 0.08f;
                    _gridDirty = true; _tagFor = "";
                    Sfx.I?.Play("click", 0.3f);
                    RebuildCraftingSoon();
                }
                _drag.Clear();
            }
        }
    }
}
