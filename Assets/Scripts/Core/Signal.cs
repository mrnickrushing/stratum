using System;
using System.Collections.Generic;

namespace Stratum.Core
{
    // Spark: power that runs along wire. Levers, buttons, pressure plates and spark torches give it; wire carries
    // it fifteen blocks, losing a step each block; it lights lamps, opens doors, trapdoors and gates, and wakes
    // booster rails. A solid block a source is fixed to, or that wire runs onto or points into, passes it on to
    // what's beside it. A spark torch goes out when the block it stands on is powered, which (with a moment's
    // delay) makes NOT gates and clocks; a repeater passes power one way, a moment later, at full strength.
    //
    // It works out a whole circuit at once whenever something in it changes, so it doesn't matter in what order
    // the pieces were laid. Runs in single-player worlds and on servers; players only ever flip a lever or press
    // a button (ordinary block edits).
    public sealed class Signal
    {
        readonly Func<int, int, int, ushort> _get;
        readonly Action<int, int, int, ushort> _set;
        public Func<int, int, bool> Loaded;
        readonly HashSet<(int, int, int)> _dirty = new HashSet<(int, int, int)>();
        readonly HashSet<(int, int, int)> _was = new HashSet<(int, int, int)>();          // consumers powered last time
        readonly Dictionary<(int, int, int), (float at, ushort id)> _later = new Dictionary<(int, int, int), (float, ushort)>();
        readonly Dictionary<(int, int, int), float> _plates = new Dictionary<(int, int, int), float>();
        float _time;
        bool _applying;
        public const float Delay = 0.1f, ButtonFor = 1.0f, PlateFor = 0.6f;
        const int MaxCells = 6000;

        static readonly int[] DX = { 0, 1, 0, -1, 0, 0 }, DY = { 0, 0, 0, 0, 1, -1 }, DZ = { 1, 0, -1, 0, 0, 0 };

        public Signal(Func<int, int, int, ushort> get, Action<int, int, int, ushort> set) { _get = get; _set = set; }

        // ------------------------------------------------------------------ what's what

        static int Base(ushort id) => id == 0 ? 0 : Blocks.Get(id).BaseId;
        public static bool IsWire(ushort id) => id != 0 && Base(id) == Blocks.SparkWire;
        static bool IsLever(ushort id) => id != 0 && Base(id) == Blocks.Lever;
        static bool IsButton(ushort id) => id != 0 && Base(id) == Blocks.Button;
        static bool IsPlate(ushort id) => id != 0 && (Base(id) == Blocks.PressurePlate || Base(id) == Blocks.DetectorRail);       // stood on, or ridden over
        static bool IsTorch(ushort id) => id != 0 && Base(id) == Blocks.SparkTorch;
        static bool IsLamp(ushort id) => id != 0 && Base(id) == Blocks.SparkLamp;
        static bool IsRepeater(ushort id) => id != 0 && Blocks.Get(id).Repeat;
        static bool Opens(ushort id) => id != 0 && Blocks.Get(id).Opens;
        static bool IsEmitter(ushort id) => id != 0 && Blocks.Get(id).Emitter;
        static bool IsMachine(ushort id) => id != 0 && Blocks.Get(id).Machine;
        // Where an observer or comparator puts its power: an observer behind itself, a comparator in front.
        static (int, int, int) OutOf(BlockDef d, int x, int y, int z) => d.Sensor ? (x, y - 1, z) : d.Watcher ? (x - DX[d.Dir], y - DY[d.Dir], z - DZ[d.Dir]) : (x + DX[d.Dir], y + DY[d.Dir], z + DZ[d.Dir]);
        static int V(ushort id) => id - Blocks.Get(id).BaseId;

        // Lever, button and plate: the low bit is "on". Torch: variant 0 is lit. Repeater: low bit on, facing = output.
        static bool On(ushort id) =>
            IsEmitter(id) || IsMachine(id) ? (V(id) & 1) == 1 :
            IsLever(id) || IsButton(id) || IsRepeater(id) ? (V(id) & 1) == 1 :
            IsPlate(id) ? (V(id) & 1) == 1 :
            IsWire(id) || IsLamp(id) ? V(id) == 1 :
            IsTorch(id) ? V(id) == 0 : false;

