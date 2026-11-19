using System;
using System.Buffers;
using System.Runtime.InteropServices;

namespace Stratum.Core
{
    // Vertex layout shared with the Unity side (Mesh.SetVertexBufferData). Order matches Unity's attribute order:
    // Position, Normal, Color, TexCoord0, TexCoord1.
    [StructLayout(LayoutKind.Sequential)]
    public struct Vertex
    {
        public float X, Y, Z;
        public float NX, NY, NZ;
        public uint Light;      // r sky light, g block light, b ambient occlusion, a 255
        public float U, V, Layer;
        public uint Tint;       // rgb tint, a flags (Flag*)
    }

    public static class Flag
    {
        public const int Tint = 1, TintMask = 2, Clip = 4, Sway = 8, Glow = 16, GlowMask = 32, Plant = 64;
    }

    public sealed class MeshData
    {
        public Vertex[] Verts; public int VertCount;
        public int[] Index; public int SolidCount, WaterCount;   // solid indices first, then water
        public int MinY, MaxY;
        public void Release()
        {
            if (Verts != null) ArrayPool<Vertex>.Shared.Return(Verts);
            if (Index != null) ArrayPool<int>.Shared.Return(Index);
            Verts = null; Index = null;
        }
    }

    // Lights and meshes one chunk. Light is worked out over the chunk and a 16-block border taken from its eight
    // neighbours (light never travels further than 15), so every chunk can be meshed on its own thread.
    public sealed class Mesher
    {
        const int R = 48, RA = R * R;               // region width and area
        readonly byte[] _blk = new byte[RA * Chunk.Height];
        readonly byte[] _sky = new byte[RA * Chunk.Height];
        readonly byte[] _lit = new byte[RA * Chunk.Height];
        readonly int[] _queue = new int[RA * Chunk.Height];
        int _top;                                   // region rows above this are open sky

        Vertex[] _v = new Vertex[1 << 16]; int _vn;
        int[] _is = new int[1 << 17]; int _isn;
        int[] _iw = new int[1 << 15]; int _iwn;

        static readonly bool[] Opaque = new bool[256];
        static readonly byte[] Opac = new byte[256];
        static readonly byte[] Emit = new byte[256];
        static readonly byte[] ShapeOf = new byte[256];
        static readonly int[] FlagsOf = new int[256 * 6];

        static Mesher()
        {
            for (int i = 0; i < 256; i++)
            {
                var d = Blocks.Defs[i];
                if (d == null) { Opaque[i] = false; Opac[i] = 0; continue; }
                Opaque[i] = d.Opaque; Opac[i] = d.Opaque ? (byte)15 : d.Opacity; Emit[i] = d.Emit; ShapeOf[i] = (byte)d.Shape;
                for (int f = 0; f < 6; f++)
                {
                    var t = d.Face(f);
                    int fl = 0;
                    if (t == Tex.GrassTop || t == Tex.OakLeaves || t == Tex.TallGrass || t == Tex.Fern) fl |= Flag.Tint;
                    if (t == Tex.GrassSide) fl |= Flag.TintMask;
                    if (d.Cutout) fl |= Flag.Clip;
                    if (d.Sway) fl |= Flag.Sway;
                    if (t == Tex.Sunstone || t == Tex.Torch) fl |= Flag.Glow;
                    if (t == Tex.SunstoneOre || t == Tex.FurnaceFront) fl |= Flag.GlowMask;
                    FlagsOf[i * 6 + f] = fl;
                }
            }
        }

        // n: the 3x3 chunks around the one to mesh, index (dz + 1) * 3 + (dx + 1); n[4] is the chunk itself.
        public MeshData Build(Chunk[] n)
        {
            var c = n[4];
            _top = 0;
            for (int i = 0; i < 9; i++) _top = Math.Max(_top, n[i].MaxY);
            _top = Math.Min(_top + 2, Chunk.Height);
            Copy(n);
            Light();
            return Mesh(c);
        }

        void Copy(Chunk[] n)
        {
            for (int cz = 0; cz < 3; cz++)
                for (int cx = 0; cx < 3; cx++)
                {
                    var src = n[cz * 3 + cx].Blocks;
                    for (int y = 0; y < _top; y++)
                        for (int z = 0; z < 16; z++)
                            Buffer.BlockCopy(src, (z << 4) | (y << 8), _blk, cx * 16 + (cz * 16 + z) * R + y * RA, 16);
                }
        }

