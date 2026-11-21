using Stratum.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Stratum.Game
{
    // The furnace screen: what to smelt on top, fuel under it, what's done on the right. The flame shows how much
    // of the burning fuel is left and the bar how far along the piece is. It keeps working with the screen closed.
    public sealed partial class Hud
    {
        Image _flame, _cookBar; Text _furnaceNote;

        void LayoutFurnace()
        {
            _boxGrid.sizeDelta = new Vector2(600, 420);
            const float S = 84;
            SlotView Slot(int idx, float x, float y, string label)
            {
                var l = UIKit.Label(_boxGrid, label, 19, UIKit.Dim, TextAnchor.LowerCenter);
                l.rectTransform.Place(0, 1, x - 30, y + 28, S + 60, 26);
                var v = SlotView.Make(_boxGrid, S);
                v.GetComponent<RectTransform>().Place(0, 1, x, y, S, S);
                v.Click = b => BoxClick(idx, b);
                v.Hover = on => Tip(on && _box != null ? _box[idx] : Stack.None);
                _boxSlots.Add(v);
                return v;
            }
            Slot(Smelting.In, 110, -40, "To smelt");
            Slot(Smelting.Fuel, 110, -236, "Fuel");
            Slot(Smelting.Out, 400, -138, "Done");
            // The flame between them: a dark well that fills from the bottom while fuel burns.
            var well = UIKit.Panel(_boxGrid, "FlameWell", new Color(0.05f, 0.05f, 0.06f, 0.9f), UIKit.RoundSmall);
            well.rectTransform.Place(0, 1, 138, -136, 28, 58); well.raycastTarget = false;
            _flame = UIKit.Panel(well.transform, "Flame", new Color(1f, 0.55f, 0.12f), UIKit.RoundSmall);
            _flame.rectTransform.anchorMin = Vector2.zero; _flame.rectTransform.anchorMax = new Vector2(1, 0); _flame.rectTransform.offsetMin = new Vector2(3, 3); _flame.rectTransform.offsetMax = new Vector2(-3, 3);
            _flame.raycastTarget = false;
            // The progress bar toward the result.
            var track = UIKit.Panel(_boxGrid, "CookTrack", new Color(0.05f, 0.05f, 0.06f, 0.9f), UIKit.RoundSmall);
            track.rectTransform.Place(0, 1, 222, -170, 150, 20); track.raycastTarget = false;
            _cookBar = UIKit.Panel(track.transform, "Cook", UIKit.Accent, UIKit.RoundSmall);
            _cookBar.rectTransform.anchorMin = Vector2.zero; _cookBar.rectTransform.anchorMax = new Vector2(0, 1); _cookBar.rectTransform.offsetMin = new Vector2(3, 3); _cookBar.rectTransform.offsetMax = new Vector2(3, -3);
            _cookBar.raycastTarget = false;
            _furnaceNote = UIKit.Label(_boxGrid, "", 20, UIKit.Dim, TextAnchor.UpperLeft);
            _furnaceNote.rectTransform.Place(0, 1, 20, -340, 560, 80);
            var hint = UIKit.Label(_boxGrid, "Coal smelts 8 things, a log or planks 1½, a stick ½.\nIt keeps working while you're away. Shift-click sends things to the right slot.", 18, UIKit.Dim * 0.9f, TextAnchor.UpperLeft);
            hint.rectTransform.Place(0, 1, 20, -384, 560, 60);
        }

        void UpdateFurnace()
        {
            if (_station != "smelt" || _box == null || _flame == null || _box.Length < Smelting.Slots) return;
            var burn = _box[Smelting.Burn]; var cook = _box[Smelting.Cook];
            float fire = burn.Item == Smelting.BurnItem && burn.Wear > 0 ? Mathf.Clamp01(burn.Count / (float)burn.Wear) : 0;
            float done = cook.Item == Smelting.CookItem ? Mathf.Clamp01(cook.Count / (float)Smelting.CookTicks) : 0;
            _flame.rectTransform.anchorMax = new Vector2(1, fire);
            _flame.rectTransform.offsetMax = new Vector2(-3, fire > 0 ? -3 : 3);
            _flame.enabled = fire > 0;
            _cookBar.rectTransform.anchorMax = new Vector2(done, 1);
            _cookBar.rectTransform.offsetMax = new Vector2(done > 0 ? -3 : 3, -3);
            _cookBar.enabled = done > 0;
            string note;
            var input = _box[Smelting.In];
            if (input.Empty) note = "Put ore, sand, clay, cobblestone, raw meat or a log in the top slot.";
            else if (!Smelting.Result(input.Item, out var o, out int cnt, out int need)) note = $"{Items.Get(input.Item)?.Name} can't be smelted.";
            else if (input.Count < need) note = $"That takes {need} {Items.Get(input.Item)?.Name} at a time.";
            else if (!_box[Smelting.Out].Empty && _box[Smelting.Out].Item != o) note = "Take what's done first: there's no room for the next thing.";
            else if (!_box[Smelting.Out].Empty && _box[Smelting.Out].Count + cnt > (Items.Get(o)?.MaxStack ?? 64)) note = "The result slot is full. Take some out and it carries on.";
            else if (fire <= 0 && _box[Smelting.Fuel].Empty) note = "It needs fuel: coal, a log, planks or sticks.";
            else if (fire <= 0 && Smelting.FuelTicks(_box[Smelting.Fuel].Item) == 0) note = $"{Items.Get(_box[Smelting.Fuel].Item)?.Name} doesn't burn.";
            else note = $"Making {Items.Get(o)?.Name}...";
            if (_furnaceNote.text != note) _furnaceNote.text = note;
        }

        // What a click on a furnace slot may do. False: nothing happens.
        bool FurnaceClick(int i, bool shift)
        {
            if (i == Smelting.Out)
            {
                // Only taking, and all of it at once; smelting is worth a little experience.
                var s = _box[i];
                if (s.Empty) return false;
                if (shift) { int left = P.Inv.Add(s.Item, s.Count); P.AddXp((s.Count - left + Random.Range(0, 2)) / 2); _box[i] = left > 0 ? new Stack(s.Item, left) : Stack.None; }
                else if (_cursor.Empty) { _cursor = s; _box[i] = Stack.None; P.AddXp((s.Count + Random.Range(0, 2)) / 2); }
                else if (_cursor.Item == s.Item && _cursor.Wear == 0 && _cursor.Count + s.Count <= Items.Get(s.Item).MaxStack) { _cursor.Count += s.Count; _box[i] = Stack.None; P.AddXp((s.Count + Random.Range(0, 2)) / 2); }
                else return false;
                Sfx.I?.Play("click", 0.3f);
                Link?.BoxSlot(_boxAt, i, _box[i]);
                _tagFor = "";
                return false;
            }
            if (!_cursor.Empty && i == Smelting.Fuel && Smelting.FuelTicks(_cursor.Item) == 0) { Toast("That doesn't burn"); return false; }
            if (!_cursor.Empty && i == Smelting.In && !Smelting.Result(_cursor.Item, out _, out _, out _)) { Toast("That can't be smelted"); return false; }
            return true;
        }

        // Shift-click in the pack with a furnace open: fuel to the fuel slot, anything smeltable to the top.
        bool ToFurnace(int i)
        {
            ref var slot = ref P.Inv.Slots[i];
            var s = slot;
            if (s.Empty || s.Wear > 0) return false;
            bool smelts = Smelting.Result(s.Item, out _, out _, out _), burns = Smelting.FuelTicks(s.Item) > 0;
            // A log is both: it goes on top unless that's taken by something else and the fuel slot is free.
            int to = smelts && (_box[Smelting.In].Empty || _box[Smelting.In].Item == s.Item) ? Smelting.In
                   : burns && (_box[Smelting.Fuel].Empty || _box[Smelting.Fuel].Item == s.Item) ? Smelting.Fuel : -1;
            if (to < 0) { if (!smelts && !burns) Toast("That can't be smelted or burnt"); return true; }
            int max = Items.Get(s.Item).MaxStack, have = _box[to].Empty ? 0 : _box[to].Count, n = Mathf.Min(max - have, s.Count);
            if (n <= 0) return true;
            _box[to] = new Stack(s.Item, have + n);
            s.Count -= n; slot = s.Count > 0 ? s : Stack.None;
            Link?.BoxSlot(_boxAt, to, _box[to]);
            Sfx.I?.Play("click", 0.3f);
            return true;
        }
    }
}
