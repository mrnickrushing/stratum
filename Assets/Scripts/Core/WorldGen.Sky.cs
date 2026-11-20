using System;
using System.Collections.Generic;

namespace Stratum.Core
{
    // What sort of world a seed grows into. Saved with the world (single player WorldMeta.kind, the server's
    // world.json), so a world keeps its kind whatever the settings say later.
    // Themed worlds reshape the normal land (Frozen, Dunes, Islands, Peaks, Flat); Sky Islands is its own generator.
    public enum WorldKind : byte { Normal = 0, Sky = 1, Frozen = 2, Dunes = 3, Islands = 4, Peaks = 5, Flat = 6, Count }

    // Sky Islands: floating islands over open sky. Each island has its own climate (a snowy one beside a desert one),
    // a grassy top with a little dome, and a rocky underside that tapers to a ragged point. Big islands may hold a
    // pond and, deep in the stone, a pocket of lava (obsidian for the Ember Deep). Ores sit in the island stone, the
    // rare ones in the lower half. No caves, no villages, no sea: fall off and you're gone.
    public sealed partial class WorldGen
    {
        public readonly WorldKind Kind;
        public static readonly string[] KindNames = { "Normal", "Sky Islands", "Frozen Wilds", "Endless Dunes", "Archipelago", "Giant Peaks", "Flatlands" };
        public static readonly string[] KindHints =
        {
            "Hills, forests, rivers and seas, deserts and snowy peaks.",
            "Floating islands over open sky. Build bridges, and don't fall!",
            "Snow and pine forest everywhere, and the seas are frozen over.",
            "Sand as far as you can see, with rivers for oases.",
            "Warm seas full of little islands and sandy beaches.",
            "Enormous mountains, cliffs and deep valleys.",
            "Perfectly flat grassland, made for building.",
        };
        public const int FlatTop = Chunk.SeaLevel + 3;   // just above where the rules call the ground beach

        // "sky", "frozen", "dunes"/"desert", "islands"/"archipelago", "peaks"/"mountains", "flat"; anything else: normal.
        public static WorldKind ParseKind(string s)
        {
            s = (s ?? "").Trim().ToLowerInvariant();
            if (s.StartsWith("sky")) return WorldKind.Sky;
            if (s.StartsWith("frozen") || s.StartsWith("snow") || s.StartsWith("ice")) return WorldKind.Frozen;
            if (s.StartsWith("dune") || s.StartsWith("desert")) return WorldKind.Dunes;
            if (s.StartsWith("island") || s.StartsWith("archi")) return WorldKind.Islands;
            if (s.StartsWith("peak") || s.StartsWith("mountain") || s.StartsWith("giant")) return WorldKind.Peaks;
            if (s.StartsWith("flat")) return WorldKind.Flat;
            return WorldKind.Normal;
        }
        public static string KindName(WorldKind k) => (int)k < KindNames.Length ? KindNames[(int)k] : KindNames[0];

        const int BigCell = 60, SmallCell = 26;
        public const int StartX = 8, StartZ = 8;       // the middle of the island everyone starts on

        struct Island
        {
            public float X, Z, R, Top, Dome, Depth, Temp, Humid;
            public bool Pond, Lava; public float PondX, PondZ, PondR;
            public int Key;
        }

