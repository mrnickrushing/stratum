using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Stratum.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Stratum.Game
{
    // Playing on a server: the world's blocks come from it and edits go to it; this machine still moves its own
    // player and keeps its own backpack, which the server holds between visits.
    public sealed partial class GameRoot
    {
        NetClient _net;
        Avatars _avatars;
        VoiceChat _voice;
        Coroutine _joining;
        int _myId;
        string _serverName = "", _serverAddr = "";
        int _maxView = 10, _online = 1, _serverMax = 10;
        Vector3 _netSpawn;
        float _moveTimer, _keepTimer, _pingTimer, _pingMs = -1, _sinceHeard;
        Vector3 _lastSentPos; float _lastSentYaw, _lastSentPitch; byte _lastSentFlags; float _forceMove;
        bool Online => _net != null;

        void JoinServer(string address, byte mode, string user, string secret, string invite, string world = "")
        {
            if (_joining != null) StopCoroutine(_joining);
            _joining = StartCoroutine(EnterRemote(address, mode, user, secret, invite, world));
        }

        // Asks a server which worlds it has (a ping answers with the list) for the Multiplayer screen.
        Coroutine _finding;
        void FindWorlds(string address)
        {
            if (_finding != null) StopCoroutine(_finding);
            _finding = StartCoroutine(AskWorlds(address));
        }

        IEnumerator AskWorlds(string address)
        {
            if (!NetClient.ParseAddress(DefaultServer.Resolve(address), out var host, out int port)) yield break;
            using (var net = new NetClient(host, port))
            {
                float t = 0;
                while (net.Connected == null && t < 6) { t += Time.unscaledDeltaTime; yield return null; }
                if (net.Connected != true) { _menus.SetWorlds(address, null, "Couldn't reach this server to see its worlds."); yield break; }
                net.Send(new Packet(Net.Msg.Ping).F64(Time.realtimeSinceStartupAsDouble));
                t = 0;
                while (t < 6)
                {
                    var r = net.Poll();
                    if (r == null) { if (!net.Open) break; t += Time.unscaledDeltaTime; yield return null; continue; }
                    if (r.Type != Net.Msg.Pong) continue;
                    _menus.SetWorlds(address, ReadWorlds(r), null);
                    net.Close();
                    yield break;
                }
                _menus.SetWorlds(address, null, "This server didn't say what worlds it has.");
            }
        }

        // A pong: time, name, message, online, max, then (servers from 0.3 on) the worlds.
        static List<Menus.WorldInfo> ReadWorlds(Reader r)
        {
            r.F64(); r.Str(); r.Str(); r.I32(); r.I32();
            var list = new List<Menus.WorldInfo>();
            if (!r.More) return list;
            int n = r.U8();
            for (int i = 0; i < n; i++)
                list.Add(new Menus.WorldInfo { Id = r.Str(), Name = r.Str(), Desc = r.Str(), Creative = r.Bool(), Difficulty = r.U8(), Online = r.I32() });
            if (r.More)
            {
                int k = r.U8();
                for (int i = 0; i < k; i++) { var kind = (WorldKind)r.U8(); if (i < list.Count) { var w = list[i]; w.Kind = kind < WorldKind.Count ? kind : WorldKind.Normal; list[i] = w; } }
            }
            return list;
        }

        void CancelJoin()
        {
            if (_joining != null) StopCoroutine(_joining);
            _joining = null;
            _pendingNet?.Dispose();
            _pendingNet = null;
        }

        NetClient _pendingNet;

        IEnumerator EnterRemote(string address, byte mode, string user, string secret, string invite, string world = "")
        {
            if (!NetClient.ParseAddress(DefaultServer.Resolve(address), out var host, out int port)) { _menus.ShowJoin("That address doesn't look right."); yield break; }
            var net = _pendingNet = new NetClient(host, port);
            float t = 0;
            while (net.Connected == null)
            {
                t += Time.unscaledDeltaTime;
                if (t > 10) { net.Dispose(); _pendingNet = null; _menus.ShowJoin("The server didn't answer. Check the address and that it's running."); yield break; }
                yield return null;
            }
            if (net.Connected == false) { _pendingNet = null; _menus.ShowJoin(net.Error ?? "Couldn't connect."); yield break; }
            _menus.SetJoinStatus(mode == Menus.ModeCreate ? "Making your account..." : "Logging in...", false);
            // Ask for the server's key and a one-time number, so the password never crosses the wire in the clear.
            net.Send(new Packet(Net.Msg.Ping).F64(Time.realtimeSinceStartupAsDouble));
            byte[] keyMod = null, keyExp = null, nonce = null, loginKey = null; bool ponged = false; t = 0;
            while (!ponged)
            {
                var pr = net.Poll();
                if (pr == null)
                {
                    if (!net.Open) { _pendingNet = null; _menus.ShowJoin(net.Error ?? "The server closed the connection."); yield break; }
                    t += Time.unscaledDeltaTime;
                    if (t > 8) { net.Dispose(); _pendingNet = null; _menus.ShowJoin("The server didn't answer. Try again."); yield break; }
                    yield return null;
                    continue;
                }
                if (pr.Type != Net.Msg.Pong) continue;
                ponged = true;
                try { ReadWorlds(pr); if (pr.More) { keyMod = pr.Blob(); keyExp = pr.Blob(); nonce = pr.Blob(); } } catch (Exception) { keyMod = null; }
            }
            var hello = new Packet(Net.Msg.Hello).I32(Net.Protocol).U8(mode).Str(user);
            if (keyMod != null && keyExp != null && nonce != null)
            {
                // Is this the server we think it is? The download site vouches for our own server's key; any other
                // server is remembered from the first visit.
                string why = DefaultServer.Trust(address, Secure.Fingerprint(keyMod));
                if (why != null) { net.Dispose(); _pendingNet = null; _menus.ShowJoin(why); yield break; }
                byte[] sealedLogin;
                try { sealedLogin = Secure.Seal(keyMod, keyExp, nonce, secret ?? "", invite ?? "", out loginKey); }
                catch (Exception e) { Debug.LogWarning("[Stratum] couldn't seal the login: " + e); net.Dispose(); _pendingNet = null; _menus.ShowJoin("This computer couldn't encrypt the login. Please tell whoever runs the server."); yield break; }
                hello.Str("").Str("").Str(Application.version + " " + Application.platform).Str(world ?? "").I32(7).U8((byte)Extras.Outfit).Blob(sealedLogin);
            }
            else   // a server from before 0.8 has no key to seal to
                hello.Str(secret ?? "").Str(invite ?? "").Str(Application.version + " " + Application.platform).Str(world ?? "").I32(7).U8((byte)Extras.Outfit);
            net.Send(hello);

            Reader welcome = null; t = 0;
            while (welcome == null)
            {
                var r = net.Poll();
                if (r == null)
                {
                    if (!net.Open) { _pendingNet = null; _menus.ShowJoin(net.Error ?? "The server closed the connection."); yield break; }
                    t += Time.unscaledDeltaTime;
                    if (t > 12) { net.Dispose(); _pendingNet = null; _menus.ShowJoin("The server didn't let us in (no answer)."); yield break; }
                    yield return null;
                    continue;
                }
                if (r.Type == Net.Msg.Refuse)
                {
                    var why = r.Str(); net.Dispose(); _pendingNet = null;
                    if (mode == Menus.ModeToken) Logins.Forget(address, user);     // the remembered login no longer works
                    _menus.ShowJoin(why);
                    yield break;
                }
                if (r.Type == Net.Msg.Welcome) welcome = r;
            }
            _pendingNet = null;
            _joining = null;

            // In: build the world around what the server said.
            Teardown();
            _state = State.Loading;
            _menus.ShowLoading();
            _menus.SetLoading(0, "Joining " + host);
            _net = net;
            _serverAddr = address;
            _myId = welcome.I32();
            int seed = welcome.I32();
            _serverName = welcome.Str();
            string motd = welcome.Str();
            bool creative = welcome.Bool();
            _maxView = welcome.U8();
            double time = welcome.F64(); int day = welcome.I32();
            int sx = welcome.I32(), sy = welcome.I32(), sz = welcome.I32();
            bool hasPos = welcome.Bool();
            var feet = new Vector3(welcome.F32(), welcome.F32(), welcome.F32());
            float yaw = welcome.F32(), pitch = welcome.F32(), health = welcome.F32();
            int selected = welcome.U8();
            var slots = welcome.Stacks();
            string name = welcome.Str(), token = welcome.Str();
            if (Secure.IsSealed(token)) token = Secure.OpenText(loginKey, token) ?? "";       // the remember-me token came hidden
            float food = welcome.F32(), sat = welcome.F32();
            bool hasBed = welcome.Bool(); var bed = new Vector3Int(welcome.I32(), welcome.I32(), welcome.I32());
            int difficulty = welcome.U8();
            int xpLevel = welcome.I32(), xpPoints = welcome.I32();
            var kind = welcome.More ? (WorldKind)welcome.U8() : WorldKind.Normal;
            if (kind >= WorldKind.Count) kind = WorldKind.Normal;
            _keepInv = welcome.More && welcome.Bool();          // 0.5.1: the server's world lets players keep their things
            Logins.Remember(address, name, token);
            _menus.JoinedAs(name);
            _netSpawn = new Vector3(sx + 0.5f, sy, sz + 0.5f);
            yield return null;

            _world = VoxelWorld.Create(seed, null, _blockMat, _waterMat, Mathf.Min(Settings.ViewDistance, _maxView), kind);
            _world.RequestChunks = SendWant;
            _world.Edited += (x, y, z, old, id) => _net?.Send(new Packet(Net.Msg.SetBlock).I32(x).I32(y).I32(z).U8(id).U8(old));
            _sky.ViewDistanceMeters = _world.ViewDistance * 16;
            _sky.Frozen = false; _sky.Islands = kind == WorldKind.Sky;
            _sky.Time01 = (float)time; _sky.Day = Math.Max(1, day);
            _world.SetFocus(feet);

            _player = Player.Create(_world, _cam);
            _player.Creative = creative;
            _player.Feet = feet;
            _player.Yaw = yaw; _player.Pitch = pitch;
            _player.Health = hasPos ? Mathf.Clamp(health, 1, 20) : 20;
            _player.Food = hasPos ? Mathf.Clamp(food, 0, 20) : 20;
            _player.Saturation = hasPos ? Mathf.Clamp(sat, 0, 20) : 5;
            _hasBed = hasBed; _bed = bed;
            _player.XpLevel = xpLevel; _player.XpPoints = xpPoints;
            _player.Inv.Selected = Mathf.Clamp(selected, 0, 8);
            for (int i = 0; i < slots.Length && i < Inventory.Size; i++)
                if (!slots[i].Empty && Items.Get(slots[i].Item) != null) _player.Inv.Slots[i] = slots[i];
            _looseBack.Clear();
            for (int i = Inventory.Size; i < slots.Length && i < Inventory.Size + MaxLoose; i++)
                if (!slots[i].Empty && Items.Get(slots[i].Item) != null) _looseBack.Add(slots[i]);
            if (!hasPos && creative) StarterCreative(_player.Inv);
            _player.Died = OnDied;
            _player.InputEnabled = false;
            _act = Interaction.Create(_player, _world, _blockMat);
            _act.Sky = _sky;
            _act.InputEnabled = false;
            _act.OpenStation = s => OpenInventory(s);
            _hud = Hud.Create(_player, _act, _sky, _world);
            _hud.SendChat = text => { if (text.StartsWith("/") && Command(text)) return; _net?.Send(new Packet(Net.Msg.Chat).Str(text)); };
            _hud.OnlineInfo = OnlineLine;
            _hud.gameObject.SetActive(false);
            _avatars = Avatars.Create(_world, _sky, _cam);
            _voice = VoiceChat.Create(_avatars, _cam.transform);
            _voice.Send = f => _net?.Send(new Packet(Net.Msg.Voice).Blob(f));
            SetupSurvival(new RemoteLink(() => _net, difficulty), creative ? 0 : difficulty);
            _voice.CanTalk = () => _state == State.Playing && _hud != null && !_hud.ChatOpen && !_menus.AnyOpen;
            _sinceHeard = 0; _pingTimer = 0; _keepTimer = 10; _moveTimer = 0; _forceMove = 0;
            SendMove(true);

            t = 0;
            while (true)
            {
                PumpNet();
                if (_net == null) yield break;                     // dropped while loading (PumpNet went back to the menu)
                _world.SetFocus(_player.Feet);
                float p = _world.Progress(3);
                _menus.SetLoading(p, p < 0.5f ? "Fetching the land from " + _serverName : p < 0.95f ? "Growing the forests" : "Lighting the world");
                if (p >= 1 && _world.IsMeshed(Mathf.FloorToInt(feet.x), Mathf.FloorToInt(feet.z))) break;
                t += Time.unscaledDeltaTime;
                if (t > 45 && _world.IsMeshed(Mathf.FloorToInt(feet.x), Mathf.FloorToInt(feet.z))) break;      // enough to stand on: the rest arrives as we play
                if (t > 90) { LeaveServer("The server is taking too long to send the world. Check your connection and try again."); yield break; }
                if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) { LeaveServer(null); yield break; }
                yield return null;
            }
            if (!hasPos) { _player.Feet = GroundAt(_netSpawn); _netSpawn = _player.Feet; }

            _menus.HideAll();
            _menus.SetOnline(true, "");
            _hud.gameObject.SetActive(true);
            _player.InputEnabled = true;
            _act.InputEnabled = true;
            _state = State.Playing;
            Sfx.I.InGame = true;
            SetCursor(true);
            _hud.AddChat("", $"Welcome to {_serverName}. Press T to chat.");
            if (!string.IsNullOrWhiteSpace(motd)) _hud.AddChat("", motd);
            if (!hasPos) _hud.Toast(creative ? "Creative: double-tap Space to fly" : "Press E to open your backpack");
            if (Settings.VoiceMode == 1) _hud.AddChat("", "Hold V to talk.");
            else if (Settings.VoiceMode == 2) _hud.AddChat("", "Your microphone is on: friends hear you when you speak.");
            Debug.Log($"[Stratum] joined {_serverName} at {address} as {name} (id {_myId})");
        }

        // The top of the ground at a column (if it's here yet), else the point itself.
        Vector3 GroundAt(Vector3 p)
        {
            int x = Mathf.FloorToInt(p.x), z = Mathf.FloorToInt(p.z);
            if (!_world.IsMeshed(x, z)) return p;
            for (int y = Chunk.Height - 2; y > 1; y--)
            {
                var b = Blocks.Get(_world.Get(x, y, z));
                if (b.Sway || b.Name.EndsWith(" Log")) continue;      // through the trees to the ground
                if (b.Solid || b.Id == Blocks.Water) return new Vector3(x + 0.5f, y + 1, z + 0.5f);
            }
            return p;
        }

        string OnlineLine() =>
            $"{_serverName}  ·  {_online}/{_serverMax} online" + (_pingMs >= 0 ? $"  ·  ping {_pingMs:0} ms" : "") +
            (_net != null ? $"  ·  in {_net.BytesIn / 1024f / 1024f:0.0} MB" : "");

        void SendWant(List<(int x, int z)> list)
        {
            if (_net == null) return;
            for (int start = 0; start < list.Count; start += 512)
            {
                int n = Math.Min(512, list.Count - start);
                var p = new Packet(Net.Msg.WantChunks).U16(n);
                for (int i = start; i < start + n; i++) p.I32(list[i].x).I32(list[i].z);
                _net.Send(p);
            }
        }

        void SendMove(bool force)
        {
            if (_net == null || _player == null) return;
            var f = _player.Feet;
            byte flags = 0;
            if (_player.Sneaking) flags |= Net.Sneaking;
            if (_player.Flying) flags |= Net.Flying;
            if (_act != null && _act.Swinging) flags |= Net.Swinging;
            if (_player.InWater) flags |= Net.InWater;
            if (_player.Health <= 0) flags |= Net.Dead;
            if (_player.Asleep) flags |= Net.Asleep;
            if (_player.Riding == MobKind.Boat) flags |= Net.InBoat;
            if (_player.Riding == MobKind.Cart) flags |= Net.InCart;
            bool same = (f - _lastSentPos).sqrMagnitude < 0.0004f && Mathf.Abs(Mathf.DeltaAngle(_player.Yaw, _lastSentYaw)) < 0.5f &&
                        Mathf.Abs(_player.Pitch - _lastSentPitch) < 0.5f && flags == _lastSentFlags;
            if (same && !force) return;
            _lastSentPos = f; _lastSentYaw = _player.Yaw; _lastSentPitch = _player.Pitch; _lastSentFlags = flags;
            var held = _player.Inv.Held;
            _net.Send(new Packet(Net.Msg.Move).F32(f.x).F32(f.y).F32(f.z).F32(_player.Yaw).F32(_player.Pitch).U8(flags).Str(held.Empty ? "" : held.Item));
        }

        void SendKeep()
        {
            if (_net == null || _player == null) return;
            _net.Send(new Packet(Net.Msg.Keep).F32(_player.Health).F32(_player.Food).F32(_player.Saturation).U8((byte)_player.Inv.Selected).Stacks(SaveSlots())
                .Bool(_hasBed).I32(_bed.x).I32(_bed.y).I32(_bed.z).I32(_player.XpLevel).I32(_player.XpPoints));
        }

        // Reads what the server sent and keeps it up to date with us. Runs every frame while connected.
        void PumpNet()
        {
            if (_net == null) return;
            float dt = Time.unscaledDeltaTime;
            Reader r;
            int budget = 600;
            while (budget-- > 0 && (r = _net?.Poll()) != null)
            {
                _sinceHeard = 0;
                try { Handle(r); }
                catch (Exception e) { Debug.LogWarning($"bad {r.Type} from server: {e.Message}"); }
            }
            if (_net == null) return;
            if (!_net.Open) { LeaveServer(_net.Error ?? "Lost the connection to the server."); return; }
            _sinceHeard += dt;
            if (_sinceHeard > 30) { LeaveServer("The server stopped answering."); return; }

            _moveTimer -= dt; _keepTimer -= dt; _pingTimer -= dt; _forceMove -= dt;
            if (_moveTimer <= 0) { _moveTimer = 0.1f; SendMove(_forceMove <= 0); if (_forceMove <= 0) _forceMove = 2; }
            if (_keepTimer <= 0) { _keepTimer = 10; SendKeep(); }
            if (_pingTimer <= 0) { _pingTimer = 5; _net.Send(new Packet(Net.Msg.Ping).F64(Time.realtimeSinceStartupAsDouble)); }
        }

        void Handle(Reader r)
        {
            switch (r.Type)
            {
                case Net.Msg.Chunk:
                {
                    int x = r.I32(), z = r.I32();
                    _world.ReceiveChunk(x, z, r.Blob());
                    break;
                }
                case Net.Msg.Block:
                {
                    int x = r.I32(), y = r.I32(), z = r.I32(); byte id = r.U8(); int by = r.I32();
                    if (by == _myId) break;                    // our own edit coming back: already here
                    _world.ApplyRemote(x, y, z, id);
                    // The chest or furnace on screen has just been broken or replaced: what the screen shows is no
                    // longer there to take.
                    if (_hud != null && _hud.BoxOpen && _hud.BoxAt == new Vector3Int(x, y, z) && !Blocks.Get(id).Container)
                    { _hud.ForgetBox(); CloseInventory(); _hud.Toast("That was taken away"); }
                    break;
                }
                case Net.Msg.Joined:
                {
                    int id = r.I32(); string name = r.Str();
                    var p = new Vector3(r.F32(), r.F32(), r.F32()); float yaw = r.F32();
                    int outfit = r.More ? r.U8() : 0;               // 0.6: what they're wearing
                    if (id != _myId) _avatars.Add(id, name, p, yaw, outfit);
                    _online = _avatars.Count + 1;
                    break;
                }
                case Net.Msg.Heard:
                    _voice?.Heard(r.I32(), r.Blob());
                    break;
                case Net.Msg.Left:
                {
                    int id = r.I32();
                    _avatars.Remove(id);
                    _voice?.Remove(id);
                    _online = _avatars.Count + 1;
                    break;
                }
                case Net.Msg.Moved:
                {
                    int id = r.I32();
                    var p = new Vector3(r.F32(), r.F32(), r.F32()); float yaw = r.F32(), pitch = r.F32(); byte flags = r.U8();
                    _avatars.Moved(id, p, yaw, pitch, flags);
                    break;
                }
                case Net.Msg.Clock:
                {
                    float time = (float)r.F64(); int day = r.I32();
                    _sky.Weather = r.U8();
                    if (Mathf.Abs(Mathf.DeltaAngle(_sky.Time01 * 360, time * 360)) > 0.5f) _sky.Time01 = time;
                    _sky.Day = Math.Max(1, day);
                    break;
                }
                case Net.Msg.Said:
                {
                    string from = r.Str(), text = r.Str();
                    _hud?.AddChat(from, text);
                    if (!string.IsNullOrEmpty(from)) Sfx.I?.Play("click", 0.35f);
                    break;
                }
                case Net.Msg.Pong:
                {
                    double sent = r.F64(); r.Str(); r.Str(); _online = r.I32(); _serverMax = r.I32();
                    _menus.SetWorldCount(r.More ? r.U8() : 1);
                    _pingMs = (float)((Time.realtimeSinceStartupAsDouble - sent) * 1000);
                    break;
                }
                case Net.Msg.Refuse:
                    LeaveServer(r.Str());
                    break;
                case Net.Msg.Sign:
                {
                    var at = new Vector3Int(r.I32(), r.I32(), r.I32());
                    _link?.SignChanged?.Invoke(at, r.Str());
                    break;
                }
                case Net.Msg.Mobs:
                    (_link as RemoteLink)?.Received(r);
                    break;
                case Net.Msg.Arrived:
                    Land(new Vector3(r.F32(), r.F32() + 0.01f, r.F32()));
                    break;
                case Net.Msg.Mounted:
                {
                    var k = (MobKind)r.U8(); var at = new Vector3(r.F32(), r.F32(), r.F32()); float yaw = r.F32();
                    _link?.Mounted?.Invoke(k, at, yaw);
                    break;
                }
                case Net.Msg.Hurt:
                {
                    float dmg = r.F32(), fx = r.F32(), fz = r.F32(); var kind = (MobKind)r.U8();
                    _link?.Hurt?.Invoke(dmg, new Vector3(fx, _player.Feet.y, fz), kind);
                    break;
                }
                case Net.Msg.Give:
                {
                    string item = r.Str(); int count = r.U16(), wear = r.U16();
                    _link?.Gave?.Invoke(item, count, wear);
                    break;
                }
                case Net.Msg.TakeBack:
                {
                    // Something taken from a furnace had already been used up by it: it comes back out of our hands.
                    string item = r.Str(); int count = r.U16();
                    _hud?.TakeBack(item, count);
                    break;
                }
                case Net.Msg.Box:
                {
                    var at = new Vector3Int(r.I32(), r.I32(), r.I32());
                    var all = r.Stacks();
                    (_link as RemoteLink)?.Saw(at, all);
                    _link?.BoxOpened?.Invoke(at, all);
                    break;
                }
                case Net.Msg.BoxSet:
                {
                    var at = new Vector3Int(r.I32(), r.I32(), r.I32()); int slot = r.U8();
                    var now = r.Slot();
                    (_link as RemoteLink)?.Saw(at, slot, now);
                    _link?.BoxChanged?.Invoke(at, slot, now);
                    break;
                }
                case Net.Msg.Sleepers:
                {
                    int asleep = r.U8(), total = r.U8();
                    _link?.Sleepers?.Invoke(asleep, total);
                    break;
                }
                case Net.Msg.MobSound:
                {
                    r.I32(); var kind = (MobKind)r.U8(); int code = r.U8();
                    var at = new Vector3(r.F32(), r.F32(), r.F32());
                    if (code < Net.MobSounds.Length) _link?.MobSound?.Invoke(kind, Net.MobSounds[code], at);
                    break;
                }
            }
        }

        // Back to the join screen (with why, if it wasn't our choice), saying goodbye properly if we can.
        // Saves this world's progress on the server and goes back to the Multiplayer screen to pick another; a
        // remembered login makes going in again one press.
        void ChangeWorld()
        {
            LeaveServer(null);
            _menus.ShowJoin(null);
            _menus.SetJoinStatus("Pick a world, then press the button to go in.", false);
        }

        void LeaveServer(string why)
        {
            if (_net != null)
            {
                if (_net.Open) { SendMove(true); SendKeep(); }
                _net.Close();
                _net = null;
            }
            Time.timeScale = 1;
            if (why != null) Debug.Log("[Stratum] left the server: " + why);
            ToTitle();
            _menus.SetOnline(false, "");
            if (why != null) _menus.ShowJoin(why);
        }

        void TeardownNet()
        {
            if (_avatars != null) Destroy(_avatars.gameObject);
            _avatars = null;
            if (_voice != null) Destroy(_voice.gameObject);
            _voice = null;
            if (_net != null) { _net.Dispose(); _net = null; }
            _pingMs = -1; _online = 1;
        }

        // ------------------------------------------------------------------ two-player test

        // "-mptest <folder> -connect host:port -name X [-mprole host|guest]": joins, builds or looks for a marker
        // pillar beside the spawn, chats, takes screenshots and quits. Two copies of the game (one of each role)
        // against one server check that each sees the other's world and figure.
        void MaybeMultiplayerTest()
        {
            var args = Environment.GetCommandLineArgs();
            string Arg(string flag) { int i = Array.IndexOf(args, flag); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
            var connect = Arg("-connect");
            if (connect == null) return;
            string name = Arg("-name") ?? "Player";
            VoiceChat.TestTone = Array.IndexOf(args, "-voicetone") >= 0;
            if (Array.IndexOf(args, "-mptest") < 0)
            {
                // "-connect host:port -name X -password P [-create]": log straight in (or make the account).
                _joining = StartCoroutine(EnterRemote(connect, Array.IndexOf(args, "-create") >= 0 ? Menus.ModeCreate : Menus.ModeLogin, name, Arg("-password") ?? "", ""));
                return;
            }
            _smokeDir = Arg("-mptest") ?? Path.Combine(Application.persistentDataPath, "mptest");
            Directory.CreateDirectory(_smokeDir);
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            StartCoroutine(MpTest(name, connect, Arg("-mprole") ?? "host", Arg("-password") ?? "mptest-password", Array.IndexOf(args, "-create") >= 0));
        }

        IEnumerator MpTest(string name, string address, string role, string password, bool create)
        {
            float t0 = Time.realtimeSinceStartup;
            SmokeNote($"mptest {role} as {name} to {address}{(create ? " (new account)" : "")}");
            StartCoroutine(EnterRemote(address, create ? Menus.ModeCreate : Menus.ModeLogin, name, password, ""));
            while (_state != State.Playing && Time.realtimeSinceStartup - t0 < 120) yield return null;
            SmokeNote($"joined: {_state == State.Playing} in {Time.realtimeSinceStartup - t0:0.0} s, {_net?.BytesIn / 1024 ?? 0} KB in");
            if (_state != State.Playing) { Finish(); yield break; }
            SetCursor(false);
            _sky.Time01 = 0.42f;

            // The marker: a pillar of sunstone, four blocks east of the spawn, built by the host.
            var spawn = _netSpawn;
            int mx = Mathf.FloorToInt(spawn.x) + 4, mz = Mathf.FloorToInt(spawn.z);
            int gy = Mathf.FloorToInt(GroundAt(new Vector3(mx, 0, mz) + new Vector3(0.5f, spawn.y, 0.5f)).y);
            if (role == "host")
            {
                _player.Feet = GroundAt(new Vector3(mx - 2.5f, spawn.y, mz + 0.5f));      // right by it: the spawn can be steep
                _player.Yaw = 90; _player.Pitch = -10;
                for (int k = 0; k < 4; k++) _world.Set(mx, gy + k, mz, Blocks.Sunstone);
                _hud.SendChat("hello from the host");
                yield return Wait(4);
                yield return Shot("host_1_pillar");
                // Wait for the guest, then look at them.
                for (float w = 0; w < 60 && _avatars.Count == 0; w += Time.unscaledDeltaTime) yield return null;
                SmokeNote($"sees {_avatars.Count} other player(s): {string.Join(", ", _avatars.Names)}");
                yield return Wait(6);
                LookAtOther();
                yield return Wait(1.5f);
                yield return Shot("host_2_guest");
                yield return Fps("online", 4);
                // Stay while the guest looks round and breaks the top block, then leave after they do.
                for (float w = 0; w < 180 && _avatars.Count > 0; w += Time.unscaledDeltaTime) yield return null;
            }
            else
            {
                _player.Feet = GroundAt(new Vector3(mx + 3.5f, spawn.y, mz + 2.5f));
                _player.Yaw = -110; _player.Pitch = 5;
                // The pillar may already be there, so look up the whole column for it rather than above "the ground".
                int top = -1;
                for (float w = 0; w < 90 && top < 0; w += Time.unscaledDeltaTime)
                {
                    for (int y = Chunk.Height - 1; y > 1; y--) if (_world.Get(mx, y, mz) == Blocks.Sunstone) { top = y; break; }
                    if (top < 0) yield return null;
                }
                byte id = top >= 0 ? Blocks.Sunstone : (byte)0;
                SmokeNote($"pillar seen: {(top >= 0 ? $"sunstone up to y {top}" : "no")}");
                for (float w = 0; w < 30 && _avatars.Count == 0; w += Time.unscaledDeltaTime) yield return null;
                SmokeNote($"sees {_avatars.Count} other player(s): {string.Join(", ", _avatars.Names)}");
                // Break the top of the pillar; the host should see it go.
                // Hover level with the top, two blocks off (the mountain round the spawn is too steep to walk up).
                if (top >= 0) { _player.Flying = true; _player.Feet = new Vector3(mx + 2.5f, top - 1, mz + 0.5f); SendMove(true); yield return Wait(1); }
                SmokeNote($"standing at {_player.Feet}, {Vector3.Distance(_player.Feet + Vector3.up * 1.6f, new Vector3(mx + 0.5f, top + 0.5f, mz + 0.5f)):0.0} m from the top");
                if (top >= 0) { _world.Set(mx, top, mz, Blocks.Air); gy = top - 3; }
                _hud.SendChat("hello from the guest");
                yield return Wait(3);
                LookAtOther();
                yield return Wait(1.5f);
                yield return Shot("guest_1_host");
                _hud.OpenChat();
                yield return Wait(1);
                yield return Shot("guest_2_chat");
                _hud.CloseChat();
            }
            if (_voice != null) SmokeNote($"voice: sent {_voice.FramesSent} frames, heard {_voice.FramesHeard} (last level {_voice.HeardLevel:0.000})");
            SmokeNote($"host sees top of pillar: {Blocks.Get(_world.Get(mx, gy + 3, mz)).Name}; in {_net?.BytesIn / 1024 ?? 0} KB, out {_net?.BytesOut / 1024 ?? 0} KB, ping {_pingMs:0} ms");
            LeaveServer(null);
            SmokeNote($"done in {Time.realtimeSinceStartup - t0:0} s");
            Finish();
        }

        void LookAtOther()
        {
            foreach (int id in _avatars.Ids)
            {
                if (!_avatars.TryGet(id, out var p)) continue;
                var d = p + Vector3.up * 1.4f - (_player.Feet + Vector3.up * 1.6f);
                float dist = d.magnitude;
                _player.Yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                _player.Pitch = -Mathf.Asin(Mathf.Clamp(d.y / Mathf.Max(dist, 0.01f), -1, 1)) * Mathf.Rad2Deg;
                SmokeNote($"looking at player {id} {dist:0.0} m away");
                return;
            }
        }
    }
}
