using System;

namespace Stratum.Core
{
    // Seeded simplex noise (2D and 3D, after Gustavson) plus fractal helpers and integer hashes.
    // Simplex has no flat lattice, so whole-number inputs don't line up into seams.
    public sealed class Noise
    {
        readonly short[] _perm = new short[512];
        readonly short[] _perm12 = new short[512];

        static readonly float[] G3 =
        {
            1,1,0, -1,1,0, 1,-1,0, -1,-1,0, 1,0,1, -1,0,1, 1,0,-1, -1,0,-1, 0,1,1, 0,-1,1, 0,1,-1, 0,-1,-1
        };

        public Noise(int seed)
        {
            var p = new short[256];
            for (short i = 0; i < 256; i++) p[i] = i;
            uint s = (uint)seed * 2654435761u + 0x9E3779B9u;
            for (int i = 255; i > 0; i--)
            {
                s ^= s << 13; s ^= s >> 17; s ^= s << 5;
                int j = (int)(s % (uint)(i + 1));
                (p[i], p[j]) = (p[j], p[i]);
            }
            for (int i = 0; i < 512; i++) { _perm[i] = p[i & 255]; _perm12[i] = (short)(_perm[i] % 12); }
        }

        const float F2 = 0.36602540378f, G2 = 0.21132486540f;

        public float S2(float x, float y)
        {
            float s = (x + y) * F2;
            int i = FastFloor(x + s), j = FastFloor(y + s);
            float t = (i + j) * G2;
            float x0 = x - (i - t), y0 = y - (j - t);
            int i1 = x0 > y0 ? 1 : 0, j1 = 1 - i1;
            float x1 = x0 - i1 + G2, y1 = y0 - j1 + G2, x2 = x0 - 1 + 2 * G2, y2 = y0 - 1 + 2 * G2;
            int ii = i & 255, jj = j & 255;
            float n = 0;
            float t0 = 0.5f - x0 * x0 - y0 * y0;
            if (t0 > 0) { int g = _perm12[ii + _perm[jj]] * 3; t0 *= t0; n += t0 * t0 * (G3[g] * x0 + G3[g + 1] * y0); }
            float t1 = 0.5f - x1 * x1 - y1 * y1;
            if (t1 > 0) { int g = _perm12[ii + i1 + _perm[jj + j1]] * 3; t1 *= t1; n += t1 * t1 * (G3[g] * x1 + G3[g + 1] * y1); }
            float t2 = 0.5f - x2 * x2 - y2 * y2;
            if (t2 > 0) { int g = _perm12[ii + 1 + _perm[jj + 1]] * 3; t2 *= t2; n += t2 * t2 * (G3[g] * x2 + G3[g + 1] * y2); }
            return 70f * n;   // about -1..1
        }

        const float F3 = 1f / 3f, G3c = 1f / 6f;

