using System;

namespace Stratum.Core
{
    // What spark machines do that needs more than blocks: a dispenser throwing something out of its store.
    public static class Machines
    {
        static readonly int[] DX = { 0, 1, 0, -1, 0, 0 }, DY = { 0, 0, 0, 0, 1, -1 }, DZ = { 1, 0, -1, 0, 0, 0 };

        // One thing from a random slot goes out of the front: arrows fly, a blast keg lands lit, anything else is
        // thrown down to be picked up.
        public static void Dispense(Containers boxes, DropSim drops, MobSim mobs, Func<int, int, int, ushort> get, Action<int, int, int, ushort> set,
            int x, int y, int z, int dir, Random dice)
        {
            if (!boxes.Has(x, y, z)) return;
            var slots = boxes.At(x, y, z);
            int n = 0; foreach (var s in slots) if (!s.Empty) n++;
            if (n == 0) return;
            int pick = dice.Next(n), at = -1;
            for (int i = 0; i < slots.Length; i++) if (!slots[i].Empty && pick-- == 0) { at = i; break; }
            var st = slots[at];
            var one = st; one.Count = 1;
            st.Count--; if (st.Count <= 0) st = Stack.None;
            boxes.Set(x, y, z, at, st);
            int dx = DX[dir], dy = DY[dir], dz = DZ[dir];
            float fx = x + 0.5f + dx * 0.75f, fy = y + 0.5f + dy * 0.75f, fz = z + 0.5f + dz * 0.75f;
            if (one.Item == "arrow") mobs.Shoot(1 << 30, fx, fy - 0.1f, fz, dx * 26, dy * 26 + 1.2f, dz * 26, 4);
            else if (one.Item == "blast_keg" && get(x + dx, y + dy, z + dz) == 0) set(x + dx, y + dy, z + dz, Blocks.KegLit);
            else drops.Toss(one.Item, 1, one.Wear, fx, fy - 0.2f, fz, dx * 4.5f, dy * 4.5f + 1.5f, dz * 4.5f, 0.3f);
        }

        // How full a container is, the way a comparator reads it: 0 when empty, 1 for anything at all, 15 when every
        // slot holds a full stack.
        public static int Fill(Containers boxes, int x, int y, int z)
        {
            if (!boxes.Has(x, y, z)) return 0;
            var slots = boxes.At(x, y, z); if (slots.Length == 0) return 0;
            float sum = 0; bool any = false;
            foreach (var s in slots)
            {
                if (s.Empty || Smelting.Hidden(s.Item)) continue;
                any = true; sum += s.Count / (float)Math.Max(1, Items.Get(s.Item)?.MaxStack ?? 64);
            }
            return any ? (int)Math.Floor(1 + 14 * Math.Min(1f, sum / Math.Max(Containers.ChestSlots, slots.Length))) : 0;
        }

        public static bool Holds(Containers boxes, int x, int y, int z)
        {
            if (!boxes.Has(x, y, z)) return false;
            foreach (var s in boxes.At(x, y, z)) if (!s.Empty && !Smelting.Hidden(s.Item)) return true;
            return false;
        }
    }
}
