using System;

namespace Stratum.Core
{
    public enum Biome : byte { Ocean, River, Beach, Plains, Forest, BirchWood, Taiga, Tundra, Desert, Peaks }

    // Turns a seed into terrain. Everything here is a pure function of the seed and world position, so any chunk
    // can be made in any order on any thread and always comes out the same.
    public sealed partial class WorldGen
    {
        public readonly int Seed;
        readonly Noise _cont, _eros, _peak, _hill, _detail, _river, _temp, _humid, _cave1, _cave2, _cheese, _strata, _patch;

        public static readonly string[] BiomeNames =
            { "Ocean", "River", "Beach", "Meadow", "Oakwood", "Birch Grove", "Pinewood", "Frostland", "Dunes", "High Peaks" };

        public readonly Villages Villages;

        public WorldGen(int seed, WorldKind kind = WorldKind.Normal)
        {
            Kind = kind;
            Villages = new Villages(this, seed);
            Seed = seed;
            _cont = new Noise(seed); _eros = new Noise(seed + 1); _peak = new Noise(seed + 2); _hill = new Noise(seed + 3);
            _detail = new Noise(seed + 4); _river = new Noise(seed + 5); _temp = new Noise(seed + 6); _humid = new Noise(seed + 7);
            _cave1 = new Noise(seed + 8); _cave2 = new Noise(seed + 9); _cheese = new Noise(seed + 10); _strata = new Noise(seed + 11);
            _patch = new Noise(seed + 12);
        }

        static float Clamp(float v, float a, float b) => v < a ? a : v > b ? b : v;
        static float Smooth(float e0, float e1, float x) { float t = Clamp((x - e0) / (e1 - e0), 0, 1); return t * t * (3 - 2 * t); }
        static float Lerp(float a, float b, float t) => a + (b - a) * t;

        static readonly float[] ContX = { -1f, -0.45f, -0.2f, -0.08f, 0f, 0.25f, 0.6f, 1f };
        static readonly float[] ContY = { 28f, 42f, 56f, 62f, 66f, 71f, 82f, 94f };

        static float Spline(float[] xs, float[] ys, float x)
        {
            if (x <= xs[0]) return ys[0];
            for (int i = 1; i < xs.Length; i++)
                if (x <= xs[i]) { float t = (x - xs[i - 1]) / (xs[i] - xs[i - 1]); t = t * t * (3 - 2 * t); return Lerp(ys[i - 1], ys[i], t); }
            return ys[ys.Length - 1];
        }

        // Bottom and Water are for Sky Islands (the underside, and a pond's surface); Sky marks its columns.
        // Lift raises where bare peaks begin (Towering Wilds); Only is a single-biome world's biome + 1.
        public struct Column { public float Height, Temp, Humid, River, Mountain, Bottom, Lift; public int Water; public bool Sky; public ushort Only; }

        // The climate that goes with each biome, for a single-biome world's grass colour and weather.
        static readonly float[] OnlyTemp = { 0.15f, 0.1f, 0.1f, -0.3f, -0.7f, 0.7f, -0.2f };
        static readonly float[] OnlyHumid = { 0.0f, 0.3f, 0.6f, 0.2f, 0.0f, -0.4f, 0.0f };

