using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using Stratum.Core;

namespace Stratum.Server
{
    // A scripted player for tests: says hello, asks for chunks, edits, chats, and records what it hears.
    public sealed class Bot : IDisposable
    {
        readonly TcpClient _tcp;
        readonly NetworkStream _s;
        readonly BlockingCollection<Reader> _in = new BlockingCollection<Reader>();
        readonly List<Reader> _seen = new List<Reader>();
        public int Id, Seed; public bool HasPos; public string Name, Token; public float X, Y, Z; public int SX, SY, SZ; public Stack[] Slots;
        public readonly Dictionary<long, byte[]> Chunks = new Dictionary<long, byte[]>();

        public Bot(int port)
        {
            _tcp = new TcpClient("127.0.0.1", port) { NoDelay = true };
            _s = _tcp.GetStream();
            new Thread(() =>
            {
                var buf = new byte[1 << 16]; var fr = new Framer(); var frames = new List<byte[]>();
                try
                {
                    while (true)
                    {
                        int n = _s.Read(buf, 0, buf.Length);
                        if (n <= 0) break;
                        frames.Clear(); fr.Feed(buf, n, frames);
                        foreach (var f in frames) _in.Add(new Reader(f));
                    }
                }
                catch { }
                _in.CompleteAdding();
            }) { IsBackground = true }.Start();
        }

        public void Send(Packet p) { var b = p.Bytes(); _s.Write(b, 0, b.Length); }

        public void Hello(Accounts.Mode mode, string name, string secret, string invite = "", int proto = Net.Protocol, string world = null)
        {
            var pk = new Packet(Net.Msg.Hello).I32(proto).U8((byte)mode).Str(name).Str(secret).Str(invite).Str("selftest");
            if (world != null) pk.Str(world);         // clients before 0.2.2 don't say which world
            Send(pk);
        }
        public void Create(string name, string pw, string invite = "", string world = null) => Hello(Accounts.Mode.Create, name, pw, invite, world: world);
        public void Login(string name, string pw, string world = null) => Hello(Accounts.Mode.Login, name, pw, world: world);

        // Asks what worlds there are: (id, name, creative, difficulty, players in it).
        public List<(string id, string name, bool creative, int difficulty, int online)> Worlds()
        {
            Send(new Packet(Net.Msg.Ping).F64(1));
            var r = Wait(Net.Msg.Pong);
            var l = new List<(string, string, bool, int, int)>();
            if (r == null) return l;
            r.F64(); r.Str(); r.Str(); r.I32(); r.I32();
            int n = r.U8();
            for (int i = 0; i < n; i++) { string id = r.Str(), name = r.Str(); r.Str(); bool c = r.Bool(); int d = r.U8(); l.Add((id, name, c, d, r.I32())); }
            return l;
        }

        public bool Closed(int ms = 5000)
        {
            var end = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < end) { if (_in.IsCompleted && _in.Count == 0) return true; if (_in.TryTake(out var r, 50)) _seen.Add(r); }
            return _in.IsCompleted;
        }

        public string Refused(int ms = 5000) { var r = Wait(Net.Msg.Refuse, null, ms); return r?.Str(); }

