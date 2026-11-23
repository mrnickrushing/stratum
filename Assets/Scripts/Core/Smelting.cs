using System;
using System.Collections.Generic;

namespace Stratum.Core
{
    // A furnace is a container with five slots: what goes in, the fuel, what comes out, and two hidden ones that
    // remember the fire ("_burn": ticks left, with the fuel's full burn in Wear) and the progress ("_cook": ticks
    // done). One tick is a tenth of a second. Fuel is only lit when there is something to smelt and room for it.
    public static class Smelting
    {
        public const int In = 0, Fuel = 1, Out = 2, Burn = 3, Cook = 4, Slots = 5;
        public const int Mark = 5;                       // "_furnace": this container is a furnace (so a loaded one can start by itself)
        public const string MarkItem = "_furnace";
        public const int CookTicks = 80;                 // eight seconds a piece
        public const string BurnItem = "_burn", CookItem = "_cook";

        // What an item smelts into (and how many of it one piece of work uses up).
        public static bool Result(string item, out string output, out int count, out int need)
        {
            output = null; count = 0; need = 1;
            if (item == null) return false;
            foreach (var r in Items.Recipes)
            {
                if (r.Station != "furnace" || r.In.Length == 0 || !Items.Matches(r.In[0].item, item)) continue;
                output = r.Out; count = r.Count; need = r.In[0].count;
                return true;
            }
            return false;
        }

        // How long a piece of fuel burns, in ticks (0: it isn't fuel). Coal smelts eight things.
        public static int FuelTicks(string item)
        {
            if (item == null) return 0;
            if (item == "coal") return CookTicks * 8;
            if (item.EndsWith("_log", StringComparison.Ordinal) || item.EndsWith("_planks", StringComparison.Ordinal)) return CookTicks * 3 / 2;
            if (item == "stick") return CookTicks / 2;
            return 0;
        }

        static bool CanWork(Stack[] a, out string output, out int count, out int need)
        {
            output = null; count = 0; need = 1;
            if (a[In].Empty || !Result(a[In].Item, out output, out count, out need) || a[In].Count < need) return false;
            if (a[Out].Empty) return true;
            return a[Out].Item == output && a[Out].Wear == 0 && a[Out].Count + count <= (Items.Get(output)?.MaxStack ?? 64);
        }

        public static bool Burning(Stack[] a) => a.Length >= Slots && a[Burn].Item == BurnItem && a[Burn].Count > 0;

        // Whether a furnace has anything to do on the next tick (burning, cooling, or able to light).
        public static bool Busy(Stack[] a)
        {
            if (a.Length < Slots) return false;
            if (Burning(a) || !a[Cook].Empty) return true;
            return CanWork(a, out _, out _, out _) && !a[Fuel].Empty && FuelTicks(a[Fuel].Item) > 0;
        }

        // One tick. Returns the slots that changed, as bits.
        public static int Step(Stack[] a)
        {
            if (a.Length < Slots) return 0;
            int changed = 0;
            int burn = Burning(a) ? a[Burn].Count : 0, full = burn > 0 ? a[Burn].Wear : 0;
            int cook = a[Cook].Item == CookItem ? a[Cook].Count : 0;
            int burn0 = burn, cook0 = cook;
            bool can = CanWork(a, out var output, out int count, out int need);
            // Progress belongs to the thing it was made on: something else put in starts from nothing.
            // (Something that can be smelted but has no room to come out is still the same thing: its progress waits.)
            bool waiting = !can && !a[In].Empty && Result(a[In].Item, out _, out _, out int needs) && a[In].Count >= needs;
            int tag = can || waiting ? Tag(a[In].Item) : 0, tag0 = a[Cook].Item == CookItem ? a[Cook].Wear : 0;
            if (cook > 0 && tag0 != tag) cook = 0;
            if (burn == 0 && can && !a[Fuel].Empty)
            {
                int t = FuelTicks(a[Fuel].Item);
                if (t > 0)
                {
                    burn = full = t;
                    a[Fuel].Count--; if (a[Fuel].Count <= 0) a[Fuel] = Stack.None;
                    changed |= 1 << Fuel;
                }
            }
            if (burn > 0)
            {
                burn--;
                if (can)
                {
                    if (++cook >= CookTicks)
                    {
                        cook = 0;
                        a[In].Count -= need; if (a[In].Count <= 0) a[In] = Stack.None;
                        if (a[Out].Empty) a[Out] = new Stack(output, count); else a[Out].Count += count;
                        changed |= 1 << In | 1 << Out;
                    }
                }
                else if (!waiting) cook = 0;
            }
            else if (!waiting) cook = Math.Max(0, cook - 2);        // the fire is out: what was half done cools off
            if (burn != burn0) { a[Burn] = burn > 0 ? new Stack(BurnItem, burn) { Wear = full } : Stack.None; changed |= 1 << Burn; }
            if (cook != cook0 || cook > 0 && tag0 != tag) { a[Cook] = cook > 0 ? new Stack(CookItem, cook) { Wear = tag } : Stack.None; changed |= 1 << Cook; }
            return changed;
        }

