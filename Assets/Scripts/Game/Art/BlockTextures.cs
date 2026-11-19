using Stratum.Core;
using UnityEngine;

namespace Stratum.Game
{
    // Paints every block face in code (no downloaded art): a 64-pixel colour layer and a normal layer per Tex,
    // tileable, with mipmaps. Grass, oak leaves and plants are painted pale so the biome tint colours them.
    public static class BlockTextures
    {
        public static Texture2DArray Albedo, Normal;
        public static Color[] Average = new Color[(int)Tex.Count];
        static Color[][] _pixels = new Color[(int)Tex.Count][];

        public static Color[] Pixels(Tex t) => _pixels[(int)t];

        public static void Build()
        {
            if (Albedo != null) return;
            int n = (int)Tex.Count;
            Albedo = new Texture2DArray(Paint.S, Paint.S, n, TextureFormat.RGBA32, true, false)
            { filterMode = FilterMode.Trilinear, anisoLevel = 8, wrapMode = TextureWrapMode.Repeat, name = "BlockAlbedo" };
            Normal = new Texture2DArray(Paint.S, Paint.S, n, TextureFormat.RGBA32, true, true)
            { filterMode = FilterMode.Trilinear, anisoLevel = 8, wrapMode = TextureWrapMode.Repeat, name = "BlockNormal" };
            for (int i = 0; i < n; i++)
            {
                var p = new Paint(1000 + i * 31);
                float bump = Draw((Tex)i, p);
                Albedo.SetPixels(p.C, i);
                Normal.SetPixels(p.Normals(bump), i);
                _pixels[i] = p.C;
                Color sum = Color.clear; int k = 0;
                foreach (var c in p.C) if (c.a > 0.5f) { sum += c; k++; }
                Average[i] = k > 0 ? sum / k : Color.gray;
            }
            Albedo.Apply(true, true);
            Normal.Apply(true, true);
        }

        static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1);

