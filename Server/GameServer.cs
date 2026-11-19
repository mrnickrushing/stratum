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
    public sealed class ServerConfig
    {
        public int Port = Net.DefaultPort;
        public string Dir = "world";
        public int? Seed;
        public string Name = "Stratum Server";
        public string Motd = "";
        public int MaxPlayers = 10;
        public string Password = "";
        public bool Creative;
        public int MaxView = 10;
    }

    // One thread runs the world at 20 ticks a second; connections hand it their messages through a queue.
    public sealed class GameServer
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
        }

        readonly ServerConfig _cfg;
        public readonly ServerWorld World;
        readonly ConcurrentQueue<(Connection c, byte[] f)> _inbox = new ConcurrentQueue<(Connection, byte[])>();
        readonly ConcurrentQueue<(Connection c, string why)> _closed = new ConcurrentQueue<(Connection, string)>();
        readonly ConcurrentQueue<Connection> _opened = new ConcurrentQueue<Connection>();
        readonly Dictionary<int, Player> _players = new Dictionary<int, Player>();
        readonly Dictionary<long, List<Player>> _waiters = new Dictionary<long, List<Player>>();
        TcpListener _listener;
        Thread _loop;
        volatile bool _stop;
        int _nextId;
        public int Port { get; private set; }
        public int Online => _onlineCount;
        volatile int _onlineCount;
        readonly ManualResetEventSlim _stopped = new ManualResetEventSlim(false);

        public GameServer(ServerConfig cfg)
        {
            _cfg = cfg;
            World = new ServerWorld(cfg.Dir, cfg.Seed);
            World.Changed += (x, y, z, id) => Broadcast(BlockPacket(x, y, z, id, 0));
        }

        public void Start()
        {
            _listener = new TcpListener(IPAddress.IPv6Any, _cfg.Port);
            _listener.Server.DualMode = true;
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            new Thread(Accept) { IsBackground = true, Name = "accept" }.Start();
            _loop = new Thread(Loop) { Name = "game loop" };
            _loop.Start();
            Log.Info($"\"{_cfg.Name}\" listening on port {Port}, world {System.IO.Path.GetFullPath(_cfg.Dir)}, seed {World.Meta.seed}, " +
                     $"{(_cfg.Creative ? "creative" : "survival")}, up to {_cfg.MaxPlayers} players{(_cfg.Password != "" ? ", password set" : "")}");
        }

        void Accept()
        {
            while (!_stop)
            {
                try
                {
                    var tcp = _listener.AcceptTcpClient();
                    int id = Interlocked.Increment(ref _nextId);
                    var c = new Connection(id, tcp, (cn, f) => _inbox.Enqueue((cn, f)), (cn, why) => _closed.Enqueue((cn, why)));
                    _opened.Enqueue(c);
                }
                catch (Exception) { if (_stop) return; Thread.Sleep(50); }
            }
        }

        // Saves everything and stops; returns when done.
        public void Stop()
        {
            if (_stop) return;
            _stop = true;
            try { _listener?.Stop(); } catch { }
            _stopped.Wait(10000);
        }

        // ------------------------------------------------------------------ loop

        void Loop()
        {
            const double Tick = 0.05;
            var clock = Stopwatch.StartNew();
            double last = 0, nextMoves = 0, nextClock = 0, nextSave = 60, nextUnload = 30, nextStats = 300, nextSecond = 1;
            var made = new List<Chunk>();
            while (!_stop)
            {
                double now = clock.Elapsed.TotalSeconds;
                float dt = (float)Math.Min(0.25, now - last);
                last = now;
                try
                {
                    while (_opened.TryDequeue(out var c)) _players[c.Id] = new Player { Conn = c };
                    while (_inbox.TryDequeue(out var m)) Handle(m.c, m.f);
                    while (_closed.TryDequeue(out var cl)) Leave(cl.c, cl.why);

                    made.Clear();
                    World.Collect(made);
                    foreach (var ch in made) Serve(ch);

                    World.Water.Tick(dt);
                    World.Meta.time += dt / 1200.0;     // Sky.DayLength
                    if (World.Meta.time >= 1) { World.Meta.time -= 1; World.Meta.day++; }

                    if (now >= nextMoves) { nextMoves = now + 0.1; SendMoves(); }
                    if (now >= nextClock) { nextClock = now + 5; Broadcast(ClockPacket()); KickSilent(); }
                    if (now >= nextSecond) { nextSecond = now + 1; foreach (var p in _players.Values) p.Edits = 0; }
                    if (now >= nextSave) { nextSave = now + 60; SaveAll(false); }
                    if (now >= nextUnload) { nextUnload = now + 30; World.Maintain(Needed, true); }
                    if (now >= nextStats) { nextStats = now + 300; Log.Info($"{_onlineCount} online, {World.Loaded} chunks loaded, day {World.Meta.day}"); }
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
            World.SaveMeta();
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
                if (!p.Joined)
                {
                    if (r.Type == Net.Msg.Hello) Hello(p, r);
                    else if (r.Type == Net.Msg.Ping) c.Send(new Packet(Net.Msg.Pong).F64(r.F64()).Str(_cfg.Name).Str(_cfg.Motd).I32(_onlineCount).I32(_cfg.MaxPlayers));
                    else c.Close("spoke before saying hello");
                    return;
                }
                switch (r.Type)
                {
                    case Net.Msg.Move: Move(p, r); break;
                    case Net.Msg.WantChunks: WantChunks(p, r); break;
                    case Net.Msg.SetBlock: SetBlock(p, r); break;
                    case Net.Msg.Chat: Chat(p, r); break;
                    case Net.Msg.Keep: Keep(p, r); break;
                    case Net.Msg.Ping: c.Send(new Packet(Net.Msg.Pong).F64(r.F64()).Str(_cfg.Name).Str(_cfg.Motd).I32(_onlineCount).I32(_cfg.MaxPlayers)); break;
                }
            }
            catch (Exception e) { c.Close("bad message: " + e.Message); }
        }

        void Refuse(Player p, string why)
        {
            p.Conn.Send(new Packet(Net.Msg.Refuse).Str(why));
            p.Conn.CloseAfterSending(why);
        }

        void Hello(Player p, Reader r)
        {
            int proto = r.I32();
            string name = r.Str().Trim(), secret = r.Str(), pass = r.Str(), version = r.Str();
            if (proto != Net.Protocol) { Refuse(p, proto < Net.Protocol ? "This server needs a newer Stratum." : "This server runs an older Stratum."); return; }
            if (!Net.ValidName(name)) { Refuse(p, "Pick a name of 1-16 letters, numbers, spaces, - or _."); return; }
            if (secret.Length < 16 || secret.Length > 128) { Refuse(p, "Bad player key."); return; }
            if (_cfg.Password != "" && !FixedEquals(pass, _cfg.Password)) { Refuse(p, "Wrong server password."); return; }
            string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("stratum:" + secret))).ToLowerInvariant().Substring(0, 32);

            // The same player coming back from a dropped connection replaces the old one.
            foreach (var o in _players.Values.ToList())
                if (o.Joined && o != p && o.Key == key) o.Conn.Close("joined again elsewhere");
            if (_players.Values.Any(o => o.Joined && o != p && o.Key != key && string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase)))
            { Refuse(p, $"Someone called {name} is already playing."); return; }
            if (_players.Values.Count(o => o.Joined && o.Key != key) >= _cfg.MaxPlayers) { Refuse(p, "The server is full."); return; }

            p.Name = name; p.Key = key;
            p.Rec = PlayerRecord.Load(_cfg.Dir, key) ?? new PlayerRecord();
            p.Rec.name = name;
            var m = World.Meta;
            if (p.Rec.hasPos) { p.X = p.Rec.x; p.Y = p.Rec.y; p.Z = p.Rec.z; p.Yaw = p.Rec.yaw; p.Pitch = p.Rec.pitch; }
            else { p.X = m.sx + 0.5f; p.Y = m.sy; p.Z = m.sz + 0.5f; }
            p.Joined = true;
            _onlineCount = _players.Values.Count(o => o.Joined);

            var w = new Packet(Net.Msg.Welcome)
                .I32(p.Conn.Id).I32(m.seed).Str(_cfg.Name).Str(_cfg.Motd).Bool(_cfg.Creative).U8((byte)_cfg.MaxView)
                .F64(m.time).I32(m.day).I32(m.sx).I32(m.sy).I32(m.sz)
                .Bool(p.Rec.hasPos).F32(p.X).F32(p.Y).F32(p.Z).F32(p.Yaw).F32(p.Pitch).F32(p.Rec.health).U8((byte)Math.Clamp(p.Rec.selected, 0, 8))
                .Stacks(p.Rec.Stacks());
            p.Conn.Send(w);
            foreach (var o in _players.Values)
                if (o.Joined && o != p)
                {
                    p.Conn.Send(JoinedPacket(o));
                    o.Conn.Send(JoinedPacket(p));
                }
            Broadcast(SaidPacket("", $"{name} joined the game"));
            Log.Info($"{name} joined from {p.Conn.Address} ({version}), {_onlineCount} online");
        }

        static bool FixedEquals(string a, string b) =>
            CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(a)), SHA256.HashData(Encoding.UTF8.GetBytes(b)));

        void Leave(Connection c, string why)
        {
            if (!_players.TryGetValue(c.Id, out var p)) return;
            _players.Remove(c.Id);
            foreach (long k in p.Waiting) if (_waiters.TryGetValue(k, out var l)) l.Remove(p);
            if (!p.Joined) return;
            SavePlayer(p);
            _onlineCount = _players.Values.Count(o => o.Joined);
            var left = new Packet(Net.Msg.Left).I32(c.Id).Bytes();
            foreach (var o in _players.Values) if (o.Joined) o.Conn.Send(left);
            if (!_players.Values.Any(o => o.Joined && o.Key == p.Key)) Broadcast(SaidPacket("", $"{p.Name} left the game"));
            Log.Info($"{p.Name} left ({why}), {_onlineCount} online");
        }

        void SavePlayer(Player p)
        {
            p.Rec.hasPos = true;
            p.Rec.x = p.X; p.Rec.y = p.Y; p.Rec.z = p.Z; p.Rec.yaw = p.Yaw; p.Rec.pitch = p.Pitch;
            p.Rec.lastSeen = DateTime.UtcNow.Ticks;
            p.Rec.Save(_cfg.Dir, p.Key);
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
                       : !_cfg.Creative && Blocks.Get(cur).Hardness < 0 ? "unbreakable"
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
            if (id == 0) World.Water.Wake(x, y, z);
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
            if (text.StartsWith("/")) { p.Conn.Send(SaidPacket("", "Commands: /list")); return; }
            Broadcast(SaidPacket(p.Name, text));
            Log.Info($"<{p.Name}> {text}");
        }

        void Keep(Player p, Reader r)
        {
            p.Rec.health = Math.Clamp(r.F32(), 0, 20);
            p.Rec.selected = r.U8();
            var st = r.Stacks();
            if (st.Length == Inventory.Size) p.Rec.SetStacks(st);
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
        Packet ClockPacket() => new Packet(Net.Msg.Clock).F64(World.Meta.time).I32(World.Meta.day);
        static Packet SaidPacket(string from, string text) => new Packet(Net.Msg.Said).Str(from).Str(text);
    }
}
