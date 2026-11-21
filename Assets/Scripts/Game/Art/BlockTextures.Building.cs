using Stratum.Core;
using UnityEngine;

namespace Stratum.Game
{
    // Paint for the 0.11 building set: the extra colours, doors in other woods and furniture faces.
    public static partial class BlockTextures
    {
        static Color Dye(int i) => Hex((int)Blocks.Dyes[i].rgb);

        static float Building(Tex t, Paint p)
        {
            if (t >= Tex.WoolOrange && t <= Tex.WoolLightGrey) { Wool(p, Dye(Blocks.DyeIndex(Blocks.NewWool[t - Tex.WoolOrange]))); return 2f; }
            if (t >= Tex.PlasterTint0 && t < Tex.Tiles0)
            {
                // Plaster keeps its chalky look: the colour is mixed toward white.
                Color c = Color.Lerp(Dye(t - Tex.PlasterTint0), Hex(0xe9e4d6), 0.3f);
                PlasterTex(p, c, Paint.Mul(c, 0.88f)); return 0.8f;
            }
            if (t >= Tex.Tiles0 && t < Tex.PaintedPlanks0) { FloorTiles(p, Dye(t - Tex.Tiles0)); return 2f; }
            if (t >= Tex.PaintedPlanks0 && t < Tex.DoorBirchLower)
            {
                Planks(p, Color.Lerp(Dye(Blocks.DyeIndex(Blocks.PaintedPlanks[t - Tex.PaintedPlanks0])), Hex(0x8a7a66), 0.18f), 4); return 2f;
            }
            switch (t)
            {
                case Tex.DoorBirchLower: Door(p, false, Hex(0xd8c49a), Hex(0x9a8458)); return 2.5f;
                case Tex.DoorBirchUpper: Door(p, true, Hex(0xd8c49a), Hex(0x9a8458)); return 2.5f;
                case Tex.DoorPineLower: Door(p, false, Hex(0x8a5c38), Hex(0x4a2e1c)); return 2.5f;
                case Tex.DoorPineUpper: Door(p, true, Hex(0x8a5c38), Hex(0x4a2e1c)); return 2.5f;
                case Tex.DoorDarkLower: Door(p, false, Hex(0x4a3524), Hex(0x24180e)); return 2.5f;
                case Tex.DoorDarkUpper: Door(p, true, Hex(0x4a3524), Hex(0x24180e)); return 2.5f;
                case Tex.CabinetFront: Planks(p, Hex(0xa27a48), 1); CabinetDoors(p); return 2.5f;
                case Tex.StoveFront: Metal(p, Hex(0x3e4046), Hex(0x24252a)); Frame(p, Hex(0x1e1f23), 2); StoveFront(p); return 3f;
                case Tex.StoveTop: Metal(p, Hex(0x3e4046), Hex(0x24252a)); StoveTop(p); return 3f;
                case Tex.FridgeFront: Metal(p, Hex(0xe2e4e8), Hex(0xb4b8be)); FridgeFront(p); return 2f;
                case Tex.TvFront: Screen(p); return 1.5f;
                case Tex.PianoFront: Planks(p, Hex(0x2a1e16), 1); Keys(p); return 2f;
                case Tex.Picture0: Picture(p, 0); return 1.5f;
                case Tex.Picture1: Picture(p, 1); return 1.5f;
                case Tex.Picture2: Picture(p, 2); return 1.5f;
                case Tex.ClockFace: Clock(p); return 1.5f;
                case Tex.MirrorGlass: Mirror(p); return 1.5f;
                case Tex.Porcelain: PlasterTex(p, Hex(0xf4f4f0), Hex(0xe2e4e6)); return 0.3f;
                case Tex.Candle: CandleTex(p); return 0.5f;
                case Tex.LampShade: LampShade(p); return 1f;
                case Tex.CrateSide: Planks(p, Hex(0xb08a58), 4); CrateBraces(p); return 3f;
                case Tex.PottedYellow: PottedPlant(p); Bloom(p, Hex(0xf2cc3a), Hex(0xb06a1c)); return 1f;
                case Tex.PottedBlue: PottedPlant(p); Bloom(p, Hex(0x5a8ae8), Hex(0xf0e8a0)); return 1f;
            }
            PlasterTex(p, Hex(0xe9e4d6), Hex(0xd6cfbd)); return 1f;
        }