        void Light()
        {
            int top = _top;
            int qh = 0, qt = 0;
            var q = _queue; var blk = _blk; var sky = _sky; var lit = _lit;
            Array.Clear(lit, 0, top * RA);

            // Straight down from the sky.
            for (int col = 0; col < RA; col++)
            {
                int l = 15;
                for (int y = top - 1; y >= 0; y--)
                {
                    int i = col + y * RA;
                    byte o = Opac[blk[i]];
                    if (o >= 15) l = 0; else if (o > 0) l = Math.Max(0, l - o);
                    sky[i] = (byte)l;
                }
            }
            // Then sideways and down into overhangs and caves.
            for (int y = 0; y < top; y++)
                for (int z = 1; z < R - 1; z++)
                    for (int x = 1; x < R - 1; x++)
                    {
                        int i = x + z * R + y * RA;
                        int l = sky[i];
                        if (l < 2) continue;
                        int m = l - 1;
                        if (sky[i + 1] < m && !Opaque[blk[i + 1]] || sky[i - 1] < m && !Opaque[blk[i - 1]] ||
                            sky[i + R] < m && !Opaque[blk[i + R]] || sky[i - R] < m && !Opaque[blk[i - R]])
                            q[qt++] = i;
                    }
            Spread(sky, qh, qt);

            // Block light from torches and glowing stone.
            qt = 0;
            for (int i = 0, n = top * RA; i < n; i++)
            {
                byte e = Emit[blk[i]];
                if (e > 0) { lit[i] = e; q[qt++] = i; }
            }
            Spread(lit, 0, qt);
        }

        void Spread(byte[] L, int qh, int qt)
        {
            var q = _queue; var blk = _blk; int top = _top; int cap = q.Length;
            while (qh != qt)
            {
                int i = q[qh]; qh = qh + 1 == cap ? 0 : qh + 1;
                int l = L[i];
                if (l <= 1) continue;
                int x = i % R, z = (i / R) % R, y = i / RA;
                for (int d = 0; d < 6; d++)
                {
                    int j;
                    switch (d)
                    {
                        case 0: if (x == R - 1) continue; j = i + 1; break;
                        case 1: if (x == 0) continue; j = i - 1; break;
                        case 2: if (z == R - 1) continue; j = i + R; break;
                        case 3: if (z == 0) continue; j = i - R; break;
                        case 4: if (y == top - 1) continue; j = i + RA; break;
                        default: if (y == 0) continue; j = i - RA; break;
                    }
                    byte b = blk[j];
                    if (Opaque[b]) continue;
                    int nl = l - 1 - Opac[b];
                    if (nl > L[j])
                    {
                        L[j] = (byte)nl;
                        q[qt] = j; qt = qt + 1 == cap ? 0 : qt + 1;
                    }
                }
            }
        }

        // ------------------------------------------------------------------ meshing

        byte B(int x, int y, int z) => (uint)y < (uint)_top ? _blk[x + z * R + y * RA] : (byte)0;
        int SkyAt(int x, int y, int z) => y >= _top ? 15 : y < 0 ? 0 : _sky[x + z * R + y * RA];
        int LitAt(int x, int y, int z) => (uint)y < (uint)_top ? _lit[x + z * R + y * RA] : 0;
        bool Op(int x, int y, int z) => (uint)y < (uint)_top && Opaque[_blk[x + z * R + y * RA]];

        // Face tables: normal, the four corners (clockwise seen from outside) and the two in-plane axes.
        static readonly int[,] N = { { 1, 0, 0 }, { -1, 0, 0 }, { 0, 1, 0 }, { 0, -1, 0 }, { 0, 0, 1 }, { 0, 0, -1 } };
        static readonly int[,,] C =
        {
            { { 1, 0, 0 }, { 1, 1, 0 }, { 1, 1, 1 }, { 1, 0, 1 } },
            { { 0, 0, 1 }, { 0, 1, 1 }, { 0, 1, 0 }, { 0, 0, 0 } },
            { { 0, 1, 0 }, { 0, 1, 1 }, { 1, 1, 1 }, { 1, 1, 0 } },
            { { 0, 0, 0 }, { 1, 0, 0 }, { 1, 0, 1 }, { 0, 0, 1 } },
            { { 1, 0, 1 }, { 1, 1, 1 }, { 0, 1, 1 }, { 0, 0, 1 } },
            { { 0, 0, 0 }, { 0, 1, 0 }, { 1, 1, 0 }, { 1, 0, 0 } },
        };
        static readonly float[,] UV = { { 0, 0 }, { 0, 1 }, { 1, 1 }, { 1, 0 } };
        static readonly float[,] UVBottom = { { 0, 0 }, { 1, 0 }, { 1, 1 }, { 0, 1 } };
        static readonly int[] AxisA = { 1, 1, 0, 0, 0, 0 };   // first in-plane axis per face (0 x, 1 y, 2 z)
        static readonly int[] AxisB = { 2, 2, 2, 2, 1, 1 };

