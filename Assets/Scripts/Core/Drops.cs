using System;
using System.Collections.Generic;

namespace Stratum.Core
{
    // One stack lying on the ground.
    public sealed class Drop
    {
        public int Id;
        public string Item; public int Count, Wear;
        public float X, Y, Z, VX, VY, VZ;
        public float Age, Wait;              // Wait: seconds before anyone may pick it up (so a thrown thing leaves the hand)
        public bool Rest;
    }

    // Things lying on the ground: thrown from a pack, fallen from a mined block, a creature or a broken chest. They
    // fall, float in water, pile together and are gone after five minutes. One copy runs in a single-player game
    // and one on a server.
    public sealed class DropSim
    {
        public const float Life = 300, PickUp = 1.7f, TakeReach = 5f;
        public const int Most = 700;

        public readonly List<Drop> All = new List<Drop>();
        readonly Dictionary<int, Drop> _byId = new Dictionary<int, Drop>();
        readonly Func<int, int, int, ushort> _get;
        public Func<int, int, bool> Loaded = (x, z) => true;
        public Func<int, int, int, bool> Burns;            // is there fire or lava here (set by whoever knows)
        int _next = 1; uint _rng = 0x9E3779B9; float _merge;

        public DropSim(Func<int, int, int, ushort> get) { _get = get; }

        float Rand() { _rng ^= _rng << 13; _rng ^= _rng >> 17; _rng ^= _rng << 5; return (_rng & 0xFFFFFF) / (float)0x1000000; }

        public bool TryGet(int id, out Drop d) => _byId.TryGetValue(id, out d);

        public Drop Toss(string item, int count, int wear, float x, float y, float z, float vx, float vy, float vz, float wait = 0.5f)
        {
            var def = Items.Get(item);
            if (def == null || count <= 0 || float.IsNaN(x + y + z + vx + vy + vz)) return null;
            if (All.Count >= Most) Remove(All[0]);
            var d = new Drop { Id = _next++, Item = item, Count = Math.Min(count, def.MaxStack), Wear = wear, X = x, Y = y, Z = z,
                VX = Math.Clamp(vx, -12, 12), VY = Math.Clamp(vy, -12, 12), VZ = Math.Clamp(vz, -12, 12), Wait = wait };
            All.Add(d); _byId[d.Id] = d;
            return d;
        }

        // Something falls out here (a mined block, loot, a chest's contents): a little hop in a random direction.
        public void Scatter(string item, int count, int wear, float x, float y, float z, float wait = 0.35f)
        {
            var def = Items.Get(item);
            if (def == null) return;
            while (count > 0)
            {
                int n = Math.Min(count, def.MaxStack); count -= n;
                float a = Rand() * 6.2832f, s = 0.6f + Rand() * 0.9f;
                Toss(item, n, wear, x, y, z, (float)Math.Cos(a) * s, 2.2f + Rand() * 1.2f, (float)Math.Sin(a) * s, wait);
            }
        }

        // A player at (px, py, pz) picks one up. Null if it's gone, too far or still leaving a hand.
        public (string item, int count, int wear)? Take(int id, float px, float py, float pz)
        {
            if (!_byId.TryGetValue(id, out var d) || d.Wait > 0) return null;
            float dx = d.X - px, dy = d.Y - (py + 0.9f), dz = d.Z - pz;
            if (dx * dx + dy * dy + dz * dz > TakeReach * TakeReach) return null;
            Remove(d);
            return (d.Item, d.Count, d.Wear);
        }

        void Remove(Drop d) { All.Remove(d); _byId.Remove(d.Id); }

        // What lies about is kept between visits (with how long each thing has lain there).
        public void SaveFile(string path)
        {
            var tmp = path + ".tmp";
            using (var w = new System.IO.BinaryWriter(System.IO.File.Create(tmp)))
            {
                w.Write(1); w.Write(All.Count);
                foreach (var d in All) { w.Write(d.Item); w.Write(d.Count); w.Write(d.Wear); w.Write(d.X); w.Write(d.Y); w.Write(d.Z); w.Write(d.Age); }
            }
            SafeFile.Swap(tmp, path);
        }

        public void LoadFile(string path)
        {
            if (!System.IO.File.Exists(path)) return;
            using var r = new System.IO.BinaryReader(System.IO.File.OpenRead(path));
            if (r.ReadInt32() != 1) return;
            int n = Math.Min(r.ReadInt32(), Most);
            for (int i = 0; i < n; i++)
            {
                string item = r.ReadString(); int count = r.ReadInt32(), wear = r.ReadInt32();
                float x = r.ReadSingle(), y = r.ReadSingle(), z = r.ReadSingle(), age = r.ReadSingle();
                var d = Toss(item, count, wear, x, y, z, 0, 0, 0, 0);
                if (d != null) d.Age = age;
            }
        }

