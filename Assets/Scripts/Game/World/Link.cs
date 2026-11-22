using System;
using System.Collections.Generic;
using System.IO;
using Stratum.Core;
using UnityEngine;

namespace Stratum.Game
{
    // Whoever runs the creatures, chests and nights: this computer in a single-player world (LocalLink), or the
    // server (GameRoot's RemoteLink). The rest of the game talks to it the same way either way.
    public abstract class Link
    {
        public readonly List<MobState> Mobs = new List<MobState>();
        public readonly List<DropState> Drops = new List<DropState>();     // what lies on the ground nearby

        public abstract void Toss(Stack s, Vector3 from, Vector3 velocity);    // thrown from the pack
        public abstract void Spill(Vector3 at, Stack s);                       // fallen out of a block just broken
        public abstract void TakeDrop(int id);                                 // picked up: it arrives through Gave

        public abstract void Strike(int mob, float damage);
        public abstract void Shoot(Vector3 from, Vector3 velocity, float damage);
        public abstract void UseMob(int mob, string item);
        public abstract void OpenBox(Vector3Int at);             // the contents come back through BoxOpened
        public abstract void BoxSlot(Vector3Int at, int slot, Stack s);
        public abstract void CloseBox();
        public abstract void Sleep(bool on);
        public abstract void Drop(Vector3 at, Stack[] stacks);   // into a satchel on the ground
        public abstract void SetSign(Vector3Int at, string text);
        public abstract void Fertilize(Vector3Int at);           // bone meal
        public abstract void Mount(int mob);                     // into a boat or cart: Mounted answers
        public abstract void Park(MobKind kind, Vector3 at, float yaw);
        public Action<MobKind, Vector3, float> Mounted;
        public Action<Vector3Int, string> SignChanged;
        public virtual void Tick(float dt) { }
        public virtual void Save() { }
        public virtual int Difficulty => 2;

        public Action<Vector3Int, Stack[]> BoxOpened;
        public Action<Vector3Int, int, Stack> BoxChanged;
        public Action<string, int, int> Gave;                   // item, count, wear
        public Action<float, Vector3, MobKind> Hurt;            // damage, from where, by what
        public Action<int, int> Sleepers;                       // asleep, players
        public Action<MobKind, string, Vector3> MobSound;
        public Action<string> Said;
        public Action<Vector3, float> Boomed;                   // a blast: where, how strong
        public Action<Vector3, string, float> Splashed;         // a splash potion burst: where, what it does, for how long
        public virtual void Splash(Vector3 at, string effect, float secs) => Splashed?.Invoke(at, effect, secs);
    }

    // Single player: the creatures live on this computer, and chests are kept in the world's folder.
    public sealed class LocalLink : Link
    {
        readonly VoxelWorld _world;
        readonly Player _player;
        readonly Sky _sky;
        readonly string _file, _mobFile;
        readonly int _difficulty;
        public readonly MobSim Sim;
        public readonly DropSim Ground;
        public readonly Decay Leaves;
        public readonly Fire Flames;
        public readonly Blasts Blasts;
        float _bolt = 20;
        readonly System.Random _dice = new System.Random();
        public readonly Containers Boxes = new Containers();
        public Furnaces Furnaces;
        public Chutes Chutes;
        public readonly WeatherSim Weather = new WeatherSim(Environment.TickCount);
        readonly List<Seen> _seen = new List<Seen>();
        readonly List<(float, float, float)> _bodies = new List<(float, float, float)>();
        bool _open; Vector3Int _box;
        float _sleepTimer;

        public override int Difficulty => _difficulty;

