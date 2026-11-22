using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Stratum.Core;

namespace Stratum.Server
{
    // One world: its own thread runs it at 20 ticks a second, and connections hand it their messages through a
    // queue. Players arrive already logged in, handed over by the GameServer (the front door).
    public sealed class WorldHost
    {
        sealed class Player
        {
            public Connection Conn;
            public string Name, Key;
            public int Features;                         // what this player's game knows how to do (GameServer.Knows...)
            public long Order;                           // which login this session came from (GameServer numbers them)
            public byte[] Look;                          // an outfit made piece by piece (Outfit 255), passed on as it came
            public byte Outfit;                          // what other players see them wearing (the game's Extras page)
            public PlayerRecord Rec;
            public bool Joined;
            public float X, Y, Z, Yaw, Pitch;
            public byte Flags;
            public string Held = ""; public bool Shield;
            public bool Moved;
            public int CX => (int)Math.Floor(X / 16f);
            public int CZ => (int)Math.Floor(Z / 16f);
            public readonly HashSet<long> Waiting = new HashSet<long>();
            public int Edits;                       // this second, to stop a flood
            public int Refused;
            public bool Checking;                   // login being checked
            public int VoiceFrames;                 // this second
            public int Strikes, Shots;              // this second
            public bool HasBox; public int BX, BY, BZ;   // the chest they have open
            public int BoxMob;                           // or the chest cart (its creature number)
            public int Tosses;                           // this second
            public readonly (int x, int y, int z, long at)[] Broke = new (int, int, int, long)[8]; public int BrokeAt;   // blocks they just broke (what falls out of them is theirs to drop)
            public bool SeesDrops => (Features & GameServer.KnowsDrops) != 0;
            public long NextPortal;                      // when they may next ask for a portal to be found or built
            public int Given;                            // how many things have been handed over in this visit
            public readonly List<(int n, Stack s)> Owed = new List<(int, Stack)>();    // handed over, not yet seen in a backpack update
            // What their own game moved in or out of the pack since its last backpack update (+ into their hands,
            // - out of them): a chest slot changed, a thing thrown down, everything dropped on falling. The next
            // update carries it; a connection lost before then is settled from this.
            public readonly List<(string item, int n, int wear)> Transit = new List<(string, int, int)>();
            public MobKind? Boarded; public long BoardedAt;      // what they just got on, until their own game says so
            public bool Away; public long AwayAt; public float AX, AY, AZ;   // mid-journey (a portal, a teleport): where they set out from
            public bool HasSafe; public float SX, SY, SZ;  // where they last stood on something (a satchel after a fall into the sky goes there)
            public bool Alive => (Flags & Net.Dead) == 0;
            public bool Asleep => (Flags & Net.Asleep) != 0;
        }

        DropSim _drops;
        Decay _leaves;
        Fire _fire;
        Blasts _blasts;
        float _bolt = 30, _feed;
        readonly Random _dice = new Random();
        readonly ServerConfig _cfg;
        public readonly ServerWorld World;
        readonly ConcurrentQueue<(Connection c, byte[] f)> _inbox = new ConcurrentQueue<(Connection, byte[])>();
        readonly ConcurrentQueue<(Connection c, string why)> _closed = new ConcurrentQueue<(Connection, string)>();
        readonly ConcurrentQueue<(Connection c, Accounts.Result res, Accounts.Mode mode, string version, int outfit, long order, int features, byte[] look)> _arrivals = new ConcurrentQueue<(Connection, Accounts.Result, Accounts.Mode, string, int, long, int, byte[])>();
        readonly GameServer _hub;
        public readonly WorldDef Def;
        readonly ConcurrentQueue<Action> _later = new ConcurrentQueue<Action>();   // work finished off the loop
        readonly MobSim _mobs;
        readonly List<(float, float, float)> _bodies = new List<(float, float, float)>();
        readonly Containers _boxes = new Containers();
        Furnaces _furnaces;
        Chutes _chutes;
        readonly List<(string item, int n, int wear)> _owed = new List<(string, int, int)>();
        readonly List<Seen> _seen = new List<Seen>();
        int _lastSleepers = -1, _lastAwake = -1;
        readonly List<(float, float)> _near = new List<(float, float)>();
        WeatherSim _weather;
        string MobFile => System.IO.Path.Combine(_cfg.Dir, "creatures.bin");
        string DropFile => System.IO.Path.Combine(_cfg.Dir, "drops.bin");
        string BoxFile => System.IO.Path.Combine(_cfg.Dir, "containers.bin");
        readonly Dictionary<int, Player> _players = new Dictionary<int, Player>();
        readonly Dictionary<long, List<Player>> _waiters = new Dictionary<long, List<Player>>();
        Thread _loop;
        volatile bool _stop;
        int _nextId;
        public int Online => _onlineCount;
        volatile string[] _keys = new string[0];                // accounts in this world (read by the front door)
        public int OnlineExcept(string key) => _keys.Count(k => k != key);
        void Counted() { _onlineCount = _players.Values.Count(o => o.Joined); _keys = _players.Values.Where(o => o.Joined).Select(o => o.Key).ToArray(); }
        volatile int _onlineCount;
        readonly ManualResetEventSlim _stopped = new ManualResetEventSlim(false);

