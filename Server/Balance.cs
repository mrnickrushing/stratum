using System;
using System.Collections.Generic;
using System.Linq;
using Stratum.Core;

namespace Stratum.Server
{
    // "--balance": the numbers a player feels, worked out from the same rules the game uses. Read it after changing
    // a tool, a set of armour or a creature.
    public static class Balance
    {
        static readonly string[] Mats = { "wood", "stone", "copper", "iron", "gold", "star" };
        static readonly string[] Sets = { "hide", "copper", "iron", "gold", "star" };

        public static int Run()
        {
            Console.WriteLine("MINING: seconds to break (x = breaks but drops nothing)");
            Console.WriteLine($"{"",-16}{"hand",8}" + string.Concat(Mats.Select(m => $"{m,8}")));
            foreach (var key in new[] { "dirt", "sand", "oak_log", "oak_planks", "stone", "cobble", "coal_ore", "copper_ore", "iron_ore", "gold_ore", "jade_ore", "sunstone_ore", "star_ore", "stillstone", "gate_stone" })
            {
                int id = Array.IndexOf(Blocks.Keys, key);
                if (id < 0) continue;
                var b = Blocks.Get((ushort)id);
                string tool = b.Tool == Tool.Axe ? "axe" : b.Tool == Tool.Shovel ? "shovel" : "pickaxe";
                string Cell(ItemDef held) { float t = Mining.Time(b, held, false); return (float.IsInfinity(t) ? "never" : t.ToString("0.00")) + (Mining.Drops(b, held) ? "" : "x"); }
                Console.WriteLine($"{key,-16}{Cell(null),8}" + string.Concat(Mats.Select(m => $"{Cell(Items.Get(m + "_" + tool)),8}")) + $"   (hardness {b.Hardness}, needs tier {b.TierNeeded})");
            }

            Console.WriteLine();
            Console.WriteLine("TOOLS: tier, speed, uses; sword damage");
            foreach (var m in Mats)
            {
                var p = Items.Get(m + "_pickaxe"); var s = Items.Get(m + "_sword");
                Console.WriteLine($"{m,-8} tier {p.Tier}  speed {p.Speed,4}  uses {p.Durability,5}   sword {s.Damage,4} a hit, {s.Damage / 0.55f:0.0} a second");
            }

            Console.WriteLine();
            Console.WriteLine("ARMOUR: points, share of a blow stopped, blows before the coat wears out");
            foreach (var set in Sets)
            {
                int pts = 0; foreach (var piece in new[] { "cap", "coat", "leggings", "boots" }) pts += Items.Get(set + "_" + piece).Armour;
                Console.WriteLine($"{set,-8} {pts,2} points  stops {Math.Min(20, pts) * 4,2}%  lasts {Items.Get(set + "_coat").Durability} blows");
            }

            Console.WriteLine();
            Console.WriteLine("FIGHTS (Normal): swings to bring it down / seconds; then seconds it needs to bring a full-health player down, bare and in each set");
            Console.WriteLine($"{"",-16}{"hp",4}{"hits",5}{"every",6}  {"fist",9}" + string.Concat(Mats.Select(m => $"{m,9}")) + "  |" + $"{"bare",6}" + string.Concat(Sets.Select(s => $"{s,7}")));
            foreach (var d in MobSim.Defs)
            {
                if (d == null || d.Damage <= 0 || !(d.Hostile || d.Kind == MobKind.Hound)) continue;
                string Kill(float dmg, float every) { int n = (int)Math.Ceiling(d.Health / dmg); return $"{n}/{n * every:0}s"; }
                string line = $"{d.Name,-16}{d.Health,4}{d.Damage,5}{d.AttackEvery,6}  {Kill(1, 0.5f),9}" + string.Concat(Mats.Select(m => $"{Kill(Items.Get(m + "_sword").Damage, 0.55f),9}")) + "  |";
                string Dies(int pts) { float per = d.Damage * (1 - Math.Min(20, pts) * 0.04f); int blows = (int)Math.Ceiling(20 / per); return $"{blows * d.AttackEvery:0}s"; }
                line += $"{Dies(0),6}";
                foreach (var set in Sets) { int pts = 0; foreach (var piece in new[] { "cap", "coat", "leggings", "boots" }) pts += Items.Get(set + "_" + piece).Armour; line += $"{Dies(pts),7}"; }
                Console.WriteLine(line);
            }
            Console.WriteLine("(Easy: creatures hit 0.6 as hard. Hard: 1.5 as hard.)");

            Console.WriteLine();
            Console.WriteLine("FOOD: points and how long it holds");
            foreach (var it in Items.Order) if (it.Food > 0 && !it.Drink) Console.WriteLine($"{it.Name,-18} {it.Food,2} food  {it.Saturation,5:0.0} lasting");
            Console.WriteLine("A food point is spent by 40 m of sprinting, 267 m of swimming, 40 swings or jumps-with-a-blow, or two thirds of one health mended.");
            Console.WriteLine();
            Nights(1);
            Console.WriteLine();
            Nights(2);
            Console.WriteLine();
            Nights(3);
            return 0;
        }

