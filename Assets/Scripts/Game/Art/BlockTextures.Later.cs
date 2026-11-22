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
                case Tex.PistonSide: PistonSide(p); return 3f;
                case Tex.PistonBack: Rock(p, Hex(0x7c7d82), Hex(0x5c5d62), 0.12f, false); Frame(p, Hex(0x4a4b50), 3); return 3f;
                case Tex.PistonFace: PistonFace(p, false); return 3f;
                case Tex.PistonSticky: PistonFace(p, true); return 3f;
                case Tex.DispenserFace: MachineFace(p, 0); return 4f;
                case Tex.ObserverFace: MachineFace(p, 1); return 4f;
                case Tex.ObserverBack: MachineFace(p, 2); return 3f;
                case Tex.ObserverSide: MachineFace(p, 3); return 3f;
                case Tex.ComparatorOff: Gauge(p, false); return 2f;
                case Tex.ComparatorOn: Gauge(p, true); return 2f;
                case Tex.CageSide: CageSide(p); return 3f;
                case Tex.DustyFront: case Tex.DustySide: Chest(p, false); Dust(p); return 3f;
                case Tex.DustyTop: Planks(p, Hex(0x8a6a44), 4); Dust(p); return 2f;
                case Tex.Stillstone: Rock(p, Hex(0xd9d4c4), Hex(0xb9b6ac), 0.1f, true); return 3f;
                case Tex.GateStone: GateStone(p); return 3f;
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

        // A piston's side: cobbled stone below, a band of planks at the pushing end (the top of the picture).
        static void PistonSide(Paint p)
        {
            Rock(p, Hex(0x7c7d82), Hex(0x5c5d62), 0.12f, false);
            for (int y = 46; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    float g = p.Streak(x, y, 12, 2, 3);
                    Color c = Paint.Mul(Hex(0xa8824e), 0.82f + g * 0.3f);
                    if (y == 46 || y == 47) c = Hex(0x3a3c42);
                    c.a = 1; p.Set(x, y, c, 0.55f + g * 0.1f);
                }
            for (int x = 0; x < 64; x++) { p.Set(x, 20, Hex(0x45464c), 0.8f); p.Set(x, 21, Hex(0x45464c), 0.8f); }
        }

        // The pushing face: planks in an iron frame; the sticky one has a green smear across it.
        static void PistonFace(Paint p, bool sticky)
        {
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    float g = p.Streak(x, y, 12, 2, 3);
                    Color c = Paint.Mul(Hex(0xb08a54), ((y / 16) % 2 == 0 ? 0.95f : 0.86f) * (0.85f + g * 0.3f));
                    float h = 0.5f + g * 0.1f;
                    if (y % 16 == 0) { c = Paint.Mul(c, 0.55f); h = 0.2f; }
                    bool rim = x < 4 || x > 59 || y < 4 || y > 59;
                    if (rim) { c = Paint.Mul(Hex(0x55585e), 0.9f + p.V(x, y, 16) * 0.25f); h = 0.85f; }
                    if (sticky && !rim)
                    {
                        float blob = p.Fbm(x, y, 8, 3);
                        float dx = x - 31.5f, dy = y - 31.5f;
                        if (blob > 0.42f + (dx * dx + dy * dy) / 2600f) { c = Color.Lerp(Hex(0x5fae3a), Hex(0x9be06a), blob); h = 0.75f; }
                    }
                    c.a = 1; p.Set(x, y, c, h);
                }
        }

        // Stone machine faces: 0 a dispenser's mouth, 1 an observer's eye, 2 its output spot, 3 its plain side with an arrow.
        static void MachineFace(Paint p, int kind)
        {
            Rock(p, Hex(0x7c7d82), Hex(0x5c5d62), 0.12f, false); Frame(p, Hex(0x4a4b50), 3);
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    float dx = x - 31.5f, dy = y - 31.5f, r = Mathf.Sqrt(dx * dx + dy * dy);
                    if (kind == 0)
                    {
                        // A round mouth, dark inside, with a lip.
                        if (r < 13) { var c = Color.Lerp(Hex(0x0c0c0e), Hex(0x2a2a2e), Mathf.Clamp01((dy + 13) / 40f)); c.a = 1; p.Set(x, y, c, 0.05f); }
                        else if (r < 16) { p.Set(x, y, Hex(0x3e3f44), 0.9f); }
                    }
                    else if (kind == 1)
                    {
                        // A wide eye: pale, with a dark slot of a pupil.
                        float ex = dx / 22f, ey = dy / 11f;
                        if (ex * ex + ey * ey < 1)
                        {
                            Color c = Mathf.Abs(dx) < 4 && Mathf.Abs(dy) < 9 ? Hex(0x16161a) : Color.Lerp(Hex(0xe8e2d0), Hex(0xb4ae9c), ex * ex + ey * ey);
                            c.a = 1; p.Set(x, y, c, 0.3f);
                        }
                        else if (ex * ex + ey * ey < 1.3f) p.Set(x, y, Hex(0x3a3b40), 0.9f);
                    }
                    else if (kind == 2)
                    {
                        if (r < 6) p.Set(x, y, Hex(0xd8302a), 0.8f);
                        else if (r < 9) p.Set(x, y, Hex(0x3a3b40), 0.9f);
                    }
                    else
                    {
                        // An arrow along the side, pointing from the eye to the output.
                        if (Mathf.Abs(dy) < 3 && Mathf.Abs(dx) < 16 || Mathf.Abs(dy) < 12 - (dx + 16) * 1.0f && dx < -4 && dx > -17) p.Set(x, y, Hex(0x3a3b40), 0.3f);
                    }
                }
        }

        // A comparator's top: a stone plate with three pins (one in front, two behind), lit red when it's on.
        static void Gauge(Paint p, bool on)
        {
            Rock(p, Hex(0x9a9b9f), Hex(0x7a7b80), 0.08f, false);
            Color pin = on ? Hex(0xff4a3a) : Hex(0x6a1a16), line = on ? Hex(0xd8302a) : Hex(0x4a1512);
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    bool a = (x - 32) * (x - 32) + (y - 14) * (y - 14) < 30, b = (x - 18) * (x - 18) + (y - 46) * (y - 46) < 30, c = (x - 46) * (x - 46) + (y - 46) * (y - 46) < 30;
                    if (a || b || c) p.Set(x, y, pin, 0.9f);
                    else if (Mathf.Abs(x - 32) < 2 && y > 14 && y < 46 || Mathf.Abs(y - 46) < 2 && x > 18 && x < 46) p.Set(x, y, line, 0.4f);
                }
        }

        // Iron bars both ways, with a dark middle seen between them.
        static void CageSide(Paint p)
        {
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    bool bar = x % 16 < 4 || y % 16 < 4, rim = x < 4 || x > 59 || y < 4 || y > 59;
                    if (bar || rim) { var c = Paint.Mul(Hex(0x4a4c54), 0.8f + p.V(x, y, 16) * 0.4f + (x % 16 == 1 || y % 16 == 1 ? 0.25f : 0)); c.a = 1; p.Set(x, y, c, 0.9f); }
                    else p.Set(x, y, new Color(0.05f, 0.05f, 0.07f, 0), 0);
                }
        }

        // A grey film and cobwebby streaks over whatever was painted.
        static void Dust(Paint p)
        {
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    int i = Paint.I(x, y);
                    float film = 0.25f + p.Fbm(x, y, 8, 3) * 0.3f + (y > 50 ? 0.15f : 0);
                    var c = Color.Lerp(p.C[i], new Color(0.62f, 0.6f, 0.56f, 1), Mathf.Clamp01(film)); c.a = 1;
                    p.C[i] = c;
                }
        }

        // Dark stone with a ring and a mark in it that glow violet.
        static void GateStone(Paint p)
        {
            Rock(p, Hex(0x2a2434), Hex(0x18141e), 0.18f, true); Frame(p, Hex(0x100c16), 3);
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    float dx = x - 31.5f, dy = y - 31.5f, r = Mathf.Sqrt(dx * dx + dy * dy);
                    bool ring = Mathf.Abs(r - 20) < 2.2f, mark = Mathf.Abs(dx) < 2 && Mathf.Abs(dy) < 11 || Mathf.Abs(dy - 4) < 2 && Mathf.Abs(dx) < 7 || r < 3.2f;
                    if (ring || mark) { var c = Color.Lerp(Hex(0x8a4ae0), Hex(0xe0c0ff), p.V(x, y, 16)); c.a = 1; p.Set(x, y, c, 0.9f); }
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
