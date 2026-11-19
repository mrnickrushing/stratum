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
        public int Id, Seed; public bool HasPos; public float X, Y, Z; public int SX, SY, SZ; public Stack[] Slots;
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

        public void Hello(string name, string secret, int proto = Net.Protocol, string pass = "") =>
            Send(new Packet(Net.Msg.Hello).I32(proto).Str(name).Str(secret).Str(pass).Str("selftest"));

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

        public bool Welcome()
        {
            var r = Wait(Net.Msg.Welcome);
            if (r == null) return false;
            Id = r.I32(); Seed = r.I32(); r.Str(); r.Str(); r.Bool(); r.U8(); r.F64(); r.I32();
            SX = r.I32(); SY = r.I32(); SZ = r.I32();
            HasPos = r.Bool(); X = r.F32(); Y = r.F32(); Z = r.F32(); r.F32(); r.F32(); r.F32(); r.U8();
            Slots = r.Stacks();
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
                const string secretA = "0123456789abcdef0123456789abcdef";
                int px, py, pz; float ax, ay, az;

                using (var a = new Bot(port))
                using (var b = new Bot(port))
                {
                    a.Hello("Ada", secretA);
                    Check(a.Welcome() && a.Seed == 20261119 && !a.HasPos, "Ada joins, gets the seed and is new here");
                    b.Hello("Bea", "fedcba9876543210fedcba9876543210");
                    Check(b.Welcome(), "Bea joins");
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

                    ax = a.X + 1.25f; ay = a.Y; az = a.Z - 0.5f;
                    a.Move(ax, ay, az);
                    Check(b.Wait(Net.Msg.Moved, r => r.I32() == a.Id && Math.Abs(r.F32() - ax) < 0.01f) != null, "Bea sees Ada move");

                    var inv = new Stack[Inventory.Size]; inv[0] = new Stack("cobble", 5); inv[3] = new Stack("torch", 12);
                    a.Send(new Packet(Net.Msg.Keep).F32(17).U8(3).Stacks(inv));

                    using (var c = new Bot(port))
                    {
                        c.Hello("ada", "11111111111111111111111111111111");
                        Check(c.Wait(Net.Msg.Refuse) != null, "a second player can't take a name in use");
                    }
                    using (var d = new Bot(port))
                    {
                        d.Hello("Old", "22222222222222222222222222222222", Net.Protocol - 1);
                        Check(d.Wait(Net.Msg.Refuse) != null, "an older game is told to update");
                    }
                    Thread.Sleep(300);
                }
                Thread.Sleep(500);
                srv.Stop();

                // Back again: the world and Ada are as she left them.
                var srv2 = new GameServer(cfg); srv2.Start();
                using (var a = new Bot(srv2.Port))
                {
                    a.Hello("Ada", secretA);
                    bool w = a.Welcome();
                    Check(w && a.HasPos && Math.Abs(a.X - ax) < 0.01f && Math.Abs(a.Z - az) < 0.01f, "after a restart Ada is where she stood");
                    Check(w && a.Slots.Length == Inventory.Size && a.Slots[0].Item == "cobble" && a.Slots[0].Count == 5 && a.Slots[3].Count == 12,
                          "and her backpack came back");
                    a.Want(new[] { (px >> 4, pz >> 4) });
                    a.Wait(Net.Msg.Chunk, null, 15000);
                    Check(a.BlockAt(px, py, pz) == Blocks.Stone, "and the stone she placed is still there");
                }
                srv2.Stop();
            }
            catch (Exception e) { Check(false, "threw " + e); }
            finally { try { Directory.Delete(dir, true); } catch { } }
            Console.WriteLine(_fails == 0 ? "ALL PASSED" : $"{_fails} FAILED");
            return _fails == 0 ? 0 : 1;
        }
    }
}
