using System;
using System.Collections.Generic;

namespace Stratum.Core
{
    // Flowing water and lava. A source spreads sideways one level weaker a step at a time (water reaches 7 blocks,
    // lava 3) and anything that can fall, falls; a flow with nothing left feeding it dries up. Two water sources
    // side by side over something solid make a new source between them. Water meeting lava turns a lava source to
    // obsidian and a lava flow to cobblestone; lava falling on water makes stone. The single-player world and the
    // server each run one; nothing moves until a block next to a fluid changes.
    public sealed class Water
    {
        readonly Func<int, int, int, ushort> _get;
        readonly Action<int, int, int, ushort> _set;
        public Func<int, int, bool> Loaded;               // false for columns that aren't here (never flow into them)
        public float LavaEvery = 1.5f;                    // seconds between lava steps (faster in the Ember Deep)
        const float WaterEvery = 0.25f;
        const int Budget = 600;                           // updates a step, so a broken dam can't stall a frame

        readonly List<(int x, int y, int z)>[] _next = { new List<(int, int, int)>(), new List<(int, int, int)>() };
        readonly HashSet<long>[] _queued = { new HashSet<long>(), new HashSet<long>() };
        readonly List<(int x, int y, int z)> _work = new List<(int, int, int)>();
        readonly float[] _timer = new float[2];

        public Water(Func<int, int, int, ushort> get, Action<int, int, int, ushort> set) { _get = get; _set = set; }

        public bool Busy => _next[0].Count > 0 || _next[1].Count > 0 || _loose.Count > 0;

        static long Key(int x, int y, int z) => ((long)(x & 0x3FFFFFF) << 38) | ((long)(z & 0x3FFFFFF) << 12) | (long)(y & 0xFFF);

        void Queue(int x, int y, int z)
        {
            if ((uint)y >= Chunk.Height) return;
            var d = Blocks.Get(_get(x, y, z));
            if (d.Fluid == 0) return;
            int q = d.Fluid - 1;
            if (_queued[q].Add(Key(x, y, z))) _next[q].Add((x, y, z));
        }

        // Something changed at (x, y, z): the fluids there and around it take another look.
        public void Wake(int x, int y, int z)
        {
            Loose(x, y, z); Loose(x, y + 1, z);
            Queue(x, y, z);
            Queue(x + 1, y, z); Queue(x - 1, y, z);
            Queue(x, y + 1, z); Queue(x, y - 1, z);
            Queue(x, y, z + 1); Queue(x, y, z - 1);
        }

        // ---- loose blocks (sand, gravel): with nothing under them they fall, a block at a time
        readonly List<(int x, int y, int z)> _loose = new List<(int, int, int)>(), _falling = new List<(int, int, int)>();
        readonly HashSet<long> _looseQueued = new HashSet<long>();
        float _fallTimer;
        const float FallEvery = 0.07f;

        void Loose(int x, int y, int z)
        {
            if ((uint)y >= Chunk.Height || !Blocks.Get(_get(x, y, z)).Falls) return;
            if (_looseQueued.Add(Key(x, y, z))) _loose.Add((x, y, z));
        }

        static bool FallsThrough(ushort id)
        {
            if (id == 0) return true;
            var d = Blocks.Get(id);
            return d.Fluid != 0 || d.Replaceable;
        }

        void Fall(float dt)
        {
            if (_loose.Count == 0) { _fallTimer = 0; return; }
            if ((_fallTimer -= dt) > 0) return;
            _fallTimer = FallEvery;
            _falling.Clear();
            int n = Math.Min(_loose.Count, Budget);
            for (int i = 0; i < n; i++) _falling.Add(_loose[i]);
            _loose.RemoveRange(0, n);
            foreach (var p in _falling) _looseQueued.Remove(Key(p.x, p.y, p.z));
            foreach (var (x, y, z) in _falling)
            {
                ushort id = _get(x, y, z);
                if (!Blocks.Get(id).Falls || y <= 0 || !Here(x, z) || !FallsThrough(_get(x, y - 1, z))) continue;
                _set(x, y, z, 0);
                _set(x, y - 1, z, id);
                Wake(x, y, z);            // whatever rested on it follows, and water closes in
                Wake(x, y - 1, z);        // and it keeps falling
            }
        }

