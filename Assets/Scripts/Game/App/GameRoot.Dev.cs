#if UNITY_EDITOR
using Stratum.Core;
using UnityEngine;

namespace Stratum.Game
{
    // Editor-only shortcuts for checking the game from the command line (script eval).
    public sealed partial class GameRoot
    {
        // A fresh survival world named "Dev" (replacing the last one).
        public static void DevWorld(int difficulty = 2, int seed = 4242, int kind = 0)
        {
            foreach (var m in WorldStore.List()) if (m.name == "Dev") WorldStore.Delete(m);
            I.StartCoroutine(I.Enter(WorldStore.Create("Dev", seed, false, difficulty, (WorldKind)kind)));
        }

        // Hover above where the player stands, looking out (for screenshots).
        public static void DevLift(float up, float yaw, float pitch) { var f = I._player.Feet; DevView(f.x, f.y + up, f.z, yaw, pitch); }

        // Stand somewhere and look (for screenshots): feet position, yaw, pitch; flying so nothing falls.
        public static void DevView(float x, float y, float z, float yaw, float pitch)
        {
            I._player.Creative = true; I._player.Flying = true; I._player.Feet = new Vector3(x, y, z); I._player.Velocity = Vector3.zero;
            I._player.Yaw = yaw; I._player.Pitch = pitch; I._world.SetFocus(I._player.Feet);
        }

        // Things held by the cursor when the backpack closes: a worn tool keeps its wear, and with a full pack the
        // rest lands in a satchel. Then a whole pack dropped at once is opened as a satchel (for a screenshot).
        public static string DevPack()
        {
            var g = I; var inv = g._player.Inv; var sb = new System.Text.StringBuilder();
            g._player.Creative = false;
            for (int i = 0; i < Inventory.Size; i++) inv.Slots[i] = Core.Stack.None;
            g._hud.OpenInventory(null);
            g._hud.DevCursor(new Core.Stack("iron_pickaxe", 1) { Wear = 150 });
            g._hud.CloseInventory();
            sb.Append($"pickaxe back with wear {inv.Slots[0].Wear}; ");
            for (int i = 0; i < Inventory.Main; i++) inv.Slots[i] = new Core.Stack("cobble", 64);
            g._hud.OpenInventory(null);
            g._hud.DevCursor(new Core.Stack("iron_pickaxe", 1) { Wear = 77 });
            g._hud.CloseInventory();
            var f = g._player.Feet; int fx = Mathf.FloorToInt(f.x), fy = Mathf.FloorToInt(f.y), fz = Mathf.FloorToInt(f.z);
            var l = (LocalLink)g._link;
            string found = "no satchel";
            for (int dy = -2; dy <= 3; dy++) for (int dz = -3; dz <= 3; dz++) for (int dx = -3; dx <= 3; dx++)
                if (g._world.Get(fx + dx, fy + dy, fz + dz) == Blocks.Satchel) { var st = l.Boxes.At(fx + dx, fy + dy, fz + dz); found = $"satchel holds {st[0].Item} wear {st[0].Wear}"; g._world.Set(fx + dx, fy + dy, fz + dz, 0); }
            sb.Append(found + "; ");
            for (int i = 0; i < Inventory.Size; i++) inv.Slots[i] = Core.Stack.None;
            var pack = new Core.Stack[41]; for (int i = 0; i < 41; i++) pack[i] = new Core.Stack(i % 3 == 0 ? "cobble" : i % 3 == 1 ? "torch" : "steak", i + 1);
            g._link.Drop(f, pack);
            for (int dy = -2; dy <= 3; dy++) for (int dz = -3; dz <= 3; dz++) for (int dx = -3; dx <= 3; dx++)
                if (g._world.Get(fx + dx, fy + dy, fz + dz) == Blocks.Satchel) { g._link.OpenBox(new Vector3Int(fx + dx, fy + dy, fz + dz)); sb.Append($"opened a satchel of {l.Boxes.At(fx + dx, fy + dy, fz + dz).Length}"); return sb.ToString(); }
            return sb + "no second satchel";
        }

        public static string DevState() => I._state + (I._player != null ? $" at {I._player.Feet} hp {I._player.Health} food {I._player.Food}" : "") + (I._link != null ? $" mobs {I._link.Mobs.Count}" : "");