        public static bool IsPart(ushort id) => IsWire(id) || IsLever(id) || IsButton(id) || IsPlate(id) || IsTorch(id) || IsLamp(id) || IsRepeater(id) || id == Blocks.BlastKeg
                                              || Opens(id) || Rails.Booster(id) || IsEmitter(id) || IsMachine(id);

        // Things that stand on the block below them (and pop off when it goes).
        public static bool NeedsFloor(ushort id) => IsWire(id) || IsLever(id) || IsPlate(id) || IsRepeater(id) || id != 0 && Blocks.Get(id).Gauge;

        static bool Conductor(ushort id) { if (id == 0) return false; var d = Blocks.Get(id); return d.Solid && d.Opaque && d.Shape == Shape.Cube; }

        // Does wire reach toward this neighbour (for drawing and for which way it points)?
        public static bool WireJoins(ushort other, int dir)
        {
            if (IsWire(other) || IsLever(other) || IsButton(other) || IsPlate(other) || IsTorch(other)) return true;
            if (IsRepeater(other)) { int f = Blocks.Get(other).Facing; return f == dir || ((f + 2) & 3) == dir; }
            return false;
        }

        // Which of the four sides a wire joins (stepping up or down a block counts).
        public static int WireMask(Func<int, int, int, ushort> get, int x, int y, int z)
        {
            int m = 0;
            bool roofOpen = !Conductor(get(x, y + 1, z));
            for (int d = 0; d < 4; d++)
            {
                int nx = x + DX[d], nz = z + DZ[d];
                ushort n = get(nx, y, nz);
                if (WireJoins(n, d)) { m |= 1 << d; continue; }
                if (roofOpen && IsWire(get(nx, y + 1, nz))) { m |= 1 << d; continue; }
                if (!Conductor(n) && IsWire(get(nx, y - 1, nz))) m |= 1 << d;
            }
            return m;
        }

        // ------------------------------------------------------------------ running

        public void Wake(int x, int y, int z)
        {
            // An observer looking at this cell blinks; an arm whose piston has gone goes too.
            for (int d = 0; d < 6; d++)
            {
                int nx = x + DX[d], ny = y + DY[d], nz = z + DZ[d];
                if ((uint)ny >= Chunk.Height) continue;
                var n = Blocks.Get(_get(nx, ny, nz));
                if (n.Watcher && (n.Variant & 1) == 0 && nx + DX[n.Dir] == x && ny + DY[n.Dir] == y && nz + DZ[n.Dir] == z) _blinks.Add((nx, ny, nz));
                if (n.Arm && nx - DX[n.Dir] == x && ny - DY[n.Dir] == y && nz - DZ[n.Dir] == z && !Blocks.Get(_get(x, y, z)).Piston) _blinks.Add((nx, ny, nz));
            }
            if (_applying) return;
            _dirty.Add((x, y, z));
        }
        readonly HashSet<(int, int, int)> _blinks = new HashSet<(int, int, int)>();
        const float BlinkFor = 0.2f;