        public LocalLink(VoxelWorld world, Player player, Sky sky, string folder, int seed, int difficulty)
        {
            _world = world; _player = player; _sky = sky; _difficulty = difficulty;
            _file = folder != null ? Path.Combine(folder, "containers.bin") : null;
            if (_file != null) try { Boxes.Load(_file); } catch (Exception e) { Debug.LogWarning("containers unreadable: " + e.Message); }
            Furnaces = new Furnaces(Boxes);
            Furnaces.WakeSaved();
            Furnaces.Changed = (x, y, z, slot, s) => { var at = new Vector3Int(x, y, z); if (_open && at == _box) BoxChanged?.Invoke(at, slot, s); world.Signal?.Wake(x, y, z); };
            Chutes = new Chutes(Boxes, world.Get, (x, z) => world.IsReady(x, z)) { Changed = Furnaces.Changed, Fed = Furnaces.Wake };
            Chutes.LoadSaved();
            Sim = new MobSim(world.Get, seed ^ Environment.TickCount) { Difficulty = difficulty, BiomeAt = world.Gen.BiomeAt };
            Sim.Loaded = (x, z) => world.IsReady(x, z);
            Sim.VillageNear = (x, z) => world.Gen.Villages.Near(x, z, 96);
            Sim.Seed = world.Gen.Seed;
            _mobFile = folder != null ? Path.Combine(folder, "creatures.bin") : null;
            if (_mobFile != null) try { Sim.LoadFile(_mobFile); } catch (Exception e) { Debug.LogWarning("creatures unreadable: " + e.Message); }
            Sim.HitPlayer += (id, dmg, fx, fz, from) => Hurt?.Invoke(dmg, new Vector3(fx, _player.Feet.y, fz), from.Kind);
            Ground = new DropSim(world.Get) { Loaded = (x, z) => world.IsReady(x, z) };
            // What a creature carried falls where it fell; the experience goes to whoever struck the last blow.
            Sim.Died += (m, killer) =>
            {
                foreach (var (item, n) in Sim.Loot(m))
                    if (item == "xp") { if (killer > 0) Gave?.Invoke(item, n, 0); }
                    else Ground.Scatter(item, n, 0, m.X, m.Y + 0.4f, m.Z);
            };
            Sim.Sound += (m, what) => MobSound?.Invoke(m.Kind, what, new Vector3(m.X, m.Y, m.Z));
            // Breaking a chest or opening a satchel hands over what was in it.
            Leaves = new Decay(world.Get, (x, y, z, id) => world.Set(x, y, z, id));
            Leaves.Fell = (x, y, z, d) =>
            {
                var b = Mining.Bonus(d, _dice); if (b != null) Ground.Scatter(b, 1, 0, x + 0.5f, y + 0.4f, z + 0.5f);
                b = Mining.Bonus2(d, _dice); if (b != null) Ground.Scatter(b, 1, 0, x + 0.5f, y + 0.4f, z + 0.5f);
            };
            // Fire and blasts. Fire only spreads in a survival world that isn't peaceful.
            Flames = new Fire(world.Get, (x, y, z, id) => world.Set(x, y, z, id)) { Loaded = (x, z) => world.IsReady(x, z), Spreads = difficulty > 0 && !player.Creative };
            Blasts = new Blasts(world.Get, (x, y, z, id) => world.Set(x, y, z, id)) { Loaded = (x, z) => world.IsReady(x, z) };
            if (world.Growth != null) world.Growth.FoundFire = Flames.Found;
            if (world.Growth != null) world.Growth.Snowing = (x, z) => Weather.State != 0 && world.Gen.BiomeAt(x, z) is Biome b && (b == Biome.Tundra || b == Biome.Taiga || b == Biome.Peaks);
            Ground.Burns = (x, y, z) => (uint)y < Chunk.Height && (world.Get(x, y, z) == Blocks.Fire || Blocks.IsLava(world.Get(x, y, z)));
            Blasts.Broke = (x, y, z, d) =>
            {
                if (_dice.NextDouble() > 0.3) return;
                var (item, n) = Mining.Drop(d);
                if (item != null && n > 0) Ground.Scatter(item, n, 0, x + 0.5f, y + 0.4f, z + 0.5f);
            };
            Blasts.Boom = (x, y, z, power) => { Sim.Blast(x, y, z, power, _seen); Boomed?.Invoke(new Vector3(x, y, z), power); };
            Blasts.Lit = (x, y, z) => MobSound?.Invoke(MobKind.Bloat, "say", new Vector3(x + 0.5f, y + 0.5f, z + 0.5f));
            Sim.Burst += m => Blasts.Explode(m.X, m.Y + 0.6f, m.Z, 3);
            if (world.Signal != null)
            {
                world.Signal.Fired = (x, y, z, dir) => { Machines.Dispense(Boxes, Ground, Sim, world.Get, (bx, by, bz, id) => world.Set(bx, by, bz, id), x, y, z, dir, _dice); MobSound?.Invoke(MobKind.Arrow, "shoot", new Vector3(x + 0.5f, y + 0.5f, z + 0.5f)); };
                world.Signal.Holds = (x, y, z) => Machines.Holds(Boxes, x, y, z);
            }
            Sim.Laid += m => Ground.Toss("egg", 1, 0, m.X, m.Y + 0.2f, m.Z, 0, 1, 0);
            world.Changed += (x, y, z, old, id) =>
            {
                if (id == Blocks.Fire) Flames.Found(x, y, z);
                if (id == Blocks.KegLit) Blasts.Prime(x, y, z);
                if (id == 0 && Decay.IsLog(Blocks.Get(old))) Leaves.LogBroken(x, y, z);
                if (id == Blocks.Chute) Chutes?.Add(x, y, z);
                if (Boxes.Signs.Remove(Containers.Key(x, y, z))) { Boxes.Dirty = true; SignChanged?.Invoke(new Vector3Int(x, y, z), ""); }
                if (!Blocks.Get(old).Container || Blocks.Get(id).Container || !Boxes.Has(x, y, z)) return;
                // A broken chest spills on the ground (a satchel hands its things straight back to the pack).
                foreach (var s in Boxes.Remove(x, y, z))
                    if (s.Empty || Smelting.Hidden(s.Item)) continue;
                    else if (old == Blocks.Satchel) Gave?.Invoke(s.Item, s.Count, s.Wear);
                    else Ground.Scatter(s.Item, s.Count, s.Wear, x + 0.5f, y + 0.4f, z + 0.5f);
            };
        }

