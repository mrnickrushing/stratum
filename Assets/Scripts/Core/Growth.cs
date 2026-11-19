using System;
using System.Collections.Generic;

namespace Stratum.Core
{
    // Things that change by themselves near players: wheat ripens, saplings become trees, sugar cane gets taller,
    // farmland dries out away from water, grass creeps over bare dirt. A few blocks in each nearby chunk are picked
    // at random every step and looked at, so on average a crop moves on every few minutes. Bone meal hurries it.
    public sealed class Growth
    {
        readonly Func<int, int, int, byte> _get;
        readonly Action<int, int, int, byte> _set;
        public Func<int, int, bool> Loaded;
        public int Radius = 4;                 // chunks round each player
        public int PerChunk = 192;             // blocks looked at per chunk per step
        const float Step = 0.25f;
        float _timer;
        Rng _rng;
        readonly HashSet<long> _chunks = new HashSet<long>();

        public Growth(Func<int, int, int, byte> get, Action<int, int, int, byte> set, uint seed)
        {
            _get = get; _set = set; _rng = new Rng(seed | 1);
        }

        public void Tick(float dt, IEnumerable<(float x, float z)> players)
        {
            _timer += dt;
            if (_timer < Step) return;
            _timer -= Step;
            if (_timer > Step) _timer = 0;
            _chunks.Clear();
            foreach (var (px, pz) in players)
            {
                int cx = (int)Math.Floor(px / 16), cz = (int)Math.Floor(pz / 16);
                for (int dz = -Radius; dz <= Radius; dz++)
                    for (int dx = -Radius; dx <= Radius; dx++)
                        if (dx * dx + dz * dz <= Radius * Radius + 1) _chunks.Add(Chunk.Key(cx + dx, cz + dz));
            }
            foreach (long k in _chunks)
            {
                int cx = (int)(k >> 32), cz = (int)(k & 0xFFFFFFFF);
                int bx = cx * 16, bz = cz * 16;
                if (Loaded != null && !Loaded(bx, bz)) continue;
                for (int i = 0; i < PerChunk; i++)
                {
                    uint r = _rng.Next();
                    int x = bx + (int)(r & 15), z = bz + (int)((r >> 4) & 15), y = 1 + (int)((r >> 8) % (Chunk.Height - 2));
                    byte id = _get(x, y, z);
                    if (id != 0) Tick(x, y, z, id);
                }
            }
        }

        void Tick(int x, int y, int z, byte id)
        {
            var d = Blocks.Get(id);
            if (d.BaseId == Blocks.Wheat)
            {
                var below = Blocks.Get(_get(x, y - 1, z));
                if (below.BaseId != Blocks.Farmland) { _set(x, y, z, 0); return; }
                if (d.Variant < Blocks.WheatStages - 1 && _rng.Value() < (below.Id == Blocks.FarmlandWet ? 0.34f : 0.15f))
                    _set(x, y, z, (byte)(Blocks.Wheat + d.Variant + 1));
            }
            else if (d.Grows == 2)
            {
                if (_rng.Value() < 0.14f) GrowTree(x, y, z, id);
            }
            else if (id == Blocks.SugarCane)
            {
                if (_get(x, y + 1, z) != 0) return;
                int h = 1; while (h < 4 && _get(x, y - h, z) == Blocks.SugarCane) h++;
                if (h < 3 && _rng.Value() < 0.25f) _set(x, y + 1, z, Blocks.SugarCane);
            }
            else if (d.BaseId == Blocks.Farmland)
            {
                bool wet = NearWater(x, y, z);
                var above = Blocks.Get(_get(x, y + 1, z));
                if (above.Solid) { _set(x, y, z, Blocks.Dirt); return; }
                if (wet && id != Blocks.FarmlandWet) _set(x, y, z, Blocks.FarmlandWet);
                else if (!wet && id == Blocks.FarmlandWet) _set(x, y, z, Blocks.Farmland);
                else if (!wet && above.BaseId != Blocks.Wheat && _rng.Value() < 0.3f) _set(x, y, z, Blocks.Dirt);
            }
            else if (id == Blocks.Dirt)
            {
                // Grass spreads onto dirt that sees the sky next to it.
                var above = Blocks.Get(_get(x, y + 1, z));
                if (above.Opaque || above.Fluid != 0) return;
                for (int i = 0; i < 4; i++)
                {
                    uint r = _rng.Next();
                    int nx = x + (int)(r % 3) - 1, ny = y + (int)((r >> 4) % 3) - 1, nz = z + (int)((r >> 8) % 3) - 1;
                    if (_get(nx, ny, nz) == Blocks.Grass) { _set(x, y, z, Blocks.Grass); return; }
                }
            }
            else if (id == Blocks.Grass)
            {
                var above = Blocks.Get(_get(x, y + 1, z));
                if (above.Opaque && above.Shape == Shape.Cube) _set(x, y, z, Blocks.Dirt);
            }
        }

