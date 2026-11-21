using System;
using System.Collections.Generic;
using System.IO;

namespace Stratum.Core
{
    // What's inside each chest and lost satchel in a world, by position. Kept in <world>/containers.bin, the same
    // file in a single-player save and on a server.
    public sealed class Containers
    {
        public const int ChestSlots = 27;
        public const int MaxSlots = 200;         // the most a satchel grows to
        public const int SignChars = 90;
        const int Version = 2;
        readonly Dictionary<long, Stack[]> _boxes = new Dictionary<long, Stack[]>();
        // What's written on each sign (kept in the same file).
        public readonly Dictionary<long, string> Signs = new Dictionary<long, string>();
        public bool Dirty;

        public static (int x, int y, int z) Unkey(long k)
        {
            int x = (int)(k >> 38), z = (int)((k >> 12) & 0x3FFFFFF), y = (int)(k & 0xFFF);
            x = (x << 6) >> 6; z = (z << 6) >> 6;               // sign-extend the 26-bit fields
            return (x, y, z);
        }

        public static string CleanSign(string t)
        {
            if (t == null) return "";
            var lines = t.Replace("\r", "").Split('\n');
            var keep = new List<string>();
            for (int i = 0; i < lines.Length && i < 4; i++) keep.Add(lines[i].Length > 24 ? lines[i].Substring(0, 24) : lines[i]);
            var s = string.Join("\n", keep);
            return s.Length > SignChars ? s.Substring(0, SignChars) : s;
        }

        public void SetSign(int x, int y, int z, string text)
        {
            text = CleanSign(text);
            if (text.Trim().Length == 0) Signs.Remove(Key(x, y, z)); else Signs[Key(x, y, z)] = text;
            Dirty = true;
        }

        public static long Key(int x, int y, int z) => ((long)(x & 0x3FFFFFF) << 38) | ((long)(z & 0x3FFFFFF) << 12) | (long)(y & 0xFFF);

        public int Count => _boxes.Count;

        // The slots of the container at a position, made (empty) if there wasn't one yet.
        public Stack[] At(int x, int y, int z, int size = ChestSlots)
        {
            long k = Key(x, y, z);
            if (!_boxes.TryGetValue(k, out var a)) { a = new Stack[size]; _boxes[k] = a; }
            else if (a.Length < size) { Array.Resize(ref a, size); _boxes[k] = a; }
            return a;
        }

        public static bool Same(Stack a, Stack b) => a.Empty ? b.Empty : !b.Empty && a.Item == b.Item && a.Count == b.Count && a.Wear == b.Wear;

        // A player changed a slot from what they saw (`seen`) to `want`, but the slot really holds `now` (a furnace
        // finished a piece, or used one, in between). What they did is applied as a difference, "took three" or
        // "added five", to what is really there, so nothing made meanwhile is wiped out and nothing is counted twice.
public static Stack Reconcile(Stack now, Stack seen, Stack want) => Reconcile(now, seen, want, null);

        // As above, and `owed` gets what the player must be given (n > 0) or must hand back (n < 0) so that nothing is
        // made or lost: they acted on `seen` and already count themselves richer by seen - want, but the slot only
        // gave up now - result.
        public static Stack Reconcile(Stack now, Stack seen, Stack want, List<(string item, int n, int wear)> owed)
        {
            Stack result;
            if (Same(now, seen)) return want;
            string item = want.Empty ? seen.Item : want.Item;
            if (!seen.Empty && !want.Empty && seen.Item != want.Item) result = want;        // swapped for something else: theirs stands
            else if (item == null) result = now;
            else
            {
                int delta = (want.Empty ? 0 : want.Count) - (seen.Empty ? 0 : seen.Count);
                if (now.Empty) result = delta > 0 ? new Stack(item, delta) { Wear = want.Wear } : Stack.None;
                else if (now.Item != item || now.Wear != 0 || want.Wear != 0) result = want;
                else
                {
                    int n = Math.Clamp(now.Count + delta, 0, Items.Get(item)?.MaxStack ?? 64);
                    result = n > 0 ? new Stack(item, n) : Stack.None;
                }
            }
            if (owed != null)
            {
                void Count(Stack s, int sign)
                {
                    if (s.Empty) return;
                    for (int k = 0; k < owed.Count; k++) if (owed[k].item == s.Item) { owed[k] = (s.Item, owed[k].n + sign * s.Count, Math.Max(owed[k].wear, s.Wear)); return; }
                    owed.Add((s.Item, sign * s.Count, s.Wear));
                }
                Count(now, 1); Count(result, -1); Count(seen, -1); Count(want, 1);
                owed.RemoveAll(o => o.n == 0);
            }
            return result;
        }

