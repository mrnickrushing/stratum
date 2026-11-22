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
        public int Id, Seed; public bool HasPos; public string Name, Token, TokenOnWire; public float X, Y, Z; public int SX, SY, SZ; public Stack[] Slots;
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

        public bool Sealed;                                      // log in the 0.8 way
        public byte[] KeyMod, KeyExp, Nonce, LoginKey, SentSealed;
        public int Outfit;
        public byte[] Look;                              // an outfit made piece by piece (0.12)
        public string Notes = "";
        public int Features = GameServer.KnowsKinds | GameServer.KnowsTypes | GameServer.KnowsTransfers | GameServer.KnowsBuilds;     // 0: act like a 0.3 client

        public void Hello(Accounts.Mode mode, string name, string secret, string invite = "", int proto = Net.Protocol, string world = null)
        {
            if (Sealed)
            {
                // A 0.8 game: asks for the server's key first, then sends the password and invite sealed to it.
                if (KeyMod == null) Worlds();
                SentSealed = Secure.Seal(KeyMod, KeyExp, Nonce, secret, invite, out LoginKey);
                Send(new Packet(Net.Msg.Hello).I32(proto).U8((byte)mode).Str(name).Str("").Str("").Str("selftest").Str(world ?? "").I32(Features).U8((byte)Outfit).Blob(SentSealed).Blob(Look ?? new byte[0]));
                return;
            }
            var pk = new Packet(Net.Msg.Hello).I32(proto).U8((byte)mode).Str(name).Str(secret).Str(invite).Str("selftest");
            if (world != null) { pk.Str(world); if (Features != 0) { pk.I32(Features); if (Outfit != 0) { pk.U8((byte)Outfit); if (Look != null) pk.Blob(new byte[0]).Blob(Look); } } }   // clients before 0.3.0 don't say which world
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
            if (r.More) { KeyMod = r.Blob(); KeyExp = r.Blob(); Nonce = r.Blob(); }
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
            Name = r.Str(); Token = r.Str(); TokenOnWire = Token;
            if (Secure.IsSealed(Token)) Token = Secure.OpenText(LoginKey, Token) ?? "";
            Food = r.F32(); r.F32(); HasBed = r.Bool(); BX = r.I32(); r.I32(); r.I32(); Difficulty = r.U8(); XpLevel = r.I32(); r.I32();
            Kind = r.More ? (WorldKind)r.U8() : WorldKind.Normal;
            Keeps = r.More && r.Bool();
            Notes = r.More ? r.Str() : "";
            Slots = r.WithMore(Slots);
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

        public ushort BlockAt(int x, int y, int z)
        {
            if (!Chunks.TryGetValue(Chunk.Key(x >> 4, z >> 4), out var data)) return 65535;
            var b = new ushort[Chunk.Volume]; Net.Decompress(data, b);
            return b[Chunk.Index(x & 15, y, z & 15)];
        }

        public int Top(int x, int z) { for (int y = Chunk.Height - 1; y > 0; y--) { ushort b = BlockAt(x, y, z); if (b != 0 && b != 65535) return y; } return 0; }

        public void Dispose() { try { _tcp.Close(); } catch { } }
    }

    public static class SelfTest
    {
        static int _fails;
        // Themed worlds: what their land is like, and how the server hands them out.
        static void KindChecks(string dir)
        {
            ushort Top(Chunk c, int x, int z, out int y) { for (y = Chunk.Height - 1; y > 0; y--) { ushort b = c.Get(x, y, z); if (b != 0) return b; } return 0; }
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
            ushort At(int x, int y, int z) => (uint)y >= Chunk.Height ? (ushort)0 : chunks.TryGetValue((x >> 4, z >> 4), out var c) ? c.Get(x & 15, y, z & 15) : (ushort)65535;
            foreach (var kv in chunks)
            {
                var c = kv.Value; int bx = c.X * 16, bz = c.Z * 16;
                for (int z = 0; z < 16; z++) for (int x = 0; x < 16; x++)
                {
                    if (Top(c, x, z, out _) == 0) voidCols++; else islands++;
                    for (int y = 1; y < Chunk.Height - 1; y++)
                    {
                        ushort b = c.Get(x, y, z);
                        if (b == Blocks.CoalOre || b == Blocks.IronOre || b == Blocks.GoldOre || b == Blocks.CopperOre) ores++;
                        if (b != Blocks.Water && b != Blocks.Lava) continue;
                        int wx = bx + x, wz = bz + z;
                        bool lv = b == Blocks.Lava; if (lv) lava++; else water++;
                        foreach (var (dx, dy, dz) in new[] { (1, 0, 0), (-1, 0, 0), (0, 0, 1), (0, 0, -1), (0, -1, 0) })
                        {
                            ushort n = At(wx + dx, y + dy, wz + dz);
                            if (n == 65535 || n == b || n == Blocks.Ice || Blocks.Get(n).Solid) continue;
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
                    ushort t = c.Get(x, Chunk.SeaLevel, z), top = Top(c, x, z, out _);
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
                        ushort b = c.Get(x, y, z), up = c.Get(x, y + 1, z);
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
            Console.WriteLine($"INFO block numbers used: {Blocks.Count} of {Blocks.Max}, textures: {(int)Tex.Count}");
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
                // Saved part-way through and loaded again: the fire and the progress are still there, and it finishes.
                var b2 = new Containers(); var f2 = new Furnaces(b2);
                b2.Set(4, 5, 6, Smelting.In, new Stack("iron_ore", 3)); b2.Set(4, 5, 6, Smelting.Fuel, new Stack("coal", 1)); f2.Wake(4, 5, 6);
                for (int i = 0; i < 20; i++) f2.Tick(0.1f);
                var was = b2.At(4, 5, 6); int burnWas = was[Smelting.Burn].Count, cookWas = was[Smelting.Cook].Count;
                Directory.CreateDirectory(dir); string file = Path.Combine(dir, "furnace-test.bin"); b2.Save(file);
                var b3 = new Containers(); b3.Load(file); var f3 = new Furnaces(b3); f3.WakeSaved();
                var now = b3.At(4, 5, 6);
                Check(burnWas > 600 && cookWas == 20 && now[Smelting.Burn].Count == burnWas && now[Smelting.Burn].Wear == Smelting.CookTicks * 8 && now[Smelting.Cook].Count == cookWas && f3.Active == 1,
                      $"a burning furnace is still burning after a save and reload ({now[Smelting.Burn].Count} of {burnWas} burn, {now[Smelting.Cook].Count} of {cookWas} done)");
                for (int i = 0; i < 3000; i++) f3.Tick(0.1f);
                Check(b3.At(4, 5, 6)[Smelting.Out].Count == 3 && b3.At(4, 5, 6)[Smelting.Fuel].Empty, "and finishes on the coal it had already lit");
                File.Delete(file);

                // Loaded and fuelled but never lit before the save: it starts by itself after loading. A chest with
                // the same things in the same slots does not.
                var b4 = new Containers(); var f4 = new Furnaces(b4);
                b4.Set(7, 8, 9, Smelting.In, new Stack("sand", 2)); b4.Set(7, 8, 9, Smelting.Fuel, new Stack("coal", 1)); f4.Wake(7, 8, 9);
                b4.Set(1, 1, 1, 0, new Stack("sand", 2)); b4.Set(1, 1, 1, 1, new Stack("coal", 1));           // a chest
                b4.Save(file);
                var b5 = new Containers(); b5.Load(file); var f5 = new Furnaces(b5); f5.WakeSaved();
                for (int i = 0; i < 400; i++) f5.Tick(0.1f);
                Check(b5.At(7, 8, 9)[Smelting.Out].Item == "glass" && b5.At(7, 8, 9)[Smelting.Out].Count == 2, "a furnace saved ready to start gets going after a reload");
                Check(b5.At(1, 1, 1)[0].Count == 2 && b5.At(1, 1, 1)[1].Count == 1 && b5.At(1, 1, 1)[2].Empty, "and a chest holding sand over coal is left alone");
                File.Delete(file);

                // Progress on one thing doesn't finish another.
                var sw = new Stack[27]; sw[Smelting.In] = new Stack("iron_ore", 2); sw[Smelting.Fuel] = new Stack("coal", 1);
                for (int i = 0; i < 79; i++) Smelting.Step(sw);
                sw[Smelting.In] = new Stack("gold_ore", 2); Smelting.Step(sw);
                Check(sw[Smelting.Out].Empty && sw[Smelting.Cook].Count <= 1, $"swapping the ore starts the work again (progress {sw[Smelting.Cook].Count}, nothing made)");
                for (int i = 0; i < Smelting.CookTicks; i++) Smelting.Step(sw);
                Check(sw[Smelting.Out].Item == "gold_ingot" && sw[Smelting.Out].Count == 1, "and the new ore takes its full time");

                // A change made on an out-of-date view is applied as a difference.
                Stack R(Stack now, Stack seen, Stack want) => Containers.Reconcile(now, seen, want);
                Check(R(new Stack("iron_ingot", 2), new Stack("iron_ingot", 1), Stack.None).Count == 1, "taking the one ingot you saw leaves the one made since");
                Check(R(new Stack("iron_ore", 9), new Stack("iron_ore", 10), new Stack("iron_ore", 20)).Count == 19, "adding ten ore to a stack the furnace used one of gives nineteen");
                Check(R(new Stack("iron_ingot", 3), new Stack("iron_ingot", 3), Stack.None).Empty, "and an up-to-date view is taken as it is");

                // The adventure worlds: the same build from the same kind every time, standing where it should.
                {
                    foreach (WorldKind k in new[] { WorldKind.Castle, WorldKind.Parkour, WorldKind.Maze, WorldKind.Town })
                    {
                        var g1 = new WorldGen(5, k); var g2 = new WorldGen(99, k); var cv = Builds.Of(k);
                        int filled = 0, same = 0, total = 0;
                        for (int cx = -3; cx <= 3; cx++)
                            for (int cz = 0; cz <= 5; cz++)
                            {
                                var a1 = new Chunk(cx, cz); g1.Generate(a1); var a2 = new Chunk(cx, cz); g2.Generate(a2);
                                for (int y = Builds.Ground; y < Builds.Ground + 30; y++)
                                    for (int i = 0; i < 256; i++)
                                    {
                                        ushort v = a1.Blocks[i | (y << 8)];
                                        if (v != 0 && Blocks.Defs[v] == null) filled = -100000;
                                        if (v != 0 && v != Blocks.TallGrass) filled++;
                                        int wx = cx * 16 + (i & 15), wz = cz * 16 + (i >> 4); if (!cv.In(wx, y - Builds.Ground, wz) || cv.Get(wx, y - Builds.Ground, wz) == Builds.Leave) continue;
                                        total++; if (v == a2.Blocks[i | (y << 8)]) same++;
                                    }
                            }
                        var (px0, py0, pz0) = g1.FindSpawn();
                        var sc0 = new Chunk(px0 >> 4, pz0 >> 4); g1.Generate(sc0);
                        bool stand = Blocks.Get(sc0.Get(px0 & 15, py0 - 1, pz0 & 15)).Solid && sc0.Get(px0 & 15, py0, pz0 & 15) == 0 && sc0.Get(px0 & 15, py0 + 1, pz0 & 15) == 0;
                        Check(filled > 1000 && same == total && stand && WorldGen.ParseKind(WorldGen.KindName(k)) == k,
                            $"{WorldGen.KindName(k)}: built the same whatever the seed ({filled} blocks above ground), with somewhere to stand at the start");
                    }
                    // Sky Run: every jump is within what the player can do (run 6.2 a second, jump speed 8.6, gravity 30:
                    // about three across on the level, two when going up one), and every landing is really there.
                    {
                        var run = Builds.Of(WorldKind.Parkour); var steps = Builds.ParkourSteps;
                        int bad = 0, missing = 0; string first = "";
                        for (int i = 1; i < steps.Count; i++)
                        {
                            var a0 = steps[i - 1]; var b0 = steps[i];
                            // Somewhere on each landing to stand: something solid underfoot and two clear above it.
                            int room = 0;
                            for (int px = b0.x0; px <= b0.x1; px++) for (int pz = b0.z0; pz <= b0.z1; pz++)
                            { ushort ub = run.Get(px, b0.y - 1, pz); if (ub != Builds.Leave && ub != 0 && Blocks.Get(ub).Solid && !Blocks.Get(run.Get(px, b0.y, pz)).Solid && !Blocks.Get(run.Get(px, b0.y + 1, pz)).Solid) room++; }
                            if (room == 0) { missing++; if (first == "") first = $" step {i}: nowhere to stand at {b0.x0},{b0.y},{b0.z0}"; }
                            if (b0.climb) continue;
                            int gx = Math.Max(0, Math.Max(b0.x0 - a0.x1, a0.x0 - b0.x1) - 1), gz = Math.Max(0, b0.z0 - a0.z1 - 1), rise = b0.y - a0.y;
                            double far = Math.Sqrt(gx * gx + gz * gz);
                            bool ok = rise <= 1 && far <= (rise == 1 ? 2.3 : 3.0);
                            if (!ok) { bad++; if (first == "") first = $" step {i}: {gx} by {gz} across, {rise} up"; }
                        }
                        var last = steps[steps.Count - 1];
                        Check(steps.Count > 60 && bad == 0 && missing == 0 && run.In(last.x1 + 2, last.y + 10, last.z1 + 4), $"Sky Run: all {steps.Count - 1} jumps can be made and every landing is there, to the finish at z {last.z1} height {last.y} ({bad} too far, {missing} missing){first}");
                    }
                    var maze = Builds.Of(WorldKind.Maze);
                    // Walk the maze from the gate: the prize in the middle can be reached.
                    var mq = new Queue<(int, int)>(); var been = new HashSet<(int, int)>();
                    mq.Enqueue((1, 9)); been.Add((1, 9)); bool reached = false;
                    while (mq.Count > 0)
                    {
                        var (x, z) = mq.Dequeue();
                        if (Math.Abs(x - (-30 + 9 * 3 + 3)) <= 1 && Math.Abs(z - (10 + 9 * 3 + 3)) <= 1) { reached = true; break; }
                        foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                        {
                            int nx = x + dx, nz = z + dz;
                            ushort a0 = maze.Get(nx, 0, nz), a1 = maze.Get(nx, 1, nz);
                            if (a0 == Builds.Leave || been.Contains((nx, nz))) continue;
                            if (Blocks.Get(a0).Solid || Blocks.Get(a1).Solid) continue;
                            been.Add((nx, nz)); mq.Enqueue((nx, nz));
                        }
                    }
                    Check(reached, "Hedge Maze: there is a way from the gate to the prize");
                }

                // Block numbers are two bytes now; a chunk file from before (one byte a block) still loads as it was.
                {
                    var old = new byte[Chunk.Volume];
                    old[Chunk.Index(3, 70, 5)] = (byte)Blocks.Stone; old[Chunk.Index(4, 70, 5)] = (byte)Blocks.Chest; old[Chunk.Index(0, 1, 0)] = 254;
                    var ms1 = new MemoryStream();
                    using (var z = new System.IO.Compression.DeflateStream(ms1, System.IO.Compression.CompressionLevel.Fastest, true)) z.Write(old, 0, old.Length);
                    var c1 = new Chunk(0, 0); c1.Read(new MemoryStream(ms1.ToArray()));
                    Check(c1.Get(3, 70, 5) == Blocks.Stone && c1.Get(4, 70, 5) == Blocks.Chest && c1.Get(0, 1, 0) == 254 && c1.Get(9, 9, 9) == 0 && c1.MaxY == 70, "a chunk saved before 0.11 (one byte a block) reads back block for block");
                    c1.Set(7, 80, 7, 3000);                                  // a block number that needs two bytes
                    var ms2 = new MemoryStream(); c1.Write(ms2);
                    var c2 = new Chunk(0, 0); c2.Read(new MemoryStream(ms2.ToArray()));
                    Check(c2.Get(7, 80, 7) == 3000 && c2.Get(3, 70, 5) == Blocks.Stone && c2.Blocks.AsSpan().SequenceEqual(c1.Blocks), "and saved again it keeps block numbers past 255");
                    var c3 = new Chunk(0, 0); Net.Decompress(Net.Compress(c1.Blocks), c3.Blocks);
                    Check(c3.Blocks.AsSpan().SequenceEqual(c1.Blocks), "the same goes for a chunk sent over the network");
                }

                // A chute: chest, chute, furnace, chute, chest in a column smelts a chestful by itself.
                {
                    var col = new Dictionary<(int, int, int), ushort> { [(0, 14, 0)] = Blocks.Chest, [(0, 13, 0)] = Blocks.Chute, [(0, 12, 0)] = Blocks.Furnace, [(0, 11, 0)] = Blocks.Chute, [(0, 10, 0)] = Blocks.Chest };
                    ushort Col(int x, int y, int z) => col.TryGetValue((x, y, z), out var id) ? id : (ushort)0;
                    var cb2 = new Containers(); var fur = new Furnaces(cb2);
                    var ch = new Chutes(cb2, Col, (x, z) => true) { Fed = fur.Wake };
                    ch.Add(0, 13, 0); ch.Add(0, 11, 0);
                    cb2.Set(0, 14, 0, 0, new Stack("iron_ore", 6)); cb2.Set(0, 14, 0, 1, new Stack("coal", 2)); cb2.Set(0, 14, 0, 2, new Stack("dirt", 4));
                    for (int i = 0; i < 1200; i++) { fur.Tick(0.1f); ch.Tick(0.1f); }
                    var top = cb2.At(0, 14, 0); var bottom = cb2.At(0, 10, 0); var f0 = cb2.At(0, 12, 0);
                    int ingots = 0; foreach (var s2 in bottom) if (s2.Item == "iron_ingot") ingots += s2.Count;
                    Check(ingots == 6 && f0[Smelting.In].Empty && f0[Smelting.Out].Empty, $"chest, chute, furnace, chute, chest: six ore go in at the top and six ingots arrive at the bottom ({ingots})");
                    int dirt = 0; foreach (var s2 in cb2.At(0, 13, 0)) if (s2.Item == "dirt") dirt += s2.Count; foreach (var s2 in top) if (s2.Item == "dirt") dirt += s2.Count;
                    Check(dirt == 4 && f0[Smelting.Fuel].Item == "coal", $"dirt can't go into a furnace, so it waits above and none is lost ({dirt} of 4); the coal went in as fuel");
                    string file2 = Path.Combine(dir, "chute-test.bin"); Directory.CreateDirectory(dir); cb2.Save(file2);
                    var cb3 = new Containers(); cb3.Load(file2); var ch3 = new Chutes(cb3, Col, (x, z) => true); ch3.LoadSaved();
                    Check(ch3.Count == 2, "chutes are found again after a save and reload");
                    col.Remove((0, 11, 0)); for (int i = 0; i < 10; i++) ch.Tick(0.4f);
                    Check(ch.Count == 1, "a chute that was broken stops being worked");
                    File.Delete(file2);
                    var one = Items.Recipes.Find(r => r.Out == "chute");
                    Check(one != null && one.Shape != null && Items.Get("chute") != null, "a chute is made from five iron ingots round a chest");
                }

                // The first night, from empty hands: every step uses the game's own rules (what a block gives to the
                // tool in hand, what the crafting grid makes, what a furnace does with fuel).
                {
                    var inv = new Inventory(); string where = "";
                    bool Mine(ushort block, int times)
                    {
                        var def = Blocks.Get(block);
                        for (int i = 0; i < times; i++)
                        {
                            if (!Mining.Drops(def, Items.Get(inv.Held.Item))) { where = $"can't mine {def.Name} with {inv.Held.Item ?? "bare hands"}"; return false; }
                            var (item, n) = Mining.Drop(def);
                            if (item != null && inv.Add(item, n) > 0) { where = "pack full"; return false; }
                        }
                        return true;
                    }
                    bool Craft1(string what, bool bench, int times = 1)
                    {
                        var rc = Items.Recipes.Find(r => r.Out == what && r.Station != "furnace");
                        if (rc == null) { where = "no recipe for " + what; return false; }
                        for (int t = 0; t < times; t++)
                        {
                            if (Crafting.Size(rc) > (bench ? 3 : 2)) { where = what + " needs a workbench"; return false; }
                            var cells = Crafting.Layout(rc); var grid = new Stack[9];
                            for (int i = 0; i < 9; i++)
                            {
                                if (cells[i] == null) continue;
                                int k = Array.FindIndex(inv.Slots, s => !s.Empty && Items.Matches(cells[i], s.Item));
                                if (k < 0) { where = $"{what}: no {cells[i]}"; return false; }
                                grid[i] = new Stack(inv.Slots[k].Item, 1); inv.Slots[k].Count--; if (inv.Slots[k].Count <= 0) inv.Slots[k] = Stack.None;
                            }
                            var made = Crafting.Match(grid, bench ? 3 : 2);
                            if (made == null || made.Out != what) { where = $"{what}: the grid made {made?.Out ?? "nothing"}"; return false; }
                            if (inv.Add(made.Out, made.Count) > 0) { where = "pack full"; return false; }
                        }
                        return true;
                    }
                    bool Smelt(string input, int n, string fuel, int fuelN, string expect)
                    {
                        if (!inv.Take(input, n) || !inv.Take(fuel, fuelN)) { where = $"smelting {input}: not enough {input} or {fuel}"; return false; }
                        var f = new Stack[27]; f[Smelting.In] = new Stack(input, n); f[Smelting.Fuel] = new Stack(fuel, fuelN);
                        int guard = 0; while (Smelting.Busy(f) && guard++ < 200000) Smelting.Step(f);
                        if (f[Smelting.Out].Item != expect || f[Smelting.Out].Count != n) { where = $"smelting {input} gave {f[Smelting.Out].Count} {f[Smelting.Out].Item}"; return false; }
                        inv.Add(expect, n); if (!f[Smelting.Fuel].Empty) inv.Add(f[Smelting.Fuel].Item, f[Smelting.Fuel].Count);
                        return true;
                    }
                    void Hold(string item) { int k = Array.FindIndex(inv.Slots, s => !s.Empty && s.Item == item); if (k >= 0) { (inv.Slots[0], inv.Slots[k]) = (inv.Slots[k], inv.Slots[0]); inv.Selected = 0; } }
                    bool ok1 = Mine(Blocks.OakLog, 10)                                 // punch a tree
                        && Craft1("oak_planks", false, 8) && Craft1("stick", false, 2) && Craft1("workbench", false)
                        && Craft1("wood_pickaxe", true) && Craft1("wood_axe", true);
                    Hold("wood_pickaxe");
                    bool ok2 = ok1 && Mine(Blocks.Stone, 20)                           // cobblestone, with the pickaxe
                        && Craft1("stone_pickaxe", true) && Craft1("furnace", true)
                        && Smelt("oak_log", 1, "oak_planks", 1, "coal")                // charcoal from the last log
                        && Craft1("torch", false);
                    Hold("stone_pickaxe");
                    // Stratum's ladder has a rung Minecraft's doesn't: stone mines copper, and copper mines iron.
                    bool ok3 = ok2 && Mine(Blocks.CopperOre, 3) && Smelt("copper_ore", 3, "oak_planks", 3, "copper_ingot") && Craft1("stick", false) && Craft1("copper_pickaxe", true);
                    Hold("copper_pickaxe");
                    ok3 = ok3 && Mine(Blocks.IronOre, 3) && Smelt("iron_ore", 3, "oak_planks", 3, "iron_ingot") && Craft1("iron_pickaxe", true);
                    Check(ok1, "first night: logs by hand make planks, sticks, a workbench, a wooden pickaxe and axe" + (ok1 ? "" : " (" + where + ")"));
                    Check(ok2 && inv.Count("torch") == 4 && inv.Count("furnace") == 1, "then cobblestone, a stone pickaxe, a furnace, charcoal and torches" + (ok2 ? "" : " (" + where + ")"));
                    Check(ok3 && inv.Count("iron_pickaxe") == 1, "and on to metal: copper ore by stone, iron ore by copper, each smelted with planks for fuel, makes an iron pickaxe" + (ok3 ? "" : " (" + where + ")"));
                    Check(!Mining.Drops(Blocks.Get(Blocks.Stone), null) && !Mining.Drops(Blocks.Get(Blocks.IronOre), Items.Get("wood_pickaxe")), "stone needs a pickaxe and iron ore a better one than wood");
                }

                // Light for monsters is the light players see: a torch behind a wall doesn't count on the dark side.
                {
                    var room = new Dictionary<(int, int, int), ushort>();
                    for (int yy = 9; yy <= 14; yy++) for (int zz = -8; zz <= 8; zz++) for (int xx = -8; xx <= 8; xx++)
                        if (yy == 9 || yy == 14 || xx == 0 || Math.Abs(xx) == 8 || Math.Abs(zz) == 8) room[(xx, yy, zz)] = Blocks.Stone;      // a sealed hall with a wall down the middle
                    room[(-2, 10, 0)] = Blocks.Torch;
                    var sim = new MobSim((xx, yy, zz) => room.TryGetValue((xx, yy, zz), out var id) ? id : (ushort)0, 1);
                    int near = sim.BlockLight(-4, 10, 0), far = sim.BlockLight(2, 10, 0);
                    Check(near > 8 && far == 0, $"a torch lights its own side of a wall and not the other (lit side {near}, dark side {far})");
                    room.Remove((0, 10, 0)); room.Remove((0, 11, 0));                                 // a doorway
                    int through = sim.BlockLight(2, 10, 0);
                    Check(through > 3 && through < near, $"and through a doorway the light carries on ({through})");
                }

                {
                    string missing = "";
                    foreach (var rc in Items.Recipes)
                    {
                        if (Items.Get(rc.Out) == null) missing += " " + rc.Out;
                        foreach (var (item, _) in rc.In) if (item[0] != '@' && Items.Get(item) == null) missing += " " + item;
                    }
                    Check(missing == "", "every recipe makes and uses things that exist" + (missing != "" ? ":" + missing : ""));
                    int unmade = 0; string which = "";
                    foreach (var id in Blocks.Palette)
                        if (id >= Blocks.ByKey["orange_wool"] && id != Blocks.StarOre && id != Blocks.Stillstone && id != Blocks.Vine && !Items.Recipes.Exists(r => r.Out == Blocks.Keys[id])) { unmade++; which += " " + Blocks.Keys[id]; }
                    Check(unmade == 0, "every block added since the 0.11 building set can be crafted (ores are dug)" + which);
                }
                // Crafting: every recipe, laid out the way the recipe list lays it out, makes that thing and no other.
                {
                    int shaped = 0, wrong = 0; string firstWrong = "";
                    foreach (var rc in Items.Recipes)
                    {
                        if (rc.Station == "furnace" || rc.Station == "brewer") continue;
                        if (rc.Shape != null) shaped++;
                        var cells = Crafting.Layout(rc); var grid = new Stack[9];
                        for (int i = 0; i < 9; i++) if (cells[i] != null) grid[i] = new Stack(cells[i] == "@planks" ? "oak_planks" : cells[i] == "@log" ? "oak_log" : cells[i], 1);
                        var made = Crafting.Match(grid, rc.Station == "workbench" ? 3 : Crafting.Size(rc));
                        if (made == null || made.Out != rc.Out) { wrong++; if (firstWrong == "") firstWrong = $"{rc.Out} gave {made?.Out ?? "nothing"}"; }
                    }
                    Check(wrong == 0 && shaped > 60, $"every recipe laid out in the grid makes its own thing ({shaped} with a pattern, {wrong} wrong{(firstWrong != "" ? ": " + firstWrong : "")})");
                    Stack[] G(params (int cell, string item)[] put) { var g = new Stack[9]; foreach (var (cell, item) in put) g[cell] = new Stack(item, 1); return g; }
                    string M(Stack[] g, int n = 3) => Crafting.Match(g, n)?.Out ?? "nothing";
                    Check(M(G((0, "oak_planks"), (1, "oak_planks"), (2, "oak_planks"), (4, "stick"), (7, "stick"))) == "wood_pickaxe"
                          && M(G((0, "oak_planks"), (1, "oak_planks"), (3, "oak_planks"), (4, "stick"), (7, "stick"))) == "wood_axe"
                          && M(G((1, "oak_planks"), (2, "oak_planks"), (5, "oak_planks"), (4, "stick"), (7, "stick"))) == "wood_axe", "a pickaxe and an axe are told apart by their shape (the axe either way round)");
                    Check(M(G((0, "oak_planks"), (3, "oak_planks"), (4, "oak_planks"), (6, "oak_planks"), (7, "oak_planks"), (8, "oak_planks"))) == "oak_stairs"
                          && M(G((0, "oak_planks"), (1, "oak_planks"), (3, "oak_planks"), (4, "oak_planks"), (6, "oak_planks"), (7, "oak_planks"))) == "oak_door"
                          && M(G((0, "oak_planks"), (1, "oak_planks"), (2, "oak_planks"), (3, "oak_planks"), (4, "oak_planks"), (5, "oak_planks"))) == "trapdoor", "six planks make stairs, a door or a trapdoor by how they are laid");
                    Check(M(G((3, "cobble"), (4, "cobble"), (5, "cobble"), (6, "cobble"), (7, "cobble"), (8, "cobble"))) == "cobble_wall"
                          && M(G((0, "cobble"), (3, "cobble"), (4, "cobble"), (6, "cobble"), (7, "cobble"), (8, "cobble"))) == "cobble_stairs", "six cobblestone make a wall or stairs");
                    Check(M(G((4, "oak_planks"), (7, "oak_planks")), 2) == "nothing" && M(G((1, "oak_planks"), (4, "oak_planks")), 2) == "stick" && M(G((0, "stick"), (1, "coal")), 2) == "nothing" && M(G((1, "coal"), (4, "stick")), 2) == "torch",
                          "patterns can sit anywhere in the grid, but not outside the part in use");
                    Check(M(G((0, "white_wool"), (4, "red_flower")), 2) == "red_wool" && M(G((3, "red_flower"), (1, "white_wool")), 2) == "red_wool", "things without a pattern are made from any squares");
                }

                // The player's side of it: what they must hand back or be given so nothing is made or lost.
                var owed = new List<(string item, int n, int wear)>();
                var left = Containers.Reconcile(Stack.None, new Stack("iron_ore", 2), Stack.None, owed);
                Check(left.Empty && owed.Count == 1 && owed[0].item == "iron_ore" && owed[0].n == -2, "taking two ore the furnace had already used: both are taken back");
                owed.Clear(); left = Containers.Reconcile(new Stack("iron_ore", 1), new Stack("iron_ore", 2), Stack.None, owed);
                Check(left.Empty && owed.Count == 1 && owed[0].n == -1, "taking two when one was left: one is taken back");
                owed.Clear(); left = Containers.Reconcile(new Stack("coal", 60), new Stack("coal", 50), new Stack("coal", 60), owed);
                Check(left.Count == 64 && owed.Count == 1 && owed[0].n == 6, "adding ten where only four fit: six are given back");
                owed.Clear(); left = Containers.Reconcile(new Stack("iron_ingot", 2), new Stack("iron_ingot", 1), Stack.None, owed);
                Check(left.Count == 1 && owed.Count == 0, "taking the one ingot you saw: nothing is owed either way");
                owed.Clear(); left = Containers.Reconcile(new Stack("iron_ore", 1), new Stack("iron_ore", 3), new Stack("gold_ore", 2), owed);
                Check(left.Item == "gold_ore" && owed.Count == 1 && owed[0].item == "iron_ore" && owed[0].n == -2, "swapping three ore for gold when one was left: two ore are taken back");

                // Satchels: a full one is never emptied to make room. The newcomers go beside it, or back to the player.
                {
                    var land = new Dictionary<(int, int, int), ushort>();
                    ushort Land(int x, int y, int z) => land.TryGetValue((x, y, z), out var id) ? id : (y == 11 && z == 0 && (x == 0 || x == 1) ? (ushort)0 : Blocks.Stone);   // two free cells in solid rock
                    var sb = new Containers();
                    var full = new Stack[Containers.MaxSlots]; for (int i = 0; i < full.Length; i++) full[i] = new Stack("cobble", 64);
                    sb.Put(0, 11, 0, full); land[(0, 11, 0)] = Blocks.Satchel;
                    var over = sb.Stash(Land, (x, y, z) => land[(x, y, z)] = Blocks.Satchel, 0, 11, 0, new[] { new Stack("torch", 1) });
                    int cob = 0; foreach (var s in sb.At(0, 11, 0)) if (s.Item == "cobble") cob += s.Count;
                    Check(over.Count == 0 && cob == Containers.MaxSlots * 64 && land.ContainsKey((1, 11, 0)) && sb.At(1, 11, 0)[0].Item == "torch",
                          $"a torch dropped on a full satchel goes into a new one beside it ({cob / 64} stacks still in the first)");
                    var lots = new Stack[250]; for (int i = 0; i < lots.Length; i++) lots[i] = new Stack("slate", 64);
                    over = sb.Stash(Land, (x, y, z) => land[(x, y, z)] = Blocks.Satchel, 0, 11, 0, lots);
                    int inSecond = 0; foreach (var s in sb.At(1, 11, 0)) if (!s.Empty) inSecond++;
                    Check(inSecond == Containers.MaxSlots && over.Count == 250 - (Containers.MaxSlots - 1) && sb.At(0, 11, 0).Length == Containers.MaxSlots,
                          $"with nowhere left, what doesn't fit is handed back, not dropped ({inSecond} in the second satchel, {over.Count} returned)");
                    var merged = sb.Stash(Land, (x, y, z) => { }, 0, 11, 0, new Stack[0]);
                    var two = Containers.Compact(new[] { new Stack("coal", 40), new Stack("coal", 40), new Stack("iron_pickaxe", 1) { Wear = 5 }, new Stack("iron_pickaxe", 1) { Wear = 5 } });
                    Check(two.Count == 4 && two[0].Count == 64 && two[1].Count == 16 && two[2].Wear == 5, "stacks of the same thing join up; worn tools stay separate");
                }

                // A list of stacks too long for its one-byte count is cut, not garbled.
                var many = new Stack[300]; for (int i = 0; i < many.Length; i++) many[i] = new Stack("cobble", 1 + i % 60);
                var rd = new Reader(new Packet(Net.Msg.Box).Stacks(many).I32(777).Bytes().AsSpan(4).ToArray());      // without the length in front
                var got = rd.Stacks();
                Check(got.Length == 255 && got[254].Count == many[254].Count && rd.I32() == 777 && !rd.More, $"300 stacks travel as 255 and the reader keeps its place ({got.Length})");
            }

            // Saving: a chunk that can't be written is tried again, and a damaged one comes back from a backup.
            {
                string wdir = Path.Combine(dir, "save-test"); Directory.CreateDirectory(wdir);
                Chunk Load(ServerWorld w, int cx, int cz)
                {
                    w.Want(cx, cz); var made = new List<Chunk>();
                    for (int i = 0; i < 400 && !w.TryGet(cx, cz, out _); i++) { Thread.Sleep(10); w.Collect(made); }
                    w.TryGet(cx, cz, out var c); return c;
                }
                var w1 = new ServerWorld(wdir, 5);
                Load(w1, 0, 0);
                int ty = Chunk.Height - 2; while (ty > 1 && w1.Get(3, ty, 3) == 0) ty--;
                w1.SetRaw(3, ty + 1, 3, Blocks.Stone);
                string cdir = Path.Combine(wdir, "chunks"), parked = cdir + ".away";
                if (Directory.Exists(cdir)) Directory.Move(cdir, parked);
                File.WriteAllText(cdir, "in the way");                 // nowhere to write the chunk
                w1.Maintain((x, z) => false, true);
                Check(w1.Unsaved == 1 && w1.IsLoaded(3, 3), "a chunk that couldn't be saved stays in memory and on the list");
                File.Delete(cdir);
                if (Directory.Exists(parked)) Directory.Move(parked, cdir); else Directory.CreateDirectory(cdir);
                w1.Maintain((x, z) => false, true);
                string cfile = Path.Combine(cdir, "0_0.bin");
                Check(w1.Unsaved == 0 && File.Exists(cfile) && !w1.IsLoaded(3, 3), "and is saved, then let go, once it can be");
                w1.Stop();

                Backups.Take(wdir);
                File.WriteAllBytes(cfile, new byte[] { 1, 2, 3, 4, 5, 6, 7 });                  // the chunk file is damaged
                var w2 = new ServerWorld(wdir, 5);
                Load(w2, 0, 0);
                Check(w2.Get(3, ty + 1, 3) == Blocks.Stone, "a damaged chunk comes back from the newest backup, with what was built there");
                Check(File.Exists(cfile + ".bad") && new FileInfo(cfile).Length > 100, "the damaged file is kept aside and the good copy put back");
                w2.Stop();
                foreach (var b in Directory.GetDirectories(Path.Combine(wdir, "backups"))) Directory.Delete(b, true);
                File.WriteAllBytes(cfile, new byte[] { 9, 9, 9 });
                var w3 = new ServerWorld(wdir, 5);
                var c3 = Load(w3, 0, 0);
                Check(c3 != null && w3.Get(3, ty + 1, 3) == 0 && File.Exists(cfile + ".bad") && new FileInfo(cfile + ".bad").Length == 3, "with no backup the land is made fresh, and the damaged file is still kept");
                w3.Stop();
                // Damaged files: the copy kept beside each one is used, and nothing good is written over.
                {
                    string bdir = Path.Combine(dir, "bak-test"); Directory.CreateDirectory(bdir);
                    var wa = new ServerWorld(bdir, 4321); wa.Meta.day = 7; wa.SaveMeta(); wa.Meta.day = 8; wa.SaveMeta(); wa.Stop();       // two saves: world.json and world.json.bak
                    string wj = Path.Combine(bdir, "world.json");
                    File.WriteAllText(wj, "{ this is not json");
                    var wb = new ServerWorld(bdir, 99);
                    Check(wb.Meta.seed == 4321 && File.Exists(wj + ".bad"), $"a damaged world.json is read from its backup, keeping the seed ({wb.Meta.seed})");
                    wb.Stop();
                    File.WriteAllText(wj, "garbage"); File.WriteAllText(wj + ".bak", "more garbage");
                    bool refused = false; try { new ServerWorld(bdir, 99).Stop(); } catch (InvalidDataException) { refused = true; }
                    Check(refused && File.ReadAllText(wj) == "garbage", "with no readable copy the world is not started over on a new seed");
                    var rec = new PlayerRecord { name = "Zed", hasPos = true, x = 5 }; rec.Save(bdir, "zed"); rec.x = 9; rec.Save(bdir, "zed");
                    File.WriteAllText(PlayerRecord.FileFor(bdir, "zed"), "{{{{");
                    var back = PlayerRecord.Load(bdir, "zed");
                    Check(back != null && back.hasPos && back.x == 5 && File.Exists(PlayerRecord.FileFor(bdir, "zed") + ".bad"), "a damaged player file is read from its backup, not treated as a new player");
                    var cb = new Containers(); cb.Set(1, 2, 3, 0, new Stack("cobble", 11)); string cfile2 = Path.Combine(bdir, "containers.bin");
                    cb.Save(cfile2); cb.Set(1, 2, 3, 1, new Stack("torch", 3)); cb.Save(cfile2);
                    var good = File.ReadAllBytes(cfile2); var cut = new byte[good.Length - 9]; Array.Copy(good, cut, cut.Length); File.WriteAllBytes(cfile2, cut);     // cut short part-way through
                    var cc = new Containers(); cc.Load(cfile2);
                    Check(cc.At(1, 2, 3)[0].Count == 11 && cc.At(1, 2, 3)[1].Empty && cc.LoadedFrom.EndsWith(".bak"), "a damaged containers file is read from its backup, whole");
                    File.WriteAllBytes(cfile2 + ".bak", cut);
                    var cd = new Containers(); bool threw = false; try { cd.Load(cfile2); } catch (InvalidDataException) { threw = true; }
                    int leftIn = 0; foreach (var kv in cd.All) leftIn++;
                    Check(threw && leftIn == 0 && File.Exists(cfile2 + ".bad"), "with both copies damaged nothing half-read is left behind, and the file is kept aside");
                    try { Directory.Delete(bdir, true); } catch { }
                }
                Thread.Sleep(100);
                try { Directory.Delete(wdir, true); } catch { }
                if (Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length == 0) Directory.Delete(dir);
            }
            {
            }

            // On a server: the kind travels with the welcome, older games are kept out, and a world keeps its kind.
            int Seed(string t) => int.TryParse(t, out int n) ? n : t.GetHashCode();
            var cfg = new ServerConfig { Port = 0, Dir = dir, Seed = 99, MaxView = 4, LegacyLogin = true,
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
                    var fell = a.Wait(Net.Msg.Block, r => { int qx = r.I32(), qy = r.I32(), qz = r.I32(); return r.U16() == Blocks.Satchel && Math.Abs(qx - a.SX) <= 2 && Math.Abs(qz - a.SZ) <= 2 && Math.Abs(qy - a.SY) <= 3; });
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
            var cfg = new ServerConfig { Port = 0, Dir = dir, Seed = 20261119, MaxView = 4, LegacyLogin = true,
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
                    bool got = a.Welcome();
                    Check(got && !a.Creative && a.Seed == 20261119, $"a client that can't choose goes to the first world (welcome {got}, creative {a.Creative}, seed {a.Seed}, said: {(got ? "" : a.Refused(200))})");
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
            for (int i = 0; i < ch.Blocks.Length; i++) { ushort id = ch.Blocks[i]; if (id == Blocks.Lava) lava++; else if (id == Blocks.Cinderstone) rock++; if (id == Blocks.Bedrock && i >> 8 >= Ember.Roof) roof++; }
            Check(ch.Ready && rock > 1000 && roof >= 256, "the Ember Deep is cinderstone under a bedrock roof");
            Check(Ember.ToOverworld(Ember.ToEmber(800, -400).x, Ember.ToEmber(800, -400).z) == (800f, -400f), "a step in the Deep is eight on the surface, both ways");

            var w = new Dictionary<(int, int, int), ushort>();
            ushort Get(int x, int y, int z) => w.TryGetValue((x, y, z), out var b) ? b : (ushort)0;
            void Set(int x, int y, int z, ushort b) { if (b == 0) w.Remove((x, y, z)); else w[(x, y, z)] = b; }
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
            var cfg = new ServerConfig { Port = 0, Dir = dir, Seed = 20261119, Name = "Test Server", MaxPlayers = 3, LegacyLogin = true };
            Log.Quiet = Environment.GetEnvironmentVariable("STRATUM_TEST_LOG") != "1";
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
                    a.Create("Ada", pwA, world: "");      // a current game: it says which world (the first) and what it can do
                    Check(a.Welcome() && a.Seed == 20261119 && !a.HasPos && a.Name == "Ada" && a.Token.Length == 64, "Ada makes an account, joins, gets the seed and a remember-me token");
                    tokenA = a.Token;
                    b.Outfit = 3; b.Look = new byte[22]; b.Look[0] = 1; b.Look[5] = 200; b.Look[20] = 4;
                    b.Create("Bea", "bea-password", world: "");
                    Check(b.Welcome(), "Bea makes an account and joins");
                    int wears = -1; byte[] madeLook = null;
                    Check(a.Wait(Net.Msg.Joined, r => { r.I32(); if (r.Str() != "Bea") return false; r.F32(); r.F32(); r.F32(); r.F32(); wears = r.More ? r.U8() : -1; madeLook = r.More ? r.Blob() : null; return true; }) != null, "Ada hears that Bea joined");
                    Check(wears == 3, $"and which outfit Bea is wearing ({wears})");
                    Check(madeLook != null && madeLook.Length == 22 && madeLook[5] == 200 && madeLook[20] == 4, "and the outfit Bea made piece by piece comes with it");

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
                    a.Send(new Packet(Net.Msg.SetBlock).I32(px).I32(py).I32(pz).U16(Blocks.Stone).U16(0));
                    var seen = b.Wait(Net.Msg.Block, r => r.I32() == px && r.I32() == py && r.I32() == pz && r.U16() == Blocks.Stone);
                    Check(seen != null, "Bea sees the stone Ada placed");

                    a.Send(new Packet(Net.Msg.SetBlock).I32(px).I32(py + 1).I32(pz).U16(Blocks.Stone).U16(Blocks.Dirt));
                    Check(a.Wait(Net.Msg.Block, r => r.I32() == px && r.I32() == py + 1 && r.I32() == pz && r.U16() == 0) != null,
                          "an edit that disagrees about what was there is put right");
                    a.Send(new Packet(Net.Msg.SetBlock).I32(a.SX + 30).I32(py).I32(pz).U16(Blocks.Stone).U16(0));
                    Check(a.Wait(Net.Msg.Block, r => r.I32() == a.SX + 30 && r.I32() == py) != null, "an edit out of reach is refused");

                    b.Send(new Packet(Net.Msg.Chat).Str("hello Ada"));
                    Check(a.Wait(Net.Msg.Said, r => r.Str() == "Bea" && r.Str() == "hello Ada") != null, "chat reaches the other player");

                    // Operators: Ada made the first account, so she is one; Bea isn't until Ada says so.
                    b.Send(new Packet(Net.Msg.Chat).Str("/give torch 4"));
                    Check(b.Wait(Net.Msg.Said, r => r.Str() == "" && r.Str().StartsWith("Only this server's operators"), 2000) != null, "Bea, who isn't an operator, can't /give");
                    a.Send(new Packet(Net.Msg.Chat).Str("/give torch 4"));
                    Check(a.Wait(Net.Msg.Give, r => r.Str() == "torch" && r.U16() == 4, 2000) != null, "Ada made the first account, so she can");
                    a.Send(new Packet(Net.Msg.Chat).Str("/op Bea"));
                    Check(b.Wait(Net.Msg.Said, r => r.Str() == "" && r.Str() == "Ada made Bea an operator.", 2000) != null, "Ada makes Bea an operator");
                    b.Send(new Packet(Net.Msg.Chat).Str("/give torch 2"));
                    Check(b.Wait(Net.Msg.Give, r => r.Str() == "torch" && r.U16() == 2, 2000) != null, "and now Bea can /give too");
                    a.Send(new Packet(Net.Msg.Chat).Str("/deop Bea"));
                    b.Wait(Net.Msg.Said, r => r.Str() == "" && r.Str().Contains("operator rights away"), 2000);
                    a.Send(new Packet(Net.Msg.Chat).Str("/deop Ada"));
                    Check(a.Wait(Net.Msg.Said, r => r.Str() == "" && r.Str() == "There has to be one operator left.", 2000) != null, "the last operator can't step down");
                    // A shield in the other hand is passed on with what is held; a splash potion's burst reaches those near.
                    a.Send(new Packet(Net.Msg.Move).F32(px + 0.5f).F32(py + 1).F32(pz + 0.5f).F32(0).F32(0).U8(0).Str("iron_sword~s"));
                    Check(b.Wait(Net.Msg.Moved, r => { r.I32(); r.F32(); r.F32(); r.F32(); r.F32(); r.F32(); r.U8(); return r.Str() == "iron_sword~s"; }, 3000) != null, "Bea is told Ada carries a shield");
                    a.Send(new Packet(Net.Msg.Splash).F32(px + 0.5f).F32(py + 1).F32(pz + 1.5f).Str("Swift").F32(60));
                    Check(b.Wait(Net.Msg.Splashed, r => { r.F32(); r.F32(); r.F32(); return r.Str() == "Swift" && Math.Abs(r.F32() - 60) < 0.01f; }, 3000) != null, "a splash potion Ada throws bursts for Bea too");
                    // Goals and statistics are kept with the account.
                    a.Send(new Packet(Net.Msg.Keep).F32(20).F32(20).F32(5).U8(0).Stacks(new Stack[Inventory.Size]).Bool(false).I32(0).I32(0).I32(0).I32(0).I32(0).Str("wood,bench|mined=12;"));
                    Thread.Sleep(200);
                    // Things on the ground, for a game that knows about them.
                    using (var d = new Bot(port) { Features = 31 })
                    {
                        d.Create("Dot", "dot-password", world: "");
                        Check(d.Welcome(), "Dot joins with a game that shows things on the ground");
                        // Her pack holds the 4 cobble she is about to throw down.
                        var carrying = new Stack[Inventory.Size]; carrying[0] = new Stack("cobble", 4);
                        d.Send(new Packet(Net.Msg.Keep).F32(20).F32(20).F32(5).U8(0).Stacks(carrying).Bool(false).I32(0).I32(0).I32(0).I32(0).I32(0).Str("").I32(0).MoreStacks(carrying));
                        d.Move(px + 0.5f, py + 1, pz + 2.5f);
                        Thread.Sleep(150);
                        d.Send(new Packet(Net.Msg.Toss).Str("gold_ingot").U16(1).U16(0).F32(px + 0.5f).F32(py + 3).F32(pz + 2.5f).F32(0).F32(0).F32(0).Bool(true));
                        d.Send(new Packet(Net.Msg.Toss).Str("cobble").U16(4).U16(0).F32(px + 0.5f).F32(py + 3).F32(pz + 2.5f).F32(0).F32(0).F32(0).Bool(false));
                        int dropId = 0; bool gold = false;
                        Check(d.Wait(Net.Msg.Drops, r =>
                        {
                            int n = r.U16(); bool any = false;
                            for (int i = 0; i < n; i++) { int id = r.I32(); string it = r.Str(); int c = r.U16(); float qx = r.F32(), qy = r.F32(), qz = r.F32(); if (it == "cobble" && c == 4) { dropId = id; any = true; } if (it == "gold_ingot") gold = true; }
                            return any;
                        }, 3000) != null && !gold, "what she throws down lies on the ground for everyone; something said to have fallen from a block she never broke does not appear");
                        d.Send(new Packet(Net.Msg.TakeDrop).I32(dropId));
                        Check(d.Wait(Net.Msg.Give, null, 500) == null, "it can't be picked up the instant it leaves the hand");
                        Thread.Sleep(1500);
                        float lx = 0, ly = 0, lz = 0;
                        // The newest word on where it lies (it was still falling in the first ones).
                        for (int k = 0; k < 40; k++)
                            d.Wait(Net.Msg.Drops, r => { int n = r.U16(); for (int i = 0; i < n; i++) { int id = r.I32(); r.Str(); r.U16(); float qx = r.F32(), qy = r.F32(), qz = r.F32(); if (id == dropId) { lx = qx; ly = qy; lz = qz; } } return true; }, 120);
                        d.Move(lx, ly, lz);
                        Thread.Sleep(150);
                        d.Send(new Packet(Net.Msg.TakeDrop).I32(dropId));
                        string gotIt = null; int gotN = 0;
                        d.Wait(Net.Msg.Give, r => { gotIt = r.Str(); gotN = r.U16(); return true; }, 2000);
                        Check(gotIt == "cobble" && gotN == 4, $"a moment later she picks it up (got {gotN} {gotIt}, it lay at {lx:0.0} {ly:0.0} {lz:0.0}, she stood by {px} {py} {pz})");
                        d.Send(new Packet(Net.Msg.TakeDrop).I32(dropId));
                        Check(d.Wait(Net.Msg.Give, null, 400) == null, "and only once");
                        // She is cut off before her game ever sends its pack: the cobble is still hers.
                    }
                    Thread.Sleep(300);
                    int Cobble(Bot who) { int n = 0; foreach (var st in who.Slots) if (st.Item == "cobble") n += st.Count; return n; }
                    (int id, int boats) Boats(Bot who)
                    {
                        int found = 0, count = -1;
                        for (int tries = 0; tries < 3; tries++)
                            who.Wait(Net.Msg.Mobs, r => { int n = r.U16(); count = 0; for (int i = 0; i < n; i++) { int id = r.I32(); var kind = (MobKind)r.U8(); r.F32(); r.F32(); r.F32(); r.U8(); r.U8(); r.U8(); r.U8(); r.U8(); if (kind == MobKind.Boat) { found = id; count++; } } return true; }, 1500);
                        return (found, count);
                    }
                    using (var d = new Bot(port) { Features = 63 })
                    {
                        d.Login("Dot", "dot-password", world: "");
                        Check(d.Welcome() && Cobble(d) == 4, $"cut off right after picking it up, Dot still has her 4 cobble when she returns (has {Cobble(d)})");
                        // Her game says the pack holds it (and that it has seen nothing handed over this visit).
                        var dotPack = new Stack[Inventory.Size]; dotPack[0] = new Stack { Item = "cobble", Count = 4 };
                        d.Send(new Packet(Net.Msg.Keep).F32(20).F32(20).F32(5).U8(0).Stacks(dotPack).Bool(false).I32(0).I32(0).I32(0).I32(0).I32(0).Str("").I32(0));
                        // Two people put stone in the same place: the second is told plainly that hers didn't happen.
                        d.Move(px + 0.5f, py + 1, pz + 2.5f);
                        Thread.Sleep(150);
                        a.Send(new Packet(Net.Msg.SetBlock).I32(px + 2).I32(py + 2).I32(pz).U16(Blocks.Stone).U16(0));
                        Check(d.Wait(Net.Msg.Block, r => r.I32() == px + 2 && r.I32() == py + 2 && r.I32() == pz && r.U16() == Blocks.Stone, 2000) != null, "Ada puts stone down");
                        d.Send(new Packet(Net.Msg.SetBlock).I32(px + 2).I32(py + 2).I32(pz).U16(Blocks.Stone).U16(0));
                        Check(d.Wait(Net.Msg.Block, r => r.I32() == px + 2 && r.I32() == py + 2 && r.I32() == pz && r.U16() == Blocks.Stone && r.I32() == WorldHost.Refused, 2000) != null,
                              "Dot, putting stone in the same place, is told it was refused (so her game hands the block back)");
                        a.Send(new Packet(Net.Msg.SetBlock).I32(px + 2).I32(py + 2).I32(pz).U16(0).U16(Blocks.Stone));
                        // A chest: all 4 cobble go in, 3 come back out, and she is cut off before her game sends its pack.
                        d.Send(new Packet(Net.Msg.SetBlock).I32(px + 2).I32(py + 2).I32(pz + 2).U16(Blocks.Chest).U16(0));
                        Thread.Sleep(200);
                        d.Send(new Packet(Net.Msg.OpenBox).I32(px + 2).I32(py + 2).I32(pz + 2));
                        bool opened = d.Wait(Net.Msg.Box, null, 2000) != null;
                        d.Send(new Packet(Net.Msg.BoxSlot).I32(px + 2).I32(py + 2).I32(pz + 2).U8(0).Slot(new Stack("cobble", 4)).Slot(Stack.None));
                        d.Send(new Packet(Net.Msg.BoxSlot).I32(px + 2).I32(py + 2).I32(pz + 2).U8(0).Slot(new Stack("cobble", 1)).Slot(new Stack("cobble", 4)));
                        Check(opened, "Dot puts her cobble in a chest and takes 3 back");
                        // A boat she has only just got into is left behind when she is cut off.
                        var before = Boats(d);
                        d.Send(new Packet(Net.Msg.Park).U8((byte)MobKind.Boat).F32(px + 0.5f).F32(py + 1).F32(pz + 3.5f).F32(0));
                        Thread.Sleep(400);
                        var with = Boats(d);
                        d.Send(new Packet(Net.Msg.Mount).I32(with.id));
                        bool on = d.Wait(Net.Msg.Mounted, null, 2000) != null;
                        Check(with.boats == before.boats + 1 && on, $"Dot puts a boat down and gets in ({before.boats} boats, then {with.boats})");
                    }
                    Thread.Sleep(300);
                    using (var d = new Bot(port) { Features = 63 })
                    {
                        d.Login("Dot", "dot-password", world: "");
                        bool dotBack = d.Welcome();
                        Check(dotBack && Cobble(d) == 3, $"cut off with the chest open, she has the 3 she took and not the 1 she left (has {Cobble(d)})");
                        d.Send(new Packet(Net.Msg.OpenBox).I32(px + 2).I32(py + 2).I32(pz + 2));
                        int inChest = -1;
                        d.Wait(Net.Msg.Box, r => { r.I32(); r.I32(); r.I32(); inChest = 0; foreach (var st in r.Stacks()) if (st.Item == "cobble") inChest += st.Count; return true; }, 2000);
                        Check(inChest == 1, $"and the chest holds the 1 ({inChest})");
                        var after = Boats(d);
                        Check(after.boats >= 1, $"cut off the moment she got in, her boat is still there ({after.boats})");
                        // In, riding, out again, and cut off before her next word on moving: one boat, not two.
                        d.Move(px + 0.5f, py + 1, pz + 2.5f);
                        Thread.Sleep(150);
                        d.Send(new Packet(Net.Msg.Mount).I32(after.id));
                        d.Wait(Net.Msg.Mounted, null, 2000);
                        d.Send(new Packet(Net.Msg.Move).F32(px + 0.5f).F32(py + 1).F32(pz + 3.5f).F32(0).F32(0).U8(Net.InBoat).Str(""));
                        Thread.Sleep(150);
                        d.Send(new Packet(Net.Msg.Park).U8((byte)MobKind.Boat).F32(px + 0.5f).F32(py + 1).F32(pz + 3.5f).F32(0));
                        // She falls: everything she carries goes into a satchel, and she is cut off before her game says its pack is empty.
                        var falls = new Stack[Inventory.Size]; falls[0] = new Stack("cobble", 3);
                        d.Send(new Packet(Net.Msg.Drop).F32(px + 0.5f).F32(py + 1).F32(pz + 2.5f).Stacks(falls).MoreStacks(falls));
                        Thread.Sleep(200);
                        // Waiting high over the far side of a portal, then cut off: she returns where she set out.
                        d.Move(px + 0.5f, py + 1, pz + 2.5f);
                        Thread.Sleep(150);
                        d.Move(px + 9000.5f, 140, pz + 0.5f);
                        Thread.Sleep(150);
                    }
                    Thread.Sleep(300);
                    using (var d = new Bot(port) { Features = 63 })
                    {
                        // A pack with far more waiting for room than one list of 255 can carry: all of it is kept.
                        d.Login("Dot", "dot-password", world: "");
                        bool afterFall = d.Welcome();
                        Check(afterFall && Cobble(d) == 0, $"cut off as she fell, she doesn't come back still carrying what lies in her satchel (has {Cobble(d)})");
                        var boatsNow = Boats(d);
                        Check(boatsNow.boats == 1, $"and getting out of the boat just before left one boat, not two ({boatsNow.boats})");
                        var big = new Stack[Inventory.Size + 700];
                        for (int i = Inventory.Size; i < big.Length - 1; i++) big[i] = new Stack { Item = "cobble", Count = 1 + (i % 7) };
                        big[big.Length - 1] = new Stack { Item = "shield", Count = 1 };
                        d.Send(new Packet(Net.Msg.Keep).F32(20).F32(20).F32(5).U8(0).Stacks(big).Bool(false).I32(0).I32(0).I32(0).I32(0).I32(0).Str("").I32(0).MoreStacks(big));
                        Thread.Sleep(300);
                    }
                    Thread.Sleep(300);
                    using (var d = new Bot(port) { Features = 63 })
                    {
                        d.Login("Dot", "dot-password", world: "");
                        bool again = d.Welcome(); int kept = 0, right = 0;
                        for (int i = Inventory.Size; again && i < d.Slots.Length; i++) if (!d.Slots[i].Empty) { kept++; if (d.Slots[i].Item == "cobble" && d.Slots[i].Count == 1 + (i % 7)) right++; }
                        Check(again && d.Slots.Length == Inventory.Size + 700 && kept == 700 && right == 699 && d.Slots[d.Slots.Length - 1].Item == "shield",
                              $"700 stacks waiting beyond the pack all come back, in order, the shield last ({kept} kept, {right} as sent, {d.Slots.Length} slots)");
                        d.Send(new Packet(Net.Msg.Keep).F32(20).F32(20).F32(5).U8(0).Stacks(new Stack[Inventory.Size]).Bool(false).I32(0).I32(0).I32(0).I32(0).I32(0).Str("").I32(0).MoreStacks(new Stack[Inventory.Size]));
                        Thread.Sleep(300);
                    }
                    Thread.Sleep(300);
                    using (var d = new Bot(port) { Features = 63 })
                    {
                        // One of two asleep, and a third arrives: the sleeper's count follows.
                        a.Send(new Packet(Net.Msg.Sleep).Bool(true));
                        Check(a.Wait(Net.Msg.Sleepers, r => r.U8() == 1 && r.U8() == 2, 2000) != null, "Ada lies down: 1 of 2 asleep");
                        d.Login("Dot", "dot-password", world: "");
                        Check(d.Welcome() && Math.Abs(d.X - (px + 0.5f)) < 1 && Math.Abs(d.Y - (py + 1)) < 1, $"cut off mid-portal, Dot is back where she set out, not in the air (at {d.X:0.0} {d.Y:0.0} {d.Z:0.0})");
                        Check(a.Wait(Net.Msg.Sleepers, r => r.U8() == 1 && r.U8() == 3, 3000) != null, "Dot arrives: Ada's count becomes 1 of 3");
                        a.Send(new Packet(Net.Msg.Sleep).Bool(false));
                    }
                    Thread.Sleep(300);
                    // A chest: Ada fills a slot, Bea sees it, breaking it hands the things to Ada.
                    a.Send(new Packet(Net.Msg.SetBlock).I32(px).I32(py + 1).I32(pz).U16(Blocks.Chest).U16(0));
                    Check(b.Wait(Net.Msg.Block, r => r.I32() == px && r.I32() == py + 1 && r.I32() == pz && r.U16() == Blocks.Chest) != null, "Ada places a chest");
                    a.Send(new Packet(Net.Msg.OpenBox).I32(px).I32(py + 1).I32(pz));
                    var box = a.Wait(Net.Msg.Box);
                    Check(box != null && box.I32() == px && box.I32() == py + 1 && box.I32() == pz && box.Stacks().Length == Containers.ChestSlots, "she opens it: 27 empty slots");
                    // Only one of them in the chest at a time, so the same stack can't be taken twice.
                    b.Send(new Packet(Net.Msg.OpenBox).I32(px).I32(py + 1).I32(pz));
                    Check(b.Wait(Net.Msg.Said, r => r.Str() == "" && r.Str().StartsWith("Ada is using that")) != null, "Bea is told Ada is using the chest");
                    a.Send(new Packet(Net.Msg.BoxSlot).I32(px).I32(py + 1).I32(pz).U8(2).Slot(new Stack("cobble", 9)));
                    b.Send(new Packet(Net.Msg.BoxSlot).I32(px).I32(py + 1).I32(pz).U8(2).Slot(Stack.None));      // Bea can't empty it from outside
                    a.Send(new Packet(Net.Msg.CloseBox));
                    Thread.Sleep(200);          // two connections: let Ada's close land before Bea asks
                    b.Send(new Packet(Net.Msg.OpenBox).I32(px).I32(py + 1).I32(pz));
                    var bbox = b.Wait(Net.Msg.Box); Stack[] inBox = null;
                    if (bbox != null) { bbox.I32(); bbox.I32(); bbox.I32(); inBox = bbox.Stacks(); }
                    Check(inBox != null && inBox[2].Item == "cobble" && inBox[2].Count == 9, "once Ada closes it Bea opens it and finds the cobble");
                    // Ada can't break the chest out from under Bea while she has it open.
                    a.Send(new Packet(Net.Msg.SetBlock).I32(px).I32(py + 1).I32(pz).U16(0).U16(Blocks.Chest));
                    Check(a.Wait(Net.Msg.Block, r => r.I32() == px && r.I32() == py + 1 && r.I32() == pz && r.U16() == Blocks.Chest, 2000) != null
                          && a.Wait(Net.Msg.Give, null, 400) == null, "a chest someone has open can't be broken, and gives nothing");
                    // Bea takes the 9 she can see, but (say a furnace had been at work) there are really 12: three stay.
                    b.Send(new Packet(Net.Msg.BoxSlot).I32(px).I32(py + 1).I32(pz).U8(2).Slot(new Stack("cobble", 12)));
                    b.Send(new Packet(Net.Msg.BoxSlot).I32(px).I32(py + 1).I32(pz).U8(2).Slot(Stack.None).Slot(new Stack("cobble", 9)));
                    Stack put = default;
                    Check(b.Wait(Net.Msg.BoxSet, r => { r.I32(); r.I32(); r.I32(); if (r.U8() != 2) return false; put = r.Slot(); return true; }, 2000) != null && put.Item == "cobble" && put.Count == 3,
                          $"taking what you saw leaves what arrived since, and your screen is put right ({put.Count} left)");
                    b.Send(new Packet(Net.Msg.BoxSlot).I32(px).I32(py + 1).I32(pz).U8(2).Slot(new Stack("cobble", 9)).Slot(new Stack("cobble", 3)));
                    b.Send(new Packet(Net.Msg.CloseBox));
                    Check(b.Wait(Net.Msg.TakeBack, null, 300) == null && b.Wait(Net.Msg.Give, null, 300) == null, "and nothing is owed either way when what was there covers it");
                    Thread.Sleep(200);
                    a.Send(new Packet(Net.Msg.SetBlock).I32(px).I32(py + 1).I32(pz).U16(0).U16(Blocks.Chest));
                    var give = a.Wait(Net.Msg.Give);
                    Check(give != null && give.Str() == "cobble" && give.U16() == 9, "breaking the chest gives Ada what was in it");
                    Check(a.Wait(Net.Msg.Mobs, null, 3000) != null, "the server tells players about the creatures near them");
                    // Everyday things that change a block without breaking it: a door, a lever, tilled soil, a second slab.
                    bool Accepted(int bx, int by, int bz, ushort id, ushort old)
                    {
                        a.Send(new Packet(Net.Msg.SetBlock).I32(bx).I32(by).I32(bz).U16(id).U16(old));
                        return b.Wait(Net.Msg.Block, r => r.I32() == bx && r.I32() == by && r.I32() == bz && r.U16() == id, 2000) != null;
                    }
                    int dy = py + 1;
                    Check(Accepted(px, dy, pz, Blocks.OakDoor, 0) && Accepted(px, dy + 1, pz, (ushort)(Blocks.OakDoor + 1), 0), "Ada stands a door up (both halves)");
                    ushort lowOpen = Blocks.Flip(Blocks.OakDoor, 2), highOpen = Blocks.Flip((ushort)(Blocks.OakDoor + 1), 2);
                    Check(Accepted(px, dy, pz, lowOpen, Blocks.OakDoor) && Accepted(px, dy + 1, pz, highOpen, (ushort)(Blocks.OakDoor + 1)), "and opens it: both halves swing, and Bea sees it");
                    Check(Accepted(px, dy, pz, Blocks.OakDoor, lowOpen) && Accepted(px, dy + 1, pz, (ushort)(Blocks.OakDoor + 1), highOpen), "and shuts it again");
                    Accepted(px, dy + 1, pz, 0, (ushort)(Blocks.OakDoor + 1)); Accepted(px, dy, pz, 0, Blocks.OakDoor);
                    Check(Accepted(px, dy, pz, Blocks.Lever, 0) && Accepted(px, dy, pz, Blocks.Flip(Blocks.Lever, 1), Blocks.Lever), "a lever can be thrown");
                    Accepted(px, dy, pz, 0, Blocks.Flip(Blocks.Lever, 1));
                    ushort slab = Blocks.Slabs.First();
                    Check(Accepted(px, dy, pz, slab, 0) && Accepted(px, dy, pz, Blocks.Get(slab).Pair, slab), "a second slab on the first makes the whole block");
                    Accepted(px, dy, pz, 0, Blocks.Get(slab).Pair);
                    Check(Accepted(px, dy, pz, Blocks.Grass, 0) && Accepted(px, dy, pz, Blocks.Farmland, Blocks.Grass), "grass can be tilled");
                    Accepted(px, dy, pz, 0, Blocks.Farmland);
                    a.Send(new Packet(Net.Msg.SetBlock).I32(px).I32(py).I32(pz).U16(Blocks.Chest).U16(Blocks.Stone));
                    Check(a.Wait(Net.Msg.Block, r => r.I32() == px && r.I32() == py && r.I32() == pz && r.U16() == Blocks.Stone, 2000) != null, "but one block still can't be swapped for another kind");
                    a.Send(new Packet(Net.Msg.Drop).F32(a.X).F32(a.Y).F32(a.Z).Stacks(new[] { new Stack("torch", 4) }));
                    Check(b.Wait(Net.Msg.Block, r => { r.I32(); r.I32(); r.I32(); return r.U16() == Blocks.Satchel; }) != null, "what Ada drops lands in a satchel everyone sees");

                    // A furnace: ore in, coal under it, and it starts to work while she watches.
                    a.Send(new Packet(Net.Msg.SetBlock).I32(px).I32(py + 1).I32(pz).U16(Blocks.Furnace).U16(0));
                    b.Wait(Net.Msg.Block, r => r.I32() == px && r.I32() == py + 1 && r.I32() == pz && r.U16() == Blocks.Furnace);
                    a.Send(new Packet(Net.Msg.OpenBox).I32(px).I32(py + 1).I32(pz));
                    Check(a.Wait(Net.Msg.Box) != null, "Ada opens a furnace");
                    a.Send(new Packet(Net.Msg.BoxSlot).I32(px).I32(py + 1).I32(pz).U8(Smelting.In).Slot(new Stack("iron_ore", 2)).Slot(Stack.None));
                    a.Send(new Packet(Net.Msg.BoxSlot).I32(px).I32(py + 1).I32(pz).U8(Smelting.Fuel).Slot(new Stack("coal", 1)).Slot(Stack.None));
                    a.Send(new Packet(Net.Msg.BoxSlot).I32(px).I32(py + 1).I32(pz).U8(Smelting.Burn).Slot(new Stack("_burn", 9999)));      // she can't light it by hand
                    Stack fire = default;
                    Check(a.Wait(Net.Msg.BoxSet, r => { r.I32(); r.I32(); r.I32(); if (r.U8() != Smelting.Burn) return false; fire = r.Slot(); return true; }, 3000) != null
                          && fire.Count > 0 && fire.Count < Smelting.CookTicks * 8 && fire.Wear == Smelting.CookTicks * 8, $"the coal lights and burns down ({fire.Count} of {fire.Wear})");
                    Check(a.Wait(Net.Msg.BoxSet, r => { r.I32(); r.I32(); r.I32(); return r.U8() == Smelting.Cook && r.Slot().Count > 0; }, 3000) != null, "and the ore starts to smelt");
                    // She takes "five" ore out, having seen five, but only two were ever there: three come back out of her hands.
                    a.Send(new Packet(Net.Msg.BoxSlot).I32(px).I32(py + 1).I32(pz).U8(Smelting.In).Slot(Stack.None).Slot(new Stack("iron_ore", 5)));
                    var tb = a.Wait(Net.Msg.TakeBack, null, 2000); string tbItem = tb?.Str(); int tbN = tb?.U16() ?? 0;
                    Check(tbItem == "iron_ore" && tbN == 3, $"taking ore the furnace no longer has is put right: the extra is taken back ({tbN} {tbItem})");
                    a.Send(new Packet(Net.Msg.BoxSlot).I32(px).I32(py + 1).I32(pz).U8(Smelting.In).Slot(new Stack("iron_ore", 2)).Slot(Stack.None));
                    // A game from before 0.7.2 can't say what it saw, so it may not use a furnace at all.
                    a.Send(new Packet(Net.Msg.CloseBox)); Thread.Sleep(200);
                    using (var old = new Bot(port) { Features = GameServer.KnowsKinds | GameServer.KnowsTypes })
                    {
                        old.Create("Olda", "olda-password", world: "");
                        bool inOld = old.Welcome();
                        old.Move(a.X, a.Y, a.Z); Thread.Sleep(200);
                        old.Send(new Packet(Net.Msg.OpenBox).I32(px).I32(py + 1).I32(pz));
                        Check(inOld && old.Wait(Net.Msg.Said, r => r.Str() == "" && r.Str().StartsWith("Furnaces need the newest Stratum"), 2000) != null, "an older game is told furnaces need the update, and can't open one");
                        old.Send(new Packet(Net.Msg.BoxSlot).I32(px).I32(py + 1).I32(pz).U8(Smelting.In).Slot(Stack.None));
                    }
                    Thread.Sleep(200);
                    a.Send(new Packet(Net.Msg.OpenBox).I32(px).I32(py + 1).I32(pz));
                    var fb = a.Wait(Net.Msg.Box); Stack[] fin = null; if (fb != null) { fb.I32(); fb.I32(); fb.I32(); fin = fb.Stacks(); }
                    Check(fin != null && fin[Smelting.In].Item == "iron_ore", "and its attempt to empty the furnace did nothing");
                    a.Send(new Packet(Net.Msg.CloseBox));
                    a.Send(new Packet(Net.Msg.SetBlock).I32(px).I32(py + 1).I32(pz).U16(0).U16(Blocks.Furnace));
                    var back1 = a.Wait(Net.Msg.Give); string gotItem = back1?.Str();
                    var back2 = a.Wait(Net.Msg.Give, null, 600);
                    Check(gotItem == "iron_ore" && back2 == null, "breaking it gives back the ore, and nothing of the fire");

                    // A whole pack (40 slots and the XP) in one satchel: every stack can be reached.
                    var pack = new Stack[41]; for (int i = 0; i < pack.Length; i++) pack[i] = new Stack(i % 2 == 0 ? "cobble" : "torch", 64);
                    b.Move(b.X + 3, b.Y, b.Z); System.Threading.Thread.Sleep(150);
                    b.Send(new Packet(Net.Msg.Drop).F32(b.X + 3).F32(b.Y).F32(b.Z).Stacks(pack));
                    int tx = 0, ty = 0, tz = 0;
                    b.Wait(Net.Msg.Block, r => { int qx = r.I32(), qy = r.I32(), qz = r.I32(); if (r.U16() != Blocks.Satchel) return false; tx = qx; ty = qy; tz = qz; return true; });
                    b.Send(new Packet(Net.Msg.OpenBox).I32(tx).I32(ty).I32(tz));
                    var sbox = b.Wait(Net.Msg.Box); Stack[] held = null;
                    if (sbox != null) { sbox.I32(); sbox.I32(); sbox.I32(); held = sbox.Stacks(); }
                    Check(held != null && held.Length >= 41 && held[40].Count == 64, $"a satchel holds a full pack of 41 stacks ({held?.Length} slots)");
                    b.Send(new Packet(Net.Msg.BoxSlot).I32(tx).I32(ty).I32(tz).U8(40).Slot(Stack.None));
                    b.Send(new Packet(Net.Msg.CloseBox));
                    b.Send(new Packet(Net.Msg.OpenBox).I32(tx).I32(ty).I32(tz));
                    sbox = b.Wait(Net.Msg.Box); held = null;
                    if (sbox != null) { sbox.I32(); sbox.I32(); sbox.I32(); held = sbox.Stacks(); }
                    Check(held != null && held.Length > 40 && held[40].Empty && held[39].Count == 64, "and the last stack can be taken out of it");
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

                    // Her pack, and after it two things she was holding in the cursor and the crafting grid with no free slot.
                    var inv = new Stack[Inventory.Size + 2]; inv[0] = new Stack("cobble", 5); inv[3] = new Stack("torch", 12);
                    inv[Inventory.Size] = new Stack("iron_pickaxe", 1) { Wear = 30 }; inv[Inventory.Size + 1] = new Stack("coal", 4);
                    a.Send(new Packet(Net.Msg.Keep).F32(17).F32(12).F32(3).U8(3).Stacks(inv).Bool(true).I32(px).I32(py).I32(pz).I32(7).I32(3));

                    // Bea logs in again from another computer while still connected: she is where she stands now, with
                    // what she carries now, not what was last written to disk.
                    b.Move(b.X + 6.25f, b.Y, b.Z + 2.5f);
                    var binv = new Stack[Inventory.Size]; binv[1] = new Stack("torch", 21);
                    b.Send(new Packet(Net.Msg.Keep).F32(20).F32(20).F32(5).U8(0).Stacks(binv).Bool(false).I32(0).I32(0).I32(0).I32(0).I32(0));
                    Thread.Sleep(250);
                    float wantX = b.X + 6.25f, wantZ = b.Z + 2.5f;
                    using (var b2 = new Bot(port))
                    {
                        b2.Login("Bea", "bea-password");
                        bool okb = b2.Welcome();
                        Check(okb && b2.HasPos && Math.Abs(b2.X - wantX) < 0.01f && Math.Abs(b2.Z - wantZ) < 0.01f, $"logging in again while still connected puts her where she was standing ({b2.X:0.00} {b2.Z:0.00}, has place {b2.HasPos})");
                        Check(okb && b2.Slots[1].Item == "torch" && b2.Slots[1].Count == 21, "with what she was carrying");
                    }

                    using (var c = new Bot(port)) { c.Create("ada", "another-pass"); Check(c.Refused()?.Contains("already an account") == true, "nobody can make a second account called ada"); }
                    using (var c = new Bot(port)) { c.Login("Bea", "wrong-password"); Check(c.Refused() == "Wrong password.", "a wrong password is refused"); }
                    using (var c = new Bot(port)) { c.Login("Nobody", "whatever1"); Check(c.Refused()?.Contains("no account") == true, "logging in to an account that doesn't exist is refused"); }
                    using (var c = new Bot(port)) { c.Create("Cyd", "123"); Check(c.Refused()?.Contains("Passwords are") == true, "a too-short password is refused"); }
                    using (var c = new Bot(port)) { c.Hello(Accounts.Mode.Token, "Ada", new string('0', 64)); Check(c.Refused() != null, "a made-up token is refused"); }
                    // The 0.8 login: password and invite sealed to the server's key, the token hidden on the way back.
                    byte[] recorded = null, recMod = null, recExp = null;
                    using (var c = new Bot(port) { Sealed = true })
                    {
                        c.Create("Cleo", "cleo-password", world: "");
                        bool okc = c.Welcome();
                        Check(okc && c.Name == "Cleo" && c.Token.Length == 64 && Secure.IsSealed(c.TokenOnWire) && !c.TokenOnWire.Contains(c.Token), "a sealed login gets in, and its remember-me token travels hidden");
                        Check(c.SentSealed != null && System.Text.Encoding.UTF8.GetString(c.SentSealed).IndexOf("cleo-password", StringComparison.Ordinal) < 0, "the password isn't in what was sent");
                        recorded = c.SentSealed; recMod = c.KeyMod; recExp = c.KeyExp;
                        Check(Secure.Fingerprint(c.KeyMod) == srv.KeyFingerprint, "the key the game was given is the server's own");
                    }
                    Thread.Sleep(300);
                    using (var c = new Bot(port))
                    {
                        // Someone who recorded that login plays it back on a new connection.
                        c.Worlds();
                        c.Send(new Packet(Net.Msg.Hello).I32(Net.Protocol).U8((byte)Accounts.Mode.Login).Str("Cleo").Str("").Str("").Str("selftest").Str("").I32(c.Features).U8(0).Blob(recorded));
                        Check(c.Refused()?.Contains("couldn't be read safely") == true, "a recorded login played back is refused");
                    }
                    using (var c = new Bot(port) { Sealed = true }) { c.Login("Cleo", "wrong-password", world: ""); Check(c.Refused() == "Wrong password.", "a sealed login with the wrong password is refused like any other"); }
                    using (var c = new Bot(port) { Sealed = true }) { c.Login("Cleo", "cleo-password", world: ""); Check(c.Welcome() && c.Name == "Cleo", "and with the right one gets in"); }
                    using (var d = new Bot(port)) { d.Hello(Accounts.Mode.Login, "Old", "whatever1", "", Net.Protocol - 1); Check(d.Refused()?.Contains("newer") == true, "an older game is told to update"); }
                    Thread.Sleep(300);
                }
                Thread.Sleep(500);
                srv.Stop();
                Check(!File.ReadAllText(Path.Combine(dir, "accounts.json")).Contains(pwA), "accounts.json never holds a password");

                // A server as it runs by default: a game that sends its password in the clear is told to update.
                cfg.LegacyLogin = false;
                var strict = new GameServer(cfg); strict.Start();
                try
                {
                    using (var c = new Bot(strict.Port)) { c.Login("Ada", pwA, world: ""); Check(c.Refused()?.Contains("logins are encrypted now") == true, "by default a login sent in the clear is refused, with where to get the update"); }
                    using (var c = new Bot(strict.Port) { Sealed = true }) { c.Login("Ada", pwA, world: ""); Check(c.Welcome() && c.Name == "Ada", "and a sealed one gets in"); }
                    Thread.Sleep(300);
                }
                finally { strict.Stop(); }
                cfg.LegacyLogin = true;

                // Back again, now with an invite code: the world and Ada are as she left them.
                cfg.Password = "open sesame";
                var srv2 = new GameServer(cfg); srv2.Start();
                using (var a = new Bot(srv2.Port))
                {
                    a.Hello(Accounts.Mode.Token, "Ada", tokenA);
                    bool w = a.Welcome();
                    Check(w && a.Token == "", "after a restart Ada gets in with her remember-me token (no invite needed to log in)");
                    Check(w && a.HasPos && Math.Abs(a.X - ax) < 0.01f && Math.Abs(a.Z - az) < 0.01f, "and is where she stood");
                    Check(w && a.Slots.Length == Inventory.Size + 2 && a.Slots[0].Item == "cobble" && a.Slots[0].Count == 5 && a.Slots[3].Count == 12,
                          "and her backpack came back");
                    Check(w && a.Slots.Length == Inventory.Size + 2 && a.Slots[Inventory.Size].Item == "iron_pickaxe" && a.Slots[Inventory.Size].Wear == 30 && a.Slots[Inventory.Size + 1].Count == 4,
                          "with the things she was holding when her pack was full");
                    Check(w && Math.Abs(a.Food - 12) < 0.01f && a.HasBed && a.BX == px, "and so do her hunger and her bed");
                    Check(a.XpLevel == 7, "and her experience level");
                    a.Want(new[] { (px >> 4, pz >> 4) });
                    a.Wait(Net.Msg.Chunk, null, 15000);
                    Check(a.BlockAt(px, py, pz) == Blocks.Stone, "and the stone she placed is still there");
                    using (var b = new Bot(srv2.Port)) { b.Login("ADA", pwA); Check(b.Welcome() && b.Name == "Ada", "logging in on another computer (any capitals) works"); Check(b.Notes == "wood,bench|mined=12;", $"and her goals and statistics come with her ({b.Notes})"); }
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
                using (var a = new Bot(srv4.Port)) { a.Create("Kit", "kit-password", "open sesame"); Check(a.Welcome(), "Kit makes an account"); }
                using (var a = new Bot(srv4.Port)) { a.Create("\u212Ait", "other-password", "open sesame"); Check(a.Refused() != null, "a name spelled with a look-alike letter can't open a second account over Kit's save"); }
                using (var a = new Bot(srv4.Port)) { a.Login("\u212Ait", "other-password"); Check(a.Refused() != null, "nor log in as Kit without Kit's password"); }
                srv4.Stop();
                ReviewChecks();
                MechanicsChecks();
                EmberChecks();
                WorldChecks(Path.Combine(dir, "worlds-test"));
                KindChecks(Path.Combine(dir, "kinds-test"));
            }
            catch (Exception e) { Check(false, "threw " + e); }
            finally { try { Directory.Delete(dir, true); } catch { } }
            Console.WriteLine(_fails == 0 ? "ALL PASSED" : $"{_fails} FAILED");
            return _fails == 0 ? 0 : 1;
        }

        // A little world in a dictionary for trying rules out: stone up to y = 10, air above.
        sealed class Patch
        {
            public readonly Dictionary<(int, int, int), ushort> Cells = new Dictionary<(int, int, int), ushort>();
            public ushort Get(int x, int y, int z) => Cells.TryGetValue((x, y, z), out var b) ? b : y <= 10 ? Blocks.Stone : (ushort)0;
            public void Set(int x, int y, int z, ushort id) => Cells[(x, y, z)] = id;
        }

        // 0.14: things on the ground, loose blocks, leaves without a trunk.
        static void MechanicsChecks()
        {
            foreach (int seed in new[] { 4242, 1119, 7, 419773523, 90210 })
            {
                var g = new WorldGen(seed, WorldKind.Normal);
                var (sx, sy, sz) = g.FindSpawn();
                var near = new Dictionary<(int, int), Chunk>();
                ushort At(int x, int y, int z) { var k = (x >> 4, z >> 4); if (!near.TryGetValue(k, out var c)) { c = new Chunk(k.Item1, k.Item2); g.Generate(c); near[k] = c; } return c.Get(x & 15, y, z & 15); }
                bool open = Blocks.Get(At(sx, sy - 1, sz)).Solid;
                for (int y = sy; y < Chunk.Height && open; y++) if (Blocks.Get(At(sx, y, sz)).Solid) open = false;
                for (int oz = -1; oz <= 1 && open; oz++) for (int ox = -1; ox <= 1; ox++) if (!Blocks.Get(At(sx + ox, sy - 1, sz + oz)).Solid || Blocks.Get(At(sx + ox, sy, sz + oz)).Solid) open = false;
                Check(open, $"seed {seed}: the start ({sx} {sy} {sz}) is level ground under open sky");
            }
            {
                int last = 0, lastWear = 0; bool rising = true;
                foreach (var mat in new[] { "hide", "copper", "iron", "gold", "star" })
                {
                    int pts = 0, wear = Items.Get(mat + "_coat").Durability;
                    foreach (var piece in new[] { "cap", "coat", "leggings", "boots" }) pts += Items.Get(mat + "_" + piece).Armour;
                    if (pts <= last || wear <= lastWear) rising = false;
                    last = pts; lastWear = wear;
                }
                Check(rising && last == 20, "armour gets better and lasts longer set by set: hide, copper, iron, golden, starstone");
                float lastSpeed = 0, lastHit = 0; int lastUses = 0; bool better = true;
                foreach (var mat in new[] { "wood", "stone", "copper", "iron", "gold", "star" })
                {
                    var pick = Items.Get(mat + "_pickaxe"); var sword = Items.Get(mat + "_sword");
                    if (pick.Speed <= lastSpeed || pick.Durability <= lastUses || sword.Damage <= lastHit) better = false;
                    lastSpeed = pick.Speed; lastUses = pick.Durability; lastHit = sword.Damage;
                }
                Check(better && Mining.Drops(Blocks.Get(Blocks.StarOre), Items.Get("gold_pickaxe")), "tools get faster, last longer and hit harder level by level: wood, stone, copper, iron, golden, starstone");
                {
                    // A creature shut away far from anyone moves on by night, so it doesn't count against the open ground.
                    var far = new MobSim(new Patch().Get, 3) { Spawning = false, Time01 = 0.9f };
                    far.Spawn(MobKind.Ghoul, 60.5f, 11, 0.5f); var close = far.Spawn(MobKind.Ghoul, 12.5f, 11, 0.5f);
                    var one = new List<Seen> { new Seen { Id = 1, X = 0.5f, Y = 11, Z = 0.5f, Alive = true, Creative = true, Key = "p", Held = "" } };
                    for (int i = 0; i < 20 * 240; i++) far.Tick(0.05f, one);
                    int ghouls = 0; foreach (var m in far.All) if (m.Kind == MobKind.Ghoul && m.Dead <= 0) ghouls++;
                    Check(ghouls == 1, $"a ghoul far from anyone moves on within a few minutes of night; one near by stays ({ghouls} left)");
                }
            }
            {
                var ask = Items.Ask("Oak Planks 20", out int askN, out string askSay);
                Check(ask != null && ask.Id == "oak_planks" && askN == 20, "/give understands a thing's name and a count");
                Check(Items.Ask("torch", out askN, out askSay)?.Id == "torch" && askN == 1, "/give torch is the torch, though other things have torch in their names");
                Check(Items.Ask("planks 5", out askN, out askSay) == null && askSay.StartsWith("Which one? Oak Planks"), "/give planks asks which planks: " + askSay);
                Check(Items.Ask("zzz", out askN, out askSay) == null && askSay.Contains("nothing called"), "/give of nothing says so");
            }
            var w = new Patch();
            var drops = new DropSim(w.Get);
            drops.Toss("cobble", 3, 0, 0.5f, 14, 0.5f, 2, 1, 0);
            drops.Toss("cobble", 2, 0, 0.5f, 14, 0.5f, 2, 1, 0);
            for (int i = 0; i < 200; i++) drops.Tick(0.05f);
            Check(drops.All.Count == 1 && drops.All[0].Count == 5 && Math.Abs(drops.All[0].Y - 11) < 0.05f && drops.All[0].Rest,
                $"things thrown down land on the ground, lie still and pile together ({drops.All.Count} stacks, y {(drops.All.Count > 0 ? drops.All[0].Y : 0):0.00})");
            int id = drops.All[0].Id; float dx = drops.All[0].X;
            Check(drops.Take(id, dx + 30, 11, 0.5f) == null, "they can't be picked up from far away");
            var got = drops.Take(id, dx, 11, 0.5f);
            Check(got is (string it, int n, int _) && it == "cobble" && n == 5 && drops.All.Count == 0, "walking over them picks the whole stack up, once");
            Check(drops.Take(id, dx, 11, 0.5f) == null, "and a second taker gets nothing");
            drops.Toss("cobble", 1, 0, 0.5f, 12, 0.5f, 0, 0, 0);
            for (int i = 0; i < 20 * 301; i++) drops.Tick(0.05f);
            Check(drops.All.Count == 0, "what nobody picks up is gone after five minutes");

            // Sand with nothing under it falls, and what was on top follows.
            var water = new Water(w.Get, w.Set);
            w.Set(3, 20, 3, Blocks.Sand); w.Set(3, 21, 3, Blocks.Sand);
            water.Wake(3, 20, 3);
            for (int i = 0; i < 400 && water.Busy; i++) water.Tick(0.05f);
            Check(w.Get(3, 11, 3) == Blocks.Sand && w.Get(3, 12, 3) == Blocks.Sand && w.Get(3, 20, 3) == 0 && w.Get(3, 21, 3) == 0, "sand with nothing under it falls to the ground, and the sand on top of it follows");

            // A tree: cut the trunk and the leaves go; leaves by another trunk stay.
            var t = new Patch();
            for (int y = 11; y <= 14; y++) t.Set(0, y, 0, Blocks.OakLog);
            for (int x = -2; x <= 2; x++) for (int z = -2; z <= 2; z++) for (int y = 14; y <= 15; y++) if (t.Get(x, y, z) == 0) t.Set(x, y, z, Blocks.OakLeaves);
            for (int y = 11; y <= 14; y++) t.Set(20, y, 0, Blocks.OakLog);
            t.Set(21, 14, 0, Blocks.OakLeaves);
            var decay = new Decay(t.Get, t.Set); int fell = 0; decay.Fell = (x, y, z, d) => fell++;
            for (int y = 11; y <= 13; y++) { t.Set(0, y, 0, 0); decay.LogBroken(0, y, 0); }
            for (int i = 0; i < 200; i++) decay.Tick(0.05f);
            Check(fell == 0, "leaves stay while any of the trunk is left");
            t.Set(0, 14, 0, 0); decay.LogBroken(0, 14, 0);
            for (int i = 0; i < 200; i++) decay.Tick(0.05f);
            int left = 0; for (int x = -2; x <= 2; x++) for (int z = -2; z <= 2; z++) for (int y = 14; y <= 15; y++) if (t.Get(x, y, z) == Blocks.OakLeaves) left++;
            Check(left == 0 && fell == 49 && t.Get(21, 14, 0) == Blocks.OakLeaves, $"with the whole trunk cut the leaves fall away, and another tree's leaves stay ({left} left, {fell} fell)");
            // A ghoul finds its way round a wall it can't climb.
            {
                var pw = new Patch();
                for (int z = -4; z <= 4; z++) for (int y = 11; y <= 13; y++) pw.Set(4, y, z, Blocks.Stone);
                var sim = new MobSim(pw.Get, 5) { Spawning = false, Time01 = 0.9f };
                var ghoul = sim.Spawn(MobKind.Ghoul, 0.5f, 11, 0.5f);
                ghoul.Target = 1;                      // it has already seen them (a wall hides them now)
                var seen = new List<Seen> { new Seen { Id = 1, X = 8.5f, Y = 11, Z = 0.5f, Alive = true, Key = "kid" } };
                bool hit = false; sim.HitPlayer += (id, dmg, fx, fz, from) => hit = true;
                for (int i = 0; i < 20 * 25 && !hit; i++) sim.Tick(0.05f, seen);
                Check(hit, $"a ghoul walks round a wall to reach someone (it got to {ghoul.X:0.0}, {ghoul.Z:0.0})");

                // A hound takes a few bones to befriend, then keeps up with its owner, sits when told and stays.
                var hs = new MobSim(new Patch().Get, 9) { Spawning = false, Time01 = 0.3f };
                var hound = hs.Spawn(MobKind.Hound, 0.5f, 11, 0.5f);
                var owner = new List<Seen> { new Seen { Id = 1, X = 1.5f, Y = 11, Z = 0.5f, Alive = true, Key = "kid" } };
                int bones = 0; while (!hound.Tame && bones < 60) { hs.Use(hound.Id, "bone", 1.5f, 0.5f, "kid"); bones++; }
                Check(hound.Tame && hound.OwnerKey == "kid", $"a hound is befriended with bones ({bones})");
                owner[0] = new Seen { Id = 1, X = 15.5f, Y = 11, Z = 3.5f, Alive = true, Key = "kid" };
                for (int i = 0; i < 20 * 15; i++) hs.Tick(0.05f, owner);
                float hd = (float)Math.Sqrt((hound.X - 15.5f) * (hound.X - 15.5f) + (hound.Z - 3.5f) * (hound.Z - 3.5f));
                Check(hd < 5, $"it keeps up with its owner ({hd:0.0} away)");
                Check(!hs.Strike(hound.Id, 1, 5, 15.5f, 11, 3.5f) || hound.Health == hound.Def.Health, "its owner can't hurt it by mistake");
                hs.Use(hound.Id, "", 15.5f, 3.5f, "kid");
                owner[0] = new Seen { Id = 1, X = 1.5f, Y = 11, Z = 0.5f, Alive = true, Key = "kid" };
                float sx = hound.X, sz = hound.Z;
                for (int i = 0; i < 20 * 8; i++) hs.Tick(0.05f, owner);
                Check(hound.Sit && Math.Abs(hound.X - sx) < 1 && Math.Abs(hound.Z - sz) < 1, "told to sit, it stays where it is");
                // Kept across a save.
                var ms = new System.IO.MemoryStream();
                using (var bw2 = new System.IO.BinaryWriter(ms, System.Text.Encoding.UTF8, true)) hs.Write(bw2);
                var hs2 = new MobSim(new Patch().Get, 9) { Spawning = false };
                ms.Position = 0; using (var br2 = new System.IO.BinaryReader(ms)) hs2.Read(br2);
                Check(hs2.All.Count == 1 && hs2.All[0].Tame && hs2.All[0].Sit && hs2.All[0].OwnerKey == "kid", "a friend is still a friend, still sitting, after a save");

                // A fed camel can be ridden, and is left where its rider gets off.
                var camel = hs.Spawn(MobKind.Camel, 3.5f, 11, 0.5f);
                Check(hs.Mount(camel.Id, 1.5f, 11, 0.5f, out _, out _, out _, out _) == null, "a wild camel can't be ridden");
                hs.Use(camel.Id, "wheat", 1.5f, 0.5f, "kid");
                Check(hs.Mount(camel.Id, 1.5f, 11, 0.5f, out _, out _, out _, out _) == MobKind.Camel, "a fed camel can");
                var parked = hs.Park(MobKind.Camel, 9.5f, 11, 0.5f, 0, "kid");
                Check(parked != null && parked.Tame && parked.Kept, "and waits where its rider got off");
            }

            // A vault: found from the seed, it has its cage and two dusty chests, and a dusty chest has things in it.
            {
                int seed = 20261122; var gen = new WorldGen(seed, WorldKind.Normal);
                int vx = 0, vy = 0, vz = 0; bool any = false;
                for (int c = 0; c < 40 && !any; c++) any = Ruins.Vault(seed, c, 0, out vx, out vy, out vz);
                var vc = new Chunk(vx >> 4, vz >> 4); gen.Generate(vc);
                ushort mid = vc.Get(vx & 15, vy, vz & 15);
                int chests = 0;
                foreach (var (ox, oz) in new[] { (-3, 0), (3, 1) })
                {
                    var cc = new Chunk((vx + ox) >> 4, (vz + oz) >> 4); gen.Generate(cc);
                    if (cc.Get((vx + ox) & 15, vy, (vz + oz) & 15) == Blocks.LootChest) chests++;
                }
                Check(any && mid == Blocks.Cage && chests == 2, $"a vault has a monster cage in the middle and two dusty chests ({Blocks.Get(mid).Name}, {chests})");
                var loot = Ruins.Roll(seed, vx - 3, vy, vz); int stacks = 0; foreach (var st in loot) if (!st.Empty && Items.Get(st.Item) != null) stacks++;
                var again = Ruins.Roll(seed, vx - 3, vy, vz); bool same = true; for (int i = 0; i < loot.Length; i++) if (loot[i].Item != again[i].Item || loot[i].Count != again[i].Count) same = false;
                Check(stacks >= 3 && same, $"a dusty chest holds several things, the same whoever opens it first ({stacks} stacks)");
                // The cage lets monsters out while someone is near, four at most.
                var who = new List<Seen> { new Seen { Id = 1, X = vx + 6.5f, Y = vy, Z = vz + 0.5f, Alive = true, Creative = true, Key = "kid" } };
                ushort CageWorld(int x, int y, int z) => x == vx && y == vy && z == vz ? Blocks.Cage : y < vy || y > vy + 4 ? Blocks.Stone : (ushort)0;      // a cave with a roof
                var cs2 = new MobSim(CageWorld, 3) { Time01 = 0.3f, Seed = seed, HostileCap = 0, PassiveCap = 0 };      // nothing but what the cage lets out
                int most = 0;
                for (int i = 0; i < 20 * 90; i++) { cs2.Tick(0.05f, who); int now = 0; foreach (var m in cs2.All) if (m.Def.Hostile && m.Dead <= 0) now++; most = Math.Max(most, now); }
                int out2 = most;
                Check(out2 >= 2 && out2 <= 4, $"a vault's cage lets its monsters out while someone is near, four at most ({out2})");
            }

            // The Quiet: somewhere to land, the way home, and the King at his throne.
            {
                int seed = 77; var gen = new WorldGen(seed, WorldKind.Normal);
                var land = Quiet.Landing; var gate = Quiet.HomeGate; var throne = Quiet.Throne;
                ushort At(int x, int y, int z) { var c = new Chunk(x >> 4, z >> 4); gen.Generate(c); return c.Get(x & 15, y, z & 15); }
                int lx = (int)Math.Floor(land.x), ly = (int)land.y, lz = (int)Math.Floor(land.z);
                Check(Blocks.Get(At(lx, ly - 1, lz)).Solid && At(lx, ly, lz) == 0 && At(lx, ly + 1, lz) == 0, "the Quiet has solid ground to land on, with room to stand");
                Check(At(gate.x, gate.y, gate.z) == Blocks.GateStone, "and a gate stone there for the way home");
                Check(At(throne.x, throne.y - 1, throne.z) == Blocks.Obsidian && At(Quiet.Mid + 300, 20, 300) != Blocks.Stone, "a throne on the great island, and no ordinary ground anywhere");
                Check(Items.Recipes.Exists(r => r.Out == "gate_stone"), "a gate stone can be made");
                ushort Flat(int x, int y, int z) => y <= Quiet.Top ? Blocks.Stillstone : (ushort)0;
                var qs = new MobSim(Flat, 4) { Time01 = 0.5f, Seed = seed, PassiveCap = 0 };
                var who = new List<Seen> { new Seen { Id = 1, X = Quiet.Mid + 20.5f, Y = Quiet.Top + 1, Z = 0.5f, Alive = true, Key = "kid" } };
                for (int i = 0; i < 20 * 12; i++) qs.Tick(0.05f, who);
                Mob king = null; foreach (var m in qs.All) if (m.Kind == MobKind.King) king = m;
                Check(king != null, "the Hollow King is at his throne when someone comes near");
                bool hurt = false; qs.HitPlayer += (id, dmg, fx, fz, from) => hurt = true;
                for (int i = 0; i < 20 * 40 && !hurt; i++) qs.Tick(0.05f, who);
                int shades = 0; foreach (var m in qs.All) if (m.Kind == MobKind.Shade) shades++;
                Check(hurt && shades >= 1, $"he comes for them and calls Shades to his side ({shades})");

                // A Glob breaks into two smaller ones, and those into two smaller still.
                var gs = new MobSim(new Patch().Get, 6) { Spawning = false };
                var glob = gs.Spawn(MobKind.Glob, 0.5f, 11, 0.5f);
                gs.Strike(glob.Id, 1, 50, 1.5f, 11, 0.5f);
                int alive = 0; foreach (var m in gs.All) if (m.Kind == MobKind.Glob && m.Dead <= 0) alive++;
                Check(alive == 2 && gs.Loot(glob).Count == 0, $"a Glob breaks into two smaller ones and leaves nothing itself ({alive})");
            }

            // Spark machines.
            {
                var mw = new Patch(); Signal sig = null;
                sig = new Signal(mw.Get, (x, y, z, id) => { mw.Set(x, y, z, id); sig.Wake(x, y, z); });
                void Put(int x, int y, int z, ushort id) { mw.Set(x, y, z, id); sig.Wake(x, y, z); }
                void Run(float secs) { for (int i = 0; i < secs * 20; i++) sig.Tick(0.05f, null); }
                // A lever beside a piston that faces +X, a stone in front of it.
                Put(1, 11, 0, (ushort)(Blocks.Piston + 1 * 2)); Put(2, 11, 0, Blocks.Cobble); Put(0, 11, 0, Blocks.Lever);
                Run(0.5f);
                Check(mw.Get(2, 11, 0) == Blocks.Cobble, "a piston with no power leaves things be");
                Put(0, 11, 0, (ushort)(Blocks.Lever + 1)); Run(0.5f);
                Check(Blocks.Get(mw.Get(1, 11, 0)).Piston && (Blocks.Get(mw.Get(1, 11, 0)).Variant & 1) == 1 && Blocks.Get(mw.Get(2, 11, 0)).Arm && mw.Get(3, 11, 0) == Blocks.Cobble,
                    "with power it pushes the block in front along and puts its arm out");
                Put(0, 11, 0, Blocks.Lever); Run(0.5f);
                Check(mw.Get(2, 11, 0) == 0 && mw.Get(3, 11, 0) == Blocks.Cobble && mw.Get(1, 11, 0) == Blocks.Piston + 2, "without power the arm goes back and the block stays where it was pushed");
                // A sticky one brings it back.
                Put(1, 11, 0, (ushort)(Blocks.StickyPiston + 1 * 2)); Put(2, 11, 0, Blocks.Cobble); Put(3, 11, 0, 0);
                Put(0, 11, 0, (ushort)(Blocks.Lever + 1)); Run(0.5f);
                Put(0, 11, 0, Blocks.Lever); Run(0.5f);
                Check(mw.Get(2, 11, 0) == Blocks.Cobble && mw.Get(3, 11, 0) == 0, "a sticky piston pulls the block back");
                // Obsidian can't be pushed; thirteen blocks are one too many.
                Put(2, 11, 0, Blocks.Obsidian); Put(0, 11, 0, (ushort)(Blocks.Lever + 1)); Run(0.5f);
                Check(mw.Get(2, 11, 0) == Blocks.Obsidian && mw.Get(3, 11, 0) == 0, "obsidian can't be pushed");
                Put(0, 11, 0, Blocks.Lever); Run(0.3f);
                for (int i = 0; i < 13; i++) Put(2 + i, 11, 0, Blocks.Cobble);
                Put(0, 11, 0, (ushort)(Blocks.Lever + 1)); Run(0.5f);
                Check(mw.Get(15, 11, 0) == 0 && mw.Get(2, 11, 0) == Blocks.Cobble, "thirteen blocks in a row are one too many to push");

                // An observer looking at a cell blinks when the block there changes, and lights a lamp behind it.
                Put(0, 11, 5, (ushort)(Blocks.Observer + 1 * 2));          // its eye looks toward +X
                Put(-1, 11, 5, Blocks.SparkLamp);
                Run(0.3f);
                bool lit = false;
                Put(1, 11, 5, Blocks.Cobble);
                for (int i = 0; i < 12; i++) { sig.Tick(0.05f, null); if (mw.Get(-1, 11, 5) == Blocks.SparkLamp + 1) lit = true; }
                Run(1);
                Check(lit && mw.Get(-1, 11, 5) == Blocks.SparkLamp && mw.Get(0, 11, 5) == Blocks.Observer + 2, "an observer blinks when the block it watches changes, and the lamp behind it flashes");

                // A comparator is on while the container behind it holds something; a dispenser fires once per switch.
                bool full = false; int fired = 0;
                sig.Holds = (x, y, z) => full && x == 0 && y == 11 && z == 9;
                sig.Fired = (x, y, z, dir) => fired++;
                Put(1, 11, 9, (ushort)(Blocks.Comparator + 1 * 2));        // reads (0,11,9), gives power toward +X
                Put(2, 11, 9, Blocks.SparkLamp);
                Run(0.5f);
                Check(mw.Get(2, 11, 9) == Blocks.SparkLamp, "a comparator by an empty chest gives nothing");
                full = true; sig.Wake(0, 11, 9); Run(0.6f);
                Check(mw.Get(2, 11, 9) == Blocks.SparkLamp + 1, "with something in the chest it lights the lamp in front of it");
                full = false; sig.Wake(0, 11, 9); Run(0.6f);
                Check(mw.Get(2, 11, 9) == Blocks.SparkLamp, "and stops when the chest is emptied");
                Put(1, 11, 13, (ushort)(Blocks.Dispenser + 0)); Put(0, 11, 13, Blocks.Lever);
                Run(0.3f);
                Put(0, 11, 13, (ushort)(Blocks.Lever + 1)); Run(0.4f); Put(0, 11, 13, Blocks.Lever); Run(0.4f); Put(0, 11, 13, (ushort)(Blocks.Lever + 1)); Run(0.4f);
                Check(fired == 2, $"a dispenser goes off once each time power arrives ({fired})");

                // A repeater set slower passes power on later than a quick one.
                float When(ushort repeaterBase)
                {
                    var rw = new Patch(); Signal rs = null;
                    rs = new Signal(rw.Get, (x, y, z, id) => { rw.Set(x, y, z, id); rs.Wake(x, y, z); });
                    void P2(int x, int y, int z, ushort id) { rw.Set(x, y, z, id); rs.Wake(x, y, z); }
                    P2(1, 11, 0, (ushort)(repeaterBase + 1 * 2)); P2(2, 11, 0, Blocks.SparkLamp); P2(0, 11, 0, Blocks.Lever);
                    for (int i = 0; i < 10; i++) rs.Tick(0.05f, null);
                    P2(0, 11, 0, (ushort)(Blocks.Lever + 1));
                    for (int i = 1; i <= 40; i++) { rs.Tick(0.05f, null); if (rw.Get(2, 11, 0) == Blocks.SparkLamp + 1) return i * 0.05f; }
                    return -1;
                }
                float quick = When(Blocks.Repeaters[0]), slow = When(Blocks.Repeaters[3]);
                Check(quick > 0 && slow > quick + 0.2f, $"a repeater set to its longest wait passes power on later ({quick:0.00} s against {slow:0.00} s)");

                // A detector rail gives power while something is on it.
                Put(0, 11, 20, Blocks.DetectorRail); Put(1, 11, 20, Blocks.SparkLamp);
                Run(0.3f);
                Check(mw.Get(1, 11, 20) == Blocks.SparkLamp, "a detector rail with nothing on it gives no power");
                var cart = new List<(float, float, float)> { (0.5f, 11.06f, 20.5f) };
                for (int i = 0; i < 8; i++) sig.Tick(0.05f, cart);
                Check(mw.Get(1, 11, 20) == Blocks.SparkLamp + 1 && mw.Get(0, 11, 20) == Blocks.DetectorRail + 1, "with a cart on it, it lights the lamp beside it");
                Run(1.2f);
                Check(mw.Get(1, 11, 20) == Blocks.SparkLamp && mw.Get(0, 11, 20) == Blocks.DetectorRail, "and stops once the cart has gone");
                Check(Rails.Is(Blocks.DetectorRail) && Rails.Ends((ushort)(Blocks.DetectorRail + 3)) == (1, 3, -1), "a detector rail is a rail, whichever way it is on");

                // A daylight sensor follows the sun; a note block sounds once each time power arrives.
                bool day = false; int notes = 0, lastNote = -1;
                sig.IsDay = () => day; sig.Played = (x, y, z, pitch) => { notes++; lastNote = pitch; };
                Put(0, 11, 24, Blocks.DaylightSensor); Put(1, 11, 24, (ushort)(Blocks.NoteBlock + 3 * 2));
                Run(1.5f);
                Check(notes == 0 && mw.Get(0, 11, 24) == Blocks.DaylightSensor, "a daylight sensor is off at night");
                day = true; Run(1.6f);
                Check(mw.Get(0, 11, 24) == Blocks.DaylightSensor + 1 && notes == 1 && lastNote == 3, $"at sunrise it comes on, and the note block beside it sounds its note once ({notes}, note {lastNote})");
                day = false; Run(1.6f);
                Check(mw.Get(0, 11, 24) == Blocks.DaylightSensor && notes == 1, "at sunset it goes off again");
            }

            // A village guard sees off a ghoul; on Hard, creatures hit half as hard again.
            {
                var gw = new MobSim(new Patch().Get, 8) { Spawning = false, Time01 = 0.9f };
                var guard = gw.Spawn(MobKind.Guard, 0.5f, 11, 0.5f); guard.HomeX = 0; guard.HomeZ = 0;
                var ghoul = gw.Spawn(MobKind.Ghoul, 6.5f, 11, 0.5f);
                var nobody = new List<Seen>();
                for (int i = 0; i < 20 * 20 && ghoul.Health > 0; i++) gw.Tick(0.05f, nobody);
                Check(ghoul.Health <= 0 && guard.Health == guard.Def.Health, $"a village guard sees off a ghoul (ghoul health {ghoul.Health:0})");
                float Blow(int difficulty)
                {
                    var hs = new MobSim(new Patch().Get, 8) { Spawning = false, Time01 = 0.9f, Difficulty = difficulty };
                    hs.Spawn(MobKind.Ghoul, 1.5f, 11, 0.5f);
                    var who = new List<Seen> { new Seen { Id = 1, X = 0.5f, Y = 11, Z = 0.5f, Alive = true, Key = "kid" } };
                    float got = 0; hs.HitPlayer += (id, dmg, fx, fz, from) => { if (got == 0) got = dmg; };
                    for (int i = 0; i < 20 * 6 && got == 0; i++) hs.Tick(0.05f, who);
                    return got;
                }
                float normal = Blow(2), hard = Blow(3);
                Check(normal > 0 && Math.Abs(hard - normal * 1.5f) < 0.01f, $"on Hard a ghoul hits half as hard again ({normal:0.0} against {hard:0.0})");
                // Dyed sheep give wool of their colour.
                var ss = new MobSim(new Patch().Get, 8) { Spawning = false };
                var sheep = ss.Spawn(MobKind.Sheep, 0.5f, 11, 0.5f);
                ss.Use(sheep.Id, "dye_purple", 0.5f, 0.5f, "kid");
                var wool = ss.Use(sheep.Id, "shears", 0.5f, 0.5f, "kid");
                Check(wool is (string woolItem, int woolCount) && woolItem == "purple_wool" && woolCount > 0 && Mob.FleeceOf(sheep.More) == Blocks.DyeIndex("purple"), "a sheep dyed purple is purple to everyone and gives purple wool");
            }

            // What lies on the ground is still there after a save and a load, as old as it was.
            {
                var dw = new Patch(); var d1 = new DropSim(dw.Get);
                d1.Toss("cobble", 7, 0, 0.5f, 12, 0.5f, 0, 0, 0); d1.Toss("iron_pickaxe", 1, 30, 3.5f, 12, 0.5f, 0, 0, 0);
                for (int i = 0; i < 20 * 20; i++) d1.Tick(0.05f);
                string file = Path.Combine(Path.GetTempPath(), "stratum-drops-" + Guid.NewGuid().ToString("N") + ".bin");
                d1.SaveFile(file);
                var d2 = new DropSim(dw.Get); d2.LoadFile(file);
                try { File.Delete(file); } catch { }
                bool same = d2.All.Count == 2 && d2.All.Exists(x => x.Item == "cobble" && x.Count == 7 && x.Age > 19) && d2.All.Exists(x => x.Item == "iron_pickaxe" && x.Wear == 30);
                Check(same, $"things lying on the ground are still there after a save and a load ({d2.All.Count})");
            }

            // Chutes: one draws in what lies on it; a side chute feeds the chest it points at.
            {
                var cw = new Patch();
                cw.Set(0, 11, 0, Blocks.Chute); cw.Set(0, 10, 0, Blocks.Chest);
                cw.Set(5, 11, 0, (ushort)(Blocks.SideChute + 1)); cw.Set(6, 11, 0, Blocks.Chest); cw.Set(5, 12, 0, Blocks.Chest);
                var boxes = new Containers(); var chutes = new Chutes(boxes, cw.Get, (x, z) => true);
                chutes.Add(0, 11, 0); chutes.Add(5, 11, 0);
                var lying = new DropSim(cw.Get);
                lying.Toss("cobble", 6, 0, 0.5f, 13, 0.5f, 0, 0, 0, 0);
                boxes.Set(5, 12, 0, 3, new Stack("coal", 4));
                for (int i = 0; i < 20 * 12; i++) { lying.Tick(0.05f); if (i % 8 == 0) lying.Feed(chutes.Take); chutes.Tick(0.05f); }
                int under = 0; foreach (var st in boxes.At(0, 10, 0)) if (st.Item == "cobble") under += st.Count;
                int side = 0; foreach (var st in boxes.At(6, 11, 0)) if (st.Item == "coal") side += st.Count;
                Check(lying.All.Count == 0 && under == 6, $"what lies on a chute is drawn in and passed to the chest below ({under} of 6, {lying.All.Count} left lying)");
                Check(side == 4, $"a side chute takes from the chest above it and feeds the chest it points at ({side} of 4)");

                // An empty cart on a powered booster with a block behind it rolls off down the line and stops at the end.
                var rw = new Patch();
                rw.Set(-1, 11, 0, Blocks.Stone); rw.Set(0, 11, 0, (ushort)(Blocks.BoosterRail + 1)); rw.Set(0, 11, 1, (ushort)(Blocks.Lever + 1));
                for (int x = 1; x <= 9; x++) rw.Set(x, 11, 0, (ushort)(Blocks.Rail + 1));
                var rs = new MobSim(rw.Get, 2) { Spawning = false };
                var cart = rs.Park(MobKind.Cart, 0.5f, 11.06f, 0.5f, 90);
                var nobody = new List<Seen>();
                for (int i = 0; i < 20 * 12; i++) rs.Tick(0.05f, nobody);
                Check(cart.X > 9.4f && cart.X < 10.05f && cart.Roll == 0, $"an empty cart on a powered booster rolls down the line and stops at its end (x {cart.X:0.0})");
                var still = rs.Park(MobKind.Cart, 4.5f, 11.06f, 0.5f, 90);
                for (int i = 0; i < 20 * 3; i++) rs.Tick(0.05f, nobody);
                Check(Math.Abs(still.X - 4.5f) < 0.01f, "a cart on plain level rail stays where it was put");
                var cc = rs.Park(MobKind.ChestCart, 2.5f, 11.06f, 0.5f, 90);
                Check(cc.Hold > 0 && rs.Mount(cc.Id, 2.5f, 11, 0.5f, out _, out _, out _, out _) == null, "a chest cart has a store of its own and can't be ridden");
            }

            // A keg blows a hole, sets off the keg beside it, and leaves obsidian standing.
            var bw = new Patch(); int booms = 0, broke = 0;
            var blasts = new Blasts(bw.Get, bw.Set) { Boom = (x, y, z, p) => booms++, Broke = (x, y, z, d) => broke++ };
            bw.Set(0, 11, 0, Blocks.KegLit); bw.Set(2, 11, 0, Blocks.BlastKeg); bw.Set(0, 9, 0, Blocks.Obsidian);
            blasts.Prime(0, 11, 0);
            for (int i = 0; i < 20 * 7; i++) blasts.Tick(0.05f);
            Check(booms == 2 && broke > 20 && bw.Get(0, 10, 0) == 0 && bw.Get(0, 9, 0) == Blocks.Obsidian && bw.Get(2, 11, 0) == 0 && bw.Get(0, 3, 0) == Blocks.Stone,
                $"a lit keg blows a hole in the ground, sets off the keg beside it, and obsidian stands ({booms} blasts, {broke} blocks)");

            // Fire eats a wooden hut and then goes out; in a world where fire doesn't spread it only burns where lit.
            foreach (bool spreads in new[] { true, false })
            {
                var fw = new Patch();
                for (int x = 0; x < 4; x++) for (int y = 11; y < 14; y++) for (int z = 0; z < 4; z++) fw.Set(x, y, z, Blocks.OakPlanks);
                var fire = new Fire(fw.Get, fw.Set) { Spreads = spreads };
                Check(fire.Light(-1, 11, 1), "a fire is lit beside a wooden hut");
                for (int i = 0; i < 20 * 400; i++) fire.Tick(0.05f);
                int planks = 0, burning = 0;
                foreach (var kv in fw.Cells) { if (kv.Value == Blocks.OakPlanks) planks++; if (kv.Value == Blocks.Fire) burning++; }
                if (spreads) Check(planks < 10 && burning == 0 && fire.Count == 0, $"it eats the hut and goes out ({planks} planks left, {burning} still burning)");
                else Check(planks == 48 && burning == 0 && fire.Count == 0, $"where fire doesn't spread the hut stands and the fire dies ({planks} planks left)");
            }
            Check(Items.Recipes.Exists(r => r.Out == "blast_keg") && Items.Get("blast_powder") != null, "a blast keg can be made");
            Check(Blocks.Get((ushort)(Blocks.WallTorch + 2)).OnWall && Mining.Drop(Blocks.Get((ushort)(Blocks.WallTorch + 2))).item == "torch", "a torch on a wall gives a torch back");
        }

        // Things a read through the code turned up (0.13).
        static void ReviewChecks()
        {
            var shears = Items.Get("shears");
            Check(Mining.Drop(Blocks.Get(Blocks.OakLeaves), shears).item == "oak_leaves" && Mining.Drop(Blocks.Get(Blocks.TallGrass), shears).item == "tall_grass",
                "shears bring leaves and tall grass away whole (mossy cobble, hedges and the forest picture need them)");
            Check(Mining.Drop(Blocks.Get(Blocks.OakLeaves), null).item == null, "bare hands still get nothing from leaves");
            int before = Items.DerivedCount;
            for (int i = 0; i < 50; i++) Items.Get("nothing_" + i + "@zz");
            Items.Get("x@" + new string('q', 5000));
            Check(Items.DerivedCount == before, "made-up item names aren't remembered");
            Check(Items.Get("no_such_thing@zz") == null, "and still aren't items");
        }
    }
}