        public Column Sample(int x, int z)
        {
            if (Kind == WorldKind.Sky) return SkySample(x, z);
            if (Level) return new Column { Height = FlatTop, Temp = 0.15f, Humid = 0.1f };
            float fx = x + 0.5f, fz = z + 0.5f;
            bool tower = Kind == WorldKind.Towering, only = IsOnly(Kind);
            bool crags = Kind == WorldKind.Peaks || Kind == WorldKind.OnlyPeaks;
            float wide = Kind == WorldKind.Vast ? 0.25f : 1f;                  // regions four times as wide
            float c = Kind == WorldKind.Islands
                ? Clamp(_cont.Fbm2(fx / 420f, fz / 420f, 4) * 1.7f, -1, 1) * 0.8f - 0.24f    // small islands in warm sea
                : Clamp(_cont.Fbm2(fx * wide / 1100f, fz * wide / 1100f, 4) * 1.7f, -1, 1);
            if (Kind == WorldKind.Dunes || Kind == WorldKind.Peaks) c = Math.Min(1, c * 0.7f + 0.3f);     // mostly land
            if (only) c = 0.22f + Math.Abs(c) * 0.6f;                                                     // all land: no seas, just rivers
            float e = Clamp(_eros.Fbm2(fx * wide / 700f, fz * wide / 700f, 3) * 1.7f, -1, 1) * 0.5f + 0.5f;
            if (crags) e *= 0.55f;                                                                       // mountains everywhere
            if (tower) e *= 0.75f;
            float land = Smooth(-0.12f, 0.15f, c);
            float mountain = Smooth(0.25f, 0.85f, 1 - e) * land;
            float peaks = _peak.Ridge2(fx / 420f, fz / 420f, 4);
            float h = Spline(ContX, ContY, c);
            h += mountain * (float)(peaks * Math.Sqrt(peaks)) * (crags ? 175f : 115f);     // (x * sqrt x: the same on every runtime, unlike Pow)
            h += _hill.Fbm2(fx / 150f, fz / 150f, 4) * (4f + 12f * land) * (1 - mountain * 0.5f);
            h += _detail.Fbm2(fx / 37f, fz / 37f, 2) * 2.2f;
            if (tower && h > Chunk.SeaLevel)
            {
                // Everything above the sea doubles, and broad terraces and spires stand on top of that.
                float up = h - Chunk.SeaLevel;
                float mesa = Smooth(0.05f, 0.3f, _patch.Fbm2(fx / 95f + 11.7f, fz / 95f - 4.3f, 3)) * land;
                double sp = Math.Max(0, _strata.Fbm2(fx / 85f - 9.1f, fz / 85f + 2.9f, 3) * 1.6f); float spire = (float)(sp * sp);
                h = Chunk.SeaLevel + up * 2.1f + mesa * 42f + spire * 40f * land;
            }

            float rv = Math.Abs(_river.Fbm2(fx / 650f, fz / 650f, 3));
            float river = Smooth(0.045f, 0.012f, rv) * land * (1 - mountain);
            if (tower || Kind == WorldKind.OnlyPeaks) river = Smooth(0.05f, 0.012f, rv) * land;   // rivers cut gorges right through the heights
            h = Lerp(h, Math.Min(h, 58f), river);

            float t = Clamp(_temp.Fbm2(fx * wide / 1500f + 31.3f, fz * wide / 1500f - 12.7f, 3) * 1.8f, -1, 1);
            float lift = tower ? 55 : 0;
            t -= Math.Max(0, h - 95 - lift * 1.6f) / 90f;
            float u = Clamp(_humid.Fbm2(fx * wide / 1100f - 70.1f, fz * wide / 1100f + 44.9f, 3) * 1.8f, -1, 1);
            switch (Kind)
            {
                case WorldKind.Frozen: t = -0.48f + t * 0.3f; break;                         // snowfields and pinewoods
                case WorldKind.Dunes: t = 0.62f + t * 0.25f; u = -0.32f + u * 0.4f; break;     // sand, and dry grass by the rivers
                case WorldKind.Islands: t = 0.22f + t * 0.3f; u = 0.2f + u * 0.5f; break;      // warm and green
            }
            ushort one = 0;
            if (only)
            {
                int k = (int)Kind - (int)FirstOnly;
                one = (ushort)((int)OnlyBiome(Kind) + 1);
                t = OnlyTemp[k] + t * 0.08f; u = OnlyHumid[k] + u * 0.08f;       // a little drift, so the grass isn't one flat colour
            }
            return new Column { Height = Clamp(h, 6, 245), Temp = t, Humid = u, River = river, Mountain = mountain, Lift = lift, Only = one };
        }

        public static Biome BiomeOf(in Column col)
        {
            if (col.Sky) return SkyBiome(col);
            int h = (int)col.Height;
            if (h < Chunk.SeaLevel) return col.River > 0.4f || col.Only != 0 ? Biome.River : Biome.Ocean;
            if (col.Only != 0) return (Biome)(col.Only - 1);
            if (h > 150 + col.Lift) return Biome.Peaks;
            if (h <= Chunk.SeaLevel + 2 && col.River < 0.3f && col.Temp > -0.45f) return Biome.Beach;
            if (col.Temp > 0.4f && col.Humid < 0.05f) return Biome.Desert;
            if (col.Temp < -0.45f) return Biome.Tundra;
            if (col.Temp < -0.12f) return Biome.Taiga;
            if (col.Humid > 0.45f && col.Temp < 0.35f) return Biome.BirchWood;
            if (col.Humid > 0.15f) return Biome.Forest;
            return Biome.Plains;
        }

        public Biome BiomeAt(int x, int z) => BiomeOf(Sample(x, z));
        public int HeightAt(int x, int z) => (int)Sample(x, z).Height;

        const int M = 4;                     // tree margin around a chunk
        const int W = Chunk.Size + 2 * M;    // heightmap width with margin

        [ThreadStatic] static Column[] _cols;
        [ThreadStatic] static float[] _cA, _cB, _cC;

