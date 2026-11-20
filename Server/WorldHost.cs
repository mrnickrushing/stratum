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
            public PlayerRecord Rec;
            public bool Joined;
            public float X, Y, Z, Yaw, Pitch;
            public byte Flags;
            public string Held = "";
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
            public bool Alive => (Flags & Net.Dead) == 0;
            public bool Asleep => (Flags & Net.Asleep) != 0;
        }

        readonly ServerConfig _cfg;
        public readonly ServerWorld World;
        readonly ConcurrentQueue<(Connection c, byte[] f)> _inbox = new ConcurrentQueue<(Connection, byte[])>();
        readonly ConcurrentQueue<(Connection c, string why)> _closed = new ConcurrentQueue<(Connection, string)>();
        readonly ConcurrentQueue<(Connection c, Accounts.Result res, Accounts.Mode mode, string version)> _arrivals = new ConcurrentQueue<(Connection, Accounts.Result, Accounts.Mode, string)>();
        readonly GameServer _hub;
        public readonly WorldDef Def;
        readonly ConcurrentQueue<Action> _later = new ConcurrentQueue<Action>();   // work finished off the loop
        readonly MobSim _mobs;
        readonly List<(float, float, float)> _bodies = new List<(float, float, float)>();
        readonly Containers _boxes = new Containers();
        readonly List<Seen> _seen = new List<Seen>();
        int _lastSleepers = -1;
        readonly List<(float, float)> _near = new List<(float, float)>();
        WeatherSim _weather;
        string MobFile => System.IO.Path.Combine(_cfg.Dir, "creatures.bin");
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
            catch (Exception e) { Log.Warn("containers.bin unreadable, keeping it aside: " + e.Message); try { System.IO.File.Copy(BoxFile, BoxFile + ".bad", true); } catch { } }
            _mobs = new MobSim(World.Get, World.Meta.seed) { Difficulty = cfg.Difficulty, BiomeAt = World.Gen.BiomeAt };
            _mobs.Loaded = (x, z) => World.IsLoaded(x, z);
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
                if (killer <= 0 || !_players.TryGetValue(killer, out var p) || !p.Joined) return;
                foreach (var (item, n) in _mobs.Loot(m)) Give(p, item, n);
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
                     $"{WorldGen.KindName(World.Gen.Kind)}, {(_cfg.Creative ? "creative" : "survival")}, {new[] { "peaceful", "easy", "normal" }[Math.Clamp(_cfg.Difficulty, 0, 2)]}");
        }

        // A logged-in player chose this world.
        public void Adopt(Connection c, Accounts.Result res, Accounts.Mode mode, string version)
        {
            c.Route((cn, f) => _inbox.Enqueue((cn, f)), (cn, why) => _closed.Enqueue((cn, why)));
            _arrivals.Enqueue((c, res, mode, version));
        }

        // The same account is coming in to another world: end its session here.
        public void Drop(string key) => _later.Enqueue(() =>
        {
            foreach (var o in _players.Values.ToList()) if (o.Joined && o.Key == key) o.Conn.Close("went to another world");
        });

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
            double last = 0, nextMoves = 0, nextClock = 0, nextSave = 60, nextUnload = 30, nextStats = 300, nextSecond = 1, nextMobs = 0;
            var made = new List<Chunk>();
            while (!_stop)
            {
                double now = clock.Elapsed.TotalSeconds;
                float dt = (float)Math.Min(0.25, now - last);
                last = now;
                try
                {
                    while (_arrivals.TryDequeue(out var a)) { var np = new Player { Conn = a.c }; _players[a.c.Id] = np; Admit(np, a.res, a.mode, a.version); }
                    while (_inbox.TryDequeue(out var m)) Handle(m.c, m.f);
                    while (_closed.TryDequeue(out var cl)) Leave(cl.c, cl.why);
                    while (_later.TryDequeue(out var a)) a();

                    made.Clear();
                    World.Collect(made);
                    foreach (var ch in made) Serve(ch);

                    World.Water.Tick(dt);
                    _weather.Tick(dt);
                    _near.Clear(); foreach (var pl in _players.Values) if (pl.Joined) _near.Add((pl.X, pl.Z));
                    World.Growth.Tick(dt, _near);
                    _bodies.Clear();
                    foreach (var pl in _players.Values) if (pl.Joined && pl.Alive) _bodies.Add((pl.X, pl.Y, pl.Z));
                    foreach (var m in _mobs.All) if (m.Dead <= 0 && m.Kind != MobKind.Arrow && !m.Def.Vehicle) _bodies.Add((m.X, m.Y, m.Z));
                    World.Signal.Tick(dt, _bodies);
                    TickMobs(dt);
                    if (now >= nextMobs) { nextMobs = now + 0.1; SendMobs(); }
                    World.Meta.time += dt / 1200.0;     // Sky.DayLength
                    if (World.Meta.time >= 1) { World.Meta.time -= 1; World.Meta.day++; }

                    if (now >= nextMoves) { nextMoves = now + 0.1; SendMoves(); }
                    if (now >= nextClock) { nextClock = now + 5; Broadcast(ClockPacket()); KickSilent(); }
                    if (now >= nextSecond) { nextSecond = now + 1; foreach (var p in _players.Values) { p.Edits = 0; p.VoiceFrames = 0; p.Strikes = 0; p.Shots = 0; } CheckSleep(); }
                    if (now >= nextSave) { nextSave = now + 60; SaveAll(false); }
                    if (now >= nextUnload) { nextUnload = now + 30; World.Maintain(Needed, true); }
                    if (now >= nextStats) { nextStats = now + 300; Log.Info($"{_onlineCount} online, {World.Loaded} chunks loaded, {_mobs.All.Count} creatures, day {World.Meta.day}"); }
                }
                catch (Exception e) { Log.Warn("tick failed: " + e); }
                double sleep = Tick - (clock.Elapsed.TotalSeconds - now);
                if (sleep > 0) Thread.Sleep(TimeSpan.FromSeconds(sleep));
            }
            foreach (var p in _players.Values.ToList()) { if (p.Joined) SavePlayer(p); p.Conn.Close("server stopping"); }
            SaveAll(true);
            World.Stop();
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
            World.SaveMeta();
            try { _mobs.SaveFile(MobFile); } catch (Exception e) { Log.Warn("could not save creatures: " + e.Message); }
            if (_boxes.Dirty) { try { _boxes.Save(BoxFile); } catch (Exception e) { Log.Warn("could not save containers: " + e.Message); } }
            foreach (var p in _players.Values) if (p.Joined) SavePlayer(p);
        }

        void KickSilent()
        {
            long now = Environment.TickCount64;
            foreach (var p in _players.Values.ToList())
                if (now - p.Conn.LastHeard > (p.Joined ? 30000 : 10000)) p.Conn.Close(p.Joined ? "timed out" : "never said hello");
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
                if (o.Joined && o != p && o.Key == key) o.Conn.Close("logged in again elsewhere");

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
                .U8((byte)World.Gen.Kind);                                  // 0.4: what sort of world (older clients stop reading before it)
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
            Log.Info($"{name} joined from {p.Conn.Address} ({version}{(mode == Accounts.Mode.Create ? ", new account" : "")}), {_onlineCount} online");
        }

        void Leave(Connection c, string why)
        {
            if (!_players.TryGetValue(c.Id, out var p)) return;
            _players.Remove(c.Id);
            // Leaving in a boat or cart leaves it where they were.
            if (p.Joined && (p.Flags & Net.InBoat) != 0) _mobs.Park(MobKind.Boat, p.X, p.Y, p.Z, p.Yaw);
            if (p.Joined && (p.Flags & Net.InCart) != 0) _mobs.Park(MobKind.Cart, p.X, p.Y, p.Z, p.Yaw);
            foreach (long k in p.Waiting) if (_waiters.TryGetValue(k, out var l)) l.Remove(p);
            if (!p.Joined) return;
            SavePlayer(p);
            Counted();
            var left = new Packet(Net.Msg.Left).I32(c.Id).Bytes();
            foreach (var o in _players.Values) if (o.Joined) o.Conn.Send(left);
            if (!_players.Values.Any(o => o.Joined && o.Key == p.Key)) Broadcast(SaidPacket("", $"{p.Name} left the game"));
            CheckSleep();
            Log.Info($"{p.Name} left ({why}), {_onlineCount} online");
        }

        void SavePlayer(Player p)
        {
            p.Rec.hasPos = true;
            p.Rec.x = p.X; p.Rec.y = p.Y; p.Rec.z = p.Z; p.Rec.yaw = p.Yaw; p.Rec.pitch = p.Pitch;
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
            p.X = x; p.Y = y; p.Z = z; p.Yaw = yaw; p.Pitch = Math.Clamp(pitch, -90, 90); p.Flags = flags;
            p.Held = held.Length > 40 ? "" : held;
            p.Moved = true;
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
            int x = r.I32(), y = r.I32(), z = r.I32(); byte id = r.U8(), old = r.U8();
            if (++p.Edits > 40) return;
            if (!World.IsLoaded(x, z) || (uint)y >= Chunk.Height) return;
            byte cur = World.Get(x, y, z);
            // Edits must be in reach, of a real block, by a known rule, and agree on what was there.
            float dx = x + 0.5f - p.X, dy = y + 0.5f - (p.Y + 1.6f), dz = z + 0.5f - p.Z;
            string why = dx * dx + dy * dy + dz * dz > (Net.Reach + 2) * (Net.Reach + 2) ? $"out of reach ({Math.Sqrt(dx * dx + dy * dy + dz * dz):0.0} m)"
                       : Blocks.Defs[id] == null ? $"no block {id}"
                       : cur != old ? $"it was {Blocks.Get(cur).Name}, not {Blocks.Get(old).Name}"
                       : !_cfg.Creative && Blocks.Get(cur).Hardness < 0 && !(Blocks.Get(cur).Fluid != 0 && Blocks.Get(cur).Level == 0 && id == 0) ? "unbreakable"
                       : id != 0 && cur != 0 && !Blocks.Get(cur).Replaceable ? "something is in the way"
                       : null;
            if (why != null)
            {
                p.Conn.Send(BlockPacket(x, y, z, cur, 0));                       // put them right
                if (p.Refused++ < 20) Log.Info($"refused {p.Name}'s edit at {x} {y} {z}: {why}");
                return;
            }
            World.SetRaw(x, y, z, id);
            Broadcast(BlockPacket(x, y, z, id, p.Conn.Id));
            World.Water.Wake(x, y, z);
            World.Signal.Wake(x, y, z);
            if (_boxes.Signs.Remove(Containers.Key(x, y, z))) { _boxes.Dirty = true; Broadcast(new Packet(Net.Msg.Sign).I32(x).I32(y).I32(z).Str("")); }
            if (Blocks.Get(cur).Container && !Blocks.Get(id).Container && _boxes.Has(x, y, z))
                foreach (var st in _boxes.Remove(x, y, z)) if (!st.Empty) p.Conn.Send(new Packet(Net.Msg.Give).Str(st.Item).U16(st.Count).U16(st.Wear));
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
            if (text.StartsWith("/")) { p.Conn.Send(SaidPacket("", "Commands: /list  /spawn  /home  /tp <name>  /weather <clear|rain|thunder>")); return; }
            Broadcast(SaidPacket(p.Name, text));
            Log.Info($"<{p.Name}> {text}");
        }

        void Keep(Player p, Reader r)
        {
            p.Rec.health = Math.Clamp(r.F32(), 0, 20);
            p.Rec.food = Math.Clamp(r.F32(), 0, 20);
            p.Rec.saturation = Math.Clamp(r.F32(), 0, 20);
            p.Rec.selected = r.U8();
            var st = r.Stacks();
            if (st.Length == Inventory.Size) p.Rec.SetStacks(st);
            p.Rec.hasBed = r.Bool(); p.Rec.bx = r.I32(); p.Rec.by = r.I32(); p.Rec.bz = r.I32();
            p.Rec.xpLevel = Math.Clamp(r.I32(), 0, 10000); p.Rec.xpPoints = Math.Clamp(r.I32(), 0, 100000);
        }

        // ------------------------------------------------------------------ creatures

        void TickMobs(float dt)
        {
            _seen.Clear();
            foreach (var p in _players.Values)
                if (p.Joined) _seen.Add(new Seen { Id = p.Conn.Id, X = p.X, Y = p.Y, Z = p.Z, Sneaking = (p.Flags & Net.Sneaking) != 0, Creative = _cfg.Creative, Alive = p.Alive, Sleeping = p.Asleep, Held = p.Held });
            _mobs.Time01 = (float)World.Meta.time;
            _mobs.Tick(dt, _seen);
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
            var got = _mobs.Use(id, item, p.X, p.Z);
            if (got is (string it, int n)) Give(p, it, n);
        }

        void Give(Player p, string item, int n)
        {
            if (Items.Get(item) == null || n <= 0) return;
            p.Conn.Send(new Packet(Net.Msg.Give).Str(item).U16(n).U16(0));
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
            if (!InReach(p, x, y, z) || !Blocks.Get(World.Get(x, y, z)).Container) return;
            p.HasBox = true; p.BX = x; p.BY = y; p.BZ = z;
            p.Conn.Send(new Packet(Net.Msg.Box).I32(x).I32(y).I32(z).Stacks(_boxes.At(x, y, z)));
        }

        void BoxSlot(Player p, Reader r)
        {
            int x = r.I32(), y = r.I32(), z = r.I32(), slot = r.U8(); var st = r.Slot();
            if (!p.HasBox || p.BX != x || p.BY != y || p.BZ != z || slot >= Containers.ChestSlots) return;
            if (!Blocks.Get(World.Get(x, y, z)).Container) return;
            _boxes.Set(x, y, z, slot, st);
            var f = new Packet(Net.Msg.BoxSet).I32(x).I32(y).I32(z).U8((byte)slot).Slot(st).Bytes();
            foreach (var o in _players.Values) if (o.Joined && o != p && o.HasBox && o.BX == x && o.BY == y && o.BZ == z) o.Conn.Send(f);
        }

        // What a player had when they fell, or what didn't fit in their pack: into a satchel on the ground.
        void Drop(Player p, Reader r)
        {
            float x = r.F32(), y = r.F32(), z = r.F32(); var st = r.Stacks();
            if (Math.Abs(x - p.X) > 8 || Math.Abs(z - p.Z) > 8 || Math.Abs(y - p.Y) > 12) { x = p.X; y = p.Y; z = p.Z; }
            bool any = false; foreach (var s in st) if (!s.Empty) any = true;
            if (!any) return;
            int ix = (int)Math.Floor(x), iz = (int)Math.Floor(z);
            if (!World.IsLoaded(ix, iz)) return;
            // Into a satchel already there if there's room, else a new one.
            var (sx, sy, sz) = Containers.SpotFor(World.Get, ix, (int)Math.Floor(y), iz);
            _boxes.Put(sx, sy, sz, Merge(_boxes.Has(sx, sy, sz) ? _boxes.Remove(sx, sy, sz) : Array.Empty<Stack>(), st));
            World.SetRaw(sx, sy, sz, Blocks.Satchel);
            Broadcast(BlockPacket(sx, sy, sz, Blocks.Satchel, 0));
            Log.Info($"{p.Name}'s things went into a satchel at {sx} {sy} {sz}");
        }

        void Mount(Player p, Reader r)
        {
            int id = r.I32();
            if (!p.Alive) return;
            var k = _mobs.Mount(id, p.X, p.Y, p.Z, out float x, out float y, out float z, out float yaw);
            if (k == null) return;
            p.Conn.Send(new Packet(Net.Msg.Mounted).U8((byte)k.Value).F32(x).F32(y).F32(z).F32(yaw));
        }

        void Park(Player p, Reader r)
        {
            var k = (MobKind)r.U8(); float x = r.F32(), y = r.F32(), z = r.F32(), yaw = r.F32();
            if ((int)k >= (int)MobKind.Count || !MobSim.Def(k).Vehicle || ++p.Edits > 40) return;
            float dx = x - p.X, dy = y - p.Y, dz = z - p.Z;
            if (dx * dx + dy * dy + dz * dz > (Net.Reach + 2) * (Net.Reach + 2)) return;
            _mobs.Park(k, x, y, z, yaw);
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
            if (asleep != _lastSleepers) { _lastSleepers = asleep; Broadcast(new Packet(Net.Msg.Sleepers).U8((byte)asleep).U8((byte)total)); }
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
                var f = new Packet(Net.Msg.Moved).I32(p.Conn.Id).F32(p.X).F32(p.Y).F32(p.Z).F32(p.Yaw).F32(p.Pitch).U8(p.Flags).Str(p.Held).Bytes();
                foreach (var o in _players.Values) if (o.Joined && o != p) o.Conn.Send(f);
            }
        }

        static Packet BlockPacket(int x, int y, int z, byte id, int by) => new Packet(Net.Msg.Block).I32(x).I32(y).I32(z).U8(id).I32(by);
        Packet JoinedPacket(Player p) => new Packet(Net.Msg.Joined).I32(p.Conn.Id).Str(p.Name).F32(p.X).F32(p.Y).F32(p.Z).F32(p.Yaw);
        Packet ClockPacket() => new Packet(Net.Msg.Clock).F64(World.Meta.time).I32(World.Meta.day).U8((byte)_weather.State);
        static Packet SaidPacket(string from, string text) => new Packet(Net.Msg.Said).Str(from).Str(text);
    }
}