        MeshData Mesh(Chunk c)
        {
            _vn = 0; _isn = 0; _iwn = 0;
            int minY = int.MaxValue, maxY = 0;
            int ymax = Math.Min(c.MaxY + 1, _top);
            for (int y = 0; y < ymax; y++)
                for (int z = 0; z < 16; z++)
                    for (int x = 0; x < 16; x++)
                    {
                        int rx = x + 16, rz = z + 16;
                        byte id = _blk[rx + rz * R + y * RA];
                        if (id == 0) continue;
                        int start = _vn;
                        switch ((Shape)ShapeOf[id])
                        {
                            case Shape.Cube: Cube(c, id, x, y, z, rx, rz); break;
                            case Shape.Liquid: Water(id, x, y, z, rx, rz); break;
                            case Shape.Cross: Cross(c, id, x, y, z, rx, rz); break;
                            case Shape.Torch: Torch(id, x, y, z, rx, rz); break;
                        }
                        if (_vn != start) { if (y < minY) minY = y; if (y > maxY) maxY = y; }
                    }

            var md = new MeshData { VertCount = _vn, SolidCount = _isn, WaterCount = _iwn, MinY = minY == int.MaxValue ? 0 : minY, MaxY = maxY + 1 };
            md.Verts = ArrayPool<Vertex>.Shared.Rent(Math.Max(1, _vn));
            Array.Copy(_v, md.Verts, _vn);
            md.Index = ArrayPool<int>.Shared.Rent(Math.Max(1, _isn + _iwn));
            Array.Copy(_is, md.Index, _isn);
            Array.Copy(_iw, 0, md.Index, _isn, _iwn);
            return md;
        }

        void Grow()
        {
            if (_vn + 64 > _v.Length) Array.Resize(ref _v, _v.Length * 2);
            if (_isn + 96 > _is.Length) Array.Resize(ref _is, _is.Length * 2);
            if (_iwn + 96 > _iw.Length) Array.Resize(ref _iw, _iw.Length * 2);
        }

        static uint Pack(int sky, int lit, int ao) => (uint)(sky * 17) | ((uint)(lit * 17) << 8) | ((uint)(ao * 85) << 16) | 0xFF000000u;

        uint TintFor(Chunk c, int x, int z, int flags, byte id)
        {
            uint rgb;
            if ((flags & (Flag.Tint | Flag.TintMask)) == 0) rgb = 0xFFFFFF;
            else
            {
                int i = (x + z * 16) * 4;
                int r = c.Tint[i], g = c.Tint[i + 1], b = c.Tint[i + 2];
                if (id == Blocks.OakLeaves) { float k = c.Tint[i + 3] / 255f * 0.82f; r = (int)(r * k); g = (int)(g * k); b = (int)(b * k); }
                rgb = (uint)r | ((uint)g << 8) | ((uint)b << 16);
            }
            return rgb | ((uint)flags << 24);
        }

