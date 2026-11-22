using Stratum.Core;
using UnityEngine;

namespace Stratum.Game
{
    // Pictures for the blocks added from 0.14 on.
    public static partial class BlockTextures
    {
        static float Later(Tex t, Paint p)
        {
            switch (t)
            {
                case Tex.Fire: Flames(p); return 0.3f;
                case Tex.KegTop: KegTop(p); return 2.5f;
                case Tex.KegSide: KegSide(p, false); return 3f;
                case Tex.KegLit: KegSide(p, true); return 3f;
                case Tex.StarOre: Ore(p, Hex(0x8ff0f4), Hex(0x3aa6c8), false); return 4f;
            }
            if (t >= Tex.Carrot0 && t <= Tex.Carrot3) { RootCrop(p, t - Tex.Carrot0, Hex(0x4f9a2c), Hex(0xf08a1e), true); return 1f; }
            if (t >= Tex.Potato0 && t <= Tex.Potato3) { RootCrop(p, t - Tex.Potato0, Hex(0x3f7e34), Hex(0xc9a25e), false); return 1f; }
            switch (t)
            {
            }
            return 1f;
        }

        // Tongues of flame rising from the bottom edge: deep orange at the foot, yellow-white in the heart.
        static void Flames(Paint p)
        {
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++) p.Set(x, y, new Color(1f, 0.5f, 0.1f, 0), 0);
            for (int x = 0; x < 64; x++)
            {
                // Several tongues of different heights across the width.
                float tongue = 0;
                for (int k = 0; k < 6; k++)
                {
                    float cx = 6 + k * 10.5f + (p.Hash(k, 3) - 0.5f) * 6, w = 5 + p.Hash(k, 5) * 4, h = 26 + p.Hash(k, 7) * 34;
                    float d = Mathf.Abs(x - cx) / w;
                    if (d < 1) tongue = Mathf.Max(tongue, h * (1 - d * d));
                }
                tongue += (p.V(x, 3, 16) - 0.5f) * 6;
                for (int y = 0; y < 64; y++)
                {
                    if (y > tongue) continue;
                    float up = y / Mathf.Max(1f, tongue);
                    float heart = Mathf.Clamp01(1 - up * 1.5f) * Mathf.Clamp01(tongue / 40f);
                    Color c = Color.Lerp(Color.Lerp(new Color(0.95f, 0.32f, 0.05f), new Color(1f, 0.66f, 0.12f), 1 - up), new Color(1f, 0.95f, 0.6f), heart * 0.8f);
                    if (p.V(x * 1.3f, y * 0.8f, 16, 1) > 0.78f && up > 0.55f) continue;         // ragged tips
                    c.a = 1;
                    p.Set(x, y, c, 0.5f);
                }
            }
        }