        // bodies: everyone standing about (players and creatures), for pressure plates.
        public void Tick(float dt, IEnumerable<(float x, float y, float z)> bodies)
        {
            _time += dt;
            if (bodies != null)
                foreach (var (bx, by, bz) in bodies)
                {
                    int x = (int)Math.Floor(bx), y = (int)Math.Floor(by + 0.05f), z = (int)Math.Floor(bz);
                    ushort id = _get(x, y, z);
                    if (!IsPlate(id)) continue;
                    _plates[(x, y, z)] = _time;
                    if (!On(id)) Put(x, y, z, (ushort)(id + 1));
                }
            if (_plates.Count > 0)
            {
                List<(int, int, int)> up = null;
                foreach (var kv in _plates) if (_time - kv.Value > PlateFor) (up ??= new List<(int, int, int)>()).Add(kv.Key);
                if (up != null)
                    foreach (var (x, y, z) in up)
                    {
                        _plates.Remove((x, y, z));
                        if (IsPlate(_get(x, y, z)) && On(_get(x, y, z))) Put(x, y, z, (ushort)(_get(x, y, z) - 1));
                    }
            }
            if (_later.Count > 0)
            {
                List<(int, int, int)> due = null;
                foreach (var kv in _later) if (kv.Value.at <= _time) (due ??= new List<(int, int, int)>()).Add(kv.Key);
                if (due != null)
                    foreach (var k in due)
                    {
                        var (at, id) = _later[k]; _later.Remove(k);
                        var (x, y, z) = k;
                        ushort cur = _get(x, y, z);
                        if (cur == 0 || Base(cur) != Base(id) || cur == id) continue;   // broken or changed meanwhile
                        Put(x, y, z, id);
                    }
            }
            // At dawn and dusk the daylight sensors look again.
            if (IsDay != null && (_dayCheck -= dt) <= 0)
            {
                _dayCheck = 1;
                bool day = IsDay();
                if (day != _wasDay) { _wasDay = day; foreach (var sc in _sensors) _dirty.Add(sc); _sensors.Clear(); }
            }
            if (_blinks.Count > 0)
            {
                var blinks = new List<(int, int, int)>(_blinks); _blinks.Clear();
                foreach (var (x, y, z) in blinks)
                {
                    ushort id = _get(x, y, z); var d = Blocks.Get(id);
                    if (d.Arm) { if (!Blocks.Get(_get(x - DX[d.Dir], y - DY[d.Dir], z - DZ[d.Dir])).Piston) Put(x, y, z, 0); }
                    else if (d.Watcher && (d.Variant & 1) == 0) { Put(x, y, z, (ushort)(id + 1)); _later[(x, y, z)] = (_time + BlinkFor, id); }
                }
            }
            if (_dirty.Count == 0) return;
            var seeds = new List<(int, int, int)>(_dirty);
            _dirty.Clear();
            Update(seeds);
        }

        // A change the sim makes itself: set, then look at the circuit again.
        void Put(int x, int y, int z, ushort id)
        {
            _applying = true;
            try { _set(x, y, z, id); } finally { _applying = false; }
            _dirty.Add((x, y, z));
        }

        // Power on a booster rail right now (worked out from the blocks alone, so clients riding carts agree with
        // the server): something switched on beside it or under it, or another booster in line within 8 that is.
        public static bool BoosterPowered(Func<int, int, int, ushort> get, int x, int y, int z)
        {
            ushort id = get(x, y, z);
            if (!Rails.Booster(id)) return false;
            if (Fed(get, x, y, z)) return true;
            var (a, b, _) = Rails.Ends(id);
            foreach (int dir in new[] { a, b })
            {
                int cx = x, cy = y, cz = z;
                for (int i = 0; i < 8; i++)
                {
                    cx += Rails.DX[dir]; cz += Rails.DZ[dir];
                    if (!Rails.Booster(get(cx, cy, cz)))
                    {
                        if (Rails.Booster(get(cx, cy + 1, cz))) cy++;
                        else if (Rails.Booster(get(cx, cy - 1, cz))) cy--;
                        else break;
                    }
                    if (Fed(get, cx, cy, cz)) return true;
                }
            }
            return false;
        }

        static bool Fed(Func<int, int, int, ushort> get, int x, int y, int z)
        {
            for (int d = 0; d < 6; d++)
            {
                int nx = x + DX[d], ny = y + DY[d], nz = z + DZ[d];
                ushort n = get(nx, ny, nz);
                if (IsWire(n) || IsLever(n) || IsButton(n) || IsPlate(n) || IsTorch(n)) { if (On(n)) return true; continue; }
                if (IsRepeater(n) && On(n) && Blocks.Get(n).Facing == ((d + 2) & 3) && d < 4) return true;
                if (Conductor(n))
                    for (int e = 0; e < 6; e++)
                    {
                        ushort m = get(nx + DX[e], ny + DY[e], nz + DZ[e]);
                        if ((IsLever(m) || IsButton(m) || IsPlate(m)) && On(m)) return true;
                        if (IsWire(m) && On(m) && e == 4) return true;            // wire lying on it
                    }
            }
            return false;
        }

