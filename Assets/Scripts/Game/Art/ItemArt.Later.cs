using Stratum.Core;
using UnityEngine;

namespace Stratum.Game
{
    // Pictures for the things added from 0.14 on. Each is drawn on a 96 x 96 canvas, y upward.
    public static partial class ItemArt
    {
        static Vector2 V(float x, float y) => new Vector2(x, y);

        static void Fish(Canvas c, Color back, Color belly)
        {
            c.Poly(new[] { V(8, 30), V(26, 48), V(8, 68) }, (x, y) => Shade(back, 0.8f + (y - 30) / 120f));              // tail
            c.Disc(V(52, 49), 32, (dx, dy) => Color.Lerp(belly, back, Mathf.Clamp01(dy * 0.9f + 0.45f)), 17);             // body
            c.Poly(new[] { V(44, 64), V(62, 78), V(66, 63) }, (x, y) => Shade(back, 0.75f));                              // fin
            c.Disc(V(71, 53), 3.4f, (dx, dy) => Hex(0x14100c));
            c.Line(V(60, 38), V(60, 58), 1.6f, (t, ss) => Shade(back, 0.65f));                                            // gill
        }

        static void Root(Canvas c, Color skin, Color dark, bool split)
        {
            c.Disc(V(48, 46), 31, (dx, dy) => Color.Lerp(skin, dark, Mathf.Clamp01(-dy * 0.5f + dx * 0.25f + 0.35f)), 22);
            foreach (var (x, y) in new[] { (34f, 52f), (56f, 40f), (62f, 54f), (42f, 36f) }) c.Disc(V(x, y), 2.4f, (dx, dy) => Shade(dark, 0.7f));
            if (split)
            {
                c.Poly(new[] { V(30, 52), V(48, 62), V(66, 52), V(48, 44) }, (x, y) => Hex(0xf6e6b0));
                c.Disc(V(48, 53), 4, (dx, dy) => Hex(0xf2d24a));                                                          // a knob of butter
            }
        }

        // A round-bellied bottle with a cork; a potion fills it with its colour.
        static void Bottle(Canvas c, Color liquid, bool full)
        {
            Color glass = new Color(0.82f, 0.93f, 0.97f, 0.55f), rim = new Color(0.55f, 0.72f, 0.8f, 1);
            c.Disc(V(48, 36), 27, (dx, dy) => Mathf.Sqrt(dx * dx + dy * dy) > 0.86f ? rim : full ? (dy > 0.55f ? glass : Color.Lerp(Shade(liquid, 0.75f), Color.Lerp(liquid, Color.white, 0.25f), Mathf.Clamp01(dx * 0.5f + dy * 0.5f + 0.5f))) : glass);
            c.Poly(new[] { V(39, 58), V(57, 58), V(57, 78), V(39, 78) }, (x, y) => x < 41.5f || x > 54.5f ? rim : glass);
            c.Poly(new[] { V(37, 78), V(59, 78), V(59, 82), V(37, 82) }, (x, y) => rim);
            c.Poly(new[] { V(41, 82), V(55, 82), V(54, 91), V(42, 91) }, (x, y) => Color.Lerp(Hex(0x9a6a3a), Hex(0xc89a5c), (x - 41) / 14f));
            c.Disc(V(38, 44), 5, (dx, dy) => new Color(1, 1, 1, 0.75f), 8);
        }

        // Things that place a block but are shown as themselves (a carrot, not a carrot plant).
        public static bool Painted(string id) => id == "carrot" || id == "potato";