        public WorldHost(GameServer hub, ServerConfig cfg, WorldDef def)
        {
            _hub = hub; _cfg = cfg; Def = def;
            World = new ServerWorld(cfg.Dir, cfg.Seed, cfg.Kind);
            World.Changed += (x, y, z, id) => Broadcast(BlockPacket(x, y, z, id, 0));
            try { _boxes.Load(BoxFile); }
            catch (Exception e)
            {
                Log.Warn("containers.bin unreadable, keeping it aside: " + e.Message);
                try { System.IO.File.Copy(BoxFile, BoxFile + ".bad", true); } catch { }
                string stamp = Backups.Recover(BoxFile, cand => _boxes.Load(cand));
                if (stamp != null) { _boxes.Dirty = true; Log.Warn("containers.bin restored from backup " + stamp); }
                else Log.Warn("no backup of containers.bin could be read: chests start empty");
            }
            _furnaces = new Furnaces(_boxes);
            _furnaces.WakeSaved();
            // Whoever has the furnace open watches it work.
            _furnaces.Changed = (x, y, z, slot, st) =>
            {
                byte[] f = null;
                foreach (var o in _players.Values)
                    if (o.Joined && o.HasBox && o.BX == x && o.BY == y && o.BZ == z)
                        o.Conn.Send(f ??= new Packet(Net.Msg.BoxSet).I32(x).I32(y).I32(z).U8((byte)slot).Slot(st).Bytes());
                World.Signal.Wake(x, y, z);          // a comparator beside it looks again
            };
            _chutes = new Chutes(_boxes, World.Get, World.IsLoaded) { Changed = _furnaces.Changed, Fed = _furnaces.Wake };
            _chutes.LoadSaved();
            _mobs = new MobSim(World.Get, World.Meta.seed) { Difficulty = cfg.Difficulty, BiomeAt = World.Gen.BiomeAt };
            _mobs.Loaded = (x, z) => World.IsLoaded(x, z);
            _drops = new DropSim(World.Get) { Loaded = (x, z) => World.IsLoaded(x, z) };
            try { _drops.LoadFile(DropFile); } catch (Exception e) { Log.Warn("drops.bin unreadable: " + e.Message); }
            _fire = new Fire(World.Get, World.Put) { Loaded = World.IsLoaded, Spreads = cfg.Difficulty > 0 && !cfg.Creative };
            _blasts = new Blasts(World.Get, World.Put) { Loaded = World.IsLoaded };
            World.Growth.FoundFire = _fire.Found;
            World.Growth.Snowing = (x, z) => _weather != null && _weather.State != 0 && World.Gen.BiomeAt(x, z) is Biome b && (b == Biome.Tundra || b == Biome.Taiga || b == Biome.Peaks);
            _drops.Burns = (x, y, z) => (uint)y < Chunk.Height && (World.Get(x, y, z) == Blocks.Fire || Blocks.IsLava(World.Get(x, y, z)));
            World.Changed += (x, y, z, id) => { if (id == Blocks.Fire) _fire.Found(x, y, z); else if (id == Blocks.KegLit) _blasts.Prime(x, y, z); };
            _blasts.Broke = (x, y, z, d) =>
            {
                if (d.Container)
                {
                    foreach (var o in _players.Values) if (o.HasBox && o.BX == x && o.BY == y && o.BZ == z) o.HasBox = false;
                    if (_boxes.Has(x, y, z)) foreach (var st in _boxes.Remove(x, y, z)) if (!st.Empty && !Smelting.Hidden(st.Item)) _drops.Scatter(st.Item, st.Count, st.Wear, x + 0.5f, y + 0.4f, z + 0.5f);
                }
                if (_boxes.Signs.Remove(Containers.Key(x, y, z))) { _boxes.Dirty = true; Broadcast(new Packet(Net.Msg.Sign).I32(x).I32(y).I32(z).Str("")); }
                if (_dice.NextDouble() > 0.3) return;
                var (item, n) = Mining.Drop(d);
                if (item != null && n > 0) _drops.Scatter(item, n, 0, x + 0.5f, y + 0.4f, z + 0.5f);
            };
            _blasts.Boom = (x, y, z, power) =>
            {
                _mobs.Blast(x, y, z, power, _seen);
                var f = new Packet(Net.Msg.Boom).F32(x).F32(y).F32(z).F32(power).Bytes();
                foreach (var o in _players.Values) if (o.Joined && (o.X - x) * (o.X - x) + (o.Z - z) * (o.Z - z) < 96 * 96) o.Conn.Send(f);
            };
            _mobs.Burst += m => _blasts.Explode(m.X, m.Y + 0.6f, m.Z, 3);
            World.Signal.Fired = (x, y, z, dir) => Machines.Dispense(_boxes, _drops, _mobs, World.Get, World.Put, x, y, z, dir, _dice);
            World.Signal.Holds = (x, y, z) => Machines.Holds(_boxes, x, y, z);
            World.Signal.IsDay = () => _mobs.IsDay && _weather.State != 2;
            World.Signal.Played = (x, y, z, pitch) =>
            {
                var f = new Packet(Net.Msg.Note).I32(x).I32(y).I32(z).U8((byte)pitch).Bytes();
                foreach (var o in _players.Values) if (o.Joined && (o.X - x) * (o.X - x) + (o.Z - z) * (o.Z - z) < 48 * 48) o.Conn.Send(f);
            };
            World.Growth.FoundPart = (x, y, z) => World.Signal.Wake(x, y, z);
            _mobs.Laid += m => _drops.Toss("egg", 1, 0, m.X, m.Y + 0.2f, m.Z, 0, 1, 0);
            _leaves = new Decay(World.Get, World.Put);
            _leaves.Fell = (x, y, z, d) =>
            {
                var b = Mining.Bonus(d, _dice); if (b != null) _drops.Scatter(b, 1, 0, x + 0.5f, y + 0.4f, z + 0.5f);
                b = Mining.Bonus2(d, _dice); if (b != null) _drops.Scatter(b, 1, 0, x + 0.5f, y + 0.4f, z + 0.5f);
            };
            _mobs.VillageNear = (x, z) => World.Gen.Villages.Near(x, z, 96);
            _mobs.Seed = World.Meta.seed;
            _weather = new WeatherSim(World.Meta.seed ^ Environment.TickCount);
            _weather.Set(World.Meta.weather, World.Meta.weatherLeft);
            _weather.Changed += s => { _mobs.Weather = s; Broadcast(ClockPacket()); Broadcast(SaidPacket("", s == 0 ? "The weather clears." : s == 2 ? "A thunderstorm rolls in." : "It starts to rain.")); };
            _mobs.Weather = _weather.State;
            try { _mobs.LoadFile(MobFile); } catch (Exception e) { Log.Warn("creatures.bin unreadable: " + e.Message); }
            _mobs.HitPlayer += (id, dmg, fx, fz, from) =>
            {
                if (_players.TryGetValue(id, out var p) && p.Joined) p.Conn.Send(new Packet(Net.Msg.Hurt).F32(dmg).F32(fx).F32(fz).U8((byte)from.Kind));
            };
            _mobs.Died += (m, killer) =>
            {
                _players.TryGetValue(killer, out var p);
                if (p != null && !p.Joined) p = null;
                // What it carried falls where it fell; the experience goes to whoever struck the last blow.
                foreach (var (item, n) in _mobs.Loot(m))
                {
                    if (item == "xp") { if (p != null) Give(p, item, n); }
                    else if (p != null && !p.SeesDrops) Give(p, item, n);
                    else _drops.Scatter(item, n, 0, m.X, m.Y + 0.4f, m.Z);
                }
                // A broken chest cart spills what it carried.
                if (m.Kind == MobKind.ChestCart && _boxes.Has(Mob.HoldX + m.Hold, 0, 0))
                {
                    // Whoever has it open is told it is gone (as for a broken chest), so their screen closes.
                    foreach (var o in _players.Values) if (o.HasBox && o.BX == Mob.HoldX + m.Hold) { o.HasBox = false; o.Conn.Send(BlockPacket(o.BX, 0, 0, 0, 0)); }
                    foreach (var st in _boxes.Remove(Mob.HoldX + m.Hold, 0, 0)) if (!st.Empty) _drops.Scatter(st.Item, st.Count, st.Wear, m.X, m.Y + 0.5f, m.Z);
                }
                if (p == null) return;
                if (m.Def.Boss) Broadcast(SaidPacket("", $"{p.Name} defeated the {m.Def.Name}!"));
            };
            _mobs.Sound += (m, what) =>
            {
                int code = Array.IndexOf(Net.MobSounds, what);
                if (code < 0) return;
                var f = new Packet(Net.Msg.MobSound).I32(m.Id).U8((byte)m.Kind).U8((byte)code).F32(m.X).F32(m.Y).F32(m.Z).Bytes();
                foreach (var p in _players.Values)
                    if (p.Joined && (p.X - m.X) * (p.X - m.X) + (p.Z - m.Z) * (p.Z - m.Z) < 48 * 48) p.Conn.Send(f);
            };
        }

        public void Start()
        {
            _loop = new Thread(Loop) { Name = "world " + Def.Id };
            _loop.Start();
            Log.Info($"world \"{Def.Name}\" ({Def.Id}): {System.IO.Path.GetFullPath(_cfg.Dir)}, seed {World.Meta.seed}, " +
                     $"{WorldGen.KindName(World.Gen.Kind)}, {(_cfg.Creative ? "creative" : "survival")}, {new[] { "peaceful", "easy", "normal", "hard" }[Math.Clamp(_cfg.Difficulty, 0, 3)]}");
        }

        // A logged-in player chose this world.
        public void Adopt(Connection c, Accounts.Result res, Accounts.Mode mode, string version, int outfit = 0, long order = 0, int features = 0, byte[] look = null)
        {
            c.Route((cn, f) => _inbox.Enqueue((cn, f)), (cn, why) => _closed.Enqueue((cn, why)));
            _arrivals.Enqueue((c, res, mode, version, outfit, order, features, look));
        }

        // The same account is coming in to another world: end its session here.
        public void Drop(string key, long before = long.MaxValue) => _later.Enqueue(() =>
        {
            foreach (var o in _players.Values.ToList()) if (o.Joined && o.Key == key && o.Order < before) o.Conn.Close("went to another world");
        });

        public void Announce(string text) => World.Notices.Enqueue(text);

        // Saves everything and stops; returns when done.
        public void Stop()
        {
            if (_stop) return;
            _stop = true;
            _stopped.Wait(10000);
        }

        // ------------------------------------------------------------------ loop

        void Loop()
        {
            const double Tick = 0.05;
            var clock = Stopwatch.StartNew();
            bool drops = false;
            double last = 0, nextMoves = 0, nextClock = 0, nextSave = 60, nextUnload = 30, nextStats = 300, nextSecond = 1, nextMobs = 0;
            var made = new List<Chunk>();
            while (!_stop)
            {
                double now = clock.Elapsed.TotalSeconds;
                float dt = (float)Math.Min(0.25, now - last);
                last = now;
                try
                {
                    while (_arrivals.TryDequeue(out var a)) { var np = new Player { Conn = a.c, Outfit = (byte)Math.Clamp(a.outfit, 0, 255), Look = a.look, Order = a.order, Features = a.features }; _players[a.c.Id] = np; Admit(np, a.res, a.mode, a.version); }
                    while (_inbox.TryDequeue(out var m)) Handle(m.c, m.f);
                    while (_closed.TryDequeue(out var cl)) Leave(cl.c, cl.why);
                    while (_later.TryDequeue(out var a)) a();

                    made.Clear();
                    World.Collect(made);
                    foreach (var ch in made) Serve(ch);

                    while (World.Notices.TryDequeue(out var note)) Broadcast(SaidPacket("", note));
                    World.Water.Tick(dt);
                    _weather.Tick(dt);
                    _furnaces.Tick(dt);
                    _chutes.Tick(dt);
                    _near.Clear(); foreach (var pl in _players.Values) if (pl.Joined) _near.Add((pl.X, pl.Z));
                    World.Growth.Tick(dt, _near);
                    _bodies.Clear();
                    foreach (var pl in _players.Values) if (pl.Joined && pl.Alive) _bodies.Add((pl.X, pl.Y, pl.Z));
                    foreach (var m in _mobs.All) if (m.Dead <= 0 && m.Kind != MobKind.Arrow && (!m.Def.Vehicle || m.Kind != MobKind.Boat)) _bodies.Add((m.X, m.Y, m.Z));
                    World.Signal.Tick(dt, _bodies);
                    TickMobs(dt);
                    if (now >= nextMobs) { nextMobs = now + 0.1; SendMobs(); if (drops ^= true) SendDrops(); }
                    World.Meta.time += dt / 1200.0;     // Sky.DayLength
                    if (World.Meta.time >= 1) { World.Meta.time -= 1; World.Meta.day++; }

                    if (now >= nextMoves) { nextMoves = now + 0.1; SendMoves(); }
                    if (now >= nextClock) { nextClock = now + 5; Broadcast(ClockPacket()); KickSilent(); }
                    if (now >= nextSecond) { nextSecond = now + 1; CheckSleep(); foreach (var p in _players.Values) { p.Edits = 0; p.Tosses = 0; p.VoiceFrames = 0; p.Strikes = 0; p.Shots = 0; } CheckSleep(); }
                    if (now >= nextSave) { nextSave = now + 60; SaveAll(false); }
                    if (now >= nextUnload) { nextUnload = now + 30; World.Maintain(Needed, true); }
                    if (now >= nextStats) { nextStats = now + 300; Log.Info($"{_onlineCount} online, {World.Loaded} chunks loaded, {_mobs.All.Count} creatures, day {World.Meta.day}"); }
                }
                catch (Exception e) { Log.Warn("tick failed: " + e); }
                double sleep = Tick - (clock.Elapsed.TotalSeconds - now);
                if (sleep > 0) Thread.Sleep(TimeSpan.FromSeconds(sleep));
            }
            foreach (var p in _players.Values.ToList())
            {
                try
                {
                    // A boat or cart being ridden is left where it is, as if they had stepped out.
                    if (p.Joined) LeaveMount(p);
                    if (p.Joined) SavePlayer(p);
                }
                catch (Exception e) { Log.Warn($"could not save {p.Name} on the way out: {e.Message}"); }
                // Told why, so their game says so and not just that the connection was lost.
                try { p.Conn.Send(new Packet(Net.Msg.Refuse).Str("The server restarted for an update. Give it a minute, then log in again.")); } catch { }
                p.Conn.CloseAfterSending("server stopping");
            }
            if (_players.Count > 0) Thread.Sleep(700);          // long enough for that word to reach them
            try { SaveAll(true); } catch (Exception e) { Log.Warn("last save failed: " + e); }
            try { World.Stop(); } catch (Exception e) { Log.Warn("closing the world failed: " + e); }
            Log.Info("world saved, server stopped");
            _stopped.Set();
        }

