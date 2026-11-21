using System.Collections.Generic;
using Stratum.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Stratum.Game
{
    // The crafting grid: two by two in the backpack, three by three at a workbench. Put one of each thing a recipe
    // needs in the squares (anywhere: the arrangement doesn't matter) and what it makes shows in the slot beside it.
    // Taking the result uses one from every square, so stacks in the squares make it again and again. The recipe
    // list beside it still works, and is the place to look up what something needs.
    public sealed partial class Hud
    {
        readonly Stack[] _grid = new Stack[9];
        readonly List<(SlotView v, int cell)> _gridSlots = new List<(SlotView, int)>();
        SlotView _gridOut; RectTransform _gridArea; Text _gridHint;
        Recipe _gridRecipe; int _gridN = 2; bool _gridDirty = true;

        bool GridBench => _station == "workbench" || Near(Blocks.Workbench, 4);

        void LayoutGrid()
        {
            if (_gridArea != null) Destroy(_gridArea.gameObject);
            _gridSlots.Clear(); _gridOut = null; _gridArea = null;
            if (P.Creative) return;
            _gridN = GridBench ? 3 : 2;
            // Anything left in squares the smaller grid doesn't have goes back to the pack.
            for (int i = 0; i < 9; i++) if (!InGrid(i) && !_grid[i].Empty) { GiveBack(_grid[i]); _grid[i] = Stack.None; }
            const float C = 58, G = 6;
            _gridArea = UIKit.Rect(_invGrid, "CraftGrid").Place(0, 1, 400, -432, 400, 250);
            var title = UIKit.Label(_gridArea, _gridN == 3 ? "Workbench grid" : "Crafting grid", 22, UIKit.Dim);
            title.rectTransform.Place(0, 1, 0, 0, 300, 30);
            float side = _gridN * C + (_gridN - 1) * G, top = -36 - (3 * C + 2 * G - side) / 2;
            for (int r = 0; r < _gridN; r++)
                for (int c = 0; c < _gridN; c++)
                {
                    int cell = r * 3 + c;
                    var v = SlotView.Make(_gridArea, C);
                    v.GetComponent<RectTransform>().Place(0, 1, c * (C + G), top - r * (C + G), C, C);
                    v.Click = b => { if (ClickIsDrag) return; CursorClick(ref _grid[cell], b); _gridDirty = true; };
                    v.Hover = on => { Tip(on ? _grid[cell] : Stack.None); DragOver(on, 1000 + cell, () => _grid[cell], st => { _grid[cell] = st; _gridDirty = true; }); };
                    _gridSlots.Add((v, cell));
                }
            float mid = -36 - (3 * C + 2 * G) / 2;
            var arrow = UIKit.Label(_gridArea, "›", 54, UIKit.Dim, TextAnchor.MiddleCenter, FontStyle.Bold);
            arrow.rectTransform.Place(0, 1, side + 8, mid + 34, 44, 68);
            _gridOut = SlotView.Make(_gridArea, 76);
            _gridOut.GetComponent<RectTransform>().Place(0, 1, side + 58, mid + 38, 76, 76);
            _gridOut.Click = TakeGrid;
            _gridOut.Hover = on => Tip(on && _gridRecipe != null ? new Stack(_gridRecipe.Out, _gridRecipe.Count) : Stack.None);
            _gridHint = UIKit.Label(_gridArea, "", 17, UIKit.Dim, TextAnchor.UpperLeft);
            _gridHint.rectTransform.Place(0, 1, 0, -36 - (3 * C + 2 * G) - 6, 400, 24);
            _gridDirty = true;
        }

        bool InGrid(int cell) => cell / 3 < _gridN && cell % 3 < _gridN;

        void UpdateGrid()
        {
            if (_gridArea == null) return;
            if (_gridDirty) { _gridDirty = false; _gridRecipe = MatchGrid(); }
            foreach (var (v, cell) in _gridSlots) v.Show(_grid[cell]);
            _gridOut.Show(_gridRecipe != null ? new Stack(_gridRecipe.Out, _gridRecipe.Count) : Stack.None);
            bool any = false; foreach (var s in _grid) if (!s.Empty) any = true;
            string hint = _gridRecipe != null ? "Click to take it. Shift-click makes all you can."
                        : any ? (_gridN == 2 ? "Nothing yet. Bigger recipes need a workbench." : "Nothing yet. Look it up in the list.")
                        : "Put one of each thing in a square.";
            if (_gridHint.text != hint) _gridHint.text = hint;
        }

        // The recipe the squares spell out (Core.Crafting: patterns for tools and building pieces, any squares for
        // the rest).
        Recipe MatchGrid() => Crafting.Match(_grid, _gridN);

        // The recipe list's "show me": the pack's things laid out in the grid the way the recipe wants them, ready
        // to take. Whatever was in the grid goes back to the pack first.
        public void FillGrid(Recipe r)
        {
            if (_gridArea == null) return;
            if (Crafting.Size(r) > _gridN) { Toast("That needs a workbench's bigger grid"); return; }
            if (!P.Inv.CanCraft(r)) { Toast("You don't have everything for that"); return; }
            ReturnGrid();
            var cells = Crafting.Layout(r);
            for (int i = 0; i < 9; i++)
            {
                if (cells[i] == null) continue;
                for (int k = 0; k < Inventory.Main; k++)
                {
                    ref var s = ref P.Inv.Slots[k];
                    if (s.Empty || s.Wear > 0 || !Items.Matches(cells[i], s.Item)) continue;
                    _grid[i] = new Stack(s.Item, 1);
                    s.Count--; if (s.Count <= 0) s = Stack.None;
                    break;
                }
            }
            _gridDirty = true;
            Sfx.I?.Play("click", 0.3f);
            RebuildCraftingSoon();
        }

        void UseGrid()
        {
            for (int i = 0; i < 9; i++)
                if (InGrid(i) && !_grid[i].Empty) { _grid[i].Count--; if (_grid[i].Count <= 0) _grid[i] = Stack.None; }
        }

        void TakeGrid(PointerEventData.InputButton button)
        {
            if (_gridRecipe == null) return;
            var kb = Keyboard.current;
            bool all = kb != null && kb.leftShiftKey.isPressed;
            int made = 0;
            if (all)
            {
                // Straight into the pack, as many as the squares and the pack allow.
                while (made < 64 && _gridRecipe != null)
                {
                    var r = _gridRecipe;
                    var copy = (Stack[])P.Inv.Slots.Clone();
                    if (P.Inv.Add(r.Out, r.Count) > 0) { System.Array.Copy(copy, P.Inv.Slots, Inventory.Size); if (made == 0) Toast("No room for it"); break; }
                    UseGrid(); made++;
                    _gridRecipe = MatchGrid();
                }
            }
            else
            {
                var r = _gridRecipe;
                int max = Items.Get(r.Out).MaxStack;
                if (_cursor.Empty) _cursor = new Stack(r.Out, r.Count);
                else if (_cursor.Item == r.Out && _cursor.Wear == 0 && _cursor.Count + r.Count <= max) _cursor.Count += r.Count;
                else return;
                UseGrid(); made = 1;
            }
            if (made > 0) Sfx.I?.Play("craft", 0.6f);
            _gridDirty = true; _tagFor = "";
            RebuildCraftingSoon();
        }

        // Back into the pack when the backpack closes.
        void ReturnGrid()
        {
            for (int i = 0; i < 9; i++) if (!_grid[i].Empty) { GiveBack(_grid[i]); _grid[i] = Stack.None; }
            _gridDirty = true;
        }

        public void DevGrid(int cell, Stack s) { _grid[cell] = s; _gridDirty = true; }
        public string DevGridOut() { _gridRecipe = MatchGrid(); return _gridRecipe == null ? "nothing" : _gridRecipe.Out + " x" + _gridRecipe.Count; }
        public void DevGridTake(bool all) { _gridRecipe = MatchGrid(); if (_gridRecipe == null) return; if (all) { while (_gridRecipe != null && P.Inv.Add(_gridRecipe.Out, _gridRecipe.Count) == 0) { UseGrid(); _gridRecipe = MatchGrid(); } } else TakeGrid(PointerEventData.InputButton.Left); }
    }
}