        public void Generate(Chunk ch)
        {
            if (Ember.InChunk(ch.X)) { Ember.Generate(ch, _cave1, _cave2, Seed); ch.RecalcMaxY(); ch.Ready = true; return; }
            var cols = _cols ??= new Column[W * W];
            if (Kind == WorldKind.Sky) { GenerateSky(ch, cols); return; }
            int bx = ch.X * Chunk.Size, bz = ch.Z * Chunk.Size;
            for (int z = 0; z < W; z++)
                for (int x = 0; x < W; x++)
                    cols[x + z * W] = Sample(bx + x - M, bz + z - M);

            var b = ch.Blocks;
            Array.Clear(b, 0, b.Length);
            int maxH = 0;

            for (int z = 0; z < 16; z++)
                for (int x = 0; x < 16; x++)
                {
                    ref var col = ref cols[(x + M) + (z + M) * W];
                    int h = (int)col.Height;
                    if (h > maxH) maxH = h;
                    var biome = BiomeOf(col);
                    ch.Biome[x + z * 16] = (byte)biome;
                    WriteTint(ch, x, z, col, biome);

                    // Cliffs show bare rock.
                    float hx = cols[(x + M + 1) + (z + M) * W].Height - cols[(x + M - 1) + (z + M) * W].Height;
                    float hz = cols[(x + M) + (z + M + 1) * W].Height - cols[(x + M) + (z + M - 1) * W].Height;
                    bool steep = hx * hx + hz * hz > 18f;

                    int wx = bx + x, wz = bz + z;
                    float strata = _strata.S2(wx / 60f, wz / 60f) * 4;
                    int slateTop = 46 + (int)strata, basaltTop = 18 + (int)(strata * 0.75f);
                    uint hh = Noise.Hash(wx, 0, wz, Seed);

                    ushort top, fill; int fillDepth = 3 + (int)(hh % 2);
                    switch (biome)
                    {
                        case Biome.Desert: top = Blocks.Sand; fill = Blocks.Sand; fillDepth = 4; break;
                        case Biome.Beach: top = Blocks.Sand; fill = Blocks.Sand; break;
                        case Biome.Tundra: top = Blocks.SnowyGrass; fill = Blocks.Dirt; break;
                        case Biome.Peaks: top = h > 168 + col.Lift ? Blocks.Snow : Blocks.Stone; fill = Blocks.Stone; break;
                        case Biome.Ocean:
                        case Biome.River:
                            float p = _patch.S2(wx / 18f, wz / 18f);
                            top = p > 0.45f ? Blocks.Clay : p < -0.4f ? Blocks.Gravel : Blocks.Sand; fill = top == Blocks.Clay ? Blocks.Clay : Blocks.Sand; break;
                        default: top = Blocks.Grass; fill = Blocks.Dirt; break;
                    }
                    if (steep && biome != Biome.Ocean && biome != Biome.River && biome != Biome.Beach)
                    {
                        top = h > 165 + col.Lift || biome == Biome.Tundra && col.Only != 0 ? Blocks.Snow : Blocks.Stone; fill = Blocks.Stone;
                    }
                    if (biome == Biome.Taiga && (h > 120 || Kind == WorldKind.Frozen)) top = Blocks.SnowyGrass;

                    int ci = x | (z << 4);
                    b[ci] = Blocks.Bedrock;
                    for (int y = 1; y <= h; y++)
                    {
                        ushort id;
                        if (y <= 3 && (Noise.Hash(wx, y, wz, Seed) & 3) < (uint)(4 - y)) id = Blocks.Bedrock;
                        else if (y == h) id = top;
                        else if (y > h - fillDepth) id = fill;
                        else if (biome == Biome.Desert && y > h - fillDepth - 4) id = Blocks.Sandstone;
                        else if (y <= basaltTop) id = Blocks.Basalt;
                        else if (y <= slateTop) id = Blocks.Slate;
                        else id = Blocks.Stone;
                        b[ci | (y << 8)] = id;
                    }
                    for (int y = h + 1; y <= Chunk.SeaLevel; y++)
                        b[ci | (y << 8)] = (y == Chunk.SeaLevel && (biome == Biome.Tundra || Kind == WorldKind.Frozen || Kind == WorldKind.OnlyFrost)) ? Blocks.Ice : Blocks.Water;
                }

            ch.MaxY = Math.Max(maxH, Chunk.SeaLevel);
            if (!Level) Caves(ch, cols);
            if (Kind == WorldKind.Towering) Overhangs(ch, cols);
            Ores(ch);
            Trees(ch, cols);
            Flora(ch, cols);
            int vbx = bx, vbz = bz;
            if (!Level && Kind != WorldKind.Sky) Ruins.Stamp(ch, Seed);
            if (Builds.Is(Kind)) Builds.Stamp(ch, Kind);
            else Villages.Stamp(ch, (x, z) => (int)cols[(x - vbx + M) + (z - vbz + M) * W].Height);
            ch.RecalcMaxY();
            ch.Ready = true;
        }