        // Returns true if this file drew the item.
        static bool Later(Canvas c, string id)
        {
            switch (id)
            {
                case "blast_powder":
                {
                    var rng = new System.Random(11);
                    for (int i = 0; i < 60; i++)
                    {
                        float a = (float)rng.NextDouble() * 6.283f, r = (float)System.Math.Sqrt(rng.NextDouble()) * 28;
                        var at = V(48 + Mathf.Cos(a) * r, 32 + Mathf.Sin(a) * r * 0.5f + (28 - r) * 0.55f);
                        float k = 0.25f + (float)rng.NextDouble() * 0.3f;
                        Color col = i % 9 == 0 ? Hex(0xe8742a) : new Color(k, k, k * 1.05f, 1);
                        c.Disc(at, 3.2f, (dx, dy) => col);
                    }
                    return true;
                }
                case "fishing_rod":
                    c.Line(V(14, 12), V(80, 84), 5, (t, ss) => { var h = Handle(t, ss); h.a = 1; return h; });
                    c.Line(V(80, 84), V(78, 34), 1.2f, (t, ss) => Hex(0xe8e8e8));
                    c.Disc(V(78, 30), 6, (dx, dy) => dy > 0 ? Hex(0xd83a2a) : Hex(0xf2f2ee));
                    c.Line(V(20, 18), V(30, 14), 4, (t, ss) => Hex(0x4a4c52));                                            // reel
                    return true;
                case "bottle": Bottle(c, new Color(0.8f, 0.92f, 0.96f, 0.35f), false); return true;
                case "potion_swift": Bottle(c, Hex(0x7fd8f0), true); return true;
                case "potion_leap": Bottle(c, Hex(0x8ee05a), true); return true;
                case "potion_strength": Bottle(c, Hex(0xb8322a), true); return true;
                case "potion_regen": Bottle(c, Hex(0xf06a9a), true); return true;
                case "potion_heal": Bottle(c, Hex(0xf22a4a), true); return true;
                case "potion_fire": Bottle(c, Hex(0xf59a2a), true); return true;
                case "potion_gills": Bottle(c, Hex(0x3a6ae0), true); return true;
                case "potion_night": Bottle(c, Hex(0x3a3a9a), true); return true;
                case "potion_feather": Bottle(c, Hex(0xe8e8f4), true); return true;
                case "potion_haste": Bottle(c, Hex(0xf2d24a), true); return true;
                case "starstone":
                    // A cut stone: pale blue facets round a bright table.
                    c.Poly(new[] { V(48, 88), V(80, 62), V(72, 22), V(48, 8), V(24, 22), V(16, 62) }, (x, y) => Color.Lerp(Hex(0x2f9ab8), Hex(0xa8f6f8), Mathf.Clamp01((x * 0.4f + y * 0.9f - 20) / 100f)));
                    c.Poly(new[] { V(48, 70), V(64, 56), V(60, 34), V(48, 26), V(36, 34), V(32, 56) }, (x, y) => Color.Lerp(Hex(0xd8feff), Hex(0x7fe4ee), Mathf.Clamp01((y - 26) / 44f)));
                    c.Line(V(48, 88), V(48, 70), 1.4f, (t, ss) => Hex(0x1f7a98)); c.Line(V(80, 62), V(64, 56), 1.4f, (t, ss) => Hex(0x1f7a98));
                    c.Line(V(16, 62), V(32, 56), 1.4f, (t, ss) => Hex(0x1f7a98)); c.Line(V(48, 8), V(48, 26), 1.4f, (t, ss) => Hex(0x1f7a98));
                    return true;
                case "shield":
                    // A round-topped board with an iron rim and boss.
                    c.Poly(new[] { V(18, 84), V(78, 84), V(80, 46), V(66, 20), V(48, 8), V(30, 20), V(16, 46) }, (x, y) => Hex(0x55585e));
                    c.Poly(new[] { V(23, 79), V(73, 79), V(75, 47), V(63, 24), V(48, 14), V(33, 24), V(21, 47) }, (x, y) =>
                    {
                        float g = Mathf.PerlinNoise(x * 0.5f, y * 0.05f);
                        Color wood = Color.Lerp(Hex(0x8a5a30), Hex(0xb07c44), g);
                        return Mathf.Abs(x - 48) < 5 ? Hex(0xb8322a) : (int)(x / 8.5f) % 2 == 0 ? wood : Shade(wood, 0.9f);
                    });
                    c.Disc(V(48, 50), 9, (dx, dy) => Color.Lerp(Hex(0xe2e4e8), Hex(0x7a7c84), Mathf.Clamp01(dx * 0.5f - dy * 0.5f + 0.5f)));
                    return true;
                case "raw_fish": Fish(c, Hex(0x5a8ab0), Hex(0xd8e4ea)); return true;
                case "cooked_fish": Fish(c, Hex(0x9a5a2a), Hex(0xe0b070)); return true;
                case "egg":
                    c.Disc(V(48, 46), 24, (dx, dy) => Color.Lerp(Hex(0xfaf2de), Hex(0xcdb894), Mathf.Clamp01(dx * 0.4f - dy * 0.4f + 0.3f)), 31);
                    return true;
                case "fried_egg":
                    c.Disc(V(48, 44), 36, (dx, dy) => Color.Lerp(Hex(0xffffff), Hex(0xe2dccc), Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy) - 0.3f)), 26);
                    c.Disc(V(30, 62), 14, (dx, dy) => Hex(0xf8f6ee), 10);
                    c.Disc(V(52, 46), 12, (dx, dy) => Color.Lerp(Hex(0xffd84a), Hex(0xe89a1a), Mathf.Clamp01(dx * 0.5f - dy * 0.5f + 0.4f)));
                    return true;
                case "carrot":
                    c.Poly(new[] { V(16, 14), V(50, 62), V(66, 46) }, (x, y) => Color.Lerp(Hex(0xf59a2a), Hex(0xc8601a), Mathf.Clamp01((x - y + 20) / 60f)));
                    c.Line(V(58, 54), V(74, 84), 5, (t, ss) => Hex(0x4f9a2c));
                    c.Line(V(58, 54), V(86, 70), 5, (t, ss) => Hex(0x3f8a24));
                    c.Line(V(58, 54), V(60, 88), 4, (t, ss) => Hex(0x62ac38));
                    return true;
                case "potato": Root(c, Hex(0xd2aa66), Hex(0x9a7238), false); return true;
                case "baked_potato": Root(c, Hex(0xb07a3a), Hex(0x6e4418), true); return true;
            }
            return false;
        }
    }
}