        // Waits for a message of this type that `match` accepts (messages read on the way are kept).
        public Reader Wait(Net.Msg type, Func<Reader, bool> match = null, int ms = 5000)
        {
            var end = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < end)
            {
                if (!_in.TryTake(out var r, 50)) { if (_in.IsCompleted) return null; continue; }
                if (r.Type == Net.Msg.Chunk) { int cx = r.I32(), cz = r.I32(); Chunks[Chunk.Key(cx, cz)] = r.Blob(); if (type == Net.Msg.Chunk) return r; continue; }
                if (r.Type == type && (match == null || match(r))) return r;
                _seen.Add(r);
            }
            return null;
        }

        public float Food; public bool HasBed; public int BX, XpLevel; public string ServerName; public bool Creative; public int Difficulty;

        public bool Welcome()
        {
            var r = Wait(Net.Msg.Welcome);
            if (r == null) return false;
            Id = r.I32(); Seed = r.I32(); ServerName = r.Str(); r.Str(); Creative = r.Bool(); r.U8(); r.F64(); r.I32();
            SX = r.I32(); SY = r.I32(); SZ = r.I32();
            HasPos = r.Bool(); X = r.F32(); Y = r.F32(); Z = r.F32(); r.F32(); r.F32(); r.F32(); r.U8();
            Slots = r.Stacks();
            Name = r.Str(); Token = r.Str();
            Food = r.F32(); r.F32(); HasBed = r.Bool(); BX = r.I32(); r.I32(); r.I32(); Difficulty = r.U8(); XpLevel = r.I32(); r.I32();
            return true;
        }

        public void Move(float x, float y, float z) => Send(new Packet(Net.Msg.Move).F32(x).F32(y).F32(z).F32(0).F32(0).U8(0).Str(""));

        public void Want(IEnumerable<(int x, int z)> keys)
        {
            var l = keys.ToList();
            var p = new Packet(Net.Msg.WantChunks).U16(l.Count);
            foreach (var (x, z) in l) p.I32(x).I32(z);
            Send(p);
        }

        public byte BlockAt(int x, int y, int z)
        {
            if (!Chunks.TryGetValue(Chunk.Key(x >> 4, z >> 4), out var data)) return 255;
            var b = new byte[Chunk.Volume]; Net.Decompress(data, b);
            return b[Chunk.Index(x & 15, y, z & 15)];
        }

        public int Top(int x, int z) { for (int y = Chunk.Height - 1; y > 0; y--) { byte b = BlockAt(x, y, z); if (b != 0 && b != 255) return y; } return 0; }

        public void Dispose() { try { _tcp.Close(); } catch { } }
    }

    public static class SelfTest
    {
        static int _fails;
        // Several worlds on one server: the list, choosing one, each keeping its own things, moving between them.
        static void WorldChecks(string dir)
        {
            int Seed(string t) => int.TryParse(t, out int n) ? n : t.GetHashCode();
            var cfg = new ServerConfig { Port = 0, Dir = dir, Seed = 20261119, MaxView = 4,
                Worlds = WorldDef.Parse("main|Survival|survival|normal||Where it began; calm|Peaceful|survival|peaceful|7|No monsters ; build|Creative|creative||| Build anything", Seed) };
            var srv = new GameServer(cfg); srv.Start();
            try
            {
                using (var q = new Bot(srv.Port))
                {
                    var l = q.Worlds();
                    Check(l.Count == 3 && l[0].id == "main" && l[1].name == "Peaceful" && l[1].difficulty == 0 && l[2].creative && !l[0].creative,
                          "a ping lists the three worlds with their modes");
                }
                float bx, bz;
                using (var a = new Bot(srv.Port))
                {
                    a.Create("Ada", "ada-password", world: "build");
                    Check(a.Welcome() && a.Creative && a.ServerName.EndsWith("Creative") && !a.HasPos, "Ada makes an account straight into the creative world");
                    bx = a.X + 5; bz = a.Z + 3; a.Move(bx, a.Y, bz);
                    Thread.Sleep(300);
                    Check(srv.Worlds[2].Online == 1 && srv.Online == 1, "the creative world counts her");
                    using (var b = new Bot(srv.Port))
                    {
                        b.Login("Ada", "ada-password", world: "calm");
                        Check(b.Welcome() && !b.Creative && b.Difficulty == 0 && !b.HasPos, "logging in to the peaceful world starts her fresh there");
                        Check(a.Closed(), "and ends her session in the creative world");
                        Thread.Sleep(300);
                        Check(srv.Worlds[2].Online == 0 && srv.Worlds[1].Online == 1, "so she is only in one world at a time");
                    }
                }
                using (var a = new Bot(srv.Port))
                {
                    a.Login("Ada", "ada-password", world: "build");
                    Check(a.Welcome() && a.HasPos && Math.Abs(a.X - bx) < 0.01f && Math.Abs(a.Z - bz) < 0.01f, "back in the creative world she is where she left it");
                }
                using (var a = new Bot(srv.Port))
                {
                    a.Login("Ada", "ada-password");
                    Check(a.Welcome() && !a.Creative && a.Seed == 20261119, "a client that can't choose goes to the first world");
                }
                using (var a = new Bot(srv.Port))
                {
                    a.Login("Ada", "ada-password", world: "nowhere");
                    Check(a.Welcome() && a.Seed == 20261119, "an unknown world name goes to the first world too");
                }
            }
            finally { srv.Stop(); }
            Check(File.Exists(Path.Combine(dir, "world.json")) || Directory.GetFiles(dir).Length > 0, "the first world keeps the data folder");
            Check(Directory.Exists(Path.Combine(dir, "worlds", "build")) && Directory.Exists(Path.Combine(dir, "worlds", "calm")), "the others get folders of their own");
        }

        // The Ember Deep: its land, lighting a frame, breaking it, and coming out by a portal on the far side.
        static void EmberChecks()
        {
            var gen = new WorldGen(31337);
            var ch = new Chunk(Ember.Mid / 16, 0); gen.Generate(ch);
            int lava = 0, rock = 0, roof = 0;
            for (int i = 0; i < ch.Blocks.Length; i++) { byte id = ch.Blocks[i]; if (id == Blocks.Lava) lava++; else if (id == Blocks.Cinderstone) rock++; if (id == Blocks.Bedrock && i >> 8 >= Ember.Roof) roof++; }
            Check(ch.Ready && rock > 1000 && roof >= 256, "the Ember Deep is cinderstone under a bedrock roof");
            Check(Ember.ToOverworld(Ember.ToEmber(800, -400).x, Ember.ToEmber(800, -400).z) == (800f, -400f), "a step in the Deep is eight on the surface, both ways");

            var w = new Dictionary<(int, int, int), byte>();
            byte Get(int x, int y, int z) => w.TryGetValue((x, y, z), out var b) ? b : (byte)0;
            void Set(int x, int y, int z, byte b) { if (b == 0) w.Remove((x, y, z)); else w[(x, y, z)] = b; }
            for (int x = 0; x < 4; x++) { Set(x, 0, 0, Blocks.Obsidian); Set(x, 4, 0, Blocks.Obsidian); }
            for (int y = 1; y < 4; y++) { Set(0, y, 0, Blocks.Obsidian); Set(3, y, 0, Blocks.Obsidian); }
            Check(!Ember.Light(Get, Set, 1, 1, 1), "a striker beside the frame (not in it) lights nothing");
            Check(Ember.Light(Get, Set, 1, 1, 0) && Ember.IsPortal(Get(2, 3, 0)) && w.Count == 14 + 6, "a 4x5 obsidian frame lights");
            Set(3, 2, 0, 0); Ember.Collapse(Get, Set, 3, 2, 0);
            Check(!Ember.IsPortal(Get(1, 1, 0)) && !Ember.IsPortal(Get(2, 3, 0)), "breaking the frame puts the portal out");
            Set(3, 2, 0, Blocks.Obsidian); Set(1, 2, 0, Blocks.Stone);
            Check(!Ember.Light(Get, Set, 1, 1, 0), "a frame with something in it won't light");
            Set(1, 2, 0, 0); Ember.Light(Get, Set, 1, 1, 0);
            int had = w.Count;
            var (sx, sy, sz) = Ember.Arrive(Get, Set, 6, 3, false, 0);
            Check(w.Count == had && sx > 0 && sx < 3 && sy == 1 && Math.Abs(sz) <= 1.5f, "coming through, a nearby portal is used rather than a new one built");
            int before = w.Count;
            var (ax, ay, az) = Ember.Arrive(Get, Set, 300, 300, false, 70);
            Check(w.Count > before && Ember.IsPortal(Get(301, 71, 300)) && ay == 71, "with none near, a new frame is built and lit");
        }

        static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (!ok) _fails++; }

        public static int Run()
        {
            var dir = Path.Combine(Path.GetTempPath(), "stratum-selftest-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            var cfg = new ServerConfig { Port = 0, Dir = dir, Seed = 20261119, Name = "Test Server", MaxPlayers = 3 };
            Log.Quiet = true;
            try
            {
                var srv = new GameServer(cfg); srv.Start();
                int port = srv.Port;
                const string pwA = "sunflower";
                int px, py, pz; float ax, ay, az;
                string tokenA;

                using (var a = new Bot(port))
                using (var b = new Bot(port))
                {
                    a.Create("Ada", pwA);
                    Check(a.Welcome() && a.Seed == 20261119 && !a.HasPos && a.Name == "Ada" && a.Token.Length == 64, "Ada makes an account, joins, gets the seed and a remember-me token");
                    tokenA = a.Token;
                    b.Create("Bea", "bea-password");
                    Check(b.Welcome(), "Bea makes an account and joins");
                    Check(a.Wait(Net.Msg.Joined, r => { r.I32(); return r.Str() == "Bea"; }) != null, "Ada hears that Bea joined");

                    int scx = a.SX >> 4, scz = a.SZ >> 4;
                    var keys = new List<(int, int)>();
                    for (int dz = -2; dz <= 2; dz++) for (int dx = -2; dx <= 2; dx++) keys.Add((scx + dx, scz + dz));
                    a.Move(a.X, a.Y, a.Z);
                    a.Want(keys);
                    var t0 = DateTime.UtcNow;
                    while (a.Chunks.Count < keys.Count && a.Wait(Net.Msg.Chunk, null, 15000) != null) { }
                    Check(a.Chunks.Count == keys.Count, $"Ada gets all {keys.Count} chunks around spawn ({(DateTime.UtcNow - t0).TotalMilliseconds:0} ms, ~{a.Chunks.Values.Sum(c => c.Length) / keys.Count / 1024.0:0.0} KB each)");

                    px = a.SX + 2; pz = a.SZ; py = a.Top(px, pz) + 1;
                    Check(a.BlockAt(px, py, pz) == 0, $"there is air above the ground at {px},{py},{pz}");
                    a.Send(new Packet(Net.Msg.SetBlock).I32(px).I32(py).I32(pz).U8(Blocks.Stone).U8(0));
                    var seen = b.Wait(Net.Msg.Block, r => r.I32() == px && r.I32() == py && r.I32() == pz && r.U8() == Blocks.Stone);
                    Check(seen != null, "Bea sees the stone Ada placed");

                    a.Send(new Packet(Net.Msg.SetBlock).I32(px).I32(py + 1).I32(pz).U8(Blocks.Stone).U8(Blocks.Dirt));
                    Check(a.Wait(Net.Msg.Block, r => r.I32() == px && r.I32() == py + 1 && r.I32() == pz && r.U8() == 0) != null,
                          "an edit that disagrees about what was there is put right");
                    a.Send(new Packet(Net.Msg.SetBlock).I32(a.SX + 30).I32(py).I32(pz).U8(Blocks.Stone).U8(0));
                    Check(a.Wait(Net.Msg.Block, r => r.I32() == a.SX + 30 && r.I32() == py) != null, "an edit out of reach is refused");

                    b.Send(new Packet(Net.Msg.Chat).Str("hello Ada"));
                    Check(a.Wait(Net.Msg.Said, r => r.Str() == "Bea" && r.Str() == "hello Ada") != null, "chat reaches the other player");

                    // A chest: Ada fills a slot, Bea sees it, breaking it hands the things to Ada.
                    a.Send(new Packet(Net.Msg.SetBlock).I32(px).I32(py + 1).I32(pz).U8(Blocks.Chest).U8(0));
                    Check(b.Wait(Net.Msg.Block, r => r.I32() == px && r.I32() == py + 1 && r.I32() == pz && r.U8() == Blocks.Chest) != null, "Ada places a chest");
                    a.Send(new Packet(Net.Msg.OpenBox).I32(px).I32(py + 1).I32(pz));
                    var box = a.Wait(Net.Msg.Box);
                    Check(box != null && box.I32() == px && box.I32() == py + 1 && box.I32() == pz && box.Stacks().Length == Containers.ChestSlots, "she opens it: 27 empty slots");
                    b.Send(new Packet(Net.Msg.OpenBox).I32(px).I32(py + 1).I32(pz)); b.Wait(Net.Msg.Box);
                    a.Send(new Packet(Net.Msg.BoxSlot).I32(px).I32(py + 1).I32(pz).U8(2).Slot(new Stack("cobble", 9)));
                    var set = b.Wait(Net.Msg.BoxSet);
                    Check(set != null && set.I32() == px && set.I32() == py + 1 && set.I32() == pz && set.U8() == 2 && set.Slot().Count == 9, "Bea, looking in too, sees the cobble go in");
                    a.Send(new Packet(Net.Msg.CloseBox)); b.Send(new Packet(Net.Msg.CloseBox));
                    a.Send(new Packet(Net.Msg.SetBlock).I32(px).I32(py + 1).I32(pz).U8(0).U8(Blocks.Chest));
                    var give = a.Wait(Net.Msg.Give);
                    Check(give != null && give.Str() == "cobble" && give.U16() == 9, "breaking the chest gives Ada what was in it");
                    Check(a.Wait(Net.Msg.Mobs, null, 3000) != null, "the server tells players about the creatures near them");
                    a.Send(new Packet(Net.Msg.Drop).F32(a.X).F32(a.Y).F32(a.Z).Stacks(new[] { new Stack("torch", 4) }));
                    Check(b.Wait(Net.Msg.Block, r => { r.I32(); r.I32(); r.I32(); return r.U8() == Blocks.Satchel; }) != null, "what Ada drops lands in a satchel everyone sees");

                    var tone = new float[Voice.FrameSamples];
                    for (int i = 0; i < tone.Length; i++) tone[i] = 0.4f * (float)Math.Sin(2 * Math.PI * 440 * i / Voice.Rate);
                    var enc = new Voice.Encoder();
                    enc.Encode(tone);                                   // settle the coder like a running microphone would
                    a.Send(new Packet(Net.Msg.Voice).Blob(enc.Encode(tone)));
                    var heard = b.Wait(Net.Msg.Heard, r => r.I32() == a.Id);
                    var back = new float[Voice.FrameSamples];
                    bool decoded = heard != null && Voice.Decode(heard.Blob(), back);
                    double err = 0; for (int i = 0; i < back.Length; i++) err += (back[i] - tone[i]) * (back[i] - tone[i]);
                    double snr = decoded ? 10 * Math.Log10(back.Length * 0.08 / Math.Max(err, 1e-12)) : 0;
                    Check(decoded && snr > 20, $"Bea hears Ada's voice ({snr:0} dB signal to noise)");
                    for (int i = 0; i < 40; i++) a.Send(new Packet(Net.Msg.Voice).Blob(enc.Encode(tone)));
                    Thread.Sleep(400);
                    int got = 0; while (b.Wait(Net.Msg.Heard, null, 300) != null) got++;
                    Check(got <= Voice.MaxFramesPerSecond && got >= 20, $"a voice flood is cut to {Voice.MaxFramesPerSecond} frames a second ({got} of 40 passed)");

                    ax = a.X + 1.25f; ay = a.Y; az = a.Z - 0.5f;
                    a.Move(ax, ay, az);
                    Check(b.Wait(Net.Msg.Moved, r => r.I32() == a.Id && Math.Abs(r.F32() - ax) < 0.01f) != null, "Bea sees Ada move");

                    var inv = new Stack[Inventory.Size]; inv[0] = new Stack("cobble", 5); inv[3] = new Stack("torch", 12);
                    a.Send(new Packet(Net.Msg.Keep).F32(17).F32(12).F32(3).U8(3).Stacks(inv).Bool(true).I32(px).I32(py).I32(pz).I32(7).I32(3));

                    using (var c = new Bot(port)) { c.Create("ada", "another-pass"); Check(c.Refused()?.Contains("already an account") == true, "nobody can make a second account called ada"); }
                    using (var c = new Bot(port)) { c.Login("Bea", "wrong-password"); Check(c.Refused() == "Wrong password.", "a wrong password is refused"); }
                    using (var c = new Bot(port)) { c.Login("Nobody", "whatever1"); Check(c.Refused()?.Contains("no account") == true, "logging in to an account that doesn't exist is refused"); }
                    using (var c = new Bot(port)) { c.Create("Cyd", "123"); Check(c.Refused()?.Contains("Passwords are") == true, "a too-short password is refused"); }
                    using (var c = new Bot(port)) { c.Hello(Accounts.Mode.Token, "Ada", new string('0', 64)); Check(c.Refused() != null, "a made-up token is refused"); }
                    using (var d = new Bot(port)) { d.Hello(Accounts.Mode.Login, "Old", "whatever1", "", Net.Protocol - 1); Check(d.Refused()?.Contains("newer") == true, "an older game is told to update"); }
                    Thread.Sleep(300);
                }
                Thread.Sleep(500);
                srv.Stop();
                Check(!File.ReadAllText(Path.Combine(dir, "accounts.json")).Contains(pwA), "accounts.json never holds a password");

                // Back again, now with an invite code: the world and Ada are as she left them.
                cfg.Password = "open sesame";
                var srv2 = new GameServer(cfg); srv2.Start();
                using (var a = new Bot(srv2.Port))
                {
                    a.Hello(Accounts.Mode.Token, "Ada", tokenA);
                    bool w = a.Welcome();
                    Check(w && a.Token == "", "after a restart Ada gets in with her remember-me token (no invite needed to log in)");
                    Check(w && a.HasPos && Math.Abs(a.X - ax) < 0.01f && Math.Abs(a.Z - az) < 0.01f, "and is where she stood");
                    Check(w && a.Slots.Length == Inventory.Size && a.Slots[0].Item == "cobble" && a.Slots[0].Count == 5 && a.Slots[3].Count == 12,
                          "and her backpack came back");
                    Check(w && Math.Abs(a.Food - 12) < 0.01f && a.HasBed && a.BX == px, "and so do her hunger and her bed");
                    Check(a.XpLevel == 7, "and her experience level");
                    a.Want(new[] { (px >> 4, pz >> 4) });
                    a.Wait(Net.Msg.Chunk, null, 15000);
                    Check(a.BlockAt(px, py, pz) == Blocks.Stone, "and the stone she placed is still there");
                    using (var b = new Bot(srv2.Port)) { b.Login("ADA", pwA); Check(b.Welcome() && b.Name == "Ada", "logging in on another computer (any capitals) works"); }
                }
                using (var c = new Bot(srv2.Port)) { c.Create("Cyd", "cy-password"); Check(c.Refused()?.Contains("invite code") == true, "with an invite code set, a new account needs it"); }
                using (var c = new Bot(srv2.Port)) { c.Create("Cyd", "cy-password", "open sesame"); Check(c.Welcome(), "and the right code makes one"); }
                srv2.Stop();

                // A forgotten password: STRATUM_RESET clears it once, the next login sets a new one.
                cfg.Reset = "Ada";
                var srv3 = new GameServer(cfg); srv3.Start();
                using (var a = new Bot(srv3.Port)) { a.Hello(Accounts.Mode.Token, "Ada", tokenA); Check(a.Refused() != null, "after a reset the old token no longer works"); }
                using (var a = new Bot(srv3.Port)) { a.Login("Ada", "new-password"); Check(a.Welcome() && a.Slots[3].Count == 12, "Ada sets a new password by logging in, and keeps her things"); }
                srv3.Stop();
                var srv4 = new GameServer(cfg); srv4.Start();
                using (var a = new Bot(srv4.Port)) { a.Login("Ada", "someone-else"); Check(a.Refused() == "Wrong password.", "the same reset isn't applied twice"); }
                srv4.Stop();
                EmberChecks();
                WorldChecks(Path.Combine(dir, "worlds-test"));
            }
            catch (Exception e) { Check(false, "threw " + e); }
            finally { try { Directory.Delete(dir, true); } catch { } }
            Console.WriteLine(_fails == 0 ? "ALL PASSED" : $"{_fails} FAILED");
            return _fails == 0 ? 0 : 1;
        }
    }
}