        void WriteTint(Chunk ch, int x, int z, in Column col, Biome biome)
        {
            // Grass: lush green when wet and mild, straw when hot and dry, cool blue-green when cold.
            float t = Clamp(col.Temp, -1, 1), u = Clamp(col.Humid, -1, 1);
            float r = 0.40f, g = 0.66f, bl = 0.24f;
            float dry = Clamp(t * 0.9f - u * 0.6f, 0, 1);
            r = Lerp(r, 0.74f, dry); g = Lerp(g, 0.70f, dry); bl = Lerp(bl, 0.30f, dry);
            float cold = Clamp(-t, 0, 1);
            r = Lerp(r, 0.38f, cold); g = Lerp(g, 0.58f, cold); bl = Lerp(bl, 0.44f, cold);
            float wet = Clamp(u, 0, 1) * 0.35f;
            r = Lerp(r, 0.28f, wet); g = Lerp(g, 0.58f, wet); bl = Lerp(bl, 0.18f, wet);
            int i = (x + z * 16) * 4;
            ch.Tint[i] = (byte)(r * 255); ch.Tint[i + 1] = (byte)(g * 255); ch.Tint[i + 2] = (byte)(bl * 255);
            ch.Tint[i + 3] = (byte)(Clamp(0.85f - dry * 0.15f + wet * 0.2f, 0, 1) * 255);
        }

        public void TintOnly(Chunk ch)
        {
            int bx = ch.X * 16, bz = ch.Z * 16;
            for (int z = 0; z < 16; z++)
                for (int x = 0; x < 16; x++)
                {
                    var col = Sample(bx + x, bz + z);
                    var biome = BiomeOf(col);
                    ch.Biome[x + z * 16] = (byte)biome;
                    WriteTint(ch, x, z, col, biome);
                }
        }

        // Caves: winding tunnels where two noise fields both cross zero, plus big caverns deep down.
        // The fields are sampled every 4 blocks and blended in between.
        void Caves(Chunk ch, Column[] cols)
        {
            int top = Math.Min(ch.MaxY + 1, 250);
            int ny = top / 4 + 2;
            int n = 5 * 5 * ny;
            var A = _cA = (_cA == null || _cA.Length < n) ? new float[n] : _cA;
            var B = _cB = (_cB == null || _cB.Length < n) ? new float[n] : _cB;
            var C = _cC = (_cC == null || _cC.Length < n) ? new float[n] : _cC;
            int bx = ch.X * 16, bz = ch.Z * 16;
            for (int gy = 0; gy < ny; gy++)
                for (int gz = 0; gz < 5; gz++)
                    for (int gx = 0; gx < 5; gx++)
                    {
                        float x = bx + gx * 4 + 0.37f, y = gy * 4 + 0.21f, z = bz + gz * 4 + 0.63f;
                        int i = gx + gz * 5 + gy * 25;
                        A[i] = _cave1.S3(x / 52f, y / 30f, z / 52f);
                        B[i] = _cave2.S3(x / 52f, y / 30f, z / 52f);
                        C[i] = y < 70 ? _cheese.Fbm3(x / 80f, y / 44f, z / 80f, 2) : -1;
                    }

            var b = ch.Blocks;
            for (int z = 0; z < 16; z++)
                for (int x = 0; x < 16; x++)
                {
                    int h = (int)cols[(x + M) + (z + M) * W].Height;
                    bool wet = h <= Chunk.SeaLevel + 1;
                    int limit = wet ? h - 6 : h;               // never open a cave under water
                    int gx = x >> 2, gz = z >> 2; float fx = (x & 3) / 4f, fz = (z & 3) / 4f;
                    for (int y = 5; y <= limit && y < top; y++)
                    {
                        int gy = y >> 2; float fy = (y & 3) / 4f;
                        float a = Tri(A, gx, gy, gz, fx, fy, fz), bb = Tri(B, gx, gy, gz, fx, fy, fz);
                        // Tunnels get a little wider with depth; they rarely break the surface.
                        float width = 0.0028f + (y < 40 ? 0.0016f : 0);
                        if (y > h - 4) width *= 0.55f;
                        bool cave = a * a + bb * bb < width;
                        if (!cave && y < 64)
                        {
                            float c = Tri(C, gx, gy, gz, fx, fy, fz);
                            cave = c > 0.58f - (64 - y) * 0.0015f;
                        }
                        if (!cave) continue;
                        int idx = x | (z << 4) | (y << 8);
                        ushort cur = b[idx];
                        if (cur == Blocks.Bedrock || cur == Blocks.Water || cur == Blocks.Ice) continue;
                        b[idx] = 0;
                        // Turn the dirt under an opening back to grass.
                        if (y > 1 && b[idx - 256] == Blocks.Dirt && y >= h - 4) b[idx - 256] = Blocks.Grass;
                    }
                }
        }