        // A creature a few metres in front of the player, on the ground.
        public static int DevSpawn(string kind, float ahead = 5, float side = 0)
        {
            if (!(I._link is LocalLink l)) return -1;
            var p = I._player;
            var f = Quaternion.Euler(0, p.Yaw, 0) * new Vector3(side, 0, ahead) + p.Feet;
            int x = Mathf.FloorToInt(f.x), z = Mathf.FloorToInt(f.z), y = Chunk.Height - 2;
            while (y > 1 && !Blocks.Get(I._world.Get(x, y - 1, z)).Solid) y--;
            var m = l.Sim.Spawn((MobKind)System.Enum.Parse(typeof(MobKind), kind), f.x, y, f.z);
            return m != null ? m.Id : -1;
        }

        // Somewhere open: a grass column with nothing over it and flat ground around.
        public static string DevOpen(int range = 120)
        {
            var w = I._world; var p = I._player.Feet;
            for (int r = 4; r < range; r += 3)
                for (int a = 0; a < 24; a++)
                {
                    int x = Mathf.FloorToInt(p.x + Mathf.Cos(a * 0.2618f) * r), z = Mathf.FloorToInt(p.z + Mathf.Sin(a * 0.2618f) * r);
                    if (!w.IsReady(x, z)) continue;
                    int top = Top(x, z);
                    if (top < 0 || w.Get(x, top, z) != Blocks.Grass) continue;
                    bool flat = true;
                    for (int dz = -6; dz <= 6 && flat; dz += 2) for (int dx = -6; dx <= 6 && flat; dx += 2)
                    { int t = Top(x + dx, z + dz); if (t < 0 || Mathf.Abs(t - top) > 1 || !Blocks.Get(w.Get(x + dx, t, z + dz)).Solid) flat = false; }
                    if (!flat) continue;
                    I._player.Feet = new Vector3(x + 0.5f, top + 1, z + 0.5f); I._player.ResetFall();
                    return $"moved to {x} {top + 1} {z}";
                }
            return "nothing open";
        }

        static int Top(int x, int z)
        {
            for (int y = Chunk.Height - 2; y > 1; y--) { var b = Blocks.Get(I._world.Get(x, y, z)); if (b.Id != 0 && !(b.Replaceable && !b.Solid)) return y; }
            return -1;
        }

        public static void DevTime(float t) => I._sky.Time01 = t;
        public static void DevGive(string item, int n) => I._player.Inv.Add(item, n);
        public static void DevLook(float yaw, float pitch) { I._player.Yaw = yaw; I._player.Pitch = pitch; }
        public static void DevSpawning(bool on) { if (I._link is LocalLink l) l.Sim.Spawning = on; }
        // Runs through chests, satchels, beds, eating and fighting, and says what happened.
        public static string DevCheck()
        {
            var g = I; var log = new System.Text.StringBuilder();
            void Say(string x) => log.Append(x).Append(" | ");
            if (g._state == State.Dead) g.Respawn();
            var l = (LocalLink)g._link; l.Sim.Clear(); l.Sim.Spawning = false;
            g._sky.Time01 = 0.3f;
            var inv = g._player.Inv;
            for (int i = 0; i < Inventory.Size; i++) inv.Slots[i] = Core.Stack.None;
            inv.Add("iron_sword", 1); inv.Add("steak", 3); inv.Add("chest", 1); inv.Add("bed", 1); inv.Add("cobble", 20);
            var f = g._player.Feet; int x = Mathf.FloorToInt(f.x) + 2, y = Mathf.FloorToInt(f.y), z = Mathf.FloorToInt(f.z);
            // Chest: put a stack in, break it, get it back.
            g._world.Set(x, y, z, Blocks.Chest);
            Core.Stack[] seen = null; var was = g._link.BoxOpened;
            g._link.BoxOpened = (at, st) => seen = st;
            g._link.OpenBox(new Vector3Int(x, y, z));
            g._link.BoxOpened = was;
            Say($"chest opened with {seen?.Length} slots");
            g._link.BoxSlot(new Vector3Int(x, y, z), 4, new Core.Stack("cobble", 7));
            g._link.CloseBox();
            Say($"chest holds {l.Boxes.At(x, y, z)[4].Count} cobble");
            int before = inv.Count("cobble");
            g._world.Set(x, y, z, 0);
            Say($"broke chest: cobble {before} -> {inv.Count("cobble")}");
            // Satchel: drop things, take them back.
            g._link.Drop(f, new[] { new Core.Stack("steak", 2) });
            var (sx, sy, sz) = Containers.SpotFor(g._world.Get, Mathf.FloorToInt(f.x), Mathf.FloorToInt(f.y), Mathf.FloorToInt(f.z));
            Say($"satchel at {sx} {sy} {sz}: {Blocks.Get(g._world.Get(sx, sy, sz)).Name}");
            int st0 = inv.Count("steak"); g._world.Set(sx, sy, sz, 0);
            Say($"opened satchel: steak {st0} -> {inv.Count("steak")}");
            // Eating.
            g._player.Food = 10; g._player.Eat(Items.Get("steak"));
            Say($"ate steak: food {g._player.Food} sat {g._player.Saturation}");
            // Bed: two halves, sleep through the night.
            int bx = x, bz = z - 2;
            g._world.Set(bx, y, bz, (byte)(Blocks.Bed + 0)); g._world.Set(bx, y, bz + 1, (byte)(Blocks.Bed + 1));
            g._sky.Time01 = 0.85f;
            g._player.Feet = new Vector3(bx + 1.5f, y, bz + 0.5f);
            g.TrySleep(new Vector3Int(bx, y, bz));
            Say($"asleep {g._player.Asleep}, bed set {g._hasBed}");
            return log.ToString();
        }