        // The island in a cell, if there is one (deterministic from the seed and the cell).
        bool IslandIn(int cx, int cz, bool small, out Island isl)
        {
            isl = default;
            uint h = Noise.Hash(cx, small ? 733 : 731, cz, Seed);
            int cell = small ? SmallCell : BigCell;
            bool start = !small && cx == 0 && cz == 0;
            if (!start && h % 100 >= (small ? 20u : 78u)) return false;
            float fx = (h >> 8 & 0xFF) / 255f, fz = (h >> 16 & 0xFF) / 255f;
            int margin = small ? 6 : 14;
            isl.X = cx * cell + margin + fx * (cell - 2 * margin);
            isl.Z = cz * cell + margin + fz * (cell - 2 * margin);
            var rng = new Rng(h ^ 0x51a7);
            if (small) isl.R = 3 + rng.Value() * 3.5f;
            else isl.R = rng.Value() < 0.12f ? 26 + rng.Value() * 10 : 11 + rng.Value() * 13;
            // Neighbouring islands float at similar heights, so most gaps can be bridged.
            float level = 96 + _hill.Fbm2(isl.X / 420f + 7.1f, isl.Z / 420f - 3.3f, 2) * 34;
            isl.Top = level + (rng.Value() - 0.5f) * (small ? 22 : 16);
            isl.Dome = 1.5f + isl.R * 0.11f;
            isl.Depth = isl.R * (0.95f + rng.Value() * 0.5f) + 3;
            isl.Temp = Clamp(_temp.Fbm2(isl.X / 520f + 31.3f, isl.Z / 520f - 12.7f, 3) * 1.9f, -1, 1);
            isl.Humid = Clamp(_humid.Fbm2(isl.X / 420f - 70.1f, isl.Z / 420f + 44.9f, 3) * 1.9f, -1, 1);
            isl.Key = (cx * 92821) ^ (cz * 68917) ^ (small ? 1 : 0);
            if (start)
            {
                // Somewhere kind to begin: a big, wooded, temperate island with a pond.
                isl.X = StartX; isl.Z = StartZ; isl.R = 31; isl.Top = 96; isl.Dome = 4; isl.Depth = 34;
                isl.Temp = 0.1f; isl.Humid = 0.3f; isl.Pond = true; isl.Lava = true;
            }
            else if (!small)
            {
                isl.Pond = isl.R >= 14 && rng.Value() < 0.4f;
                isl.Lava = isl.R >= 16 && rng.Value() < 0.3f;
            }
            if (isl.Pond)
            {
                float a = rng.Value() * 6.283f;
                isl.PondR = Math.Max(3, isl.R * 0.2f);
                isl.PondX = isl.X + (float)Math.Cos(a) * isl.R * 0.38f;
                isl.PondZ = isl.Z + (float)Math.Sin(a) * isl.R * 0.38f;
            }
            isl.Top = (float)Math.Floor(isl.Top);
            return true;
        }

        [ThreadStatic] static List<Island> _near;

        // Islands whose reach could cover any of the area x0..x1, z0..z1.
        List<Island> IslandsNear(int x0, int z0, int x1, int z1)
        {
            var list = _near ??= new List<Island>();
            list.Clear();
            for (int pass = 0; pass < 2; pass++)
            {
                bool small = pass == 1;
                int cell = small ? SmallCell : BigCell, reach = small ? 8 : 46;
                int c0x = FloorDiv(x0 - reach, cell), c1x = FloorDiv(x1 + reach, cell);
                int c0z = FloorDiv(z0 - reach, cell), c1z = FloorDiv(z1 + reach, cell);
                for (int cz = c0z; cz <= c1z; cz++)
                    for (int cx = c0x; cx <= c1x; cx++)
                        if (IslandIn(cx, cz, small, out var isl)
                            && isl.X + isl.R * 1.3f >= x0 && isl.X - isl.R * 1.3f <= x1 && isl.Z + isl.R * 1.3f >= z0 && isl.Z - isl.R * 1.3f <= z1)
                            list.Add(isl);
            }
            return list;
        }

        static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);

        Column SkySample(int x, int z) => SkyColumn(x, z, IslandsNear(x, z, x, z));