        bool Needed(int cx, int cz)
        {
            int r = _cfg.MaxView + 4;
            foreach (var p in _players.Values)
                if (p.Joined && Math.Abs(p.CX - cx) <= r && Math.Abs(p.CZ - cz) <= r) return true;
            int dx = cx - (World.Meta.sx >> 4), dz = cz - (World.Meta.sz >> 4);
            return dx * dx + dz * dz <= 4;          // keep the spawn warm
        }

        void SaveAll(bool final)
        {
            World.Maintain(Needed, false);
            World.Meta.weather = _weather.State; World.Meta.weatherLeft = _weather.Left;
            try { World.SaveMeta(); } catch (Exception e) { Log.Warn("could not save world.json: " + e.Message); }
            try { _mobs.SaveFile(MobFile); } catch (Exception e) { Log.Warn("could not save creatures: " + e.Message); }
            try { _drops.SaveFile(DropFile); } catch (Exception e) { Log.Warn("could not save what lies on the ground: " + e.Message); }
            if (_boxes.Dirty) { try { _boxes.Save(BoxFile); } catch (Exception e) { Log.Warn("could not save containers: " + e.Message); } }
            foreach (var p in _players.Values) if (p.Joined) SavePlayer(p, true);
        }

        void KickSilent()
        {
            long now = Environment.TickCount64;
            foreach (var p in _players.Values.ToList())
            {
                // A connection that closed while it was being handed to this world never sends its goodbye here.
                if (!p.Conn.Open) { Leave(p.Conn, "connection closed"); continue; }
                if (now - p.Conn.LastHeard > (p.Joined ? 30000 : 10000)) p.Conn.Close(p.Joined ? "timed out" : "never said hello");
            }
        }

        // ------------------------------------------------------------------ messages

        void Handle(Connection c, byte[] frame)
        {
            if (!_players.TryGetValue(c.Id, out var p)) return;
            Reader r;
            try { r = new Reader(frame); } catch { return; }
            try
            {
                if (!p.Joined) return;
                switch (r.Type)
                {
                    case Net.Msg.Move: Move(p, r); break;
                    case Net.Msg.WantChunks: WantChunks(p, r); break;
                    case Net.Msg.SetBlock: SetBlock(p, r); break;
                    case Net.Msg.Chat: Chat(p, r); break;
                    case Net.Msg.Voice: Speak(p, r); break;
                    case Net.Msg.Keep: Keep(p, r); break;
                    case Net.Msg.Strike: Strike(p, r); break;
                    case Net.Msg.Shoot: Shoot(p, r); break;
                    case Net.Msg.UseMob: UseMob(p, r); break;
                    case Net.Msg.OpenBox: OpenBox(p, r); break;
                    case Net.Msg.BoxSlot: BoxSlot(p, r); break;
                    case Net.Msg.CloseBox: p.HasBox = false; break;
                    case Net.Msg.Sleep: p.Flags = (byte)(r.Bool() ? p.Flags | Net.Asleep : p.Flags & ~Net.Asleep); CheckSleep(); break;
                    case Net.Msg.Drop: Drop(p, r); break;
                    case Net.Msg.SignText: SignText(p, r); break;
                    case Net.Msg.Fertilize: Fertilize(p, r); break;
                    case Net.Msg.Mount: Mount(p, r); break;
                    case Net.Msg.Park: Park(p, r); break;
                    case Net.Msg.Portal: Portal(p, r); break;
                    case Net.Msg.Toss: Toss(p, r); break;
                    case Net.Msg.Splash: Splash(p, r); break;
                    case Net.Msg.OpenCart: OpenCart(p, r); break;
                    case Net.Msg.TakeDrop: TakeDrop(p, r); break;
                    case Net.Msg.Ping: c.Send(_hub.Pong(r.F64())); break;
                }
            }
            catch (Exception e) { c.Close("bad message: " + e.Message); }
        }

        void Refuse(Player p, string why)
        {
            p.Conn.Send(new Packet(Net.Msg.Refuse).Str(why));
            p.Conn.CloseAfterSending(why);
        }

        void Admit(Player p, Accounts.Result res, Accounts.Mode mode, string version)
        {
            if (!_players.TryGetValue(p.Conn.Id, out var still) || still != p || p.Joined) return;    // gone meanwhile
            if (!res.Ok) { Refuse(p, res.Error); return; }
            string name = res.Name, key = res.Key;

            // The same account coming back (a dropped connection, or another computer) replaces the old session.
            foreach (var o in _players.Values.ToList())
                if (o.Joined && o != p && o.Key == key)
                {
                    // Their place and their things as they are right now are written before the new session reads
                    // them: closing alone would save too late, and the newcomer would get what was on disk.
                    Leave(o.Conn, "logged in again elsewhere");
                    o.Conn.Close("logged in again elsewhere");
                }

            p.Name = name; p.Key = key;
            p.Rec = PlayerRecord.Load(_cfg.Dir, key) ?? new PlayerRecord();
            p.Rec.name = name;
            var m = World.Meta;
            if (p.Rec.hasPos) { p.X = p.Rec.x; p.Y = p.Rec.y; p.Z = p.Rec.z; p.Yaw = p.Rec.yaw; p.Pitch = p.Rec.pitch; }
            else { p.X = m.sx + 0.5f; p.Y = m.sy; p.Z = m.sz + 0.5f; }
            p.Joined = true;
            Counted();

            var w = new Packet(Net.Msg.Welcome)
                .I32(p.Conn.Id).I32(m.seed).Str(_hub.Worlds.Count > 1 ? $"{_cfg.Name} · {Def.Name}" : _cfg.Name).Str(_cfg.Motd).Bool(_cfg.Creative).U8((byte)_cfg.MaxView)
                .F64(m.time).I32(m.day).I32(m.sx).I32(m.sy).I32(m.sz)
                .Bool(p.Rec.hasPos).F32(p.X).F32(p.Y).F32(p.Z).F32(p.Yaw).F32(p.Pitch).F32(p.Rec.health).U8((byte)Math.Clamp(p.Rec.selected, 0, 8))
                .Stacks(p.Rec.Stacks())
                .Str(name).Str(res.Token ?? "")
                .F32(p.Rec.food).F32(p.Rec.saturation).Bool(p.Rec.hasBed).I32(p.Rec.bx).I32(p.Rec.by).I32(p.Rec.bz).U8((byte)_cfg.Difficulty).I32(p.Rec.xpLevel).I32(p.Rec.xpPoints)
                .U8((byte)World.Gen.Kind)                                   // 0.4: what sort of world (older clients stop reading before it)
                .Bool(_cfg.Keep)                                            // 0.5.1: things are kept on death here
                .Str(p.Rec.notes ?? "")                                     // 0.17: goals reached and statistics, kept with the account
                .MoreStacks(p.Rec.Stacks());                                // 0.16.4: what a list of 255 stacks had no room for
            p.Conn.Send(w);
            foreach (var kv in _boxes.Signs)
            {
                var (sx, sy, sz) = Containers.Unkey(kv.Key);
                p.Conn.Send(new Packet(Net.Msg.Sign).I32(sx).I32(sy).I32(sz).Str(kv.Value));
            }
            foreach (var o in _players.Values)
                if (o.Joined && o != p)
                {
                    p.Conn.Send(JoinedPacket(o));
                    o.Conn.Send(JoinedPacket(p));
                }
            Broadcast(SaidPacket("", mode == Accounts.Mode.Create ? $"{name} joined the game for the first time!" : $"{name} joined the game"));
            if (mode == Accounts.Mode.Create && _hub.IsOp(name)) p.Conn.Send(SaidPacket("", "You made the first account, so you are this server's operator: /give, /time set, /op <name>."));
            Log.Info($"{name} joined from {p.Conn.Address} ({version}{(mode == Accounts.Mode.Create ? ", new account" : "")}), {_onlineCount} online");
        }

