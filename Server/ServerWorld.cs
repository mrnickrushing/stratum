using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using Stratum.Core;

namespace Stratum.Server
{
    // The world on disk and in memory. Chunks load (or generate) on worker threads and are handed to the game
    // loop, which alone reads and changes them. Chunk files use the same format as a single-player save, so a
    // world can move from one to the other.
    public sealed class ServerWorld
    {
        public sealed class Info
        {
            public int version { get; set; } = 1;
            public int seed { get; set; }
            public int kind { get; set; }           // WorldKind, fixed when the world is made
            public int sx { get; set; }
            public int sy { get; set; }
            public int sz { get; set; }
            public double time { get; set; } = 0.3;
            public int day { get; set; } = 1;
            public int weather { get; set; }
            public float weatherLeft { get; set; } = 900;
            public long created { get; set; }
        }

        public readonly string Dir;
        public readonly Info Meta;
        public readonly WorldGen Gen;
        public readonly Water Water;
        public readonly Growth Growth;
        public readonly Signal Signal;

        // A change the server makes itself, told to everyone (through Changed).
        public void Put(int x, int y, int z, byte id) { if (SetRaw(x, y, z, id)) { Changed?.Invoke(x, y, z, id); Water.Wake(x, y, z); Signal.Wake(x, y, z); } }

        readonly Dictionary<long, Chunk> _chunks = new Dictionary<long, Chunk>();
        readonly Dictionary<long, byte[]> _packed = new Dictionary<long, byte[]>();    // compressed copies to send
        readonly HashSet<long> _dirty = new HashSet<long>();
        readonly HashSet<long> _pending = new HashSet<long>();
        readonly BlockingCollection<long> _work = new BlockingCollection<long>();
        readonly ConcurrentQueue<Chunk> _made = new ConcurrentQueue<Chunk>();
        readonly Thread[] _workers;

        public ServerWorld(string dir, int? seed, WorldKind kind = WorldKind.Normal)
        {
            Dir = dir;
            Directory.CreateDirectory(Path.Combine(dir, "chunks"));
            Directory.CreateDirectory(Path.Combine(dir, "players"));
            var path = Path.Combine(dir, "world.json");
            if (File.Exists(path)) Meta = JsonSerializer.Deserialize<Info>(File.ReadAllText(path));
            else
            {
                Meta = new Info { seed = seed ?? new Random().Next(1, int.MaxValue), kind = (int)kind, created = DateTime.UtcNow.Ticks };
                Log.Info($"new {WorldGen.KindName(kind)} world, seed {Meta.seed}");
            }
            Gen = new WorldGen(Meta.seed, (uint)Meta.kind < (uint)WorldKind.Count ? (WorldKind)Meta.kind : WorldKind.Normal);
            if (!File.Exists(path))
            {
                var s = Gen.FindSpawn();
                Meta.sx = s.x; Meta.sy = s.y; Meta.sz = s.z;
                SaveMeta();
            }
            Water = new Water(Get, (x, y, z, id) => { SetRaw(x, y, z, id); Changed?.Invoke(x, y, z, id); }) { Loaded = IsLoaded };
            Signal = new Signal(Get, (x, y, z, id) => { if (SetRaw(x, y, z, id)) { Changed?.Invoke(x, y, z, id); Water.Wake(x, y, z); } }) { Loaded = IsLoaded };
            Growth = new Growth(Get, (x, y, z, id) => { if (SetRaw(x, y, z, id)) { Changed?.Invoke(x, y, z, id); Water.Wake(x, y, z); } }, (uint)Meta.seed ^ 0x5eed) { Loaded = IsLoaded };

            int n = Math.Clamp(Environment.ProcessorCount - 1, 1, 6);
            _workers = new Thread[n];
            for (int i = 0; i < n; i++) { _workers[i] = new Thread(Work) { IsBackground = true, Name = "chunks " + i }; _workers[i].Start(); }
        }

        // Water and anything else the world changes by itself, for the loop to tell the players.
        public event Action<int, int, int, byte> Changed;

        string ChunkPath(int x, int z) => Path.Combine(Dir, "chunks", x + "_" + z + ".bin");
        // Things players should be told (a damaged piece of the world): the world's host says them in chat.
        public readonly System.Collections.Concurrent.ConcurrentQueue<string> Notices = new System.Collections.Concurrent.ConcurrentQueue<string>();

