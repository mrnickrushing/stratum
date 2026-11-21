using System;
using System.Collections.Generic;

namespace Stratum.Core
{
    // Hand-built adventure worlds: a castle, a parkour run, a hedge maze and a market town, each standing on flat
    // grass just north of where players start. A build is drawn once into a box of block numbers ("leave it" where
    // nothing was drawn) and copied into each chunk the world generator makes, so the game and a server make the
    // same world from nothing but the kind. Heights count from the first layer of air above the ground.
    public static class Builds
    {
        public const ushort Leave = 0xFFFF;
        public static int Ground => WorldGen.FlatTop + 1;

        public static bool Is(WorldKind k) => k >= WorldKind.Castle && k <= WorldKind.Town;

        public sealed class Canvas
        {
            public readonly int X0, Y0, Z0, W, H, D;
            public readonly ushort[] C;
            public Canvas(int x0, int y0, int z0, int x1, int y1, int z1)
            {
                X0 = x0; Y0 = y0; Z0 = z0; W = x1 - x0 + 1; H = y1 - y0 + 1; D = z1 - z0 + 1;
                C = new ushort[W * H * D];
                for (int i = 0; i < C.Length; i++) C[i] = Leave;
            }
            int I(int x, int y, int z) => (x - X0) + (z - Z0) * W + (y - Y0) * W * D;
            public bool In(int x, int y, int z) => (uint)(x - X0) < (uint)W && (uint)(y - Y0) < (uint)H && (uint)(z - Z0) < (uint)D;
            public void Set(int x, int y, int z, ushort id) { if (In(x, y, z)) C[I(x, y, z)] = id; }
            public void Set(int x, int y, int z, string key) => Set(x, y, z, B(key));
            public ushort Get(int x, int y, int z) => In(x, y, z) ? C[I(x, y, z)] : Leave;
            public void Fill(int x0, int y0, int z0, int x1, int y1, int z1, ushort id)
            {
                if (x0 > x1) (x0, x1) = (x1, x0); if (y0 > y1) (y0, y1) = (y1, y0); if (z0 > z1) (z0, z1) = (z1, z0);
                for (int y = y0; y <= y1; y++) for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++) Set(x, y, z, id);
            }
            public void Fill(int x0, int y0, int z0, int x1, int y1, int z1, string key) => Fill(x0, y0, z0, x1, y1, z1, B(key));
            // The four walls of a box.
            public void Walls(int x0, int y0, int z0, int x1, int y1, int z1, ushort id)
            {
                Fill(x0, y0, z0, x1, y1, z0, id); Fill(x0, y0, z1, x1, y1, z1, id); Fill(x0, y0, z0, x0, y1, z1, id); Fill(x1, y0, z0, x1, y1, z1, id);
            }
            public void Walls(int x0, int y0, int z0, int x1, int y1, int z1, string key) => Walls(x0, y0, z0, x1, y1, z1, B(key));
            // Things that turn: 0 faces +Z, 1 +X, 2 -Z, 3 -X.
            public void Turned(int x, int y, int z, string key, int f) => Set(x, y, z, (ushort)(B(key) + (f & 3)));
            public void Door(int x, int y, int z, string key, int f) { Set(x, y, z, (ushort)(B(key) + (f & 3) * 4)); Set(x, y + 1, z, (ushort)(B(key) + (f & 3) * 4 + 1)); }
            public void Bed(int x, int y, int z, int f)
            {
                Set(x, y, z, (ushort)(Blocks.Bed + (f & 3) * 2)); Set(x + DX[f & 3], y, z + DZ[f & 3], (ushort)(Blocks.Bed + (f & 3) * 2 + 1));
            }
        }

        static readonly int[] DX = { 0, 1, 0, -1 }, DZ = { 1, 0, -1, 0 };
        static ushort B(string key) => Blocks.ByKey[key];

        // Our own dice: the same on every machine and runtime.
        struct Dice
        {
            uint _s;
            public Dice(uint seed) { _s = seed == 0 ? 1u : seed; }
            public int Next(int n) { _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5; return (int)(_s % (uint)n); }
            public bool Chance(int outOf) => Next(outOf) == 0;
        }

        static readonly Canvas[] _made = new Canvas[4];
        static readonly object _lock = new object();

        public static Canvas Of(WorldKind kind)
        {
            int i = kind - WorldKind.Castle;
            lock (_lock)
            {
                if (_made[i] == null)
                    _made[i] = kind == WorldKind.Castle ? Castle() : kind == WorldKind.Parkour ? Parkour() : kind == WorldKind.Maze ? Maze() : Town();
                return _made[i];
            }
        }

        // Where players start: on the grass at the south end, looking north at the build.
        public static (int x, int y, int z) Spawn(WorldKind kind) => (0, Ground, 0);

        // Is this spot within `margin` of the build's footprint?
        public static bool Near(WorldKind kind, int x, int z, int margin)
        {
            var c = Of(kind);
            return x >= c.X0 - margin && x < c.X0 + c.W + margin && z >= c.Z0 - margin && z < c.Z0 + c.D + margin;
        }

        // Copies the part of the build that lies in this chunk.
        public static void Stamp(Chunk ch, WorldKind kind)
        {
            var c = Of(kind);
            int bx = ch.X * 16, bz = ch.Z * 16;
            int x0 = Math.Max(bx, c.X0), x1 = Math.Min(bx + 15, c.X0 + c.W - 1), z0 = Math.Max(bz, c.Z0), z1 = Math.Min(bz + 15, c.Z0 + c.D - 1);
            if (x0 > x1 || z0 > z1) return;
            for (int y = c.Y0; y < c.Y0 + c.H; y++)
            {
                int wy = Ground + y;
                if ((uint)wy >= Chunk.Height) continue;
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                    {
                        ushort id = c.Get(x, y, z);
                        if (id != Leave) ch.Blocks[(x - bx) | ((z - bz) << 4) | (wy << 8)] = id;
                    }
            }
        }

        // A clear paved spot to arrive on, whatever trees the land would have grown there.
        static void Pad(Canvas c)
        {
            c.Fill(-5, 0, -4, 5, 14, 6, 0);
            c.Fill(-2, -1, -3, 2, -1, 6, "cobble");
            c.Set(-3, 0, -3, "lamp_post"); c.Set(3, 0, -3, "lamp_post");
        }

