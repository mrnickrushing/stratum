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

        public int Outfit;
        public int Features = GameServer.KnowsKinds | GameServer.KnowsTypes;     // 0: act like a 0.3 client

        public void Hello(Accounts.Mode mode, string name, string secret, string invite = "", int proto = Net.Protocol, string world = null)
        {
            var pk = new Packet(Net.Msg.Hello).I32(proto).U8((byte)mode).Str(name).Str(secret).Str(invite).Str("selftest");
            if (world != null) { pk.Str(world); if (Features != 0) { pk.I32(Features); if (Outfit != 0) pk.U8((byte)Outfit); } }   // clients before 0.3.0 don't say which world
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
            Kinds = new List<WorldKind>();
            if (r.More) { int k = r.U8(); for (int i = 0; i < k; i++) Kinds.Add((WorldKind)r.U8()); }
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

        public float Food; public bool HasBed; public int BX, XpLevel; public string ServerName; public bool Creative; public int Difficulty; public WorldKind Kind; public List<WorldKind> Kinds; public bool Keeps;

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
            Kind = r.More ? (WorldKind)r.U8() : WorldKind.Normal;
            Keeps = r.More && r.Bool();
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
        // Themed worlds: what their land is like, and how the server hands them out.
        static void KindChecks(string dir)
        {
            byte Top(Chunk c, int x, int z, out int y) { for (y = Chunk.Height - 1; y > 0; y--) { byte b = c.Get(x, y, z); if (b != 0) return b; } return 0; }
            Chunk Make(WorldGen g, int cx, int cz) { var c = new Chunk(cx, cz); g.Generate(c); return c; }

            var sky = new WorldGen(4242, WorldKind.Sky);
            var (sx, sy, sz) = sky.FindSpawn();
            var home = Make(sky, sx >> 4, sz >> 4);
            int lx = sx & 15, lz = sz & 15;
            Check(Blocks.Get(home.Get(lx, sy - 1, lz)).Solid && home.Get(lx, sy, lz) == 0 && home.Get(lx, sy + 1, lz) == 0,
                  "Sky Islands: the start is on solid ground with room to stand");
            int under = sy - 1; while (under > 0 && home.Get(lx, under, lz) != 0) under--;
            bool open = true; for (int y = under; y >= 0; y--) if (home.Get(lx, y, lz) != 0) open = false;
            Check(under > 20 && open, $"under the start island is open sky (island bottom at {under}, nothing below)");
            Check(home.Get(0, 0, 0) != Blocks.Bedrock, "and no bedrock floor anywhere");
            int water = 0, leaks = 0, lava = 0, lavaLeaks = 0, islands = 0, voidCols = 0, ores = 0;
            var chunks = new Dictionary<(int, int), Chunk>();
            for (int cz = -6; cz <= 6; cz++) for (int cx = -6; cx <= 6; cx++) chunks[(cx, cz)] = Make(sky, cx, cz);
            byte At(int x, int y, int z) => (uint)y >= Chunk.Height ? (byte)0 : chunks.TryGetValue((x >> 4, z >> 4), out var c) ? c.Get(x & 15, y, z & 15) : (byte)255;
            foreach (var kv in chunks)
            {
                var c = kv.Value; int bx = c.X * 16, bz = c.Z * 16;
                for (int z = 0; z < 16; z++) for (int x = 0; x < 16; x++)
                {
                    if (Top(c, x, z, out _) == 0) voidCols++; else islands++;
                    for (int y = 1; y < Chunk.Height - 1; y++)
                    {
                        byte b = c.Get(x, y, z);
                        if (b == Blocks.CoalOre || b == Blocks.IronOre || b == Blocks.GoldOre || b == Blocks.CopperOre) ores++;
                        if (b != Blocks.Water && b != Blocks.Lava) continue;
                        int wx = bx + x, wz = bz + z;
                        bool lv = b == Blocks.Lava; if (lv) lava++; else water++;
                        foreach (var (dx, dy, dz) in new[] { (1, 0, 0), (-1, 0, 0), (0, 0, 1), (0, 0, -1), (0, -1, 0) })
                        {
                            byte n = At(wx + dx, y + dy, wz + dz);
                            if (n == 255 || n == b || n == Blocks.Ice || Blocks.Get(n).Solid) continue;
                            if (lv && n == 0 && dy == 0 && At(wx + dx, y - 1, wz + dz) != 0) continue;   // the air pocket above the lava
                            if (lv) lavaLeaks++; else leaks++;
                        }
                    }
                }
            }
            Check(islands > 2000 && voidCols > 8000, $"islands with sky between them ({islands} land columns, {voidCols} open)");
            Check(water > 50 && leaks == 0, $"ponds hold their water ({water} water blocks, {leaks} could spill)");
            Check(lava > 10 && lavaLeaks == 0, $"lava sits sealed inside the stone ({lava} lava blocks, {lavaLeaks} open edges)");
            Check(ores > 100, $"ores in the islands ({ores})");
            var again = Make(sky, 1, -2); Check(again.Blocks.AsSpan().SequenceEqual(chunks[(1, -2)].Blocks), "and the same seed always gives the same islands");

            var flat = new WorldGen(7, WorldKind.Flat);
            var fc = Make(flat, 3, 5); int fy; Top(fc, 2, 9, out fy);
            bool level = true; for (int z = 0; z < 16; z++) for (int x = 0; x < 16; x++) if (fc.Get(x, WorldGen.FlatTop, z) != Blocks.Grass || fc.Get(x, WorldGen.FlatTop + 1, z) is var a && a != 0 && !Blocks.Get(a).Replaceable && !Blocks.Get(a).Sway && a != Blocks.OakLog && a != Blocks.BirchLog) level = false;
            Check(level, "Flatlands: grass at the same height everywhere");
            var frozen = new WorldGen(11, WorldKind.Frozen);
            int seaIce = 0, seaWater = 0, snow = 0, grass = 0;
            for (int cz = -4; cz <= 4; cz++) for (int cx = -4; cx <= 4; cx++)
            {
                var c = Make(frozen, cx * 3, cz * 3);
                for (int z = 0; z < 16; z++) for (int x = 0; x < 16; x++)
                {
                    byte t = c.Get(x, Chunk.SeaLevel, z), top = Top(c, x, z, out _);
                    if (t == Blocks.Ice) seaIce++; else if (t == Blocks.Water && c.Get(x, Chunk.SeaLevel + 1, z) == 0) seaWater++;
                    if (top == Blocks.SnowyGrass || top == Blocks.Snow || top == Blocks.PineNeedles) snow++; else if (top == Blocks.Grass) grass++;
                }
            }
            Check(seaIce > 0 && seaWater == 0, $"Frozen Wilds: the seas are frozen over ({seaIce} ice, {seaWater} open water)");
            Check(snow > grass * 5, $"and it's snowy ({snow} snowy tops, {grass} grass)");
            var dunes = new WorldGen(13, WorldKind.Dunes); int sand = 0, all = 0;
            for (int i = 0; i < 400; i++) { var b = dunes.BiomeAt(i * 97 - 20000, i * 61 - 12000); all++; if (b == Biome.Desert) sand++; }
            Check(sand > all / 2, $"Endless Dunes: mostly desert ({sand} of {all})");
            var isl = new WorldGen(17, WorldKind.Islands); int sea = 0;
            for (int i = 0; i < 400; i++) if (isl.HeightAt(i * 97 - 20000, i * 61 - 12000) < Chunk.SeaLevel) sea++;
            Check(sea > 200 && sea < 360, $"Archipelago: more sea than land, but land ({sea} of 400 sea)");
            var peaks = new WorldGen(19, WorldKind.Peaks); var norm = new WorldGen(19); int hp = 0, hn = 0;
            for (int i = 0; i < 400; i++) { if (peaks.HeightAt(i * 97 - 20000, i * 61 - 12000) > 130) hp++; if (norm.HeightAt(i * 97 - 20000, i * 61 - 12000) > 130) hn++; }
            Check(hp > hn * 2 && hp > 60, $"Giant Peaks: far more high ground than a normal world ({hp} vs {hn} of 400 above 130)");

            // 0.5: Towering Wilds, Vast Lands and the single-biome worlds.
            var tower = new WorldGen(19, WorldKind.Towering); int ht = 0, hn2 = 0, low = 0;
            for (int i = 0; i < 400; i++) { int th = tower.HeightAt(i * 97 - 20000, i * 61 - 12000); if (th > 130) ht++; if (th < Chunk.SeaLevel + 12) low++; if (norm.HeightAt(i * 97 - 20000, i * 61 - 12000) > 130) hn2++; }
            Check(ht > hn2 * 3 && ht > 80 && low > 20, $"Towering Wilds: far more high ground than a normal world, and still valleys and sea ({ht} vs {hn2} above 130, {low} low)");
            int hang = 0, floaters = 0, turfed = 0, bare = 0;
            for (int cz = -5; cz <= 5; cz++) for (int cx = -5; cx <= 5; cx++)
            {
                var c = Make(tower, cx * 2 + 40, cz * 2 - 17);
                for (int z = 0; z < 16; z++) for (int x = 0; x < 16; x++)
                {
                    int ty; Top(c, x, z, out ty);
                    bool gap = false;
                    for (int y = ty - 1; y > 88; y--)
                    {
                        byte b = c.Get(x, y, z), up = c.Get(x, y + 1, z);
                        if (b == 0 && Blocks.Get(up).Solid && !Blocks.Get(up).Sway) gap = true;                                  // rock with air beneath it
                        if ((b == Blocks.Sand || b == Blocks.Gravel) && c.Get(x, y - 1, z) == 0) floaters++;
                        if (up == 0 && c.Get(x, y + 2, z) == 0 && c.Get(x, y + 3, z) == 0 && y < ty - 3) { if (b == Blocks.Grass || b == Blocks.SnowyGrass || b == Blocks.Sand) turfed++; else if (b == Blocks.Dirt) bare++; }
                    }
                    if (gap) hang++;
                }
            }
            Check(hang > 300, $"with overhangs and arches ({hang} columns with rock over open air)");
            Check(floaters == 0, $"no sand or gravel left hanging to fall ({floaters})");
            Check(turfed > 50 && bare < turfed / 10, $"and the ledges under them grow over ({turfed} grown, {bare} bare soil)");
            var tAgain = Make(tower, 41, -16); Check(tAgain.Blocks.AsSpan().SequenceEqual(Make(tower, 41, -16).Blocks), "the same seed always gives the same cliffs");
            var vast = new WorldGen(23, WorldKind.Vast); var plain = new WorldGen(23); int vRuns = 0, nRuns = 0; Biome pv = 0, pn = 0;
            bool Region(Biome b) => b != Biome.River && b != Biome.Beach && b != Biome.Peaks && b != Biome.Ocean;
            for (int i = 0; i < 4000; i++)
            {
                // Lowland only: a mountain is cold whatever region it stands in.
                var cv = vast.Sample(i * 16 - 32000, 300); var cn = plain.Sample(i * 16 - 32000, 300);
                var bv = WorldGen.BiomeOf(cv); var bn = WorldGen.BiomeOf(cn);
                if (Region(bv) && cv.Height < 95) { if (bv != pv) vRuns++; pv = bv; }
                if (Region(bn) && cn.Height < 95) { if (bn != pn) nRuns++; pn = bn; }
            }
            Check(vRuns * 2 < nRuns && vRuns > 3, $"Vast Lands: regions are far wider than normal ({vRuns} changes across 64,000 blocks, normal {nRuns})");
            for (var k = WorldGen.FirstOnly; k <= WorldGen.LastOnly; k++)
            {
                var g = new WorldGen(31 + (int)k, k); var want = WorldGen.OnlyBiome(k); int ok = 0, wet = 0, other = 0;
                for (int i = 0; i < 1500; i++) { var b = g.BiomeAt(i * 97 - 70000, i * 61 - 40000); if (b == want) ok++; else if (b == Biome.River) wet++; else other++; }
                var (px, py, pz) = g.FindSpawn(); var sc = Make(g, px >> 4, pz >> 4);
                bool stand = Blocks.Get(sc.Get(px & 15, py - 1, pz & 15)).Solid && py > Chunk.SeaLevel;
                Check(other == 0 && ok > 1200 && wet > 0 && wet < 300 && stand, $"{WorldGen.KindName(k)}: nothing but {WorldGen.BiomeNames[(int)want]} and rivers ({ok} of 1500, {wet} river), with a dry start");
                Check(WorldGen.ParseKind(WorldGen.KindName(k)) == k, $"and \"{WorldGen.KindName(k)}\" is how a server asks for it");
            }
            var frost = Make(new WorldGen(31 + (int)WorldKind.OnlyFrost, WorldKind.OnlyFrost), 0, 0); var oak = new WorldGen(5, WorldKind.OnlyOakwood); int logs = 0;
            for (int cz = 0; cz < 4; cz++) for (int cx = 0; cx < 4; cx++) { var c = Make(oak, cx, cz); for (int i = 0; i < c.Blocks.Length; i++) if (c.Blocks[i] == Blocks.OakLog) logs++; }
            Check(logs > 150, $"All Oakwood is wooded ({logs} oak logs in 16 chunks)");
            Check(WorldGen.ParseKind("towering") == WorldKind.Towering && WorldGen.ParseKind("vast") == WorldKind.Vast && WorldGen.ParseKind("islands") == WorldKind.Islands, "the other kinds still parse");

            // Furnaces: fuel, time and room for the result.
            {
                var f = new Stack[Smelting.Slots];
                f[Smelting.In] = new Stack("iron_ore", 10); f[Smelting.Fuel] = new Stack("coal", 1);
                int ticks = 0; while (Smelting.Busy(f) && ticks < 100000) { Smelting.Step(f); ticks++; }
                Check(f[Smelting.Out].Item == "iron_ingot" && f[Smelting.Out].Count == 8 && f[Smelting.In].Count == 2 && f[Smelting.Fuel].Empty && ticks >= Smelting.CookTicks * 8,
                      $"one coal smelts eight ore and then the fire goes out ({f[Smelting.Out].Count} ingots, {f[Smelting.In].Count} ore left, {ticks / 10} s)");
                f[Smelting.Fuel] = new Stack("oak_planks", 2); ticks = 0; while (Smelting.Busy(f) && ticks < 100000) { Smelting.Step(f); ticks++; }
                Check(f[Smelting.Out].Count == 10 && f[Smelting.In].Empty && f[Smelting.Fuel].Empty, $"planks burn too, for less ({f[Smelting.Out].Count} ingots)");
                var g = new Stack[Smelting.Slots]; g[Smelting.In] = new Stack("cobble", 3); g[Smelting.Fuel] = new Stack("coal", 1); g[Smelting.Out] = new Stack("glass", 5);
                Check(!Smelting.Busy(g) && Smelting.Step(g) == 0 && g[Smelting.Fuel].Count == 1, "no fuel is lit when the result has nowhere to go");
                var h = new Stack[Smelting.Slots]; h[Smelting.In] = new Stack("dirt", 3); h[Smelting.Fuel] = new Stack("coal", 1);
                Check(!Smelting.Busy(h), "or when the thing can't be smelted");
                var c = new Stack[Smelting.Slots]; c[Smelting.In] = new Stack("raw_beef", 1); c[Smelting.Fuel] = new Stack("stick", 2);
                ticks = 0; while (Smelting.Busy(c) && ticks < 100000) { Smelting.Step(c); ticks++; }
                Check(c[Smelting.Out].Item == "steak" && c[Smelting.Fuel].Empty, "two sticks cook a steak");
                var boxes = new Containers(); var fs = new Furnaces(boxes);
                boxes.Set(1, 2, 3, Smelting.In, new Stack("sand", 1)); boxes.Set(1, 2, 3, Smelting.Fuel, new Stack("stick", 4)); fs.Wake(1, 2, 3);
                for (int i = 0; i < 300; i++) fs.Tick(0.1f);
                Check(boxes.At(1, 2, 3)[Smelting.Out].Item == "glass" && fs.Active == 0 && boxes.At(1, 2, 3)[Smelting.Fuel].Count == 2, "a furnace left alone finishes, uses only the fuel it needs, and rests");
            }

            // On a server: the kind travels with the welcome, older games are kept out, and a world keeps its kind.
            int Seed(string t) => int.TryParse(t, out int n) ? n : t.GetHashCode();
            var cfg = new ServerConfig { Port = 0, Dir = dir, Seed = 99, MaxView = 4,
                Worlds = WorldDef.Parse("main|Home;sky|Sky Islands|survival|easy||Floating islands|sky;flat|Flatlands|creative|||Flat|flat;tall|Towering Wilds|survival|easy||Cliffs|towering|keep", Seed) };
            var srv = new GameServer(cfg); srv.Start();
            try
            {
                using (var q = new Bot(srv.Port)) { q.Worlds(); Check(q.Kinds?.Count == 4 && q.Kinds[1] == WorldKind.Sky && q.Kinds[2] == WorldKind.Flat && q.Kinds[3] == WorldKind.Towering, "a ping says what sort each world is"); }
                using (var a = new Bot(srv.Port))
                {
                    a.Create("Sky", "sky-password", world: "sky");
                    Check(a.Welcome() && a.Kind == WorldKind.Sky && a.SY > 60, "joining Sky Islands says so, and the spawn is up on an island");
                    // Falling off: the things wait on the island where she last stood, not down in the sky.
                    a.Want(new[] { (a.SX >> 4, a.SZ >> 4) }); a.Wait(Net.Msg.Chunk);
                    a.Move(a.SX + 0.5f, a.SY, a.SZ + 0.5f); System.Threading.Thread.Sleep(200);
                    a.Move(a.SX + 40.5f, -30, a.SZ + 0.5f); System.Threading.Thread.Sleep(200);
                    a.Send(new Packet(Net.Msg.Drop).F32(a.SX + 0.5f).F32(a.SY + 0.5f).F32(a.SZ + 0.5f).Stacks(new[] { new Stack("torch", 4) }));
                    var fell = a.Wait(Net.Msg.Block, r => { int qx = r.I32(), qy = r.I32(), qz = r.I32(); return r.U8() == Blocks.Satchel && Math.Abs(qx - a.SX) <= 2 && Math.Abs(qz - a.SZ) <= 2 && Math.Abs(qy - a.SY) <= 3; });
                    Check(fell != null, "after a fall into the sky the satchel is left on the island she fell from");
                }
                using (var old = new Bot(srv.Port) { Features = 0 })
                {
                    old.Login("Sky", "sky-password", world: "sky");
                    Check(old.Refused()?.Contains("newest Stratum") == true, "a 0.3 game is asked to update before going to Sky Islands");
                }
                using (var old = new Bot(srv.Port) { Features = 1 })
                {
                    old.Login("Sky", "sky-password", world: "tall");
                    Check(old.Refused()?.Contains("newest Stratum") == true, "a 0.4 game is asked to update before going to Towering Wilds");
                }
                using (var old = new Bot(srv.Port) { Features = 1 })
                {
                    old.Login("Sky", "sky-password", world: "sky");
                    Check(old.Welcome() && old.Kind == WorldKind.Sky && !old.Keeps, "but still gets into Sky Islands (where things drop as usual)");
                }
                using (var now = new Bot(srv.Port))
                {
                    now.Login("Sky", "sky-password", world: "tall");
                    Check(now.Welcome() && now.Kind == WorldKind.Towering, "and a 0.5 game gets into Towering Wilds");
                    Check(now.Keeps, "a world marked keep tells the game things are kept on death");
                }
                using (var old = new Bot(srv.Port) { Features = 0 })
                {
                    old.Login("Sky", "sky-password", world: "main");
                    Check(old.Welcome() && old.Kind == WorldKind.Normal, "but can still play in a normal world");
                }
            }
            finally { srv.Stop(); }
            var saved = Directory.Exists(Path.Combine(dir, "backups")) ? Directory.GetDirectories(Path.Combine(dir, "backups")) : new string[0];
            Check(saved.Length == 0, "a brand-new server has nothing to back up yet");
            cfg.Worlds = WorldDef.Parse("main|Home;sky|Sky Islands|survival|easy||Changed my mind|frozen", Seed);
            var srv2 = new GameServer(cfg); srv2.Start();
            try
            {
                using (var a = new Bot(srv2.Port))
                {
                    a.Login("Sky", "sky-password", world: "sky");
                    Check(a.Welcome() && a.Kind == WorldKind.Sky, "a world keeps the sort it was made as, whatever the settings say later");
                }
            }
            finally { srv2.Stop(); }
            saved = Directory.Exists(Path.Combine(dir, "backups")) ? Directory.GetDirectories(Path.Combine(dir, "backups")) : new string[0];
            Check(saved.Length == 1 && File.Exists(Path.Combine(saved[0], "accounts.json")) && File.Exists(Path.Combine(saved[0], "worlds", "sky", "world.json"))
                  && !Directory.Exists(Path.Combine(saved[0], "backups")), "starting again backs up the accounts and every world first");
            for (int i = 0; i < Backups.Keep + 3; i++) { Directory.CreateDirectory(Path.Combine(dir, "backups", $"2020010{i}-000000")); }
            System.Threading.Thread.Sleep(1100); Backups.Take(dir);
            Check(Directory.GetDirectories(Path.Combine(dir, "backups")).Length == Backups.Keep, $"only the newest {Backups.Keep} backups are kept");
        }

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
                    b.Outfit = 3;
                    b.Create("Bea", "bea-password", world: "");
                    Check(b.Welcome(), "Bea makes an account and joins");
                    int wears = -1;
                    Check(a.Wait(Net.Msg.Joined, r => { r.I32(); if (r.Str() != "Bea") return false; r.F32(); r.F32(); r.F32(); r.F32(); wears = r.More ? r.U8() : -1; return true; }) != null, "Ada hears that Bea joined");
                    Check(wears == 3, $"and which outfit Bea is wearing ({wears})");

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
                    // Only one of them in the chest at a time, so the same stack can't be taken twice.
                    b.Send(new Packet(Net.Msg.OpenBox).I32(px).I32(py + 1).I32(pz));
                    Check(b.Wait(Net.Msg.Said, r => r.Str() == "" && r.Str().StartsWith("Ada is using that")) != null, "Bea is told Ada is using the chest");
                    a.Send(new Packet(Net.Msg.BoxSlot).I32(px).I32(py + 1).I32(pz).U8(2).Slot(new Stack("cobble", 9)));
                    b.Send(new Packet(Net.Msg.BoxSlot).I32(px).I32(py + 1).I32(pz).U8(2).Slot(Stack.None));      // Bea can't empty it from outside
                    a.Send(new Packet(Net.Msg.CloseBox));
                    b.Send(new Packet(Net.Msg.OpenBox).I32(px).I32(py + 1).I32(pz));
                    var bbox = b.Wait(Net.Msg.Box); Stack[] inBox = null;
                    if (bbox != null) { bbox.I32(); bbox.I32(); bbox.I32(); inBox = bbox.Stacks(); }
                    Check(inBox != null && inBox[2].Item == "cobble" && inBox[2].Count == 9, "once Ada closes it Bea opens it and finds the cobble");
                    b.Send(new Packet(Net.Msg.CloseBox));
                    a.Send(new Packet(Net.Msg.SetBlock).I32(px).I32(py + 1).I32(pz).U8(0).U8(Blocks.Chest));
                    var give = a.Wait(Net.Msg.Give);
                    Check(give != null && give.Str() == "cobble" && give.U16() == 9, "breaking the chest gives Ada what was in it");
                    Check(a.Wait(Net.Msg.Mobs, null, 3000) != null, "the server tells players about the creatures near them");
                    a.Send(new Packet(Net.Msg.Drop).F32(a.X).F32(a.Y).F32(a.Z).Stacks(new[] { new Stack("torch", 4) }));
                    Check(b.Wait(Net.Msg.Block, r => { r.I32(); r.I32(); r.I32(); return r.U8() == Blocks.Satchel; }) != null, "what Ada drops lands in a satchel everyone sees");

                    // A furnace: ore in, coal under it, and it starts to work while she watches.
                    a.Send(new Packet(Net.Msg.SetBlock).I32(px).I32(py + 1).I32(pz).U8(Blocks.Furnace).U8(0));
                    b.Wait(Net.Msg.Block, r => r.I32() == px && r.I32() == py + 1 && r.I32() == pz && r.U8() == Blocks.Furnace);
                    a.Send(new Packet(Net.Msg.OpenBox).I32(px).I32(py + 1).I32(pz));
                    Check(a.Wait(Net.Msg.Box) != null, "Ada opens a furnace");
                    a.Send(new Packet(Net.Msg.BoxSlot).I32(px).I32(py + 1).I32(pz).U8(Smelting.In).Slot(new Stack("iron_ore", 2)));
                    a.Send(new Packet(Net.Msg.BoxSlot).I32(px).I32(py + 1).I32(pz).U8(Smelting.Fuel).Slot(new Stack("coal", 1)));
                    a.Send(new Packet(Net.Msg.BoxSlot).I32(px).I32(py + 1).I32(pz).U8(Smelting.Burn).Slot(new Stack("_burn", 9999)));      // she can't light it by hand
                    Stack fire = default;
                    Check(a.Wait(Net.Msg.BoxSet, r => { r.I32(); r.I32(); r.I32(); if (r.U8() != Smelting.Burn) return false; fire = r.Slot(); return true; }, 3000) != null
                          && fire.Count > 0 && fire.Count < Smelting.CookTicks * 8 && fire.Wear == Smelting.CookTicks * 8, $"the coal lights and burns down ({fire.Count} of {fire.Wear})");
                    Check(a.Wait(Net.Msg.BoxSet, r => { r.I32(); r.I32(); r.I32(); return r.U8() == Smelting.Cook && r.Slot().Count > 0; }, 3000) != null, "and the ore starts to smelt");
                    a.Send(new Packet(Net.Msg.CloseBox));
                    a.Send(new Packet(Net.Msg.SetBlock).I32(px).I32(py + 1).I32(pz).U8(0).U8(Blocks.Furnace));
                    var back1 = a.Wait(Net.Msg.Give); string gotItem = back1?.Str();
                    var back2 = a.Wait(Net.Msg.Give, null, 600);
                    Check(gotItem == "iron_ore" && back2 == null, "breaking it gives back the ore, and nothing of the fire");

                    // A whole pack (40 slots and the XP) in one satchel: every stack can be reached.
                    var pack = new Stack[41]; for (int i = 0; i < pack.Length; i++) pack[i] = new Stack(i % 2 == 0 ? "cobble" : "torch", i + 1);
                    b.Move(b.X + 3, b.Y, b.Z); System.Threading.Thread.Sleep(150);
                    b.Send(new Packet(Net.Msg.Drop).F32(b.X + 3).F32(b.Y).F32(b.Z).Stacks(pack));
                    int tx = 0, ty = 0, tz = 0;
                    b.Wait(Net.Msg.Block, r => { int qx = r.I32(), qy = r.I32(), qz = r.I32(); if (r.U8() != Blocks.Satchel) return false; tx = qx; ty = qy; tz = qz; return true; });
                    b.Send(new Packet(Net.Msg.OpenBox).I32(tx).I32(ty).I32(tz));
                    var sbox = b.Wait(Net.Msg.Box); Stack[] held = null;
                    if (sbox != null) { sbox.I32(); sbox.I32(); sbox.I32(); held = sbox.Stacks(); }
                    Check(held != null && held.Length >= 41 && held[40].Count == 41, $"a satchel holds a full pack of 41 stacks ({held?.Length} slots)");
                    b.Send(new Packet(Net.Msg.BoxSlot).I32(tx).I32(ty).I32(tz).U8(40).Slot(Stack.None));
                    b.Send(new Packet(Net.Msg.CloseBox));
                    b.Send(new Packet(Net.Msg.OpenBox).I32(tx).I32(ty).I32(tz));
                    sbox = b.Wait(Net.Msg.Box); held = null;
                    if (sbox != null) { sbox.I32(); sbox.I32(); sbox.I32(); held = sbox.Stacks(); }
                    Check(held != null && held.Length > 40 && held[40].Empty && held[39].Count == 40, "and the last stack can be taken out of it");
                    b.Send(new Packet(Net.Msg.CloseBox));

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
                KindChecks(Path.Combine(dir, "kinds-test"));
            }
            catch (Exception e) { Check(false, "threw " + e); }
            finally { try { Directory.Delete(dir, true); } catch { } }
            Console.WriteLine(_fails == 0 ? "ALL PASSED" : $"{_fails} FAILED");
            return _fails == 0 ? 0 : 1;
        }
    }
}