        void Cube(Chunk c, byte id, int x, int y, int z, int rx, int rz)
        {
            bool selfOpaque = Opaque[id];
            Span<int> ao = stackalloc int[4];
            for (int f = 0; f < 6; f++)
            {
                int nx = rx + N[f, 0], ny = y + N[f, 1], nz = rz + N[f, 2];
                if (ny < 0) continue;
                byte nb = B(nx, ny, nz);
                if (Opaque[nb]) continue;
                if (!selfOpaque && nb == id && id != Blocks.OakLeaves && id != Blocks.BirchLeaves && id != Blocks.PineNeedles) continue; // glass, ice
                Grow();
                int flags = FlagsOf[id * 6 + f];
                uint tint = TintFor(c, x, z, flags, id);
                float layer = (float)Blocks.Defs[id].Face(f);
                int a = AxisA[f], b = AxisB[f];
                int baseV = _vn;
                for (int k = 0; k < 4; k++)
                {
                    int ox = C[f, k, 0], oy = C[f, k, 1], oz = C[f, k, 2];
                    int da = (a == 0 ? ox : a == 1 ? oy : oz) == 1 ? 1 : -1;
                    int db = (b == 0 ? ox : b == 1 ? oy : oz) == 1 ? 1 : -1;
                    int ax = a == 0 ? da : 0, ay = a == 1 ? da : 0, az = a == 2 ? da : 0;
                    int bx2 = b == 0 ? db : 0, by = b == 1 ? db : 0, bz = b == 2 ? db : 0;
                    bool s1 = Op(nx + ax, ny + ay, nz + az), s2 = Op(nx + bx2, ny + by, nz + bz);
                    bool cc = Op(nx + ax + bx2, ny + ay + by, nz + az + bz);
                    int o = s1 && s2 ? 0 : 3 - ((s1 ? 1 : 0) + (s2 ? 1 : 0) + (cc ? 1 : 0));
                    ao[k] = o;
                    int sk = SkyAt(nx, ny, nz), lt = LitAt(nx, ny, nz), cnt = 1;
                    if (!s1) { sk += SkyAt(nx + ax, ny + ay, nz + az); lt += LitAt(nx + ax, ny + ay, nz + az); cnt++; }
                    if (!s2) { sk += SkyAt(nx + bx2, ny + by, nz + bz); lt += LitAt(nx + bx2, ny + by, nz + bz); cnt++; }
                    if (!cc && !(s1 && s2)) { sk += SkyAt(nx + ax + bx2, ny + ay + by, nz + az + bz); lt += LitAt(nx + ax + bx2, ny + ay + by, nz + az + bz); cnt++; }
                    ref var v = ref _v[_vn++];
                    v.X = x + ox; v.Y = y + oy; v.Z = z + oz;
                    v.NX = N[f, 0]; v.NY = N[f, 1]; v.NZ = N[f, 2];
                    v.Light = Pack((sk + cnt / 2) / cnt, (lt + cnt / 2) / cnt, o);
                    if (f == 3) { v.U = UVBottom[k, 0]; v.V = UVBottom[k, 1]; } else { v.U = UV[k, 0]; v.V = UV[k, 1]; }
                    v.Layer = layer; v.Tint = tint;
                }
                // Turn the quad so its diagonal follows the darker corners (no streaky shading).
                if (ao[0] + ao[2] < ao[1] + ao[3])
                { _is[_isn++] = baseV + 1; _is[_isn++] = baseV + 2; _is[_isn++] = baseV + 3; _is[_isn++] = baseV + 1; _is[_isn++] = baseV + 3; _is[_isn++] = baseV; }
                else
                { _is[_isn++] = baseV; _is[_isn++] = baseV + 1; _is[_isn++] = baseV + 2; _is[_isn++] = baseV; _is[_isn++] = baseV + 2; _is[_isn++] = baseV + 3; }
            }
        }

        void Water(byte id, int x, int y, int z, int rx, int rz)
        {
            bool covered = B(rx, y + 1, rz) == id;
            float topH = covered ? 1f : 0.875f;
            for (int f = 0; f < 6; f++)
            {
                int nx = rx + N[f, 0], ny = y + N[f, 1], nz = rz + N[f, 2];
                if (ny < 0) continue;
                byte nb = B(nx, ny, nz);
                if (nb == id || Opaque[nb]) continue;
                if (nb == Blocks.Ice && f == 2) continue;
                Grow();
                int sk = SkyAt(nx, ny, nz), lt = LitAt(nx, ny, nz);
                if (f == 2 && !covered) { sk = SkyAt(rx, y + 1, rz); lt = LitAt(rx, y + 1, rz); }
                uint light = Pack(sk, lt, 3);
                int baseV = _vn;
                for (int k = 0; k < 4; k++)
                {
                    ref var v = ref _v[_vn++];
                    v.X = x + C[f, k, 0]; v.Y = y + (C[f, k, 1] == 1 ? topH : 0); v.Z = z + C[f, k, 2];
                    v.NX = N[f, 0]; v.NY = N[f, 1]; v.NZ = N[f, 2];
                    v.Light = light;
                    v.U = f == 3 ? UVBottom[k, 0] : UV[k, 0]; v.V = f == 3 ? UVBottom[k, 1] : UV[k, 1];
                    v.Layer = (float)Tex.Water; v.Tint = 0xFFFFFF;
                }
                _iw[_iwn++] = baseV; _iw[_iwn++] = baseV + 1; _iw[_iwn++] = baseV + 2;
                _iw[_iwn++] = baseV; _iw[_iwn++] = baseV + 2; _iw[_iwn++] = baseV + 3;
                // The surface is seen from below too.
                if (f == 2)
                {
                    _iw[_iwn++] = baseV; _iw[_iwn++] = baseV + 2; _iw[_iwn++] = baseV + 1;
                    _iw[_iwn++] = baseV; _iw[_iwn++] = baseV + 3; _iw[_iwn++] = baseV + 2;
                }
            }
        }