        // ------------------------------------------------------------------------------------------ castle
        // A moated castle: curtain wall with a wall walk and four roofed towers, a gatehouse, a courtyard with a well,
        // a stable and a smithy, and a three-floor keep (great hall, bedchambers, throne room) over a dungeon.
        static Canvas Castle()
        {
            var c = new Canvas(-34, -9, -4, 34, 34, 70); Pad(c);
            const int cz = 37;
            c.Fill(-34, 0, 4, 34, 34, 70, 0);
            ushort sb = B("stone_bricks"), cob = B("cobble");
            // The moat, with a plank bridge on the south side.
            for (int x = -31; x <= 31; x++)
                for (int z = cz - 31; z <= cz + 31; z++)
                {
                    int r = Math.Max(Math.Abs(x), Math.Abs(z - cz));
                    if (r < 28 || r > 31) continue;
                    c.Set(x, -1, z, Blocks.Water); c.Set(x, -2, z, Blocks.Water); c.Set(x, -3, z, Blocks.Water); c.Set(x, -4, z, "clay");
                }
            c.Fill(-2, -1, cz - 32, 2, -1, cz - 27, "dark_planks");
            for (int z = cz - 31; z <= cz - 28; z++) { c.Set(-2, 0, z, "oak_fence"); c.Set(2, 0, z, "oak_fence"); }
            c.Set(-2, 0, cz - 32, "lamp_post"); c.Set(2, 0, cz - 32, "lamp_post");

            // The curtain wall: two thick, seven high, a walk on top with battlements on the outside.
            int s = 24, zs = cz - s, zn = cz + s;
            for (int t = 0; t < 2; t++) c.Walls(-s + t, 0, zs + t, s - t, 6, zn - t, sb);
            c.Walls(-s + 2, 6, zs + 2, s - 2, 6, zn - 2, "dark_planks");
            for (int x = -s; x <= s; x++) for (int z = zs; z <= zn; z++)
                if ((x == -s || x == s || z == zs || z == zn) && ((x + z) & 1) == 0) c.Set(x, 7, z, sb);
            // Mossy patches low on the outside.
            var dice = new Dice(77);
            for (int i = 0; i < 160; i++)
            {
                int side = dice.Next(4), a = dice.Next(2 * s + 1) - s, y = dice.Next(3);
                if (side == 0) c.Set(a, y, zs, "mossy_cobble"); else if (side == 1) c.Set(a, y, zn, "mossy_cobble"); else if (side == 2) c.Set(-s, y, cz + a, "mossy_cobble"); else c.Set(s, y, cz + a, "mossy_cobble");
            }

            // The gatehouse: two square towers and an arch with the portcullis drawn up.
            foreach (int sx in new[] { -1, 1 })
            {
                c.Fill(sx * 3, 0, zs - 2, sx * 6, 10, zs + 3, sb);
                for (int x = 3; x <= 6; x++) for (int z = zs - 2; z <= zs + 3; z++)
                    if ((x == 3 || x == 6 || z == zs - 2 || z == zs + 3) && ((x + z) & 1) == 0) c.Set(sx * x, 11, z, sb);
                c.Set(sx * 4, 11, zs, "lantern");
                c.Set(sx * 3, 2, zs - 3, "lantern");
            }
            c.Fill(-2, 0, zs - 2, 2, 3, zs + 3, 0);
            c.Fill(-2, 4, zs - 2, 2, 8, zs + 3, sb);
            c.Fill(-2, 3, zs, 2, 3, zs, "iron_bars");
            for (int x = -2; x <= 2; x += 2) c.Set(x, 9, zs - 2, sb);
            c.Fill(-2, -1, zs - 3, 2, -1, cz - 8, cob);                          // the road in

            // Corner towers with tiled roofs, a ladder inside and doors onto the wall walk.
            foreach (int sx in new[] { -1, 1 })
                foreach (int tz in new[] { zs, zn })
                {
                    int tx = sx * s, dz = tz == zs ? 1 : -1;
                    c.Fill(tx - 4, 0, tz - 4, tx + 4, 13, tz + 4, sb);
                    c.Fill(tx - 3, 0, tz - 3, tx + 3, 13, tz + 3, 0);
                    c.Fill(tx - 3, 6, tz - 3, tx + 3, 6, tz + 3, "oak_planks");
                    c.Fill(tx - 3, -1, tz - 3, tx + 3, -1, tz + 3, cob);
                    for (int i = 0; i < 5; i++) c.Fill(tx - 5 + i, 14 + i, tz - 5 + i, tx + 5 - i, 14 + i, tz + 5 - i, "roof_tiles");
                    c.Set(tx, 19, tz, "lantern");
                    // The ladder up the outer wall, through the floor.
                    for (int y = 0; y <= 7; y++) c.Turned(tx + sx * 3, y, tz, "ladder", sx > 0 ? 1 : 3);
                    // Ground door toward the courtyard, and openings onto both walks.
                    c.Fill(tx - sx * 4, 0, tz + dz * 3, tx - sx * 4, 1, tz + dz * 3, 0);
                    c.Fill(tx - sx * 4, 7, tz, tx - sx * 4, 8, tz + dz, 0);
                    c.Fill(tx, 7, tz + dz * 4, tx - sx, 8, tz + dz * 4, 0);
                    // Arrow slits and a lantern.
                    c.Fill(tx + sx * 4, 9, tz, tx + sx * 4, 10, tz, "iron_bars"); c.Fill(tx, 9, tz - dz * 4, tx, 10, tz - dz * 4, "iron_bars");
                    c.Set(tx - sx * 2, 7, tz - dz * 2, "lantern"); c.Set(tx - sx * 2, 0, tz - dz * 2, "lantern");
                    c.Set(tx - sx * 2, 7, tz + dz * 2, "chest"); c.Set(tx + sx * 2, 0, tz + dz * 2, "barrel");
                }
            // Steps up to the wall walk beside the gate.
            for (int i = 0; i < 7; i++)
            {
                c.Turned(-8 - i, i, zs + 2, "stone_brick_stairs", 3); c.Fill(-8 - i, 0, zs + 2, -8 - i, i - 1, zs + 2, sb);
                c.Turned(8 + i, i, zs + 2, "stone_brick_stairs", 1); c.Fill(8 + i, 0, zs + 2, 8 + i, i - 1, zs + 2, sb);
            }

            // The courtyard: a well, a stable, a smithy, lamps and a few flowers.
            int wx = -13, wz = zs + 10;
            c.Walls(wx - 1, 0, wz - 1, wx + 1, 0, wz + 1, "cobble_wall"); c.Set(wx, -1, wz, Blocks.Water); c.Set(wx, -2, wz, Blocks.Water);
            c.Set(wx - 1, 1, wz - 1, "oak_fence"); c.Set(wx + 1, 1, wz + 1, "oak_fence"); c.Set(wx - 1, 2, wz - 1, "oak_fence"); c.Set(wx + 1, 2, wz + 1, "oak_fence");
            c.Fill(wx - 1, 3, wz - 1, wx + 1, 3, wz + 1, "oak_slab");
            // Stable (east): posts, a roof, hay and fenced stalls.
            for (int z = zs + 5; z <= zs + 13; z += 4) { c.Fill(14, 0, z, 14, 2, z, "oak_log"); c.Fill(21, 0, z, 21, 2, z, "oak_log"); }
            c.Fill(13, 3, zs + 4, 22, 3, zs + 14, "dark_slab");
            for (int z = zs + 5; z <= zs + 13; z += 4) for (int x = 15; x <= 20; x++) if (z != zs + 5) c.Set(x, 0, z, "oak_fence");
            c.Set(20, 0, zs + 6, "hay_bale"); c.Set(20, 1, zs + 6, "hay_bale"); c.Set(19, 0, zs + 6, "hay_bale"); c.Set(20, 0, zs + 10, "hay_bale"); c.Set(20, 0, zs + 11, "hay_bale");
            c.Set(15, 0, zs + 12, "barrel"); c.Set(15, 0, zs + 7, "crate");
            // Smithy (west, by the north wall): a roof over a hearth, a furnace and an anvil.
            c.Fill(-21, -1, zn - 12, -14, -1, zn - 5, cob);
            foreach (var (px, pz) in new[] { (-21, zn - 12), (-14, zn - 12), (-21, zn - 5), (-14, zn - 5) }) c.Fill(px, 0, pz, px, 2, pz, "stone_pillar");
            c.Fill(-22, 3, zn - 13, -13, 3, zn - 4, "slate_tile_slab");
            c.Set(-20, 0, zn - 6, "hearth"); c.Turned(-18, 0, zn - 6, "furnace", 2); c.Set(-16, 0, zn - 8, "anvil"); c.Set(-20, 0, zn - 10, "workbench");
            c.Set(-16, 0, zn - 6, "chest"); c.Set(-15, 0, zn - 11, "barrel"); c.Set(-18, 2, zn - 9, "lantern");
            // Training yard (east, north): hay targets on fence posts.
            for (int i = 0; i < 3; i++) { c.Set(14 + i * 3, 0, zn - 8, "oak_fence"); c.Set(14 + i * 3, 1, zn - 8, "hay_bale"); }
            foreach (var (lx, lz) in new[] { (-4, zs + 8), (4, zs + 8), (-4, cz - 10), (4, cz - 10), (-14, cz), (14, cz), (-14, cz + 14), (14, cz + 14) }) c.Set(lx, 0, lz, "lamp_post");
            for (int i = 0; i < 40; i++)
            {
                int x = dice.Next(40) - 20, z = zs + 4 + dice.Next(40);
                if (Math.Abs(x) < 13 && z > cz - 10 || c.Get(x, 0, z) != 0 || c.Get(x, -1, z) != Leave) continue;
                c.Set(x, 0, z, new[] { "red_flower", "yellow_flower", "blue_flower", "tall_grass", "fern" }[dice.Next(5)]);
            }

            // The keep.
            int kx = 10, k0 = cz - 7, k1 = cz + 13;                              // x -10..10, z 30..50
            c.Fill(-kx, -1, k0, kx, -1, k1, "polished_stone");
            c.Walls(-kx, 0, k0, kx, 17, k1, sb);
            c.Fill(-kx + 1, 0, k0 + 1, kx - 1, 17, k1 - 1, 0);
            foreach (int fy in new[] { 5, 11 }) c.Fill(-kx + 1, fy, k0 + 1, kx - 1, fy, k1 - 1, "dark_planks");
            c.Fill(-kx + 1, 17, k0 + 1, kx - 1, 17, k1 - 1, sb);
            // Corner buttresses and battlements.
            foreach (int sx in new[] { -1, 1 }) foreach (int z in new[] { k0, k1 })
            {
                int dzz = z == k0 ? -1 : 1;
                c.Fill(sx * kx, 0, z, sx * (kx + 1), 19, z + dzz, sb); c.Fill(sx * (kx + 1), 0, z, sx * (kx + 1), 19, z, sb);
                c.Set(sx * kx, 20, z, "lantern");
            }
            for (int x = -kx; x <= kx; x++) for (int z = k0; z <= k1; z++)
                if ((x == -kx || x == kx || z == k0 || z == k1) && ((x + z) & 1) == 0) c.Set(x, 18, z, sb);
            // Windows on every floor.
            foreach (int fy in new[] { 1, 7, 13 })
            {
                for (int x = -7; x <= 7; x += 3) { if (fy == 1 && Math.Abs(x) < 3) continue; c.Fill(x, fy, k0, x, fy + 1, k0, "lattice_window"); c.Fill(x, fy, k1, x, fy + 1, k1, "lattice_window"); }
                for (int z = k0 + 4; z <= k1 - 3; z += 4) { c.Fill(-kx, fy, z, -kx, fy + 1, z, "lattice_window"); c.Fill(kx, fy, z, kx, fy + 1, z, "lattice_window"); }
            }
            // The great door, with banners either side.
            c.Fill(-1, 0, k0, 1, 2, k0, 0);
            foreach (int sx in new[] { -1, 1 }) { c.Fill(sx * 3, 3, k0 - 1, sx * 3, 6, k0 - 1, "red_wool"); c.Set(sx * 3, 7, k0 - 1, "yellow_wool"); c.Set(sx * 2, 0, k0 - 1, "lantern"); }
            c.Fill(-1, -1, k0 - 4, 1, -1, k0 - 1, "polished_stone");
            // Stairs: east wall up to the bedchambers, west wall up to the throne room, east again to the roof.
            int sz = k0 + 3;
            for (int i = 0; i < 6; i++)
            {
                c.Turned(kx - 1, i, sz + i, "stone_brick_stairs", 0); c.Fill(kx - 1, 0, sz + i, kx - 1, i - 1, sz + i, sb); c.Fill(kx - 1, i + 1, sz + i, kx - 1, 8, sz + i, 0);
                c.Turned(-kx + 1, 6 + i, sz + i, "dark_stairs", 0); c.Fill(-kx + 1, 7 + i, sz + i, -kx + 1, 14, sz + i, 0);
                c.Turned(kx - 1, 12 + i, sz + i, "stone_brick_stairs", 0); c.Fill(kx - 1, 13 + i, sz + i, kx - 1, 20, sz + i, 0);
            }
            // Guard rails round the stair wells.
            for (int z = sz - 1; z <= sz + 5; z++) { c.Set(kx - 2, 6, z, "dark_fence"); c.Set(-kx + 2, 12, z, "dark_fence"); }
            c.Set(kx - 1, 6, sz - 1, "dark_fence"); c.Set(-kx + 1, 12, sz - 1, "dark_fence");

            // Great hall: a carpet up the middle, long tables with benches, the hearth and chandeliers.
            c.Fill(-1, 0, k0 + 1, 1, 0, k1 - 3, "carpet_red");
            foreach (int sx in new[] { -1, 1 })
            {
                for (int z = k0 + 4; z <= k1 - 6; z++)
                {
                    c.Set(sx * 5, 0, z, "table"); c.Turned(sx * 6, 0, z, "bench", sx > 0 ? 3 : 1); c.Turned(sx * 4, 0, z, "bench", sx > 0 ? 1 : 3);
                    if ((z & 1) == 0) c.Set(sx * 5, 1, z, "candle");
                }
                c.Turned(sx * 8, 0, k1 - 1, "cabinet", 2); c.Set(sx * 7, 0, k1 - 1, "barrel");
            }
            c.Set(0, 0, k1 - 1, "hearth"); c.Set(-1, 0, k1 - 1, sb); c.Set(1, 0, k1 - 1, sb); c.Fill(-1, 1, k1 - 1, 1, 1, k1 - 1, "stone_brick_slab");
            c.Turned(0, 3, k1 - 1, "painting_forest", 0);
            foreach (int z in new[] { k0 + 5, k0 + 10, k0 + 15 }) c.Set(0, 4, z, "chandelier");
            foreach (int sx in new[] { -1, 1 }) for (int z = k0 + 2; z <= k1 - 2; z += 4) c.Set(sx * 2, 0, z, 0);

            // Bedchambers: four beds, wardrobes, a study with bookshelves, and a sitting corner.
            int y1 = 6;
            c.Fill(-3, y1, k0 + 8, 3, y1, k1 - 2, "carpet_blue");
            foreach (int sx in new[] { -1, 1 })
            {
                c.Bed(sx * 5, y1, k1 - 2, 2); c.Bed(sx * 7, y1, k1 - 2, 2); c.Set(sx * 6, y1, k1 - 1, "cabinet");
                c.Turned(sx * 6, y1 + 1, k1 - 1, "candle", 0);
                c.Turned(sx * 4, y1, k1 - 1, "wardrobe", 2);
            }
            for (int x = -4; x <= 4; x++) { if (x == 0) continue; c.Fill(x, y1, k0 + 1, x, y1 + 2, k0 + 1, "bookshelf"); }
            c.Set(0, y1, k0 + 3, "table"); c.Set(0, y1 + 1, k0 + 3, "candle"); c.Turned(0, y1, k0 + 4, "chair", 2); c.Turned(-1, y1, k0 + 3, "chair", 1); c.Turned(1, y1, k0 + 3, "chair", 3);
            c.Turned(-2, y1, k1 - 1, "sofa_red", 2); c.Turned(2, y1, k1 - 1, "sofa_red", 2); c.Set(0, y1, k1 - 1, "floor_lamp");
            c.Turned(kx - 1, y1 + 1, k1 - 6, "mirror", 1); c.Turned(-kx + 1, y1 + 1, k1 - 4, "painting_lake", 3);
            foreach (int z in new[] { k0 + 6, k0 + 14 }) c.Set(0, 10, z, "chandelier");

            // Throne room: a purple carpet between pillars to a golden throne.
            int y2 = 12;
            c.Fill(-1, y2, k0 + 2, 1, y2, k1 - 4, "carpet_purple");
            for (int z = k0 + 4; z <= k1 - 6; z += 4) foreach (int sx in new[] { -1, 1 }) { c.Fill(sx * 4, y2, z, sx * 4, y2 + 4, z, "stone_pillar"); c.Set(sx * 6, y2, z, "lamp_post"); }
            c.Fill(-2, y2, k1 - 3, 2, y2, k1 - 1, "polished_stone");
            c.Turned(0, y2 + 1, k1 - 2, "polished_stone_stairs", 0); c.Fill(0, y2 + 1, k1 - 1, 0, y2 + 3, k1 - 1, "gold_block");
            c.Set(-1, y2 + 1, k1 - 2, "gold_block"); c.Set(1, y2 + 1, k1 - 2, "gold_block");
            foreach (int sx in new[] { -1, 1 }) { c.Fill(sx * 3, y2, k1 - 1, sx * 3, y2 + 4, k1 - 1, "purple_wool"); c.Set(sx * 7, y2, k1 - 1, "chest"); c.Turned(sx * (kx - 1), y2 + 1, k0 + 10, sx > 0 ? "painting_sunset" : "painting_lake", sx > 0 ? 1 : 3); }
            foreach (int z in new[] { k0 + 6, k0 + 14 }) c.Set(0, 16, z, "chandelier");

            // The roof: a flag on a pole.
            c.Fill(0, 18, cz + 3, 0, 24, cz + 3, "oak_fence"); c.Fill(1, 22, cz + 3, 4, 24, cz + 3, "red_wool"); c.Fill(1, 23, cz + 3, 4, 23, cz + 3, "yellow_wool");
            for (int z = sz - 1; z <= sz + 5; z++) c.Set(kx - 2, 18, z, "stone_brick_wall");

            // The dungeon under the keep: down the steps by the west wall.
            c.Fill(-kx, -8, k0, kx, -2, k1, "cobble");
            c.Fill(-kx + 1, -7, k0 + 1, kx - 1, -3, k1 - 1, 0);
            c.Fill(-kx + 1, -8, k0 + 1, kx - 1, -8, k1 - 1, "slate");
            for (int i = 0; i < 6; i++)
            {
                int z = k1 - 8 + i;
                c.Turned(-kx + 1, -1 - i, z, "cobble_stairs", 2); c.Fill(-kx + 1, -i, z, -kx + 1, 2, z, 0);
                c.Fill(-kx + 1, -8, z, -kx + 1, -2 - i, z, "cobble");
            }
            c.Set(-kx + 2, 0, k1 - 9, "dark_fence"); for (int z = k1 - 8; z <= k1 - 3; z++) c.Set(-kx + 2, 0, z, "dark_fence");
            c.Fill(-kx + 1, -7, k1 - 2, -kx + 1, -4, k1 - 1, 0);
            // Three cells along the east side behind iron bars, and the treasury at the south end.
            for (int z = k0 + 4; z <= k1 - 2; z += 5)
            {
                c.Fill(3, -7, z, kx - 1, -3, z, "cobble");
                c.Fill(3, -7, z + 1, 3, -4, z + 4, "iron_bars"); c.Fill(3, -7, z + 2, 3, -6, z + 2, 0); c.Door(3, -7, z + 2, "dark_door", 1);
                c.Set(kx - 2, -7, z + 3, "hay_bale"); c.Set(6, -4, z + 2, "lantern");
            }
            c.Fill(-kx + 1, -7, k0 + 4, 2, -3, k0 + 4, "cobble"); c.Fill(-3, -7, k0 + 4, -3, -6, k0 + 4, 0); c.Door(-3, -7, k0 + 4, "dark_door", 0);
            c.Set(-6, -7, k0 + 2, "chest"); c.Set(-5, -7, k0 + 2, "gold_block"); c.Set(-7, -7, k0 + 2, "chest"); c.Set(-1, -7, k0 + 2, "barrel"); c.Set(-4, -5, k0 + 2, "lantern");
            foreach (int z in new[] { k0 + 8, k0 + 14 }) c.Set(-4, -4, z, "lantern");
            for (int i = 0; i < 30; i++) c.Set(-kx + 1 + dice.Next(2 * kx - 1), -8, k0 + 1 + dice.Next(k1 - k0 - 1), "mossy_cobble");
            return c;
        }