        // What lies on a chute is drawn into it (take says how many it had room for).
        public void Feed(Func<int, int, int, string, int, int, int> take)
        {
            for (int i = All.Count - 1; i >= 0; i--)
            {
                var d = All[i];
                if (!d.Rest || d.Wait > 0) continue;
                int got = take((int)Math.Floor(d.X), (int)Math.Floor(d.Y - 0.3f), (int)Math.Floor(d.Z), d.Item, d.Count, d.Wear);
                if (got <= 0) continue;
                d.Count -= got;
                if (d.Count <= 0) Remove(d);
            }
        }
        public void Clear() { All.Clear(); _byId.Clear(); }

        bool Solid(float x, float y, float z)
        {
            if (y < 0 || y >= Chunk.Height) return false;
            ushort id = _get((int)Math.Floor(x), (int)Math.Floor(y), (int)Math.Floor(z));
            if (!Blocks.Get(id).Solid) return false;
            // Chests, chutes, slabs and the like hold things up too, as high as they really stand.
            float top = Body.TopAt(id, x - (float)Math.Floor(x), z - (float)Math.Floor(z));
            return top > 0 && y - (float)Math.Floor(y) < top;
        }

        public void Tick(float dt)
        {
            if (dt <= 0) return;
            if (dt > 0.1f) dt = 0.1f;
            for (int i = All.Count - 1; i >= 0; i--)
            {
                var d = All[i];
                d.Age += dt; if (d.Wait > 0) d.Wait -= dt;
                if (d.Age > Life || d.Y < -16) { Remove(d); continue; }
                int bx = (int)Math.Floor(d.X), by = (int)Math.Floor(d.Y), bz = (int)Math.Floor(d.Z);
                if (!Loaded(bx, bz)) continue;
                if (Burns != null && Burns(bx, by, bz)) { Remove(d); continue; }
                var here = Blocks.Get(by >= 0 && by < Chunk.Height ? _get(bx, by, bz) : (ushort)0);
                bool water = here.Fluid != 0;
                if (d.Rest && !water && Solid(d.X, d.Y - 0.05f, d.Z) && !Solid(d.X, d.Y + 0.1f, d.Z)) continue;       // lying still
                d.Rest = false;
                // Buried (something was put down on it): up and out.
                if (Solid(d.X, d.Y + 0.1f, d.Z)) { d.Y = (float)Math.Floor(d.Y) + 1.02f; d.VX = d.VY = d.VZ = 0; continue; }
                if (water) { d.VY += (1.4f - d.VY) * Math.Min(1, dt * 4); d.VX *= 1 - Math.Min(1, dt * 2); d.VZ *= 1 - Math.Min(1, dt * 2); }
                else d.VY -= 22 * dt;
                float nx = d.X + d.VX * dt, nz = d.Z + d.VZ * dt, ny = d.Y + d.VY * dt;
                if (Solid(nx, d.Y + 0.1f, d.Z)) { d.VX = -d.VX * 0.3f; nx = d.X; }
                if (Solid(nx, d.Y + 0.1f, nz)) { d.VZ = -d.VZ * 0.3f; nz = d.Z; }
                if (d.VY <= 0 && Solid(nx, ny, nz))
                {
                    // On top of what is really there: a slab is half a block high, a chest a little short of one.
                    float top = Body.TopAt(_get((int)Math.Floor(nx), (int)Math.Floor(ny), (int)Math.Floor(nz)), nx - (float)Math.Floor(nx), nz - (float)Math.Floor(nz));
                    if (top <= 0 || top > 1) top = 1;
                    ny = (float)Math.Floor(ny) + top + 0.001f;
                    if (d.VY > -2.5f) { d.VY = 0; d.VX *= 0.5f; d.VZ *= 0.5f; if (Math.Abs(d.VX) + Math.Abs(d.VZ) < 0.15f) { d.VX = d.VZ = 0; d.Rest = true; } }
                    else d.VY = -d.VY * 0.25f;
                }
                else if (d.VY > 0 && Solid(nx, ny + 0.25f, nz)) { d.VY = 0; ny = d.Y; }
                d.X = nx; d.Y = ny; d.Z = nz;
            }
            if ((_merge -= dt) > 0) return;
            _merge = 1;
            // Stacks of the same thing lying together become one.
            for (int i = 0; i < All.Count; i++)
            {
                var a = All[i];
                if (a.Wear != 0) continue;
                int max = Items.Get(a.Item)?.MaxStack ?? 1;
                if (max <= 1 || a.Count >= max) continue;
                for (int j = All.Count - 1; j > i; j--)
                {
                    var b = All[j];
                    if (b.Item != a.Item || b.Wear != 0 || a.Count + b.Count > max) continue;
                    float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
                    if (dx * dx + dy * dy + dz * dz > 1.2f * 1.2f) continue;
                    a.Count += b.Count; a.Wait = Math.Max(a.Wait, b.Wait); a.Age = Math.Min(a.Age, b.Age);
                    Remove(b);
                }
            }
        }
    }
}
