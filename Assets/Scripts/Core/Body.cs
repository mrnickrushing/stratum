using System;

namespace Stratum.Core
{
    // Collision of an upright box (a player or a creature: centred on x/z, standing on y) against the blocks,
    // including blocks that are only partly solid (beds, slabs, doors). Shared by the game and the server.
    public static class Body
    {
        static readonly Box Whole = new Box(0, 0, 0, 1, 1, 1);
        const float Eps = 0.0005f;

        // Does anything solid overlap the box (x0..x1, y0..y1, z0..z1)?
        public static bool Blocked(Func<int, int, int, byte> get, float x0, float y0, float z0, float x1, float y1, float z1)
        {
            int ix0 = (int)Math.Floor(x0 + Eps), ix1 = (int)Math.Floor(x1 - Eps);
            int iy0 = (int)Math.Floor(y0 + Eps), iy1 = (int)Math.Floor(y1 - Eps);
            int iz0 = (int)Math.Floor(z0 + Eps), iz1 = (int)Math.Floor(z1 - Eps);
            for (int y = iy0; y <= iy1; y++)
                for (int z = iz0; z <= iz1; z++)
                    for (int x = ix0; x <= ix1; x++)
                    {
                        var d = Blocks.Get(get(x, y, z));
                        if (!d.Solid) continue;
                        if (d.Shape != Shape.Boxes || d.Boxes == null) return true;
                        foreach (var b in d.Boxes)
                            if (x + b.X1 > x0 + Eps && x + b.X0 < x1 - Eps && y + b.Y1 > y0 + Eps && y + b.Y0 < y1 - Eps && z + b.Z1 > z0 + Eps && z + b.Z0 < z1 - Eps)
                                return true;
                    }
            return false;
        }

        public static bool Blocked(Func<int, int, int, byte> get, float fx, float fy, float fz, float hw, float h) =>
            Blocked(get, fx - hw, fy, fz - hw, fx + hw, fy + h, fz + hw);

        // How far the body can move along one axis (0 x, 1 y, 2 z) toward d before something solid stops it.
        public static float Sweep(Func<int, int, int, byte> get, float fx, float fy, float fz, float hw, float h, int axis, float d)
        {
            if (d == 0) return 0;
            Span<float> lo = stackalloc float[3], hi = stackalloc float[3], slo = stackalloc float[3], shi = stackalloc float[3];
            lo[0] = fx - hw; lo[1] = fy; lo[2] = fz - hw; hi[0] = fx + hw; hi[1] = fy + h; hi[2] = fz + hw;
            lo.CopyTo(slo); hi.CopyTo(shi);
            if (d > 0) shi[axis] += d; else slo[axis] += d;
            int ix0 = (int)Math.Floor(slo[0] + Eps), ix1 = (int)Math.Floor(shi[0] - Eps);
            int iy0 = (int)Math.Floor(slo[1] + Eps), iy1 = (int)Math.Floor(shi[1] - Eps);
            int iz0 = (int)Math.Floor(slo[2] + Eps), iz1 = (int)Math.Floor(shi[2] - Eps);
            float best = d;
            Span<float> bl = stackalloc float[3], bh = stackalloc float[3];
            for (int y = iy0; y <= iy1; y++)
                for (int z = iz0; z <= iz1; z++)
                    for (int x = ix0; x <= ix1; x++)
                    {
                        var def = Blocks.Get(get(x, y, z));
                        if (!def.Solid) continue;
                        var boxes = def.Shape == Shape.Boxes && def.Boxes != null ? def.Boxes : null;
                        int n = boxes?.Length ?? 1;
                        for (int k = 0; k < n; k++)
                        {
                            var b = boxes != null ? boxes[k] : Whole;
                            bl[0] = x + b.X0; bl[1] = y + b.Y0; bl[2] = z + b.Z0;
                            bh[0] = x + b.X1; bh[1] = y + b.Y1; bh[2] = z + b.Z1;
                            bool across = true;
                            for (int a = 0; a < 3 && across; a++)
                                if (a != axis && !(bh[a] > lo[a] + Eps && bl[a] < hi[a] - Eps)) across = false;
                            if (!across) continue;
                            if (d > 0)
                            {
                                if (bl[axis] < hi[axis] - Eps) continue;          // already inside or behind
                                float gap = bl[axis] - hi[axis] - 0.001f;
                                if (gap < best) best = Math.Max(0, gap);
                            }
                            else
                            {
                                if (bh[axis] > lo[axis] + Eps) continue;
                                float gap = bh[axis] - lo[axis] + 0.001f;
                                if (gap > best) best = Math.Min(0, gap);
                            }
                        }
                    }
            return best;
        }

        // Height of the solid top of a cell's blocks under a point (0..1 within the cell), or -1 if nothing's there.
        public static float TopAt(byte id, float lx, float lz)
        {
            var d = Blocks.Get(id);
            if (!d.Solid) return -1;
            if (d.Shape != Shape.Boxes || d.Boxes == null) return 1;
            float top = -1;
            foreach (var b in d.Boxes) if (lx >= b.X0 && lx <= b.X1 && lz >= b.Z0 && lz <= b.Z1) top = Math.Max(top, b.Y1);
            return top;
        }

        // Ray against a block's shape (cell-local origin and direction); distance along the ray, or -1.
        public static float Ray(byte id, float ox, float oy, float oz, float dx, float dy, float dz)
        {
            var d = Blocks.Get(id);
            if (d.Shape != Shape.Boxes || d.Boxes == null) return RayBox(Whole, ox, oy, oz, dx, dy, dz);
            float best = -1;
            foreach (var b in d.Boxes)
            {
                float t = RayBox(b, ox, oy, oz, dx, dy, dz);
                if (t >= 0 && (best < 0 || t < best)) best = t;
            }
            return best;
        }

        public static float RayBox(Box b, float ox, float oy, float oz, float dx, float dy, float dz)
        {
            float t0 = 0, t1 = 1e9f;
            if (!Slab(b.X0, b.X1, ox, dx, ref t0, ref t1)) return -1;
            if (!Slab(b.Y0, b.Y1, oy, dy, ref t0, ref t1)) return -1;
            if (!Slab(b.Z0, b.Z1, oz, dz, ref t0, ref t1)) return -1;
            return t0;
        }

        static bool Slab(float lo, float hi, float o, float d, ref float t0, ref float t1)
        {
            if (Math.Abs(d) < 1e-9f) return o >= lo && o <= hi;
            float a = (lo - o) / d, b = (hi - o) / d;
            if (a > b) { var t = a; a = b; b = t; }
            if (a > t0) t0 = a;
            if (b < t1) t1 = b;
            return t0 <= t1;
        }
    }
}