        public override void Tick(float dt)
        {
            _seen.Clear();
            _seen.Add(new Seen { Id = 1, X = _player.Feet.x, Y = _player.Feet.y, Z = _player.Feet.z, Sneaking = _player.Sneaking, Creative = _player.Creative, Alive = _player.Health > 0, Sleeping = _player.Asleep, Held = _player.Inv.Held.Item, Key = "me" });
            Sim.Time01 = _sky.Time01;
            Weather.Tick(dt);
            Furnaces.Tick(dt);
            Chutes.Tick(dt);
            _bodies.Clear();
            _bodies.Add((_player.Feet.x, _player.Feet.y, _player.Feet.z));
            foreach (var m in Sim.All) if (m.Dead <= 0 && m.Kind != MobKind.Arrow && !m.Def.Vehicle) _bodies.Add((m.X, m.Y, m.Z));
            _world.Signal?.Tick(dt, _bodies);
            _sky.Weather = Sim.Weather = Weather.State;
            Sim.Tick(dt, _seen);
            Ground.Tick(dt);
            Leaves.Tick(dt);
            Flames.Tick(dt);
            Blasts.Tick(dt);
            // A thunderstorm now and then sets something alight.
            if (Weather.State == 2 && Flames.Spreads && (_bolt -= dt) <= 0)
            {
                _bolt = 25 + (float)_dice.NextDouble() * 50;
                int bx = Mathf.FloorToInt(_player.Feet.x) + _dice.Next(-40, 41), bz = Mathf.FloorToInt(_player.Feet.z) + _dice.Next(-40, 41);
                if (_world.IsReady(bx, bz))
                    for (int y = Chunk.Height - 2; y > 1; y--)
                    {
                        ushort id = _world.Get(bx, y, bz);
                        if (id == 0) continue;
                        if (Blocks.Get(id).Burns) Flames.Light(bx, y + 1, bz);
                        break;
                    }
            }
            Drops.Clear();
            foreach (var d in Ground.All) Drops.Add(new DropState { Id = d.Id, Item = d.Item, Count = d.Count, Pos = new Vector3(d.X, d.Y, d.Z) });
            Mobs.Clear();
            foreach (var m in Sim.All)
                Mobs.Add(new MobState { Id = m.Id, Kind = m.Kind, Pos = new Vector3(m.X, m.Y, m.Z), Yaw = m.Yaw, Pitch = m.Pitch, Flags = m.Flags, Health = m.Health / m.Def.Health, More = m.More });
            // Asleep at night: after a moment the night passes.
            if (_player.Asleep)
            {
                _sleepTimer += dt;
                if (_sleepTimer > 2.2f)
                {
                    _sleepTimer = 0;
                    if (_sky.Time01 >= 0.77f) _sky.Day++;
                    _sky.Time01 = 0.25f;
                    Sleepers?.Invoke(0, 1);
                    Said?.Invoke("Good morning!");
                }
            }
            else _sleepTimer = 0;
        }

        public override void Strike(int mob, float damage) { var f = _player.Feet; Sim.Strike(mob, 1, damage, f.x, f.y, f.z); }
        public override void Shoot(Vector3 from, Vector3 v, float damage) => Sim.Shoot(1, from.x, from.y, from.z, v.x, v.y, v.z, damage);
        public override void UseMob(int mob, string item)
        {
            var got = Sim.Use(mob, item, _player.Feet.x, _player.Feet.z, "me");
            if (got is (string it, int n)) Gave?.Invoke(it, n, 0);
        }

