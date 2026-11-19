using System;
using System.Collections.Generic;

namespace Stratum.Core
{
    // Water filling holes dug next to it: a breadth-first spread, a few dozen cells a tick, with a budget so an
    // opened ocean doesn't flood the world. The single-player world and the server each run one.
    public sealed class Water
    {
        readonly Func<int, int, int, byte> _get;
        readonly Action<int, int, int, byte> _set;
        readonly Queue<(int x, int y, int z)> _q = new Queue<(int, int, int)>();
        float _timer;
        int _budget;

        public Water(Func<int, int, int, byte> get, Action<int, int, int, byte> set) { _get = get; _set = set; }

        public bool Busy => _q.Count > 0;

        // Call when (x, y, z) has just become air.
        public void Wake(int x, int y, int z)
        {
            byte w = Blocks.Water;
            if (_get(x, y + 1, z) == w || _get(x + 1, y, z) == w || _get(x - 1, y, z) == w || _get(x, y, z + 1) == w || _get(x, y, z - 1) == w)
            {
                if (_q.Count == 0) _budget = 1500;
                _q.Enqueue((x, y, z));
            }
        }

        public void Tick(float dt)
        {
            if (_q.Count == 0) return;
            _timer -= dt;
            if (_timer > 0) return;
            _timer = 0.2f;
            int n = Math.Min(_q.Count, 48);
            byte w = Blocks.Water;
            for (int i = 0; i < n && _budget > 0; i++)
            {
                var (x, y, z) = _q.Dequeue();
                if (_get(x, y, z) != 0) continue;
                bool fed = _get(x, y + 1, z) == w;
                if (!fed)
                {
                    // Sideways only over something to rest on.
                    byte below = _get(x, y - 1, z);
                    bool held = below == w || Blocks.Get(below).Solid;
                    bool side = _get(x + 1, y, z) == w || _get(x - 1, y, z) == w || _get(x, y, z + 1) == w || _get(x, y, z - 1) == w;
                    fed = side && (held || y <= Chunk.SeaLevel);
                }
                if (!fed) continue;
                _set(x, y, z, w);
                _budget--;
                if (_get(x, y - 1, z) == 0) _q.Enqueue((x, y - 1, z));
                if (_get(x - 1, y, z) == 0) _q.Enqueue((x - 1, y, z));
                if (_get(x + 1, y, z) == 0) _q.Enqueue((x + 1, y, z));
                if (_get(x, y, z + 1) == 0) _q.Enqueue((x, y, z + 1));
                if (_get(x, y, z - 1) == 0) _q.Enqueue((x, y, z - 1));
            }
            if (_budget <= 0) _q.Clear();
        }
    }
}