        void Leave(Connection c, string why)
        {
            if (!_players.TryGetValue(c.Id, out var p)) return;
            _players.Remove(c.Id);
            // Leaving in a boat or cart leaves it where they were.
            if (p.Joined) LeaveMount(p);
            foreach (long k in p.Waiting) if (_waiters.TryGetValue(k, out var l)) l.Remove(p);
            if (!p.Joined) return;
            SavePlayer(p, true);
            // Their chests and their building are written in the same breath as their pack, so a crash right after
            // can't leave an item both in the pack on disk and in the chest on disk (or in neither).
            if (_boxes.Dirty) { try { _boxes.Save(BoxFile); } catch (Exception e) { Log.Warn("could not save containers: " + e.Message); } }
            World.Maintain(Needed, false);
            Counted();
            var left = new Packet(Net.Msg.Left).I32(c.Id).Bytes();
            foreach (var o in _players.Values) if (o.Joined) o.Conn.Send(left);
            if (!_players.Values.Any(o => o.Joined && o.Key == p.Key)) Broadcast(SaidPacket("", $"{p.Name} left the game"));
            CheckSleep();
            Log.Info($"{p.Name} left ({why}), {_onlineCount} online");
        }

        void SavePlayer(Player p, bool leaving = false)
        {
            p.Rec.hasPos = true;
            p.Rec.x = p.X; p.Rec.y = p.Y; p.Rec.z = p.Z; p.Rec.yaw = p.Yaw; p.Rec.pitch = p.Pitch;
            // Lost mid-journey (waiting high over the far side of a portal): they come back where they set out.
            if (p.Away) { p.Rec.x = p.AX; p.Rec.y = p.AY; p.Rec.z = p.AZ; }
            if ((p.Owed.Count > 0 || p.Transit.Count > 0) && leaving)
            {
                // Handed over but never seen in their pack, or moved by their own game since its last word: the saved
                // pack is settled, so nothing is lost and nothing is doubled.
                var owed = new List<Stack>(); foreach (var o in p.Owed) owed.Add(o.s);
                p.Rec.SetStacks(Settle(p.Rec.Stacks(), p.Transit, owed));
                Log.Info($"{p.Name} left with {p.Owed.Count + p.Transit.Count} thing(s) between their pack and the world; settled");
                p.Owed.Clear(); p.Transit.Clear();
            }
            p.Rec.lastSeen = DateTime.UtcNow.Ticks;
            p.Rec.Save(_cfg.Dir, p.Key);
        }

        // Voice: passed on as it is to everyone else (the game places it at the speaker's figure).
        void Speak(Player p, Reader r)
        {
            var f = r.Blob();
            if (f.Length != Voice.FrameBytes || ++p.VoiceFrames > Voice.MaxFramesPerSecond) return;
            var heard = new Packet(Net.Msg.Heard).I32(p.Conn.Id).Blob(f).Bytes();
            foreach (var o in _players.Values) if (o.Joined && o != p) o.Conn.Send(heard);
        }

        void Move(Player p, Reader r)
        {
            float x = r.F32(), y = r.F32(), z = r.F32(), yaw = r.F32(), pitch = r.F32();
            byte flags = r.U8(); string held = r.Str();
            if (Math.Abs(x) > 3e7 || y < -64 || y > 512 || Math.Abs(z) > 3e7) return;
            // A leap (a portal, /tp, /home): until they next stand on something, where they came from is where they are.
            if (!p.Away && (Math.Abs(x - p.X) > 48 || Math.Abs(z - p.Z) > 48)) { p.Away = true; p.AwayAt = Environment.TickCount64; p.AX = p.X; p.AY = p.Y; p.AZ = p.Z; }
            p.X = x; p.Y = y; p.Z = z; p.Yaw = yaw; p.Pitch = Math.Clamp(pitch, -90, 90); p.Flags = flags;
            if ((flags & (Net.InBoat | Net.InCart)) != 0) p.Boarded = null;        // their game knows they're riding now
            // A "~s" on the end says there's a shield in the other hand (passed on, and no part of what is held).
            p.Shield = held.EndsWith("~s"); if (p.Shield) held = held.Substring(0, held.Length - 2);
            p.Held = held.Length > 40 ? "" : held;
            p.Moved = true;
            if (y > 1 && y < Chunk.Height)
            {
                int bx = (int)Math.Floor(x), bz = (int)Math.Floor(z), by = (int)Math.Floor(y - 0.2f);
                if (World.IsLoaded(bx, bz) && Blocks.Get(World.Get(bx, by, bz)).Solid) { p.HasSafe = true; p.SX = x; p.SY = y; p.SZ = z; p.Away = false; }
            }
            // Flying, swimming or riding there for half a minute is being there too (a builder who flew off after /tp).
            if (p.Away && (flags & (Net.Flying | Net.InWater | Net.InBoat | Net.InCart)) != 0 && Environment.TickCount64 - p.AwayAt > 30000) p.Away = false;
        }

        void WantChunks(Player p, Reader r)
        {
            int n = r.U16();
            int lim = _cfg.MaxView + 4;
            for (int i = 0; i < n; i++)
            {
                int cx = r.I32(), cz = r.I32();
                if (Math.Abs(cx - p.CX) > lim || Math.Abs(cz - p.CZ) > lim) continue;   // too far from them
                long k = Chunk.Key(cx, cz);
                if (World.Want(cx, cz)) { World.TryGet(cx, cz, out var c); SendChunk(p, c); }
                else
                {
                    if (!_waiters.TryGetValue(k, out var l)) _waiters[k] = l = new List<Player>();
                    if (!l.Contains(p)) { l.Add(p); p.Waiting.Add(k); }
                }
            }
        }

        void Serve(Chunk c)
        {
            long k = Chunk.Key(c.X, c.Z);
            if (!_waiters.TryGetValue(k, out var l)) return;
            _waiters.Remove(k);
            foreach (var p in l) { p.Waiting.Remove(k); if (p.Conn.Open) SendChunk(p, c); }
        }

        void SendChunk(Player p, Chunk c) => p.Conn.Send(new Packet(Net.Msg.Chunk).I32(c.X).I32(c.Z).Blob(World.Packed(c)));