        void Work()
        {
            foreach (long k in _work.GetConsumingEnumerable())
            {
                var c = new Chunk((int)(k >> 32), (int)k);
                try
                {
                    var path = ChunkPath(c.X, c.Z);
                    bool loaded = false;
                    if (File.Exists(path))
                    {
                        try { using var f = File.OpenRead(path); c.Read(f); loaded = true; }
                        catch (Exception e)
                        {
                            // What players built there must not be quietly replaced by fresh land: keep the damaged
                            // file, and look for the chunk in the server's backups, newest first.
                            try { File.Copy(path, path + ".bad", true); } catch { }
                            c = new Chunk(c.X, c.Z);
                            string from = Backups.Recover(path, cand => { var t = new Chunk(c.X, c.Z); using var bf = File.OpenRead(cand); t.Read(bf); c = t; });
                            if (from != null)
                            {
                                loaded = true;
                                Log.Warn($"chunk {c.X},{c.Z} was unreadable ({e.Message}); restored from {from}");
                                Notices.Enqueue($"A piece of the world near {c.X * 16 + 8}, {c.Z * 16 + 8} was damaged on disk and has been put back from a backup. Anything built there since that backup is missing.");
                                try { var tmp = path + ".tmp"; using (var wf = File.Create(tmp)) c.Write(wf); File.Move(tmp, path, true); } catch { }
                            }
                            else
                            {
                                Log.Warn($"chunk {c.X},{c.Z} is unreadable and no backup has it: {e.Message}. Fresh land is used; the damaged file is kept as {path}.bad");
                                Notices.Enqueue($"A piece of the world near {c.X * 16 + 8}, {c.Z * 16 + 8} was damaged on disk and no backup has it. It has been made fresh; the damaged file is kept on the server.");
                            }
                        }
                    }
                    if (!loaded) Gen.Generate(c);
                    c.Ready = true;
                }
                catch (Exception e) { Log.Warn($"chunk {c.X},{c.Z} failed: {e}"); c.Ready = true; }
                _made.Enqueue(c);
            }
        }

        public bool TryGet(int cx, int cz, out Chunk c) => _chunks.TryGetValue(Chunk.Key(cx, cz), out c);

        // Starts loading a chunk if it isn't here; true if it's ready now.
        public bool Want(int cx, int cz)
        {
            long k = Chunk.Key(cx, cz);
            if (_chunks.ContainsKey(k)) return true;
            if (_pending.Add(k)) _work.Add(k);
            return false;
        }

        // Chunks finished since the last call (game loop only).
        public void Collect(List<Chunk> into)
        {
            while (_made.TryDequeue(out var c))
            {
                long k = Chunk.Key(c.X, c.Z);
                _pending.Remove(k);
                _chunks[k] = c;
                into.Add(c);
            }
        }

        public byte[] Packed(Chunk c)
        {
            long k = Chunk.Key(c.X, c.Z);
            if (!_packed.TryGetValue(k, out var p)) { p = Net.Compress(c.Blocks); _packed[k] = p; }
            return p;
        }

        public byte Get(int x, int y, int z)
        {
            if ((uint)y >= Chunk.Height) return 0;
            return _chunks.TryGetValue(Chunk.Key(x >> 4, z >> 4), out var c) ? c.Blocks[Chunk.Index(x & 15, y, z & 15)] : (byte)0;
        }

        public bool IsLoaded(int x, int z) => _chunks.ContainsKey(Chunk.Key(x >> 4, z >> 4));

        // Changes a block without telling anyone (the caller does).
        public bool SetRaw(int x, int y, int z, byte id)
        {
            if ((uint)y >= Chunk.Height) return false;
            long k = Chunk.Key(x >> 4, z >> 4);
            if (!_chunks.TryGetValue(k, out var c)) return false;
            int lx = x & 15, lz = z & 15;
            byte old = c.Get(lx, y, lz);
            if (old == id) return false;
            c.Set(lx, y, lz, id);
            if (id == 0 && y == c.MaxY) c.RecalcMaxY();
            _packed.Remove(k);
            _dirty.Add(k);
            return true;
        }