        // Four square tiles to a block with dark grout, every other one a shade lighter.
        static void FloorTiles(Paint p, Color col)
        {
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    int tx = x / 32, ty = y / 32; int ex = Mathf.Min(x % 32, 31 - x % 32), ey = Mathf.Min(y % 32, 31 - y % 32), edge = Mathf.Min(ex, ey);
                    float n = p.Fbm(x, y, 8, 3);
                    Color c; float h;
                    if (edge < 1) { c = Paint.Mul(col, 0.42f); h = 0; }
                    else
                    {
                        float bevel = Mathf.Clamp01(edge / 3f);
                        c = Paint.Mul(Color.Lerp(col, Color.white, (tx + ty) % 2 == 0 ? 0.16f : 0.02f), (0.9f + n * 0.14f) * (0.9f + bevel * 0.1f));
                        // A soft glaze highlight toward one corner.
                        float gl = Mathf.Clamp01(1 - Vector2.Distance(new Vector2(x % 32, y % 32), new Vector2(9, 23)) / 9f);
                        c = Color.Lerp(c, Color.white, gl * 0.18f);
                        h = 0.5f + bevel * 0.4f;
                    }
                    c.a = 1; p.Set(x, y, c, h);
                }
        }

        static void Rect(Paint p, int x0, int y0, int x1, int y1, Color c, float h, float mix = 1)
        {
            for (int y = Mathf.Max(0, y0); y <= Mathf.Min(63, y1); y++)
                for (int x = Mathf.Max(0, x0); x <= Mathf.Min(63, x1); x++)
                { int i = Paint.I(x, y); var o = Color.Lerp(p.C[i], c, mix); o.a = 1; p.C[i] = o; p.H[i] = h; }
        }

