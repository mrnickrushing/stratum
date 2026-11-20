using System.Collections.Generic;
using Stratum.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Stratum.Game
{
    // The enchanting table and the anvil: two slots in the crafting panel's place and what they can do with them.
    //  Enchanting: a tool, weapon, armour piece or book and sunstone shards; three offers, each costing levels
    //  (and that many shards) with a level you must have reached.
    //  Anvil: two of the same thing mend into one (keeping both sets of enchantments), an enchanted book puts its
    //  enchantments on, and the material it's made of mends a quarter each.
    public sealed partial class Hud
    {
        RectTransform _workGrid;
        readonly Stack[] _work = new Stack[2];
        readonly List<SlotView> _workSlots = new List<SlotView>();
        readonly List<Button> _offers = new List<Button>();
        Text _workInfo;
        int _enchSeed = System.Environment.TickCount & 0x7FFFFFF;
        string _workShown;
        bool WorkStation => _station == "enchant" || _station == "anvil" || _station == "trade";
        int _tradeJob, _tradeSeed;
        readonly List<(Trades.Offer o, Button b)> _tradeRows = new List<(Trades.Offer, Button)>();

        public void SetTrade(int job, int seed) { _tradeJob = job; _tradeSeed = seed; }

        static readonly int[] OfferNeeds = { 5, 15, 30 };

        void LayoutWork()
        {
            if (_workGrid == null) _workGrid = UIKit.Rect(_boxGrid.parent, "Work").Place(0, 1, 26, -110, 600, 660);
            foreach (Transform t in _workGrid) Destroy(t.gameObject);
            _workSlots.Clear(); _offers.Clear(); _workShown = null;
            _workGrid.gameObject.SetActive(WorkStation);
            _tradeRows.Clear();
            if (!WorkStation) return;
            if (_station == "trade") { LayoutTrade(); return; }
            bool ench = _station == "enchant";
            const float S = 88;
            string[] labels = ench ? new[] { "Item", "Sunstone" } : new[] { "Item", "Add" };
            for (int i = 0; i < 2; i++)
            {
                int idx = i;
                var v = SlotView.Make(_workGrid, S);
                v.GetComponent<RectTransform>().Place(0, 1, 20 + i * 130, -40, S, S);
                v.Click = b => WorkClick(idx, b);
                v.Hover = on => Tip(on ? _work[idx] : Stack.None);
                _workSlots.Add(v);
                var l = UIKit.Label(_workGrid, labels[i], 20, UIKit.Dim, TextAnchor.MiddleCenter);
                l.rectTransform.Place(0, 1, 20 + i * 130 - 10, -8, S + 20, 28);
            }
            _workInfo = UIKit.Label(_workGrid, "", 21, UIKit.Dim, TextAnchor.UpperLeft);
            _workInfo.rectTransform.Place(0, 1, 290, -40, 300, 120);
            _workInfo.supportRichText = true;
            int offers = ench ? 3 : 1;
            for (int i = 0; i < offers; i++)
            {
                int k = i;
                var b = UIKit.Button(_workGrid, "", () => { if (ench) Enchant(k); else Combine(); }, 21, true);
                ((RectTransform)b.transform).Place(0, 1, 0, -170 - i * 112, 600, 100);
                var t = b.GetComponentInChildren<Text>();
                t.alignment = TextAnchor.MiddleLeft; t.supportRichText = true;
                t.rectTransform.offsetMin = new Vector2(20, 0);
                _offers.Add(b);
            }
        }

#if UNITY_EDITOR
        public void DevWork(Stack a, Stack b) { _work[0] = a; _work[1] = b; _workShown = null; }
#endif

        void WorkClick(int i, PointerEventData.InputButton button)
        {
            if (!_cursor.Empty)
            {
                var d = Items.Get(_cursor.Item);
                if (_station == "enchant" && i == 1 && _cursor.Item != "sunstone_shard") { Toast("Sunstone shards go there"); return; }
                if (_station == "enchant" && i == 0 && !Enchants.CanEnchant(d)) { Toast(d.Enchants != null ? "That's enchanted already" : "That can't be enchanted"); return; }
            }
            CursorClick(ref _work[i], button);
            _workShown = null;
        }

        // Back into the pack when the station is closed.
        void ReturnWork()
        {
            for (int i = 0; i < _work.Length; i++)
            {
                if (_work[i].Empty) continue;
                var s = _work[i];
                int left = s.Wear > 0 || s.Item.Contains("@") ? Put(s) : P.Inv.Add(s.Item, s.Count);
                if (left > 0) Toast("Your pack is full");
                _work[i] = Stack.None;
            }
        }

        int Put(Stack s)
        {
            for (int k = 0; k < Inventory.Main; k++) if (P.Inv.Slots[k].Empty) { P.Inv.Slots[k] = s; return 0; }
            return s.Count;
        }

        void UpdateWork()
        {
            if (!WorkStation || _workGrid == null) return;
            if (_station == "trade")
            {
                foreach (var (o, b) in _tradeRows) b.interactable = CanTrade(o);
                return;
            }
            for (int i = 0; i < _workSlots.Count; i++) _workSlots[i].Show(_work[i]);
            string key = _work[0].Item + "|" + _work[0].Wear + "|" + _work[1].Item + "|" + _work[1].Count + "|" + P.XpLevel + "|" + _enchSeed;
            if (key == _workShown) return;
            _workShown = key;
            if (_station == "enchant") ShowOffers(); else ShowCombine();
        }

        // ------------------------------------------------------------------ trading

        void LayoutTrade()
        {
            var title = UIKit.Label(_workGrid, "The " + Trades.Jobs[Mathf.Clamp(_tradeJob, 0, Trades.Jobs.Length - 1)].ToLowerInvariant() + " will trade:", 22, UIKit.Dim);
            title.rectTransform.Place(0, 1, 0, -6, 600, 30);
            int row = 0;
            foreach (var o in Trades.For(_tradeJob, _tradeSeed))
            {
                var bar = UIKit.Panel(_workGrid, "Offer", new Color(0.16f, 0.17f, 0.21f, 0.95f), UIKit.RoundSmall);
                bar.rectTransform.Place(0, 1, 0, -44 - row * 86, 600, 78);
                float x = 12;
                void Thing(string item, int n)
                {
                    var ic = UIKit.Icon(bar.transform, Icons.Get(item)); ic.rectTransform.Place(0, 0.5f, x, 0, 56, 56);
                    var cnt = UIKit.Label(ic.transform, n > 1 ? n.ToString() : "", 20, Color.white, TextAnchor.LowerRight, FontStyle.Bold).Shadowed();
                    cnt.rectTransform.Fill(-2);
                    var tipRt = ic.gameObject.AddComponent<SlotHover>(); tipRt.Hover = on => Tip(on ? new Stack(item, n) : Stack.None);
                    x += 64;
                }
                Thing(o.In, o.InN);
                if (o.In2 != null) Thing(o.In2, o.In2N);
                var arrow = UIKit.Label(bar.transform, "→", 34, UIKit.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
                arrow.rectTransform.Place(0, 0.5f, x, 0, 40, 56); x += 48;
                Thing(o.Out, o.OutN);
                var oo = o;
                var b = UIKit.Button(bar.transform, "TRADE", () => Trade(oo), 22, true);
                ((RectTransform)b.transform).Place(1, 0.5f, -12, 0, 130, 54);
                _tradeRows.Add((o, b));
                row++;
            }
        }

        bool CanTrade(Trades.Offer o) => P.Inv.Count(o.In) >= o.InN && (o.In2 == null || P.Inv.Count(o.In2) >= o.In2N);

        void Trade(Trades.Offer o)
        {
            if (!CanTrade(o) || Items.Get(o.Out) == null) return;
            var copy = (Stack[])P.Inv.Slots.Clone();
            P.Inv.Take(o.In, o.InN);
            if (o.In2 != null) P.Inv.Take(o.In2, o.In2N);
            int left = o.Out.Contains("@") ? Put(new Stack(o.Out, 1)) : P.Inv.Add(o.Out, o.OutN);
            if (left > 0) { System.Array.Copy(copy, P.Inv.Slots, copy.Length); Toast("Your pack is full"); return; }
            P.AddXp(1 + Random.Range(0, 2));
            Sfx.I?.Play("trade", 0.6f);
        }

        // ------------------------------------------------------------------ enchanting

        Dictionary<char, int> Offer(int k, ItemDef d) => Enchants.Roll(d, OfferNeeds[k], _enchSeed + k * 7919 + d.Id.GetHashCode());

        void ShowOffers()
        {
            var d = Items.Get(_work[0].Item);
            _workInfo.text = $"Your level: <b>{P.XpLevel}</b>\nPut in something to enchant and sunstone shards to pay with.";
            for (int k = 0; k < _offers.Count; k++)
            {
                var t = _offers[k].GetComponentInChildren<Text>();
                if (d == null || !Enchants.CanEnchant(d)) { t.text = $"<b>{k + 1} level{(k > 0 ? "s" : "")}, {k + 1} shard{(k > 0 ? "s" : "")}</b>  ·  needs level {OfferNeeds[k]}"; _offers[k].interactable = false; continue; }
                var e = Offer(k, d);
                bool can = P.XpLevel >= OfferNeeds[k] && _work[1].Count >= k + 1 || P.Creative;
                string col = can ? "#FFFFFF" : "#8A8F99";
                t.text = $"<color={col}><b>{Enchants.Describe(e)}</b>\n{k + 1} level{(k > 0 ? "s" : "")} and {k + 1} shard{(k > 0 ? "s" : "")}  ·  needs level {OfferNeeds[k]}</color>";
                _offers[k].interactable = can && e.Count > 0;
            }
        }

        void Enchant(int k)
        {
            var d = Items.Get(_work[0].Item);
            if (d == null || !Enchants.CanEnchant(d)) return;
            if (!P.Creative && (P.XpLevel < OfferNeeds[k] || _work[1].Count < k + 1)) return;
            var e = Offer(k, d);
            if (e.Count == 0) return;
            string id = Enchants.Make(d.Id, e);
            if (Items.Get(id) == null) return;
            if (!P.Creative)
            {
                P.XpLevel -= k + 1;
                P.XpPoints = Mathf.Min(P.XpPoints, Xp.Next(P.XpLevel) - 1);
                _work[1].Count -= k + 1; if (_work[1].Count <= 0) _work[1] = Stack.None;
            }
            _work[0] = new Stack(id, 1) { Wear = _work[0].Wear };
            _enchSeed = Random.Range(1, int.MaxValue);
            Sfx.I?.Play("enchant", 0.7f);
            _workShown = null;
        }

        // ------------------------------------------------------------------ the anvil

        // What the two slots would make, and for how many levels.
        bool AnvilResult(out Stack result, out int cost, out string why)
        {
            result = Stack.None; cost = 0; why = "";
            var a = Items.Get(_work[0].Item); var b = Items.Get(_work[1].Item);
            if (a == null || b == null) { why = "Put a worn or plain tool in the first slot, and in the second the same tool, an enchanted book, or what it's made of."; return false; }
            var ea = a.Enchants != null ? new Dictionary<char, int>(a.Enchants) : new Dictionary<char, int>();
            string plainA = a.Base ?? a.Id, plainB = b.Base ?? b.Id;
            int wear = _work[0].Wear;
            if (plainA == plainB && a.Durability > 0)
            {
                // Two of a kind: one tool with what's left of both (and a bit), enchantments merged.
                int leftA = a.Durability - _work[0].Wear, leftB = b.Durability - _work[1].Wear;
                wear = Mathf.Max(0, a.Durability - (leftA + leftB + a.Durability * 12 / 100));
                cost = 2 + Merge(ea, b.Enchants, a);
            }
            else if (plainB == "book" && b.Enchants != null)
            {
                int added = Merge(ea, b.Enchants, a);
                if (added == 0) { why = "None of the book's enchantments fit that."; return false; }
                cost = 1 + added;
            }
            else if (a.Durability > 0 && Mends(plainA) == plainB)
            {
                if (_work[0].Wear == 0) { why = "That isn't worn."; return false; }
                int use = Mathf.Min(_work[1].Count, Mathf.CeilToInt(_work[0].Wear / (a.Durability / 4f)));
                wear = Mathf.Max(0, _work[0].Wear - use * a.Durability / 4);
                cost = use;
            }
            else { why = "Those don't go together."; return false; }
            cost += ea.Count;
            if (cost >= 40 && !P.Creative) { why = "Too costly: the anvil won't take it."; return false; }
            result = new Stack(Enchants.Make(plainA, ea), 1) { Wear = wear };
            if (Items.Get(result.Item) == null) { why = "Those don't go together."; return false; }
            return true;
        }

        // Adds b's enchantments to a's (the higher level, or one more when both match); returns how many levels came over.
        static int Merge(Dictionary<char, int> a, Dictionary<char, int> b, ItemDef target)
        {
            if (b == null) return 0;
            int n = 0;
            var plain = Items.Get(target.Base ?? target.Id);
            foreach (var kv in b)
            {
                var k = Enchants.Find(kv.Key);
                if (k == null || plain.Id != "book" && !k.Fits(plain)) continue;
                a.TryGetValue(kv.Key, out int have);
                int to = have == kv.Value ? Mathf.Min(k.Max, have + 1) : Mathf.Max(have, kv.Value);
                if (to > have) { n += to; a[kv.Key] = to; }
            }
            return n;
        }

        // The material that mends a tool: its ingot, planks or cobblestone.
        static string Mends(string id)
        {
            if (id.StartsWith("iron_")) return "iron_ingot";
            if (id.StartsWith("copper_")) return "copper_ingot";
            if (id.StartsWith("gold_")) return "gold_ingot";
            if (id.StartsWith("stone_")) return "cobble";
            if (id.StartsWith("wood_")) return "oak_planks";
            if (id.StartsWith("hide_") || id.StartsWith("leather_")) return "hide";
            return null;
        }

        void ShowCombine()
        {
            var t = _offers[0].GetComponentInChildren<Text>();
            if (AnvilResult(out var r, out int cost, out string why))
            {
                bool can = P.Creative || P.XpLevel >= cost;
                _workInfo.text = $"Your level: <b>{P.XpLevel}</b>";
                t.text = $"<b>{Items.Get(r.Item).Name}</b>\n<color={(can ? "#FFFFFF" : "#E06A5A")}>{cost} level{(cost == 1 ? "" : "s")}</color>";
                _offers[0].interactable = can;
            }
            else
            {
                _workInfo.text = $"Your level: <b>{P.XpLevel}</b>\n{why}";
                t.text = "<b>Combine</b>";
                _offers[0].interactable = false;
            }
        }

        void Combine()
        {
            if (!AnvilResult(out var r, out int cost, out _)) return;
            if (!P.Creative && P.XpLevel < cost) return;
            var a = Items.Get(_work[0].Item); var b = Items.Get(_work[1].Item);
            string plainA = a.Base ?? a.Id, plainB = b.Base ?? b.Id;
            int used = 1;
            if (a.Durability > 0 && Mends(plainA) == plainB && plainA != plainB)
                used = Mathf.Min(_work[1].Count, Mathf.CeilToInt(_work[0].Wear / (a.Durability / 4f)));
            if (!P.Creative) { P.XpLevel -= cost; P.XpPoints = Mathf.Min(P.XpPoints, Xp.Next(P.XpLevel) - 1); }
            _work[1].Count -= used; if (_work[1].Count <= 0) _work[1] = Stack.None;
            _work[0] = r;
            Sfx.I?.Play("anvil", 0.7f);
            _workShown = null;
        }
    }
}