        // Whole nights lived through by a player who stays out in the open by the world's start and goes for whatever
        // is after them (walking, one block up at a time): the hard way to spend a night, since anyone sensible digs
        // in or sleeps. Fed well enough to mend slowly. Counted: times brought down, creatures seen off, hurt taken.
        const int Runs = 4;

        public static void Nights(int difficulty)
        {
            Console.WriteLine($"NIGHTS in the open ({new[] { "Peaceful", "Easy", "Normal", "Hard" }[difficulty]}): ten minutes of dark, going for whatever attacks, twelve nights each (three worlds)");
            Console.WriteLine($"{"armour",-8}{"sword",-8}{"falls",7}{"first fall",12}{"seen off",10}{"came",6}{"hurt taken",12}   what brought them down");
            var kits = new (string set, string sword)[] { ("", ""), ("", "wood"), ("hide", "stone"), ("copper", "copper"), ("iron", "iron"), ("gold", "gold"), ("star", "star") };
            foreach (var (set, sword) in kits)
            {
                int pts = 0; if (set != "") foreach (var piece in new[] { "cap", "coat", "leggings", "boots" }) pts += Items.Get(set + "_" + piece).Armour;
                float dmg = sword == "" ? 1 : Items.Get(sword + "_sword").Damage, every = sword == "" ? 0.5f : 0.55f;
                int falls = 0, kills = 0, came = 0; float first = 0, taken = 0; int firsts = 0;
                var by = new Dictionary<string, int>();
                foreach (int seed in new[] { 4242, 7, 90210 })
                for (int run = 0; run < Runs; run++)
                {
                    var g = new WorldGen(seed, WorldKind.Normal);
                    var chunks = new Dictionary<(int, int), Chunk>();
                    ushort Get(int x, int y, int z)
                    {
                        if ((uint)y >= Chunk.Height) return 0;
                        var k = (x >> 4, z >> 4);
                        if (!chunks.TryGetValue(k, out var c)) { c = new Chunk(k.Item1, k.Item2); g.Generate(c); chunks[k] = c; }
                        return c.Get(x & 15, y, z & 15);
                    }
                    var (sx, sy, sz) = g.FindSpawn();
                    var sim = new MobSim(Get, seed + run * 7919) { Difficulty = difficulty, BiomeAt = g.BiomeAt, Time01 = 0.76f };
                    float hp = 20, cool = 0, sinceHurt = 9, mend = 0; bool fell = false; Mob last = null;
                    var seen = new List<Seen> { new Seen { Id = 1, X = sx + 0.5f, Y = sy, Z = sz + 0.5f, Alive = true, Key = "p", Held = "" } };
                    sim.HitPlayer += (id, d, x, z, m) => { float t = d * (1 - Math.Min(20, pts) * 0.04f); hp -= t; taken += t; sinceHurt = 0; last = m; };
                    sim.Burst += m => sim.Blast(m.X, m.Y + 0.6f, m.Z, 3, seen);
                    sim.Died += (m, who) => { if (m.Def.Hostile && who == 1) kills++; };
                    var met = new HashSet<int>();
                    const float dt = 0.05f;
                    for (float t = 0; t < 600; t += dt)
                    {
                        sim.Time01 = 0.76f + t / 1200f; if (sim.Time01 >= 1) sim.Time01 -= 1;
                        sim.Tick(dt, seen);
                        cool -= dt; sinceHurt += dt;
                        Mob near = null; float best = 3.2f * 3.2f;
                        foreach (var m in sim.All)
                        {
                            if (!m.Def.Hostile || m.Dead > 0 || m.Kind == MobKind.Arrow) continue;
                            float dx = m.X - seen[0].X, dy = m.Y - seen[0].Y, dz = m.Z - seen[0].Z, d2 = dx * dx + dy * dy + dz * dz;
                            if (d2 < 12 * 12 && met.Add(m.Id)) came++;
                            if (d2 < best) { best = d2; near = m; }
                        }
                        if (near != null && cool <= 0) { cool = every; sim.Strike(near.Id, 1, dmg, seen[0].X, seen[0].Y, seen[0].Z); }
                        // Nothing in reach: walk at whatever is after us (the nearest), over ground that can be walked.
                        if (near == null)
                        {
                            Mob foe = null; float fb = 40 * 40;
                            foreach (var m in sim.All)
                            {
                                if (!m.Def.Hostile || m.Dead > 0 || m.Kind == MobKind.Arrow || m.Target != 1) continue;
                                float dx = m.X - seen[0].X, dz = m.Z - seen[0].Z, d2 = dx * dx + dz * dz;
                                if (d2 < fb) { fb = d2; foe = m; }
                            }
                            if (foe != null)
                            {
                                var me = seen[0];
                                float dx = foe.X - me.X, dz = foe.Z - me.Z, len = (float)Math.Sqrt(dx * dx + dz * dz);
                                float nx = me.X + dx / len * 4.4f * dt, nz = me.Z + dz / len * 4.4f * dt;
                                int gx = (int)Math.Floor(nx), gz = (int)Math.Floor(nz), gy = int.MinValue;
                                for (int y = (int)Math.Floor(me.Y) + 1; y >= (int)Math.Floor(me.Y) - 4; y--)
                                    if (Blocks.Get(Get(gx, y - 1, gz)).Solid && !Blocks.Get(Get(gx, y, gz)).Solid && !Blocks.Get(Get(gx, y + 1, gz)).Solid) { gy = y; break; }
                                if (gy != int.MinValue) { me.X = nx; me.Z = nz; me.Y = gy; seen[0] = me; }
                            }
                        }
                        if (sinceHurt > 1 && hp < 20) { mend += dt; if (mend > 4) { mend = 0; hp = Math.Min(20, hp + 1); } }
                        if (hp <= 0)
                        {
                            falls++; hp = 20;
                            if (!fell) { fell = true; first += t; firsts++; }
                            string what = last?.Def.Name ?? "?"; by[what] = by.TryGetValue(what, out int n) ? n + 1 : 1;
                        }
                    }
                }
                string who = string.Join(", ", by.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}"));
                Console.WriteLine($"{(set == "" ? "none" : set),-8}{(sword == "" ? "fist" : sword),-8}{falls / (3f * Runs),7:0.0}{(firsts == 0 ? "never" : $"{first / firsts:0} s"),12}{kills / (3f * Runs),10:0.0}{came / (3f * Runs),6:0.0}{taken / (3f * Runs),12:0}   {who}");
            }
        }
    }
}
