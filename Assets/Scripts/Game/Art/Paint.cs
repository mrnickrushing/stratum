using UnityEngine;

namespace Stratum.Game
{
    // Small toolkit for painting tileable textures in code: periodic noise, periodic cells, height-to-normal.
    public sealed class Paint
    {
        public const int S = 64;
        public readonly Color[] C = new Color[S * S];
        public readonly float[] H = new float[S * S];   // height, for the normal map
        readonly int _seed;

        public Paint(int seed) { _seed = seed; for (int i = 0; i < C.Length; i++) C[i] = Color.clear; }

        public static int I(int x, int y) => (x & (S - 1)) + (y & (S - 1)) * S;

        float Lattice(int x, int y, int p, int oct)
        {
            x = ((x % p) + p) % p; y = ((y % p) + p) % p;
            uint h = (uint)(x * 374761393 + y * 668265263 + _seed * 2147483647 + oct * 1274126177);
            h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16;
            return (h & 0xFFFF) / 65535f;
        }

        // Value noise that repeats every 64 pixels: `cells` lattice cells across the tile.
        public float V(float x, float y, int cells, int oct = 0)
        {
            float fx = x / S * cells, fy = y / S * cells;
            int ix = Mathf.FloorToInt(fx), iy = Mathf.FloorToInt(fy);
            float tx = fx - ix, ty = fy - iy;
            tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty);
            float a = Lattice(ix, iy, cells, oct), b = Lattice(ix + 1, iy, cells, oct);
            float c = Lattice(ix, iy + 1, cells, oct), d = Lattice(ix + 1, iy + 1, cells, oct);
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
        }

        public float Fbm(float x, float y, int cells, int octaves, float gain = 0.5f)
        {
            float s = 0, a = 1, n = 0;
            for (int o = 0; o < octaves; o++) { s += V(x, y, cells << o, o) * a; n += a; a *= gain; }
            return s / n;
        }

        // Anisotropic noise: separate cell counts across and along (bark, grain).
        public float Streak(float x, float y, int cx, int cy, int octaves)
        {
            float s = 0, a = 1, n = 0;
            for (int o = 0; o < octaves; o++)
            {
                float fx = x / S * (cx << o), fy = y / S * (cy << o);
                int ix = Mathf.FloorToInt(fx), iy = Mathf.FloorToInt(fy);
                float tx = fx - ix, ty = fy - iy;
                tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty);
                int px = cx << o, py = cy << o;
                float va = L2(ix, iy, px, py, o), vb = L2(ix + 1, iy, px, py, o), vc = L2(ix, iy + 1, px, py, o), vd = L2(ix + 1, iy + 1, px, py, o);
                s += Mathf.Lerp(Mathf.Lerp(va, vb, tx), Mathf.Lerp(vc, vd, tx), ty) * a; n += a; a *= 0.5f;
            }
            return s / n;
        }

        float L2(int x, int y, int px, int py, int oct)
        {
            x = ((x % px) + px) % px; y = ((y % py) + py) % py;
            return Lattice(x * 7 + 3, y * 13 + 5, 1 << 20, oct + 11);
        }

        // Periodic cells: distance to nearest and second-nearest feature point (in pixels) and the cell's id.
        public void Cells(float x, float y, int cells, out float f1, out float f2, out int id)
        {
            float cs = (float)S / cells;
            int cx = Mathf.FloorToInt(x / cs), cy = Mathf.FloorToInt(y / cs);
            f1 = f2 = 1e9f; id = 0;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int gx = cx + dx, gy = cy + dy;
                    int wx = ((gx % cells) + cells) % cells, wy = ((gy % cells) + cells) % cells;
                    float px = (gx + 0.15f + 0.7f * Lattice(wx, wy, cells, 51)) * cs;
                    float py = (gy + 0.15f + 0.7f * Lattice(wx, wy, cells, 52)) * cs;
                    float d = Mathf.Sqrt((px - x) * (px - x) + (py - y) * (py - y));
                    if (d < f1) { f2 = f1; f1 = d; id = wx + wy * cells; } else if (d < f2) f2 = d;
                }
        }

        public float Hash(int id, int k) => Lattice(id, k, 1 << 20, 77);

        public static Color Mul(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);

        public void Set(int x, int y, Color c, float h) { int i = I(x, y); C[i] = c; H[i] = h; }

        public void Blend(int x, int y, Color c, float a, float h)
        {
            int i = I(x, y);
            C[i] = Color.Lerp(C[i], new Color(c.r, c.g, c.b, Mathf.Max(C[i].a, c.a)), a);
            H[i] = Mathf.Lerp(H[i], h, a);
        }

        // Tangent-space normal (rg) from the height field, wrapping at the edges.
        public Color[] Normals(float strength)
        {
            var n = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = (H[I(x + 1, y)] - H[I(x - 1, y)]) * strength;
                    float dy = (H[I(x, y + 1)] - H[I(x, y - 1)]) * strength;
                    var v = new Vector3(-dx, -dy, 1).normalized;
                    n[I(x, y)] = new Color(v.x * 0.5f + 0.5f, v.y * 0.5f + 0.5f, v.z * 0.5f + 0.5f, 1);
                }
            return n;
        }
    }
}