        // Stacks of the same thing joined up to their limit, so a satchel holds as much as it can.
        public static List<Stack> Compact(IEnumerable<Stack> stacks)
        {
            var list = new List<Stack>();
            foreach (var s0 in stacks)
            {
                var s = s0;
                if (s.Empty) continue;
                int max = Items.Get(s.Item)?.MaxStack ?? 1;
                if (s.Wear == 0 && max > 1)
                    for (int k = 0; k < list.Count && s.Count > 0; k++)
                    {
                        if (list[k].Item != s.Item || list[k].Wear != 0 || list[k].Count >= max) continue;
                        int n = Math.Min(max - list[k].Count, s.Count);
                        var t = list[k]; t.Count += n; list[k] = t; s.Count -= n;
                    }
                if (s.Count > 0) list.Add(s);
            }
            return list;
        }

        // Puts things into satchels at and around a place: a satchel already there is topped up, and when one is
        // full (MaxSlots) the rest goes into another beside it. `place` sets the satchel block. Returns whatever
        // found no room anywhere near, which the caller must hand back: nothing is thrown away here.
        public List<Stack> Stash(Func<int, int, int, byte> get, Action<int, int, int> place, int x, int y, int z, IEnumerable<Stack> stacks)
        {
            var rest = Compact(stacks);
            var tried = new HashSet<long>();
            for (int guard = 0; guard < 12 && rest.Count > 0; guard++)
            {
                if (!SpotFor(get, x, y, z, (cx, cy, cz) => tried.Contains(Key(cx, cy, cz)), out var at)) break;
                tried.Add(Key(at.x, at.y, at.z));
                bool satchel = get(at.x, at.y, at.z) == Blocks.Satchel;
                var have = new List<Stack>();
                if (Has(at.x, at.y, at.z)) foreach (var s in Remove(at.x, at.y, at.z)) if (!s.Empty) have.Add(s);
                int before = have.Count;
                var all = Compact(System.Linq.Enumerable.Concat(have, rest));
                // What was there stays there (it comes first); the newcomers fill what room is left.
                int keep = Math.Min(all.Count, Math.Max(MaxSlots, before));
                Put(at.x, at.y, at.z, all.GetRange(0, keep).ToArray());
                if (!satchel) place(at.x, at.y, at.z);
                rest = all.GetRange(keep, all.Count - keep);
            }
            return rest;
        }

        public bool Has(int x, int y, int z) => _boxes.ContainsKey(Key(x, y, z));
        public bool TryGet(long key, out Stack[] slots) => _boxes.TryGetValue(key, out slots);
        public IEnumerable<KeyValuePair<long, Stack[]>> All => _boxes;

        public bool IsEmpty(int x, int y, int z)
        {
            if (!_boxes.TryGetValue(Key(x, y, z), out var a)) return true;
            foreach (var s in a) if (!s.Empty) return false;
            return true;
        }

        public void Set(int x, int y, int z, int slot, Stack s)
        {
            var a = At(x, y, z, Math.Max(ChestSlots, slot + 1));
            if ((uint)slot >= (uint)a.Length) return;
            a[slot] = s.Empty ? Stack.None : s;
            Dirty = true;
        }

        // Takes the container away and returns what was in it.
        public Stack[] Remove(int x, int y, int z)
        {
            long k = Key(x, y, z);
            if (!_boxes.TryGetValue(k, out var a)) return Array.Empty<Stack>();
            _boxes.Remove(k);
            Dirty = true;
            return a;
        }

