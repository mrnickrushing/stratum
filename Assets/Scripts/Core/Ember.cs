using System;
using System.Collections.Generic;

namespace Stratum.Core
{
    // The Ember Deep: a burning underworld reached through obsidian portals. It lives in the same world as
    // everything else, in a strip far to the east (x 56000-64000) that the overworld never reaches; a step there is
    // eight steps in the overworld. Being one world means saving, loading chunks and playing together all just work.
    public static class Ember
    {
        public const int X0 = 56000, X1 = 64000, Mid = 60000, Scale = 8, LavaTop = 31, Roof = 128;
        public const int KeepCell = 448;

        public static bool In(float x) => x >= X0 && x < X1;
        public static bool InChunk(int cx) => cx * 16 >= X0 && cx * 16 < X1;

        public static (float x, float z) ToEmber(float x, float z) => (Mid + Math.Clamp(x / Scale, -3900, 3900), z / Scale);
        public static (float x, float z) ToOverworld(float x, float z) => ((x - Mid) * Scale, z * Scale);

        // ------------------------------------------------------------------ the land

        public static void Generate(Chunk ch, Noise caves, Noise detail, int seed)
        {
            var b = ch.Blocks;
            int bx = ch.X * 16, bz = ch.Z * 16;
            for (int z = 0; z < 16; z++)
                for (int x = 0; x < 16; x++)
                {
                    int wx = bx + x, wz = bz + z;
                    bool wall = wx == X0 || wx == X1 - 1;
                    for (int y = 0; y <= Roof + 1; y++)
                    {
                        byte id;
                        uint h = Noise.Hash(wx, y, wz, seed);
                        if (y <= 1 || y == 2 && (h & 1) == 0 || y >= Roof || y == Roof - 1 && (h & 1) == 0 || wall) id = Blocks.Bedrock;
                        else
                        {
                            // Caverns: open through the middle, closing toward floor and roof.
                            float n = caves.Fbm3(wx * 0.016f, y * 0.028f, wz * 0.016f, 3);
                            float m = detail.Fbm3(wx * 0.05f + 3.3f, y * 0.06f, wz * 0.05f + 1.7f, 2) * 0.25f;
                            float t = (y - 70) / 58f;
                            float density = n + m + t * t * 0.95f - 0.3f + (y < LavaTop + 4 ? 0.08f : 0);
                            id = density > 0 ? Blocks.Cinderstone : y <= LavaTop ? Blocks.Lava : (byte)0;
                        }
                        b[x | (z << 4) | (y << 8)] = id;
                    }
                }
            // Floors of ash in patches, sunstone hanging from the roof, ember ore through the rock.
            for (int z = 0; z < 16; z++)
                for (int x = 0; x < 16; x++)
                {
                    int wx = bx + x, wz = bz + z;
                    bool ashy = detail.Fbm2(wx * 0.03f + 11.1f, wz * 0.03f + 7.7f, 2) > 0.18f;
                    for (int y = LavaTop + 1; y < Roof - 2; y++)
                    {
                        int i = x | (z << 4) | (y << 8);
                        if (b[i] != Blocks.Cinderstone) continue;
                        bool airUp = b[i + 256] == 0, airDown = b[i - 256] == 0;
                        if (airUp && ashy) { b[i] = Blocks.Ash; if (b[i - 256] == Blocks.Cinderstone) b[i - 256] = Blocks.Ash; }
                        else if (airDown && y > 70 && Noise.Hash01(wx, y, wz, seed + 9) < 0.02f)
                        {
                            for (int k = 0; k < 4 && y - k > LavaTop + 2; k++)
                            {
                                int j = i - k * 256;
                                if (k > 0 && b[j] != 0) break;
                                if (Noise.Hash01(wx + k, y, wz, seed + 10) < 0.7f || k == 0) b[j] = Blocks.Sunstone;
                            }
                        }
                    }
                }
            var rng = new Rng(Noise.Hash(ch.X, 77, ch.Z, seed));
            for (int v = 0; v < 12; v++)
            {
                int x = rng.Range(0, 16), y = rng.Range(LavaTop, Roof - 4), z = rng.Range(0, 16);
                for (int k = 0; k < 5; k++)
                {
                    int ix = x + rng.Range(-1, 2), iy = y + rng.Range(-1, 2), iz = z + rng.Range(-1, 2);
                    if ((uint)ix < 16 && (uint)iz < 16 && (uint)iy < Chunk.Height && b[ix | (iz << 4) | (iy << 8)] == Blocks.Cinderstone)
                        b[ix | (iz << 4) | (iy << 8)] = Blocks.EmberOre;
                }
            }
            DrawKeeps(ch, seed);
        }

