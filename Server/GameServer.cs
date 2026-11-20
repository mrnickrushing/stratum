using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
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
        public string Password = "";               // invite code needed to make an account (blank: anyone can)
        public string Reset = "";                  // account names whose passwords to clear once (STRATUM_RESET)
        public bool Creative;
        public int MaxView = 10;
        public int Difficulty = 2;                 // 0 peaceful, 1 easy, 2 normal (STRATUM_DIFFICULTY)
        public WorldKind Kind;                     // a new world's sort (STRATUM_KIND); a world keeps the one it was made with
        public List<WorldDef> Worlds = new List<WorldDef>();   // STRATUM_WORLDS; none: one world from the settings above

        // The settings one world runs with: these, with its own folder, mode, difficulty and seed.
        public ServerConfig For(WorldDef w)
        {
            var c = (ServerConfig)MemberwiseClone();
            c.Dir = w.Id == WorldDef.Main ? Dir : Path.Combine(Dir, "worlds", w.Id);
            c.Creative = w.Creative; c.Difficulty = w.Difficulty; c.Kind = w.Kind;
            c.Seed = w.Seed ?? (w.Id == WorldDef.Main ? Seed : null);
            return c;
        }
    }

    // A world players can choose. "main" keeps its files in the data folder itself (where the first world always
    // was); the others live in <data>/worlds/<id>. Each world keeps its own backpacks, so creative things stay there.
    public sealed class WorldDef
    {
        public const string Main = "main";
        public string Id, Name, Desc = "";
        public bool Creative;
        public int Difficulty = 2;
        public int? Seed;
        public WorldKind Kind;

        // "id|Name|survival or creative|peaceful, easy or normal|seed|what it's like|kind" separated by ";", where kind
        // is normal, sky, frozen, dunes, islands, peaks or flat. Blank parts take the defaults; the first world is where
        // old clients (that can't choose) go.
        public static List<WorldDef> Parse(string text, Func<string, int> seedFrom)
        {
            var list = new List<WorldDef>();
            foreach (var entry in (text ?? "").Split(';'))
            {
                var f = entry.Split('|').Select(x => x.Trim()).ToArray();
                if (f.Length == 0 || f[0] == "") continue;
                string id = new string(f[0].ToLowerInvariant().Where(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_').ToArray());
                if (id == "" || list.Any(w => w.Id == id)) continue;
                string Part(int i) => i < f.Length ? f[i] : "";
                var w = new WorldDef { Id = id, Name = Part(1) != "" ? Part(1) : f[0], Desc = Part(5) };
                w.Creative = Part(2).StartsWith("c", StringComparison.OrdinalIgnoreCase);
                string d = Part(3).ToLowerInvariant();
                w.Difficulty = d.StartsWith("p") || d == "0" ? 0 : d.StartsWith("e") || d == "1" ? 1 : 2;
                if (Part(4) != "") w.Seed = seedFrom(Part(4));
                w.Kind = WorldGen.ParseKind(Part(6));
                list.Add(w);
            }
            return list;
        }
    }

    // The front door: listens, answers "who's there?" pings with the list of worlds, checks logins, and hands each
    // player to the world they picked. Each world runs on its own thread (WorldHost).
    public sealed class GameServer
    {
        sealed class Pending { public Connection Conn; public bool Checking; }

        readonly ServerConfig _cfg;
        readonly Accounts _accounts;
        public readonly List<WorldHost> Worlds = new List<WorldHost>();
        public ServerWorld World => Worlds[0].World;     // the first world (tests)

        readonly ConcurrentQueue<Connection> _opened = new ConcurrentQueue<Connection>();
        readonly ConcurrentQueue<(Connection c, byte[] f)> _inbox = new ConcurrentQueue<(Connection, byte[])>();
        readonly ConcurrentQueue<(Connection c, string why)> _closed = new ConcurrentQueue<(Connection, string)>();
        readonly ConcurrentQueue<Action> _later = new ConcurrentQueue<Action>();
        readonly Dictionary<int, Pending> _pending = new Dictionary<int, Pending>();
        TcpListener _listener;
        Thread _loop;
        volatile bool _stop;
        int _nextId;
        readonly ManualResetEventSlim _stopped = new ManualResetEventSlim(false);

        public int Port { get; private set; }
        public int Online => Worlds.Sum(w => w.Online);

        public GameServer(ServerConfig cfg)
        {
            _cfg = cfg;
            Directory.CreateDirectory(cfg.Dir);
            _accounts = new Accounts(cfg.Dir);
            _accounts.ApplyReset(cfg.Reset);
            var defs = cfg.Worlds.Count > 0 ? cfg.Worlds
                     : new List<WorldDef> { new WorldDef { Id = WorldDef.Main, Name = "World", Creative = cfg.Creative, Difficulty = cfg.Difficulty, Kind = cfg.Kind } };
            foreach (var d in defs) Worlds.Add(new WorldHost(this, cfg.For(d), d));
        }

        public void Start()
        {
            foreach (var w in Worlds) w.Start();
            _listener = new TcpListener(IPAddress.IPv6Any, _cfg.Port);
            _listener.Server.DualMode = true;
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            new Thread(Accept) { IsBackground = true, Name = "accept" }.Start();
            _loop = new Thread(Loop) { Name = "front door" };
            _loop.Start();
            Log.Info($"\"{_cfg.Name}\" listening on port {Port}, {Worlds.Count} world{(Worlds.Count == 1 ? "" : "s")} ({string.Join(", ", Worlds.Select(w => w.Def.Name))}), " +
                     $"up to {_cfg.MaxPlayers} players, {_accounts.Count} accounts{(_cfg.Password != "" ? ", invite code set" : ", anyone can make an account")}");
        }

        void Accept()
        {
            while (!_stop)
            {
                try
                {
                    var tcp = _listener.AcceptTcpClient();
                    int id = Interlocked.Increment(ref _nextId);
                    _opened.Enqueue(new Connection(id, tcp, (cn, f) => _inbox.Enqueue((cn, f)), (cn, why) => _closed.Enqueue((cn, why))));
                }
                catch (Exception) { if (_stop) return; Thread.Sleep(50); }
            }
        }

        // Saves every world and stops; returns when done.
        public void Stop()
        {
            if (_stop) return;
            _stop = true;
            try { _listener?.Stop(); } catch { }
            _stopped.Wait(5000);
            foreach (var w in Worlds) w.Stop();
        }

        void Loop()
        {
            var clock = Stopwatch.StartNew();
            double nextKick = 1;
            while (!_stop)
            {
                try
                {
                    while (_opened.TryDequeue(out var c)) _pending[c.Id] = new Pending { Conn = c };
                    while (_inbox.TryDequeue(out var m)) Handle(m.c, m.f);
                    while (_closed.TryDequeue(out var cl)) _pending.Remove(cl.c.Id);
                    while (_later.TryDequeue(out var a)) a();
                    if (clock.Elapsed.TotalSeconds >= nextKick)
                    {
                        nextKick = clock.Elapsed.TotalSeconds + 1;
                        long now = Environment.TickCount64;
                        foreach (var p in _pending.Values.ToList()) if (now - p.Conn.LastHeard > 10000) p.Conn.Close("never said hello");
                    }
                }
                catch (Exception e) { Log.Warn("front door: " + e); }
                Thread.Sleep(20);
            }
            foreach (var p in _pending.Values) p.Conn.Close("server stopping");
            _stopped.Set();
        }

        // The answer to a ping: the server, then each world with how many are in it.
        public byte[] Pong(double t)
        {
            var pk = new Packet(Net.Msg.Pong).F64(t).Str(_cfg.Name).Str(_cfg.Motd).I32(Online).I32(_cfg.MaxPlayers).U8((byte)Worlds.Count);
            foreach (var w in Worlds) pk.Str(w.Def.Id).Str(w.Def.Name).Str(w.Def.Desc).Bool(w.Def.Creative).U8((byte)w.Def.Difficulty).I32(w.Online);
            // 0.4: each world's sort, after the list so 0.3 clients (which stop reading there) aren't thrown.
            pk.U8((byte)Worlds.Count);
            foreach (var w in Worlds) pk.U8((byte)w.World.Gen.Kind);
            return pk.Bytes();
        }

        void Handle(Connection c, byte[] frame)
        {
            if (!_pending.TryGetValue(c.Id, out var p)) return;      // already in a world
            Reader r;
            try { r = new Reader(frame); } catch { return; }
            try
            {
                if (r.Type == Net.Msg.Ping) c.Send(Pong(r.F64()));
                else if (r.Type == Net.Msg.Hello) Hello(p, r);
                else c.Close("spoke before saying hello");
            }
            catch (Exception e) { c.Close("bad message: " + e.Message); }
        }

        static void Refuse(Connection c, string why)
        {
            c.Send(new Packet(Net.Msg.Refuse).Str(why));
            c.CloseAfterSending(why);
        }

        void Hello(Pending p, Reader r)
        {
            int proto = r.I32();
            if (proto != Net.Protocol) { Refuse(p.Conn, proto < Net.Protocol ? "This server needs a newer Stratum. Get it at stratum.rushingtechnologies.com" : "This server runs an older Stratum."); return; }
            if (p.Checking) return;                        // already checking this connection's login
            var mode = (Accounts.Mode)r.U8();
            string name = r.Str().Trim(), secret = r.Str(), invite = r.Str(), version = r.Str();
            string world = r.More ? r.Str().Trim().ToLowerInvariant() : "";     // clients before 0.3.0 don't choose
            int features = r.More ? r.I32() : 0;                                  // 1: knows the themed world kinds (0.4)
            p.Checking = true;
            // Password hashing takes a moment on purpose; do it off the loop, and answer a wrong one slowly.
            System.Threading.Tasks.Task.Run(async () =>
            {
                Accounts.Result res;
                try { res = _accounts.Enter(mode, name, secret, invite, _cfg.Password); }
                catch (Exception e) { Log.Warn("login failed: " + e); res = new Accounts.Result("The server had trouble with that login. Try again."); }
                if (!res.Ok) await System.Threading.Tasks.Task.Delay(600);
                _later.Enqueue(() => Admit(p, res, mode, version, world, features));
            });
        }

        public const int KnowsKinds = 1;

        void Admit(Pending p, Accounts.Result res, Accounts.Mode mode, string version, string world, int features)
        {
            if (!_pending.TryGetValue(p.Conn.Id, out var still) || still != p) return;    // gone meanwhile
            if (!res.Ok) { Refuse(p.Conn, res.Error); return; }
            var host = Worlds.FirstOrDefault(w => w.Def.Id == world) ?? Worlds[0];
            // An older game would draw a themed world's land right but get its look and creatures wrong.
            if (host.World.Gen.Kind != WorldKind.Normal && (features & KnowsKinds) == 0)
            { Refuse(p.Conn, $"{host.Def.Name} needs the newest Stratum. Get it at stratum.rushingtechnologies.com"); return; }
            // Coming back replaces the old session wherever it is; a full server only turns away someone new.
            int others = Worlds.Sum(w => w.OnlineExcept(res.Key));
            if (others >= _cfg.MaxPlayers) { Refuse(p.Conn, "The server is full."); return; }
            foreach (var w in Worlds) if (w != host) w.Drop(res.Key);
            _pending.Remove(p.Conn.Id);
            host.Adopt(p.Conn, res, mode, version);
        }
    }
}
