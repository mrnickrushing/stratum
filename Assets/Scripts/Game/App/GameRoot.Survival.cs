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
        readonly List<MobState> _shown = new List<MobState>();
        Creatures _creatures;
        SignView _signs;
        WeatherFx _weatherFx;
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
            _dropViews = DropViews.Create(_world, _blockMat);
            _signs = SignView.Create(_world);
            _weatherFx = WeatherFx.Create(_world, _sky, _cam.transform, _player);
            _weatherFx.Thundered = () => _hud?.Caption("Thunder");
            _link.Mounted = (k, at, yaw) => _player.GetIn(k, at, yaw);
            _player.GotOut = (k, at, yaw) => _link.Park(k, at, yaw);
            _player.Powered = (x, y, z) => Signal.BoosterPowered(_world.Get, x, y, z);
            _act.OpenTrade = (id, job) => { _hud.SetTrade(job, id); OpenInventory("trade"); };
            _hud.SpawnPoint = () => Online ? _netSpawn : new Vector3(_meta.sx, _meta.sy, _meta.sz);
            _hud.Others = () =>
            {
                var l = new List<(string, Vector3)>();
                if (_avatars != null) foreach (int id in _avatars.Ids) if (_avatars.TryGet(id, out var at)) l.Add((_avatars.NameOf(id) ?? "?", at));
                return l;
            };
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
            _player.Trampled = (at, def) => _act.DropFor(def, at);
            // A gate stone carries whoever touches it to the Quiet, and the one there carries them home (their bed, or the spawn).
            _act.Travel = () =>
            {
                if (_player.Riding != null) _player.GetOut();
                Sfx.I?.Play("portal", 0.7f, 0.9f);
                if (Quiet.In(_player.Feet.x))
                {
                    var bedSpot = _hasBed && BedAt(_bed) ? BesideBed(_bed) : null;
                    Teleport(bedSpot ?? (Online ? GroundAt(_netSpawn) : new Vector3(_meta.sx, _meta.sy + 0.01f, _meta.sz)), "Home again");
                }
                else { var l = Quiet.Landing; Teleport(new Vector3(l.x, l.y + 0.05f, l.z), "The Quiet"); }
            };
            _act.Toast = s => _hud?.Toast(s);
            _act.Spill = Give;                 // mined things that don't fit the pack: the ground, or the waiting list
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
                float before = _player.Health;
                _player.Hit(dmg, from);
                // A crawler's bite that gets through leaves poison behind.
                if (kind == MobKind.Crawler && _player.Health < before && _difficulty >= 2) _player.AddFx(Player.Fx.Poison, 6);
            };
            link.Sleepers = (asleep, total) =>
            {
                if (!_player.Asleep) return;
                _hud.Sleeping(true, total > 1 ? $"Sleeping...  {asleep}/{total} players in bed" : "Sleeping...");
            };
            link.MobSound = MobSfx;
            link.Boomed = (at, power) =>
            {
                Sfx.I?.PlayAt("thunder", at, 1f, 1.5f + UnityEngine.Random.value * 0.3f);
                Sfx.I?.PlayAt("hit_hard", at, 1f, 0.6f);
                _hud?.Caption("Blast");
                _act.Puff(at, power);
            };
            link.Said = s => { _hud.Toast(s); if (_hud.CanChat) _hud.AddChat("", s); };
        }

        void TeardownSurvival()
        {
            _waiting.Clear(); _waitTold = false;          // saved with the player before this; the next world has its own
            if (_creatures != null) Destroy(_creatures.gameObject);
            if (_dropViews != null) Destroy(_dropViews.gameObject);
            if (_self != null) Destroy(_self.gameObject);
            _self = null; if (_player != null) _player.View = 0;
            _dropViews = null; _askedDrops.Clear();
            if (_signs != null) Destroy(_signs.gameObject);
            if (_weatherFx != null) Destroy(_weatherFx.gameObject);
            _sky.Weather = 0;
            _signs = null;
            _creatures = null; _link = null;
        }

        // Each frame while in a world.
        void TickSurvival(Keyboard kb)
        {
            if (_link == null) return;
            _link.Tick(Time.deltaTime);
            _shown.Clear(); _shown.AddRange(_link.Mobs);
            if (_player.Riding != null) _shown.Add(new MobState { Id = -1, Kind = _player.Riding.Value, Pos = _player.Feet, Yaw = _player.RideYaw, Health = 1, More = Mob.MTame });
            _avatars?.Rides(_shown);
            _creatures.Show(_shown, Time.deltaTime);
            _dropViews.Day = _sky.DayFactor;
            _dropViews.Show(_link.Drops, Time.deltaTime);
            PickUp(kb);
            if (_player.Asleep)
            {
                _sleptFor += Time.deltaTime;
                bool day = _sky.Time01 > 0.23f && _sky.Time01 < 0.77f;
                bool leave = kb != null && (kb.spaceKey.wasPressedThisFrame || kb.leftShiftKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame);
                if (leave || day && _sleptFor > 1 || !BedAt(_bed)) WakeUp();
                else _player.Velocity = Vector3.zero;
            }
        }

        DropViews _dropViews;
        Avatars _self;                               // our own figure, for the views from outside (F5)

        void SelfView()
        {
            if (_player == null || _state != State.Playing) return;
            if (_player.InputEnabled && !_hud.ChatOpen && Keys.Pressed(Act.View)) _player.View = (_player.View + 1) % 3;
            bool show = _player.View != 0 && _player.Health > 0;
            if (show && _self == null)
            {
                _self = Avatars.Create(_world, _sky, _cam);
                _self.Add(0, "", _player.Feet, _player.Yaw, Extras.Outfit, Extras.Outfit == Extras.Custom ? CustomLook.Mine.Pack() : null);
            }
            if (_self == null) return;
            _self.gameObject.SetActive(show);
            if (!show) return;
            byte flags = 0;
            if (_player.Sneaking) flags |= Net.Sneaking;
            if (_player.Flying) flags |= Net.Flying;
            if (_player.InWater) flags |= Net.InWater;
            if (_act.Swinging) flags |= Net.Swinging;
            _self.Moved(0, _player.Feet, _player.Yaw, _player.Pitch, flags);
        }
        readonly Dictionary<int, float> _askedDrops = new Dictionary<int, float>();     // drops we asked for, and when

        // Walking over something that fits in the pack picks it up; Q throws what's in the hand (Ctrl: the whole stack).
        void PickUp(Keyboard kb)
        {
            if (_player.Health <= 0 || _state != State.Playing) return;
            float now = Time.unscaledTime;
            var body = _player.Feet + Vector3.up * 0.9f;
            foreach (var d in _link.Drops)
            {
                if ((d.Pos - body).sqrMagnitude > DropSim.PickUp * DropSim.PickUp) continue;
                if (_askedDrops.TryGetValue(d.Id, out float t) && now - t < 0.4f) continue;
                if (!Fits(d.Item, d.Count)) continue;
                _askedDrops[d.Id] = now;
                _link.TakeDrop(d.Id);
            }
            if (_askedDrops.Count > 200) _askedDrops.Clear();
            if (kb != null && _player.InputEnabled && !_hud.ChatOpen && Keys.Pressed(Act.Drop))
            {
                var held = _player.Inv.Held;
                if (held.Empty) return;
                bool all = kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed;
                var thrown = held; if (!all) thrown.Count = 1;
                _player.Inv.UseHeld(thrown.Count);
                var fwd = _cam.transform.forward;
                _link.Toss(thrown, _player.EyePos - Vector3.up * 0.25f + fwd * 0.3f, fwd * 6.5f + Vector3.up * 1.6f + _player.Velocity * 0.5f);
                Sfx.I?.Play("swish", 0.3f, 1.2f);
            }
        }

        // Is there room in the pack for the whole of this?
        bool Fits(string item, int count)
        {
            var def = Items.Get(item);
            if (def == null) return false;
            var inv = _player.Inv;
            for (int i = 0; i < Inventory.Main; i++)
            {
                var s = inv.Slots[i];
                if (s.Empty) return true;
                if (s.Item == item && s.Wear == 0 && def.MaxStack > 1) { count -= def.MaxStack - s.Count; if (count <= 0) return true; }
            }
            return false;
        }

        // Things handed over by the host (loot, a chest's contents, wool): into the pack, or onto the ground.
        void Give(string item, int count, int wear)
        {
            if (_player == null || Items.Get(item) == null || count <= 0) return;
            if (item == "xp") { _player.AddXp(count); return; }
            int left = count;
            if (wear > 0)
            {
                for (int k = 0; k < Inventory.Main; k++) if (_player.Inv.Slots[k].Empty) { _player.Inv.Slots[k] = new Core.Stack(item, count) { Wear = wear }; left = 0; break; }
            }
            else left = _player.Inv.Add(item, count);
            if (left < count) Sfx.I?.Play("pickup", 0.4f, 1.1f + UnityEngine.Random.value * 0.3f);
            if (left > 0) Wait(new Core.Stack(item, left) { Wear = wear });
        }

        // ------------------------------------------------------------------ things waiting for room
        // Whatever fits neither the pack nor the ground (the host hands back what it found no place for) waits
        // here. It is saved with the player, tried in the pack every couple of seconds, and put on the ground again
        // only after a pause or once the player has moved on, so a full pack on crowded ground can't bounce the
        // same things back and forth.
        readonly List<Core.Stack> _waiting = new List<Core.Stack>();
        float _waitPack, _waitDrop; Vector3 _waitDropAt; bool _waitTold;

        void Wait(Core.Stack s)
        {
            if (s.Empty) return;
            var joined = Containers.Compact(System.Linq.Enumerable.Concat(_waiting, new[] { s }));
            _waiting.Clear(); _waiting.AddRange(joined);
        }

        void TickWaiting(float dt)
        {
            if (_waiting.Count == 0 || _player == null || _link == null) { _waitTold = false; return; }
            if ((_waitPack -= dt) <= 0)
            {
                _waitPack = 2;
                for (int i = _waiting.Count - 1; i >= 0; i--)
                {
                    var s = _waiting[i]; int left = s.Count;
                    if (s.Wear > 0) { for (int k = 0; k < Inventory.Main; k++) if (_player.Inv.Slots[k].Empty) { _player.Inv.Slots[k] = s; left = 0; break; } }
                    else left = _player.Inv.Add(s.Item, s.Count);
                    if (left <= 0) _waiting.RemoveAt(i); else { s.Count = left; _waiting[i] = s; }
                }
                if (_waiting.Count == 0) return;
            }
            _waitDrop -= dt;
            bool moved = (_player.Feet - _waitDropAt).sqrMagnitude > 9;
            if (_waitDrop > 0 && !moved) return;
            // Onto the ground. If there's no room there either, the host hands them back and they wait again.
            _waitDrop = 10; _waitDropAt = _player.Feet;
            var all = _waiting.ToArray(); _waiting.Clear();
            _link.Drop(_player.Feet, all);
            if (!_waitTold) { _waitTold = true; _hud.Toast("Your pack is full: what didn't fit is in a satchel, or waits until there's room"); }
        }

        public int WaitingCount => _waiting.Count;

        static readonly Dictionary<MobKind, string> Voices = new Dictionary<MobKind, string>
        {
            { MobKind.Cow, "moo" }, { MobKind.Pig, "oink" }, { MobKind.Sheep, "baa" }, { MobKind.Chicken, "cluck" },
            { MobKind.Ghoul, "groan" }, { MobKind.BoneArcher, "rattle" }, { MobKind.Crawler, "hiss" },
            { MobKind.EmberImp, "imp" }, { MobKind.Colossus, "roar" }, { MobKind.Villager, "villager" }, { MobKind.Bloat, "sizzle" }, { MobKind.Hound, "bark" }, { MobKind.Glob, "burp" }, { MobKind.Shade, "portal" }, { MobKind.King, "roar" },
        };

        void MobSfx(MobKind kind, string what, Vector3 at)
        {
            if (Sfx.I == null) return;
            Voices.TryGetValue(kind, out var voice);
            float big = kind == MobKind.Colossus || kind == MobKind.King ? 1.6f : 1;
            // Sound captions (Settings > Controls): what made the noise, and which side it came from.
            string who = MobSim.Def(kind).Name;
            if (_hud != null) _hud.Caption(what == "say" ? who + (voice == "moo" || voice == "oink" || voice == "baa" || voice == "cluck" ? " calls" : voice == "villager" ? " mutters" : " growls") : what == "hurt" ? who + " is hurt" : what == "die" ? who + " falls" : what == "shoot" ? who + " shoots" : null, at);
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
            for (int i = 0; i <= Inventory.OffHand; i++)
                if (!_player.Inv.Slots[i].Empty) { all.Add(_player.Inv.Slots[i]); _player.Inv.Slots[i] = Core.Stack.None; }
            int xp = Xp.Dropped(_player.XpLevel);
            if (xp > 0) all.Add(new Core.Stack("xp", xp));
            _player.XpLevel = 0; _player.XpPoints = 0;
            // Fallen off into the sky: the satchel waits where they last stood.
            var at = _player.Feet.y < 1 ? _player.LastGround : _player.Feet;
            if (all.Count > 0) _link.Drop(at + Vector3.up * 0.5f, all.ToArray());
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
                // Only a bed that can be seen to be gone is forgotten: far from home its piece of the world may
                // simply not be loaded yet.
                bool known = _world.IsReady(_bed.x, _bed.z);
                _hud.Toast(known ? "Your bed was missing or blocked" : "Your home was too far away to reach just now");
                if (known && !BedAt(_bed)) _hasBed = false;
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
                    _hud.AddChat("", "Commands: /spawn  /home  /keep <on|off>  /tp <name>  /list  /weather <clear|rain|thunder>  /day  /night  /time set <day|noon|night|midnight>  /give <thing> [how many]  /gamemode <survival|creative>");
                    return true;
                case "/keep":
                    if (Online) { _hud.AddChat("", _keepInv ? "This world lets you keep your things when you fall." : "In this world your things drop in a satchel when you fall. The server decides."); return true; }
                    if (arg == "on" || arg == "off") { _keepInv = _meta.keepInventory = arg == "on"; Save(); }
                    _hud.AddChat("", _keepInv ? "Keep your things when you fall: on" : "Keep your things when you fall: off  (/keep on to change)");
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
                {
                    // Three numbers: a place rather than a person.
                    var c = arg.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                    if (c.Length == 3 && float.TryParse(c[0], out float tx) && float.TryParse(c[1], out float ty) && float.TryParse(c[2], out float tz))
                    {
                        if (Online) return false;           // the server decides who may
                        if (Mathf.Abs(tx) > 3e6f || Mathf.Abs(tz) > 3e6f || ty < -20 || ty > 400) { _hud.AddChat("", "That's off the edge of the world."); return true; }
                        Teleport(new Vector3(tx, ty, tz), $"Went to {tx:0} {ty:0} {tz:0}");
                        return true;
                    }
                }
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
                case "/weather":
                    if (Online) return false;
                    int w = WeatherSim.Parse(arg.ToLowerInvariant());
                    if (w < 0 || !(_link is LocalLink wl)) { _hud.AddChat("", "/weather clear, /weather rain or /weather thunder"); return true; }
                    wl.Weather.Set(w, w == 0 ? 900 : 480);
                    _hud.AddChat("", w == 0 ? "The weather clears." : w == 2 ? "A thunderstorm rolls in." : "It starts to rain.");
                    return true;
                case "/give":
                {
                    if (Online) return false;               // the server decides who may
                    var ga = arg.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    string what = ga.Length > 0 ? ga[0].ToLowerInvariant() : "";
                    int many = ga.Length > 1 && int.TryParse(ga[1], out int gn) ? Mathf.Clamp(gn, 1, 640) : 1;
                    var gd = FindItem(what);
                    if (gd == null) { _hud.AddChat("", ga.Length == 0 ? "/give <thing> [how many], for example /give torch 16" : $"There's nothing called \"{what}\"."); return true; }
                    Give(gd.Id, many, 0);
                    _hud.AddChat("", $"Given {many} {gd.Name}.");
                    return true;
                }
                case "/gamemode":
                {
                    if (Online) { _hud.AddChat("", "On a server the world's mode is set by whoever runs it."); return true; }
                    string gm = arg.ToLowerInvariant();
                    bool? want = gm == "creative" || gm == "c" || gm == "1" ? true : gm == "survival" || gm == "s" || gm == "0" ? false : (bool?)null;
                    if (want == null) { _hud.AddChat("", "/gamemode survival or /gamemode creative"); return true; }
                    _player.Creative = _meta.creative = want.Value;
                    if (!want.Value) _player.Flying = false;
                    Save();
                    _hud.AddChat("", want.Value ? "Creative: every block to hand, and you can fly." : "Survival: mine it, make it, stay alive.");
                    return true;
                }
                case "/time":
                {
                    if (Online) return false;
                    var ta = arg.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    float t = ta.Length == 2 && ta[0] == "set" ? WeatherSim.TimeOf(ta[1]) : -1;
                    if (t < 0) { _hud.AddChat("", "/time set day, noon, night or midnight (or a number from 0 to 24000)"); return true; }
                    _sky.Time01 = t;
                    _hud.AddChat("", "The time is set.");
                    return true;
                }
                case "/day":
                case "/night":
                    if (Online) return false;
                    _sky.Time01 = cmd == "/day" ? 0.27f : 0.8f;
                    _hud.AddChat("", cmd == "/day" ? "It's morning." : "Night falls.");
                    return true;
            }
            return false;
        }

        // A thing by its key or its name ("oak_planks", "Oak Planks", "oak planks").
        static ItemDef FindItem(string what)
        {
            if (string.IsNullOrEmpty(what)) return null;
            var d = Items.Get(what);
            if (d != null && d.Creative) return d;
            string spaced = what.Replace('_', ' ');
            foreach (var it in Items.Order) if (it.Creative && string.Equals(it.Name, spaced, StringComparison.OrdinalIgnoreCase)) return it;
            return null;
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
        // What the server last said each slot of the open container holds. Every change is sent with what was seen
        // there before it, so the server can apply "took three" to what is really there now.
        Core.Stack[] _seen = new Core.Stack[0]; Vector3Int _seenAt;
        public void Saw(Vector3Int at, Core.Stack[] all) { _seenAt = at; _seen = (Core.Stack[])all.Clone(); }
        public void Saw(Vector3Int at, int slot, Core.Stack s)
        {
            if (at != _seenAt) return;
            if (slot >= _seen.Length) Array.Resize(ref _seen, slot + 1);
            _seen[slot] = s;
        }

        public override void BoxSlot(Vector3Int at, int slot, Core.Stack s)
        {
            var seen = at == _seenAt && slot < _seen.Length ? _seen[slot] : default;
            Send(new Packet(Net.Msg.BoxSlot).I32(at.x).I32(at.y).I32(at.z).U8((byte)slot).Slot(s).Slot(seen));
            Saw(at, slot, s);
        }
        public override void CloseBox() => Send(new Packet(Net.Msg.CloseBox));
        public override void Sleep(bool on) => Send(new Packet(Net.Msg.Sleep).Bool(on));
        public override void Drop(Vector3 at, Core.Stack[] stacks) => Send(new Packet(Net.Msg.Drop).F32(at.x).F32(at.y).F32(at.z).Stacks(stacks));
        public override void Toss(Core.Stack s, Vector3 o, Vector3 v) => Send(new Packet(Net.Msg.Toss).Str(s.Item).U16(s.Count).U16(s.Wear).F32(o.x).F32(o.y).F32(o.z).F32(v.x).F32(v.y).F32(v.z).Bool(false));
        public override void Spill(Vector3 at, Core.Stack s) => Send(new Packet(Net.Msg.Toss).Str(s.Item).U16(s.Count).U16(s.Wear).F32(at.x).F32(at.y).F32(at.z).F32(0).F32(0).F32(0).Bool(true));
        public override void TakeDrop(int id) => Send(new Packet(Net.Msg.TakeDrop).I32(id));
        public void ReceivedDrops(Reader r)
        {
            Drops.Clear();
            int n = r.U16();
            for (int i = 0; i < n; i++) Drops.Add(new DropState { Id = r.I32(), Item = r.Str(), Count = r.U16(), Pos = new Vector3(r.F32(), r.F32(), r.F32()) });
        }
        public override void Fertilize(Vector3Int at) => Send(new Packet(Net.Msg.Fertilize).I32(at.x).I32(at.y).I32(at.z));
        public override void Mount(int mob) => Send(new Packet(Net.Msg.Mount).I32(mob));
        public override void Park(MobKind kind, Vector3 at, float yaw) => Send(new Packet(Net.Msg.Park).U8((byte)kind).F32(at.x).F32(at.y).F32(at.z).F32(yaw));
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