        void SetBlock(Player p, Reader r)
        {
            int x = r.I32(), y = r.I32(), z = r.I32(); ushort id = (ushort)r.U16(), old = (ushort)r.U16();
            if (!World.IsLoaded(x, z) || (uint)y >= Chunk.Height) return;
            if (++p.Edits > 40) { p.Conn.Send(BlockPacket(x, y, z, World.Get(x, y, z), (p.Features & GameServer.KnowsRefusals) != 0 ? Refused : 0)); return; }      // too fast: show them it didn't happen
            ushort cur = World.Get(x, y, z);
            // Edits must be in reach, of a real block, by a known rule, and agree on what was there.
            float dx = x + 0.5f - p.X, dy = y + 0.5f - (p.Y + 1.6f), dz = z + 0.5f - p.Z;
            string why = dx * dx + dy * dy + dz * dz > (Net.Reach + 2) * (Net.Reach + 2) ? $"out of reach ({Math.Sqrt(dx * dx + dy * dy + dz * dz):0.0} m)"
                       : id >= Blocks.Max || Blocks.Defs[id] == null ? $"no block {id}"
                       : cur != old ? $"it was {Blocks.Get(cur).Name}, not {Blocks.Get(old).Name}"
                       : !_cfg.Creative && Blocks.Get(cur).Hardness < 0 && !(Blocks.Get(cur).Fluid != 0 && Blocks.Get(cur).Level == 0 && id == 0) ? "unbreakable"
                       : id != 0 && cur != 0 && !Blocks.Get(cur).Replaceable && !Blocks.Changes(cur, id) ? "something is in the way"
                       : Blocks.Get(cur).Container && !Blocks.Get(id).Container && InUse(p, x, y, z, out string user) ? $"{user} has it open"
                       : null;
            if (why != null)
            {
                // Put them right. A game that knows a refusal from a change hands back what the edit cost, even when
                // what's there is the very thing it tried to place (two people building in the same spot).
                p.Conn.Send(BlockPacket(x, y, z, cur, (p.Features & GameServer.KnowsRefusals) != 0 ? Refused : 0));
                if (why.EndsWith("has it open")) p.Conn.Send(SaidPacket("", why.Replace("has it open", "is using that. Wait for them to close it.")));
                if (p.Refused++ < 20) Log.Info($"refused {p.Name}'s edit at {x} {y} {z}: {why}");
                return;
            }
            World.SetRaw(x, y, z, id);
            // Something put down where there was nothing: it came out of their pack (cut off before their game says
            // so, they must not keep both the block and the thing it was).
            if (id != 0 && !_cfg.Creative && (cur == 0 || Blocks.Get(cur).Replaceable) && ItemFor(id) is string spent) p.Transit.Add((spent, -1, 0));
            if (id == 0 && Decay.IsLog(Blocks.Get(cur))) _leaves.LogBroken(x, y, z);
            if (id == Blocks.Fire) _fire.Found(x, y, z); else if (id == Blocks.KegLit) _blasts.Prime(x, y, z);
            if (id == 0 || Blocks.Get(id).BaseId != Blocks.Get(cur).BaseId) { p.Broke[p.BrokeAt] = (x, y, z, Environment.TickCount64); p.BrokeAt = (p.BrokeAt + 1) % p.Broke.Length; }
            Broadcast(BlockPacket(x, y, z, id, p.Conn.Id));
            if (Chutes.Is(id)) _chutes.Add(x, y, z);
            World.Water.Wake(x, y, z);
            World.Signal.Wake(x, y, z);
            if (_boxes.Signs.Remove(Containers.Key(x, y, z))) { _boxes.Dirty = true; Broadcast(new Packet(Net.Msg.Sign).I32(x).I32(y).I32(z).Str("")); }
            if (Blocks.Get(cur).Container && !Blocks.Get(id).Container)
                foreach (var o in _players.Values) if (o.HasBox && o.BX == x && o.BY == y && o.BZ == z) o.HasBox = false;
            if (Blocks.Get(cur).Container && !Blocks.Get(id).Container && _boxes.Has(x, y, z))
                foreach (var st in _boxes.Remove(x, y, z))
                {
                    if (st.Empty || Smelting.Hidden(st.Item)) continue;
                    // A broken chest spills on the ground (a satchel hands its things straight back to its owner's pack).
                    if (p.SeesDrops && cur != Blocks.Satchel) _drops.Scatter(st.Item, st.Count, st.Wear, x + 0.5f, y + 0.4f, z + 0.5f);
                    else Give(p, st.Item, st.Count, st.Wear);
                }
        }

        // Whether someone else has the container at this place open.
        bool InUse(Player p, int x, int y, int z, out string who)
        {
            foreach (var o in _players.Values)
                if (o != p && o.Joined && o.HasBox && o.BX == x && o.BY == y && o.BZ == z)
                {
                    // Someone who walked off with the screen still open (or whose game never said it closed) lets go.
                    if (!InReach(o, x, y, z)) { o.HasBox = false; continue; }
                    who = o.Name; return true;
                }
            who = null; return false;
        }

        void Chat(Player p, Reader r)
        {
            string text = r.Str().Trim();
            if (text.Length == 0) return;
            if (text.Length > 200) text = text.Substring(0, 200);
            if (text == "/list" || text == "/who")
            {
                var names = _players.Values.Where(o => o.Joined).Select(o => o.Name).OrderBy(n => n);
                p.Conn.Send(SaidPacket("", $"Online ({_onlineCount}/{_cfg.MaxPlayers}): {string.Join(", ", names)}"));
                return;
            }
            if (text.StartsWith("/tpnote ")) { Broadcast(SaidPacket("", $"{p.Name} went to {text.Substring(8).Trim()}")); return; }
            if (text.StartsWith("/weather"))
            {
                int w = WeatherSim.Parse(text.Substring(8).Trim().ToLowerInvariant());
                if (w < 0) { p.Conn.Send(SaidPacket("", "/weather clear, /weather rain or /weather thunder")); return; }
                _weather.Set(w, w == 0 ? 900 : 480);
                Broadcast(SaidPacket("", $"{p.Name} changed the weather to {WeatherSim.Name(w)}"));
                return;
            }
            if (text.StartsWith("/tp "))
            {
                // Three numbers: an operator going to a place (a name is handled by the game itself).
                var c = text.Substring(4).Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (c.Length == 3 && float.TryParse(c[0], out float tx) && float.TryParse(c[1], out float ty) && float.TryParse(c[2], out float tz))
                {
                    if (!_hub.IsOp(p.Name)) { p.Conn.Send(SaidPacket("", "Only this server's operators can go to a place by its numbers.")); return; }
                    if (Math.Abs(tx) > 3e6f || Math.Abs(tz) > 3e6f || ty < -20 || ty > 400) { p.Conn.Send(SaidPacket("", "That's off the edge of the world.")); return; }
                    p.Conn.Send(new Packet(Net.Msg.Arrived).F32(tx).F32(ty).F32(tz));
                    return;
                }
            }
            if (text == "/ops") { p.Conn.Send(SaidPacket("", "Operators: " + string.Join(", ", _hub.OpNames()))); return; }
            if (text.StartsWith("/op ") || text.StartsWith("/deop ") || text == "/op" || text == "/deop")
            {
                if (!_hub.IsOp(p.Name)) { p.Conn.Send(SaidPacket("", "Only this server's operators can do that.")); return; }
                bool on = text.StartsWith("/op");
                string who = text.Substring(on ? 3 : 5).Trim();
                if (who.Length == 0) { p.Conn.Send(SaidPacket("", "/op <name> makes someone an operator; /deop <name> takes it away; /ops lists them.")); return; }
                string why = _hub.SetOp(who, on);
                if (why != null) { p.Conn.Send(SaidPacket("", why)); return; }
                Broadcast(SaidPacket("", on ? $"{p.Name} made {who} an operator." : $"{p.Name} took {who}'s operator rights away."));
                return;
            }
            if (text.StartsWith("/give") || text.StartsWith("/time") || text == "/day" || text == "/night")
            {
                // For the server's operators (STRATUM_OPS).
                if (!_hub.IsOp(p.Name)) { p.Conn.Send(SaidPacket("", "Only this server's operators can do that.")); return; }
                var a = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (a[0] == "/give")
                {
                    var d = Items.Ask(text.Substring(5), out int n, out string say);
                    if (d == null) { p.Conn.Send(SaidPacket("", say)); return; }
                    for (int left = n; left > 0; left -= d.MaxStack) Give(p, d.Id, Math.Min(left, d.MaxStack), 0);
                    Log.Info($"{p.Name} gave themselves {n} {d.Id}");
                    p.Conn.Send(SaidPacket("", $"Given {n} {d.Name}."));
                    return;
                }
                float t = a[0] == "/day" ? 0.27f : a[0] == "/night" ? 0.8f : a.Length == 3 && a[1] == "set" ? WeatherSim.TimeOf(a[2].ToLowerInvariant()) : -1;
                if (t < 0) { p.Conn.Send(SaidPacket("", "/time set day, noon, night or midnight (or a number from 0 to 24000)")); return; }
                World.Meta.time = t;
                Broadcast(ClockPacket());
                Broadcast(SaidPacket("", $"{p.Name} set the time."));
                return;
            }
            if (text.StartsWith("/")) { p.Conn.Send(SaidPacket("", "Commands: /list  /spawn  /home  /tp <name>  /weather <clear|rain|thunder>  /ops   Operators: /give <thing> [how many]  /time set <day|night>  /day  /night  /tp <x> <y> <z>  /op <name>  /deop <name>")); return; }
            Broadcast(SaidPacket(p.Name, text));
            Log.Info($"<{p.Name}> {text}");
        }

        void Keep(Player p, Reader r)
        {
            p.Transit.Clear();                  // their game's pack, as of now, counts all it has done
            p.Rec.health = Math.Clamp(r.F32(), 0, 20);
            p.Rec.food = Math.Clamp(r.F32(), 0, 20);
            p.Rec.saturation = Math.Clamp(r.F32(), 0, 20);
            p.Rec.selected = r.U8();
            var st = r.Stacks();
            p.Rec.hasBed = r.Bool(); p.Rec.bx = r.I32(); p.Rec.by = r.I32(); p.Rec.bz = r.I32();
            p.Rec.xpLevel = Math.Clamp(r.I32(), 0, 10000); p.Rec.xpPoints = Math.Clamp(r.I32(), 0, 100000);
            if (r.More) { string notes = r.Str(); if (notes.Length <= 4000) p.Rec.notes = notes; }      // 0.17: goals reached and statistics
            // 0.16.3: how many handed-over things this pack already counts. An older game doesn't say, and is trusted.
            if (r.More) { int seen = r.I32(); p.Owed.RemoveAll(o => o.n <= seen); } else p.Owed.Clear();
            st = r.WithMore(st);                 // 0.16.4: the stacks a list of 255 had no room for
            // Past the pack's own slots: what the cursor or a station held, what is waiting for room. More than the
            // most a player may have is cut short, not thrown out whole (the pack itself comes first).
            if (st.Length > Inventory.Size + Net.MaxExtraStacks) Array.Resize(ref st, Inventory.Size + Net.MaxExtraStacks);
            if (st.Length >= Inventory.Size) p.Rec.SetStacks(st);
        }