        // A row of leafy tufts that fill out as they grow; ripe, the tops of the roots show at the soil.
        static void RootCrop(Paint p, int stage, Color leaf, Color root, bool feathery)
        {
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) p.Set(x, y, new Color(leaf.r, leaf.g, leaf.b, 0), 0);
            float height = new[] { 0.22f, 0.4f, 0.6f, 0.78f }[stage];
            for (int k = 0; k < 4; k++)
            {
                float cx = 8 + k * 16 + (p.Hash(k, 1) - 0.5f) * 4;
                float h = (0.85f + p.Hash(k, 2) * 0.15f) * height * 64;
                int leaves = feathery ? 7 : 5;
                for (int l = 0; l < leaves; l++)
                {
                    float a = (l / (float)(leaves - 1) - 0.5f) * (feathery ? 1.5f : 1.9f), len = h * (1 - Mathf.Abs(a) * 0.28f);
                    for (float s = 0; s < len; s += 0.5f)
                    {
                        float t = s / len;
                        int x = Mathf.RoundToInt(cx + Mathf.Sin(a) * s * 0.75f), y = Mathf.RoundToInt(2 + Mathf.Cos(a) * s);
                        float w = feathery ? 1 + (1 - t) * 0.6f : 1.2f + Mathf.Sin(t * Mathf.PI) * 2.6f;
                        for (int dx = -Mathf.CeilToInt(w); dx <= Mathf.CeilToInt(w); dx++)
                        {
                            if (Mathf.Abs(dx) > w || x + dx < 0 || x + dx > 63 || y < 0 || y > 63) continue;
                            var c = Paint.Mul(leaf, 0.72f + t * 0.4f + (((x + dx + y) & 3) == 0 ? -0.12f : 0)); c.a = 1;
                            p.Set(x + dx, y, c, 0.3f + t * 0.5f);
                        }
                    }
                }
                if (stage == 3)
                    for (int y = 0; y < 6; y++)
                        for (int x = -4; x <= 4; x++)
                        {
                            if (Mathf.Abs(x) > 4 - y * 0.4f) continue;
                            int px = Mathf.RoundToInt(cx) + x; if (px < 0 || px > 63) continue;
                            var c = Paint.Mul(root, 0.8f + (4 - Mathf.Abs(x)) * 0.06f); c.a = 1; p.Set(px, y, c, 0.7f);
                        }
            }
        }

        static void KegTop(Paint p)
        {
            Color wood = new Color(0.56f, 0.2f, 0.16f), iron = new Color(0.24f, 0.24f, 0.27f);
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    float dx = x - 31.5f, dy = y - 31.5f, r = Mathf.Sqrt(dx * dx + dy * dy);
                    float grain = p.Streak(x, y, 12, 2, 3);
                    Color c = Paint.Mul(wood, 0.8f + grain * 0.35f);
                    float h = 0.5f + grain * 0.1f;
                    if (r > 27) { c = iron; h = 0.8f; }
                    else if (y % 13 == 0) { c = Paint.Mul(wood, 0.5f); h = 0.2f; }
                    if (r < 4.5f) { c = new Color(0.9f, 0.86f, 0.7f); h = 0.9f; }                     // the fuse hole and its cord
                    else if (r < 6.5f) { c = new Color(0.12f, 0.1f, 0.1f); h = 0.1f; }
                    c.a = 1; p.Set(x, y, c, h);
                }
        }

        // Red staves, iron hoops and a pale band with a burst painted on it; lit, the band glows.
        static void KegSide(Paint p, bool lit)
        {
            Color wood = new Color(0.6f, 0.2f, 0.15f), iron = new Color(0.25f, 0.25f, 0.28f), band = lit ? new Color(1f, 0.86f, 0.5f) : new Color(0.9f, 0.86f, 0.74f);
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    int stave = x / 8, lx = x % 8;
                    float grain = p.Streak(y + stave * 17, x, 2, 12, 3);
                    Color c = Paint.Mul(wood, (0.82f + p.Hash(stave, 2) * 0.22f) * (0.85f + grain * 0.3f));
                    float h = 0.5f + grain * 0.15f;
                    if (lx == 0) { c = Paint.Mul(wood, 0.45f); h = 0.05f; }
                    if (y >= 6 && y < 11 || y >= 53 && y < 58) { c = Paint.Mul(iron, 0.9f + p.V(x, y, 16) * 0.3f); h = 0.85f; }
                    if (y >= 22 && y < 42)
                    {
                        c = Paint.Mul(band, 0.92f + p.V(x, y, 8) * 0.12f); h = 0.6f;
                        // A many-pointed burst in the middle of the band.
                        float dx = x - 31.5f, dy = y - 31.5f, r = Mathf.Sqrt(dx * dx + dy * dy), a = Mathf.Atan2(dy, dx);
                        float star = 5.5f + 3.5f * Mathf.Abs(Mathf.Cos(a * 4));
                        if (r < star) c = lit ? new Color(1f, 0.35f, 0.1f) : new Color(0.75f, 0.16f, 0.1f);
                        if (y == 22 || y == 41) c = Paint.Mul(c, 0.6f);
                    }
                    c.a = 1; p.Set(x, y, c, h);
                }
        }
    }
}