        // A row of the building blocks in front of the player, a pool of water and one of lava left to flow.
        public static string DevShowcase()
        {
            var g = I; var w = g._world; var f = g._player.Feet;
            int x0 = Mathf.FloorToInt(f.x) - 6, y = Mathf.FloorToInt(f.y), z0 = Mathf.FloorToInt(f.z) + 5;
            g._player.Yaw = 0; g._player.Pitch = 12;
            for (int dz = -1; dz <= 8; dz++) for (int dx = -1; dx <= 14; dx++) { w.Set(x0 + dx, y - 1, z0 + dz, Blocks.Grass); for (int h = 0; h < 5; h++) w.Set(x0 + dx, y + h, z0 + dz, 0); }
            void P(int dx, int dz, byte id, int h = 0) => w.Set(x0 + dx, y + h, z0 + dz, id);
            P(0, 0, Blocks.Slabs[0]); P(1, 0, (byte)(Blocks.Slabs[3] + 1)); P(2, 0, Blocks.Slabs[5]);
            P(3, 0, (byte)(Blocks.Stairs[0] + 2)); P(4, 0, (byte)(Blocks.Stairs[3] + 2)); P(5, 0, (byte)(Blocks.Stairs[4] + 2));
            P(7, 0, (byte)(Blocks.OakDoor + 2 * 4)); P(7, 0, (byte)(Blocks.OakDoor + 2 * 4 + 1), 1);
            P(8, 0, (byte)(Blocks.OakDoor + 2 * 4 + 2)); P(8, 0, (byte)(Blocks.OakDoor + 2 * 4 + 3), 1);
            P(10, 0, (byte)(Blocks.Trapdoor + 4)); P(11, 0, (byte)(Blocks.Trapdoor + 5));
            for (int i = 0; i < 4; i++) P(i, 3, Blocks.OakFence);
            P(4, 3, (byte)(Blocks.FenceGate + 0)); P(5, 3, Blocks.OakFence); P(5, 4, Blocks.OakFence);
            for (int i = 7; i < 10; i++) P(i, 3, Blocks.CobbleWall);
            for (int i = 11; i < 14; i++) { P(i, 3, Blocks.GlassPane); P(i, 3, Blocks.IronBars, 1); }
            P(13, 0, Blocks.Stone); P(13, 0, Blocks.Stone, 1); P(13, 0, Blocks.Stone, 2);
            P(13, -1, (byte)(Blocks.Ladder + 0)); P(13, -1, (byte)(Blocks.Ladder + 0), 1); P(13, -1, (byte)(Blocks.Ladder + 0), 2);
            P(6, 1, (byte)(Blocks.Sign + 2));
            g._link.SetSign(new Vector3Int(x0 + 6, y, z0 + 1), "Welcome to\nStratum!");
            // Water and lava on a raised shelf, to spill off.
            for (int dx = 0; dx < 3; dx++) for (int dz = 6; dz < 8; dz++) P(dx, dz, Blocks.Cobble);
            P(1, 7, Blocks.Water, 1);
            for (int dx = 10; dx < 13; dx++) for (int dz = 6; dz < 8; dz++) P(dx, dz, Blocks.Cobble);
            P(11, 7, Blocks.Lava, 1);
            return $"built at {x0} {y} {z0}";
        }