        // A short number for an item's name (never 0), kept with the progress so it can't pass to another recipe.
        static int Tag(string item)
        {
            uint h = 2166136261;
            foreach (char ch in item) h = (h ^ ch) * 16777619;
            return (int)(h % 65535) + 1;
        }

        // A hidden slot's marker: never handed to a player.
        public static bool Hidden(string item) => item != null && item.Length > 0 && item[0] == '_';
    }

    // Keeps every furnace with something to do going, whether or not anyone is near it.
    public sealed class Furnaces
    {
        readonly Containers _boxes;
        readonly HashSet<long> _active = new HashSet<long>();
        readonly List<long> _done = new List<long>();
        float _acc;
        public Action<int, int, int, int, Stack> Changed;       // x, y, z, slot, what it holds now
        public int Active => _active.Count;

        public Furnaces(Containers boxes) { _boxes = boxes; }

        public void Wake(int x, int y, int z)
        {
            var a = _boxes.At(x, y, z);
            if (a.Length > Smelting.Mark && a[Smelting.Mark].Item != Smelting.MarkItem) { a[Smelting.Mark] = new Stack(Smelting.MarkItem, 1); _boxes.Dirty = true; }
            _active.Add(Containers.Key(x, y, z));
        }

        // After loading: every furnace that was burning or part-way through carries on.
        public void WakeSaved()
        {
            foreach (var kv in _boxes.All)
            {
                var a = kv.Value;
                if (a.Length < Smelting.Slots) continue;
                bool going = a[Smelting.Burn].Item == Smelting.BurnItem || a[Smelting.Cook].Item == Smelting.CookItem;
                // Loaded and fuelled but not yet lit when it was saved: it starts now. Only containers known to be
                // furnaces, or a chest with ore above coal would start smelting.
                bool ready = a.Length > Smelting.Mark && a[Smelting.Mark].Item == Smelting.MarkItem && Smelting.Busy(a);
                if (going || ready) _active.Add(kv.Key);
            }
        }

        public void Tick(float dt)
        {
            _acc += dt;
            int steps = 0;
            while (_acc >= 0.1f && steps++ < 5) { _acc -= 0.1f; Step(); }
            if (_acc > 1) _acc = 0;
        }

        void Step()
        {
            if (_active.Count == 0) return;
            _done.Clear();
            foreach (long k in _active)
            {
                if (!_boxes.TryGet(k, out var a) || !Smelting.Busy(a)) { _done.Add(k); continue; }
                int changed = Smelting.Step(a);
                if (changed == 0) continue;
                _boxes.Dirty = true;
                if (Changed == null) continue;
                var (x, y, z) = Containers.Unkey(k);
                for (int s = 0; s < Smelting.Slots; s++) if ((changed & 1 << s) != 0) Changed(x, y, z, s, a[s]);
            }
            foreach (long k in _done) _active.Remove(k);
        }
    }
}
