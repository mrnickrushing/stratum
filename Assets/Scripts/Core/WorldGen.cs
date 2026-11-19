using System;

namespace Stratum.Core
{
    public enum Biome : byte { Ocean, River, Beach, Plains, Forest, BirchWood, Taiga, Tundra, Desert, Peaks }

    // Turns a seed into terrain. Everything here is a pure function of the seed and world position, so any chunk
    // can be made in any order on any thread and always comes out the same.
    public sealed class WorldGen
    {
        public readonly int Seed;
        readonly Noise _cont, _eros, _peak, _hill, _detail, _river, _temp, _humid, _cave1, _cave2, _cheese, _strata, _patch;

        public static readonly string[] BiomeNames =
            { "Ocean", "River", "Beach", "Meadow", "Oakwood", "Birch Grove", "Pinewood", "Frostland", "Dunes", "High Peaks" };

        public WorldGen(int seed)
        {
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

        public struct Column { public float Height, Temp, Humid, River, Mountain; }

        public Column Sample(int x, int z)
        {
            float fx = x + 0.5f, fz = z + 0.5f;
            float c = Clamp(_cont.Fbm2(fx / 1100f, fz / 1100f, 4) * 1.7f, -1, 1);
            float e = Clamp(_eros.Fbm2(fx / 700f, fz / 700f, 3) * 1.7f, -1, 1) * 0.5f + 0.5f;
            float land = Smooth(-0.12f, 0.15f, c);
            float mountain = Smooth(0.25f, 0.85f, 1 - e) * land;
            float peaks = _peak.Ridge2(fx / 420f, fz / 420f, 4);
            float h = Spline(ContX, ContY, c);
            h += mountain * (float)Math.Pow(peaks, 1.5) * 115f;
            h += _hill.Fbm2(fx / 150f, fz / 150f, 4) * (4f + 12f * land) * (1 - mountain * 0.5f);
            h += _detail.Fbm2(fx / 37f, fz / 37f, 2) * 2.2f;

            float rv = Math.Abs(_river.Fbm2(fx / 650f, fz / 650f, 3));
            float river = Smooth(0.045f, 0.012f, rv) * land * (1 - mountain);
            h = Lerp(h, Math.Min(h, 58f), river);

            float t = Clamp(_temp.Fbm2(fx / 1500f + 31.3f, fz / 1500f - 12.7f, 3) * 1.8f, -1, 1);
            t -= Math.Max(0, h - 95) / 90f;
            float u = Clamp(_humid.Fbm2(fx / 1100f - 70.1f, fz / 1100f + 44.9f, 3) * 1.8f, -1, 1);
            return new Column { Height = Clamp(h, 6, 245), Temp = t, Humid = u, River = river, Mountain = mountain };
        }

        public static Biome BiomeOf(in Column col)
        {
            int h = (int)col.Height;
            if (h < Chunk.SeaLevel) return col.River > 0.4f ? Biome.River : Biome.Ocean;
            if (h > 150) return Biome.Peaks;
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
            var cols = _cols ??= new Column[W * W];
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

                    byte top, fill; int fillDepth = 3 + (int)(hh % 2);
                    switch (biome)
                    {
                        case Biome.Desert: top = Blocks.Sand; fill = Blocks.Sand; fillDepth = 4; break;
                        case Biome.Beach: top = Blocks.Sand; fill = Blocks.Sand; break;
                        case Biome.Tundra: top = Blocks.SnowyGrass; fill = Blocks.Dirt; break;
                        case Biome.Peaks: top = h > 168 ? Blocks.Snow : Blocks.Stone; fill = Blocks.Stone; break;
                        case Biome.Ocean:
                        case Biome.River:
                            float p = _patch.S2(wx / 18f, wz / 18f);
                            top = p > 0.45f ? Blocks.Clay : p < -0.4f ? Blocks.Gravel : Blocks.Sand; fill = top == Blocks.Clay ? Blocks.Clay : Blocks.Sand; break;
                        default: top = Blocks.Grass; fill = Blocks.Dirt; break;
                    }
                    if (steep && biome != Biome.Ocean && biome != Biome.River && biome != Biome.Beach)
                    {
                        top = h > 165 ? Blocks.Snow : Blocks.Stone; fill = Blocks.Stone;
                    }
                    if (biome == Biome.Taiga && h > 120) top = Blocks.SnowyGrass;

                    int ci = x | (z << 4);
                    b[ci] = Blocks.Bedrock;
                    for (int y = 1; y <= h; y++)
                    {
                        byte id;
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
                        b[ci | (y << 8)] = (y == Chunk.SeaLevel && biome == Biome.Tundra) ? Blocks.Ice : Blocks.Water;
                }

            ch.MaxY = Math.Max(maxH, Chunk.SeaLevel);
            Caves(ch, cols);
            Ores(ch);
            Trees(ch, cols);
            Flora(ch, cols);
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
                        byte cur = b[idx];
                        if (cur == Blocks.Bedrock || cur == Blocks.Water || cur == Blocks.Ice) continue;
                        b[idx] = 0;
                        // Turn the dirt under an opening back to grass.
                        if (y > 1 && b[idx - 256] == Blocks.Dirt && y >= h - 4) b[idx - 256] = Blocks.Grass;
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

        void Ores(Chunk ch)
        {
            var rng = new Rng(Noise.Hash(ch.X, 7, ch.Z, Seed));
            Vein(ch, ref rng, Blocks.CoalOre, 18, 5, 140, 9);
            Vein(ch, ref rng, Blocks.CopperOre, 10, 16, 100, 7);
            Vein(ch, ref rng, Blocks.IronOre, 8, 5, 64, 6);
            Vein(ch, ref rng, Blocks.GoldOre, 3, 5, 34, 5);
            Vein(ch, ref rng, Blocks.SunstoneOre, 2, 5, 22, 4);
            Vein(ch, ref rng, Blocks.Gravel, 5, 5, 90, 14);
            // Moss grows on rock beside cave air below 50.
        }

        static void Vein(Chunk ch, ref Rng rng, byte ore, int count, int minY, int maxY, int size)
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
                        byte cur = ch.Blocks[idx];
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
                    ref var col = ref cols[lx + lz * W];
                    var biome = BiomeOf(col);
                    float chance = biome switch
                    {
                        Biome.Forest => 0.62f, Biome.BirchWood => 0.58f, Biome.Taiga => 0.55f, Biome.Plains => 0.035f,
                        Biome.Tundra => 0.06f, Biome.Desert => 0.05f, Biome.Peaks => 0.0f, _ => 0f
                    };
                    // Woods thin out toward their edges and in clearings.
                    if (biome == Biome.Forest || biome == Biome.BirchWood || biome == Biome.Taiga)
                        chance *= Smooth(-0.35f, 0.15f, _patch.S2(wx / 45f, wz / 45f));
                    float roll = ((h >> 8) & 0xFFFF) / 65536f;
                    if (roll >= chance) continue;
                    int gy = (int)col.Height;
                    if (gy <= Chunk.SeaLevel || gy > 200) continue;
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

        static void Put(Chunk ch, int x, int y, int z, byte id, bool force)
        {
            if ((uint)x >= 16 || (uint)z >= 16 || (uint)y >= Chunk.Height) return;
            int i = x | (z << 4) | (y << 8);
            byte cur = ch.Blocks[i];
            if (force || cur == 0 || Blocks.Get(cur).Replaceable && cur != Blocks.Water) ch.Blocks[i] = id;
            else if (Blocks.Get(cur).Sway && !Blocks.Get(id).Sway) ch.Blocks[i] = id;   // a trunk beats a leaf
        }

        static bool BaseOk(Chunk ch, int x, int y, int z, bool sand)
        {
            if ((uint)x >= 16 || (uint)z >= 16) return true;   // the neighbour decides; the tree is the same there
            byte below = ch.Get(x, y - 1, z);
            if (sand) return below == Blocks.Sand;
            return below == Blocks.Grass || below == Blocks.SnowyGrass || below == Blocks.Dirt;
        }

        static void Broadleaf(Chunk ch, int x, int y, int z, ref Rng rng, byte log, byte leaves, int minH, int maxH)
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
                    byte ground = ch.Get(x, h, z), above = ch.Get(x, h + 1, z);
                    if (above != 0) continue;
                    var biome = (Biome)ch.Biome[x + z * 16];
                    float r = Noise.Hash01(bx + x, 3, bz + z, Seed);
                    float p = _patch.S2((bx + x) / 9f, (bz + z) / 9f) * 0.5f + 0.5f;   // patchy meadows
                    byte plant = 0;
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
                    if (plant != 0) ch.Set(x, h + 1, z, plant);
                }
        }

        // A dry spot near the origin to start on.
        public (int x, int y, int z) FindSpawn()
        {
            for (int ring = 0; ring < 4096; ring += 16)
                for (int a = 0; a < 16; a++)
                {
                    double ang = a * Math.PI / 8;
                    int x = (int)(Math.Cos(ang) * ring), z = (int)(Math.Sin(ang) * ring);
                    var col = Sample(x, z);
                    var bio = BiomeOf(col);
                    if (col.Height > Chunk.SeaLevel + 1 && col.Height < 110 && bio != Biome.Desert && bio != Biome.Peaks && bio != Biome.Beach)
                        return (x, (int)col.Height + 1, z);
                }
            return (0, 100, 0);
        }
    }
}