        // ------------------------------------------------------------------ working a circuit out

        readonly HashSet<(int, int, int)> _cells = new HashSet<(int, int, int)>();
        readonly Queue<(int, int, int)> _q = new Queue<(int, int, int)>();
        readonly Dictionary<(int, int, int), int> _level = new Dictionary<(int, int, int), int>();
        readonly HashSet<(int, int, int)> _strong = new HashSet<(int, int, int)>(), _weak = new HashSet<(int, int, int)>();

        void Update(List<(int x, int y, int z)> seeds)
        {
            // 1. Everything connected to the change.
            _cells.Clear(); _q.Clear();
            void Visit(int x, int y, int z)
            {
                if (_cells.Count >= MaxCells || !_cells.Add((x, y, z))) return;
                if (Loaded != null && !Loaded(x, z)) return;
                if (IsPart(_get(x, y, z)) || Conductor(_get(x, y, z))) _q.Enqueue((x, y, z));
            }
            foreach (var (x, y, z) in seeds)
            {
                Visit(x, y, z);
                for (int d = 0; d < 6; d++) Visit(x + DX[d], y + DY[d], z + DZ[d]);
            }
            while (_q.Count > 0)
            {
                var (x, y, z) = _q.Dequeue();
                ushort id = _get(x, y, z);
                bool part = IsPart(id);
                for (int d = 0; d < 6; d++)
                {
                    int nx = x + DX[d], ny = y + DY[d], nz = z + DZ[d];
                    ushort n = _get(nx, ny, nz);
                    // From a solid block only into parts (power passes through one block, not along walls).
                    if (part ? (IsPart(n) || Conductor(n)) : IsPart(n)) Visit(nx, ny, nz);
                }
                if (IsWire(id))
                    for (int d = 0; d < 4; d++)
                        for (int dy = -1; dy <= 1; dy += 2)
                            if (IsWire(_get(x + DX[d], y + dy, z + DZ[d]))) Visit(x + DX[d], y + dy, z + DZ[d]);
            }

            // 2. Sources, and the blocks they power outright.
            _strong.Clear(); _weak.Clear(); _level.Clear();
            foreach (var c in _cells)
            {
                var (x, y, z) = c;
                ushort id = _get(x, y, z);
                if (!On(id)) continue;
                var d = Blocks.Get(id);
                if (IsLever(id) || IsPlate(id)) _strong.Add((x, y - 1, z));
                else if (IsButton(id)) _strong.Add((x + DX[d.Facing], y, z + DZ[d.Facing]));
                else if (IsTorch(id)) _strong.Add((x, y + 1, z));
                else if (IsRepeater(id)) _strong.Add((x + DX[d.Facing], y, z + DZ[d.Facing]));
                else if (IsEmitter(id)) _strong.Add(OutOf(d, x, y, z));
            }

            // 3. Wire: full strength next to a source, one less for each block along.
            var buckets = new List<(int, int, int)>[16];
            for (int i = 0; i < 16; i++) buckets[i] = new List<(int, int, int)>();
            foreach (var c in _cells)
            {
                var (x, y, z) = c;
                if (!IsWire(_get(x, y, z))) continue;
                bool fed = false;
                for (int d = 0; d < 6 && !fed; d++)
                {
                    int nx = x + DX[d], ny = y + DY[d], nz = z + DZ[d];
                    ushort n = _get(nx, ny, nz);
                    if ((IsLever(n) || IsButton(n) || IsPlate(n) || IsTorch(n)) && On(n)) fed = true;
                    else if (IsRepeater(n) && On(n) && d < 4 && Blocks.Get(n).Facing == ((d + 2) & 3)) fed = true;
                    else if (IsEmitter(n) && On(n) && (Blocks.Get(n).Sensor || OutOf(Blocks.Get(n), nx, ny, nz) == (x, y, z))) fed = true;
                    else if (_strong.Contains((nx, ny, nz)) && Conductor(n)) fed = true;
                }
                if (fed) { _level[c] = 15; buckets[15].Add(c); }
            }
            for (int lv = 15; lv >= 1; lv--)
                foreach (var c in buckets[lv])
                {
                    if (_level[c] != lv) continue;
                    var (x, y, z) = c;
                    bool roofOpen = !Conductor(_get(x, y + 1, z));
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = x + DX[d], nz = z + DZ[d];
                        (int, int, int) n;
                        if (IsWire(_get(nx, y, nz))) n = (nx, y, nz);
                        else if (roofOpen && IsWire(_get(nx, y + 1, nz))) n = (nx, y + 1, nz);
                        else if (!Conductor(_get(nx, y, nz)) && IsWire(_get(nx, y - 1, nz))) n = (nx, y - 1, nz);
                        else continue;
                        if (_level.TryGetValue(n, out int had) && had >= lv - 1) continue;
                        if (lv - 1 <= 0) continue;
                        _level[n] = lv - 1; buckets[lv - 1].Add(n);
                    }
                }