        Column SkyColumn(int x, int z, List<Island> near)
        {
            float fx = x + 0.5f, fz = z + 0.5f;
            var col = new Column { Sky = true, Height = 0, Bottom = 0 };
            float best = -1;
            // Edges wander, so islands aren't circles.
            float wobble = _patch.S2(fx / 17f, fz / 17f) * 0.2f + _eros.S2(fx / 7f, fz / 7f) * 0.07f;
            float rag = 0.85f + 0.3f * (_cave1.S2(fx / 9f, fz / 9f) * 0.5f + 0.5f);
            float bump = Math.Max(0, _detail.S2(fx / 23f, fz / 23f)) * 1.4f;
            foreach (var isl in near)
            {
                float dx = fx - isl.X, dz = fz - isl.Z;
                float t = (float)Math.Sqrt(dx * dx + dz * dz) / (isl.R * (1 + wobble));
                if (t >= 1) continue;
                float top = isl.Top + isl.Dome * (1 - t * t) + bump;
                if (t > 0.82f) top -= (t - 0.82f) / 0.18f * 1.6f;      // rounded shoulders
                float bottom = isl.Top - 1 - isl.Depth * (float)Math.Pow(1 - t, 0.6) * rag;
                if (bottom > top - 1) bottom = top - 1;
                if (col.Height <= 0) { col.Height = top; col.Bottom = bottom; }
                else { col.Height = Math.Max(col.Height, top); col.Bottom = Math.Min(col.Bottom, bottom); }
                // The island highest here decides the climate (and the pond).
                if (top > best)
                {
                    best = top; col.Temp = isl.Temp; col.Humid = isl.Humid; col.Water = 0;
                    if (isl.Pond)
                    {
                        float px = fx - isl.PondX, pz = fz - isl.PondZ;
                        float pd = (float)Math.Sqrt(px * px + pz * pz) / isl.PondR;
                        if (pd < 1) { col.Water = (int)isl.Top; col.Height = isl.Top - 1 - (int)(2.6f * (1 - pd * pd)); }
                    }
                }
            }
            if (col.Height <= 0)
            {
                col.Temp = Clamp(_temp.Fbm2(fx / 520f + 31.3f, fz / 520f - 12.7f, 3) * 1.9f, -1, 1);
                col.Humid = Clamp(_humid.Fbm2(fx / 420f - 70.1f, fz / 420f + 44.9f, 3) * 1.9f, -1, 1);
            }
            else col.Height = (float)Math.Floor(col.Height);
            return col;
        }

        static Biome SkyBiome(in Column col)
        {
            if (col.Temp > 0.4f && col.Humid < 0.05f) return Biome.Desert;
            if (col.Temp < -0.45f) return Biome.Tundra;
            if (col.Temp < -0.12f) return Biome.Taiga;
            if (col.Humid > 0.45f && col.Temp < 0.35f) return Biome.BirchWood;
            if (col.Humid > 0.15f) return Biome.Forest;
            return Biome.Plains;
        }

        void GenerateSky(Chunk ch, Column[] cols)
        {
            int bx = ch.X * Chunk.Size, bz = ch.Z * Chunk.Size;
            var near = IslandsNear(bx - M, bz - M, bx + 16 + M, bz + 16 + M);
            var islands = near.ToArray();
            for (int z = 0; z < W; z++)
                for (int x = 0; x < W; x++)
                    cols[x + z * W] = SkyColumn(bx + x - M, bz + z - M, near);

            var b = ch.Blocks;
            Array.Clear(b, 0, b.Length);
            int maxH = 0;
            for (int z = 0; z < 16; z++)
                for (int x = 0; x < 16; x++)
                {
                    ref var col = ref cols[(x + M) + (z + M) * W];
                    var biome = SkyBiome(col);
                    ch.Biome[x + z * 16] = (byte)biome;
                    WriteTint(ch, x, z, col, biome);
                    if (col.Height <= 0) continue;
                    int h = (int)col.Height, bot = Math.Max(1, (int)Math.Ceiling(col.Bottom));
                    if (h > maxH) maxH = Math.Max(h, col.Water);
                    int wx = bx + x, wz = bz + z;
                    uint hh = Noise.Hash(wx, 0, wz, Seed);
                    byte top, fill; int fillDepth = 3 + (int)(hh % 2);
                    switch (biome)
                    {
                        case Biome.Desert: top = Blocks.Sand; fill = Blocks.Sand; fillDepth = 4; break;
                        case Biome.Tundra: top = Blocks.SnowyGrass; fill = Blocks.Dirt; break;
                        default: top = Blocks.Grass; fill = Blocks.Dirt; break;
                    }
                    if (col.Water > 0)
                    {
                        float p = _patch.S2(wx / 6f, wz / 6f);
                        top = p > 0.2f ? Blocks.Clay : p < -0.35f ? Blocks.Gravel : Blocks.Sand;
                        fill = top == Blocks.Clay ? Blocks.Clay : Blocks.Dirt; fillDepth = 2;
                    }
                    int thick = h - bot;
                    int slateTop = bot + (int)(thick * 0.3f);
                    int ci = x | (z << 4);
                    for (int y = bot; y <= h && y < Chunk.Height; y++)
                    {
                        byte id;
                        if (y == h) id = top;
                        else if (y > h - fillDepth) id = fill;
                        else if (biome == Biome.Desert && y > h - fillDepth - 4) id = Blocks.Sandstone;
                        else if (thick > 10 && y <= slateTop) id = Blocks.Slate;
                        else id = Blocks.Stone;
                        // Nothing loose hangs over the drop: the last block underneath holds the rest up.
                        if (y == bot && (id == Blocks.Sand || id == Blocks.Gravel)) id = biome == Biome.Desert ? Blocks.Sandstone : Blocks.Stone;
                        b[ci | (y << 8)] = id;
                    }
                    for (int y = h + 1; y <= col.Water && y < Chunk.Height; y++)
                        b[ci | (y << 8)] = y == col.Water && biome == Biome.Tundra ? Blocks.Ice : Blocks.Water;
                }
            ch.MaxY = maxH;

            // Lava deep inside some big islands: well wrapped in stone, so it stays put until someone digs to it.
            foreach (var isl in islands)
            {
                if (!isl.Lava) continue;
                float lx = isl.X + isl.R * 0.15f, lz = isl.Z - isl.R * 0.1f, ly = isl.Top - isl.Depth * 0.42f;
                for (int y = (int)ly - 3; y <= (int)ly + 3; y++)
                    for (int z = (int)lz - 3; z <= (int)lz + 3; z++)
                        for (int x = (int)lx - 3; x <= (int)lx + 3; x++)
                        {
                            float dx = x + 0.5f - lx, dy = (y + 0.5f - ly) * 1.4f, dz = z + 0.5f - lz;
                            if (dx * dx + dy * dy + dz * dz > 6.5f) continue;
                            int ox = x - bx, oz = z - bz;
                            if ((uint)ox >= 16 || (uint)oz >= 16 || (uint)y >= Chunk.Height) continue;
                            int i = ox | (oz << 4) | (y << 8);
                            if (b[i] == Blocks.Stone || b[i] == Blocks.Slate) b[i] = y >= (int)ly + 1 ? (byte)0 : Blocks.Lava;
                        }
            }

            SkyOres(ch, cols);
            Trees(ch, cols);
            Flora(ch, cols);
            ch.RecalcMaxY();
            ch.Ready = true;
        }