        public int Loaded => _chunks.Count;
        public int Queued => _work.Count + _pending.Count;

        // Saves changed chunks; drops chunks no one is near.
        public void Maintain(Func<int, int, bool> needed, bool unload)
        {
            // A chunk that couldn't be written stays on the list (and in memory) until it can.
            _failed.Clear();
            foreach (long k in _dirty) if (_chunks.TryGetValue(k, out var c) && !SaveChunk(c)) _failed.Add(k);
            _dirty.Clear();
            foreach (long k in _failed) _dirty.Add(k);
            if (!unload) return;
            var gone = new List<long>();
            foreach (var kv in _chunks) if (!needed(kv.Value.X, kv.Value.Z) && !_dirty.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (long k in gone) { _chunks.Remove(k); _packed.Remove(k); }
        }

        readonly List<long> _failed = new List<long>();
        public int Unsaved => _dirty.Count;

        bool SaveChunk(Chunk c)
        {
            try
            {
                var path = ChunkPath(c.X, c.Z);
                var tmp = path + ".tmp";
                using (var f = File.Create(tmp)) c.Write(f);
                File.Move(tmp, path, true);
                return true;
            }
            catch (Exception e) { Log.Warn($"could not save chunk {c.X},{c.Z} (will try again): {e.Message}"); return false; }
        }

        public void SaveMeta()
        {
            var path = Path.Combine(Dir, "world.json");
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(Meta, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(path + ".tmp", path, true);
        }

        public void Stop() => _work.CompleteAdding();
    }

    // What the server remembers about each player, keyed by a hash of their account name (Accounts.KeyFor).
    public sealed class PlayerRecord
    {
        public string name { get; set; }
        public bool hasPos { get; set; }
        public float x { get; set; }
        public float y { get; set; }
        public float z { get; set; }
        public float yaw { get; set; }
        public float pitch { get; set; }
        public float health { get; set; } = 20;
        public float food { get; set; } = 20;
        public float saturation { get; set; } = 5;
        public bool hasBed { get; set; }
        public int xpLevel { get; set; }
        public int xpPoints { get; set; }
        public int bx { get; set; }
        public int by { get; set; }
        public int bz { get; set; }
        public int selected { get; set; }
        public List<SlotRecord> slots { get; set; } = new List<SlotRecord>();
        public long lastSeen { get; set; }

        public sealed class SlotRecord { public string item { get; set; } public int count { get; set; } public int wear { get; set; } }

        public static string FileFor(string dir, string key) => Path.Combine(dir, "players", key + ".json");

        public static PlayerRecord Load(string dir, string key)
        {
            var p = FileFor(dir, key);
            try { return File.Exists(p) ? JsonSerializer.Deserialize<PlayerRecord>(File.ReadAllText(p)) : null; }
            catch (Exception e) { Log.Warn($"player file {key} unreadable: {e.Message}"); return null; }
        }

        public void Save(string dir, string key)
        {
            try
            {
                var p = FileFor(dir, key);
                File.WriteAllText(p + ".tmp", JsonSerializer.Serialize(this));
                File.Move(p + ".tmp", p, true);
            }
            catch (Exception e) { Log.Warn($"could not save player {name}: {e.Message}"); }
        }

        public Stack[] Stacks()
        {
            var a = new Stack[Math.Clamp(slots.Count, Inventory.Size, Inventory.Size + 40)];
            for (int i = 0; i < a.Length && i < slots.Count; i++)
            {
                var s = slots[i];
                if (!string.IsNullOrEmpty(s.item) && s.count > 0 && Items.Get(s.item) != null) a[i] = new Stack(s.item, s.count) { Wear = s.wear };
            }
            return a;
        }

        public void SetStacks(Stack[] a)
        {
            slots.Clear();
            foreach (var s in a) slots.Add(new SlotRecord { item = s.Empty ? "" : s.Item, count = s.Empty ? 0 : s.Count, wear = s.Wear });
        }
    }

    public static class Log
    {
        static readonly object Lock = new object();
        public static bool Quiet;
        public static void Info(string s) { if (!Quiet) Write("    ", s); }
        public static void Warn(string s) => Write("WARN", s);
        static void Write(string level, string s) { lock (Lock) Console.WriteLine($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} {level} {s}"); }
    }
}