        public float S3(float x, float y, float z)
        {
            float s = (x + y + z) * F3;
            int i = FastFloor(x + s), j = FastFloor(y + s), k = FastFloor(z + s);
            float t = (i + j + k) * G3c;
            float x0 = x - (i - t), y0 = y - (j - t), z0 = z - (k - t);
            int i1, j1, k1, i2, j2, k2;
            if (x0 >= y0)
            {
                if (y0 >= z0) { i1 = 1; j1 = 0; k1 = 0; i2 = 1; j2 = 1; k2 = 0; }
                else if (x0 >= z0) { i1 = 1; j1 = 0; k1 = 0; i2 = 1; j2 = 0; k2 = 1; }
                else { i1 = 0; j1 = 0; k1 = 1; i2 = 1; j2 = 0; k2 = 1; }
            }
            else
            {
                if (y0 < z0) { i1 = 0; j1 = 0; k1 = 1; i2 = 0; j2 = 1; k2 = 1; }
                else if (x0 < z0) { i1 = 0; j1 = 1; k1 = 0; i2 = 0; j2 = 1; k2 = 1; }
                else { i1 = 0; j1 = 1; k1 = 0; i2 = 1; j2 = 1; k2 = 0; }
            }
            float x1 = x0 - i1 + G3c, y1 = y0 - j1 + G3c, z1 = z0 - k1 + G3c;
            float x2 = x0 - i2 + 2 * G3c, y2 = y0 - j2 + 2 * G3c, z2 = z0 - k2 + 2 * G3c;
            float x3 = x0 - 1 + 3 * G3c, y3 = y0 - 1 + 3 * G3c, z3 = z0 - 1 + 3 * G3c;
            int ii = i & 255, jj = j & 255, kk = k & 255;
            float n = 0;
            float t0 = 0.6f - x0 * x0 - y0 * y0 - z0 * z0;
            if (t0 > 0) { int g = _perm12[ii + _perm[jj + _perm[kk]]] * 3; t0 *= t0; n += t0 * t0 * (G3[g] * x0 + G3[g + 1] * y0 + G3[g + 2] * z0); }
            float t1 = 0.6f - x1 * x1 - y1 * y1 - z1 * z1;
            if (t1 > 0) { int g = _perm12[ii + i1 + _perm[jj + j1 + _perm[kk + k1]]] * 3; t1 *= t1; n += t1 * t1 * (G3[g] * x1 + G3[g + 1] * y1 + G3[g + 2] * z1); }
            float t2 = 0.6f - x2 * x2 - y2 * y2 - z2 * z2;
            if (t2 > 0) { int g = _perm12[ii + i2 + _perm[jj + j2 + _perm[kk + k2]]] * 3; t2 *= t2; n += t2 * t2 * (G3[g] * x2 + G3[g + 1] * y2 + G3[g + 2] * z2); }
            float t3 = 0.6f - x3 * x3 - y3 * y3 - z3 * z3;
            if (t3 > 0) { int g = _perm12[ii + 1 + _perm[jj + 1 + _perm[kk + 1]]] * 3; t3 *= t3; n += t3 * t3 * (G3[g] * x3 + G3[g + 1] * y3 + G3[g + 2] * z3); }
            return 32f * n;
        }

        public float Fbm2(float x, float y, int octaves, float lacunarity = 2f, float gain = 0.5f)
        {
            float sum = 0, amp = 1, norm = 0;
            for (int o = 0; o < octaves; o++)
            {
                sum += S2(x, y) * amp; norm += amp;
                x = x * lacunarity + 17.31f; y = y * lacunarity - 9.77f; amp *= gain;
            }
            return sum / norm;
        }

        // Ridged noise in 0..1: sharp crests where the noise crosses zero.
        public float Ridge2(float x, float y, int octaves)
        {
            float sum = 0, amp = 1, norm = 0;
            for (int o = 0; o < octaves; o++)
            {
                float r = 1 - Math.Abs(S2(x, y));
                sum += r * r * amp; norm += amp;
                x = x * 2.03f + 5.1f; y = y * 2.03f + 3.3f; amp *= 0.5f;
            }
            return sum / norm;
        }

        public float Fbm3(float x, float y, float z, int octaves)
        {
            float sum = 0, amp = 1, norm = 0;
            for (int o = 0; o < octaves; o++)
            {
                sum += S3(x, y, z) * amp; norm += amp;
                x = x * 2f + 3.7f; y = y * 2f + 1.3f; z = z * 2f - 4.1f; amp *= 0.5f;
            }
            return sum / norm;
        }

        static int FastFloor(float x) { int i = (int)x; return x < i ? i - 1 : i; }

        public static uint Hash(int x, int y, int z, int seed)
        {
            unchecked
            {
                uint h = (uint)seed * 0x27d4eb2du;
                h ^= (uint)x * 0x85ebca6bu; h = (h << 13) | (h >> 19);
                h ^= (uint)y * 0xc2b2ae35u; h = (h << 17) | (h >> 15);
                h ^= (uint)z * 0x165667b1u;
                h ^= h >> 16; h *= 0x7feb352du; h ^= h >> 15; h *= 0x846ca68bu; h ^= h >> 16;
                return h;
            }
        }

        public static float Hash01(int x, int y, int z, int seed) => (Hash(x, y, z, seed) & 0xFFFFFF) / 16777216f;
    }

    // Small deterministic RNG (xorshift) for structures and ore veins.
    public struct Rng
    {
        uint _s;
        public Rng(uint seed) { _s = seed == 0 ? 0x9E3779B9u : seed; }
        public uint Next() { _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5; return _s; }
        public int Range(int min, int maxExclusive) => min + (int)(Next() % (uint)(maxExclusive - min));
        public float Value() => (Next() & 0xFFFFFF) / 16777216f;
    }
}