            // 4. Blocks wire runs onto or points into.
            foreach (var kv in _level)
            {
                var (x, y, z) = kv.Key;
                _weak.Add((x, y - 1, z));
                foreach (int d in Points(x, y, z)) _weak.Add((x + DX[d], y, z + DZ[d]));
            }

            // 5. What everything should be now.
            _applying = true;
            try
            {
                foreach (var c in _cells)
                {
                    var (x, y, z) = c;
                    ushort id = _get(x, y, z);
                    if (id == 0) { _was.Remove(c); continue; }
                    var def = Blocks.Get(id);
                    if (IsWire(id))
                    {
                        ushort want = (ushort)(Blocks.SparkWire + (_level.ContainsKey(c) ? 1 : 0));
                        if (want != id) _set(x, y, z, want);
                    }
                    else if (id == Blocks.BlastKeg) { if (Powered(c, x, y, z)) _set(x, y, z, Blocks.KegLit); }
                    else if (def.Piston) Push(c, id, def, Powered(c, x, y, z));
                    else if (def.Note)
                    {
                        // A note block sounds each time power arrives.
                        bool p = Powered(c, x, y, z), on = (V(id) & 1) == 1;
                        if (p == on) continue;
                        _set(x, y, z, (ushort)(p ? id + 1 : id - 1));
                        if (p) Played?.Invoke(x, y, z, def.Variant >> 1);
                    }
                    else if (def.Sensor)
                    {
                        _sensors.Add(c);
                        bool want = IsDay != null && IsDay() && OpenSky(x, y, z), on = (V(id) & 1) == 1;
                        if (want != on) Later(c, (ushort)(want ? id + 1 : id - 1));
                    }
                    else if (def.Machine)
                    {
                        // A dispenser throws one thing out each time power arrives.
                        bool p = Powered(c, x, y, z), on = (V(id) & 1) == 1;
                        if (p == on) continue;
                        _set(x, y, z, (ushort)(p ? id + 1 : id - 1));
                        if (p) Fired?.Invoke(x, y, z, def.Dir);
                    }
                    else if (def.Gauge)
                    {
                        // A comparator is on while the container behind it has anything in it.
                        bool want = Holds != null && Holds(x - DX[def.Dir], y, z - DZ[def.Dir]), on = (V(id) & 1) == 1;
                        if (want != on) Later(c, (ushort)(want ? id + 1 : id - 1));
                    }
                    else if (IsLamp(id))
                    {
                        ushort want = (ushort)(Blocks.SparkLamp + (Powered(c, x, y, z) ? 1 : 0));
                        if (want != id) _set(x, y, z, want);
                    }
                    else if (IsTorch(id))
                    {
                        bool lit = !BlockPowered((x, y - 1, z));
                        ushort want = (ushort)(Blocks.SparkTorch + (lit ? 0 : 1));
                        if (want != id) Later(c, want);
                    }
                    else if (IsRepeater(id))
                    {
                        int f = def.Facing, b = (f + 2) & 3;
                        bool inOn = InputAt(x + DX[b], y, z + DZ[b], f);
                        ushort want = (ushort)(def.BaseId + f * 2 + (inOn ? 1 : 0));
                        if (want != id) Later(c, want, def.Slow);
                    }
                    else if (IsButton(id) && On(id) && !_later.ContainsKey(c))
                        _later[c] = (_time + ButtonFor, (ushort)(id - 1));
                    else if (Opens(id))
                    {
                        // Doors count power on either half; they swing when it comes or goes.
                        bool p = Powered(c, x, y, z);
                        bool door = def.Door;
                        if (door)
                        {
                            var other = (def.Variant & 1) == 1 ? (x, y - 1, z) : (x, y + 1, z);
                            p |= Powered(other, other.Item1, other.Item2, other.Item3);
                        }
                        bool was = _was.Contains(c);
                        if (p == was) continue;
                        if (p) _was.Add(c); else _was.Remove(c);
                        int openBit = door ? 2 : 1;
                        bool open = (def.Variant & openBit) != 0;
                        if (open == p) continue;
                        _set(x, y, z, Blocks.Flip(id, openBit));
                        if (door)
                        {
                            var other = (def.Variant & 1) == 1 ? (x, y - 1, z) : (x, y + 1, z);
                            ushort oid = _get(other.Item1, other.Item2, other.Item3);
                            if (Blocks.Get(oid).Door && ((Blocks.Get(oid).Variant & 2) != 0) != p)
                            {
                                _set(other.Item1, other.Item2, other.Item3, Blocks.Flip(oid, 2));
                                if (p) _was.Add(other); else _was.Remove(other);
                            }
                        }
                    }
                }
            }
            finally { _applying = false; }
        }