        // ------------------------------------------------------------------ creatures

        void TickMobs(float dt)
        {
            _seen.Clear();
            foreach (var p in _players.Values)
                if (p.Joined) _seen.Add(new Seen { Id = p.Conn.Id, X = p.X, Y = p.Y, Z = p.Z, Sneaking = (p.Flags & Net.Sneaking) != 0, Creative = _cfg.Creative, Alive = p.Alive, Sleeping = p.Asleep, Key = p.Key, Held = p.Held });
            _mobs.Time01 = (float)World.Meta.time;
            _mobs.Tick(dt, _seen);
            _drops.Tick(dt);
            if ((_feed -= dt) <= 0) { _feed = 0.4f; _drops.Feed(_chutes.Take); }
            _leaves.Tick(dt);
            _fire.Tick(dt);
            _blasts.Tick(dt);
            // A thunderstorm now and then sets something alight near someone.
            if (_weather.State == 2 && _fire.Spreads && (_bolt -= dt) <= 0 && _seen.Count > 0)
            {
                _bolt = 25 + (float)_dice.NextDouble() * 50;
                var who = _seen[_dice.Next(_seen.Count)];
                int bx = (int)Math.Floor(who.X) + _dice.Next(-40, 41), bz = (int)Math.Floor(who.Z) + _dice.Next(-40, 41);
                if (World.IsLoaded(bx, bz))
                    for (int y = Chunk.Height - 2; y > 1; y--)
                    {
                        ushort id = World.Get(bx, y, bz);
                        if (id == 0) continue;
                        if (Blocks.Get(id).Burns) _fire.Light(bx, y + 1, bz);
                        break;
                    }
            }
        }

        // ------------------------------------------------------------------ things on the ground

        // Thrown from a pack, or fallen out of a block the player just broke.
        void Toss(Player p, Reader r)
        {
            string item = r.Str(); int count = r.U16(), wear = r.U16();
            float x = r.F32(), y = r.F32(), z = r.F32(), vx = r.F32(), vy = r.F32(), vz = r.F32(); bool mined = r.Bool();
            if (Items.Get(item) == null || count <= 0 || Smelting.Hidden(item)) return;
            if (!mined && !_cfg.Creative) p.Transit.Add((item, -Math.Min(count, 640), wear));      // out of their pack, whatever becomes of it
            if (++p.Tosses > 40 || !p.Alive) return;
            float dx = x - p.X, dy = y - (p.Y + 1), dz = z - p.Z;
            if (float.IsNaN(dx + dy + dz) || dx * dx + dy * dy + dz * dz > (Net.Reach + 3) * (Net.Reach + 3)) return;
            if (mined)
            {
                // Only beside a block this player really broke a moment ago (a refused edit drops nothing).
                long now = Environment.TickCount64; bool ok = false;
                foreach (var b in p.Broke) if (now - b.at < 2000 && Math.Abs(b.x + 0.5f - x) <= 2.5f && Math.Abs(b.y + 0.5f - y) <= 3.5f && Math.Abs(b.z + 0.5f - z) <= 2.5f) { ok = true; break; }
                if (!ok || _cfg.Creative) return;
                _drops.Scatter(item, count, wear, x, y, z);
            }
            else _drops.Toss(item, count, wear, x, y, z, vx, vy, vz, 1.2f);
        }

        // A splash potion burst: everyone near hears of it (their own games decide whether it reached them).
        void Splash(Player p, Reader r)
        {
            float x = r.F32(), y = r.F32(), z = r.F32(); string effect = r.Str(); float secs = Math.Clamp(r.F32(), 0, 300);
            if (++p.Tosses > 40 || !p.Alive || effect.Length > 16 || float.IsNaN(x + y + z)) return;
            float dx = x - p.X, dy = y - (p.Y + 1), dz = z - p.Z;
            if (dx * dx + dy * dy + dz * dz > 12 * 12) return;
            var f = new Packet(Net.Msg.Splashed).F32(x).F32(y).F32(z).Str(effect).F32(secs).Bytes();
            foreach (var o in _players.Values) if (o.Joined && (o.X - x) * (o.X - x) + (o.Z - z) * (o.Z - z) < 32 * 32) o.Conn.Send(f);
        }

        // Something is handed to a player. Until a backpack update says their game has it, the server remembers it
        // is theirs: a connection lost in between must not lose it (their pack is only sent every few seconds).
        void Give(Player p, string item, int count, int wear)
        {
            p.Owed.Add((++p.Given, new Stack { Item = item, Count = count, Wear = wear }));
            p.Conn.Send(new Packet(Net.Msg.Give).Str(item).U16(count).U16(wear));
        }

        // The thing in a pack that becomes this block when put down (seeds for wheat, the block itself for stone).
        static Dictionary<ushort, string> _itemFor;
        static string ItemFor(ushort block)
        {
            if (_itemFor == null)
            {
                var map = new Dictionary<ushort, string>();
                foreach (var it in Items.Order) if (it.Block != 0 && !map.ContainsKey(it.Block)) map[it.Block] = it.Id;
                _itemFor = map;
            }
            return _itemFor.TryGetValue(Blocks.Get(block).BaseId, out var id) ? id : null;
        }

        void Carried(Player p, Stack before, Stack after)
        {
            if (_cfg.Creative) return;
            if (!before.Empty && !after.Empty && before.Item == after.Item && before.Wear == after.Wear) { if (before.Count != after.Count) p.Transit.Add((before.Item, before.Count - after.Count, before.Wear)); return; }
            if (!before.Empty && !Smelting.Hidden(before.Item)) p.Transit.Add((before.Item, before.Count, before.Wear));
            if (!after.Empty && !Smelting.Hidden(after.Item)) p.Transit.Add((after.Item, -after.Count, after.Wear));
        }

        // A connection lost with things in transit: the saved pack is made to say what their game's pack said.
        static Stack[] Settle(Stack[] pack, List<(string item, int n, int wear)> transit, IEnumerable<Stack> owed)
        {
            var all = new List<Stack>(pack);
            // A shield kept last of all was in their other hand, and must stay last to go back there.
            int at = all.Count > Inventory.Size && all[all.Count - 1].Item == "shield" ? all.Count - 1 : all.Count;
            void Add(Stack s) { if (all.Count < Inventory.Size + Net.MaxExtraStacks) all.Insert(at++, s); }
            foreach (var s in owed) Add(s);
            foreach (var (item, n, wear) in transit)
            {
                if (n > 0) { Add(new Stack(item, n) { Wear = wear }); continue; }
                int left = -n;
                // The very thing first (the same wear), then any of its kind.
                for (int pass = 0; pass < 2 && left > 0; pass++)
                    for (int i = all.Count - 1; i >= 0 && left > 0; i--)
                    {
                        var s = all[i];
                        if (s.Empty || s.Item != item || pass == 0 && s.Wear != wear) continue;
                        int take = Math.Min(left, s.Count); left -= take; s.Count -= take;
                        all[i] = s.Count > 0 ? s : Stack.None;
                    }
            }
            return all.ToArray();
        }

        void TakeDrop(Player p, Reader r)
        {
            int id = r.I32();
            if (!p.Alive) return;
            if (_drops.Take(id, p.X, p.Y, p.Z) is (string item, int count, int wear))
                Give(p, item, count, wear);
        }

        // Each player hears what lies within sight of them, five times a second.
        void SendDrops()
        {
            foreach (var p in _players.Values)
            {
                if (!p.Joined || !p.SeesDrops) continue;
                int n = 0;
                foreach (var d in _drops.All) { float dx = d.X - p.X, dz = d.Z - p.Z; if (dx * dx + dz * dz < 48 * 48) n++; }
                var pk = new Packet(Net.Msg.Drops).U16(Math.Min(n, 300));
                int k = 0;
                foreach (var d in _drops.All)
                {
                    float dx = d.X - p.X, dz = d.Z - p.Z;
                    if (dx * dx + dz * dz >= 48 * 48 || k++ >= 300) continue;
                    pk.I32(d.Id).Str(d.Item).U16(d.Count).F32(d.X).F32(d.Y).F32(d.Z);
                }
                p.Conn.Send(pk);
            }
        }

        // Each player hears about the creatures within sight of them, ten times a second.
        void SendMobs()
        {
            foreach (var p in _players.Values)
            {
                if (!p.Joined) continue;
                int n = 0;
                foreach (var m in _mobs.All) if (Near(m, p, 96)) n++;
                var pk = new Packet(Net.Msg.Mobs).U16(Math.Min(n, 400));
                int k = 0;
                foreach (var m in _mobs.All)
                {
                    if (!Near(m, p, 96) || k++ >= 400) continue;
                    pk.I32(m.Id).U8((byte)m.Kind).F32(m.X).F32(m.Y).F32(m.Z)
                      .U8((byte)((int)Math.Round(m.Yaw / 360f * 256) & 255)).U8((byte)(sbyte)Math.Clamp((int)m.Pitch, -90, 90)).U8(m.Flags)
                      .U8((byte)Math.Clamp((int)Math.Ceiling(m.Health / m.Def.Health * 255), 0, 255)).U8(m.More);
                }
                p.Conn.Send(pk);
            }
        }