        // Returns the bump strength for the normal map.
        static float Draw(Tex t, Paint p)
        {
            switch (t)
            {
                case Tex.Stone: Rock(p, Hex(0x85868a), Hex(0x6c6d72), 0.18f, true); return 2.5f;
                case Tex.Slate: Layered(p, Hex(0x50555f), Hex(0x3b3f48), 7); return 3f;
                case Tex.Basalt: Columns(p, Hex(0x34343a), Hex(0x24242a)); return 3f;
                case Tex.Bedrock: Rock(p, Hex(0x4a4a4e), Hex(0x1c1c20), 0.45f, false); return 4f;
                case Tex.Dirt: Dirt(p); return 2.5f;
                case Tex.GrassTop: GrassTop(p); return 2f;
                case Tex.GrassSide: Dirt(p); Fringe(p, new Color(0.86f, 0.92f, 0.80f), true); return 2.5f;
                case Tex.Sand: Sand(p, Hex(0xdacb9a), Hex(0xc2ae7c)); return 1.5f;
                case Tex.Gravel: Pebbles(p, 7, Hex(0x8a8580), Hex(0x5a5550), Hex(0x3a3633)); return 4f;
                case Tex.SandstoneSide: Layered(p, Hex(0xd8c48e), Hex(0xbfa671), 5); return 2f;
                case Tex.SandstoneTop: Sand(p, Hex(0xdac795), Hex(0xcbb683)); Cracks(p, 6, 0.6f, Hex(0xb09868)); return 1.5f;
                case Tex.Snow: Snow(p); return 1f;
                case Tex.SnowSide: Dirt(p); Fringe(p, Hex(0xf2f6fb), false); return 2f;
                case Tex.Ice: Ice(p); return 1f;
                case Tex.Water: Water(p); return 1f;
                case Tex.Clay: Rock(p, Hex(0x9fa4b0), Hex(0x8c909c), 0.08f, false); return 1f;
                case Tex.OakLog: Bark(p, Hex(0x6a5034), Hex(0x3e2d1c), 7); return 4f;
                case Tex.OakLogTop: Rings(p, Hex(0xb08a58), Hex(0x8a6a40), Hex(0x5a4128)); return 2f;
                case Tex.OakLeaves: Leaves(p, new Color(0.80f, 0.92f, 0.74f), 0.30f, 5); return 3f;
                case Tex.BirchLog: Birch(p); return 2f;
                case Tex.BirchLogTop: Rings(p, Hex(0xd8c49a), Hex(0xc0aa7c), Hex(0xe8e4da)); return 2f;
                case Tex.BirchLeaves: Leaves(p, Hex(0x86a84c), 0.28f, 6); return 3f;
                case Tex.PineLog: Plates(p, Hex(0x5a3a26), Hex(0x2e1d12)); return 4f;
                case Tex.PineLogTop: Rings(p, Hex(0xa87850), Hex(0x8a5c38), Hex(0x4a2e1c)); return 2f;
                case Tex.PineNeedles: Needles(p); return 3f;
                case Tex.CactusSide: Cactus(p, false); return 3f;
                case Tex.CactusTop: Cactus(p, true); return 2f;
                case Tex.OakPlanks: Planks(p, Hex(0xb08a56), 4); return 2.5f;
                case Tex.BirchPlanks: Planks(p, Hex(0xd9c393), 4); return 2.5f;
                case Tex.PinePlanks: Planks(p, Hex(0x8a5e3a), 4); return 2.5f;
                case Tex.Cobble: Cobble(p, false); return 5f;
                case Tex.MossyCobble: Cobble(p, true); return 5f;
                case Tex.StoneBricks: Bricks(p, 4, 2, Hex(0x8c8d92), Hex(0x5a5b60), 2); return 5f;
                case Tex.ClayBricks: Bricks(p, 8, 4, Hex(0xa0523c), Hex(0xc8bfae), 1); return 4f;
                case Tex.SlateTiles: Bricks(p, 2, 2, Hex(0x4c515c), Hex(0x2a2d33), 2); return 4f;
                case Tex.Glass: Glass(p); return 0.5f;
                case Tex.CoalOre: Ore(p, Hex(0x1e1e22), Hex(0x3a3a40), false); return 4f;
                case Tex.CopperOre: Ore(p, Hex(0xd07a46), Hex(0x4f9a82), false); return 4f;
                case Tex.IronOre: Ore(p, Hex(0xd8b49a), Hex(0x9a7462), false); return 4f;
                case Tex.GoldOre: Ore(p, Hex(0xf4d046), Hex(0xc49a22), false); return 4f;
                case Tex.SunstoneOre: Ore(p, Hex(0xffc25a), Hex(0xff8a2a), true); return 4f;
                case Tex.Sunstone: Sunstone(p); return 3f;
                case Tex.Torch: Torch(p); return 1f;
                case Tex.TallGrass: Blades(p, new Color(0.84f, 0.94f, 0.78f), 38, 0.95f); return 1f;
                case Tex.Fern: Fern(p); return 1f;
                case Tex.RedFlower: Flower(p, Hex(0xd8302a), Hex(0x2a1a10), 5); return 1f;
                case Tex.YellowFlower: Flower(p, Hex(0xf2c830), Hex(0xd08a10), 4); return 1f;
                case Tex.BlueFlower: Flower(p, Hex(0x4a72e0), Hex(0x24305a), 7); return 1f;
                case Tex.DeadBush: Twigs(p); return 1f;
                case Tex.BenchTop: BenchTop(p); return 3f;
                case Tex.BenchSide: BenchSide(p); return 3f;
                case Tex.FurnaceSide: Rock(p, Hex(0x7c7d82), Hex(0x5c5d62), 0.12f, false); Frame(p, Hex(0x4a4b50), 3); return 3f;
                case Tex.FurnaceTop: Rock(p, Hex(0x87888c), Hex(0x6a6b70), 0.08f, false); Frame(p, Hex(0x4a4b50), 3); return 3f;
                case Tex.FurnaceFront: Furnace(p); return 4f;
                case Tex.CopperBlock: Metal(p, Hex(0xd08050), Hex(0x8a4a2a)); return 3f;
                case Tex.IronBlock: Metal(p, Hex(0xd4d6da), Hex(0x8a8c92)); return 3f;
                case Tex.GoldBlock: Metal(p, Hex(0xf6d460), Hex(0xb88a20)); return 3f;
            }
            Rock(p, Color.magenta, Color.black, 0, false);
            return 1;
        }

        // ------------------------------------------------------------------ natural surfaces

