using System;
using System.Collections.Generic;

namespace Stratum.Core
{
    // A chute moves things between containers by itself: one at a time, it takes from the chest or furnace on top
    // of it and drops into the one underneath. Over a furnace it feeds the furnace (things that smelt go in to be
    // smelted, things that only burn become fuel); under a furnace it collects what is done. A chest over a chute
    // over a furnace over a chute over a chest smelts a chestful unattended.
    // A chute is a container with five slots of its own and a hidden mark so it can be found again after loading.
    public sealed class Chutes
    {
        public const int Slots = 5, Mark = 5;
        public const string MarkItem = "_chute";
        const float Every = 0.4f;                    // seconds between moves

        readonly Containers _boxes;
        readonly Func<int, int, int, byte> _get;
        readonly Func<int, int, bool> _loaded;
        readonly HashSet<long> _all = new HashSet<long>();
        readonly List<long> _order = new List<long>();
        float _acc;

        public Action<int, int, int, int, Stack> Changed;     // x, y, z, slot, what it holds now
        public Action<int, int, int> Fed;                     // a furnace was given something: wake it
        public int Count => _all.Count;

        public Chutes(Containers boxes, Func<int, int, int, byte> get, Func<int, int, bool> loaded) { _boxes = boxes; _get = get; _loaded = loaded; }

        // A chute was put down here.
        public void Add(int x, int y, int z)
        {
            var a = _boxes.At(x, y, z);
            if (a[Mark].Item != MarkItem) { a[Mark] = new Stack(MarkItem, 1); _boxes.Dirty = true; }
            _all.Add(Containers.Key(x, y, z));
        }

        public void LoadSaved()
        {
            foreach (var kv in _boxes.All)
                if (kv.Value.Length > Mark && kv.Value[Mark].Item == MarkItem) _all.Add(kv.Key);
        }

        public void Tick(float dt)
        {
            _acc += dt;
            int steps = 0;
            while (_acc >= Every && steps++ < 3) { _acc -= Every; Step(); }
            if (_acc > 2) _acc = 0;
        }

        // Which slots of a container a chute may put this item into from above (lo..hi), or false.
        static bool Into(byte block, string item, out int lo, out int hi)
        {
            lo = hi = -1;
            var d = Blocks.Get(block);
            if (!d.Container || block == Blocks.Satchel) return false;
            if (d.BaseId == Blocks.Furnace)
            {
                if (Smelting.Result(item, out _, out _, out _)) lo = hi = Smelting.In;
                else if (Smelting.FuelTicks(item) > 0) lo = hi = Smelting.Fuel;
                else return false;
                return true;
            }
            lo = 0; hi = block == Blocks.Chute ? Slots - 1 : Containers.ChestSlots - 1;
            return true;
        }

        // Which slots of a container a chute underneath may take from.
        static bool OutOf(byte block, out int lo, out int hi)
        {
            lo = hi = -1;
            var d = Blocks.Get(block);
            if (!d.Container || block == Blocks.Satchel || block == Blocks.Chute) return false;      // a chute above pushes for itself
            if (d.BaseId == Blocks.Furnace) { lo = hi = Smelting.Out; return true; }
            lo = 0; hi = Containers.ChestSlots - 1;
            return true;
        }

        void Set(int x, int y, int z, Stack[] a, int slot, Stack s)
        {
            a[slot] = s.Empty ? Stack.None : s;
            _boxes.Dirty = true;
            Changed?.Invoke(x, y, z, slot, a[slot]);
        }

        // One of `item` into slots lo..hi of a container: onto a stack of the same with room, else an empty slot.
        bool Insert(int x, int y, int z, Stack[] a, int lo, int hi, string item)
        {
            int max = Items.Get(item)?.MaxStack ?? 64, free = -1;
            for (int i = lo; i <= hi && i < a.Length; i++)
            {
                if (a[i].Empty) { if (free < 0) free = i; continue; }
                if (a[i].Item == item && a[i].Wear == 0 && a[i].Count < max) { Set(x, y, z, a, i, new Stack(item, a[i].Count + 1)); return true; }
            }
            if (free < 0) return false;
            Set(x, y, z, a, free, new Stack(item, 1));
            return true;
        }

        void Step()
        {
            if (_all.Count == 0) return;
            _order.Clear(); _order.AddRange(_all);
            foreach (long k in _order)
            {
                var (x, y, z) = Containers.Unkey(k);
                if (!_loaded(x, z)) continue;
                if (_get(x, y, z) != Blocks.Chute || !_boxes.TryGet(k, out var a)) { _all.Remove(k); continue; }

                // Down: the first thing it holds, into what's underneath.
                byte below = _get(x, y - 1, z);
                for (int i = 0; i < Slots; i++)
                {
                    var s = a[i];
                    if (s.Empty || s.Wear > 0 || Smelting.Hidden(s.Item)) continue;
                    if (!Into(below, s.Item, out int lo, out int hi)) continue;
                    var tb = _boxes.At(x, y - 1, z);
                    if (!Insert(x, y - 1, z, tb, lo, hi, s.Item)) continue;
                    Set(x, y, z, a, i, s.Count > 1 ? new Stack(s.Item, s.Count - 1) : Stack.None);
                    if (Blocks.Get(below).BaseId == Blocks.Furnace) Fed?.Invoke(x, y - 1, z);
                    else if (below == Blocks.Chute) Add(x, y - 1, z);
                    break;
                }

                // In: one thing from what's on top.
                byte above = _get(x, y + 1, z);
                if (OutOf(above, out int slo, out int shi) && _boxes.Has(x, y + 1, z))
                {
                    var sb = _boxes.At(x, y + 1, z);
                    for (int i = slo; i <= shi && i < sb.Length; i++)
                    {
                        var s = sb[i];
                        if (s.Empty || s.Wear > 0 || Smelting.Hidden(s.Item)) continue;
                        if (!Insert(x, y, z, a, 0, Slots - 1, s.Item)) continue;
                        Set(x, y + 1, z, sb, i, s.Count > 1 ? new Stack(s.Item, s.Count - 1) : Stack.None);
                        break;
                    }
                }
            }
        }
    }
}