        // ------------------------------------------------------------------ Cinder Keeps (where the Colossus waits)

        public const int KeepY = 50, KeepR = 13;

        // The keep in a cell (one in each square KeepCell wide, inside the Deep).
        public static (int x, int y, int z) KeepAt(int cx, int cz, int seed)
        {
            uint h = Noise.Hash(cx, 919, cz, seed);
            int x = cx * KeepCell + 80 + (int)(h % (KeepCell - 160)), z = cz * KeepCell + 80 + (int)((h >> 12) % (KeepCell - 160));
            return (x, KeepY, z);
        }

        public static (int x, int y, int z)? KeepNear(float px, float pz, int reach, int seed)
        {
            if (!In(px)) return null;
            int cx = (int)Math.Floor(px / KeepCell), cz = (int)Math.Floor(pz / KeepCell);
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    var k = KeepAt(cx + dx, cz + dz, seed);
                    if (!In(k.x - 60) || !In(k.x + 60)) continue;
                    if ((k.x - px) * (k.x - px) + (k.z - pz) * (k.z - pz) <= reach * reach) return k;
                }
            return null;
        }

        static void DrawKeeps(Chunk ch, int seed)
        {
            int bx = ch.X * 16, bz = ch.Z * 16;
            int cx0 = (int)Math.Floor((bx - 64f) / KeepCell), cx1 = (int)Math.Floor((bx + 80f) / KeepCell);
            int cz0 = (int)Math.Floor((bz - 64f) / KeepCell), cz1 = (int)Math.Floor((bz + 80f) / KeepCell);
            for (int cz = cz0; cz <= cz1; cz++)
                for (int cx = cx0; cx <= cx1; cx++)
                {
                    var (kx, ky, kz) = KeepAt(cx, cz, seed);
                    if (!In(kx - 60) || !In(kx + 60)) continue;
                    if (kx + 60 < bx || kx - 60 > bx + 15 || kz + 60 < bz || kz - 60 > bz + 15) continue;
                    Keep(ch, bx, bz, kx, ky, kz);
                }
        }

        static void Put(Chunk ch, int bx, int bz, int x, int y, int z, byte id)
        {
            int lx = x - bx, lz = z - bz;
            if ((uint)lx >= 16 || (uint)lz >= 16 || (uint)y >= Chunk.Height) return;
            ch.Blocks[lx | (lz << 4) | (y << 8)] = id;
        }

        static void Keep(Chunk ch, int bx, int bz, int kx, int ky, int kz)
        {
            int R = KeepR;
            for (int z = bz; z < bz + 16; z++)
                for (int x = bx; x < bx + 16; x++)
                {
                    int dx = x - kx, dz = z - kz, ax = Math.Abs(dx), az = Math.Abs(dz);
                    bool inside = ax <= R && az <= R;
                    // Bridges out from the four gates, to wherever the cavern wall is.
                    bool bridge = (ax <= 1 && az > R && az <= R + 46) || (az <= 1 && ax > R && ax <= R + 46);
                    if (inside)
                    {
                        Put(ch, bx, bz, x, ky - 1, z, Blocks.CinderBricks);
                        Put(ch, bx, bz, x, ky - 2, z, Blocks.CinderBricks);
                        for (int y = ky; y < ky + 14; y++) Put(ch, bx, bz, x, y, z, 0);
                        bool rim = ax == R || az == R, gate = rim && (ax <= 1 || az <= 1);
                        if (rim && !gate) for (int y = ky; y < ky + 3; y++) Put(ch, bx, bz, x, y, z, Blocks.CinderBricks);
                        bool corner = ax >= R - 1 && az >= R - 1;
                        if (corner) { for (int y = ky; y < ky + 9; y++) Put(ch, bx, bz, x, y, z, Blocks.CinderBricks); Put(ch, bx, bz, x, ky + 9, z, Blocks.Sunstone); }
                        // Legs down into the lava every few blocks.
                        if (dx % 6 == 0 && dz % 6 == 0) for (int y = LavaTop - 4; y < ky - 2; y++) Put(ch, bx, bz, x, y, z, Blocks.CinderBricks);
                        // A ring of sunstone set in the floor round the middle.
                        int r2 = dx * dx + dz * dz;
                        if (r2 >= 30 && r2 <= 36) Put(ch, bx, bz, x, ky - 1, z, Blocks.Sunstone);
                    }
                    else if (bridge)
                    {
                        Put(ch, bx, bz, x, ky - 1, z, Blocks.CinderBricks);
                        for (int y = ky; y < ky + 4; y++) Put(ch, bx, bz, x, y, z, 0);
                        bool edge = ax == 1 && az > R || az == 1 && ax > R;
                        if (edge) Put(ch, bx, bz, x, ky, z, Blocks.CinderBricks);      // a low parapet
                        if ((ax + az) % 8 == 0) for (int y = LavaTop - 4; y < ky - 1; y++) Put(ch, bx, bz, x, y, z, Blocks.CinderBricks);
                    }
                }
        }

