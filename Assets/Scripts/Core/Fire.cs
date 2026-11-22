using System;
using System.Collections.Generic;

namespace Stratum.Core
{
    // Fire: each flame looks round it every second or so. It eats what burns beside it (leaving more fire), jumps
    // to open air next to something that burns, lights a blast keg, and goes out when water reaches it or there's
    // nothing left to burn. In a world where fire doesn't spread it only burns where it was lit, and dies.
    public sealed class Fire
    {
        readonly Func<int, int, int, ushort> _get;
        readonly Action<int, int, int, ushort> _set;
        public Func<int, int, bool> Loaded;
        public bool Spreads = true;
        public Action<int, int, int, BlockDef> Burnt;         // a block was eaten
        public const int Most = 500;

        sealed class Flame { public int X, Y, Z, Age; public float Next; }
        readonly Dictionary<long, Flame> _flames = new Dictionary<long, Flame>();
        readonly List<Flame> _work = new List<Flame>();
        float _clock; uint _rng = 0xF12E5EED;
        static readonly int[] DX = { 1, -1, 0, 0, 0, 0 }, DY = { 0, 0, 1, -1, 0, 0 }, DZ = { 0, 0, 0, 0, 1, -1 };

        public Fire(Func<int, int, int, ushort> get, Action<int, int, int, ushort> set) { _get = get; _set = set; }

        public int Count => _flames.Count;
        float Rand() { _rng ^= _rng << 13; _rng ^= _rng >> 17; _rng ^= _rng << 5; return (_rng & 0xFFFFFF) / (float)0x1000000; }
        static long Key(int x, int y, int z) => ((long)(x & 0x3FFFFFF) << 38) | ((long)(z & 0x3FFFFFF) << 12) | (long)(y & 0xFFF);

        static bool Open(ushort id) => id == 0 || Blocks.Get(id).Replaceable && Blocks.Get(id).Fluid == 0 && id != Blocks.Fire;

        // A fire block is here (just lit, or found burning when the land loaded): keep an eye on it.
        public void Found(int x, int y, int z)
        {
            long k = Key(x, y, z);
            if (_flames.ContainsKey(k) || _flames.Count >= Most) return;
            _flames[k] = new Flame { X = x, Y = y, Z = z, Next = _clock + 0.6f + Rand() };
        }

        // Light a fire in this cell if it's open. False if something is in the way.
        public bool Light(int x, int y, int z)
        {
            if ((uint)y >= Chunk.Height || !Open(_get(x, y, z)) || _flames.Count >= Most) return false;
            _set(x, y, z, Blocks.Fire);
            Found(x, y, z);
            return true;
        }

        public void Tick(float dt)
        {
            _clock += dt;
            if (_flames.Count == 0) return;
            _work.Clear();
            foreach (var f in _flames.Values) if (f.Next <= _clock) _work.Add(f);
            foreach (var f in _work)
            {
                long k = Key(f.X, f.Y, f.Z);
                if (Loaded != null && !Loaded(f.X, f.Z)) { f.Next = _clock + 2; continue; }
                if (_get(f.X, f.Y, f.Z) != Blocks.Fire) { _flames.Remove(k); continue; }
                f.Next = _clock + 0.7f + Rand() * 0.9f; f.Age++;
                bool fuel = false, wet = false, forever = false;
                for (int d = 0; d < 6; d++)
                {
                    int x = f.X + DX[d], y = f.Y + DY[d], z = f.Z + DZ[d];
                    if ((uint)y >= Chunk.Height) continue;
                    ushort id = _get(x, y, z); var def = Blocks.Get(id);
                    if (def.Fluid == 1) wet = true;
                    if (id == Blocks.BlastKeg) _set(x, y, z, Blocks.KegLit);
                    if (d == 3 && (id == Blocks.Cinderstone || id == Blocks.Hearth)) forever = true;
                    if (!def.Burns) continue;
                    fuel = true;
                    if (!Spreads || Rand() > 0.22f) continue;
                    // Eaten: usually more fire where it was.
                    Burnt?.Invoke(x, y, z, def);
                    if (Rand() < 0.65f && _flames.Count < Most) { _set(x, y, z, Blocks.Fire); Found(x, y, z); }
                    else _set(x, y, z, 0);
                }
                // Nothing to stand on or burn: out (fire in the air above burnt wood dies fast).
                var below = Blocks.Get(f.Y > 0 ? _get(f.X, f.Y - 1, f.Z) : (ushort)0);
                if (wet || !forever && !fuel && (f.Age > 3 + (int)(Rand() * 5) || !below.Solid) || !forever && fuel && f.Age > 14 && Rand() < 0.15f)
                {
                    _set(f.X, f.Y, f.Z, 0); _flames.Remove(k); continue;
                }
                if (!Spreads || !fuel) continue;
                // Jump to open air beside something that burns.
                for (int tries = 0; tries < 3; tries++)
                {
                    int x = f.X + (int)(Rand() * 3) - 1, y = f.Y + (int)(Rand() * 4) - 1, z = f.Z + (int)(Rand() * 3) - 1;
                    if ((uint)y >= Chunk.Height || !Open(_get(x, y, z)) || Rand() > 0.3f) continue;
                    bool near = false;
                    for (int d = 0; d < 6 && !near; d++) { int yy = y + DY[d]; if ((uint)yy < Chunk.Height && Blocks.Get(_get(x + DX[d], yy, z + DZ[d])).Burns) near = true; }
                    if (near) Light(x, y, z);
                }
            }
        }
    }

