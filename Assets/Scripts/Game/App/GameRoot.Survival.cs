using System;
using System.Collections.Generic;
using Stratum.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Stratum.Game
{
    // Creatures, chests, beds, hunger, the satchel left where you fall, and the chat commands, the same in a
    // single-player world (LocalLink) and on a server (RemoteLink).
    public sealed partial class GameRoot
    {
        Link _link;
        Creatures _creatures;
        SignView _signs;
        int _difficulty = 2;
        bool _hasBed; Vector3Int _bed;
        float _sleptFor;

        public static readonly string[] DifficultyNames = { "Peaceful", "Easy", "Normal" };

        void SetupSurvival(Link link, int difficulty)
        {
            _link = link; _difficulty = Mathf.Clamp(difficulty, 0, 2);
            _player.Difficulty = _difficulty;
            _player.Toast = s => _hud?.Toast(s);
            _creatures = Creatures.Create(_world, _cam.transform);
            _signs = SignView.Create(_world);
            link.SignChanged = (at, text) => _signs.Set(at, text);
            _act.EditSign = at =>
            {
                _hud.EditSign(at, _signs.TextAt(at), text => link.SetSign(at, text));
                _signWasOpen = true;
                _player.InputEnabled = false; _act.InputEnabled = false;
                SetCursor(false);
            };
            if (link is LocalLink ll) ll.ShowSigns();
            _act.Link = link; _act.Creatures = _creatures;
            _act.OpenBox = at => link.OpenBox(at);
            _act.UseBed = TrySleep;
            _act.Toast = s => _hud?.Toast(s);
            _hud.Link = link;
            _hud.CloseBoxed = () => link.CloseBox();
            link.BoxOpened = (at, stacks) =>
            {
                if (_state != State.Playing || _hud.ChatOpen) { link.CloseBox(); return; }
                _hud.OpenBox(at, stacks);
                _player.InputEnabled = false; _act.InputEnabled = false;
                SetCursor(false);
            };
            link.BoxChanged = (at, slot, s) => _hud.BoxChanged(at, slot, s);
            link.Gave = Give;
            link.Hurt = (dmg, from, kind) =>
            {
                if (_player.Asleep) WakeUp();
                _player.Hit(dmg, from);
            };
            link.Sleepers = (asleep, total) =>
            {
                if (!_player.Asleep) return;
                _hud.Sleeping(true, total > 1 ? $"Sleeping...  {asleep}/{total} players in bed" : "Sleeping...");
            };
            link.MobSound = MobSfx;
            link.Said = s => { _hud.Toast(s); if (_hud.CanChat) _hud.AddChat("", s); };
        }

        void TeardownSurvival()
        {
            if (_creatures != null) Destroy(_creatures.gameObject);
            if (_signs != null) Destroy(_signs.gameObject);
            _signs = null;
            _creatures = null; _link = null;
        }

        // Each frame while in a world.
        void TickSurvival(Keyboard kb)
        {
            if (_link == null) return;
            _link.Tick(Time.deltaTime);
            _creatures.Show(_link.Mobs, Time.deltaTime);
            if (_player.Asleep)
            {
                _sleptFor += Time.deltaTime;
                bool day = _sky.Time01 > 0.23f && _sky.Time01 < 0.77f;
                bool leave = kb != null && (kb.spaceKey.wasPressedThisFrame || kb.leftShiftKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame);
                if (leave || day && _sleptFor > 1 || !BedAt(_bed)) WakeUp();
                else _player.Velocity = Vector3.zero;
            }
        }

        // Things handed over by the host (loot, a chest's contents, wool): into the pack, or onto the ground.
        void Give(string item, int count, int wear)
        {
            if (_player == null || Items.Get(item) == null || count <= 0) return;
            int left = count;
            if (wear > 0)
            {
                for (int k = 0; k < Inventory.Main; k++) if (_player.Inv.Slots[k].Empty) { _player.Inv.Slots[k] = new Core.Stack(item, count) { Wear = wear }; left = 0; break; }
            }
            else left = _player.Inv.Add(item, count);
            if (left < count) Sfx.I?.Play("pickup", 0.4f, 1.1f + UnityEngine.Random.value * 0.3f);
            if (left > 0) _link?.Drop(_player.Feet, new[] { new Core.Stack(item, left) { Wear = wear } });
        }

        static readonly Dictionary<MobKind, string> Voices = new Dictionary<MobKind, string>
        {
            { MobKind.Cow, "moo" }, { MobKind.Pig, "oink" }, { MobKind.Sheep, "baa" }, { MobKind.Chicken, "cluck" },
            { MobKind.Ghoul, "groan" }, { MobKind.BoneArcher, "rattle" }, { MobKind.Crawler, "hiss" },
            { MobKind.EmberImp, "imp" }, { MobKind.Colossus, "roar" }, { MobKind.Villager, "villager" },
        };

        void MobSfx(MobKind kind, string what, Vector3 at)
        {
            if (Sfx.I == null) return;
            Voices.TryGetValue(kind, out var voice);
            float big = kind == MobKind.Colossus ? 1.6f : 1;
            switch (what)
            {
                case "say": if (voice != null) Sfx.I.PlayAt(voice, at, 0.8f * big, 0.9f + UnityEngine.Random.value * 0.2f); break;
                case "hurt":
                    Sfx.I.PlayAt("hit", at, 0.6f, 1);
                    if (voice != null) Sfx.I.PlayAt(voice, at, 0.7f * big, 1.25f + UnityEngine.Random.value * 0.15f);
                    break;
                case "die": if (voice != null) Sfx.I.PlayAt(voice, at, 0.9f * big, 0.7f); break;
                case "shoot": Sfx.I.PlayAt(kind == MobKind.EmberImp ? "sizzle" : "bow", at, 0.7f, 1); break;
                case "stick": Sfx.I.PlayAt("arrow", at, 0.6f, 1); break;
            }
        }

        // ------------------------------------------------------------------ beds

        bool BedAt(Vector3Int p) => Blocks.Get(_world.Get(p.x, p.y, p.z)).BaseId == Blocks.Bed;

        void TrySleep(Vector3Int foot)
        {
            if (_player.Asleep) return;
            bool changed = !_hasBed || _bed != foot;
            _hasBed = true; _bed = foot;
            bool night = _sky.Time01 >= 0.77f || _sky.Time01 <= 0.23f;
            if (!night) { _hud.Toast(changed ? "Respawn point set. You can only sleep at night." : "You can only sleep at night"); SendKeepSoon(); return; }
            if ((_player.Feet - (foot + new Vector3(0.5f, 0, 0.5f))).sqrMagnitude > 3.5f * 3.5f) { _hud.Toast("You're too far away from the bed"); return; }
            foreach (var m in _link.Mobs)
                if (MobSim.Def(m.Kind).Hostile && (m.Flags & Mob.FDead) == 0 && (m.Pos - _player.Feet).sqrMagnitude < 8 * 8)
                { _hud.Toast("You may not rest now; there are monsters nearby"); return; }
            if (changed) _hud.Toast("Respawn point set");
            _player.Asleep = true;
            _sleptFor = 0;
            _player.Feet = new Vector3(foot.x + 0.5f, foot.y + 0.5625f, foot.z + 0.5f);
            _player.Velocity = Vector3.zero;
            _player.InputEnabled = false; _act.InputEnabled = false;
            _hud.Sleeping(true, "Sleeping...");
            _link.Sleep(true);
            SendKeepSoon();
            SendMove(true);
        }

        void WakeUp()
        {
            if (!_player.Asleep) return;
            _player.Asleep = false;
            _link?.Sleep(false);
            _hud.Sleeping(false);
            // Up beside the bed rather than in it.
            var spot = BesideBed(_bed) ?? _player.Feet + Vector3.up * 0.5f;
            _player.Feet = spot;
            if (_state == State.Playing && !_hud.InventoryOpen) { _player.InputEnabled = true; _act.InputEnabled = true; }
            SendMove(true);
        }

        // A spot for a body to stand next to a bed: two clear blocks over something solid.
        Vector3? BesideBed(Vector3Int foot)
        {
            for (int r = 1; r <= 2; r++)
                for (int dy = 0; dy <= 1; dy++)
                    for (int dz = -r; dz <= r; dz++)
                        for (int dx = -r; dx <= r; dx++)
                        {
                            int x = foot.x + dx, y = foot.y + dy, z = foot.z + dz;
                            if (Blocks.Get(_world.Get(x, y, z)).Solid || Blocks.Get(_world.Get(x, y + 1, z)).Solid) continue;
                            if (!Blocks.Get(_world.Get(x, y - 1, z)).Solid) continue;
                            var b = Blocks.Get(_world.Get(x, y - 1, z));
                            if (b.BaseId == Blocks.Bed) continue;
                            return new Vector3(x + 0.5f, y + 0.01f, z + 0.5f);
                        }
            return null;
        }

        void SendKeepSoon() { if (Online) _keepTimer = Mathf.Min(_keepTimer, 0.2f); }

        // ------------------------------------------------------------------ dying and coming back

        // Everything carried goes into a satchel where you fell.
        void DropAll()
        {
            if (_player.Creative || _link == null) return;
            var all = new List<Core.Stack>();
            for (int i = 0; i < Inventory.Size; i++)
                if (!_player.Inv.Slots[i].Empty) { all.Add(_player.Inv.Slots[i]); _player.Inv.Slots[i] = Core.Stack.None; }
            if (all.Count > 0) _link.Drop(_player.Feet + Vector3.up * 0.5f, all.ToArray());
        }

        // Where to come back: the bed if it's still there and clear, else the world's spawn.
        Vector3 RespawnPoint(Vector3 worldSpawn, out bool atBed)
        {
            atBed = false;
            if (_hasBed)
            {
                if (BedAt(_bed))
                {
                    var spot = BesideBed(_bed);
                    if (spot != null) { atBed = true; return spot.Value; }
                }
                _hud.Toast("Your bed was missing or blocked");
                if (!BedAt(_bed)) _hasBed = false;
            }
            return worldSpawn;
        }

        // ------------------------------------------------------------------ chat commands

        // Commands everyone can use; anything else starting with "/" goes to the server (or is unknown here).
        bool Command(string text)
        {
            var parts = text.Trim().Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return false;
            string cmd = parts[0].ToLowerInvariant(), arg = parts.Length > 1 ? parts[1].Trim() : "";
            switch (cmd)
            {
                case "/help":
                case "/?":
                    _hud.AddChat("", "Commands: /spawn  /home  /tp <name>  /list  /day  /night (single player)");
                    return true;
                case "/spawn":
                    Teleport(Online ? GroundAt(_netSpawn) : new Vector3(_meta.sx, _meta.sy + 0.01f, _meta.sz), "Back at the spawn");
                    return true;
                case "/home":
                case "/bed":
                    if (!_hasBed) { _hud.AddChat("", "You haven't slept in a bed yet. Right-click one to set your home."); return true; }
                    var spot = BedAt(_bed) ? BesideBed(_bed) : null;
                    if (spot == null) { _hud.AddChat("", "Your bed is missing or blocked."); return true; }
                    Teleport(spot.Value, "Home");
                    return true;
                case "/tp":
                case "/tpa":
                    if (!Online || _avatars == null) { _hud.AddChat("", "There's nobody else here to go to."); return true; }
                    if (arg.Length == 0) { _hud.AddChat("", "Who to? /tp <name>  (online: " + string.Join(", ", _avatars.Names) + ")"); return true; }
                    foreach (int id in _avatars.Ids)
                    {
                        var n = _avatars.NameOf(id);
                        if (n == null || !n.StartsWith(arg, StringComparison.OrdinalIgnoreCase)) continue;
                        if (_avatars.TryGet(id, out var at)) { Teleport(at + Vector3.up * 0.05f, "Went to " + n); _net?.Send(new Packet(Net.Msg.Chat).Str("/tpnote " + n)); }
                        return true;
                    }
                    _hud.AddChat("", $"Nobody called \"{arg}\" is online.");
                    return true;
                case "/day":
                case "/night":
                    if (Online) return false;
                    _sky.Time01 = cmd == "/day" ? 0.27f : 0.8f;
                    _hud.AddChat("", cmd == "/day" ? "It's morning." : "Night falls.");
                    return true;
            }
            return false;
        }

        void Teleport(Vector3 to, string what)
        {
            if (_player.Asleep) WakeUp();
            _player.Feet = to;
            _player.Velocity = Vector3.zero;
            _player.ResetFall();
            _world.SetFocus(to);
            SendMove(true);
            _hud.Toast(what);
            Sfx.I?.Play("pickup", 0.5f, 0.6f);
        }

        // Chat typed in a single-player world: only commands mean anything.
        void LocalChat(string text)
        {
            if (text.StartsWith("/")) { if (!Command(text)) _hud.AddChat("", "Unknown command. Try /help"); }
            else _hud.AddChat("You", text);
        }
    }

    // On a server: everything goes there as messages; GameRoot.Handle passes what comes back to the events.
    public sealed class RemoteLink : Link
    {
        readonly Func<NetClient> _net;
        readonly int _difficulty;
        public RemoteLink(Func<NetClient> net, int difficulty) { _net = net; _difficulty = difficulty; }
        public override int Difficulty => _difficulty;

        void Send(Packet p) => _net()?.Send(p);
        public override void Strike(int mob, float damage) => Send(new Packet(Net.Msg.Strike).I32(mob).F32(damage));
        public override void Shoot(Vector3 o, Vector3 v, float damage) => Send(new Packet(Net.Msg.Shoot).F32(o.x).F32(o.y).F32(o.z).F32(v.x).F32(v.y).F32(v.z).F32(damage));
        public override void UseMob(int mob, string item) => Send(new Packet(Net.Msg.UseMob).I32(mob).Str(item ?? ""));
        public override void OpenBox(Vector3Int at) => Send(new Packet(Net.Msg.OpenBox).I32(at.x).I32(at.y).I32(at.z));
        public override void BoxSlot(Vector3Int at, int slot, Core.Stack s) => Send(new Packet(Net.Msg.BoxSlot).I32(at.x).I32(at.y).I32(at.z).U8((byte)slot).Slot(s));
        public override void CloseBox() => Send(new Packet(Net.Msg.CloseBox));
        public override void Sleep(bool on) => Send(new Packet(Net.Msg.Sleep).Bool(on));
        public override void Drop(Vector3 at, Core.Stack[] stacks) => Send(new Packet(Net.Msg.Drop).F32(at.x).F32(at.y).F32(at.z).Stacks(stacks));
        public override void Fertilize(Vector3Int at) => Send(new Packet(Net.Msg.Fertilize).I32(at.x).I32(at.y).I32(at.z));
        public override void SetSign(Vector3Int at, string text) => Send(new Packet(Net.Msg.SignText).I32(at.x).I32(at.y).I32(at.z).Str(Containers.CleanSign(text)));

        // The creatures near us, as the server last saw them.
        public void Received(Reader r)
        {
            Mobs.Clear();
            int n = r.U16();
            for (int i = 0; i < n; i++)
            {
                int id = r.I32(); var kind = (MobKind)r.U8();
                var pos = new Vector3(r.F32(), r.F32(), r.F32());
                float yaw = r.U8() / 256f * 360f, pitch = (sbyte)r.U8();
                byte flags = r.U8(); float health = r.U8() / 255f; byte more = r.U8();
                if (kind >= MobKind.Count) continue;
                Mobs.Add(new MobState { Id = id, Kind = kind, Pos = pos, Yaw = yaw, Pitch = pitch, Flags = flags, Health = health, More = more });
            }
        }
    }
}