        static void Rock(Paint p, Color a, Color b, float speck, bool cracks)
        {
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    float n = p.Fbm(x, y, 4, 4);
                    float m = p.Fbm(x + 17, y + 31, 2, 2);
                    float s = p.V(x, y, 32, 9);
                    Color c = Color.Lerp(b, a, Mathf.Clamp01(n * 1.3f - 0.15f + (m - 0.5f) * 0.4f));
                    if (s > 1 - speck) c = Paint.Mul(c, 0.78f);
                    else if (s < speck * 0.5f) c = Paint.Mul(c, 1.12f);
                    c.a = 1;
                    p.Set(x, y, c, n);
                }
            if (cracks) Cracks(p, 5, 0.7f, Paint.Mul(b, 0.75f));
        }

        static void Cracks(Paint p, int cells, float width, Color col)
        {
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    p.Cells(x + p.V(x, y, 8, 3) * 6, y + p.V(x + 9, y, 8, 4) * 6, cells, out float f1, out float f2, out int id);
                    if (p.Hash(id, 3) > 0.55f) continue;
                    float e = f2 - f1;
                    if (e < width) { int i = Paint.I(x, y); p.C[i] = Color.Lerp(p.C[i], col, 0.7f * (1 - e / width)); p.H[i] -= 0.3f; }
                }
        }

        static void Layered(Paint p, Color a, Color b, int bands)
        {
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    float warp = p.Fbm(x, y, 2, 2) * 10;
                    float band = Mathf.Sin((y + warp) / 64f * Mathf.PI * 2 * bands) * 0.5f + 0.5f;
                    float n = p.Fbm(x, y, 8, 3);
                    Color c = Color.Lerp(b, a, band * 0.6f + n * 0.4f);
                    c.a = 1;
                    p.Set(x, y, c, band * 0.6f + n * 0.4f);
                }
        }

        static void Columns(Paint p, Color a, Color b)
        {
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    p.Cells(x, y, 3, out float f1, out float f2, out int id);
                    float e = Mathf.Clamp01((f2 - f1) / 4f);
                    float n = p.Fbm(x, y, 8, 3);
                    Color c = Color.Lerp(b, a, e * 0.6f + n * 0.4f) * (0.9f + p.Hash(id, 1) * 0.2f);
                    c.a = 1;
                    p.Set(x, y, c, e * 0.8f + n * 0.2f);
                }
        }

        static void Dirt(Paint p)
        {
            Color a = Hex(0x7a5a3c), b = Hex(0x553c26);
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    float n = p.Fbm(x, y, 4, 4);
                    p.Cells(x, y, 9, out float f1, out _, out int id);
                    Color c = Color.Lerp(b, a, n);
                    float h = n;
                    if (f1 < 2.2f && p.Hash(id, 2) < 0.4f) { c = Color.Lerp(c, p.Hash(id, 5) < 0.5f ? Hex(0x8e7e6a) : Hex(0x3a2a1c), 0.7f); h += 0.4f; }
                    c.a = 1;
                    p.Set(x, y, c, h);
                }
        }

        static void GrassTop(Paint p)
        {
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    float n = p.Fbm(x, y, 4, 3);
                    float blades = p.V(x, y, 32, 5) * 0.6f + p.V(x, y, 64, 6) * 0.4f;
                    float k = 0.70f + n * 0.25f + (blades - 0.5f) * 0.35f;
                    var c = new Color(0.92f * k, 0.98f * k, 0.88f * k, 1);
                    p.Set(x, y, c, blades);
                }
        }

        // The top band of a side face: grass (tinted later, alpha marks it) or snow, hanging down in tufts.
        static void Fringe(Paint p, Color col, bool tintMask)
        {
            for (int x = 0; x < 64; x++)
            {
                float depth = 9 + p.V(x, 0, 16, 7) * 6 + p.V(x, 0, 64, 8) * 6;
                for (int y = 63; y >= 0; y--)
                {
                    int i = Paint.I(x, y);
                    float d = 63 - y;
                    if (d < depth)
                    {
                        float k = 0.72f + p.Fbm(x, y, 8, 3) * 0.3f;
                        var c = Paint.Mul(col, k); c.a = 1;
                        p.C[i] = c; p.H[i] = 0.6f + k * 0.3f;
                    }
                    else if (d < depth + 1.5f) { p.C[i] = Paint.Mul(p.C[i], 0.7f); if (tintMask) p.C[i].a = 0; }
                    else if (tintMask) p.C[i].a = 0;
                }
            }
        }

        static void Sand(Paint p, Color a, Color b)
        {
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    float n = p.Fbm(x, y, 4, 3);
                    float g = p.V(x, y, 64, 4);
                    float rip = Mathf.Sin((x + p.Fbm(x, y, 2, 2) * 20) / 64f * Mathf.PI * 2 * 3) * 0.5f + 0.5f;
                    Color c = Color.Lerp(b, a, n * 0.6f + rip * 0.25f + g * 0.25f);
                    c.a = 1;
                    p.Set(x, y, c, rip * 0.5f + g * 0.3f);
                }
        }

        static void Pebbles(Paint p, int cells, Color light, Color mid, Color gap)
        {
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    p.Cells(x, y, cells, out float f1, out float f2, out int id);
                    float e = Mathf.Clamp01((f2 - f1) / 3f);
                    Color stone = Color.Lerp(mid, light, p.Hash(id, 1)) * (0.85f + p.Fbm(x, y, 16, 2) * 0.3f);
                    Color c = Color.Lerp(gap, stone, Mathf.SmoothStep(0, 1, e));
                    c.a = 1;
                    p.Set(x, y, c, e);
                }
        }

        static void Snow(Paint p)
        {
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    float n = p.Fbm(x, y, 4, 4);
                    float sp = p.V(x, y, 64, 3);
                    Color c = Color.Lerp(Hex(0xd6e0ee), Hex(0xf8fbff), n);
                    if (sp > 0.93f) c = Color.white;
                    c.a = 1;
                    p.Set(x, y, c, n);
                }
        }

        static void Ice(Paint p)
        {
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    float n = p.Fbm(x, y, 2, 3);
                    Color c = Color.Lerp(Hex(0x8fb4dc), Hex(0xc8def4), n); c.a = 1;
                    p.Set(x, y, c, n * 0.3f);
                }
            Cracks(p, 4, 0.6f, Hex(0xeef6ff));
        }

        static void Water(Paint p)
        {
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    float n = p.Fbm(x, y, 4, 3);
                    Color c = Color.Lerp(Hex(0x2a5a8a), Hex(0x3a7ab0), n); c.a = 1;
                    p.Set(x, y, c, n);
                }
        }

        // ------------------------------------------------------------------ wood

        static void Bark(Paint p, Color a, Color b, int ridges)
        {
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    float s = p.Streak(x, y, ridges, 2, 3);
                    float n = p.Fbm(x, y, 8, 2);
                    float r = Mathf.Pow(Mathf.Abs(s - 0.5f) * 2, 0.7f);
                    Color c = Color.Lerp(b, a, 1 - r * 0.8f + (n - 0.5f) * 0.3f); c.a = 1;
                    p.Set(x, y, c, 1 - r);
                }
        }

        static void Rings(Paint p, Color light, Color dark, Color bark)
        {
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    float dx = x - 31.5f, dy = y - 31.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) + p.Fbm(x, y, 4, 2) * 3;
                    float edge = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
                    Color c;
                    float h;
                    if (edge > 28) { c = bark * (0.8f + p.Fbm(x, y, 16, 2) * 0.4f); h = 0.8f; }
                    else
                    {
                        float ring = Mathf.Sin(d * 1.1f) * 0.5f + 0.5f;
                        c = Color.Lerp(dark, light, ring * 0.7f + 0.3f * p.Fbm(x, y, 8, 2));
                        h = ring * 0.3f;
                    }
                    c.a = 1;
                    p.Set(x, y, c, h);
                }
        }

        static void Birch(Paint p)
        {
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    float n = p.Fbm(x, y, 4, 3);
                    Color c = Color.Lerp(Hex(0xc8c4b8), Hex(0xeeece4), n); c.a = 1;
                    p.Set(x, y, c, 0.5f + n * 0.2f);
                }
            // Dark lenticels: short horizontal dashes.
            for (int k = 0; k < 22; k++)
            {
                int cx = (int)(p.Hash(k, 1) * 64), cy = (int)(p.Hash(k, 2) * 64);
                int len = 4 + (int)(p.Hash(k, 3) * 12), th = 1 + (int)(p.Hash(k, 4) * 2.5f);
                for (int dy = 0; dy < th; dy++)
                    for (int dx = 0; dx < len; dx++)
                    {
                        float fade = 1 - Mathf.Abs(dx - len / 2f) / (len / 2f + 1);
                        p.Blend(cx + dx, cy + dy, Hex(0x2a2622), 0.85f * fade, 0.1f);
                    }
            }
        }

        static void Plates(Paint p, Color a, Color b)
        {
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    p.Cells(x * 1.6f, y * 0.8f, 6, out float f1, out float f2, out int id);
                    float e = Mathf.Clamp01((f2 - f1) / 5f);
                    Color c = Color.Lerp(b, a, Mathf.SmoothStep(0, 1, e) * (0.8f + p.Hash(id, 2) * 0.3f)); c.a = 1;
                    p.Set(x, y, c, e);
                }
        }

        static void Planks(Paint p, Color baseCol, int boards)
        {
            int bh = 64 / boards;
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    int board = y / bh;
                    float shade = 0.86f + p.Hash(board, 1) * 0.22f;
                    float grain = p.Streak(x + board * 13, y, 2, 12, 3);
                    float knot = 0;
                    {
                        float kx = p.Hash(board, 4) * 64, ky = board * bh + bh * 0.5f;
                        float dx = Mathf.DeltaAngle(x * 5.625f, kx * 5.625f) / 5.625f, dy = (y - ky) * 1.8f;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        if (p.Hash(board, 5) < 0.5f && d < 4) knot = 1 - d / 4;
                    }
                    Color c = Paint.Mul(baseCol, shade * (0.85f + grain * 0.3f) * (1 - knot * 0.35f));
                    float h = 0.5f + grain * 0.2f;
                    int ly = y % bh;
                    if (ly == 0) { c = Paint.Mul(baseCol, 0.45f); h = 0; }
                    else if (ly == 1 || ly == bh - 1) { c = Paint.Mul(c, 0.85f); h = 0.35f; }
                    // Butt joint on alternate boards.
                    int jx = (int)(p.Hash(board, 7) * 64);
                    if (board % 2 == 1 && (x == jx || x == ((jx + 32) & 63))) { c = Paint.Mul(baseCol, 0.5f); h = 0.1f; }
                    c.a = 1;
                    p.Set(x, y, c, h);
                }
        }

        static void Leaves(Paint p, Color baseCol, float holes, int cells)
        {
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    p.Cells(x, y, cells * 2, out float f1, out float f2, out int id);
                    float leaf = Mathf.Clamp01(1 - f1 / 4.2f);
                    float n = p.Fbm(x, y, 4, 3);
                    float shade = 0.62f + leaf * 0.38f + (p.Hash(id, 1) - 0.5f) * 0.25f;
                    Color c = Paint.Mul(baseCol, shade * (0.85f + n * 0.3f));
                    float hole = p.Fbm(x + 40, y + 11, 8, 2);
                    c.a = hole < holes ? 0 : 1;
                    p.Set(x, y, c, leaf);
                }
        }

        static void Needles(Paint p)
        {
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    float s = p.Streak(x + y * 0.6f, y, 24, 3, 2);
                    float n = p.Fbm(x, y, 4, 3);
                    Color c = Color.Lerp(Hex(0x1c3226), Hex(0x3e6a48), s * 0.6f + n * 0.4f);
                    c.a = p.Fbm(x + 7, y + 3, 8, 2) < 0.26f ? 0 : 1;
                    p.Set(x, y, c, s);
                }
        }

        static void Cactus(Paint p, bool top)
        {
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    float n = p.Fbm(x, y, 4, 2);
                    float rib = top ? 1 - Mathf.Clamp01(Mathf.Abs(Mathf.Sqrt((x - 31.5f) * (x - 31.5f) + (y - 31.5f) * (y - 31.5f)) % 10 - 5) / 5)
                                    : Mathf.Abs(Mathf.Sin(x / 64f * Mathf.PI * 6));
                    Color c = Color.Lerp(Hex(0x2a5222), Hex(0x5a8c3e), rib * 0.6f + n * 0.4f);
                    if (!top && (x < 2 || x > 61)) c = Paint.Mul(c, 0.7f);
                    c.a = 1;
                    p.Set(x, y, c, rib);
                }
            for (int k = 0; k < 26; k++)
            {
                int x = (int)(p.Hash(k, 1) * 64), y = (int)(p.Hash(k, 2) * 64);
                p.Set(x, y, Hex(0xf0e6b0), 1); p.Set(x + 1, y, Hex(0xd8cc90), 1);
            }
        }

        // ------------------------------------------------------------------ built

        static void Cobble(Paint p, bool moss)
        {
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    float wx = x + p.Fbm(x, y, 4, 2) * 5, wy = y + p.Fbm(x + 13, y, 4, 2) * 5;
                    p.Cells(wx, wy, 5, out float f1, out float f2, out int id);
                    float e = Mathf.Clamp01((f2 - f1) / 5f);
                    float bevel = Mathf.SmoothStep(0, 1, e);
                    Color stone = Color.Lerp(Hex(0x5e5f64), Hex(0x9a9ba0), p.Hash(id, 1) * 0.7f + p.Fbm(x, y, 16, 2) * 0.3f);
                    Color c = Color.Lerp(Hex(0x2e2f33), stone * (0.75f + bevel * 0.35f), Mathf.Clamp01(e * 2.5f));
                    if (moss)
                    {
                        float m = p.Fbm(x + 5, y + 50, 4, 3);
                        if (m > 0.55f - (1 - e) * 0.25f) c = Color.Lerp(c, Hex(0x4a6e2a) * (0.8f + m * 0.4f), 0.85f);
                    }
                    c.a = 1;
                    p.Set(x, y, c, bevel);
                }
        }

        static void Bricks(Paint p, int rows, int perRow, Color brick, Color mortar, int gap)
        {
            int bh = 64 / rows, bw = 64 / perRow;
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    int row = y / bh;
                    int ox = (row % 2) * bw / 2;
                    int col = ((x + ox) / bw) % perRow;
                    int lx = (x + ox) % bw, ly = y % bh;
                    int id = row * 16 + col;
                    float edge = Mathf.Min(Mathf.Min(lx, bw - 1 - lx), Mathf.Min(ly, bh - 1 - ly));
                    float n = p.Fbm(x, y, 8, 3);
                    Color c;
                    float h;
                    if (edge < gap) { c = Paint.Mul(mortar, 0.85f + n * 0.3f); h = 0; }
                    else
                    {
                        float bevel = Mathf.Clamp01((edge - gap + 1) / 3f);
                        c = Paint.Mul(brick, (0.82f + p.Hash(id, 1) * 0.3f) * (0.85f + n * 0.25f) * (0.85f + bevel * 0.15f));
                        h = 0.4f + bevel * 0.5f + n * 0.1f;
                    }
                    c.a = 1;
                    p.Set(x, y, c, h);
                }
        }

        static void Frame(Paint p, Color col, int w)
        {
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                    if (x < w || y < w || x >= 64 - w || y >= 64 - w) { int i = Paint.I(x, y); p.C[i] = Color.Lerp(p.C[i], col, 0.7f); p.H[i] = 0.9f; }
        }

        static void Glass(Paint p)
        {
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    bool frame = x < 3 || y < 3 || x > 60 || y > 60;
                    Color c = frame ? Color.Lerp(Hex(0xb8ccd8), Hex(0xe8f2f8), p.Fbm(x, y, 8, 2)) : new Color(0.8f, 0.9f, 1f, 0);
                    // Glints across the pane.
                    float g = Mathf.Abs((x - y) - 14), g2 = Mathf.Abs((x - y) + 6);
                    if (!frame && (g < 1.2f && x > 10 && x < 34 || g2 < 0.8f && x > 30 && x < 48)) c = new Color(0.92f, 0.97f, 1f, 1);
                    p.Set(x, y, c, frame ? 1 : 0);
                }
        }

        static void Ore(Paint p, Color hi, Color lo, bool glow)
        {
            Rock(p, Hex(0x85868a), Hex(0x6c6d72), 0.15f, false);
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    int i = Paint.I(x, y);
                    if (glow) p.C[i].a = 0;
                    p.Cells(x, y, 6, out float f1, out _, out int id);
                    if (p.Hash(id, 4) > 0.6f) continue;
                    float r = 3.2f + p.Hash(id, 5) * 2.4f;
                    if (f1 > r) continue;
                    float k = 1 - f1 / r;
                    Color c = Color.Lerp(lo, hi, k * 0.8f + p.V(x, y, 32, 6) * 0.3f);
                    c.a = glow ? Mathf.Clamp01(k * 2) : 1;
                    p.C[i] = c; p.H[i] = 0.7f + k * 0.4f;
                }
        }

        static void Sunstone(Paint p)
        {
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    p.Cells(x, y, 4, out float f1, out float f2, out int id);
                    float e = Mathf.Clamp01((f2 - f1) / 6f);
                    Color c = Color.Lerp(Hex(0x8a4a10), Color.Lerp(Hex(0xffb040), Hex(0xfff0b0), e), Mathf.Clamp01(e * 3));
                    c *= 0.85f + p.Hash(id, 2) * 0.3f; c.a = 1;
                    p.Set(x, y, c, e);
                }
            Frame(p, Hex(0x6a4a2a), 3);
        }

        static void Torch(Paint p)
        {
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++) p.Set(x, y, new Color(0, 0, 0, 0), 0);
            for (int y = 0; y < 40; y++)
                for (int x = 28; x < 36; x++)
                {
                    Color c = Color.Lerp(Hex(0x5a3a1e), Hex(0x8a6038), p.Streak(x, y, 4, 2, 2));
                    if (y > 30) c = Color.Lerp(Hex(0xfff0a0), Hex(0xff8a20), (y - 30) / 10f * 0.6f + Mathf.Abs(x - 31.5f) / 6f);
                    p.Set(x, y, c, 0.5f);
                }
        }

        static void Blades(Paint p, Color col, int count, float height)
        {
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) p.Set(x, y, new Color(col.r, col.g, col.b, 0), 0);
            for (int k = 0; k < count; k++)
            {
                float bx = p.Hash(k, 1) * 60 + 2, lean = (p.Hash(k, 2) - 0.5f) * 0.6f;
                float h = (0.45f + p.Hash(k, 3) * 0.55f) * height * 64;
                float w = 1.2f + p.Hash(k, 4) * 1.6f;
                float shade = 0.65f + p.Hash(k, 5) * 0.4f;
                for (int y = 0; y < h; y++)
                {
                    float t = y / h;
                    float cx = bx + lean * y + lean * lean * y * t * 8;
                    float ww = w * (1 - t * 0.85f);
                    for (int x = Mathf.FloorToInt(cx - ww); x <= Mathf.CeilToInt(cx + ww); x++)
                    {
                        if (x < 0 || x > 63) continue;
                        if (Mathf.Abs(x - cx) > ww) continue;
                        var c = Paint.Mul(col, shade * (0.6f + t * 0.5f)); c.a = 1;
                        p.Set(x, y, c, t);
                    }
                }
            }
        }

        static void Fern(Paint p)
        {
            Color col = new Color(0.78f, 0.90f, 0.72f);
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) p.Set(x, y, new Color(col.r, col.g, col.b, 0), 0);
            for (int f = 0; f < 5; f++)
            {
                float ang = Mathf.Lerp(-1.0f, 1.0f, f / 4f) + (p.Hash(f, 1) - 0.5f) * 0.2f;
                float len = 40 + p.Hash(f, 2) * 18;
                for (float t = 0; t < 1; t += 0.008f)
                {
                    float bend = t * t * 0.6f * Mathf.Sign(ang);
                    float px = 32 + Mathf.Sin(ang + bend) * len * t, py = 2 + Mathf.Cos(ang + bend) * len * t;
                    p.Set((int)px, (int)py, Paint.Mul(col, 0.6f), t);
                    float leaflet = (1 - t) * 7 + 1;
                    for (int s = -1; s <= 1; s += 2)
                        for (float q = 0; q < leaflet; q += 0.7f)
                        {
                            float lx = px + Mathf.Cos(ang + bend) * q * s, ly = py - Mathf.Sin(ang + bend) * q * s + q * 0.4f;
                            if (lx < 0 || lx > 63 || ly < 0 || ly > 63) continue;
                            var c = Paint.Mul(col, 0.7f + t * 0.35f); c.a = 1;
                            p.Set((int)lx, (int)ly, c, t);
                        }
                }
            }
        }

        static void Flower(Paint p, Color petal, Color centre, int petals)
        {
            Blades(p, Hex(0x4a8a34), 6, 0.55f);
            Color stem = Hex(0x3a7a2a);
            for (int y = 0; y < 40; y++) for (int x = 31; x < 34; x++) p.Set(x, y, stem, 0.5f);
            for (int y = 12; y < 22; y++) for (int x = 34; x < 34 + (y - 12) / 2; x++) p.Set(x, y, stem, 0.5f);
            float cx = 32, cy = 44, r = 11;
            for (int y = 28; y < 60; y++)
                for (int x = 16; x < 48; x++)
                {
                    float dx = x - cx, dy = y - cy;
                    float d = Mathf.Sqrt(dx * dx + dy * dy), a = Mathf.Atan2(dy, dx);
                    float edge = r * (0.75f + 0.25f * Mathf.Cos(a * petals));
                    if (d < edge)
                    {
                        Color c = d < 3.5f ? centre : Paint.Mul(petal, 0.75f + 0.25f * (1 - d / edge) + p.V(x, y, 32, 3) * 0.15f);
                        c.a = 1;
                        p.Set(x, y, c, 1 - d / edge);
                    }
                }
        }

        static void Twigs(Paint p)
        {
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) p.Set(x, y, new Color(0.4f, 0.3f, 0.2f, 0), 0);
            void Branch(float x, float y, float ang, float len, int depth, int seed)
            {
                for (float t = 0; t < len; t += 0.5f)
                {
                    int px = (int)(x + Mathf.Sin(ang) * t), py = (int)(y + Mathf.Cos(ang) * t);
                    if (px < 0 || px > 63 || py < 0 || py > 63) continue;
                    p.Set(px, py, Color.Lerp(Hex(0x6a4a2a), Hex(0x9a7a50), p.Hash(seed, 1)), 0.5f);
                    if (depth == 0) p.Set(px + 1, py, Hex(0x5a3a1e), 0.5f);
                }
                if (depth >= 3) return;
                float ex = x + Mathf.Sin(ang) * len, ey = y + Mathf.Cos(ang) * len;
                Branch(ex, ey, ang - 0.5f - p.Hash(seed, 2) * 0.3f, len * 0.65f, depth + 1, seed * 2 + 1);
                Branch(ex, ey, ang + 0.5f + p.Hash(seed, 3) * 0.3f, len * 0.65f, depth + 1, seed * 2 + 2);
            }
            Branch(32, 0, -0.15f, 16, 0, 1);
            Branch(32, 0, 0.6f, 12, 1, 7);
        }

        static void BenchTop(Paint p)
        {
            Planks(p, Hex(0xb08a56), 4);
            Frame(p, Hex(0x5a4128), 4);
            // A cutting mat with a grid in the middle.
            for (int y = 14; y < 50; y++)
                for (int x = 14; x < 50; x++)
                {
                    bool line = (x - 14) % 12 == 0 || (y - 14) % 12 == 0;
                    Color c = line ? Hex(0x3a2a1a) : Color.Lerp(Hex(0x8a6a44), Hex(0x9a7a50), p.Fbm(x, y, 8, 2));
                    c.a = 1;
                    p.Set(x, y, c, line ? 0.2f : 0.5f);
                }
        }

        static void BenchSide(Paint p)
        {
            Planks(p, Hex(0x9a7448), 4);
            Frame(p, Hex(0x5a4128), 3);
            // A saw and a hammer hanging on the side.
            for (int y = 26; y < 52; y++)
                for (int x = 10; x < 30; x++)
                {
                    float t = (y - 26) / 26f;
                    if (x - 10 < 6 + t * 10 && x - 10 > t * 4) p.Set(x, y, Color.Lerp(Hex(0xb8bcc4), Hex(0x8a8e96), t), 0.8f);
                }
            for (int y = 46; y < 56; y++) for (int x = 8; x < 18; x++) p.Set(x, y, Hex(0x6a3a1e), 0.9f);
            for (int y = 18; y < 50; y++) for (int x = 42; x < 46; x++) p.Set(x, y, Hex(0x7a5030), 0.8f);
            for (int y = 44; y < 54; y++) for (int x = 36; x < 52; x++) p.Set(x, y, Hex(0x5a5c62), 1f);
        }

        static void Furnace(Paint p)
        {
            Rock(p, Hex(0x7c7d82), Hex(0x5c5d62), 0.12f, false);
            Frame(p, Hex(0x4a4b50), 3);
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) p.C[Paint.I(x, y)].a = 0;
            // Mouth with embers (alpha marks the glow).
            for (int y = 8; y < 30; y++)
                for (int x = 14; x < 50; x++)
                {
                    bool arch = y < 24 || (x - 32) * (x - 32) / 324f + (y - 24) * (y - 24) / 36f < 1;
                    if (!arch) continue;
                    bool rim = x < 16 || x > 47 || y < 10;
                    if (rim) { p.Set(x, y, Hex(0x2a2a2e), 0.3f); continue; }
                    float e = p.Fbm(x, y, 8, 2);
                    float heat = Mathf.Clamp01((22 - y) / 14f) * e;
                    Color c = Color.Lerp(Hex(0x140c08), Color.Lerp(Hex(0xc0400a), Hex(0xffc050), heat), heat * 1.5f);
                    c.a = Mathf.Clamp01(heat * 1.6f);
                    p.Set(x, y, c, 0);
                }
            // Vent slots.
            for (int s = 0; s < 3; s++)
                for (int y = 42; y < 52; y++)
                    for (int x = 18 + s * 10; x < 24 + s * 10; x++) { var c = Hex(0x26262a); c.a = 0; p.Set(x, y, c, 0.2f); }
        }

        static void Metal(Paint p, Color light, Color dark)
        {
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    float brushed = p.Streak(x, y, 48, 2, 2);
                    float n = p.Fbm(x, y, 4, 2);
                    float edge = Mathf.Min(Mathf.Min(x, 63 - x), Mathf.Min(y, 63 - y));
                    float bevel = Mathf.Clamp01(edge / 5f);
                    Color c = Color.Lerp(dark, light, 0.55f + brushed * 0.25f + n * 0.2f) * (0.75f + bevel * 0.25f);
                    if (edge < 1) c = Paint.Mul(dark, 0.7f);
                    c.a = 1;
                    p.Set(x, y, c, bevel);
                }
            foreach (var (rx, ry) in new[] { (8, 8), (55, 8), (8, 55), (55, 55) })
                for (int y = -2; y <= 2; y++)
                    for (int x = -2; x <= 2; x++)
                        if (x * x + y * y <= 5) p.Set(rx + x, ry + y, Color.Lerp(light, Color.white, 0.3f - y * 0.08f), 1.2f);
        }
    }
}