        // Towering Wilds: bites out of the high ground, leaving overhangs, arches and ledges. Whatever ends up open to
        // the sky under a bite grows over again.
        void Overhangs(Chunk ch, Column[] cols)
        {
            const int Low = 88;
            int top = Math.Min(ch.MaxY + 1, 250);
            if (top <= Low) return;
            int ny = top / 4 + 2;
            int n = 5 * 5 * ny;
            var A = _cA = (_cA == null || _cA.Length < n) ? new float[n] : _cA;
            int bx = ch.X * 16, bz = ch.Z * 16;
            for (int gy = Low / 4; gy < ny; gy++)
                for (int gz = 0; gz < 5; gz++)
                    for (int gx = 0; gx < 5; gx++)
                        A[gx + gz * 5 + gy * 25] = _cheese.Fbm3((bx + gx * 4 + 0.37f) / 46f + 19.3f, (gy * 4 + 0.21f) / 30f + 7.7f, (bz + gz * 4 + 0.63f) / 46f - 5.1f, 3);

            var b = ch.Blocks;
            for (int z = 0; z < 16; z++)
                for (int x = 0; x < 16; x++)
                {
                    ref var col = ref cols[(x + M) + (z + M) * W];
                    int h = (int)col.Height;
                    if (h < Low + 6 || h >= top) continue;
                    var biome = (Biome)ch.Biome[x + z * 16];
                    int gx = x >> 2, gz = z >> 2; float fx = (x & 3) / 4f, fz = (z & 3) / 4f;
                    bool cut = false;
                    for (int y = Low; y <= h; y++)
                    {
                        // Nothing near the foot of the hill, more the higher it goes.
                        float need = 0.38f - Math.Min(0.13f, (y - Low) * 0.004f);
                        if (Tri(A, gx, y >> 2, gz, fx, (y & 3) / 4f, fz) < need) continue;
                        int idx = x | (z << 4) | (y << 8);
                        if (b[idx] == Blocks.Water || b[idx] == Blocks.Ice) continue;
                        b[idx] = 0; cut = true;
                    }
                    if (!cut) continue;
                    // Nothing loose is left hanging over a bite.
                    for (int y = Low; y <= h; y++)
                    {
                        int idx = x | (z << 4) | (y << 8);
                        if ((b[idx] == Blocks.Sand || b[idx] == Blocks.Gravel) && b[idx - 256] == 0) b[idx] = b[idx] == Blocks.Sand ? Blocks.Sandstone : Blocks.Stone;
                    }
                    // New ground wherever the cut left rock or soil under open air.
                    ushort turf = biome == Biome.Desert ? Blocks.Sand : biome == Biome.Tundra ? Blocks.SnowyGrass : biome == Biome.Peaks ? (ushort)0 : Blocks.Grass;
                    if (turf == 0) continue;
                    int air = 0;
                    for (int y = h + 1; y >= Low - 1; y--)
                    {
                        int idx = x | (z << 4) | (y << 8);
                        ushort cur = b[idx];
                        if (cur == 0) { air++; continue; }
                        if (air >= 3 && y < h && (cur == Blocks.Stone || cur == Blocks.Dirt || cur == Blocks.Slate))
                        {
                            b[idx] = turf;
                            for (int d = 1; d <= 2; d++) { ushort under = b[idx - d * 256]; if (under == Blocks.Stone || under == Blocks.Slate) b[idx - d * 256] = turf == Blocks.Sand ? Blocks.Sand : Blocks.Dirt; }
                        }
                        air = 0;
                    }
                }
        }

        static float Tri(float[] g, int x, int y, int z, float fx, float fy, float fz)
        {
            int i = x + z * 5 + y * 25;
            float c000 = g[i], c100 = g[i + 1], c010 = g[i + 25], c110 = g[i + 26];
            float c001 = g[i + 5], c101 = g[i + 6], c011 = g[i + 30], c111 = g[i + 31];
            float x00 = c000 + (c100 - c000) * fx, x10 = c010 + (c110 - c010) * fx;
            float x01 = c001 + (c101 - c001) * fx, x11 = c011 + (c111 - c011) * fx;
            float y0 = x00 + (x10 - x00) * fy, y1 = x01 + (x11 - x01) * fy;
            return y0 + (y1 - y0) * fz;
        }

        // Starstone has dice of its own, so adding it moved no other ore.
        void StarVein(Chunk ch)
        {
            var rng = new Rng(Noise.Hash(ch.X, 23, ch.Z, Seed));
            if (rng.Value() < 0.55f) Vein(ch, ref rng, Blocks.StarOre, 1, 3, 15, 4);
        }

        void Ores(Chunk ch)
        {
            var rng = new Rng(Noise.Hash(ch.X, 7, ch.Z, Seed));
            Vein(ch, ref rng, Blocks.CoalOre, 18, 5, 140, 9);
            Vein(ch, ref rng, Blocks.CopperOre, 10, 16, 100, 7);
            Vein(ch, ref rng, Blocks.IronOre, 8, 5, 64, 6);
            Vein(ch, ref rng, Blocks.GoldOre, 3, 5, 34, 5);
            Vein(ch, ref rng, Blocks.SunstoneOre, 2, 5, 22, 4);
            if (rng.Value() < 0.5f) Vein(ch, ref rng, Blocks.JadeOre, 1, 8, 110, 3);
            Vein(ch, ref rng, Blocks.SparkOre, 5, 4, 18, 6);
            Vein(ch, ref rng, Blocks.Gravel, 5, 5, 90, 14);
            StarVein(ch);
            // Moss grows on rock beside cave air below 50.
        }