        // Whatever they were riding is left where they were (both ride flags together mean a camel).
        void LeaveMount(Player p)
        {
            bool boat = (p.Flags & Net.InBoat) != 0, cart = (p.Flags & Net.InCart) != 0;
            // Got on moments ago and their game hadn't yet said so: it still mustn't vanish with them.
            if (!boat && !cart && p.Boarded != null && Environment.TickCount64 - p.BoardedAt < 8000)
            {
                var k = p.Boarded.Value; p.Boarded = null;
                _mobs.Park(k, p.X, p.Y, p.Z, p.Yaw, k == MobKind.Camel ? p.Key : "");
                return;
            }
            if (boat && cart) _mobs.Park(MobKind.Camel, p.X, p.Y, p.Z, p.Yaw, p.Key);
            else if (boat) _mobs.Park(MobKind.Boat, p.X, p.Y, p.Z, p.Yaw);
            else if (cart) _mobs.Park(MobKind.Cart, p.X, p.Y, p.Z, p.Yaw);
        }

        static bool Near(Mob m, Player p, float r) { float dx = m.X - p.X, dz = m.Z - p.Z; return dx * dx + dz * dz < r * r; }

        void Strike(Player p, Reader r)
        {
            int id = r.I32(); float dmg = Math.Clamp(r.F32(), 0, 14);
            if (++p.Strikes > 8) return;
            _mobs.Strike(id, p.Conn.Id, dmg, p.X, p.Y, p.Z);
        }

        void Shoot(Player p, Reader r)
        {
            float x = r.F32(), y = r.F32(), z = r.F32(), vx = r.F32(), vy = r.F32(), vz = r.F32(), dmg = Math.Clamp(r.F32(), 0, 10);
            if (++p.Shots > 4) return;
            float dx = x - p.X, dy = y - (p.Y + 1.5f), dz = z - p.Z;
            if (dx * dx + dy * dy + dz * dz > 9 || vx * vx + vy * vy + vz * vz > 60 * 60) return;
            _mobs.Shoot(p.Conn.Id, x, y, z, vx, vy, vz, dmg);
        }

        void UseMob(Player p, Reader r)
        {
            int id = r.I32(); string item = r.Str();
            var got = _mobs.Use(id, item, p.X, p.Z, p.Key);
            if (got is (string it, int n)) Give(p, it, n);
        }

        void Give(Player p, string item, int n)
        {
            if (Items.Get(item) == null || n <= 0) return;
            Give(p, item, n, 0);
        }

        // ------------------------------------------------------------------ chests, satchels and beds

        bool InReach(Player p, int x, int y, int z)
        {
            float dx = x + 0.5f - p.X, dy = y + 0.5f - (p.Y + 1.6f), dz = z + 0.5f - p.Z;
            return dx * dx + dy * dy + dz * dz <= (Net.Reach + 2) * (Net.Reach + 2);
        }

        void OpenBox(Player p, Reader r)
        {
            int x = r.I32(), y = r.I32(), z = r.I32();
            if (InReach(p, x, y, z) && World.Get(x, y, z) == Blocks.LootChest)
            {
                // A dusty chest fills itself the first time it's opened, and is an ordinary chest from then on.
                var loot = Ruins.Roll(World.Meta.seed, x, y, z);
                World.Put(x, y, z, Blocks.Chest);
                for (int i = 0; i < loot.Length; i++) if (!loot[i].Empty) _boxes.Set(x, y, z, i, loot[i]);
            }
            if (!InReach(p, x, y, z) || !Blocks.Get(World.Get(x, y, z)).Container) return;
            // One pair of hands in a chest at a time: two people moving the same stack at once could copy or lose it.
            if (InUse(p, x, y, z, out string user)) { p.Conn.Send(SaidPacket("", $"{user} is using that. Wait for them to close it.")); return; }
            if (Blocks.Get(World.Get(x, y, z)).BaseId == Blocks.Furnace && (p.Features & GameServer.KnowsTransfers) == 0)
            { p.Conn.Send(SaidPacket("", "Furnaces need the newest Stratum. Get it at stratum.rushingtechnologies.com")); return; }
                        p.HasBox = true; p.BX = x; p.BY = y; p.BZ = z;
            p.Conn.Send(new Packet(Net.Msg.Box).I32(x).I32(y).I32(z).Stacks(_boxes.At(x, y, z)));
        }

        // A chest cart's store lives at a place of its own (Mob.HoldX + its number); it is within reach while the cart is.
        bool CartNear(Player p) => _mobs.TryGet(p.BoxMob, out var m) && m.Kind == MobKind.ChestCart && m.Dead <= 0 && (m.X - p.X) * (m.X - p.X) + (m.Y - p.Y) * (m.Y - p.Y) + (m.Z - p.Z) * (m.Z - p.Z) < 8 * 8;
        bool BoxReach(Player p, int x, int y, int z) => x >= Mob.HoldX ? CartNear(p) : InReach(p, x, y, z);

        void OpenCart(Player p, Reader r)
        {
            int id = r.I32();
            if (!p.Alive || !_mobs.TryGet(id, out var m) || m.Kind != MobKind.ChestCart || m.Dead > 0) return;
            if ((m.X - p.X) * (m.X - p.X) + (m.Y - p.Y) * (m.Y - p.Y) + (m.Z - p.Z) * (m.Z - p.Z) > 7 * 7) return;
            int x = Mob.HoldX + _mobs.HoldOf(m);
            foreach (var o in _players.Values)
                if (o != p && o.Joined && o.HasBox && o.BX == x && o.BY == 0 && o.BZ == 0)
                {
                    if (!CartNear(o)) { o.HasBox = false; continue; }
                    p.Conn.Send(SaidPacket("", $"{o.Name} is using that. Wait for them to close it.")); return;
                }
            p.HasBox = true; p.BX = x; p.BY = 0; p.BZ = 0; p.BoxMob = id;
            p.Conn.Send(new Packet(Net.Msg.Box).I32(x).I32(0).I32(0).Stacks(_boxes.At(x, 0, 0)));
        }

        void BoxSlot(Player p, Reader r)
        {
            int x = r.I32(), y = r.I32(), z = r.I32(), slot = r.U8(); var st = r.Slot();
            bool hasSeen = r.More; var seen = hasSeen ? r.Slot() : default;       // 0.7.2: what the player saw in the slot before changing it
            if (!p.HasBox || p.BX != x || p.BY != y || p.BZ != z || slot >= Containers.MaxSlots) return;
            if (!BoxReach(p, x, y, z)) { p.HasBox = false; return; }
            if (x < Mob.HoldX && !Blocks.Get(World.Get(x, y, z)).Container) return;
            if (slot >= Math.Max(Containers.ChestSlots, _boxes.At(x, y, z).Length)) return;      // a satchel is as big as what fell into it
            bool furnace = Blocks.Get(World.Get(x, y, z)).BaseId == Blocks.Furnace;
            if (Smelting.Hidden(st.Item) || furnace && slot > Smelting.Out) return;
            if (Chutes.Is(World.Get(x, y, z)) && slot >= Chutes.Slots) return;                     // a chute has five slots              // the fire and the progress are the furnace's own
            // What they did is applied to what the slot really holds (a furnace may have changed it since they looked).
            var want = st;
            if (furnace && !hasSeen) return;       // a furnace changes under the player's hands: only games that say what they saw may use one
            // What their game did to its own pack: what it saw in the slot came out, what it put there went in.
            Carried(p, hasSeen ? seen : _boxes.At(x, y, z, Math.Max(Containers.ChestSlots, slot + 1))[slot], want);
            _owed.Clear();
            if (hasSeen) st = Containers.Reconcile(_boxes.At(x, y, z, Math.Max(Containers.ChestSlots, slot + 1))[slot], seen, want, _owed);
            _boxes.Set(x, y, z, slot, st);
            if (furnace) _furnaces.Wake(x, y, z);
            World.Signal.Wake(x, y, z);
            if (!Containers.Same(st, want)) p.Conn.Send(new Packet(Net.Msg.BoxSet).I32(x).I32(y).I32(z).U8((byte)slot).Slot(st));     // put their screen right
            // And their pack: the game counted what it saw as taken. What wasn't there comes back out of their
            // hands, and what didn't fit goes back into them, so nothing is made or lost.
            foreach (var (item, n, wear) in _owed)
            {
                if (Smelting.Hidden(item)) continue;
                if (n > 0) Give(p, item, n, wear);
                else { p.Conn.Send(new Packet(Net.Msg.TakeBack).Str(item).U16(-n)); if (!_cfg.Creative) p.Transit.Add((item, n, wear)); }
            }
            var f = new Packet(Net.Msg.BoxSet).I32(x).I32(y).I32(z).U8((byte)slot).Slot(st).Bytes();
            foreach (var o in _players.Values) if (o.Joined && o != p && o.HasBox && o.BX == x && o.BY == y && o.BZ == z) o.Conn.Send(f);
        }