        // ------------------------------------------------------------------------------------------ parkour
        // Six stages of jumps over water, each its own colour and a little harder, with a gold landing between
        // stages and a tower at the end. Every jump is one the castaway can make: at most three across on the level,
        // two when it also goes up one.
        static Canvas Parkour()
        {
            var c = new Canvas(-26, -3, -4, 26, 60, 230); Pad(c);
            // The pool underneath: fall in, swim back, try again.
            c.Fill(-26, -2, 6, 26, -1, 230, Blocks.Water); c.Fill(-26, -3, 6, 26, -3, 230, "sand");
            c.Fill(-26, 0, 6, 26, 60, 230, 0);
            c.Walls(-26, -1, 6, 26, -1, 230, "polished_stone");
            c.Fill(-3, -1, 2, 3, -1, 8, "polished_stone"); c.Set(-3, 0, 8, "lamp_post"); c.Set(3, 0, 8, "lamp_post");

            var dice = new Dice(2024);
            string[] colour = { "lime_wool", "yellow_wool", "orange_wool", "red_wool", "light_blue_wool", "purple_wool" };
            int x = 0, y = 0, z = 8;                                             // the far edge of what we stand on
            for (int stage = 0; stage < 6; stage++)
            {
                ushort block = B(colour[stage]);
                int jumps = 9 + stage;
                for (int j = 0; j < jumps; j++)
                {
                    // Harder stages have longer gaps and smaller landings.
                    int gap = stage == 0 ? 1 + dice.Next(2) : stage < 3 ? 2 : 2 + dice.Next(2);
                    int up = dice.Next(3) == 0 ? 0 : 1; if (gap == 3) up = 0;
                    if (y > 40) up = 0;
                    int side = dice.Next(3) - 1; if (Math.Abs(x + side * 2) > 18) side = -side; if (gap == 3) side = 0;
                    int len = stage < 2 ? 2 : stage == 4 ? 4 : 1, wide = stage == 0 ? 1 : 0;
                    x += side * (gap == 1 ? 1 : 2); if (side != 0 && gap > 1) gap--;
                    z += gap + 1; y += up;
                    if (stage == 2 && j % 3 == 2)
                    {
                        // A ladder tower: climb four, step off the top.
                        c.Fill(x, y - 1, z, x, y + 3, z, block);
                        for (int k = 0; k <= 3; k++) c.Turned(x, y + k, z - 1, "ladder", 0);
                        c.Set(x, y - 1, z - 1, block);
                        y += 4; z += 0;
                        continue;
                    }
                    ushort use = stage == 4 ? B("ice") : stage == 5 && (j & 1) == 1 ? B("glass") : block;
                    c.Fill(x - wide, y - 1, z, x + wide, y - 1, z + len - 1, use);
                    if (stage == 3 && j % 4 == 1) c.Set(x, y - 1, z, "stone_brick_slab");     // a low step: mind your footing
                    z += len - 1;
                }
                // The landing between stages: gold corners, lamps, a chest of snacks.
                z += 3;
                c.Fill(x - 2, y - 1, z, x + 2, y - 1, z + 4, "polished_stone");
                foreach (int sx in new[] { -2, 2 }) { c.Set(x + sx, y - 1, z, "gold_block"); c.Set(x + sx, y - 1, z + 4, "gold_block"); c.Set(x + sx, y, z + 2, "lamp_post"); }
                c.Set(x - 2, y, z + 4, "chest"); c.Set(x + 2, y, z, block);
                // A ladder down to the water for whoever wants out, and one up from it.
                for (int k = -1; k < y - 1; k++) c.Turned(x + 2, k, z + 5, "ladder", 2);
                c.Fill(x + 2, -1, z + 6, x + 2, Math.Max(-1, y - 2), z + 6, "polished_stone");
                z += 4;
            }
            // The finish: a tower with a gold floor, a roof, chandeliers and the prize chests.
            z += 3;
            c.Fill(x - 4, y - 1, z, x + 4, y - 1, z + 8, "gold_block");
            c.Fill(x - 3, y - 1, z + 1, x + 3, y - 1, z + 7, "tiles_white");
            foreach (int sx in new[] { -4, 4 }) foreach (int dz in new[] { 0, 8 }) c.Fill(x + sx, y, z + dz, x + sx, y + 4, z + dz, "stone_pillar");
            for (int i = 0; i < 5; i++) c.Fill(x - 5 + i, y + 5 + i, z - 1 + i, x + 5 - i, y + 5 + i, z + 9 - i, i == 0 ? B("gold_block") : B("roof_tiles"));
            c.Fill(x - 4, y + 5, z, x + 4, y + 5, z + 8, 0); c.Fill(x - 3, y + 6, z + 1, x + 3, y + 6, z + 7, 0);
            c.Set(x, y + 5, z + 4, "chandelier");
            c.Set(x - 1, y, z + 7, "chest"); c.Set(x + 1, y, z + 7, "chest"); c.Set(x, y, z + 7, "jade_block"); c.Set(x, y + 1, z + 7, "lantern");
            c.Fill(x - 2, y, z + 3, x + 2, y, z + 5, "carpet_red");
            for (int k = 0; k < 4; k++) { c.Fill(x - 3 + k * 2, y, z + 8, x - 3 + k * 2, y + 2, z + 8, colour[k]); }
            return c;
        }