        // Ores spread through whatever island stone this chunk has, the rare ones in its lower half.
        void SkyOres(Chunk ch, Column[] cols)
        {
            int land = 0, lo = Chunk.Height, hi = 0;
            for (int z = 0; z < 16; z++)
                for (int x = 0; x < 16; x++)
                {
                    ref var col = ref cols[(x + M) + (z + M) * W];
                    if (col.Height <= 0) continue;
                    land++;
                    lo = Math.Min(lo, Math.Max(1, (int)col.Bottom)); hi = Math.Max(hi, (int)col.Height - 3);
                }
            if (land < 8 || hi - lo < 3) return;
            float f = land / 256f;
            var rng = new Rng(Noise.Hash(ch.X, 7, ch.Z, Seed));
            int mid = lo + (hi - lo) / 2;
            int N(float n) { float v = n * f; int k = (int)v; return k + (rng.Value() < v - k ? 1 : 0); }
            Vein(ch, ref rng, Blocks.CoalOre, N(22), lo, hi, 9);
            Vein(ch, ref rng, Blocks.CopperOre, N(12), lo, hi, 7);
            Vein(ch, ref rng, Blocks.IronOre, N(14), lo, hi, 6);
            Vein(ch, ref rng, Blocks.GoldOre, N(5), lo, mid + 1, 5);
            Vein(ch, ref rng, Blocks.SunstoneOre, N(3), lo, mid + 1, 4);
            Vein(ch, ref rng, Blocks.JadeOre, N(1.2f), lo, mid + 1, 3);
            Vein(ch, ref rng, Blocks.SparkOre, N(6), lo, mid + 1, 6);
            Vein(ch, ref rng, Blocks.Gravel, N(6), lo, hi, 10);
        }

        // Where a new player starts: the middle of the first island (dry, it has no pond there).
        (int x, int y, int z) SkySpawn()
        {
            for (int r = 0; r < 30; r += 2)
                for (int a = 0; a < 12; a++)
                {
                    double ang = a * Math.PI / 6;
                    int x = StartX + (int)(Math.Cos(ang) * r), z = StartZ + (int)(Math.Sin(ang) * r);
                    var col = SkySample(x, z);
                    if (col.Height > 0 && col.Water == 0) return (x, (int)col.Height + 1, z);
                }
            return (StartX, 110, StartZ);
        }
    }
}