        static void Vein(Chunk ch, ref Rng rng, ushort ore, int count, int minY, int maxY, int size)
        {
            for (int v = 0; v < count; v++)
            {
                float x = rng.Range(0, 16), y = rng.Range(minY, maxY), z = rng.Range(0, 16);
                int n = rng.Range(size / 2, size + 1);
                for (int i = 0; i < n; i++)
                {
                    int ix = (int)x, iy = (int)y, iz = (int)z;
                    if ((uint)ix < 16 && (uint)iz < 16 && (uint)iy < Chunk.Height)
                    {
                        int idx = ix | (iz << 4) | (iy << 8);
                        ushort cur = ch.Blocks[idx];
                        if (cur == Blocks.Stone || cur == Blocks.Slate || cur == Blocks.Basalt) ch.Blocks[idx] = ore;
                    }
                    x += rng.Range(-1, 2); y += rng.Range(-1, 2) * 0.6f; z += rng.Range(-1, 2);
                }
            }
        }

        // ------------------------------------------------------------------ trees

        void Trees(Chunk ch, Column[] cols)
        {
            int bx = ch.X * 16, bz = ch.Z * 16;
            const int Cell = 4;
            int c0x = (int)Math.Floor((bx - M) / (float)Cell), c1x = (int)Math.Floor((bx + 16 + M) / (float)Cell);
            int c0z = (int)Math.Floor((bz - M) / (float)Cell), c1z = (int)Math.Floor((bz + 16 + M) / (float)Cell);
            for (int cz = c0z; cz <= c1z; cz++)
                for (int cx = c0x; cx <= c1x; cx++)
                {
                    uint h = Noise.Hash(cx, 91, cz, Seed);
                    int wx = cx * Cell + (int)(h % Cell), wz = cz * Cell + (int)((h >> 4) % Cell);
                    int lx = wx - bx + M, lz = wz - bz + M;
                    if ((uint)lx >= W || (uint)lz >= W) continue;
                    if (Builds.Is(Kind) && Builds.Near(Kind, wx, wz, 5)) continue;    // no trees cut in half at a build's edge
                    ref var col = ref cols[lx + lz * W];
                    var biome = BiomeOf(col);
                    float chance = biome switch
                    {
                        Biome.Forest => 0.62f, Biome.BirchWood => 0.58f, Biome.Taiga => 0.55f, Biome.Plains => 0.035f,
                        Biome.Tundra => Kind == WorldKind.Frozen ? 0.16f : 0.06f, Biome.Desert => 0.05f, Biome.Peaks => 0.0f, _ => 0f
                    };
                    // Woods thin out toward their edges and in clearings.
                    if (biome == Biome.Forest || biome == Biome.BirchWood || biome == Biome.Taiga)
                        chance *= Smooth(-0.35f, 0.15f, _patch.S2(wx / 45f, wz / 45f));
                    float roll = ((h >> 8) & 0xFFFF) / 65536f;
                    if (roll >= chance) continue;
                    int gy = (int)col.Height;
                    if (gy <= Chunk.SeaLevel || gy > 200 + col.Lift * 0.6f) continue;
                    // Slope check: no trees on cliffs.
                    if (lx > 0 && lz > 0 && lx < W - 1 && lz < W - 1)
                    {
                        float dx = cols[lx + 1 + lz * W].Height - cols[lx - 1 + lz * W].Height;
                        float dz = cols[lx + (lz + 1) * W].Height - cols[lx + (lz - 1) * W].Height;
                        if (dx * dx + dz * dz > 18f) continue;
                    }
                    var rng = new Rng(Noise.Hash(wx, gy, wz, Seed + 5));
                    int ox = wx - bx, oz = wz - bz;
                    switch (biome)
                    {
                        case Biome.Desert: Cactus(ch, ox, gy + 1, oz, ref rng); break;
                        case Biome.Taiga: case Biome.Tundra: Pine(ch, ox, gy + 1, oz, ref rng); break;
                        case Biome.BirchWood:
                            if (rng.Value() < 0.75f) Broadleaf(ch, ox, gy + 1, oz, ref rng, Blocks.BirchLog, Blocks.BirchLeaves, 5, 8);
                            else Broadleaf(ch, ox, gy + 1, oz, ref rng, Blocks.OakLog, Blocks.OakLeaves, 4, 6);
                            break;
                        default:
                            if (rng.Value() < 0.12f) BigOak(ch, ox, gy + 1, oz, ref rng);
                            else if (rng.Value() < 0.2f) Broadleaf(ch, ox, gy + 1, oz, ref rng, Blocks.BirchLog, Blocks.BirchLeaves, 5, 7);
                            else Broadleaf(ch, ox, gy + 1, oz, ref rng, Blocks.OakLog, Blocks.OakLeaves, 4, 6);
                            break;
                    }
                }
        }