        void Cross(Chunk c, byte id, int x, int y, int z, int rx, int rz)
        {
            Grow();
            int flags = FlagsOf[id * 6 + 2] | Flag.Plant;
            uint tint = TintFor(c, x, z, flags, id);
            uint light = Pack(SkyAt(rx, y, rz), LitAt(rx, y, rz), 3);
            float layer = (float)Blocks.Defs[id].Top;
            uint h = Noise.Hash(c.X * 16 + x, y, c.Z * 16 + z, 77);
            float jx = ((h & 255) / 255f - 0.5f) * 0.3f, jz = (((h >> 8) & 255) / 255f - 0.5f) * 0.3f;
            float s = 0.45f, hgt = id == Blocks.TallGrass || id == Blocks.Fern ? 0.95f : 0.85f;
            float cx = x + 0.5f + jx, cz = z + 0.5f + jz;
            for (int p = 0; p < 2; p++)
            {
                float dx = p == 0 ? s : s, dz = p == 0 ? s : -s;
                int baseV = _vn;
                Quad(cx - dx, y, cz - dz, cx + dx, y + hgt, cz + dz, light, layer, tint);
                _is[_isn++] = baseV; _is[_isn++] = baseV + 1; _is[_isn++] = baseV + 2;
                _is[_isn++] = baseV; _is[_isn++] = baseV + 2; _is[_isn++] = baseV + 3;
                _is[_isn++] = baseV; _is[_isn++] = baseV + 2; _is[_isn++] = baseV + 1;
                _is[_isn++] = baseV; _is[_isn++] = baseV + 3; _is[_isn++] = baseV + 2;
            }
        }

        void Quad(float x0, float y0, float z0, float x1, float y1, float z1, uint light, float layer, uint tint)
        {
            for (int k = 0; k < 4; k++)
            {
                ref var v = ref _v[_vn++];
                bool right = k >= 2, up = k == 1 || k == 2;
                v.X = right ? x1 : x0; v.Y = up ? y1 : y0; v.Z = right ? z1 : z0;
                v.NX = 0; v.NY = 1; v.NZ = 0;
                v.Light = light; v.U = right ? 1 : 0; v.V = up ? 1 : 0; v.Layer = layer; v.Tint = tint;
            }
        }

        void Torch(byte id, int x, int y, int z, int rx, int rz)
        {
            Grow();
            uint light = Pack(SkyAt(rx, y, rz), 15, 3);
            float layer = (float)Tex.Torch;
            uint tint = 0xFFFFFF | ((uint)(Flag.Clip | Flag.Glow) << 24);
            const float w = 1f / 16f, hgt = 10f / 16f;
            float x0 = x + 0.5f - w, x1 = x + 0.5f + w, z0 = z + 0.5f - w, z1 = z + 0.5f + w, y0 = y, y1 = y + hgt;
            float u0 = 0.5f - w, u1 = 0.5f + w;
            for (int f = 0; f < 6; f++)
            {
                if (f == 3) continue;
                int baseV = _vn;
                for (int k = 0; k < 4; k++)
                {
                    ref var v = ref _v[_vn++];
                    int ox = C[f, k, 0], oy = C[f, k, 1], oz = C[f, k, 2];
                    v.X = ox == 1 ? x1 : x0; v.Y = oy == 1 ? y1 : y0; v.Z = oz == 1 ? z1 : z0;
                    v.NX = N[f, 0]; v.NY = N[f, 1]; v.NZ = N[f, 2];
                    v.Light = light; v.Layer = layer; v.Tint = tint;
                    if (f == 2) { v.U = ox == 1 ? u1 : u0; v.V = oz == 1 ? hgt : hgt - 2 * w; }
                    else { v.U = UV[k, 0] == 1 ? u1 : u0; v.V = UV[k, 1] == 1 ? hgt : 0; }
                }
                _is[_isn++] = baseV; _is[_isn++] = baseV + 1; _is[_isn++] = baseV + 2;
                _is[_isn++] = baseV; _is[_isn++] = baseV + 2; _is[_isn++] = baseV + 3;
            }
        }
    }
}