        // ------------------------------------------------------------------ portals

        // Right-clicking a fire striker beside an obsidian frame: fill the frame's inside with portal. The
        // inside must be air, at least 2 wide and 3 tall, at most 21 each way, with obsidian all round.
        public static bool Light(Func<int, int, int, byte> get, Action<int, int, int, byte> set, int x, int y, int z)
        {
            for (int axis = 0; axis < 2; axis++)
            {
                var cells = new List<(int, int, int)>();
                var seen = new HashSet<(int, int, int)>();
                var q = new Queue<(int, int, int)>();
                if (get(x, y, z) != 0) return false;
                q.Enqueue((x, y, z)); seen.Add((x, y, z));
                bool ok = true;
                int minA = int.MaxValue, maxA = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
                while (q.Count > 0 && ok)
                {
                    var c = q.Dequeue(); cells.Add(c);
                    var (cx, cy, cz) = c;
                    int a = axis == 0 ? cx : cz;
                    minA = Math.Min(minA, a); maxA = Math.Max(maxA, a); minY = Math.Min(minY, cy); maxY = Math.Max(maxY, cy);
                    if (cells.Count > 21 * 21 || maxA - minA > 20 || maxY - minY > 20) { ok = false; break; }
                    foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        var n = axis == 0 ? (cx + dx, cy + dy, cz) : (cx, cy + dy, cz + dx);
                        if (seen.Contains(n)) continue;
                        byte id = get(n.Item1, n.Item2, n.Item3);
                        if (id == Blocks.Obsidian) continue;
                        if (id != 0) { ok = false; break; }
                        seen.Add(n); q.Enqueue(n);
                    }
                }
                if (!ok) continue;
                int w = maxA - minA + 1, h = maxY - minY + 1;
                if (w < 2 || h < 3 || cells.Count != w * h) continue;
                byte portal = (byte)(Blocks.Portal + axis);
                foreach (var (cx, cy, cz) in cells) set(cx, cy, cz, portal);
                return true;
            }
            return false;
        }

        // A frame broken: the portal in it goes out.
        public static void Collapse(Func<int, int, int, byte> get, Action<int, int, int, byte> set, int x, int y, int z)
        {
            var q = new Queue<(int, int, int)>();
            var seen = new HashSet<(int, int, int)>();
            void Try(int a, int b, int c) { if (seen.Count < 600 && seen.Add((a, b, c)) && IsPortal(get(a, b, c))) q.Enqueue((a, b, c)); }
            Try(x, y, z);
            for (int d = 0; d < 6; d++) Try(x + (d == 0 ? 1 : d == 1 ? -1 : 0), y + (d == 2 ? 1 : d == 3 ? -1 : 0), z + (d == 4 ? 1 : d == 5 ? -1 : 0));
            while (q.Count > 0)
            {
                var (a, b, c) = q.Dequeue();
                set(a, b, c, 0);
                for (int d = 0; d < 6; d++) Try(a + (d == 0 ? 1 : d == 1 ? -1 : 0), b + (d == 2 ? 1 : d == 3 ? -1 : 0), c + (d == 4 ? 1 : d == 5 ? -1 : 0));
            }
        }

        public static bool IsPortal(byte id) => id != 0 && Blocks.Get(id).BaseId == Blocks.Portal;

        // Arriving: a portal already near the spot, or a new one built there. Returns where to stand.
        public static (float x, float y, float z) Arrive(Func<int, int, int, byte> get, Action<int, int, int, byte> set, int tx, int tz, bool toEmber, int surfaceY)
        {
            // An existing portal close by?
            int R = toEmber ? 20 : 40;
            (int x, int y, int z)? best = null; long bd = long.MaxValue;
            int y0 = toEmber ? LavaTop : 1, y1 = toEmber ? Roof - 2 : 250;
            for (int dz = -R; dz <= R; dz++)
                for (int dx = -R; dx <= R; dx++)
                    for (int y = y0; y < y1; y++)
                    {
                        if (!IsPortal(get(tx + dx, y, tz + dz)) || IsPortal(get(tx + dx, y - 1, tz + dz))) continue;
                        long d = (long)dx * dx + (long)dz * dz;
                        if (d < bd) { bd = d; best = (tx + dx, y, tz + dz); }
                    }
            if (best is (int px, int py, int pz))
            {
                byte id = get(px, py, pz);
                int axis = id - Blocks.Portal;
                // Stand just in front of it.
                foreach (int side in new[] { -1, 1 })
                {
                    int fx = axis == 0 ? px : px + side, fz = axis == 0 ? pz + side : pz;
                    if (!Blocks.Get(get(fx, py, fz)).Solid && !Blocks.Get(get(fx, py + 1, fz)).Solid) return (fx + 0.5f, py, fz + 0.5f);
                }
                return (px + 0.5f, py, pz + 0.5f);
            }

            // Build one: on the ground in the overworld; in the Deep on a ledge with room, else carved out of the rock.
            int bx = tx, by, bz = tz;
            if (toEmber)
            {
                by = -1;
                for (int r = 0; r <= 10 && by < 0; r += 2)
                    for (int a = 0; a < 8 && by < 0; a++)
                    {
                        int cx = tx + (int)Math.Round(Math.Cos(a * Math.PI / 4) * r), cz = tz + (int)Math.Round(Math.Sin(a * Math.PI / 4) * r);
                        for (int y = LavaTop + 3; y < Roof - 8; y++)
                        {
                            bool floor = Blocks.Get(get(cx, y - 1, cz)).Solid && Blocks.Get(get(cx + 3, y - 1, cz)).Solid;
                            bool room = true;
                            for (int k = 0; k < 4 && room; k++) for (int yy = y; yy < y + 4; yy++) if (get(cx + k, yy, cz) != 0) { room = false; break; }
                            if (floor && room) { by = y; bx = cx; bz = cz; break; }
                        }
                    }
                if (by < 0) by = 64;
            }
            else by = Math.Max(surfaceY + 1, 2);
            // Room round it, a floor to stand on, then the frame and the portal.
            for (int dz = -2; dz <= 2; dz++)
                for (int dx = -1; dx <= 4; dx++)
                {
                    for (int y = by; y < by + 5; y++) set(bx + dx, y, bz + dz, 0);
                    if (!Blocks.Get(get(bx + dx, by - 1, bz + dz)).Solid || toEmber && Math.Abs(dz) <= 1) set(bx + dx, by - 1, bz + dz, Blocks.Obsidian);
                }
            for (int dx = 0; dx < 4; dx++) { set(bx + dx, by - 1, bz, Blocks.Obsidian); set(bx + dx, by + 3, bz, Blocks.Obsidian); }
            for (int y = by; y < by + 3; y++) { set(bx, y, bz, Blocks.Obsidian); set(bx + 3, y, bz, Blocks.Obsidian); }
            for (int dx = 1; dx <= 2; dx++) for (int y = by; y < by + 3; y++) set(bx + dx, y, bz, Blocks.Portal);
            return (bx + 1.5f, by, bz - 0.5f);
        }
    }
}
