using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Stratum.Core
{
    // Villages on flat grassland: a well in the middle, gravel roads going out, and houses, farms and lamps along
    // them. Where each village goes and what's in it is worked out from the seed alone, so every chunk can draw
    // its share of a village on any thread, in any order, and the pieces meet.
    public sealed class Villages
    {
        public const int Cell = 384;                 // at most one village in each square this wide
        readonly WorldGen _gen;
        readonly int _seed;
        readonly ConcurrentDictionary<long, Plan> _plans = new ConcurrentDictionary<long, Plan>();

        public sealed class Part { public int Kind, X, Z, W, D, Face, Floor; }   // Kind: 0 house, 1 big house, 2 farm, 3 lamp, 4 well
        public sealed class Plan
        {
            public bool Real; public int X, Z, Y;
            public readonly List<Part> Parts = new List<Part>();
            public readonly List<(int x0, int z0, int dx, int dz, int len)> Roads = new List<(int, int, int, int, int)>();
            public int MinX, MinZ, MaxX, MaxZ;
            public int Houses;
        }

        public Villages(WorldGen gen, int seed) { _gen = gen; _seed = seed; }

        static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);

        public Plan For(int cx, int cz) => _plans.GetOrAdd(((long)cx << 32) | (uint)cz, _ => Make(cx, cz));

        // The village nearest a spot, if one is within reach.
        public Plan Near(int x, int z, int reach)
        {
            int cx = FloorDiv(x, Cell), cz = FloorDiv(z, Cell);
            Plan best = null; long bd = (long)reach * reach;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    var p = For(cx + dx, cz + dz);
                    if (!p.Real) continue;
                    long d = (long)(p.X - x) * (p.X - x) + (long)(p.Z - z) * (p.Z - z);
                    if (d < bd) { bd = d; best = p; }
                }
            return best;
        }

        Plan Make(int cx, int cz)
        {
            var plan = new Plan();
            if (_gen.Kind == WorldKind.Sky) return plan;           // nowhere to put one
            uint h = Noise.Hash(cx, 401, cz, _seed);
            if (h % 100 >= 60) return plan;
            // A few spots in the cell; the flattest dry grassland one wins.
            int x = 0, z = 0, y = 0, bestSpread = int.MaxValue;
            var tr = new Rng(h);
            for (int t = 0; t < 8; t++)
            {
                int tx = cx * Cell + 64 + tr.Range(0, Cell - 128), tz = cz * Cell + 64 + tr.Range(0, Cell - 128);
                var biome = _gen.BiomeAt(tx, tz);
                if (biome != Biome.Plains && biome != Biome.Forest && biome != Biome.Taiga && biome != Biome.BirchWood) continue;
                int ty = (int)_gen.Sample(tx, tz).Height;
                if (ty <= Chunk.SeaLevel + 1 || ty > 140) continue;
                int lo = ty, hi = ty; bool wet = false;
                for (int i = 0; i < 12 && !wet; i++)
                {
                    double a = i * Math.PI / 6;
                    for (int r = 10; r <= 26; r += 16)
                    {
                        int hh = (int)_gen.Sample(tx + (int)(Math.Cos(a) * r), tz + (int)(Math.Sin(a) * r)).Height;
                        if (hh <= Chunk.SeaLevel) { wet = true; break; }
                        lo = Math.Min(lo, hh); hi = Math.Max(hi, hh);
                    }
                }
                if (wet || hi - lo > 9 || hi - lo >= bestSpread) continue;
                bestSpread = hi - lo; x = tx; z = tz; y = ty;
            }
            if (bestSpread == int.MaxValue) return plan;

            plan.Real = true; plan.X = x; plan.Z = z; plan.Y = y;
            var rng = new Rng(h ^ 0x51a6e);
            plan.Parts.Add(new Part { Kind = 4, X = x - 2, Z = z - 2, W = 4, D = 4, Floor = y + 1 });
            int[] DX = { 0, 1, 0, -1 }, DZ = { 1, 0, -1, 0 };
            for (int arm = 0; arm < 4; arm++)
            {
                if (arm > 1 && rng.Value() < 0.25f) continue;
                int len = rng.Range(18, 32);
                int sx = x + DX[arm] * 3, sz = z + DZ[arm] * 3;
                plan.Roads.Add((sx, sz, DX[arm], DZ[arm], len));
                for (int at = 6; at < len; at += rng.Range(9, 13))
                {
                    for (int side = -1; side <= 1; side += 2)
                    {
                        if (rng.Value() < 0.2f) continue;
                        float roll = rng.Value();
                        int kind = roll < 0.45f ? 0 : roll < 0.7f ? 1 : roll < 0.9f ? 2 : 3;
                        int w = kind == 0 ? 5 : kind == 1 ? 7 : kind == 2 ? 7 : 1;
                        int d = kind == 0 ? 5 : kind == 1 ? 6 : kind == 2 ? 9 : 1;
                        // Perpendicular to the road, set back from it, front facing it.
                        int px = -DZ[arm] * side, pz = DX[arm] * side;          // toward this side
                        int roadX = sx + DX[arm] * at, roadZ = sz + DZ[arm] * at;
                        int gap = kind == 3 ? 2 : 3;
                        // Front dir points back to the road: -(px,pz).
                        int face = Dir(-px, -pz);
                        // Footprint size in world terms depends on which way it faces.
                        bool alongX = face == 0 || face == 2;           // front faces ±Z: width runs along X
                        int fw = alongX ? w : d, fd = alongX ? d : w;
                        int nearX = roadX + px * gap, nearZ = roadZ + pz * gap;
                        int x0 = px > 0 ? nearX : px < 0 ? nearX - fw + 1 : nearX - fw / 2;
                        int z0 = pz > 0 ? nearZ : pz < 0 ? nearZ - fd + 1 : nearZ - fd / 2;
                        var part = new Part { Kind = kind, X = x0, Z = z0, W = fw, D = fd, Face = face };
                        if (Overlaps(plan, part)) continue;
                        var s = _gen.Sample(x0 + fw / 2, z0 + fd / 2);
                        if (s.Height <= Chunk.SeaLevel || Math.Abs(s.Height - y) > 6) continue;
                        part.Floor = (int)s.Height + 1;
                        plan.Parts.Add(part);
                        if (kind <= 1) plan.Houses++;
                    }
                }
            }
            plan.MinX = x - 48; plan.MaxX = x + 48; plan.MinZ = z - 48; plan.MaxZ = z + 48;
            return plan;
        }

        static int Dir(int dx, int dz) => dx > 0 ? 1 : dx < 0 ? 3 : dz > 0 ? 0 : 2;

        static bool Overlaps(Plan p, Part n)
        {
            foreach (var o in p.Parts)
                if (n.X - 1 < o.X + o.W && o.X - 1 < n.X + n.W && n.Z - 1 < o.Z + o.D && o.Z - 1 < n.Z + n.D) return true;
            // Keep off the roads.
            foreach (var r in p.Roads)
                for (int i = 0; i < r.len; i++)
                {
                    int rx = r.x0 + r.dx * i, rz = r.z0 + r.dz * i;
                    if (rx >= n.X - 1 && rx <= n.X + n.W && rz >= n.Z - 1 && rz <= n.Z + n.D) return true;
                }
            return false;
        }

        // ------------------------------------------------------------------ drawing a chunk's share

        public void Stamp(Chunk ch, Func<int, int, int> heightAt)
        {
            int bx = ch.X * 16, bz = ch.Z * 16;
            int cx0 = FloorDiv(bx - 64, Cell), cx1 = FloorDiv(bx + 16 + 64, Cell);
            int cz0 = FloorDiv(bz - 64, Cell), cz1 = FloorDiv(bz + 16 + 64, Cell);
            for (int cz = cz0; cz <= cz1; cz++)
                for (int cx = cx0; cx <= cx1; cx++)
                {
                    var p = For(cx, cz);
                    if (!p.Real || p.MaxX < bx || p.MinX > bx + 15 || p.MaxZ < bz || p.MinZ > bz + 15) continue;
                    Draw(ch, bx, bz, p, heightAt);
                }
        }

        static void Put(Chunk ch, int bx, int bz, int x, int y, int z, ushort id)
        {
            int lx = x - bx, lz = z - bz;
            if ((uint)lx >= 16 || (uint)lz >= 16 || (uint)y >= Chunk.Height) return;
            ch.Blocks[lx | (lz << 4) | (y << 8)] = id;
        }

        static ushort Get(Chunk ch, int bx, int bz, int x, int y, int z)
        {
            int lx = x - bx, lz = z - bz;
            if ((uint)lx >= 16 || (uint)lz >= 16 || (uint)y >= Chunk.Height) return 0;
            return ch.Blocks[lx | (lz << 4) | (y << 8)];
        }

        static bool Inside(int bx, int bz, int x, int z) => (uint)(x - bx) < 16 && (uint)(z - bz) < 16;

        void Draw(Chunk ch, int bx, int bz, Plan p, Func<int, int, int> heightAt)
        {
            // Woods are cleared for the village (trunks, leaves and anything growing).
            for (int z = bz; z < bz + 16; z++)
                for (int x = bx; x < bx + 16; x++)
                {
                    long dx = x - p.X, dz = z - p.Z;
                    if (dx * dx + dz * dz > 38 * 38) continue;
                    int h = heightAt(x, z);
                    for (int y = h + 1; y < Math.Min(Chunk.Height, h + 24); y++)
                    {
                        var d = Blocks.Get(Get(ch, bx, bz, x, y, z));
                        if (d.Sway || d.Name.EndsWith("Log") || d.Shape == Shape.Cross) Put(ch, bx, bz, x, y, z, 0);
                    }
                    if (Get(ch, bx, bz, x, h, z) == Blocks.Dirt) Put(ch, bx, bz, x, h, z, Blocks.Grass);
                }
            // Roads: gravel over the ground, three wide.
            foreach (var r in p.Roads)
                for (int i = 0; i < r.len; i++)
                    for (int s = -1; s <= 1; s++)
                    {
                        int x = r.x0 + r.dx * i + (r.dz != 0 ? s : 0), z = r.z0 + r.dz * i + (r.dx != 0 ? s : 0);
                        if (!Inside(bx, bz, x, z)) continue;
                        int y = heightAt(x, z);
                        ushort top = Get(ch, bx, bz, x, y, z);
                        if (top == Blocks.Grass || top == Blocks.Dirt || top == Blocks.Sand || top == Blocks.SnowyGrass)
                        {
                            Put(ch, bx, bz, x, y, z, Blocks.Gravel);
                            for (int k = 1; k <= 2; k++) { var a = Blocks.Get(Get(ch, bx, bz, x, y + k, z)); if (a.Shape == Shape.Cross) Put(ch, bx, bz, x, y + k, z, 0); }
                        }
                    }
            foreach (var part in p.Parts)
            {
                if (part.X + part.W + 2 < bx || part.X - 2 > bx + 15 || part.Z + part.D + 2 < bz || part.Z - 2 > bz + 15) continue;
                Ground(ch, bx, bz, part, heightAt);
                switch (part.Kind)
                {
                    case 0: case 1: House(ch, bx, bz, part); break;
                    case 2: Farm(ch, bx, bz, part); break;
                    case 3: Lamp(ch, bx, bz, part); break;
                    case 4: Well(ch, bx, bz, part); break;
                }
            }
        }

        // Levels the plot: earth up to the floor, air above it (and a step of fill round the edge).
        static void Ground(Chunk ch, int bx, int bz, Part p, Func<int, int, int> heightAt)
        {
            int f = p.Floor;
            for (int x = p.X - 1; x <= p.X + p.W; x++)
                for (int z = p.Z - 1; z <= p.Z + p.D; z++)
                {
                    if (!Inside(bx, bz, x, z)) continue;
                    int h = heightAt(x, z);
                    for (int y = Math.Max(h + 1, f - 8); y <= f - 1; y++) Put(ch, bx, bz, x, y, z, Blocks.Dirt);
                    Put(ch, bx, bz, x, f - 1, z, Blocks.Grass);
                    for (int y = f; y <= f + 10; y++) Put(ch, bx, bz, x, y, z, 0);
                }
        }

        // Local (u across the front, v from front to back) to world, for a part facing p.Face.
        static (int x, int z) At(Part p, int u, int v)
        {
            switch (p.Face)
            {
                case 2: return (p.X + u, p.Z + v);                         // front faces -Z
                case 0: return (p.X + p.W - 1 - u, p.Z + p.D - 1 - v);     // front faces +Z
                case 1: return (p.X + p.W - 1 - v, p.Z + u);               // front faces +X
                default: return (p.X + v, p.Z + p.D - 1 - u);              // front faces -X
            }
        }

        static void P(Chunk ch, int bx, int bz, Part p, int u, int y, int v, ushort id) { var (x, z) = At(p, u, v); Put(ch, bx, bz, x, y, z, id); }

        static void House(Chunk ch, int bx, int bz, Part p)
        {
            bool big = p.Kind == 1;
            int w = big ? 7 : 5, d = big ? 6 : 5, f = p.Floor;
            int door = w / 2;
            ushort wall = Blocks.Get(Blocks.OakPlanks) != null ? Blocks.OakPlanks : Blocks.Cobble;
            for (int u = 0; u < w; u++)
                for (int v = 0; v < d; v++)
                {
                    bool edgeU = u == 0 || u == w - 1, edgeV = v == 0 || v == d - 1;
                    P(ch, bx, bz, p, u, f - 1, v, edgeU || edgeV ? Blocks.Cobble : Blocks.OakPlanks);
                    for (int y = f; y < f + 3; y++)
                    {
                        ushort id = 0;
                        if (edgeU && edgeV) id = Blocks.OakLog;
                        else if (edgeU || edgeV)
                        {
                            id = y == f ? Blocks.Cobble : wall;
                            bool window = y == f + 1 && ((edgeU && v == d / 2) || (edgeV && v == d - 1 && (u == 1 || u == w - 2)) || (big && edgeV && v == 0 && (u == 1 || u == w - 2)));
                            if (window) id = Blocks.GlassPane;
                        }
                        P(ch, bx, bz, p, u, y, v, id);
                    }
                }
            // The door, in the middle of the front.
            int df = (p.Face + 2) % 4;
            P(ch, bx, bz, p, door, f, 0, (ushort)(Blocks.OakDoor + df * 4));
            P(ch, bx, bz, p, door, f + 1, 0, (ushort)(Blocks.OakDoor + df * 4 + 1));
            P(ch, bx, bz, p, door, f + 2, 0, wall);
            // Roof: a ceiling of planks, a slab overhang, and a ridge of slabs.
            ushort slab = Blocks.Slabs.Count > 0 ? Blocks.Slabs[0] : Blocks.OakPlanks;
            for (int u = -1; u <= w; u++)
                for (int v = -1; v <= d; v++)
                {
                    bool over = u < 0 || u >= w || v < 0 || v >= d;
                    P(ch, bx, bz, p, u, f + 3, v, over ? slab : Blocks.OakPlanks);
                    if (u >= 1 && u <= w - 2 && v >= 1 && v <= d - 2) P(ch, bx, bz, p, u, f + 4, v, (u == 1 || u == w - 2 || v == 1 || v == d - 2) ? slab : Blocks.OakPlanks);
                    if (u >= 2 && u <= w - 3 && v >= 2 && v <= d - 3) P(ch, bx, bz, p, u, f + 5, v, slab);
                }
            // Inside: a bed, a workbench with a torch on it, and in big houses a chest.
            int bf = (p.Face + 2) % 4;      // the bed's head toward the back wall
            var (hx, hz) = At(p, 1, d - 2); var (fx, fz) = At(p, 1, d - 3);
            int bedFacing = Dir(hx - fx, hz - fz);
            Put(ch, bx, bz, fx, f, fz, (ushort)(Blocks.Bed + bedFacing * 2));
            Put(ch, bx, bz, hx, f, hz, (ushort)(Blocks.Bed + bedFacing * 2 + 1));
            P(ch, bx, bz, p, w - 2, f, d - 2, Blocks.Workbench);
            P(ch, bx, bz, p, w - 2, f + 1, d - 2, Blocks.Torch);
            if (big) P(ch, bx, bz, p, w - 2, f, 1, Blocks.LootChest);          // something put by: it fills when first opened
            _ = bf;
        }

        static void Farm(Chunk ch, int bx, int bz, Part p)
        {
            int f = p.Floor;
            uint h = Noise.Hash(p.X, p.Floor, p.Z, 77);
            for (int u = 0; u < 7; u++)
                for (int v = 0; v < 9; v++)
                {
                    bool rim = u == 0 || u == 6 || v == 0 || v == 8;
                    if (rim) { P(ch, bx, bz, p, u, f - 1, v, Blocks.OakLog); continue; }
                    if (u == 3) { P(ch, bx, bz, p, u, f - 1, v, Blocks.Water); P(ch, bx, bz, p, u, f - 2, v, Blocks.Dirt); continue; }
                    P(ch, bx, bz, p, u, f - 1, v, Blocks.FarmlandWet);
                    int stage = (int)((h >> ((u * 9 + v) % 24)) % 4);
                    // Wheat on one side of the ditch; carrots or potatoes on the other.
                    ushort crop = u < 3 ? Blocks.Wheat : (h & 1) == 0 ? Blocks.Carrot : Blocks.Potato;
                    P(ch, bx, bz, p, u, f, v, (ushort)(crop + stage));
                }
        }

        static void Lamp(Chunk ch, int bx, int bz, Part p)
        {
            int f = p.Floor;
            P(ch, bx, bz, p, 0, f - 1, 0, Blocks.Cobble);
            for (int y = f; y < f + 3; y++) P(ch, bx, bz, p, 0, y, 0, Blocks.OakFence);
            P(ch, bx, bz, p, 0, f + 3, 0, Blocks.Sunstone);
        }

        static void Well(Chunk ch, int bx, int bz, Part p)
        {
            int f = p.Floor;
            for (int u = 0; u < 4; u++)
                for (int v = 0; v < 4; v++)
                {
                    bool rim = u == 0 || u == 3 || v == 0 || v == 3;
                    for (int y = f - 5; y < f - 1; y++) P(ch, bx, bz, p, u, y, v, rim ? Blocks.Cobble : Blocks.Water);
                    P(ch, bx, bz, p, u, f - 1, v, rim ? Blocks.Cobble : Blocks.Water);
                    if (rim) P(ch, bx, bz, p, u, f, v, Blocks.Cobble);
                    bool corner = (u == 0 || u == 3) && (v == 0 || v == 3);
                    if (corner) { P(ch, bx, bz, p, u, f + 1, v, Blocks.OakFence); P(ch, bx, bz, p, u, f + 2, v, Blocks.OakFence); }
                    P(ch, bx, bz, p, u, f + 3, v, Blocks.Slabs.Count > 3 ? Blocks.Slabs[3] : Blocks.Cobble);
                }
        }
    }

    // What villagers will trade. Their trade is the work they do (from Mob.Job).
    public static class Trades
    {
        public sealed class Offer { public string In; public int InN; public string In2; public int In2N; public string Out; public int OutN; }
        public static readonly string[] Jobs = { "Farmer", "Smith", "Librarian", "Shepherd", "Butcher" };

        static Offer O(string i, int n, string o, int on, string i2 = null, int n2 = 0) => new Offer { In = i, InN = n, Out = o, OutN = on, In2 = i2, In2N = n2 };

        public static List<Offer> For(int job, int seed)
        {
            var r = new Random(seed);
            switch (job)
            {
                case 0: return new List<Offer> { O("wheat", 20, "jade", 1), O("berries", 16, "jade", 1), O("jade", 1, "bread", 6), O("jade", 1, "apple", 4), O("jade", 2, "seeds", 16), O("jade", 3, "hay_bale", 2) };
                case 1: return new List<Offer> { O("coal", 15, "jade", 1), O("iron_ingot", 4, "jade", 1), O("jade", 3, "iron_pickaxe", 1), O("jade", 6, "iron_sword@s2", 1),
                                                 O("jade", 9, "iron_coat@p1", 1), O("jade", 5, "bucket", 1), O("jade", 8, "anvil", 1) };
                case 2:
                {
                    var ks = new[] { 's', 'e', 'f', 'w', 'p', 'g', 'u' };
                    char a = ks[r.Next(ks.Length)], b = ks[r.Next(ks.Length)];
                    return new List<Offer> { O("paper", 24, "jade", 1), O("book", 2, "jade", 1), O("jade", 1, "glass", 4), O("jade", 7, "compass", 1), O("jade", 9, "map", 1),
                                             O("jade", 10 + r.Next(10), Enchants.Make("book", new Dictionary<char, int> { { a, 1 + r.Next(3) } }), 1, "book", 1),
                                             O("jade", 14 + r.Next(12), Enchants.Make("book", new Dictionary<char, int> { { b, 2 + r.Next(2) } }), 1, "book", 1) };
                }
                case 3: return new List<Offer> { O("white_wool", 18, "jade", 1), O("jade", 2, "shears", 1), O("jade", 3, "bed", 1), O("jade", 1, "white_wool", 3) };
                default: return new List<Offer> { O("raw_beef", 12, "jade", 1), O("raw_pork", 12, "jade", 1), O("raw_chicken", 14, "jade", 1), O("jade", 1, "steak", 5), O("jade", 1, "roast_pork", 5) };
            }
        }
    }
}