        static void Disc(Paint p, float cx, float cy, float r, Color c, float h)
        {
            for (int y = (int)(cy - r - 1); y <= cy + r + 1; y++)
                for (int x = (int)(cx - r - 1); x <= cx + r + 1; x++)
                    if ((uint)x < 64 && (uint)y < 64 && (x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r) { c.a = 1; p.Set(x, y, c, h); }
        }

        // Two panelled doors with round knobs.
        static void CabinetDoors(Paint p)
        {
            Color dark = Hex(0x5e4126);
            Frame(p, dark, 2);
            Rect(p, 31, 2, 32, 61, Paint.Mul(dark, 0.7f), 0.1f);
            foreach (int x0 in new[] { 6, 37 })
            {
                Rect(p, x0, 7, x0 + 20, 8, dark, 0.3f, 0.7f); Rect(p, x0, 55, x0 + 20, 56, dark, 0.3f, 0.7f);
                Rect(p, x0, 7, x0 + 1, 56, dark, 0.3f, 0.7f); Rect(p, x0 + 19, 7, x0 + 20, 56, dark, 0.3f, 0.7f);
            }
            Disc(p, 27, 32, 2.2f, Hex(0xd8b048), 1); Disc(p, 36, 32, 2.2f, Hex(0xd8b048), 1);
        }

        // An oven door with a glowing window, and two dials above.
        static void StoveFront(Paint p)
        {
            Rect(p, 8, 8, 55, 42, Hex(0x2c2d32), 0.7f);
            for (int y = 13; y <= 37; y++)
                for (int x = 13; x <= 50; x++)
                {
                    float f = p.Fbm(x, y, 6, 3), up = (y - 13) / 24f;
                    Color c = Color.Lerp(Hex(0xf2a030), Hex(0x8a2410), Mathf.Clamp01(up * 0.9f + f * 0.4f)); c.a = 1;
                    p.Set(x, y, c, 0.2f);
                }
            Rect(p, 12, 44, 51, 46, Hex(0x9a9ca2), 1);                           // the handle
            Disc(p, 18, 54, 3.5f, Hex(0x9a9ca2), 1); Disc(p, 32, 54, 3.5f, Hex(0x9a9ca2), 1); Disc(p, 46, 54, 3.5f, Hex(0x9a9ca2), 1);
            Rect(p, 18, 54, 18, 57, Hex(0x24252a), 1); Rect(p, 32, 54, 34, 54, Hex(0x24252a), 1); Rect(p, 46, 54, 46, 57, Hex(0x24252a), 1);
        }

        // Four round hobs; one glows.
        static void StoveTop(Paint p)
        {
            int k = 0;
            foreach (var (cx, cy) in new[] { (18, 18), (46, 18), (18, 46), (46, 46) })
            {
                for (int y = cy - 11; y <= cy + 11; y++)
                    for (int x = cx - 11; x <= cx + 11; x++)
                    {
                        float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                        if (d > 10.5f) continue;
                        bool ring = (int)d % 3 == 0;
                        Color c = k == 1 ? (ring ? Hex(0xf07028) : Hex(0x6a2010)) : (ring ? Hex(0x5a5c62) : Hex(0x1c1d20)); c.a = 1;
                        p.Set(x, y, c, ring ? 0.8f : 0.3f);
                    }
                k++;
            }
        }

        // Two doors (a small one on top) with long handles.
        static void FridgeFront(Paint p)
        {
            Frame(p, Hex(0x8a8e96), 1);
            Rect(p, 1, 40, 62, 41, Hex(0x6e727a), 0.1f);
            Rect(p, 51, 46, 53, 58, Hex(0x7a7e86), 1); Rect(p, 51, 10, 53, 35, Hex(0x7a7e86), 1);
            Rect(p, 8, 50, 20, 54, Hex(0x3a6ab0), 0.8f, 0.8f);                    // a little badge
        }

        // A dark frame round a glowing picture of hills under a sky.
        static void Screen(Paint p)
        {
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    bool frame = x < 4 || y < 6 || x > 59 || y > 59;
                    Color c;
                    if (frame) c = Paint.Mul(Hex(0x26272c), 0.9f + p.Fbm(x, y, 4, 2) * 0.2f);
                    else
                    {
                        float hill = 22 + Mathf.Sin(x * 0.17f) * 5 + Mathf.Sin(x * 0.06f + 1) * 6;
                        c = y < hill ? Color.Lerp(Hex(0x2c7a3a), Hex(0x5ab04a), y / hill) : Color.Lerp(Hex(0x8ac8f0), Hex(0x3a7ad0), (y - hill) / 34f);
                        if ((x - 46) * (x - 46) + (y - 48) * (y - 48) < 30) c = Hex(0xfff0a0);
                        if (y % 2 == 0) c = Paint.Mul(c, 0.93f);                 // scan lines
                    }
                    c.a = 1; p.Set(x, y, c, frame ? 0.9f : 0.2f);
                }
            Disc(p, 56, 3, 1.2f, Hex(0x40e060), 1);
        }

        // The keyboard: white keys with black ones between, across the middle.
        static void Keys(Paint p)
        {
            Rect(p, 2, 24, 61, 40, Hex(0xf2efe6), 0.7f);
            for (int x = 2; x <= 61; x += 5) Rect(p, x, 24, x, 40, Hex(0x8a8678), 0.3f);
            for (int k = 0; k < 12; k++) if (k % 7 != 2 && k % 7 != 6) Rect(p, 5 + k * 5, 31, 8 + k * 5, 40, Hex(0x16161a), 0.9f);
            Rect(p, 2, 41, 61, 43, Hex(0x5a1418), 0.6f);                          // the felt strip
            Rect(p, 22, 50, 41, 56, Hex(0xd8b048), 0.8f, 0.6f);                   // the maker's plate
        }