        static void Put(Chunk ch, int x, int y, int z, ushort id, bool force)
        {
            if ((uint)x >= 16 || (uint)z >= 16 || (uint)y >= Chunk.Height) return;
            int i = x | (z << 4) | (y << 8);
            ushort cur = ch.Blocks[i];
            if (force || cur == 0 || Blocks.Get(cur).Replaceable && cur != Blocks.Water) ch.Blocks[i] = id;
            else if (Blocks.Get(cur).Sway && !Blocks.Get(id).Sway) ch.Blocks[i] = id;   // a trunk beats a leaf
        }

        // A tree is drawn by every chunk its crown reaches, and only the trunk's own chunk can see the ground under
        // it. So a tree is never refused for its ground (the neighbours would draw half a crown in the air): where a
        // cave mouth took the ground away, earth is put back under the trunk instead. A cactus is one column wide,
        // in one chunk, and still wants sand.
        static bool BaseOk(Chunk ch, int x, int y, int z, bool sand)
        {
            if ((uint)x >= 16 || (uint)z >= 16) return true;
            ushort below = ch.Get(x, y - 1, z);
            if (sand) return below == Blocks.Sand;
            var d = Blocks.Get(below);
            if (y >= 2 && !(d.Solid && d.Shape == Shape.Cube)) ch.Set(x, y - 1, z, Blocks.Dirt);
            return true;
        }

        static void Broadleaf(Chunk ch, int x, int y, int z, ref Rng rng, ushort log, ushort leaves, int minH, int maxH)
        {
            if (!BaseOk(ch, x, y, z, false)) return;
            int hgt = rng.Range(minH, maxH + 1);
            int top = y + hgt;
            for (int ly = top - 3; ly <= top + 1; ly++)
            {
                int r = ly >= top ? 1 : 2;
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        bool corner = Math.Abs(dx) == r && Math.Abs(dz) == r;
                        if (corner && (ly == top + 1 || rng.Value() < 0.6f)) continue;
                        Put(ch, x + dx, ly, z + dz, leaves, false);
                    }
            }
            for (int ly = y; ly < top; ly++) Put(ch, x, ly, z, log, true);
            if ((uint)x < 16 && (uint)z < 16 && ch.Get(x, y - 1, z) == Blocks.Grass) ch.Set(x, y - 1, z, Blocks.Dirt);
        }

        static void BigOak(Chunk ch, int x, int y, int z, ref Rng rng)
        {
            if (!BaseOk(ch, x, y, z, false)) return;
            int hgt = rng.Range(7, 10);
            int top = y + hgt;
            // Three or four leaf blobs around the crown, joined by short branches.
            int blobs = rng.Range(3, 5);
            for (int k = 0; k <= blobs; k++)
            {
                int cx = x, cy = top, cz = z;
                if (k > 0)
                {
                    cx += rng.Range(-2, 3); cz += rng.Range(-2, 3); cy = top - rng.Range(0, 3);
                    int sx = Math.Sign(cx - x), sz = Math.Sign(cz - z);
                    for (int s = 1; s <= Math.Max(Math.Abs(cx - x), Math.Abs(cz - z)); s++)
                        Put(ch, x + sx * Math.Min(s, Math.Abs(cx - x)), cy - 2 + s / 2, z + sz * Math.Min(s, Math.Abs(cz - z)), Blocks.OakLog, true);
                }
                float rad = k == 0 ? 3.2f : 2.4f;
                int ir = (int)Math.Ceiling(rad);
                for (int dy = -2; dy <= 2; dy++)
                    for (int dz = -ir; dz <= ir; dz++)
                        for (int dx = -ir; dx <= ir; dx++)
                        {
                            float d = dx * dx + dz * dz + dy * dy * 2.2f;
                            if (d > rad * rad + rng.Value() * 1.5f) continue;
                            Put(ch, cx + dx, cy + dy, cz + dz, Blocks.OakLeaves, false);
                        }
            }
            for (int ly = y; ly < top; ly++) Put(ch, x, ly, z, Blocks.OakLog, true);
            // Roots flare at the base.
            if (rng.Value() < 0.6f) { Put(ch, x + 1, y, z, Blocks.OakLog, true); Put(ch, x - 1, y, z, Blocks.OakLog, true); }
            else { Put(ch, x, y, z + 1, Blocks.OakLog, true); Put(ch, x, y, z - 1, Blocks.OakLog, true); }
        }