        // What a player had when they fell, or what didn't fit in their pack: into a satchel on the ground.
        void Drop(Player p, Reader r)
        {
            float x = r.F32(), y = r.F32(), z = r.F32(); var st = r.WithMore(r.Stacks());     // everything, however much
            bool here = Math.Abs(x - p.X) <= 8 && Math.Abs(z - p.Z) <= 8 && Math.Abs(y - p.Y) <= 12;
            // After a fall off a sky island the things wait where the player last stood, which is far above them.
            bool safe = p.HasSafe && Math.Abs(x - p.SX) <= 8 && Math.Abs(z - p.SZ) <= 8 && Math.Abs(y - p.SY) <= 12;
            if (!here && !safe) { if (p.Y < 1 && p.HasSafe) { x = p.SX; y = p.SY; z = p.SZ; } else { x = p.X; y = p.Y; z = p.Z; } }
            bool any = false; foreach (var s in st) if (!s.Empty) any = true;
            if (!any) return;
            // All of it has left their pack, the moment this is heard: cut off before their game says so, they
            // must not come back still carrying what lies in the satchel.
            if (!_cfg.Creative) foreach (var s in st) if (!s.Empty) p.Transit.Add((s.Item, -s.Count, s.Wear));
            int ix = (int)Math.Floor(x), iz = (int)Math.Floor(z);
            if (!World.IsLoaded(ix, iz)) return;
            // Into satchels at and around the spot: one already there is topped up, a full one gets a neighbour,
            // and anything that finds no room at all goes back to the player. Nothing is thrown away.
            int sx = 0, sy = 0, sz = 0, made = 0;
            var rest = _boxes.Stash(World.Get, (ax, ay, az) =>
            {
                World.SetRaw(ax, ay, az, Blocks.Satchel);
                Broadcast(BlockPacket(ax, ay, az, Blocks.Satchel, 0));
                if (made++ == 0) { sx = ax; sy = ay; sz = az; }
            }, ix, (int)Math.Floor(y), iz, st);
            foreach (var s in rest) Give(p, s.Item, s.Count, s.Wear);
            Log.Info(made > 0 ? $"{p.Name}'s things went into a satchel at {sx} {sy} {sz}" : $"{p.Name}'s things found a satchel already there, or no room for one (handed back: {rest.Count} stacks)");
        }

        void Mount(Player p, Reader r)
        {
            int id = r.I32();
            if (!p.Alive) return;
            var k = _mobs.Mount(id, p.X, p.Y, p.Z, out float x, out float y, out float z, out float yaw);
            if (k == null) return;
            p.Boarded = k; p.BoardedAt = Environment.TickCount64;       // it's gone from the world: theirs until they say they ride it
            p.Conn.Send(new Packet(Net.Msg.Mounted).U8((byte)k.Value).F32(x).F32(y).F32(z).F32(yaw));
        }

        void Park(Player p, Reader r)
        {
            var k = (MobKind)r.U8(); float x = r.F32(), y = r.F32(), z = r.F32(), yaw = r.F32();
            if ((int)k >= (int)MobKind.Count || !(MobSim.Def(k).Vehicle || k == MobKind.Camel) || ++p.Edits > 40) return;
            float dx = x - p.X, dy = y - p.Y, dz = z - p.Z;
            if (dx * dx + dy * dy + dz * dz > (Net.Reach + 2) * (Net.Reach + 2)) return;
            p.Boarded = null;
            p.Flags = (byte)(p.Flags & ~(Net.InBoat | Net.InCart));       // they're out of it now, whatever their last word on moving said
            _mobs.Park(k, x, y, z, yaw, p.Key);
        }

        // Through a portal: the player has arrived near (tx, tz) and waited for the land there; find the portal
        // on this side or build one (too many blocks to send as edits), and tell them where to stand.
        void Portal(Player p, Reader r)
        {
            int tx = r.I32(), tz = r.I32(); bool toEmber = r.Bool(); int surface = r.I32();
            if (!p.Alive || Math.Abs(tx - p.X) > 64 || Math.Abs(tz - p.Z) > 64) return;
            if (toEmber != Ember.In(tx)) return;
            for (int dz = -48; dz <= 48; dz += 16) for (int dx = -48; dx <= 48; dx += 16) if (!World.IsLoaded(tx + dx, tz + dz)) { World.Want((tx + dx) >> 4, (tz + dz) >> 4); }
            if (!World.IsLoaded(tx, tz)) return;
            // Finding or building a portal reads a great many blocks: once every few seconds is plenty.
            long tick = Environment.TickCount64;
            if (tick < p.NextPortal) return;
            p.NextPortal = tick + 3000;
            surface = Math.Clamp(surface, 1, Chunk.Height - 8);
            var (x, y, z) = Ember.Arrive(World.Get, World.Put, tx, tz, toEmber, surface);
            p.Conn.Send(new Packet(Net.Msg.Arrived).F32(x).F32(y).F32(z));
        }

        void Fertilize(Player p, Reader r)
        {
            int x = r.I32(), y = r.I32(), z = r.I32();
            if (++p.Edits > 40) return;
            float dx = x + 0.5f - p.X, dy = y + 0.5f - (p.Y + 1.6f), dz = z + 0.5f - p.Z;
            if (dx * dx + dy * dy + dz * dz > (Net.Reach + 2) * (Net.Reach + 2)) return;
            World.Growth.BoneMeal(x, y, z);
        }

        void SignText(Player p, Reader r)
        {
            int x = r.I32(), y = r.I32(), z = r.I32(); string text = Containers.CleanSign(r.Str());
            if (++p.Edits > 40) return;
            float dx = x + 0.5f - p.X, dy = y + 0.5f - (p.Y + 1.6f), dz = z + 0.5f - p.Z;
            if (dx * dx + dy * dy + dz * dz > (Net.Reach + 2) * (Net.Reach + 2)) return;
            var d = Blocks.Get(World.Get(x, y, z));
            if (d.BaseId != Blocks.Sign && d.BaseId != Blocks.WallSign) return;
            _boxes.SetSign(x, y, z, text);
            Broadcast(new Packet(Net.Msg.Sign).I32(x).I32(y).I32(z).Str(text));
        }

        static Stack[] Merge(Stack[] a, Stack[] b)
        {
            var l = new List<Stack>();
            foreach (var s in a) if (!s.Empty) l.Add(s);
            foreach (var s in b) if (!s.Empty) l.Add(s);
            return l.ToArray();
        }

        // When everyone online is asleep at night, the night passes.
        void CheckSleep()
        {
            int asleep = 0, total = 0;
            foreach (var p in _players.Values) if (p.Joined && p.Alive) { total++; if (p.Asleep) asleep++; }
            if (asleep != _lastSleepers || total != _lastAwake && asleep > 0) { _lastSleepers = asleep; _lastAwake = total; Broadcast(new Packet(Net.Msg.Sleepers).U8((byte)asleep).U8((byte)total)); }
            if (total == 0 || asleep < total) return;
            double t = World.Meta.time;
            if (t > 0.23 && t < 0.77) return;
            if (t >= 0.77) World.Meta.day++;
            World.Meta.time = 0.25;
            foreach (var p in _players.Values) p.Flags = (byte)(p.Flags & ~Net.Asleep);
            Broadcast(ClockPacket());
            Broadcast(new Packet(Net.Msg.Sleepers).U8(0).U8((byte)total));
            _lastSleepers = 0;
            Broadcast(SaidPacket("", "Good morning!"));
            Log.Info($"everyone slept; day {World.Meta.day}");
        }

        // ------------------------------------------------------------------ sending

        void Broadcast(byte[] f) { foreach (var p in _players.Values) if (p.Joined) p.Conn.Send(f); }
        void Broadcast(Packet pk) => Broadcast(pk.Bytes());

        void SendMoves()
        {
            foreach (var p in _players.Values)
            {
                if (!p.Joined || !p.Moved) continue;
                p.Moved = false;
                var f = new Packet(Net.Msg.Moved).I32(p.Conn.Id).F32(p.X).F32(p.Y).F32(p.Z).F32(p.Yaw).F32(p.Pitch).U8(p.Flags).Str(p.Shield ? p.Held + "~s" : p.Held).Bytes();
                foreach (var o in _players.Values) if (o.Joined && o != p) o.Conn.Send(f);
            }
        }

        public const int Refused = -1;            // BlockPacket's "by": the edit you asked for didn't happen
        static Packet BlockPacket(int x, int y, int z, ushort id, int by) => new Packet(Net.Msg.Block).I32(x).I32(y).I32(z).U16(id).I32(by);
        Packet JoinedPacket(Player p) => new Packet(Net.Msg.Joined).I32(p.Conn.Id).Str(p.Name).F32(p.X).F32(p.Y).F32(p.Z).F32(p.Yaw).U8(p.Outfit).Blob(p.Look ?? Array.Empty<byte>());
        Packet ClockPacket() => new Packet(Net.Msg.Clock).F64(World.Meta.time).I32(World.Meta.day).U8((byte)_weather.State);
        static Packet SaidPacket(string from, string text) => new Packet(Net.Msg.Said).Str(from).Str(text);
    }
}