        // A little farm: water channel, farmland with every wheat stage, saplings, cane, hay; two cows fed wheat.
        public static string DevFarm()
        {
            var g = I; var w = g._world; var f = g._player.Feet;
            int x0 = Mathf.FloorToInt(f.x) - 5, y = Mathf.FloorToInt(f.y), z0 = Mathf.FloorToInt(f.z) + 4;
            g._player.Yaw = 0; g._player.Pitch = 25;
            for (int dz = -1; dz <= 9; dz++) for (int dx = -2; dx <= 12; dx++) { w.Set(x0 + dx, y - 1, z0 + dz, Blocks.Grass); w.Set(x0 + dx, y - 2, z0 + dz, Blocks.Dirt); for (int h = 0; h < 8; h++) w.Set(x0 + dx, y + h, z0 + dz, 0); }
            for (int dx = 0; dx < 9; dx++)
            {
                w.Set(x0 + dx, y - 1, z0 + 2, Blocks.Water);
                for (int r = 0; r < 2; r++)
                {
                    w.Set(x0 + dx, y - 1, z0 + r * 3 + 1 - (r == 0 ? 0 : 0), Blocks.FarmlandWet);
                    w.Set(x0 + dx, y, z0 + r * 3 + 1, (byte)(Blocks.Wheat + Mathf.Min(3, dx / 2)));
                }
            }
            w.Set(x0 + 10, y, z0 + 1, Blocks.SaplingOak); w.Set(x0 + 11, y, z0 + 3, Blocks.SaplingBirch); w.Set(x0 + 10, y, z0 + 5, Blocks.SaplingPine);
            w.Set(x0 + 9, y - 1, z0 + 2, Blocks.Sand);
            for (int h = 0; h < 3; h++) w.Set(x0 + 9, y + h, z0 + 2, Blocks.SugarCane);
            w.Set(x0 - 1, y, z0 + 1, Blocks.HayBale); w.Set(x0 - 1, y + 1, z0 + 1, Blocks.HayBale); w.Set(x0 - 1, y, z0 + 2, Blocks.HayBale);
            // Two cows in love beside each other.
            if (g._link is LocalLink ll)
            {
                var a = ll.Sim.Spawn(MobKind.Cow, x0 + 4, y, z0 + 7.5f); var b = ll.Sim.Spawn(MobKind.Cow, x0 + 6, y, z0 + 7.5f);
                ll.Sim.Use(a.Id, "wheat", a.X, a.Z); ll.Sim.Use(b.Id, "wheat", b.X, b.Z);
            }
            return $"farm at {x0} {y} {z0}";
        }

        // Opens a station with things in its slots: "enchant" (an iron sword, shards) or "anvil" (a worn sword, a book).
        public static string DevWork(string station, int level)
        {
            var g = I;
            g._player.XpLevel = level;
            g.OpenInventory(station);
            g._hud.DevWork(station == "enchant" ? new Core.Stack("iron_sword", 1) : new Core.Stack("iron_sword@s2", 1) { Wear = 200 },
                           station == "enchant" ? new Core.Stack("sunstone_shard", 5) : new Core.Stack("book@s2u3", 1));
            return "ok";
        }

        // Goes to the nearest village (searching outward from the player).
        public static string DevVillage()
        {
            var g = I; var f = g._player.Feet;
            for (int r = 0; r < 4000; r += 200)
                for (int a = 0; a < 16; a++)
                {
                    int x = (int)(f.x + Mathf.Cos(a * Mathf.PI / 8) * r), z = (int)(f.z + Mathf.Sin(a * Mathf.PI / 8) * r);
                    var v = g._world.Gen.Villages.Near(x, z, 400);
                    if (v == null) continue;
                    g._player.Feet = new Vector3(v.X + 0.5f, v.Y + 14, v.Z - 16.5f);
                    g._player.Flying = true; g._player.Yaw = 0; g._player.Pitch = 28;
                    return $"village at {v.X} {v.Y} {v.Z}, {v.Parts.Count} parts, {v.Houses} houses";
                }
            return "no village found";
        }

        public static string DevAfterSleep() => $"time {I._sky.Time01:0.00} day {I._sky.Day} asleep {I._player.Asleep} at {I._player.Feet}";

        // Fight: a cow in front, hit it with the sword until it dies.
        public static string DevFight()
        {
            var l = (LocalLink)I._link;
            int id = DevSpawn("Cow", 2, 0);
            int hits = 0;
            int beef0 = I._player.Inv.Count("raw_beef"), hide0 = I._player.Inv.Count("hide");
            while (hits < 20 && l.Sim.TryGet(id, out var m) && m.Dead <= 0) { m.Hurt = 0; l.Strike(id, 7); hits++; }
            return $"cow died after {hits} hits; beef {beef0}->{I._player.Inv.Count("raw_beef")}, hide {hide0}->{I._player.Inv.Count("hide")}";
        }

        public static string DevMobs()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var m in I._link.Mobs) sb.Append($"{m.Kind}#{m.Id} {(m.Pos - I._player.Feet).magnitude:0.0}m hp{m.Health:0.00} f{m.Flags}; ");
            return sb.ToString();
        }
    }
}
#endif
