using Stratum.Core;
using UnityEngine;

namespace Stratum.Game
{
    // Paints the icons of items that aren't blocks (sticks, coal, ingots, shards, tools) with antialiased shapes.
    public static class ItemArt
    {
        const int S = 96;

        sealed class Canvas
        {
            public readonly Color[] P = new Color[S * S];
            public void Put(int x, int y, Color c, float a)
            {
                if ((uint)x >= S || (uint)y >= S || a <= 0) return;
                int i = x + y * S;
                var d = P[i];
                float oa = a + d.a * (1 - a);
                if (oa <= 0) return;
                P[i] = new Color((c.r * a + d.r * d.a * (1 - a)) / oa, (c.g * a + d.g * d.a * (1 - a)) / oa, (c.b * a + d.b * d.a * (1 - a)) / oa, oa);
            }

            // Thick line from a to b with a per-point colour function (t along, s across in -1..1).
            public void Line(Vector2 a, Vector2 b, float width, System.Func<float, float, Color> col)
            {
                var ab = b - a; float len = ab.magnitude; var dir = ab / len; var nrm = new Vector2(-dir.y, dir.x);
                int x0 = Mathf.FloorToInt(Mathf.Min(a.x, b.x) - width), x1 = Mathf.CeilToInt(Mathf.Max(a.x, b.x) + width);
                int y0 = Mathf.FloorToInt(Mathf.Min(a.y, b.y) - width), y1 = Mathf.CeilToInt(Mathf.Max(a.y, b.y) + width);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        var p = new Vector2(x + 0.5f, y + 0.5f) - a;
                        float t = Mathf.Clamp(Vector2.Dot(p, dir), 0, len);
                        float d = (p - dir * t).magnitude;
                        float cov = Mathf.Clamp01(width / 2 - d + 0.5f);
                        if (cov <= 0) continue;
                        float s = Vector2.Dot(p, nrm) / (width / 2);
                        Put(x, y, col(t / len, s), cov);
                    }
            }

            // Filled polygon with a colour function of the pixel position.
            public void Poly(Vector2[] pts, System.Func<float, float, Color> col)
            {
                float minX = 1e9f, maxX = -1e9f, minY = 1e9f, maxY = -1e9f;
                foreach (var p in pts) { minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y); }
                for (int y = Mathf.FloorToInt(minY); y <= Mathf.CeilToInt(maxY); y++)
                    for (int x = Mathf.FloorToInt(minX); x <= Mathf.CeilToInt(maxX); x++)
                    {
                        // 4x supersampling for smooth edges.
                        int hit = 0;
                        for (int sy = 0; sy < 2; sy++)
                            for (int sx = 0; sx < 2; sx++)
                                if (Inside(pts, x + 0.25f + sx * 0.5f, y + 0.25f + sy * 0.5f)) hit++;
                        if (hit > 0) Put(x, y, col(x, y), hit / 4f);
                    }
            }

            static bool Inside(Vector2[] p, float x, float y)
            {
                bool c = false;
                for (int i = 0, j = p.Length - 1; i < p.Length; j = i++)
                    if ((p[i].y > y) != (p[j].y > y) && x < (p[j].x - p[i].x) * (y - p[i].y) / (p[j].y - p[i].y) + p[i].x) c = !c;
                return c;
            }

            // Filled circle (or ellipse with ry) with a colour function of the offset from the centre in -1..1.
            public void Disc(Vector2 c, float r, System.Func<float, float, Color> col, float ry = -1)
            {
                if (ry < 0) ry = r;
                for (int y = Mathf.FloorToInt(c.y - ry - 1); y <= Mathf.CeilToInt(c.y + ry + 1); y++)
                    for (int x = Mathf.FloorToInt(c.x - r - 1); x <= Mathf.CeilToInt(c.x + r + 1); x++)
                    {
                        float dx = (x + 0.5f - c.x) / r, dy = (y + 0.5f - c.y) / ry;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float cov = Mathf.Clamp01((1 - d) * Mathf.Min(r, ry) + 0.5f);
                        if (cov > 0) { var cc = col(dx, dy); Put(x, y, cc, cov * cc.a); }
                    }
            }

            public void Outline(Color c)
            {
                var copy = (Color[])P.Clone();
                for (int y = 0; y < S; y++)
                    for (int x = 0; x < S; x++)
                    {
                        if (copy[x + y * S].a > 0.5f) continue;
                        float m = 0;
                        for (int dy = -2; dy <= 2; dy++)
                            for (int dx = -2; dx <= 2; dx++)
                            {
                                int xx = x + dx, yy = y + dy;
                                if ((uint)xx < S && (uint)yy < S && dx * dx + dy * dy <= 5) m = Mathf.Max(m, copy[xx + yy * S].a);
                            }
                        if (m > 0) { var o = c; o.a = m * c.a; var d = P[x + y * S]; P[x + y * S] = Color.Lerp(o, d, d.a); P[x + y * S].a = Mathf.Max(d.a, o.a); }
                    }
            }

