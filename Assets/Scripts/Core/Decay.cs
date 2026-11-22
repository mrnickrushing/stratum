using System;
using System.Collections.Generic;

namespace Stratum.Core
{
    // Leaves without a trunk: when a log is cut, the leaves round it that no log holds up any more fall away over
    // the next few seconds (leaving saplings and the odd apple). Leaves nowhere near a cut log are never touched,
    // so a hedge or a roof built of leaves stays.
    public sealed class Decay
    {
        readonly Func<int, int, int, ushort> _get;
        readonly Action<int, int, int, ushort> _set;
        public Action<int, int, int, BlockDef> Fell;         // a leaf block went: what it was, for whoever hands out saplings
        readonly List<(int x, int y, int z, float at)> _due = new List<(int, int, int, float)>();
        float _clock; uint _rng = 0xC0FFEE11;
        const int R = 6, Hold = 5;                            // how far round the cut to look; how far a log holds leaves

        public Decay(Func<int, int, int, ushort> get, Action<int, int, int, ushort> set) { _get = get; _set = set; }

        public static bool IsLog(BlockDef d) => d.Id != 0 && Blocks.Keys[d.BaseId].EndsWith("_log", StringComparison.Ordinal);
        public static bool IsLeaf(BlockDef d) => d.Sound == Sound.Leaves && d.Shape == Shape.Cube && d.Cutout;

        float Rand() { _rng ^= _rng << 13; _rng ^= _rng >> 17; _rng ^= _rng << 5; return (_rng & 0xFFFFFF) / (float)0x1000000; }

        // A log at (cx, cy, cz) has just gone.
        public void LogBroken(int cx, int cy, int cz)
        {
            int n = R * 2 + 1 + Hold * 2;                     // logs just outside the box still hold leaves inside it
            int o = R + Hold;
            var dist = new byte[n * n * n];
            var q = new Queue<int>();
            int At(int x, int y, int z) => (x + o) + (z + o) * n + (y + o) * n * n;
            var kind = new byte[n * n * n];                   // 1 leaf, 2 log
            for (int y = -o; y <= o; y++)
                for (int z = -o; z <= o; z++)
                    for (int x = -o; x <= o; x++)
                    {
                        int wy = cy + y;
                        if ((uint)wy >= Chunk.Height) continue;
                        var d = Blocks.Get(_get(cx + x, wy, cz + z));
                        int i = At(x, y, z);
                        if (IsLog(d)) { kind[i] = 2; dist[i] = 1; q.Enqueue(i); }
                        else if (IsLeaf(d)) kind[i] = 1;
                    }
            while (q.Count > 0)
            {
                int i = q.Dequeue(); int d = dist[i];
                if (d > Hold) continue;
                int x = i % n, z = i / n % n, y = i / (n * n);
                void Try(int xx, int yy, int zz)
                {
                    if ((uint)xx >= n || (uint)yy >= n || (uint)zz >= n) return;
                    int j = xx + zz * n + yy * n * n;
                    if (kind[j] != 1 || dist[j] != 0) return;
                    dist[j] = (byte)(d + 1); q.Enqueue(j);
                }
                Try(x + 1, y, z); Try(x - 1, y, z); Try(x, y + 1, z); Try(x, y - 1, z); Try(x, y, z + 1); Try(x, y, z - 1);
            }
            for (int y = -R; y <= R; y++)
                for (int z = -R; z <= R; z++)
                    for (int x = -R; x <= R; x++)
                    {
                        int i = At(x, y, z);
                        if (kind[i] == 1 && dist[i] == 0) _due.Add((cx + x, cy + y, cz + z, _clock + 0.6f + Rand() * 7));
                    }
        }

        public void Tick(float dt)
        {
            _clock += dt;
            if (_due.Count == 0) return;
            for (int i = _due.Count - 1; i >= 0; i--)
            {
                var (x, y, z, at) = _due[i];
                if (at > _clock) continue;
                _due.RemoveAt(i);
                var d = Blocks.Get(_get(x, y, z));
                if (!IsLeaf(d)) continue;
                _set(x, y, z, 0);
                Fell?.Invoke(x, y, z, d);
            }
        }
    }
}