        // A gilded frame round a small landscape: 0 sunset over the sea, 1 a lake under mountains, 2 a forest path.
        static void Picture(Paint p, int which)
        {
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    int edge = Mathf.Min(Mathf.Min(x, 63 - x), Mathf.Min(y, 63 - y));
                    Color c; float h;
                    if (edge < 6)
                    {
                        c = Paint.Mul(Hex(0xc89a3a), 0.75f + (edge == 2 || edge == 3 ? 0.35f : 0) + p.Fbm(x, y, 4, 2) * 0.15f); h = edge == 5 ? 0.2f : 1;
                    }
                    else
                    {
                        float n = p.Fbm(x, y, 8, 3); h = 0.3f + n * 0.1f;
                        if (which == 0)
                        {
                            float sun = Vector2.Distance(new Vector2(x, y), new Vector2(32, 30));
                            if (y < 28) { c = Color.Lerp(Hex(0x2a3a6a), Hex(0xd8703a), y / 28f); if (Mathf.Abs(x - 32) < 5 + (28 - y) * 0.2f && (y % 3 != 0)) c = Color.Lerp(c, Hex(0xffd070), 0.6f); }
                            else c = Color.Lerp(Hex(0xf2b050), Hex(0x6a3a7a), (y - 28) / 30f);
                            if (sun < 7 && y >= 28) c = Hex(0xfff0b0);
                        }
                        else if (which == 1)
                        {
                            float peak = 40 - Mathf.Abs(x - 22) * 0.8f, peak2 = 36 - Mathf.Abs(x - 44) * 0.7f, m = Mathf.Max(peak, peak2);
                            if (y < 22) c = Color.Lerp(Hex(0x2a5a8a), Hex(0x6aa8d8), y / 22f + n * 0.2f);
                            else if (y < m) c = y > m - 5 ? Hex(0xf0f2f6) : Color.Lerp(Hex(0x5a6a7a), Hex(0x8a94a0), n);
                            else c = Color.Lerp(Hex(0xa8d0f0), Hex(0x5a9ae0), (y - 22) / 36f);
                            if (y >= 22 && y < 25) c = Hex(0x3a6a34);
                        }
                        else
                        {
                            c = Color.Lerp(Hex(0x1e4a22), Hex(0x6aa83a), Mathf.Clamp01(y / 58f + n * 0.3f));
                            float path = Mathf.Abs(x - 32 - Mathf.Sin(y * 0.15f) * 4) - (30 - y) * 0.25f;
                            if (y < 30 && path < 2) c = Color.Lerp(Hex(0xb89a6a), Hex(0x8a6a44), n);
                            foreach (int tx in new[] { 13, 24, 42, 52 }) if (Mathf.Abs(x - tx) < 2 && y > 12) c = Hex(0x3a2a1a);
                            if (y > 48 && n > 0.55f) c = Hex(0xf2e08a);            // light through the leaves
                        }
                    }
                    c.a = 1; p.Set(x, y, c, h);
                }
        }

        static void Clock(Paint p)
        {
            for (int i = 0; i < p.C.Length; i++) { p.C[i] = new Color(0.4f, 0.3f, 0.2f, 0); p.H[i] = 0; }
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f));
                    if (d > 30) continue;
                    Color c = d > 25 ? Paint.Mul(Hex(0x6a4a2a), 0.85f + p.Fbm(x, y, 6, 2) * 0.3f) : Color.Lerp(Hex(0xf6f0dc), Hex(0xe0d6b8), d / 25f);
                    c.a = 1; p.Set(x, y, c, d > 25 ? 1 : 0.4f);
                }
            for (int k = 0; k < 12; k++)
            {
                float a = k * Mathf.PI / 6; Disc(p, 31.5f + Mathf.Sin(a) * 21, 31.5f + Mathf.Cos(a) * 21, k % 3 == 0 ? 1.8f : 1f, Hex(0x2a2218), 0.5f);
            }
            for (float t = 0; t < 13; t += 0.5f) Disc(p, 31.5f + t * 0.8f, 31.5f + t * 0.62f, 1.2f, Hex(0x1e1810), 0.7f);   // ten past two
            for (float t = 0; t < 19; t += 0.5f) Disc(p, 31.5f + t * 0.87f, 31.5f + t * 0.5f - t * 0.0f + (t * -0.0f), 0.8f, Hex(0x1e1810), 0.7f);
            for (float t = 0; t < 19; t += 0.5f) Disc(p, 31.5f - t * 0.5f, 31.5f + t * 0.87f, 0.8f, Hex(0x1e1810), 0.7f);
            Disc(p, 31.5f, 31.5f, 2, Hex(0xc89a3a), 1);
        }

        // Silvered glass in a wooden frame, with a soft reflection of sky and floor.
        static void Mirror(Paint p)
        {
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    bool frame = x < 5 || y < 4 || x > 58 || y > 59;
                    Color c;
                    if (frame) c = Paint.Mul(Hex(0x7a5636), 0.8f + p.Streak(x, y, 2, 12, 2) * 0.35f);
                    else
                    {
                        c = Color.Lerp(Hex(0x9ab0bc), Hex(0xdcecf4), y / 60f + p.Fbm(x, y, 16, 2) * 0.15f);
                        float g = Mathf.Abs((x - y) + 8), g2 = Mathf.Abs((x - y) - 12);
                        if (g < 3 || g2 < 1.5f) c = Color.Lerp(c, Color.white, 0.55f);
                    }
                    c.a = 1; p.Set(x, y, c, frame ? 1 : 0.1f);
                }
        }

        // Wax with a darker wick end at the top and a drip or two.
        static void CandleTex(Paint p)
        {
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    float n = p.Fbm(x, y, 6, 2);
                    Color c = Color.Lerp(Hex(0xf4ead0), Hex(0xd8c8a0), n * 0.6f + Mathf.Abs(x - 32) / 80f);
                    if (y > 54) c = Color.Lerp(Hex(0xffd060), Hex(0xff8a20), (y - 54) / 10f);       // the flame end
                    c.a = 1; p.Set(x, y, c, 0.4f);
                }
        }

        // Warm cloth lit from inside, with a trim top and bottom.
        static void LampShade(Paint p)
        {
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    float weave = (x % 4 == 0 ? -0.05f : 0) + (y % 4 == 0 ? -0.04f : 0), n = p.Fbm(x, y, 8, 2);
                    float glow = 1 - Mathf.Abs(y - 30) / 44f;
                    Color c = Paint.Mul(Color.Lerp(Hex(0xe8b860), Hex(0xfff0c0), glow * 0.8f + n * 0.15f), 1 + weave);
                    if (y < 4 || y > 59) c = Hex(0x8a5a2a);
                    c.a = 1; p.Set(x, y, c, 0.4f + n * 0.2f);
                }
        }

        // A frame round the boards and a brace from corner to corner.
        static void CrateBraces(Paint p)
        {
            Color dark = Hex(0x6a4a2a);
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    bool frame = x < 6 || y < 6 || x > 57 || y > 57, brace = Mathf.Abs(x - y) < 4;
                    if (!frame && !brace) continue;
                    int i = Paint.I(x, y); var c = Paint.Mul(dark, 0.85f + p.Streak(x, y, 3, 14, 2) * 0.35f); c.a = 1; p.C[i] = c; p.H[i] = 1;
                    if ((x == 3 || x == 60) && (y == 3 || y == 60)) { p.C[i] = Hex(0x2a2a30); }
                }
        }

        // Repaint the potted plant's flower in another colour.
        static void Bloom(Paint p, Color petal, Color heart)
        {
            for (int y = 46; y < 64; y++)
                for (int x = 20; x < 44; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(32, 55));
                    if (d < 3) { heart.a = 1; p.Set(x, y, heart, 0.7f); }
                    else if (d < 7.5f && Mathf.Abs(Mathf.Sin(Mathf.Atan2(y - 55, x - 32) * 3)) > 0.25f) { var c = Paint.Mul(petal, 0.85f + (d / 7.5f) * 0.25f); c.a = 1; p.Set(x, y, c, 0.6f); }
                }
        }
    }
}
