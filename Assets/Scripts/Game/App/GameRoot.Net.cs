using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Stratum.Core;
using UnityEngine;

namespace Stratum.Game
{
    // Playing on a server: the world's blocks come from it and edits go to it; this machine still moves its own
    // player and keeps its own backpack, which the server holds between visits.
    public sealed partial class GameRoot
    {
        NetClient _net;
        Avatars _avatars;
        Coroutine _joining;
        int _myId;
        string _serverName = "", _serverAddr = "";
        int _maxView = 10, _online = 1, _serverMax = 10;
        Vector3 _netSpawn;
        float _moveTimer, _keepTimer, _pingTimer, _pingMs = -1, _sinceHeard;
        Vector3 _lastSentPos; float _lastSentYaw, _lastSentPitch; byte _lastSentFlags; float _forceMove;
        bool Online => _net != null;

        // Each copy of the game has its own secret; the server knows the player by a hash of it, so the same
        // name on a different machine is a different player and nobody can take over someone else's backpack.
        static string PlayerSecret()
        {
            var s = PlayerPrefs.GetString("mp_secret", "");
            if (s.Length < 32)
            {
                var b = new byte[24];
                using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create()) rng.GetBytes(b);
                s = BitConverter.ToString(b).Replace("-", "").ToLowerInvariant();
                PlayerPrefs.SetString("mp_secret", s);
                PlayerPrefs.Save();
            }
            return s;
        }

        void JoinServer(string name, string address, string password)
        {
            if (_joining != null) StopCoroutine(_joining);
            _joining = StartCoroutine(EnterRemote(name, address, password, null));
        }

        void CancelJoin()
        {
            if (_joining != null) StopCoroutine(_joining);
            _joining = null;
            _pendingNet?.Dispose();
            _pendingNet = null;
        }

        NetClient _pendingNet;

        IEnumerator EnterRemote(string name, string address, string password, string secret)
        {
            if (!NetClient.ParseAddress(address, out var host, out int port)) { _menus.ShowJoin("That address doesn't look right."); yield break; }
            var net = _pendingNet = new NetClient(host, port);
            float t = 0;
            while (net.Connected == null)
            {
                t += Time.unscaledDeltaTime;
                if (t > 10) { net.Dispose(); _pendingNet = null; _menus.ShowJoin("The server didn't answer. Check the address and that it's running."); yield break; }
                yield return null;
            }
            if (net.Connected == false) { _pendingNet = null; _menus.ShowJoin(net.Error ?? "Couldn't connect."); yield break; }
            _menus.SetJoinStatus("Saying hello...", false);
            net.Send(new Packet(Net.Msg.Hello).I32(Net.Protocol).Str(name).Str(secret ?? PlayerSecret()).Str(password ?? "").Str(Application.version + " " + Application.platform));

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
                if (r.Type == Net.Msg.Refuse) { var why = r.Str(); net.Dispose(); _pendingNet = null; _menus.ShowJoin(why); yield break; }
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
            _netSpawn = new Vector3(sx + 0.5f, sy, sz + 0.5f);
            yield return null;

            _world = VoxelWorld.Create(seed, null, _blockMat, _waterMat, Mathf.Min(Settings.ViewDistance, _maxView));
            _world.RequestChunks = SendWant;
            _world.Edited += (x, y, z, old, id) => _net?.Send(new Packet(Net.Msg.SetBlock).I32(x).I32(y).I32(z).U8(id).U8(old));
            _sky.ViewDistanceMeters = _world.ViewDistance * 16;
            _sky.Frozen = false;
            _sky.Time01 = (float)time; _sky.Day = Math.Max(1, day);
            _world.SetFocus(feet);

            _player = Player.Create(_world, _cam);
            _player.Creative = creative;
            _player.Feet = feet;
            _player.Yaw = yaw; _player.Pitch = pitch;
            _player.Health = hasPos ? Mathf.Clamp(health, 1, 20) : 20;
            _player.Inv.Selected = Mathf.Clamp(selected, 0, 8);
            for (int i = 0; i < slots.Length && i < Inventory.Size; i++)
                if (!slots[i].Empty && Items.Get(slots[i].Item) != null) _player.Inv.Slots[i] = slots[i];
            if (!hasPos && creative) StarterCreative(_player.Inv);
            _player.Died = OnDied;
            _player.InputEnabled = false;
            _act = Interaction.Create(_player, _world, _blockMat);
            _act.Sky = _sky;
            _act.InputEnabled = false;
            _act.OpenStation = s => OpenInventory(s);
            _hud = Hud.Create(_player, _act, _sky, _world);
            _hud.SendChat = text => _net?.Send(new Packet(Net.Msg.Chat).Str(text));
            _hud.OnlineInfo = OnlineLine;
            _hud.gameObject.SetActive(false);
            _avatars = Avatars.Create(_world, _sky, _cam);
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
                if (t > 90) { LeaveServer("The server is taking too long to send the world."); yield break; }
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
            _net.Send(new Packet(Net.Msg.Keep).F32(_player.Health).U8((byte)_player.Inv.Selected).Stacks(_player.Inv.Slots));
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
                    break;
                }
                case Net.Msg.Joined:
                {
                    int id = r.I32(); string name = r.Str();
                    var p = new Vector3(r.F32(), r.F32(), r.F32()); float yaw = r.F32();
                    if (id != _myId) _avatars.Add(id, name, p, yaw);
                    _online = _avatars.Count + 1;
                    break;
                }
                case Net.Msg.Left:
                    _avatars.Remove(r.I32());
                    _online = _avatars.Count + 1;
                    break;
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
                    _pingMs = (float)((Time.realtimeSinceStartupAsDouble - sent) * 1000);
                    break;
                }
                case Net.Msg.Refuse:
                    LeaveServer(r.Str());
                    break;
            }
        }

        // Back to the join screen (with why, if it wasn't our choice), saying goodbye properly if we can.
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
            if (Array.IndexOf(args, "-mptest") < 0) { _joining = StartCoroutine(EnterRemote(name, connect, Arg("-password"), null)); return; }
            _smokeDir = Arg("-mptest") ?? Path.Combine(Application.persistentDataPath, "mptest");
            Directory.CreateDirectory(_smokeDir);
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            StartCoroutine(MpTest(name, connect, Arg("-mprole") ?? "host"));
        }

        IEnumerator MpTest(string name, string address, string role)
        {
            float t0 = Time.realtimeSinceStartup;
            SmokeNote($"mptest {role} as {name} to {address}");
            // A secret made from the name, so two test copies on one machine are two players.
            var secret = ("mptest-" + name + "-0123456789abcdef0123456789").Substring(0, 32);
            StartCoroutine(EnterRemote(name, address, null, secret));
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