    // Blasts: kegs with a fuse burning, and what an explosion does to the blocks round it.
    public sealed class Blasts
    {
        readonly Func<int, int, int, ushort> _get;
        readonly Action<int, int, int, ushort> _set;
        public Func<int, int, bool> Loaded;
        public bool BreaksBlocks = true;
        public Action<float, float, float, float> Boom;       // where and how strong: the host hurts who is near and tells the players
        public Action<int, int, int, BlockDef> Broke;         // a block was blown out (a chest's contents, the odd drop)
        public Action<int, int, int> Lit;                     // a keg's fuse started (for the hiss)
        readonly List<(int x, int y, int z, float at)> _fuses = new List<(int, int, int, float)>();
        float _clock; uint _rng = 0xB00B1E55;
        public const float KegPower = 4, Fuse = 4;

        public Blasts(Func<int, int, int, ushort> get, Action<int, int, int, ushort> set) { _get = get; _set = set; }

        float Rand() { _rng ^= _rng << 13; _rng ^= _rng >> 17; _rng ^= _rng << 5; return (_rng & 0xFFFFFF) / (float)0x1000000; }

        // A lit keg is here: it goes off after its fuse.
        public void Prime(int x, int y, int z, float fuse = Fuse)
        {
            foreach (var f in _fuses) if (f.x == x && f.y == y && f.z == z) return;
            _fuses.Add((x, y, z, _clock + fuse));
            Lit?.Invoke(x, y, z);
        }

        public void Tick(float dt)
        {
            _clock += dt;
            for (int i = _fuses.Count - 1; i >= 0; i--)
            {
                var (x, y, z, at) = _fuses[i];
                if (at > _clock) continue;
                _fuses.RemoveAt(i);
                if (_get(x, y, z) != Blocks.KegLit) continue;
                _set(x, y, z, 0);
                Explode(x + 0.5f, y + 0.5f, z + 0.5f, KegPower);
            }
        }

        public void Explode(float fx, float fy, float fz, float power)
        {
            int r = (int)Math.Ceiling(power * 1.3f);
            int cx = (int)Math.Floor(fx), cy = (int)Math.Floor(fy), cz = (int)Math.Floor(fz);
            if (BreaksBlocks)
                for (int y = -r; y <= r; y++)
                    for (int z = -r; z <= r; z++)
                        for (int x = -r; x <= r; x++)
                        {
                            int wx = cx + x, wy = cy + y, wz = cz + z;
                            if (wy < 1 || wy >= Chunk.Height || Loaded != null && !Loaded(wx, wz)) continue;
                            ushort id = _get(wx, wy, wz);
                            if (id == 0) continue;
                            var d = Blocks.Get(id);
                            float dist = (float)Math.Sqrt(x * x + y * y + z * z);
                            if (id == Blocks.BlastKeg || id == Blocks.KegLit)
                            {
                                if (dist <= power * 1.3f) { if (id == Blocks.BlastKeg) _set(wx, wy, wz, Blocks.KegLit); Prime(wx, wy, wz, 0.4f + Rand() * 0.9f); }
                                continue;
                            }
                            if (d.Fluid != 0 || d.Hardness < 0 || d.Hardness >= 15) continue;       // water, bedrock and obsidian stand
                            float reach = power * (0.72f + Rand() * 0.5f) - Math.Max(0, d.Hardness) * 0.12f;
                            if (dist > reach) continue;
                            _set(wx, wy, wz, 0);
                            Broke?.Invoke(wx, wy, wz, d);
                        }
            Boom?.Invoke(fx, fy, fz, power);
        }
    }
}