        public void Tick(float dt)
        {
            Fall(dt);
            for (int q = 0; q < 2; q++)
            {
                if (_next[q].Count == 0) { _timer[q] = 0; continue; }
                _timer[q] -= dt;
                if (_timer[q] > 0) continue;
                _timer[q] = q == 0 ? WaterEvery : LavaEvery;
                _work.Clear();
                int n = Math.Min(_next[q].Count, Budget);
                for (int i = 0; i < n; i++) _work.Add(_next[q][i]);
                _next[q].RemoveRange(0, n);
                foreach (var p in _work) _queued[q].Remove(Key(p.x, p.y, p.z));
                foreach (var p in _work) Update(p.x, p.y, p.z);
            }
        }

        void Set(int x, int y, int z, ushort id)
        {
            _set(x, y, z, id);
            Wake(x, y, z);
        }

        bool Here(int x, int z) => Loaded == null || Loaded(x, z);

        // Can a fluid run into a cell holding this?
        static bool Open(int fluid, ushort id)
        {
            if (id == 0) return true;
            var d = Blocks.Get(id);
            if (d.Fluid != 0) return false;
            if (d.Replaceable) return true;
            return fluid == 1 && d.Shape == Shape.Torch;     // water washes torches away
        }

        static readonly int[] DX = { 1, -1, 0, 0 }, DZ = { 0, 0, 1, -1 };

        void Update(int x, int y, int z)
        {
            var d = Blocks.Get(_get(x, y, z));
            int f = d.Fluid;
            if (f == 0) return;
            int reach = f == 1 ? Blocks.WaterReach : Blocks.LavaReach;
            var at = f == 1 ? Blocks.WaterAt : Blocks.LavaAt;
            int level = d.Level;

            // Lava touched by water hardens.
            if (f == 2)
            {
                bool wet = Blocks.IsWater(_get(x, y + 1, z));
                for (int i = 0; i < 4 && !wet; i++) wet = Blocks.IsWater(_get(x + DX[i], y, z + DZ[i]));
                if (wet) { Set(x, y, z, level == 0 ? Blocks.Obsidian : Blocks.Cobble); return; }
            }

            // A flow is only as strong as what feeds it.
            if (level > 0)
            {
                int want;
                if (Blocks.Get(_get(x, y + 1, z)).Fluid == f) want = 1;
                else
                {
                    want = 99; int sources = 0;
                    for (int i = 0; i < 4; i++)
                    {
                        int nx = x + DX[i], nz = z + DZ[i];
                        var nd = Blocks.Get(_get(nx, y, nz));
                        if (nd.Fluid != f) continue;
                        if (nd.Level == 0) sources++;
                        // A flow that can still fall feeds nothing beside it.
                        if (nd.Level == 0 || !Falls(f, nx, y, nz)) want = Math.Min(want, nd.Level + 1);
                    }
                    if (f == 1 && sources >= 2)
                    {
                        var below = Blocks.Get(_get(x, y - 1, z));
                        if (below.Solid || below.Fluid == 1 && below.Level == 0) want = 0;
                    }
                }
                if (want > reach) { Set(x, y, z, 0); return; }
                if (want != level) { Set(x, y, z, at[want]); level = want; }
            }

            // Down first.
            bool fell = false;
            if (y > 0)
            {
                ushort below = _get(x, y - 1, z);
                var bd = Blocks.Get(below);
                if (f == 2 && bd.Fluid == 1) Set(x, y - 1, z, Blocks.Stone);
                else if (f == 1 && bd.Fluid == 2) Set(x, y - 1, z, bd.Level == 0 ? Blocks.Obsidian : Blocks.Cobble);
                else if (Open(f, below) || bd.Fluid == f && bd.Level > 1) { Set(x, y - 1, z, at[1]); fell = true; }
                else if (bd.Fluid == f && bd.Level > 0) fell = true;
            }
            if (fell && level > 0) return;
            if (level >= reach) return;

            // Then sideways, one level weaker.
            int next = level + 1;
            for (int i = 0; i < 4; i++)
            {
                int nx = x + DX[i], nz = z + DZ[i];
                if (!Here(nx, nz)) continue;
                ushort nb = _get(nx, y, nz);
                var nd = Blocks.Get(nb);
                if (f == 1 && nd.Fluid == 2) { Set(nx, y, nz, nd.Level == 0 ? Blocks.Obsidian : Blocks.Cobble); continue; }
                if (Open(f, nb) || nd.Fluid == f && nd.Level > next) Set(nx, y, nz, at[next]);
            }
        }

        bool Falls(int f, int x, int y, int z)
        {
            if (y <= 0) return false;
            ushort below = _get(x, y - 1, z);
            var bd = Blocks.Get(below);
            return Open(f, below) || bd.Fluid == f && bd.Level > 0;
        }
    }
}
