using System;

namespace Stratum.Core
{
    // Rails lay themselves: a new piece joins the rails beside it (straight, round a corner, or up a step) and
    // turns a loose end of its neighbours toward itself. Directions: 0 +Z, 1 +X, 2 -Z, 3 -X.
    //   rail variants: 0-1 straight (facing f joins f and f+2), 2-5 curve c (joins c and c+1), 6-9 rising toward a
    //   booster variants: 0-1 straight, 2-5 rising toward a
    public static class Rails
    {
        public static readonly int[] DX = { 0, 1, 0, -1 }, DZ = { 1, 0, -1, 0 };

        public static bool Is(ushort id) { if (id == 0) return false; int b = Blocks.Get(id).BaseId; return b == Blocks.Rail || b == Blocks.BoosterRail; }
        public static bool Booster(ushort id) => id != 0 && Blocks.Get(id).BaseId == Blocks.BoosterRail;

        // The two directions a rail joins, and the one it rises toward (-1 if it's flat).
        public static (int a, int b, int up) Ends(ushort id)
        {
            var d = Blocks.Get(id);
            int v = id - d.BaseId;
            if (d.BaseId == Blocks.BoosterRail)
            {
                if (v < 2) return (v, v + 2, -1);
                int u = v - 2; return (u, (u + 2) & 3, u);
            }
            if (v < 2) return (v, v + 2, -1);
            if (v < 6) { int c = v - 2; return (c, (c + 1) & 3, -1); }
            int r = v - 6; return (r, (r + 2) & 3, r);
        }

        public static bool Joins(ushort id, int dir) { var (a, b, _) = Ends(id); return a == dir || b == dir; }

        static ushort Make(ushort baseId, int a, int b, int up)
        {
            bool booster = baseId == Blocks.BoosterRail;
            if (up >= 0) return (ushort)(baseId + (booster ? 2 : 6) + up);
            if (((a + 2) & 3) == b || booster) return (ushort)(baseId + (a & 1));
            int c = ((a + 1) & 3) == b ? a : b;
            return (ushort)(baseId + 2 + c);
        }

        // A rail next door in that direction: level with it, a step up, or a step down (dy -1, 0, 1; 99 if none).
        static int Beside(Func<int, int, int, ushort> get, int x, int y, int z, int dir)
        {
            int nx = x + DX[dir], nz = z + DZ[dir];
            if (Is(get(nx, y, nz))) return 0;
            if (Is(get(nx, y + 1, nz))) return 1;
            if (Is(get(nx, y - 1, nz))) return -1;
            return 99;
        }

        // The shape a rail at (x, y, z) should take, given the rails round it (lookDir if it's on its own).
        public static ushort Shape(Func<int, int, int, ushort> get, int x, int y, int z, ushort baseId, int lookDir)
        {
            var near = new int[4];
            int count = 0;
            for (int d = 0; d < 4; d++) { near[d] = Beside(get, x, y, z, d); if (near[d] != 99) count++; }
            // Something above to climb toward wins: the rail rises that way.
            for (int d = 0; d < 4; d++) if (near[d] == 1) return Make(baseId, d, (d + 2) & 3, d);
            if (count == 0) return Make(baseId, lookDir, (lookDir + 2) & 3, -1);
            // A straight run through.
            for (int d = 0; d < 2; d++) if (near[d] != 99 && near[d + 2] != 99) return Make(baseId, d, d + 2, -1);
            if (count >= 2 && baseId == Blocks.Rail)
                for (int d = 0; d < 4; d++) if (near[d] != 99 && near[(d + 1) & 3] != 99) return Make(baseId, d, (d + 1) & 3, -1);
            for (int d = 0; d < 4; d++) if (near[d] != 99) return Make(baseId, d, (d + 2) & 3, -1);
            return Make(baseId, lookDir, (lookDir + 2) & 3, -1);
        }

        // After a rail is laid at (x, y, z): neighbours with a loose end turn to meet it. Calls set for each change.
        public static void Settle(Func<int, int, int, ushort> get, Action<int, int, int, ushort> set, int x, int y, int z)
        {
            for (int d = 0; d < 4; d++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    int nx = x + DX[d], ny = y + dy, nz = z + DZ[d];
                    ushort n = get(nx, ny, nz);
                    if (!Is(n)) continue;
                    int back = (d + 2) & 3;
                    if (Joins(n, back)) break;
                    // Only a rail with an end not already joined will turn.
                    var (a, b, _) = Ends(n);
                    bool aUsed = Beside(get, nx, ny, nz, a) != 99, bUsed = Beside(get, nx, ny, nz, b) != 99;
                    if (aUsed && bUsed) break;
                    ushort s = Shape(get, nx, ny, nz, Blocks.Get(n).BaseId, back);
                    if (s != n) set(nx, ny, nz, s);
                    break;
                }
        }

        // Where along a rail a cart is: entering from side `from`, leaving by the other end, t from 0 to 1.
        // Returns the point (cell-local, 0..1 across, height above the cell's floor).
        public static (float x, float y, float z) Along(ushort id, int from, float t)
        {
            var (a, b, up) = Ends(id);
            int to = from == a ? b : a;
            float ex = 0.5f + DX[from] * 0.5f, ez = 0.5f + DZ[from] * 0.5f;
            float ox = 0.5f + DX[to] * 0.5f, oz = 0.5f + DZ[to] * 0.5f;
            float x, z;
            if (((from + 2) & 3) == to) { x = ex + (ox - ex) * t; z = ez + (oz - ez) * t; }
            else
            {
                // A quarter circle round the corner the two ends share.
                float cx = 0.5f + (DX[from] + DX[to]) * 0.5f, cz = 0.5f + (DZ[from] + DZ[to]) * 0.5f;
                double a0 = Math.Atan2(ez - cz, ex - cx), a1 = Math.Atan2(oz - cz, ox - cx);
                double da = a1 - a0; while (da > Math.PI) da -= 2 * Math.PI; while (da < -Math.PI) da += 2 * Math.PI;
                double ang = a0 + da * t;
                x = cx + (float)Math.Cos(ang) * 0.5f; z = cz + (float)Math.Sin(ang) * 0.5f;
            }
            float h = 1 / 16f;
            if (up >= 0) h += up == to ? t : 1 - t;
            return (x, h, z);
        }

        // How long that path is (a corner is a little shorter, a slope a little longer).
        public static float Length(ushort id)
        {
            var (a, b, up) = Ends(id);
            if (up >= 0) return 1.414f;
            return ((a + 2) & 3) == b ? 1f : 0.785f;
        }
    }
}