        // ------------------------------------------------------------------------------------------ maze
        // A hedge maze, twenty cells each way, with a garden and a prize in the middle and a lookout by the gate.
        static Canvas Maze()
        {
            const int N = 20, P = 3, X0 = -30, Z0 = 10;
            var c = new Canvas(-36, -2, -4, 36, 14, 76); Pad(c);
            c.Fill(-36, 0, 2, 36, 14, 76, 0);
            // Carve with a depth-first walk.
            var open = new bool[N, N, 2];                                         // [cx, cz, 0] wall to +X is open; [.., 1] to +Z
            var seen = new bool[N, N];
            var dice = new Dice(31337);
            var stack = new Stack<(int, int)>();
            stack.Push((N / 2, 0)); seen[N / 2, 0] = true;
            while (stack.Count > 0)
            {
                var (cx, cy) = stack.Peek();
                int start = dice.Next(4); bool moved = false;
                for (int k = 0; k < 4 && !moved; k++)
                {
                    int d = (start + k) & 3, nx = cx + DX[d], ny = cy + DZ[d];
                    if ((uint)nx >= N || (uint)ny >= N || seen[nx, ny]) continue;
                    if (d == 1) open[cx, cy, 0] = true; else if (d == 3) open[nx, ny, 0] = true; else if (d == 0) open[cx, cy, 1] = true; else open[nx, ny, 1] = true;
                    seen[nx, ny] = true; stack.Push((nx, ny)); moved = true;
                }
                if (!moved) stack.Pop();
            }
            // A few extra gaps so there is more than one way round.
            for (int i = 0; i < 14; i++) open[1 + dice.Next(N - 2), 1 + dice.Next(N - 2), dice.Next(2)] = true;
            // The garden in the middle: four cells made one.
            int m = N / 2 - 1;
            open[m, m, 0] = open[m, m, 1] = open[m + 1, m, 1] = open[m, m + 1, 0] = true;

            ushort hedge = B("oak_leaves"), path = B("gravel");
            int w = N * P;
            c.Fill(X0, -1, Z0, X0 + w, -1, Z0 + w, path);
            for (int gx = 0; gx <= w; gx++)
                for (int gz = 0; gz <= w; gz++)
                {
                    bool onX = gx % P == 0, onZ = gz % P == 0;
                    if (!onX && !onZ) continue;
                    bool wall;
                    if (onX && onZ) wall = true;
                    else if (onX) { int cx = gx / P - 1, cy = gz / P; wall = cx < 0 || cx >= N - 1 || !open[cx, cy, 0]; }
                    else { int cx = gx / P, cy = gz / P - 1; wall = cy < 0 || cy >= N - 1 || !open[cx, cy, 1]; }
                    if (wall) { c.Fill(X0 + gx, 0, Z0 + gz, X0 + gx, 2, Z0 + gz, hedge); c.Set(X0 + gx, -1, Z0 + gz, Blocks.Grass); }
                }
            // The gate in the south hedge, under an arch.
            int ex = X0 + (N / 2) * P;
            c.Fill(ex + 1, 0, Z0, ex + 2, 2, Z0, 0); c.Fill(ex, 3, Z0, ex + 3, 3, Z0, hedge);
            c.Fill(ex + 1, -1, 3, ex + 2, -1, Z0, path);
            c.Set(ex, 0, Z0 - 1, "lamp_post"); c.Set(ex + 3, 0, Z0 - 1, "lamp_post");
            // Lamps in dead ends (cells with one way out), flowers here and there.
            for (int cx = 0; cx < N; cx++)
                for (int cy = 0; cy < N; cy++)
                {
                    int ways = (open[cx, cy, 0] && cx < N - 1 ? 1 : 0) + (open[cx, cy, 1] && cy < N - 1 ? 1 : 0) + (cx > 0 && open[cx - 1, cy, 0] ? 1 : 0) + (cy > 0 && open[cx, cy - 1, 1] ? 1 : 0);
                    int bx = X0 + cx * P + 1, bz = Z0 + cy * P + 1;
                    if (ways == 1 && !(cx == N / 2 && cy == 0)) c.Set(bx, 0, bz, dice.Next(3) == 0 ? B("flower_pot") : B("lamp_post"));
                    else if (dice.Next(9) == 0) c.Set(bx + 1, 0, bz + 1, new[] { "red_flower", "yellow_flower", "blue_flower" }[dice.Next(3)]);
                }
            // The middle: a fountain, benches, and the prize.
            int gx0 = X0 + m * P + 1, gz0 = Z0 + m * P + 1;                        // a 5 x 5 clearing
            c.Fill(gx0, 0, gz0, gx0 + 4, 2, gz0 + 4, 0);
            c.Fill(gx0, -1, gz0, gx0 + 4, -1, gz0 + 4, "tiles_white");
            c.Set(gx0 + 2, -1, gz0 + 2, Blocks.Water); c.Set(gx0 + 2, 0, gz0 + 2, "gold_block"); c.Set(gx0 + 2, 1, gz0 + 2, "lantern");
            c.Set(gx0, 0, gz0, "chest"); c.Set(gx0 + 4, 0, gz0 + 4, "chest"); c.Set(gx0 + 4, 0, gz0, "flower_pot_yellow"); c.Set(gx0, 0, gz0 + 4, "flower_pot_blue");
            // The lookout: climb it to see the way before going in.
            int tx = ex - 8;
            c.Fill(tx, 0, 4, tx + 2, 7, 6, "oak_log"); c.Fill(tx + 1, 0, 5, tx + 1, 7, 5, 0);
            c.Fill(tx, 0, 4, tx + 2, 6, 6, 0);
            foreach (var (px, pz) in new[] { (tx, 4), (tx + 2, 4), (tx, 6), (tx + 2, 6) }) c.Fill(px, 0, pz, px, 6, pz, "oak_log");
            c.Fill(tx - 1, 7, 3, tx + 3, 7, 7, "oak_planks"); c.Set(tx + 1, 7, 4, 0);
            for (int y = 0; y <= 7; y++) c.Turned(tx + 1, y, 4, "ladder", 0);
            c.Fill(tx + 1, 0, 5, tx + 1, 6, 5, "oak_log");
            c.Walls(tx - 1, 8, 3, tx + 3, 8, 7, "oak_fence");
            foreach (var (px, pz) in new[] { (tx - 1, 3), (tx + 3, 3), (tx - 1, 7), (tx + 3, 7) }) c.Fill(px, 9, pz, px, 10, pz, "oak_fence");
            c.Fill(tx - 2, 11, 2, tx + 4, 11, 8, "roof_tile_slab"); c.Set(tx + 1, 10, 5, "lantern");
            return c;
        }