        public Action<int, int, int, int> Played;             // a note block sounded: where, and which of its eight notes
        public Func<bool> IsDay;                              // is the sun up (for daylight sensors)
        readonly HashSet<(int, int, int)> _sensors = new HashSet<(int, int, int)>();
        bool _wasDay; float _dayCheck;

        bool OpenSky(int x, int y, int z)
        {
            for (int yy = y + 1; yy < Chunk.Height; yy++) if (Blocks.Get(_get(x, yy, z)).Opaque) return false;
            return true;
        }

        public Action<int, int, int, int> Fired;              // a dispenser went off: where, and which way it faces
        public Func<int, int, int, bool> Holds;               // does the container here hold anything (for comparators)
        public const int PushMost = 12;

        static bool Movable(BlockDef d) =>
            d.Hardness >= 0 && d.Hardness < 15 && !d.Container && !d.Arm && !d.Door && d.BaseId != Blocks.Bed && !(d.Piston && (d.Variant & 1) == 1) && d.Fluid == 0
            && d.BaseId != Blocks.Sign && d.BaseId != Blocks.WallSign && d.BaseId != Blocks.Portal;

        // A piston: with power it pushes up to twelve blocks along and puts its arm out; without, it draws the arm
        // back (a sticky one brings the block in front of the arm with it).
        void Push((int x, int y, int z) c, ushort id, BlockDef def, bool powered)
        {
            bool on = (def.Variant & 1) == 1;
            if (powered == on) return;
            var (x, y, z) = c;
            int dx = DX[def.Dir], dy = DY[def.Dir], dz = DZ[def.Dir];
            if (powered)
            {
                int n = 0; bool room = false;
                for (; n <= PushMost; n++)
                {
                    int by = y + dy * (n + 1);
                    if (by < 1 || by >= Chunk.Height - 1 || Loaded != null && !Loaded(x + dx * (n + 1), z + dz * (n + 1))) break;
                    var b = Blocks.Get(_get(x + dx * (n + 1), by, z + dz * (n + 1)));
                    if (b.Id == 0 || b.Replaceable || !b.Solid && !b.Arm && !b.Container && !b.Door) { room = true; break; }      // empty, or something that just breaks
                    if (!Movable(b)) break;
                }
                if (!room) return;                    // stuck: it tries again whenever the circuit next changes
                for (int i = n; i >= 1; i--)
                    _set(x + dx * (i + 1), y + dy * (i + 1), z + dz * (i + 1), _get(x + dx * i, y + dy * i, z + dz * i));
                _set(x + dx, y + dy, z + dz, (ushort)(Blocks.PistonArm + def.Dir));
                _set(x, y, z, (ushort)(id + 1));
                for (int i = 0; i <= n + 1; i++) _dirty.Add((x + dx * i, y + dy * i, z + dz * i));
            }
            else
            {
                _set(x, y, z, (ushort)(id - 1));
                if (Blocks.Get(_get(x + dx, y + dy, z + dz)).Arm) _set(x + dx, y + dy, z + dz, 0);
                if (def.Sticky)
                {
                    var b = Blocks.Get(_get(x + dx * 2, y + dy * 2, z + dz * 2));
                    if (b.Id != 0 && b.Solid && !b.Replaceable && Movable(b))
                    {
                        _set(x + dx, y + dy, z + dz, b.Id);
                        _set(x + dx * 2, y + dy * 2, z + dz * 2, 0);
                    }
                }
                for (int i = 0; i <= 2; i++) _dirty.Add((x + dx * i, y + dy * i, z + dz * i));
            }
        }