        public override void Toss(Stack s, Vector3 from, Vector3 v) => Ground.Toss(s.Item, s.Count, s.Wear, from.x, from.y, from.z, v.x, v.y, v.z, 1.2f);
        public override void Spill(Vector3 at, Stack s) => Ground.Scatter(s.Item, s.Count, s.Wear, at.x, at.y, at.z);
        public override void TakeDrop(int id)
        {
            var f = _player.Feet;
            if (Ground.Take(id, f.x, f.y, f.z) is (string item, int count, int wear)) Gave?.Invoke(item, count, wear);
        }

        public override void OpenBox(Vector3Int at)
        {
            // A dusty chest fills itself the first time it's opened, and is an ordinary chest from then on.
            if (_world.Get(at.x, at.y, at.z) == Blocks.LootChest)
            {
                var loot = Ruins.Roll(_world.Gen.Seed, at.x, at.y, at.z);
                _world.Set(at.x, at.y, at.z, Blocks.Chest);
                for (int i = 0; i < loot.Length; i++) if (!loot[i].Empty) Boxes.Set(at.x, at.y, at.z, i, loot[i]);
            }
            _open = true; _box = at;
            BoxOpened?.Invoke(at, Boxes.At(at.x, at.y, at.z));
        }

        public override void BoxSlot(Vector3Int at, int slot, Stack s)
        {
            if (!_open || at != _box) return;
            bool furnace = Blocks.Get(_world.Get(at.x, at.y, at.z)).BaseId == Blocks.Furnace;
            if (furnace && slot > Smelting.Out) return;
            if (_world.Get(at.x, at.y, at.z) == Blocks.Chute && slot >= Chutes.Slots) return;
            Boxes.Set(at.x, at.y, at.z, slot, s);
            if (furnace) Furnaces.Wake(at.x, at.y, at.z);
            _world.Signal?.Wake(at.x, at.y, at.z);          // a comparator beside it looks again
        }
        public override void CloseBox() => _open = false;

        public override void Sleep(bool on) { _sleepTimer = 0; Sleepers?.Invoke(on ? 1 : 0, 1); }

        public override void Drop(Vector3 at, Stack[] stacks)
        {
            bool any = false; foreach (var s in stacks) if (!s.Empty) any = true;
            if (!any) return;
            // One satchel is topped up, a full one gets a neighbour, and what finds no room comes back to the pack.
            var rest = Boxes.Stash(_world.Get, (x, y, z) => _world.Set(x, y, z, Blocks.Satchel), Mathf.FloorToInt(at.x), Mathf.FloorToInt(at.y), Mathf.FloorToInt(at.z), stacks);
            foreach (var s in rest) Gave?.Invoke(s.Item, s.Count, s.Wear);          // no ground for these: back to the player, who keeps them waiting
        }

        public override void SetSign(Vector3Int at, string text)
        {
            Boxes.SetSign(at.x, at.y, at.z, text);
            SignChanged?.Invoke(at, Containers.CleanSign(text));
        }

        // Tell the sign drawer about every sign in the world (once, on loading).
        public void ShowSigns()
        {
            foreach (var kv in Boxes.Signs) { var (x, y, z) = Containers.Unkey(kv.Key); SignChanged?.Invoke(new Vector3Int(x, y, z), kv.Value); }
        }

        public override void Fertilize(Vector3Int at) => _world.Growth?.BoneMeal(at.x, at.y, at.z);
        public override void Mount(int mob)
        {
            var f = _player.Feet;
            var k = Sim.Mount(mob, f.x, f.y, f.z, out float x, out float y, out float z, out float yaw);
            if (k != null) Mounted?.Invoke(k.Value, new Vector3(x, y, z), yaw);
        }
        public override void Park(MobKind kind, Vector3 at, float yaw) => Sim.Park(kind, at.x, at.y, at.z, yaw, "me");

        public override void Save()
        {
            if (_mobFile != null) try { Sim.SaveFile(_mobFile); } catch (Exception e) { Debug.LogWarning("could not save creatures: " + e.Message); }
            if (_file == null || !Boxes.Dirty) return;
            try { Boxes.Save(_file); } catch (Exception e) { Debug.LogWarning("could not save containers: " + e.Message); }
        }
    }
}