            public Texture2D Done()
            {
                Outline(new Color(0.08f, 0.06f, 0.05f, 0.85f));
                var t = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
                t.SetPixels(P); t.Apply();
                return t;
            }
        }

        static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1);

        static Color Mat(string id, float shade)
        {
            Color c = id.StartsWith("wood") ? Hex(0xb08a56) : id.StartsWith("stone") ? Hex(0x8a8b90) :
                      id.StartsWith("copper") ? Hex(0xd88452) : id.StartsWith("iron") ? Hex(0xdcdee4) : Hex(0xf0d050);
            return Color.Lerp(c * 0.55f, Color.Lerp(c, Color.white, 0.35f), shade);
        }

        static Color Handle(float t, float s) => Color.Lerp(Hex(0x4a3018), Hex(0x9a6a3a), 0.5f + 0.5f * Mathf.Cos(s * 1.4f)) * (0.9f + 0.1f * Mathf.Sin(t * 30));

        public static Texture2D Paint(ItemDef item)
        {
            var c = new Canvas();
            string id = item.Id;
            if (item.Tool == Tool.Sword) { Sword(c, id); return c.Done(); }
            if (item.Tool == Tool.Shears) { Shears(c); return c.Done(); }
            if (item.ArmourSlot >= 0) { Armour(c, item); return c.Done(); }
            if (item.Tool != Tool.None)
            {
                var a = new Vector2(18, 16); var b = new Vector2(70, 68);
                c.Line(a, b, 9, (t, s) => { var h = Handle(t, s); h.a = 1; return h; });
                if (item.Tool == Tool.Pickaxe)
                {
                    // A curved head across the end of the handle.
                    var head = new[] { new Vector2(30, 78), new Vector2(46, 82), new Vector2(66, 80), new Vector2(80, 72), new Vector2(86, 58), new Vector2(84, 40),
                                       new Vector2(78, 54), new Vector2(70, 66), new Vector2(56, 72), new Vector2(40, 74) };
                    c.Poly(head, (x, y) => Mat(id, Mathf.Clamp01((y - 40) / 45f + (x - 30) / 120f)));
                }
                else if (item.Tool == Tool.Axe)
                {
                    var head = new[] { new Vector2(56, 70), new Vector2(52, 86), new Vector2(66, 92), new Vector2(84, 84), new Vector2(88, 66), new Vector2(78, 58), new Vector2(68, 60) };
                    c.Poly(head, (x, y) => Mat(id, Mathf.Clamp01((y - 58) / 34f * 0.6f + (x - 52) / 36f * 0.5f)));
                }
                else if (item.Tool == Tool.Hoe)
                {
                    // A flat blade turned down at the end of the handle.
                    var head = new[] { new Vector2(60, 72), new Vector2(64, 86), new Vector2(84, 88), new Vector2(88, 80), new Vector2(84, 62), new Vector2(76, 62), new Vector2(76, 76), new Vector2(68, 74) };
                    c.Poly(head, (x, y) => Mat(id, Mathf.Clamp01((y - 62) / 30f * 0.5f + (x - 60) / 30f * 0.5f)));
                }
                else
                {
                    var blade = new[] { new Vector2(64, 62), new Vector2(60, 76), new Vector2(66, 88), new Vector2(78, 90), new Vector2(88, 82), new Vector2(86, 70), new Vector2(76, 64) };
                    c.Poly(blade, (x, y) => Mat(id, Mathf.Clamp01((y - 62) / 30f * 0.5f + (90 - x) / 30f * 0.4f)));
                }
                return c.Done();
            }
            switch (id)
            {
                case "bucket": case "water_bucket": case "lava_bucket": case "milk_bucket":
                {
                    // A pail seen a little from above: tapered sides, a rim, the handle, and what's in it.
                    var body = new[] { new Vector2(22, 70), new Vector2(74, 70), new Vector2(66, 16), new Vector2(30, 16) };
                    c.Poly(body, (x, y) => { float k = Mathf.Abs(x - 48) / 26f; return Color.Lerp(Hex(0xb8bcc4), Hex(0x6e7280), k * 0.8f + (70 - y) / 200f); });
                    for (int b = 0; b < 2; b++) c.Line(new Vector2(25 + b, 48 - b * 22), new Vector2(71 - b, 48 - b * 22), 3, (t, ss) => Hex(0x5a5e68));
                    c.Line(new Vector2(20, 70), new Vector2(76, 70), 6, (t, ss) => Hex(0xd4d8de));
                    Color fill = id == "water_bucket" ? Hex(0x3a6ad0) : id == "lava_bucket" ? Hex(0xff8a20) : id == "milk_bucket" ? Hex(0xf4f2ea) : Hex(0x2a2c32);
                    var top = new[] { new Vector2(24, 72), new Vector2(72, 72), new Vector2(66, 78), new Vector2(30, 78) };
                    c.Poly(top, (x, y) => id == "bucket" ? Hex(0x34363e) : Color.Lerp(fill, Color.white, Mathf.PerlinNoise(x * 0.2f, y * 0.3f) * 0.3f));
                    for (int i = 0; i < 24; i++)
                    {
                        float a0 = i / 24f * Mathf.PI, a1 = (i + 1) / 24f * Mathf.PI;
                        c.Line(new Vector2(48 - Mathf.Cos(a0) * 28, 72 + Mathf.Sin(a0) * 18), new Vector2(48 - Mathf.Cos(a1) * 28, 72 + Mathf.Sin(a1) * 18), 3, (t, ss) => Hex(0x8a8e98));
                    }
                    break;
                }
                case "oak_door":
                {
                    c.Poly(new[] { new Vector2(28, 6), new Vector2(68, 6), new Vector2(68, 90), new Vector2(28, 90) }, (x, y) =>
                    {
                        bool frame = x < 33 || x > 63 || y < 11 || y > 85 || y > 46 && y < 51;
                        bool glass = y > 56 && y < 80 && x > 37 && x < 59 && Mathf.Abs(x - 48) > 1.5f && Mathf.Abs(y - 68) > 1.5f;
                        if (glass) return new Color(0.75f, 0.86f, 0.92f, 0.85f);
                        float g = Mathf.PerlinNoise(x * 0.5f, y * 0.06f);
                        return frame ? Color.Lerp(Hex(0x5e4126), Hex(0x6e4c2c), g) : Color.Lerp(Hex(0x9a7244), Hex(0xb08a56), g) * ((x - 28) % 10 == 0 ? 0.7f : 1);
                    });
                    c.Disc(new Vector2(60, 46), 2.5f, (x, y) => Hex(0x3a3a40));
                    break;
                }
                case "sign":
                    c.Line(new Vector2(48, 6), new Vector2(48, 48), 8, (t, ss) => Color.Lerp(Hex(0x6e4c2c), Hex(0x8a6038), (ss + 1) / 2));
                    c.Poly(new[] { new Vector2(12, 44), new Vector2(84, 44), new Vector2(84, 88), new Vector2(12, 88) }, (x, y) =>
                    {
                        bool edge = x < 16 || x > 80 || y < 48 || y > 84;
                        float g = Mathf.PerlinNoise(x * 0.06f, y * 0.5f);
                        Color col = edge ? Hex(0x7a5634) : Color.Lerp(Hex(0xa27a48), Hex(0xb88e58), g);
                        // Lines of writing.
                        if (!edge && (y == 74 || y == 64 || y == 54) && x > 22 && x < 74 && Mathf.PerlinNoise(x * 0.4f, y) > 0.3f) col = Hex(0x3a2614);
                        return col;
                    });
                    break;
                case "compass": case "clock":
                {
                    bool clock = id == "clock";
                    c.Disc(new Vector2(48, 48), 38, (x, y) => clock ? Color.Lerp(Hex(0xa8801c), Hex(0xf0cc5a), (y - 10) / 80f) : Color.Lerp(Hex(0x6a6e78), Hex(0xc8ccd4), (y - 10) / 80f), 38);
                    c.Disc(new Vector2(48, 48), 30, (x, y) =>
                    {
                        if (!clock) return Color.Lerp(Hex(0xe8e2d0), Hex(0xfaf6ea), (y - 18) / 60f);
                        // Day above, night below: the dial turns with the time.
                        return y > 48 ? Color.Lerp(Hex(0x6ab0ff), Hex(0xa8d4ff), (y - 48) / 30f) : Color.Lerp(Hex(0x101830), Hex(0x24305a), (y - 18) / 30f);
                    }, 30);
                    if (!clock)
                    {
                        c.Poly(new[] { new Vector2(48, 74), new Vector2(54, 48), new Vector2(42, 48) }, (x, y) => Hex(0xd23a2a));
                        c.Poly(new[] { new Vector2(48, 22), new Vector2(54, 48), new Vector2(42, 48) }, (x, y) => Hex(0x4a4e58));
                    }
                    else { c.Disc(new Vector2(62, 62), 6, (x, y) => Hex(0xffe060), 6); c.Disc(new Vector2(34, 34), 4, (x, y) => Hex(0xe8ecf4), 4); }
                    break;
                }
                case "map":
                    c.Poly(new[] { new Vector2(12, 14), new Vector2(84, 10), new Vector2(86, 84), new Vector2(10, 86) }, (x, y) =>
                    {
                        float land = Mathf.PerlinNoise(x * 0.07f + 3, y * 0.07f + 1);
                        Color col = land > 0.5f ? Color.Lerp(Hex(0x7aa04a), Hex(0x4e7a32), (land - 0.5f) * 3) : Color.Lerp(Hex(0x5a8ad0), Hex(0x8ab4e8), land * 2);
                        if (x < 16 || x > 80 || y < 16 || y > 80) col = Hex(0xd8c8a0);
                        return col;
                    });
                    c.Poly(new[] { new Vector2(46, 52), new Vector2(52, 42), new Vector2(40, 42) }, (x, y) => Hex(0xffffff));
                    break;
                case "flint":
                    c.Poly(new[] { new Vector2(30, 20), new Vector2(66, 16), new Vector2(76, 46), new Vector2(58, 78), new Vector2(26, 62) },
                        (x, y) => Color.Lerp(Hex(0x2a2a30), Hex(0x6a6a74), Mathf.Clamp01((y - 16) / 70f + (x - 30) / 120f)));
                    c.Line(new Vector2(40, 30), new Vector2(60, 64), 2, (t, ss) => Hex(0x8a8a96));
                    break;
                case "fire_striker":
                    // A C-shaped steel striker and a chip of flint.
                    for (float a2 = 0.6f; a2 < 5.7f; a2 += 0.05f) c.Disc(new Vector2(42 + Mathf.Cos(a2) * 22, 52 + Mathf.Sin(a2) * 22), 5, (x, y) => Color.Lerp(Hex(0x6a6e76), Hex(0xc4c8d0), (y - 30) / 44f));
                    c.Poly(new[] { new Vector2(60, 28), new Vector2(84, 20), new Vector2(88, 44), new Vector2(70, 52) }, (x, y) => Color.Lerp(Hex(0x2a2a30), Hex(0x5a5a64), (y - 20) / 32f));
                    c.Disc(new Vector2(64, 60), 4, (x, y) => Hex(0xffb040)); c.Disc(new Vector2(70, 68), 2.5f, (x, y) => Hex(0xffe08a));
                    break;
                case "spark":
                {
                    // A little heap of glittering red dust.
                    var rng = new System.Random(7);
                    for (int i = 0; i < 46; i++)
                    {
                        float a = (float)rng.NextDouble() * 6.283f, r = (float)System.Math.Sqrt(rng.NextDouble()) * 26;
                        var at = new Vector2(48 + Mathf.Cos(a) * r, 34 + Mathf.Sin(a) * r * 0.55f + (26 - r) * 0.5f);
                        float sz = 3 + (float)rng.NextDouble() * 4;
                        Color col = Color.Lerp(Hex(0x8a0e08), Hex(0xff4a2a), (float)rng.NextDouble());
                        c.Disc(at, sz, (x, y) => col);
                    }
                    break;
                }
                case "lever":
                    c.Poly(new[] { new Vector2(22, 30), new Vector2(74, 30), new Vector2(70, 14), new Vector2(26, 14) }, (x, y) => Color.Lerp(Hex(0x5c5d62), Hex(0x9a9ba0), (y - 14) / 16f));
                    c.Line(new Vector2(48, 26), new Vector2(64, 82), 7, (t, ss) => Color.Lerp(Hex(0x6a4a2a), Hex(0xb08a5a), ss));
                    c.Disc(new Vector2(64, 82), 6, (x, y) => Hex(0x8a6a44));
                    break;
                case "button":
                    c.Poly(new[] { new Vector2(28, 62), new Vector2(68, 62), new Vector2(68, 34), new Vector2(28, 34) }, (x, y) => Color.Lerp(Hex(0x6c6d72), Hex(0xa6a7ac), (y - 34) / 28f));
                    c.Line(new Vector2(28, 62), new Vector2(68, 62), 3, (t, ss) => Hex(0xc4c5ca));
                    break;
                case "boat":
                {
                    // A rowing boat side on: a curved hull of planks, the gunwale, an oar resting across.
                    var hull = new[] { new Vector2(6, 62), new Vector2(90, 62), new Vector2(78, 30), new Vector2(60, 22), new Vector2(36, 22), new Vector2(18, 30) };
                    c.Poly(hull, (x, y) => { float plank = Mathf.Repeat(y, 10) < 1.4f ? 0.7f : 1; var k = Color.Lerp(Hex(0x7a5634), Hex(0xb88a56), (y - 22) / 40f) * plank; k.a = 1; return k; });
                    c.Line(new Vector2(6, 62), new Vector2(90, 62), 5, (t, ss) => Hex(0x5a3e24));
                    c.Line(new Vector2(14, 78), new Vector2(84, 50), 4, (t, ss) => Hex(0xc8a070));
                    c.Line(new Vector2(70, 56), new Vector2(84, 50), 9, (t, ss) => Hex(0xa87e50));
                    break;
                }
                case "minecart":
                {
                    var box = new[] { new Vector2(10, 70), new Vector2(86, 70), new Vector2(78, 30), new Vector2(18, 30) };
                    c.Poly(box, (x, y) => { bool rivet = (Mathf.Abs(x - 20) < 2 || Mathf.Abs(x - 76) < 2) && Mathf.Repeat(y, 12) < 2.5f; return rivet ? Hex(0x2e3036) : Color.Lerp(Hex(0x5a5e66), Hex(0x8e939c), (y - 30) / 40f); });
                    c.Line(new Vector2(8, 70), new Vector2(88, 70), 5, (t, ss) => Hex(0xa4a8b0));
                    foreach (float wx in new[] { 28f, 68f }) { c.Disc(new Vector2(wx, 26), 10, (x, y) => Hex(0x26272b)); c.Disc(new Vector2(wx, 26), 4, (x, y) => Hex(0x6a6e76)); }
                    break;
                }
                case "jade":
                    c.Poly(new[] { new Vector2(48, 88), new Vector2(76, 62), new Vector2(66, 18), new Vector2(30, 18), new Vector2(20, 62) },
                        (x, y) => Color.Lerp(Hex(0x147a40), Hex(0x6ae8a4), Mathf.Clamp01((y - 18) / 70f * 0.6f + (76 - x) / 56f * 0.4f)));
                    c.Poly(new[] { new Vector2(48, 78), new Vector2(62, 60), new Vector2(48, 30), new Vector2(34, 60) }, (x, y) => new Color(0.75f, 1f, 0.85f, 0.55f));
                    break;
                case "seeds":
                    for (int i = 0; i < 9; i++)
                    {
                        var at = new Vector2(30 + (i % 3) * 16 + (i / 3 % 2) * 6, 28 + (i / 3) * 16);
                        c.Disc(at, 5.5f, (x, y) => Color.Lerp(Hex(0x4e7a2a), Hex(0xa8c060), Mathf.Clamp01((y - at.y + 5) / 10f)), 7.5f);
                    }
                    break;
                case "wheat":
                    for (int k = -1; k <= 1; k++)
                    {
                        var foot = new Vector2(48 + k * 4, 8); var head = new Vector2(48 + k * 18, 70);
                        c.Line(foot, head, 3, (t, ss) => Hex(0xc8a848));
                        for (int g = 0; g < 7; g++)
                        {
                            var at = Vector2.Lerp(head, head + (head - foot).normalized * 22, g / 6f);
                            c.Disc(at + new Vector2(g % 2 == 0 ? -3 : 3, 0), 4f, (x, y) => Color.Lerp(Hex(0xb08a2e), Hex(0xf0d27a), Mathf.Clamp01((y - at.y + 4) / 8f)), 5.5f);
                        }
                    }
                    break;
                case "bone_meal":
                    c.Disc(new Vector2(48, 40), 30, (x, y) => Color.Lerp(Hex(0xc8c4b4), Hex(0xfaf8f0), Mathf.PerlinNoise(x * 0.3f, y * 0.3f) * 0.6f + (y - 10) / 120f), 20);
                    break;
                case "paper":
                    c.Poly(new[] { new Vector2(22, 12), new Vector2(70, 12), new Vector2(78, 84), new Vector2(28, 86) }, (x, y) =>
                        (y % 12 == 0 && x > 30 && x < 70) ? Hex(0xb8c4d0) : Color.Lerp(Hex(0xe8e4d8), Hex(0xfbfaf4), Mathf.PerlinNoise(x * 0.1f, y * 0.1f)));
                    break;
                case "book":
                    c.Poly(new[] { new Vector2(20, 14), new Vector2(70, 10), new Vector2(76, 82), new Vector2(26, 86) }, (x, y) =>
                    {
                        if (x > 66) return Hex(0xf0ead8);
                        bool spine = x < 30;
                        return Color.Lerp(spine ? Hex(0x5a2a18) : Hex(0x7a3820), Hex(0x9a5030), Mathf.PerlinNoise(x * 0.2f, y * 0.2f) * 0.5f);
                    });
                    c.Line(new Vector2(36, 60), new Vector2(60, 58), 3, (t, ss) => Hex(0xd0a040));
                    break;
                case "stick":
                    c.Line(new Vector2(22, 18), new Vector2(74, 78), 9, (t, s) => { var h = Handle(t, s); h.a = 1; return h; });
                    break;
                case "coal":
                    c.Poly(new[] { new Vector2(22, 40), new Vector2(34, 22), new Vector2(60, 18), new Vector2(76, 32), new Vector2(78, 56), new Vector2(62, 76), new Vector2(36, 74), new Vector2(20, 58) },
                        (x, y) => { float n = Mathf.PerlinNoise(x * 0.15f + 0.3f, y * 0.15f + 0.7f); return Color.Lerp(Hex(0x141418), Hex(0x4a4a54), n * 0.6f + (y - 18) / 120f); });
                    break;
                case "copper_ingot": case "iron_ingot": case "gold_ingot":
                {
                    Color m = id[0] == 'c' ? Hex(0xd88452) : id[0] == 'i' ? Hex(0xd4d6dc) : Hex(0xf2cc48);
                    c.Poly(new[] { new Vector2(14, 34), new Vector2(54, 18), new Vector2(84, 40), new Vector2(84, 52), new Vector2(44, 70), new Vector2(14, 48) }, (x, y) => m * 0.62f);
                    c.Poly(new[] { new Vector2(20, 46), new Vector2(54, 32), new Vector2(80, 50), new Vector2(46, 66) }, (x, y) => Color.Lerp(m, Color.white, 0.25f + (y - 32) / 120f));
                    c.Poly(new[] { new Vector2(14, 34), new Vector2(54, 18), new Vector2(54, 32), new Vector2(20, 46) }, (x, y) => m * 0.8f);
                    break;
                }
                case "sunstone_shard":
                    c.Poly(new[] { new Vector2(48, 88), new Vector2(66, 62), new Vector2(60, 16), new Vector2(40, 10), new Vector2(30, 56) },
                        (x, y) => Color.Lerp(Hex(0xff8a20), Hex(0xfff2b0), Mathf.Clamp01((x - 30) / 40f * 0.5f + (y - 10) / 80f * 0.6f)));
                    c.Poly(new[] { new Vector2(48, 88), new Vector2(40, 10), new Vector2(30, 56) }, (x, y) => Hex(0xe86a10));
                    break;
                case "bow": Bow(c); break;
                case "arrow": Arrow(c); break;
                case "apple": Apple(c); break;
                case "bread": Bread(c); break;
                case "raw_beef": Steak(c, false, Hex(0xc0303a)); break;
                case "steak": Steak(c, true, Hex(0x8a4a24)); break;
                case "raw_pork": Steak(c, false, Hex(0xe88a8a)); break;
                case "roast_pork": Steak(c, true, Hex(0xa86034)); break;
                case "raw_mutton": Steak(c, false, Hex(0xb8404a)); break;
                case "roast_mutton": Steak(c, true, Hex(0x7a4a2a)); break;
                case "raw_chicken": Drumstick(c, Hex(0xf0c0a8)); break;
                case "roast_chicken": Drumstick(c, Hex(0xc07a34)); break;
                case "berries": Berries(c); break;
                case "hide": Hide(c); break;
                case "feather": Feather(c); break;
                case "bone": Bone(c); break;
                case "thread": Thread(c); break;
                case "ember":
                    c.Poly(new[] { new Vector2(48, 90), new Vector2(70, 58), new Vector2(62, 14), new Vector2(36, 10), new Vector2(26, 50) },
                        (x, y) => Color.Lerp(Hex(0xb81e08), Hex(0xffb040), Mathf.Clamp01((x - 26) / 44f * 0.5f + (y - 10) / 80f * 0.6f)));
                    c.Poly(new[] { new Vector2(48, 90), new Vector2(36, 10), new Vector2(26, 50) }, (x, y) => Hex(0x8a1206));
                    break;
                case "colossus_heart":
                    c.Disc(new Vector2(36, 56), 20, (dx, dy) => Color.Lerp(Hex(0xff6a1a), Hex(0x7a0c04), Mathf.Clamp01((dx + 1) * 0.3f - dy * 0.3f + 0.2f)));
                    c.Disc(new Vector2(60, 56), 20, (dx, dy) => Color.Lerp(Hex(0xff8a2a), Hex(0x7a0c04), Mathf.Clamp01((dx + 1) * 0.35f - dy * 0.3f)));
                    c.Poly(new[] { new Vector2(17, 52), new Vector2(79, 52), new Vector2(48, 12) }, (x, y) => Color.Lerp(Hex(0x9a1406), Hex(0xff7a20), (y - 12) / 50f));
                    c.Disc(new Vector2(40, 62), 6, (dx, dy) => new Color(1, 0.95f, 0.7f, 1 - Mathf.Sqrt(dx * dx + dy * dy)));
                    break;
                default:
                    c.Poly(new[] { new Vector2(20, 20), new Vector2(76, 20), new Vector2(76, 76), new Vector2(20, 76) }, (x, y) => Color.magenta);
                    break;
            }
            return c.Done();
        }

        static Color Shade(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);

        static void Sword(Canvas c, string id)
        {
            // Blade from the guard to the tip, bright along the edge and darker down the fuller.
            var a = new Vector2(34, 34); var tip = new Vector2(84, 84);
            c.Line(a, tip - new Vector2(6, 6), 13, (t, s) => { var m = Mat(id, 0.75f - Mathf.Abs(s) * 0.15f + (s > 0.5f ? 0.25f : 0)); m.a = 1; return m; });
            c.Poly(new[] { tip - new Vector2(10.6f, 1.4f), tip, tip - new Vector2(1.4f, 10.6f) }, (x, y) => Mat(id, 0.85f));
            c.Line(a + new Vector2(2, 2), tip - new Vector2(10, 10), 2.5f, (t, s) => Mat(id, 0.35f));
            c.Line(new Vector2(22, 44), new Vector2(44, 22), 7, (t, s) => Color.Lerp(Hex(0x4a3a2a), Hex(0x8a6a3a), 0.5f + s * 0.3f));
            c.Line(new Vector2(14, 14), new Vector2(32, 32), 7.5f, (t, s) => { var h = Handle(t, s); h.a = 1; return h; });
            c.Disc(new Vector2(13, 13), 5, (dx, dy) => Color.Lerp(Hex(0x9a7a3a), Hex(0x4a3418), (dx - dy + 1.4f) / 2.8f));
        }

        static void Shears(Canvas c)
        {
            var steel = Hex(0xc8ccd4);
            c.Poly(new[] { new Vector2(44, 46), new Vector2(84, 86), new Vector2(76, 70), new Vector2(50, 40) }, (x, y) => Color.Lerp(steel, Color.white, (y - 40) / 90f));
            c.Poly(new[] { new Vector2(46, 44), new Vector2(86, 84), new Vector2(70, 76), new Vector2(40, 50) }, (x, y) => Color.Lerp(Shade(steel, 0.75f), steel, (x - 40) / 60f));
            foreach (var ctr in new[] { new Vector2(24, 40), new Vector2(40, 24) })
            {
                c.Disc(ctr, 12, (dx, dy) => dx * dx + dy * dy < 0.3f ? new Color(0, 0, 0, 0) : Color.Lerp(Hex(0xb04020), Hex(0x702010), (dx + dy + 2) / 4));
            }
            c.Disc(new Vector2(46, 46), 3.5f, (dx, dy) => Hex(0x3a3a40));
        }

        static void Bow(Canvas c)
        {
            Vector2 a = new Vector2(16, 82), b = new Vector2(82, 16), ctl = new Vector2(84, 84);
            Vector2 prev = a;
            for (int i = 1; i <= 24; i++)
            {
                float t = i / 24f;
                var pt = (1 - t) * (1 - t) * a + 2 * (1 - t) * t * ctl + t * t * b;
                float w = 6.5f + 2.5f * Mathf.Sin(t * Mathf.PI);
                c.Line(prev, pt, w, (tt, s) => Color.Lerp(Hex(0x5a3a1e), Hex(0xa87440), 0.5f + s * 0.4f));
                prev = pt;
            }
            c.Line(new Vector2(48, 48), new Vector2(58, 58), 9, (t, s) => Hex(0x3a2a1a));       // grip
            c.Line(a, b, 1.6f, (t, s) => new Color(0.92f, 0.9f, 0.84f, 1));
        }

        static void Arrow(Canvas c)
        {
            c.Line(new Vector2(18, 18), new Vector2(72, 72), 4.5f, (t, s) => Color.Lerp(Hex(0x7a5a34), Hex(0xb08a56), 0.5f + s * 0.4f));
            c.Poly(new[] { new Vector2(64, 76), new Vector2(86, 86), new Vector2(76, 64) }, (x, y) => Color.Lerp(Hex(0x6a6c72), Hex(0xc8ccd4), (x + y - 128) / 44f));
            foreach (var off in new[] { new Vector2(-9, 3), new Vector2(3, -9) })
                c.Poly(new[] { new Vector2(14, 14), new Vector2(30, 30), new Vector2(30, 30) + off * 1.4f, new Vector2(14, 14) + off * 1.6f },
                    (x, y) => Color.Lerp(Hex(0xe8e4dc), Hex(0xb8b0a4), (x - 6) / 30f));
        }

        static void Apple(Canvas c)
        {
            c.Disc(new Vector2(48, 42), 30, (dx, dy) => Color.Lerp(Hex(0xe23a2a), Hex(0x7a1410), Mathf.Clamp01(0.35f + dx * 0.35f - dy * 0.35f)), 28);
            c.Disc(new Vector2(36, 54), 8, (dx, dy) => new Color(1, 0.85f, 0.8f, 0.55f * (1 - Mathf.Sqrt(dx * dx + dy * dy))));
            c.Line(new Vector2(48, 66), new Vector2(52, 82), 4.5f, (t, s) => Hex(0x5a3a1e));
            c.Poly(new[] { new Vector2(52, 76), new Vector2(64, 86), new Vector2(76, 82), new Vector2(62, 74) }, (x, y) => Color.Lerp(Hex(0x3a8a2a), Hex(0x6ab03a), (y - 74) / 12f));
        }

        static void Bread(Canvas c)
        {
            c.Disc(new Vector2(48, 44), 38, (dx, dy) => Color.Lerp(Hex(0xe0a050), Hex(0x8a5420), Mathf.Clamp01(0.3f - dy * 0.4f + dx * 0.2f)), 22);
            for (int k = -1; k <= 1; k++)
                c.Line(new Vector2(40 + k * 20, 34), new Vector2(54 + k * 20, 56), 3, (t, s) => Hex(0xf0d090));
        }

        static void Steak(Canvas c, bool cooked, Color meat)
        {
            var pts = new[] { new Vector2(16, 44), new Vector2(26, 24), new Vector2(50, 16), new Vector2(76, 22), new Vector2(86, 42), new Vector2(80, 64), new Vector2(58, 78), new Vector2(34, 76), new Vector2(20, 62) };
            c.Poly(pts, (x, y) => Color.Lerp(Shade(meat, 0.75f), meat, Mathf.Clamp01((y - 16) / 70f + Mathf.PerlinNoise(x * 0.1f, y * 0.1f) * 0.3f)));
            // A rim of fat and, when cooked, grill marks.
            c.Line(new Vector2(22, 60), new Vector2(36, 74), 6, (t, s) => cooked ? Hex(0xc89060) : Hex(0xf4e4d4));
            c.Line(new Vector2(36, 74), new Vector2(58, 76), 6, (t, s) => cooked ? Hex(0xc89060) : Hex(0xf4e4d4));
            if (cooked) for (int k = 0; k < 3; k++) c.Line(new Vector2(28 + k * 16, 26), new Vector2(46 + k * 16, 62), 3.5f, (t, s) => Hex(0x3a1c0c));
            else c.Disc(new Vector2(56, 46), 6, (dx, dy) => Hex(0xf4e4d4));
        }

        static void Drumstick(Canvas c, Color meat)
        {
            c.Line(new Vector2(18, 18), new Vector2(42, 42), 7, (t, s) => Hex(0xf0e8d8));
            c.Disc(new Vector2(16, 20), 5, (dx, dy) => Hex(0xf0e8d8));
            c.Disc(new Vector2(20, 16), 5, (dx, dy) => Hex(0xe8dcc8));
            c.Disc(new Vector2(60, 60), 26, (dx, dy) => Color.Lerp(meat, Shade(meat, 0.65f), Mathf.Clamp01(0.4f + dx * 0.3f - dy * 0.3f)), 22);
        }

        static void Berries(Canvas c)
        {
            c.Poly(new[] { new Vector2(40, 70), new Vector2(60, 88), new Vector2(84, 82), new Vector2(62, 66) }, (x, y) => Color.Lerp(Hex(0x2e6a22), Hex(0x5aa034), (y - 66) / 22f));
            foreach (var (bx, by) in new[] { (30, 36), (50, 30), (42, 52), (64, 46), (24, 56), (56, 62) })
                c.Disc(new Vector2(bx, by), 11, (dx, dy) => Color.Lerp(Hex(0xd02a4a), Hex(0x5a0a20), Mathf.Clamp01(0.4f + dx * 0.3f - dy * 0.35f)));
        }

        static void Hide(Canvas c)
        {
            var pts = new[] { new Vector2(20, 30), new Vector2(14, 50), new Vector2(24, 64), new Vector2(18, 80), new Vector2(40, 74), new Vector2(56, 82), new Vector2(76, 76), new Vector2(72, 60), new Vector2(84, 44), new Vector2(76, 22), new Vector2(54, 26), new Vector2(36, 14) };
            c.Poly(pts, (x, y) => Color.Lerp(Hex(0x6a4428), Hex(0xa87448), Mathf.PerlinNoise(x * 0.08f + 3, y * 0.08f) * 0.8f + (y - 14) / 300f));
            c.Disc(new Vector2(46, 48), 9, (dx, dy) => new Color(0.95f, 0.88f, 0.76f, 0.5f));
        }

        static void Feather(Canvas c)
        {
            c.Line(new Vector2(16, 14), new Vector2(76, 82), 2.5f, (t, s) => Hex(0xc8c0b0));
            var vane = new[] { new Vector2(30, 26), new Vector2(26, 50), new Vector2(46, 74), new Vector2(76, 84), new Vector2(66, 60), new Vector2(54, 36), new Vector2(36, 24) };
            c.Poly(vane, (x, y) => Color.Lerp(Hex(0xf6f2ea), Hex(0xc8c0b4), Mathf.Abs(Mathf.Sin((x + y) * 0.35f)) * 0.4f + (x - y + 30) / 160f));
            c.Line(new Vector2(28, 28), new Vector2(74, 80), 1.5f, (t, s) => Hex(0xb0a898));
        }

        static void Bone(Canvas c)
        {
            var bone = Hex(0xf0e8d6);
            c.Line(new Vector2(26, 26), new Vector2(70, 70), 10, (t, s) => Color.Lerp(bone, Shade(bone, 0.78f), 0.5f + s * 0.4f));
            foreach (var ctr in new[] { new Vector2(20, 28), new Vector2(28, 20), new Vector2(68, 76), new Vector2(76, 68) })
                c.Disc(ctr, 8, (dx, dy) => Color.Lerp(bone, Shade(bone, 0.75f), Mathf.Clamp01(0.4f + dx * 0.3f - dy * 0.3f)));
        }

        static void Thread(Canvas c)
        {
            for (int k = 0; k < 4; k++)
            {
                float r = 30 - k * 5;
                Vector2 prev = Vector2.zero;
                for (int i = 0; i <= 40; i++)
                {
                    float a = i / 40f * Mathf.PI * 2;
                    var pt = new Vector2(48 + Mathf.Cos(a) * r, 48 + Mathf.Sin(a) * r * 0.7f + k * 3);
                    if (i > 0) c.Line(prev, pt, 2.6f, (t, s) => new Color(0.95f, 0.94f, 0.9f, 1) * (0.85f + 0.15f * s));
                    prev = pt;
                }
            }
        }

        static void Armour(Canvas c, ItemDef item)
        {
            string mat = item.Id.Substring(0, item.Id.IndexOf('_'));
            Color m = mat == "hide" ? Hex(0x8a5a34) : mat == "copper" ? Hex(0xd08050) : Hex(0xccd0d6);
            System.Func<float, float, Color> fill = (x, y) => Color.Lerp(Shade(m, 0.65f), Color.Lerp(m, Color.white, 0.2f), Mathf.Clamp01((y - 10) / 90f + Mathf.PerlinNoise(x * 0.07f, y * 0.07f) * 0.25f));
            switch (item.ArmourSlot)
            {
                case 0:
                    c.Disc(new Vector2(48, 42), 32, (dx, dy) => dy < 0 ? new Color(0, 0, 0, 0) : fill(48 + dx * 30, 42 + dy * 30), 30);
                    c.Poly(new[] { new Vector2(12, 40), new Vector2(84, 40), new Vector2(84, 32), new Vector2(12, 32) }, (x, y) => Shade(m, 0.7f));
                    break;
                case 1:
                    c.Poly(new[] { new Vector2(30, 12), new Vector2(66, 12), new Vector2(68, 60), new Vector2(84, 52), new Vector2(90, 70), new Vector2(64, 86), new Vector2(54, 80),
                                   new Vector2(42, 80), new Vector2(32, 86), new Vector2(6, 70), new Vector2(12, 52), new Vector2(28, 60) }, fill);
                    c.Line(new Vector2(48, 16), new Vector2(48, 78), 2.5f, (t, s) => Shade(m, 0.55f));
                    break;
                case 2:
                    c.Poly(new[] { new Vector2(24, 84), new Vector2(72, 84), new Vector2(72, 76), new Vector2(70, 12), new Vector2(54, 12), new Vector2(48, 60), new Vector2(42, 12), new Vector2(26, 12), new Vector2(24, 76) }, fill);
                    c.Poly(new[] { new Vector2(24, 84), new Vector2(72, 84), new Vector2(72, 76), new Vector2(24, 76) }, (x, y) => Shade(m, 0.6f));
                    break;
                default:
                    foreach (int ox in new[] { 0, 40 })
                        c.Poly(new[] { new Vector2(10 + ox, 20), new Vector2(46 + ox, 20), new Vector2(46 + ox, 30), new Vector2(30 + ox, 34), new Vector2(30 + ox, 70), new Vector2(12 + ox, 70) }, fill);
                    break;
            }
        }
    }
}