        void Later((int, int, int) c, ushort id) => Later(c, id, 0);
        void Later((int, int, int) c, ushort id, float extra)
        {
            if (_later.TryGetValue(c, out var l) && l.id == id) return;
            _later[c] = (_time + Delay + extra, id);
        }

        // The sides a powered wire points into: all four for a lone dot, both ends of a straight line, else none.
        IEnumerable<int> Points(int x, int y, int z)
        {
            int m = WireMask(_get, x, y, z);
            if (m == 0) { for (int d = 0; d < 4; d++) yield return d; yield break; }
            bool zs = (m & 5) != 0, xs = (m & 10) != 0;
            if (zs && !xs) { yield return 0; yield return 2; }
            else if (xs && !zs) { yield return 1; yield return 3; }
        }

        bool BlockPowered((int, int, int) c) => _strong.Contains(c) || _weak.Contains(c) && Conductor(_get(c.Item1, c.Item2, c.Item3));

        // Is a lamp, door or the like (at c) powered: a source beside it, wire pointing into it or lying on it, a
        // powered block beside it.
        bool Powered((int, int, int) c, int x, int y, int z)
        {
            for (int d = 0; d < 6; d++)
            {
                int nx = x + DX[d], ny = y + DY[d], nz = z + DZ[d];
                ushort n = _get(nx, ny, nz);
                if ((IsLever(n) || IsButton(n) || IsPlate(n) || IsTorch(n)) && On(n))
                {
                    if (IsTorch(n) && d == 4) continue;                     // a torch doesn't power the block it stands on
                    return true;
                }
                if (IsRepeater(n) && On(n) && d < 4 && Blocks.Get(n).Facing == ((d + 2) & 3)) return true;
                if (IsEmitter(n) && On(n) && (Blocks.Get(n).Sensor || OutOf(Blocks.Get(n), nx, ny, nz) == (x, y, z))) return true;
                if (IsWire(n) && _level.ContainsKey((nx, ny, nz)))
                {
                    if (d == 4) return true;                                 // wire lying on top
                    if (d < 4) foreach (int p in Points(nx, ny, nz)) if (p == ((d + 2) & 3)) return true;
                }
                if (Conductor(n) && BlockPowered((nx, ny, nz))) return true;
            }
            return false;
        }

        // A repeater's input: the cell behind it (wire with power, a source, a powered block, a repeater facing in).
        bool InputAt(int x, int y, int z, int facing)
        {
            ushort n = _get(x, y, z);
            if (IsWire(n)) return _level.ContainsKey((x, y, z));
            if (IsLever(n) || IsButton(n) || IsPlate(n) || IsTorch(n)) return On(n);
            if (IsRepeater(n)) return On(n) && Blocks.Get(n).Facing == facing;
            if (Conductor(n)) return BlockPowered((x, y, z));
            return false;
        }
    }
}