        // ------------------------------------------------------------------------------------------ town
        // A market town: a street of houses up to a square with a fountain and stalls, an inn, a bakery, a library
        // and a hall round it. Every building is furnished.
        static Canvas Town()
        {
            var c = new Canvas(-44, -2, -4, 44, 30, 100); Pad(c);
            c.Fill(-44, 0, 2, 44, 30, 100, 0);
            ushort cob = B("cobble");
            var dice = new Dice(909);
            // Streets: one north from the start, the square, and lanes east and west.
            c.Fill(-2, -1, 2, 2, -1, 46, cob);
            c.Fill(-13, -1, 46, 13, -1, 72, "polished_stone");
            for (int x = -13; x <= 13; x++) for (int z = 46; z <= 72; z++) if (((x + 100) / 2 + z / 2) % 2 == 0) c.Set(x, -1, z, "stone_bricks");
            c.Fill(-40, -1, 57, -14, -1, 60, cob); c.Fill(14, -1, 57, 40, -1, 60, cob);
            for (int i = 0; i < 60; i++) c.Set(-2 + dice.Next(5), -1, 2 + dice.Next(44), dice.Next(2) == 0 ? B("mossy_cobble") : B("gravel"));
            for (int z = 6; z <= 42; z += 9) { c.Set(-3, 0, z, "lamp_post"); c.Set(3, 0, z, "lamp_post"); }

            // The fountain.
            int fx = 0, fz = 59;
            c.Fill(fx - 3, 0, fz - 3, fx + 3, 0, fz + 3, "polished_stone"); c.Fill(fx - 2, 0, fz - 2, fx + 2, 0, fz + 2, Blocks.Water);
            c.Fill(fx - 2, -1, fz - 2, fx + 2, -1, fz + 2, "tiles_light_blue");
            c.Fill(fx, 0, fz, fx, 2, fz, "stone_pillar"); c.Set(fx, 3, fz, Blocks.Water);
            foreach (var (bx, bz, f) in new[] { (fx - 5, fz, 1), (fx + 5, fz, 3), (fx, fz - 5, 0), (fx, fz + 5, 2) }) c.Turned(bx, 0, bz, "bench", f);
            foreach (var (lx, lz) in new[] { (-12, 47), (12, 47), (-12, 71), (12, 71) }) c.Set(lx, 0, lz, "lamp_post");

            // Market stalls: four posts, a striped awning, a counter and goods.
            string[] awning = { "red_wool", "yellow_wool", "light_blue_wool", "lime_wool" };
            int si = 0;
            foreach (var (sx, sz) in new[] { (-10, 49), (7, 49), (-10, 66), (7, 66) })
            {
                foreach (var (px, pz) in new[] { (sx, sz), (sx + 3, sz), (sx, sz + 2), (sx + 3, sz + 2) }) c.Fill(px, 0, pz, px, 2, pz, "oak_fence");
                for (int x = sx - 1; x <= sx + 4; x++) for (int z = sz - 1; z <= sz + 3; z++) c.Set(x, 3, z, (x & 1) == 0 ? awning[si] : "white_wool");
                c.Set(sx + 1, 0, sz, "counter"); c.Set(sx + 2, 0, sz, "counter");
                c.Set(sx + 1, 1, sz, si == 0 ? "flower_pot" : si == 1 ? "hay_bale" : si == 2 ? "flower_pot_blue" : "candle");
                c.Set(sx + 1, 0, sz + 2, "crate"); c.Set(sx + 2, 0, sz + 2, "barrel");
                si++;
            }

            // Houses along the street, doors facing it.
            House(c, ref dice, -19, 8, 9, 8, 1, "plaster", "oak_log", "roof_tile", "oak_door", 1, 0);
            House(c, ref dice, 7, 8, 9, 8, 1, "plaster_yellow", "dark_planks", "slate_tile", "dark_door", 3, 1);
            House(c, ref dice, -21, 21, 11, 9, 2, "timber_frame", "oak_log", "dark", "birch_door", 1, 2);
            House(c, ref dice, 7, 21, 11, 9, 2, "plaster_light_blue", "birch_log", "roof_tile", "pine_door", 3, 3);
            House(c, ref dice, -19, 34, 9, 8, 1, "plaster_pink", "pine_log", "slate_tile", "oak_door", 1, 1);
            House(c, ref dice, 7, 34, 9, 8, 1, "clay_bricks", "stone_bricks", "dark", "dark_door", 3, 0);
            // Round the square: the inn (west), the bakery (east), the library and the hall (north).
            House(c, ref dice, -31, 44, 13, 11, 2, "timber_frame", "dark_planks", "roof_tile", "dark_door", 1, 4);
            House(c, ref dice, 17, 44, 12, 11, 2, "plaster_orange", "oak_log", "slate_tile", "oak_door", 3, 5);
            House(c, ref dice, -31, 63, 13, 10, 1, "stone_bricks", "stone_bricks", "slate_tile", "birch_door", 1, 6);
            House(c, ref dice, 17, 63, 12, 10, 1, "plaster_lime", "birch_log", "roof_tile", "pine_door", 3, 3);
            House(c, ref dice, -9, 78, 19, 12, 2, "sandstone", "polished_stone", "clay_brick", "dark_door", 2, 7);

            // Gardens: hedges, picket fences and flowers on any grass left near the houses.
            for (int i = 0; i < 260; i++)
            {
                int x = -40 + dice.Next(81), z = 4 + dice.Next(92);
                if (c.Get(x, 0, z) != 0 || c.Get(x, -1, z) != Leave) continue;
                c.Set(x, 0, z, new[] { "red_flower", "yellow_flower", "blue_flower", "tall_grass", "fern", "tall_grass" }[dice.Next(6)]);
            }
            foreach (var (hx, hz, len) in new[] { (-21, 6, 12), (7, 6, 12), (-23, 19, 14), (7, 19, 14) })
                for (int x = hx; x < hx + len; x++) if (c.Get(x, 0, hz) == 0 || c.Get(x, 0, hz) == Leave) c.Set(x, 0, hz, hx < 0 == (hz < 10) ? "picket_fence" : "hedge");
            return c;
        }

