using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Stratum.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Stratum.Game
{
    // Streams chunks around a point: worker threads generate (or load) and mesh them, the main thread uploads
    // the meshes. Block edits remesh the chunk and whichever neighbours its light reaches.
    public sealed class VoxelWorld : MonoBehaviour
    {
        sealed class Slot
        {
            public Chunk Chunk;
            public GameObject Go;
            public Mesh Mesh;
            public bool GenQueued, MeshQueued, Dirty = true, HasMesh;
        }

        abstract class Job { public int Priority; }
        sealed class GenJob : Job { public Slot S; }
        sealed class MeshJob : Job { public Slot S; public Chunk[] N; }

        public WorldGen Gen { get; private set; }
        public string SaveDir { get; private set; }
        public int ViewDistance = 10;
        public Material BlockMaterial, WaterMaterial;

        readonly Dictionary<long, Slot> _slots = new Dictionary<long, Slot>();
        readonly List<Job> _jobs = new List<Job>();
        readonly object _lock = new object();
        readonly ConcurrentQueue<(Slot s, MeshData md)> _done = new ConcurrentQueue<(Slot, MeshData)>();
        Thread[] _workers;
        volatile bool _stop;
        readonly AutoResetEvent _wake = new AutoResetEvent(false);
        int _cx = int.MinValue, _cz = int.MinValue;
        Vector3 _focus;
        float _rescan;

        static readonly VertexAttributeDescriptor[] Layout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.UNorm8, 4),
        };

        public static VoxelWorld Create(int seed, string saveDir, Material block, Material water, int viewDistance)
        {
            var go = new GameObject("World");
            var w = go.AddComponent<VoxelWorld>();
            w.Gen = new WorldGen(seed);
            w.SaveDir = saveDir;
            w.BlockMaterial = block; w.WaterMaterial = water;
            w.ViewDistance = viewDistance;
            if (saveDir != null) Directory.CreateDirectory(Path.Combine(saveDir, "chunks"));
            w.StartWorkers();
            return w;
        }

        void StartWorkers()
        {
            int n = Mathf.Clamp(SystemInfo.processorCount - 2, 2, 8);
            _workers = new Thread[n];
            for (int i = 0; i < n; i++)
            {
                _workers[i] = new Thread(Work) { IsBackground = true, Name = "chunk worker " + i, Priority = System.Threading.ThreadPriority.BelowNormal };
                _workers[i].Start();
            }
        }

        [ThreadStatic] static Mesher _mesher;

        void Work()
        {
            while (!_stop)
            {
                Job job = null;
                lock (_lock)
                {
                    int best = -1;
                    for (int i = 0; i < _jobs.Count; i++) if (best < 0 || _jobs[i].Priority < _jobs[best].Priority) best = i;
                    if (best >= 0) { job = _jobs[best]; _jobs[best] = _jobs[_jobs.Count - 1]; _jobs.RemoveAt(_jobs.Count - 1); }
                }
                if (job == null) { _wake.WaitOne(50); continue; }
                try
                {
                    if (job is GenJob g) Generate(g.S.Chunk);
                    else if (job is MeshJob m)
                    {
                        _mesher ??= new Mesher();
                        var md = _mesher.Build(m.N);
                        _done.Enqueue((m.S, md));
                    }
                }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        string ChunkPath(int x, int z) => Path.Combine(SaveDir, "chunks", x + "_" + z + ".bin");

        void Generate(Chunk c)
        {
            if (SaveDir != null)
            {
                var path = ChunkPath(c.X, c.Z);
                if (File.Exists(path))
                {
                    try
                    {
                        using (var f = File.OpenRead(path)) c.Read(f);
                        Gen.TintOnly(c);
                        c.Modified = true;
                        c.Ready = true;
                        return;
                    }
                    catch (Exception e) { Debug.LogWarning("chunk " + path + " unreadable, regenerating: " + e.Message); }
                }
            }
            Gen.Generate(c);
        }

        void Enqueue(Job j)
        {
            lock (_lock) _jobs.Add(j);
            _wake.Set();
        }

        public void SetFocus(Vector3 p) => _focus = p;

        int Dist2(int x, int z) { int dx = x - _cx, dz = z - _cz; return dx * dx + dz * dz; }

        // Priority: near first, and chunks in front of the camera before those behind.
        int PriorityFor(int x, int z)
        {
            int d = Dist2(x, z) * 16;
            var cam = Camera.main;
            if (cam != null)
            {
                var f = cam.transform.forward; f.y = 0;
                var to = new Vector3(x * 16 + 8 - _focus.x, 0, z * 16 + 8 - _focus.z);
                if (to.sqrMagnitude > 1 && f.sqrMagnitude > 0.01f && Vector3.Dot(f.normalized, to.normalized) < 0) d += 40;
            }
            return d;
        }

        void Update()
        {
            int cx = Mathf.FloorToInt(_focus.x / 16f), cz = Mathf.FloorToInt(_focus.z / 16f);
            _rescan -= Time.unscaledDeltaTime;
            if (cx != _cx || cz != _cz || _rescan <= 0)
            {
                _cx = cx; _cz = cz; _rescan = 0.25f;
                Schedule();
            }
            Upload();
            FluidTick();
        }

        void Schedule()
        {
            int R = ViewDistance;
            // Generate one ring beyond what is drawn, so every drawn chunk has its neighbours.
            for (int dz = -R - 1; dz <= R + 1; dz++)
                for (int dx = -R - 1; dx <= R + 1; dx++)
                {
                    if (dx * dx + dz * dz > (R + 1.5f) * (R + 1.5f)) continue;
                    int x = _cx + dx, z = _cz + dz;
                    long k = Chunk.Key(x, z);
                    if (!_slots.TryGetValue(k, out var s)) { s = new Slot { Chunk = new Chunk(x, z) }; _slots[k] = s; }
                    if (!s.GenQueued) { s.GenQueued = true; Enqueue(new GenJob { S = s, Priority = PriorityFor(x, z) }); }
                }
            for (int dz = -R; dz <= R; dz++)
                for (int dx = -R; dx <= R; dx++)
                {
                    if (dx * dx + dz * dz > (R + 0.5f) * (R + 0.5f)) continue;
                    int x = _cx + dx, z = _cz + dz;
                    if (_slots.TryGetValue(Chunk.Key(x, z), out var s) && s.Dirty && !s.MeshQueued && s.Chunk.Ready) TryMesh(s, PriorityFor(x, z));
                }
            // Drop chunks well outside the view.
            List<long> gone = null;
            foreach (var kv in _slots)
            {
                var c = kv.Value.Chunk;
                int dx = c.X - _cx, dz = c.Z - _cz;
                if (dx * dx + dz * dz > (R + 4) * (R + 4) && !kv.Value.MeshQueued && (c.Ready || !kv.Value.GenQueued))
                    (gone ??= new List<long>()).Add(kv.Key);
            }
            if (gone != null)
                foreach (var k in gone)
                {
                    var s = _slots[k];
                    if (s.Chunk.Modified) SaveChunk(s.Chunk);
                    if (s.Go != null) Destroy(s.Go);
                    if (s.Mesh != null) Destroy(s.Mesh);
                    _slots.Remove(k);
                }
        }

        bool TryMesh(Slot s, int priority)
        {
            var n = new Chunk[9];
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (!_slots.TryGetValue(Chunk.Key(s.Chunk.X + dx, s.Chunk.Z + dz), out var o) || !o.Chunk.Ready) return false;
                    n[(dz + 1) * 3 + dx + 1] = o.Chunk;
                }
            s.Dirty = false; s.MeshQueued = true;
            Enqueue(new MeshJob { S = s, N = n, Priority = priority });
            return true;
        }

        void Upload()
        {
            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < 0.006f && _done.TryDequeue(out var r))
            {
                var s = r.s; var md = r.md;
                s.MeshQueued = false;
                if (!_slots.TryGetValue(Chunk.Key(s.Chunk.X, s.Chunk.Z), out var cur) || cur != s) { md.Release(); continue; }
                Apply(s, md);
                md.Release();
                if (s.Dirty) TryMesh(s, 0);
            }
        }

        void Apply(Slot s, MeshData md)
        {
            if (s.Go == null)
            {
                s.Go = new GameObject("chunk " + s.Chunk.X + "," + s.Chunk.Z);
                s.Go.transform.SetParent(transform, false);
                s.Go.transform.position = new Vector3(s.Chunk.X * 16, 0, s.Chunk.Z * 16);
                s.Go.AddComponent<MeshFilter>();
                var mr = s.Go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = new[] { BlockMaterial, WaterMaterial };
                mr.shadowCastingMode = ShadowCastingMode.On;
                mr.receiveShadows = true;
                mr.lightProbeUsage = LightProbeUsage.Off;
                mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                s.Mesh = new Mesh { name = s.Go.name };
                s.Mesh.MarkDynamic();
                s.Go.GetComponent<MeshFilter>().sharedMesh = s.Mesh;
            }
            var m = s.Mesh;
            const MeshUpdateFlags F = MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontNotifyMeshUsers;
            m.Clear();
            m.SetVertexBufferParams(md.VertCount, Layout);
            m.SetVertexBufferData(md.Verts, 0, 0, md.VertCount, 0, F);
            int total = md.SolidCount + md.WaterCount;
            m.SetIndexBufferParams(total, IndexFormat.UInt32);
            m.SetIndexBufferData(md.Index, 0, 0, total, F);
            m.subMeshCount = 2;
            m.SetSubMesh(0, new SubMeshDescriptor(0, md.SolidCount), F);
            m.SetSubMesh(1, new SubMeshDescriptor(md.SolidCount, md.WaterCount), F);
            float y0 = md.MinY - 1, y1 = md.MaxY + 1;
            m.bounds = new Bounds(new Vector3(8, (y0 + y1) / 2, 8), new Vector3(18, y1 - y0 + 1, 18));
            s.HasMesh = true;
            s.Go.SetActive(md.VertCount > 0);
        }

        // ------------------------------------------------------------------ blocks

        Slot SlotAt(int x, int z) => _slots.TryGetValue(Chunk.Key(x >> 4, z >> 4), out var s) ? s : null;

        public bool IsReady(int x, int z) { var s = SlotAt(x, z); return s != null && s.Chunk.Ready; }

        public bool IsMeshed(int x, int z) { var s = SlotAt(x, z); return s != null && s.HasMesh; }

        public byte Get(int x, int y, int z)
        {
            if ((uint)y >= Chunk.Height) return 0;
            var s = SlotAt(x, z);
            if (s == null || !s.Chunk.Ready) return 0;
            return s.Chunk.Blocks[(x & 15) | ((z & 15) << 4) | (y << 8)];
        }

        // For collision: a chunk that isn't there yet counts as solid, so nothing falls out of the world.
        public bool SolidAt(int x, int y, int z)
        {
            if (y < 0) return true;
            if (y >= Chunk.Height) return false;
            var s = SlotAt(x, z);
            if (s == null || !s.Chunk.Ready) return true;
            return Blocks.Get(s.Chunk.Blocks[(x & 15) | ((z & 15) << 4) | (y << 8)]).Solid;
        }

        public event Action<int, int, int, byte, byte> Changed;

        public bool Set(int x, int y, int z, byte id)
        {
            if ((uint)y >= Chunk.Height) return false;
            var s = SlotAt(x, z);
            if (s == null || !s.Chunk.Ready) return false;
            int lx = x & 15, lz = z & 15;
            byte old = s.Chunk.Get(lx, y, lz);
            if (old == id) return false;
            s.Chunk.Set(lx, y, lz, id);
            if (id == 0 && y == s.Chunk.MaxY) s.Chunk.RecalcMaxY();
            s.Chunk.Modified = true;
            // Light reaches 15 blocks, so the neighbours need a fresh mesh too.
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (_slots.TryGetValue(Chunk.Key(s.Chunk.X + dx, s.Chunk.Z + dz), out var o))
                    {
                        o.Dirty = true;
                        if (!o.MeshQueued) TryMesh(o, dx == 0 && dz == 0 ? -1000 : -900);
                    }
                }
            Changed?.Invoke(x, y, z, old, id);
            if (id == 0) WakeWater(x, y, z);
            return true;
        }

        // ------------------------------------------------------------------ water filling holes

        readonly Queue<Vector3Int> _water = new Queue<Vector3Int>();
        float _waterTimer;
        int _waterBudget;

        void WakeWater(int x, int y, int z)
        {
            if (Get(x, y + 1, z) == Blocks.Water || Get(x + 1, y, z) == Blocks.Water || Get(x - 1, y, z) == Blocks.Water ||
                Get(x, y, z + 1) == Blocks.Water || Get(x, y, z - 1) == Blocks.Water)
            {
                if (_water.Count == 0) _waterBudget = 1500;
                _water.Enqueue(new Vector3Int(x, y, z));
            }
        }

        void FluidTick()
        {
            if (_water.Count == 0) return;
            _waterTimer -= Time.deltaTime;
            if (_waterTimer > 0) return;
            _waterTimer = 0.2f;
            int n = Math.Min(_water.Count, 48);
            for (int i = 0; i < n && _waterBudget > 0; i++)
            {
                var p = _water.Dequeue();
                if (Get(p.x, p.y, p.z) != 0) continue;
                bool fed = Get(p.x, p.y + 1, p.z) == Blocks.Water;
                if (!fed)
                {
                    // Sideways only over something to rest on.
                    byte below = Get(p.x, p.y - 1, p.z);
                    bool held = below == Blocks.Water || Blocks.Get(below).Solid;
                    bool side = Get(p.x + 1, p.y, p.z) == Blocks.Water || Get(p.x - 1, p.y, p.z) == Blocks.Water ||
                                Get(p.x, p.y, p.z + 1) == Blocks.Water || Get(p.x, p.y, p.z - 1) == Blocks.Water;
                    fed = side && (held || p.y <= Chunk.SeaLevel);
                }
                if (!fed) continue;
                var s = SlotAt(p.x, p.z);
                if (s == null || !s.Chunk.Ready) continue;
                s.Chunk.Set(p.x & 15, p.y, p.z & 15, Blocks.Water);
                s.Chunk.Modified = true;
                MarkDirty(p.x, p.z);
                _waterBudget--;
                foreach (var d in new[] { Vector3Int.down, Vector3Int.left, Vector3Int.right, new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1) })
                {
                    var q = p + d;
                    if (Get(q.x, q.y, q.z) == 0) _water.Enqueue(q);
                }
            }
            if (_waterBudget <= 0) _water.Clear();
        }

        void MarkDirty(int x, int z)
        {
            int cx = x >> 4, cz = z >> 4;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                    if (_slots.TryGetValue(Chunk.Key(cx + dx, cz + dz), out var o))
                    {
                        o.Dirty = true;
                        if (!o.MeshQueued) TryMesh(o, -800);
                    }
        }

        // ------------------------------------------------------------------ saving

        void SaveChunk(Chunk c)
        {
            if (SaveDir == null) return;
            try
            {
                var path = ChunkPath(c.X, c.Z);
                var tmp = path + ".tmp";
                using (var f = File.Create(tmp)) c.Write(f);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch (Exception e) { Debug.LogWarning("could not save chunk: " + e.Message); }
        }

        public void SaveAll()
        {
            foreach (var s in _slots.Values) if (s.Chunk.Ready && s.Chunk.Modified) SaveChunk(s.Chunk);
        }

        public int LoadedChunks => _slots.Count;
        public int PendingJobs { get { lock (_lock) return _jobs.Count; } }

        // Fraction of the chunks within `radius` of the focus that have a mesh (for the loading screen).
        public float Progress(int radius)
        {
            int want = 0, have = 0;
            int cx = Mathf.FloorToInt(_focus.x / 16f), cz = Mathf.FloorToInt(_focus.z / 16f);
            for (int dz = -radius; dz <= radius; dz++)
                for (int dx = -radius; dx <= radius; dx++)
                {
                    want++;
                    if (_slots.TryGetValue(Chunk.Key(cx + dx, cz + dz), out var s) && s.HasMesh) have++;
                }
            return want == 0 ? 1 : have / (float)want;
        }

        void OnDestroy()
        {
            _stop = true;
            _wake.Set();
            if (_workers != null) foreach (var t in _workers) t.Join(500);
            foreach (var s in _slots.Values) if (s.Mesh != null) Destroy(s.Mesh);
            while (_done.TryDequeue(out var r)) r.md.Release();
        }
    }
}