        // Water within four blocks across (at the farmland's level or one up) keeps it wet.
        bool NearWater(int x, int y, int z)
        {
            for (int dy = 0; dy <= 1; dy++)
                for (int dz = -4; dz <= 4; dz++)
                    for (int dx = -4; dx <= 4; dx++)
                        if (Blocks.IsWater(_get(x + dx, y + dy, z + dz))) return true;
            return false;
        }

        // Bone meal on a block: true if it did something (and the bone meal is used up).
        public bool BoneMeal(int x, int y, int z)
        {
            byte id = _get(x, y, z);
            var d = Blocks.Get(id);
            if (d.BaseId == Blocks.Wheat)
            {
                if (d.Variant >= Blocks.WheatStages - 1) return false;
                int to = Math.Min(Blocks.WheatStages - 1, d.Variant + 1 + _rng.Range(0, 2));
                _set(x, y, z, (byte)(Blocks.Wheat + to));
                return true;
            }
            if (d.Grows == 2)
            {
                if (_rng.Value() < 0.45f) GrowTree(x, y, z, id);
                return true;
            }
            if (id == Blocks.Grass)
            {
                // A spray of grass and the odd flower round about.
                for (int i = 0; i < 40; i++)
                {
                    int nx = x + _rng.Range(-3, 4), nz = z + _rng.Range(-3, 4), ny = y + _rng.Range(-1, 2);
                    if (_get(nx, ny, nz) != Blocks.Grass || _get(nx, ny + 1, nz) != 0) continue;
                    float r = _rng.Value();
                    _set(nx, ny + 1, nz, r < 0.08f ? Blocks.RedFlower : r < 0.16f ? Blocks.YellowFlower : r < 0.2f ? Blocks.BlueFlower : Blocks.TallGrass);
                }
                return true;
            }
            return false;
        }

        // A sapling becomes a tree of its kind if there's room for one.
        public bool GrowTree(int x, int y, int z, byte sapling)
        {
            bool pine = sapling == Blocks.SaplingPine, birch = sapling == Blocks.SaplingBirch;
            int h = pine ? _rng.Range(7, 11) : birch ? _rng.Range(5, 8) : _rng.Range(4, 7);
            for (int i = 1; i <= h + 1; i++)
            {
                var b = Blocks.Get(_get(x, y + i, z));
                if (b.Solid && !b.Sway) return false;
                if (y + i >= Chunk.Height - 1) return false;
            }
            byte log = pine ? Blocks.PineLog : birch ? Blocks.BirchLog : Blocks.OakLog;
            byte leaves = pine ? Blocks.PineNeedles : birch ? Blocks.BirchLeaves : Blocks.OakLeaves;
            int top = y + h;
            if (pine)
            {
                for (int ly = top + 1; ly >= y + 2; ly--)
                {
                    int ring = top + 1 - ly;
                    int r = ring == 0 ? 0 : ring == 1 ? 1 : (ring % 2 == 0 ? Math.Min(3, 1 + ring / 3) : Math.Max(1, Math.Min(3, ring / 3)));
                    for (int dz = -r; dz <= r; dz++)
                        for (int dx = -r; dx <= r; dx++)
                            if (dx * dx + dz * dz <= r * r + 1) Leaf(x + dx, ly, z + dz, leaves);
                }
            }
            else
            {
                for (int ly = top - 3; ly <= top + 1; ly++)
                {
                    int r = ly >= top ? 1 : 2;
                    for (int dz = -r; dz <= r; dz++)
                        for (int dx = -r; dx <= r; dx++)
                        {
                            bool corner = Math.Abs(dx) == r && Math.Abs(dz) == r;
                            if (corner && (ly == top + 1 || _rng.Value() < 0.6f)) continue;
                            Leaf(x + dx, ly, z + dz, leaves);
                        }
                }
            }
            for (int ly = y; ly < top; ly++) _set(x, ly, z, log);
            if (_get(x, y - 1, z) == Blocks.Grass) _set(x, y - 1, z, Blocks.Dirt);
            return true;
        }

        void Leaf(int x, int y, int z, byte leaves)
        {
            byte cur = _get(x, y, z);
            if (cur == 0 || Blocks.Get(cur).Replaceable && Blocks.Get(cur).Fluid == 0) _set(x, y, z, leaves);
        }
    }
}