        public void Put(int x, int y, int z, Stack[] stacks)
        {
            var a = new Stack[Math.Max(ChestSlots, stacks.Length)];
            int n = 0;
            foreach (var s in stacks) if (!s.Empty && Items.Get(s.Item) != null && n < a.Length) a[n++] = s;
            _boxes[Key(x, y, z)] = a;
            Dirty = true;
        }

        public void Load(string file)
        {
            _boxes.Clear();
            file = SafeFile.Pick(file);
            if (!File.Exists(file)) return;
            using var r = new BinaryReader(File.OpenRead(file));
            Signs.Clear();
            int ver = r.ReadInt32();
            if (ver != 1 && ver != Version) throw new InvalidDataException("containers file version " + ver);
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                long k = r.ReadInt64();
                int slots = r.ReadUInt16();
                var a = new Stack[slots];
                for (int j = 0; j < slots; j++)
                {
                    string item = r.ReadString(); int count = r.ReadUInt16(); int wear = r.ReadUInt16();
                    if (item.Length > 0 && count > 0 && (Items.Get(item) != null || Smelting.Hidden(item))) a[j] = new Stack(item, count) { Wear = wear };
                }
                _boxes[k] = a;
            }
            if (ver >= 2)
            {
                int ns = r.ReadInt32();
                for (int i = 0; i < ns; i++) { long k = r.ReadInt64(); Signs[k] = CleanSign(r.ReadString()); }
            }
        }

        public void Save(string file)
        {
            var tmp = file + ".tmp";
            using (var w = new BinaryWriter(File.Create(tmp)))
            {
                w.Write(Version);
                int n = 0;
                foreach (var kv in _boxes) if (!Empty(kv.Value)) n++;
                w.Write(n);
                foreach (var kv in _boxes)
                {
                    if (Empty(kv.Value)) continue;
                    w.Write(kv.Key);
                    w.Write((ushort)kv.Value.Length);
                    foreach (var s in kv.Value)
                    {
                        w.Write(s.Empty ? "" : s.Item);
                        w.Write((ushort)(s.Empty ? 0 : Math.Min(s.Count, 0xFFFF)));
                        w.Write((ushort)Math.Clamp(s.Wear, 0, 0xFFFF));
                    }
                }
                w.Write(Signs.Count);
                foreach (var kv in Signs) { w.Write(kv.Key); w.Write(kv.Value); }
            }
            SafeFile.Swap(tmp, file, keep: true);
            Dirty = false;
        }

        static bool Empty(Stack[] a) { foreach (var s in a) if (!s.Empty) return false; return true; }

        // A free cell near a point for a satchel: air (or a plant) with something solid under it, nearest first.
        public static (int x, int y, int z) SpotFor(Func<int, int, int, byte> get, int x, int y, int z)
        {
            return SpotFor(get, x, y, z, null, out var at) ? at : (x, y, z);
        }

        // The same, also counting a satchel already lying there as a place (to add to), leaving out cells `skip`
        // names, and saying honestly when there is nowhere at all.
        public static bool SpotFor(Func<int, int, int, byte> get, int x, int y, int z, Func<int, int, int, bool> skip, out (int x, int y, int z) at)
        {
            at = (x, y, z);
            y = Math.Clamp(y, 1, Chunk.Height - 2);
            for (int r = 0; r <= 3; r++)
                for (int dy = 0; dy <= 4; dy++)
                    foreach (int sy in new[] { 1, -1 })
                        for (int dz = -r; dz <= r; dz++)
                            for (int dx = -r; dx <= r; dx++)
                            {
                                if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                                int cx = x + dx, cy = y + dy * sy, cz = z + dz;
                                if (cy < 1 || cy >= Chunk.Height - 1) continue;
                                if (skip != null && skip(cx, cy, cz)) continue;
                                var here = Blocks.Get(get(cx, cy, cz));
                                bool satchel = skip != null && here.Id == Blocks.Satchel;
                                if (!satchel && !(here.Id == 0 || here.Replaceable && here.Fluid == 0)) continue;
                                if (!satchel && !Blocks.Get(get(cx, cy - 1, cz)).Solid) continue;
                                at = (cx, cy, cz);
                                return true;
                            }
            return false;
        }
    }
}