        // One furnished building. (x0, z0) is its low corner, w by d its size, `side` the wall its door is in
        // (0 +Z, 1 +X, 2 -Z, 3 -X). The roof ridge runs along the longer side.
        static void House(Canvas c, ref Dice dice, int x0, int z0, int w, int d, int floors, string wall, string post, string roof, string door, int side, int style)
        {
            int x1 = x0 + w - 1, z1 = z0 + d - 1, top = floors * 4;                // walls are y 0..top-1
            string floor = style == 5 ? "tiles_white" : style == 7 ? "polished_stone" : style == 6 ? "dark_planks" : "oak_planks";
            c.Fill(x0, -1, z0, x1, -1, z1, floor);
            c.Fill(x0 - 1, -1, z0 - 1, x1 + 1, -1, z0 - 1, "cobble"); c.Fill(x0 - 1, -1, z1 + 1, x1 + 1, -1, z1 + 1, "cobble");
            c.Fill(x0 - 1, -1, z0, x0 - 1, -1, z1, "cobble"); c.Fill(x1 + 1, -1, z0, x1 + 1, -1, z1, "cobble");
            c.Walls(x0, 0, z0, x1, top - 1, z1, wall);
            c.Walls(x0, 0, z0, x1, 0, z1, wall == "stone_bricks" || wall == "sandstone" || wall == "clay_bricks" ? wall : "stone_bricks");
            c.Fill(x0 + 1, 0, z0 + 1, x1 - 1, top - 1, z1 - 1, 0);
            foreach (var (px, pz) in new[] { (x0, z0), (x1, z0), (x0, z1), (x1, z1) }) c.Fill(px, 0, pz, px, top - 1, pz, post);
            for (int f = 1; f < floors; f++) { c.Fill(x0 + 1, f * 4 - 1, z0 + 1, x1 - 1, f * 4 - 1, z1 - 1, "oak_planks"); c.Walls(x0, f * 4 - 1, z0, x1, f * 4 - 1, z1, post); }
            // Windows, two high, on every storey.
            for (int f = 0; f < floors; f++)
            {
                int wy = f * 4 + 1;
                for (int x = x0 + 2; x <= x1 - 2; x += 3) { c.Set(x, wy, z0, "lattice_window"); c.Set(x, wy, z1, "lattice_window"); }
                for (int z = z0 + 2; z <= z1 - 2; z += 3) { c.Set(x0, wy, z, "lattice_window"); c.Set(x1, wy, z, "lattice_window"); }
            }
            // The door, a step and a lamp.
            int dx = side == 1 ? x1 : side == 3 ? x0 : x0 + w / 2, dz = side == 0 ? z1 : side == 2 ? z0 : z0 + d / 2;
            c.Fill(dx, 0, dz, dx, 2, dz, wall); c.Door(dx, 0, dz, door, side);
            int ox = DX[side], oz = DZ[side];
            c.Set(dx + ox, -1, dz + oz, "cobble"); c.Set(dx + ox * 2, -1, dz + oz * 2, "cobble");
            c.Set(dx + ox + oz, 0, dz + oz + ox, "lantern");

            // The roof: stairs up both slopes, a ridge of slabs, gable ends in the wall material.
            bool alongX = w >= d;
            string stairs = roof + "_stairs", slab = roof + "_slab";
            int span = alongX ? d : w;
            for (int i = 0; ; i++)
            {
                int lo = -1 + i, hi = span - i;                                      // offsets across the span
                if (lo > hi) break;
                int y = top + i;
                if (alongX)
                {
                    if (lo == hi) c.Fill(x0 - 1, y, z0 + lo, x1 + 1, y, z0 + lo, slab);
                    else
                    {
                        for (int x = x0 - 1; x <= x1 + 1; x++) { c.Turned(x, y, z0 + lo, stairs, 0); c.Turned(x, y, z0 + hi, stairs, 2); }
                        if (lo + 1 <= hi - 1) { c.Fill(x0, y, z0 + lo + 1, x0, y, z0 + hi - 1, wall); c.Fill(x1, y, z0 + lo + 1, x1, y, z0 + hi - 1, wall); }
                    }
                }
                else
                {
                    if (lo == hi) c.Fill(x0 + lo, y, z0 - 1, x0 + lo, y, z1 + 1, slab);
                    else
                    {
                        for (int z = z0 - 1; z <= z1 + 1; z++) { c.Turned(x0 + lo, y, z, stairs, 1); c.Turned(x0 + hi, y, z, stairs, 3); }
                        if (lo + 1 <= hi - 1) { c.Fill(x0 + lo + 1, y, z0, x0 + hi - 1, y, z0, wall); c.Fill(x0 + lo + 1, y, z1, x0 + hi - 1, y, z1, wall); }
                    }
                }
                if (lo == hi) break;
            }
            // A chimney for homes with a hearth.
            if (style <= 2 || style == 4) { c.Fill(x0 + 1, top, z0 + 1, x0 + 1, top + span / 2 + 1, z0 + 1, "clay_bricks"); }

            // Inside. Positions are from the low corner, kept clear of the door.
            int ix0 = x0 + 1, iz0 = z0 + 1, ix1 = x1 - 1, iz1 = z1 - 1;
            c.Set((ix0 + ix1) / 2, 3, (iz0 + iz1) / 2, floors > 1 ? "lantern" : "chandelier");
            if (floors > 1)
            {
                // Stairs along the far wall from the door, up through a gap in the floor.
                int stx = side == 1 ? ix0 : ix1;
                for (int i = 0; i < 4; i++) { c.Turned(stx, i, iz0 + 1 + i, "oak_stairs", 0); c.Fill(stx, i + 1, iz0 + 1 + i, stx, 6, iz0 + 1 + i, 0); if (i > 0) c.Fill(stx, 0, iz0 + 1 + i, stx, i - 1, iz0 + 1 + i, "oak_planks"); }
                int rail = stx == ix0 ? stx + 1 : stx - 1;
                for (int z = iz0 + 1; z <= iz0 + 4; z++) c.Set(rail, 4, z, "oak_fence");
                c.Set((ix0 + ix1) / 2, 7, (iz0 + iz1) / 2, "chandelier");
                // Upstairs is always bedrooms.
                int by = 4, bx = stx == ix0 ? ix1 : ix0, toward = stx == ix0 ? 3 : 1;
                c.Bed(bx, by, iz1, 2); c.Bed(bx, by, iz1 - 3 > iz0 ? iz0 + 1 : iz0, 0);
                c.Turned(bx - DX[toward] * 0 + (stx == ix0 ? -2 : 2), by, iz1, "wardrobe", 2);
                c.Set(bx, by, iz1 - 2, "cabinet"); c.Set(bx, by + 1, iz1 - 2, "candle");
                for (int x = Math.Min(ix0, ix1) + 2; x <= Math.Max(ix0, ix1) - 2; x++) for (int z = iz0 + 2; z <= iz1 - 2; z++) if (c.Get(x, by, z) == 0 && x != rail) c.Set(x, by, z, style % 2 == 0 ? "carpet_red" : "carpet_green");
                c.Turned(stx == ix0 ? ix1 : ix0, by + 1, (iz0 + iz1) / 2, "mirror", stx == ix0 ? 1 : 3);
            }
            int fx = side == 1 ? ix0 : ix1, away = side == 1 ? 1 : -1;               // the wall across from a side door
            if (side == 2 || side == 0) { fx = ix0; away = 1; }
            switch (style)
            {
                case 0:                                                               // a cottage: bed, table, hearth
                    if (floors == 1) c.Bed(ix0 + (side == 3 ? w - 4 : 0) * 0 + (side == 1 ? 0 : w - 3), 0, iz1, 2);
                    c.Set(ix0 + (side == 1 ? 0 : w - 3), 0, iz0, "hearth");
                    c.Set(x0 + w / 2, 0, iz0 + 1, "table"); c.Turned(x0 + w / 2 - 1, 0, iz0 + 1, "chair", 1); c.Turned(x0 + w / 2 + 1, 0, iz0 + 1, "chair", 3); c.Set(x0 + w / 2, 1, iz0 + 1, "candle");
                    c.Set(side == 1 ? ix0 : ix1, 0, iz1 - 2 > iz0 + 2 ? iz0 + 3 : iz0 + 2, "chest");
                    break;
                case 1:                                                               // a kitchen and a sofa
                    c.Turned(fx, 0, iz0, "stove", away > 0 ? 1 : 3); c.Set(fx, 0, iz0 + 1, "counter"); c.Set(fx, 0, iz0 + 2, "sink"); c.Turned(fx, 0, iz0 + 3, "fridge", away > 0 ? 1 : 3);
                    c.Turned(fx, 0, iz1, "sofa_green", away > 0 ? 1 : 3); c.Turned(fx, 0, iz1 - 1, "sofa_green", away > 0 ? 1 : 3);
                    c.Set(fx + away * 2, 0, iz1 - 1, "table"); c.Set(fx + away * 2, 1, iz1 - 1, "flower_pot_yellow"); c.Set(fx + away * 2, 0, iz1, "floor_lamp");
                    if (floors == 1) c.Bed(side == 1 ? ix1 - 2 : ix0 + 2, 0, iz0, 0);
                    break;
                case 2:                                                               // a family home: sofas round a rug, a piano
                    for (int x = x0 + 3; x <= x1 - 3; x++) for (int z = z0 + 3; z <= z1 - 3; z++) c.Set(x, 0, z, "carpet_blue");
                    c.Turned(x0 + w / 2, 0, iz1, "sofa_blue", 2); c.Turned(x0 + w / 2 - 1, 0, iz1, "sofa_blue", 2); c.Turned(x0 + w / 2 + 1, 0, iz1, "sofa_blue", 2);
                    c.Turned(x0 + w / 2, 0, iz0, "screen", 0); c.Set(x0 + w / 2 - 2, 0, iz0, "bookshelf"); c.Set(x0 + w / 2 + 2, 0, iz0, "hearth");
                    c.Turned(fx, 0, iz1 - 1, "piano", away > 0 ? 1 : 3);
                    c.Turned(x0 + w / 2 - 1, 2, iz0, "painting_sunset", 2);
                    break;
                case 3:                                                               // a workshop
                    c.Set(fx, 0, iz0, "workbench"); c.Turned(fx, 0, iz0 + 1, "furnace", away > 0 ? 1 : 3); c.Set(fx, 0, iz0 + 2, "anvil"); c.Set(fx, 0, iz1, "crate"); c.Set(fx, 1, iz1, "crate"); c.Set(fx, 0, iz1 - 1, "barrel");
                    c.Turned(fx + away * 3, 0, iz1, "cabinet", 2); c.Turned(fx + away * 4, 1, iz1, "shelf", 0);
                    if (floors == 1) c.Bed(fx + away * 2, 0, iz0, 0);
                    break;
                case 4:                                                               // the inn: a bar, tables, a piano
                    for (int z = iz0 + 1; z <= iz1 - 2; z++) c.Set(fx + away * 2, 0, z, "counter");
                    c.Set(fx, 0, iz0, "barrel"); c.Set(fx, 1, iz0, "barrel"); c.Set(fx, 0, iz0 + 1, "barrel"); c.Turned(fx, 0, iz1 - 1, "cabinet", away > 0 ? 1 : 3); c.Turned(fx, 2, (iz0 + iz1) / 2, "shelf", away > 0 ? 3 : 1);
                    for (int k = 0; k < 2; k++)
                    {
                        int tx = fx + away * 6, tz = iz0 + 2 + k * 4;
                        c.Set(tx, 0, tz, "table"); c.Set(tx, 1, tz, "candle"); c.Turned(tx - 1, 0, tz, "chair", 1); c.Turned(tx + 1, 0, tz, "chair", 3); c.Turned(tx, 0, tz + 1, "chair", 2);
                    }
                    c.Set(fx + away * 9, 0, iz0, "hearth"); c.Turned(fx + away * 8, 0, iz1, "piano", 2);
                    break;
                case 5:                                                               // the bakery
                    for (int x = ix0 + 1; x <= ix1 - 1; x++) c.Set(x, 0, iz0 + 4, "counter");
                    c.Set(ix0 + 2, 1, iz0 + 4, "candle"); c.Set(ix1 - 2, 1, iz0 + 4, "flower_pot_blue");
                    c.Turned(ix0 + 1, 0, iz0, "stove", 0); c.Turned(ix0 + 2, 0, iz0, "stove", 0); c.Set(ix0 + 3, 0, iz0, "sink"); c.Turned(ix0 + 4, 0, iz0, "fridge", 0); c.Set(ix0 + 5, 0, iz0, "hay_bale"); c.Set(ix0 + 6, 0, iz0, "barrel");
                    c.Set(ix1 - 1, 0, iz1, "table"); c.Turned(ix1 - 2, 0, iz1, "chair", 1);
                    break;
                case 6:                                                               // the library
                    for (int x = ix0 + 1; x <= ix1 - 1; x += 3) { c.Fill(x, 0, iz0 + 1, x, 2, iz0 + 3, "bookshelf"); c.Fill(x, 0, iz1 - 3, x, 2, iz1 - 1, "bookshelf"); }
                    c.Set(x0 + w / 2, 0, z0 + d / 2, "enchanting_table"); c.Set(ix0, 0, iz0, "floor_lamp"); c.Set(ix0, 0, iz1, "floor_lamp");
                    c.Set(fx, 0, z0 + d / 2 - 1, "table"); c.Turned(fx, 0, z0 + d / 2 + 1, "chair", 2); c.Set(fx, 1, z0 + d / 2 - 1, "candle");
                    break;
                default:                                                              // the hall: carpet, benches, a clock
                    c.Fill(x0 + w / 2 - 1, 0, iz0 + 1, x0 + w / 2 + 1, 0, iz1 - 2, "carpet_red");
                    for (int z = iz0 + 3; z <= iz1 - 4; z += 2) { for (int k = 3; k <= 6; k++) { c.Turned(x0 + w / 2 - k, 0, z, "bench", 0); c.Turned(x0 + w / 2 + k, 0, z, "bench", 0); } }
                    c.Fill(x0 + w / 2 - 2, 0, iz1, x0 + w / 2 + 2, 0, iz1, "polished_stone"); c.Set(x0 + w / 2, 1, iz1, "gold_block"); c.Set(x0 + w / 2 - 2, 1, iz1, "candle"); c.Set(x0 + w / 2 + 2, 1, iz1, "candle");
                    c.Turned(x0 + w / 2, 5, iz1, "wall_clock", 0); c.Turned(x0 + w / 2 - 4, 2, iz1, "painting_lake", 0); c.Turned(x0 + w / 2 + 4, 2, iz1, "painting_forest", 0);
                    c.Fill(ix0, 3, iz0, ix1, 3, iz1, 0);                               // one tall room
                    foreach (int z in new[] { iz0 + 3, iz1 - 3 }) c.Set(x0 + w / 2, 6, z, "chandelier");
                    break;
            }
            // Never leave anything standing right inside the door.
            c.Fill(dx - ox, 0, dz - oz, dx - ox, 1, dz - oz, 0); c.Fill(dx - ox * 2, 0, dz - oz * 2, dx - ox * 2, 1, dz - oz * 2, c.Get(dx - ox * 2, 0, dz - oz * 2) == B("carpet_red") || c.Get(dx - ox * 2, 0, dz - oz * 2) == B("carpet_blue") ? c.Get(dx - ox * 2, 0, dz - oz * 2) : (ushort)0);
            c.Set(dx - ox * 2, 1, dz - oz * 2, 0);
        }
    }
}
