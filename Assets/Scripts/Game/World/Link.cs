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
        public readonly Containers Boxes = new Containers();
        readonly List<Seen> _seen = new List<Seen>();
        bool _open; Vector3Int _box;
        float _sleepTimer;

        public override int Difficulty => _difficulty;

        public LocalLink(VoxelWorld world, Player player, Sky sky, string folder, int seed, int difficulty)
        {
            _world = world; _player = player; _sky = sky; _difficulty = difficulty;
            _file = folder != null ? Path.Combine(folder, "containers.bin") : null;
            if (_file != null) try { Boxes.Load(_file); } catch (Exception e) { Debug.LogWarning("containers unreadable: " + e.Message); }
            Sim = new MobSim(world.Get, seed ^ Environment.TickCount) { Difficulty = difficulty, BiomeAt = world.Gen.BiomeAt };
            Sim.Loaded = (x, z) => world.IsReady(x, z);
            _mobFile = folder != null ? Path.Combine(folder, "creatures.bin") : null;
            if (_mobFile != null) try { Sim.LoadFile(_mobFile); } catch (Exception e) { Debug.LogWarning("creatures unreadable: " + e.Message); }
            Sim.HitPlayer += (id, dmg, fx, fz, from) => Hurt?.Invoke(dmg, new Vector3(fx, _player.Feet.y, fz), from.Kind);
            Sim.Died += (m, killer) => { if (killer > 0) foreach (var (item, n) in Sim.Loot(m)) Gave?.Invoke(item, n, 0); };
            Sim.Sound += (m, what) => MobSound?.Invoke(m.Kind, what, new Vector3(m.X, m.Y, m.Z));
            // Breaking a chest or opening a satchel hands over what was in it.
            world.Changed += (x, y, z, old, id) =>
            {
                if (Boxes.Signs.Remove(Containers.Key(x, y, z))) { Boxes.Dirty = true; SignChanged?.Invoke(new Vector3Int(x, y, z), ""); }
                if (!Blocks.Get(old).Container || Blocks.Get(id).Container || !Boxes.Has(x, y, z)) return;
                foreach (var s in Boxes.Remove(x, y, z)) if (!s.Empty) Gave?.Invoke(s.Item, s.Count, s.Wear);
            };
        }

        public override void Tick(float dt)
        {
            _seen.Clear();
            _seen.Add(new Seen { Id = 1, X = _player.Feet.x, Y = _player.Feet.y, Z = _player.Feet.z, Sneaking = _player.Sneaking, Creative = _player.Creative, Alive = _player.Health > 0, Sleeping = _player.Asleep, Held = _player.Inv.Held.Item });
            Sim.Time01 = _sky.Time01;
            Sim.Tick(dt, _seen);
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
            var got = Sim.Use(mob, item, _player.Feet.x, _player.Feet.z);
            if (got is (string it, int n)) Gave?.Invoke(it, n, 0);
        }

        public override void OpenBox(Vector3Int at)
        {
            _open = true; _box = at;
            BoxOpened?.Invoke(at, Boxes.At(at.x, at.y, at.z));
        }

        public override void BoxSlot(Vector3Int at, int slot, Stack s) { if (_open && at == _box) Boxes.Set(at.x, at.y, at.z, slot, s); }
        public override void CloseBox() => _open = false;

        public override void Sleep(bool on) { _sleepTimer = 0; Sleepers?.Invoke(on ? 1 : 0, 1); }

        public override void Drop(Vector3 at, Stack[] stacks)
        {
            bool any = false; foreach (var s in stacks) if (!s.Empty) any = true;
            if (!any) return;
            var (x, y, z) = Containers.SpotFor(_world.Get, Mathf.FloorToInt(at.x), Mathf.FloorToInt(at.y), Mathf.FloorToInt(at.z));
            var all = new List<Stack>();
            if (Blocks.Get(_world.Get(x, y, z)).Container) foreach (var s in Boxes.At(x, y, z)) if (!s.Empty) all.Add(s);
            foreach (var s in stacks) if (!s.Empty) all.Add(s);
            Boxes.Put(x, y, z, all.ToArray());
            _world.Set(x, y, z, Blocks.Satchel);
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

        public override void Save()
        {
            if (_mobFile != null) try { Sim.SaveFile(_mobFile); } catch (Exception e) { Debug.LogWarning("could not save creatures: " + e.Message); }
            if (_file == null || !Boxes.Dirty) return;
            try { Boxes.Save(_file); } catch (Exception e) { Debug.LogWarning("could not save containers: " + e.Message); }
        }
    }
}
