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
                else
                {
                    var blade = new[] { new Vector2(64, 62), new Vector2(60, 76), new Vector2(66, 88), new Vector2(78, 90), new Vector2(88, 82), new Vector2(86, 70), new Vector2(76, 64) };
                    c.Poly(blade, (x, y) => Mat(id, Mathf.Clamp01((y - 62) / 30f * 0.5f + (90 - x) / 30f * 0.4f)));
                }
                return c.Done();
            }
            switch (id)
            {
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
                default:
                    c.Poly(new[] { new Vector2(20, 20), new Vector2(76, 20), new Vector2(76, 76), new Vector2(20, 76) }, (x, y) => Color.magenta);
                    break;
            }
            return c.Done();
        }
    }
}