        static void Pine(Chunk ch, int x, int y, int z, ref Rng rng)
        {
            if (!BaseOk(ch, x, y, z, false)) return;
            int hgt = rng.Range(8, 13);
            int top = y + hgt;
            int bare = rng.Range(2, 4);
            int r = 0;
            for (int ly = top + 1; ly >= y + bare; ly--)
            {
                int ring = top + 1 - ly;
                r = ring == 0 ? 0 : ring == 1 ? 1 : (ring % 2 == 0 ? Math.Min(3, 1 + ring / 3) : Math.Max(1, Math.Min(3, ring / 3)));
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (dx * dx + dz * dz > r * r + 1) continue;
                        Put(ch, x + dx, ly, z + dz, Blocks.PineNeedles, false);
                    }
            }
            for (int ly = y; ly < top; ly++) Put(ch, x, ly, z, Blocks.PineLog, true);
        }

        static void Cactus(Chunk ch, int x, int y, int z, ref Rng rng)
        {
            if ((uint)x >= 16 || (uint)z >= 16) return;
            if (!BaseOk(ch, x, y, z, true)) return;
            int hgt = rng.Range(1, 4);
            for (int i = 0; i < hgt; i++) Put(ch, x, y + i, z, Blocks.Cactus, false);
        }

        void Flora(Chunk ch, Column[] cols)
        {
            int bx = ch.X * 16, bz = ch.Z * 16;
            for (int z = 0; z < 16; z++)
                for (int x = 0; x < 16; x++)
                {
                    int h = (int)cols[(x + M) + (z + M) * W].Height;
                    if (h + 1 >= Chunk.Height) continue;
                    ushort ground = ch.Get(x, h, z), above = ch.Get(x, h + 1, z);
                    if (above != 0) continue;
                    var biome = (Biome)ch.Biome[x + z * 16];
                    float r = Noise.Hash01(bx + x, 3, bz + z, Seed);
                    float p = _patch.S2((bx + x) / 9f, (bz + z) / 9f) * 0.5f + 0.5f;   // patchy meadows
                    ushort plant = 0;
                    if (ground == Blocks.Grass)
                    {
                        float grass = biome == Biome.Plains ? 0.38f : biome == Biome.Forest || biome == Biome.BirchWood ? 0.16f : 0.12f;
                        grass *= 0.4f + p * 1.2f;
                        float flower = biome == Biome.Plains ? 0.03f : 0.012f;
                        if (r < flower)
                        {
                            float k = _patch.S2((bx + x) / 30f + 50, (bz + z) / 30f);
                            plant = k > 0.25f ? Blocks.YellowFlower : k < -0.25f ? Blocks.BlueFlower : Blocks.RedFlower;
                        }
                        else if (r < flower + grass) plant = biome == Biome.Taiga ? Blocks.Fern : Blocks.TallGrass;
                    }
                    else if (ground == Blocks.SnowyGrass && r < 0.04f) plant = Blocks.Fern;
                    else if (ground == Blocks.Sand && biome == Biome.Desert && r < 0.012f) plant = Blocks.DeadBush;
                    // Sugar cane by the water's edge.
                    if ((ground == Blocks.Grass || ground == Blocks.Sand) && (h == Chunk.SeaLevel || Kind == WorldKind.Sky) && x > 0 && x < 15 && z > 0 && z < 15
                        && Noise.Hash01(bx + x, 17, bz + z, Seed) < 0.1f
                        && (ch.Get(x + 1, h, z) == Blocks.Water || ch.Get(x - 1, h, z) == Blocks.Water || ch.Get(x, h, z + 1) == Blocks.Water || ch.Get(x, h, z - 1) == Blocks.Water))
                    {
                        int tall = 1 + (int)(Noise.Hash01(bx + x, 19, bz + z, Seed) * 3);
                        for (int i = 1; i <= tall && h + i < Chunk.Height; i++) ch.Set(x, h + i, z, Blocks.SugarCane);
                        continue;
                    }
                    if (plant != 0) ch.Set(x, h + 1, z, plant);
                }
        }

        // In the Archipelago, start on an island with room on it, not a rock with one tree.
        bool Roomy(int x, int z)
        {
            int land = 0;
            for (int r = 12; r <= 36; r += 12)
                for (int a = 0; a < 8; a++)
                {
                    double ang = a * Math.PI / 4 + r;
                    if (Sample(x + (int)(Math.Cos(ang) * r), z + (int)(Math.Sin(ang) * r)).Height > Chunk.SeaLevel) land++;
                }
            return land >= 20;
        }

        // A dry spot near the origin to start on.
        public (int x, int y, int z) FindSpawn()
        {
            if (Kind == WorldKind.Sky) return SkySpawn();
            if (Builds.Is(Kind)) return Builds.Spawn(Kind);
            for (int ring = 0; ring < 4096; ring += 16)
                for (int a = 0; a < 16; a++)
                {
                    double ang = a * Math.PI / 8;
                    int x = (int)(Math.Cos(ang) * ring), z = (int)(Math.Sin(ang) * ring);
                    var col = Sample(x, z);
                    var bio = BiomeOf(col);
                    bool anyBiome = IsOnly(Kind);          // a world that's all desert or all rock has nowhere else
                    if (col.Height > Chunk.SeaLevel + 1 && col.Height < (Kind == WorldKind.OnlyPeaks ? 150 : 110)
                        && (anyBiome || bio != (Kind == WorldKind.Dunes ? Biome.Ocean : Biome.Desert) && bio != Biome.Peaks && bio != Biome.Beach)
                        && (Kind != WorldKind.Islands || Roomy(x, z)))
                        return (x, (int)col.Height + 1, z);
                }
            return (0, 100, 0);
        }
    }
}
