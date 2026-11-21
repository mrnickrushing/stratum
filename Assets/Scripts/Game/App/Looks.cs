using UnityEngine;

namespace Stratum.Game
{
    // Look packs: the same block textures drawn in another style. Each is a way of repainting the finished picture
    // of a block (64 x 64), chosen on the Extras page and remembered on this computer.
    public sealed class Look
    {
        public string Name, Desc;
        public float Bump = 1;                       // how strongly surfaces catch the light
    }

    public static class Looks
    {
        public static readonly Look[] All =
        {
            new Look { Name = "Stratum", Desc = "The usual look: detailed, hand-painted blocks." },
            new Look { Name = "Chunky", Desc = "Big square pixels, like a game from long ago.", Bump = 0.5f },
            new Look { Name = "Storybook", Desc = "Bold, bright colours with flat shading, like a picture book.", Bump = 0.7f },
            new Look { Name = "Pastel", Desc = "Soft, chalky colours. Everything looks like sweets.", Bump = 0.8f },
            new Look { Name = "Inked", Desc = "Dark pen lines round every stone and plank, like a comic.", Bump = 1.2f },
        };

        public static int Current
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt("look", 0), 0, All.Length - 1);
            set { PlayerPrefs.SetInt("look", Mathf.Clamp(value, 0, All.Length - 1)); PlayerPrefs.Save(); }
        }

        static float Lum(Color c) => c.r * 0.30f + c.g * 0.59f + c.b * 0.11f;
        static Color Sat(Color c, float k) { float l = Lum(c); return new Color(l + (c.r - l) * k, l + (c.g - l) * k, l + (c.b - l) * k, c.a); }

        // Repaints one block picture (size by size pixels) in the given look. Transparency is kept.
        public static void Apply(Color[] px, int size, int look)
        {
            switch (look)
            {
                case 1:
                {
                    const int B = 4;
                    for (int by = 0; by < size; by += B)
                        for (int bx = 0; bx < size; bx += B)
                        {
                            Color sum = Color.clear; float a = 0, amin = 1, amax = 0; int solid = 0;
                            for (int y = by; y < by + B; y++)
                                for (int x = bx; x < bx + B; x++)
                                {
                                    var c = px[x + y * size];
                                    if (c.a > 0.01f) { sum += new Color(c.r, c.g, c.b, 0) * c.a; solid++; }
                                    a += c.a; amin = Mathf.Min(amin, c.a); amax = Mathf.Max(amax, c.a);
                                }
                            Color flat = a > 0 ? sum / a : px[bx + by * size];
                            flat.a = amax - amin < 0.01f ? amin : (a / (B * B) > 0.28f ? amax : 0);     // thin things (lattice, ladders) stay
                            if (flat.a == 0) { var keep = px[bx + by * size]; flat = new Color(keep.r, keep.g, keep.b, 0); }
                            for (int y = by; y < by + B; y++) for (int x = bx; x < bx + B; x++) px[x + y * size] = flat;
                        }
                    break;
                }
                case 2:
                    for (int i = 0; i < px.Length; i++)
                    {
                        var c = Sat(px[i], 1.4f);
                        float l = Mathf.Max(0.001f, Lum(c)), step = Mathf.Round(l * 6) / 6f;
                        float k = Mathf.Lerp(1, step / l, 0.65f);
                        px[i] = new Color(Mathf.Clamp01(c.r * k * 1.05f), Mathf.Clamp01(c.g * k), Mathf.Clamp01(c.b * k * 0.93f), px[i].a);
                    }
                    break;
                case 3:
                    for (int i = 0; i < px.Length; i++)
                    {
                        var c = Sat(px[i], 0.85f);
                        px[i] = new Color(Mathf.Lerp(c.r, 1, 0.38f), Mathf.Lerp(c.g, 0.98f, 0.38f), Mathf.Lerp(c.b, 0.99f, 0.38f), px[i].a);
                    }
                    break;
                case 4:
                {
                    var lum = new float[px.Length];
                    for (int i = 0; i < px.Length; i++) lum[i] = px[i].a > 0.01f ? Lum(px[i]) : -1;
                    var src = (Color[])px.Clone();
                    for (int y = 0; y < size; y++)
                        for (int x = 0; x < size; x++)
                        {
                            int i = x + y * size;
                            if (lum[i] < 0) continue;
                            float r = lum[((x + 1) % size) + y * size], d = lum[x + ((y + 1) % size) * size];
                            float gx = r < 0 ? 0.5f : Mathf.Abs(r - lum[i]), gy = d < 0 ? 0.5f : Mathf.Abs(d - lum[i]);
                            float edge = Mathf.Clamp01((gx + gy) * 5f - 0.15f);
                            var c = Sat(src[i], 1.15f);
                            float l = Mathf.Max(0.001f, Lum(c)), step = Mathf.Round(l * 8) / 8f, k = Mathf.Lerp(1, step / l, 0.5f) * (1 - edge * 0.78f);
                            px[i] = new Color(Mathf.Clamp01(c.r * k), Mathf.Clamp01(c.g * k), Mathf.Clamp01(c.b * k), src[i].a);
                        }
                    break;
                }
            }
        }
    }
}
