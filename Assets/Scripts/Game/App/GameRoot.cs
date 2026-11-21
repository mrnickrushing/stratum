using System;
using System.Collections.Generic;
using System.IO;
using Stratum.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Stratum.Game
{
    // Boots the game in an empty scene and runs it: title screen over a living world, loading, playing, pausing,
    // saving and quitting.
    public sealed partial class GameRoot : MonoBehaviour
    {
        enum State { Title, Loading, Playing, Paused, Dead }

        public static GameRoot I;
        State _state;
        Camera _cam;
        Sky _sky;
        Menus _menus;
        Material _blockMat, _waterMat;
        VoxelWorld _world;
        Player _player;
        Interaction _act;
        Hud _hud;
        WorldMeta _meta;
        float _orbit, _autosave, _loadTime;
        Vector3 _titleFocus;
        bool _hudHidden, _signWasOpen;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (I != null) return;
            Step("boot");
            var go = new GameObject("GameRoot");
            DontDestroyOnLoad(go);
            go.AddComponent<GameRoot>();
        }

        void Awake()
        {
            I = this;
            Step($"starting {Application.version} on {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType}), {SystemInfo.processorCount} cores");
            ApplyDisplaySettings();
            ConfigurePipeline();

            Step("painting textures");
            BlockTextures.Build();
            Shader.SetGlobalTexture("_BlockAlbedo", BlockTextures.Albedo);
            Shader.SetGlobalTexture("_BlockNormal", BlockTextures.Normal);
            Shader.SetGlobalFloat("_WaterLayer", (float)Tex.Water);
            _blockMat = new Material(Shader.Find("Stratum/Block")) { name = "Blocks", enableInstancing = false };
            _waterMat = new Material(Shader.Find("Stratum/Water")) { name = "Water" };

            foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None)) Destroy(c.gameObject);
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) Destroy(l.gameObject);

            Step("drawing icons");
            Icons.Build(_blockMat);
            Step("making sounds");
            Sfx.Create();
            ApplyAudio();

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            DontDestroyOnLoad(camGo);
            _cam = camGo.AddComponent<Camera>();
            _cam.nearClipPlane = 0.05f;
            _cam.farClipPlane = 2400;
            _cam.clearFlags = CameraClearFlags.Skybox;
            camGo.AddComponent<AudioListener>();
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.requiresDepthTexture = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;

            _sky = Sky.Create();
            DontDestroyOnLoad(_sky.gameObject);

            UIKit.EnsureEventSystem();
            _menus = Menus.Create_();
            DontDestroyOnLoad(_menus.gameObject);
            _menus.ListWorlds = WorldStore.List;
            _menus.Play = m => StartCoroutine(Enter(m));
            _menus.Create = (name, seed, creative, difficulty, kind, keep) => StartCoroutine(Enter(WorldStore.Create(name, WorldStore.SeedFrom(seed), creative, difficulty, kind, keep)));
            _menus.UseWorld = UsePack;
            _menus.ListBackups = WorldStore.Backups;
            _menus.Restore = WorldStore.Restore;
            _menus.Delete = WorldStore.Delete;
            _menus.Resume = Resume;
            _menus.SaveAndQuit = () => { _player?.GetOut(); if (Online) LeaveServer(null); else { Save(); ToTitle(); } };
            _menus.Join = JoinServer;
            _menus.FindWorlds = FindWorlds;
            _menus.ChangeWorld = ChangeWorld;
            _menus.CancelJoin = CancelJoin;
            _menus.Respawn = Respawn;
            _menus.Quit = () => { Application.Quit(); };
            _menus.SettingsChanged = OnSettingsChanged;
            StartCoroutine(DefaultServer.Refresh(_menus.DefaultServerChanged));

            Step("title");
            ToTitle();
            MaybeSmokeTest();
            MaybeMultiplayerTest();
        }

        static void ApplyDisplaySettings()
        {
            QualitySettings.vSyncCount = Settings.VSync ? 1 : 0;
            Application.targetFrameRate = Settings.VSync ? -1 : 240;
            if (!Application.isEditor)
            {
                // "-screen-fullscreen 0|1" on the command line wins over the saved setting (once, at launch).
                if (!_cmdChecked)
                {
                    _cmdChecked = true;
                    var args = Environment.GetCommandLineArgs();
                    int i = Array.IndexOf(args, "-screen-fullscreen");
                    if (i >= 0 && i + 1 < args.Length) Settings.Fullscreen = args[i + 1] != "0";
                }
                var mode = Settings.Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
                if (Screen.fullScreenMode != mode)
                {
                    if (mode == FullScreenMode.Windowed) Screen.SetResolution(Mathf.Min(1600, Display.main.systemWidth), Mathf.Min(900, Display.main.systemHeight), mode);
                    else Screen.SetResolution(Display.main.systemWidth, Display.main.systemHeight, mode);
                }
            }
        }

        static bool _cmdChecked;
        static void Step(string what) => Debug.Log($"[Stratum {Time.realtimeSinceStartup:0.00}s] {what}");

        static void ConfigurePipeline()
        {
            QualitySettings.lodBias = 1;
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset rp)
            {
                rp.supportsCameraDepthTexture = true;
                rp.supportsCameraOpaqueTexture = false;
                rp.msaaSampleCount = 1;
                rp.renderScale = 1;
                int sh = Settings.Shadows;
                rp.shadowDistance = sh == 0 ? 0 : sh == 1 ? 60 : 110;
                rp.shadowCascadeCount = sh == 2 ? 3 : 2;
                rp.mainLightShadowmapResolution = sh == 2 ? 4096 : 2048;
            }
        }

        void ApplyAudio()
        {
            AudioListener.volume = 1;
            if (Sfx.I != null) { Sfx.I.Master = Settings.Volume; Sfx.I.Music = Settings.Music; }
        }

        void OnSettingsChanged()
        {
            ApplyAudio();
            ConfigurePipeline();
            ApplyDisplaySettings();
            int vd = Settings.ViewDistance;
            if (_world != null) _world.ViewDistance = _state == State.Title ? Mathf.Min(vd, 10) : vd;
            _sky.ViewDistanceMeters = (_world != null ? _world.ViewDistance : vd) * 16;
        }

        // ------------------------------------------------------------------ title

        void ToTitle()
        {
            Teardown();
            _state = State.Title;
            // A living world behind the title, seen at golden hour.
            _world = VoxelWorld.Create(1119, null, _blockMat, _waterMat, Mathf.Min(Settings.ViewDistance, 10));
            var spawn = _world.Gen.FindSpawn();
            _titleFocus = new Vector3(spawn.x, spawn.y, spawn.z);
            _world.SetFocus(_titleFocus);
            _sky.ViewDistanceMeters = _world.ViewDistance * 16;
            _sky.Time01 = 0.29f; _sky.Frozen = true; _sky.Islands = false;
            _cam.transform.SetParent(null);
            _cam.fieldOfView = 62;
            Time.timeScale = 1;
            _menus.SetOnline(false, "");
            Sfx.I.InGame = false;
            _menus.ShowMain();
            SetCursor(false);
        }

        void Teardown()
        {
            TeardownNet();
            TeardownSurvival();
            if (_hud != null) Destroy(_hud.gameObject);
            if (_player != null) { _cam.transform.SetParent(null); Destroy(_player.gameObject); }
            if (_world != null) Destroy(_world.gameObject);
            _hud = null; _player = null; _act = null; _world = null; _meta = null;
        }

        // ------------------------------------------------------------------ entering a world

        System.Collections.IEnumerator Enter(WorldMeta meta)
        {
            Teardown();
            _state = State.Loading;
            _menus.ShowLoading();
            _meta = meta;
            _keepInv = meta.keepInventory;
            yield return null;
            if (meta.hasPlayer) WorldStore.Backup(meta);       // the world as it was left, before anything changes

            _world = VoxelWorld.Create(meta.seed, meta.Folder, _blockMat, _waterMat, Settings.ViewDistance, (WorldKind)Mathf.Clamp(meta.kind, 0, (int)WorldKind.Count - 1));
            _sky.ViewDistanceMeters = _world.ViewDistance * 16;
            _sky.Frozen = false; _sky.Islands = _world.Gen.Kind == WorldKind.Sky;
            _sky.Time01 = meta.time; _sky.Day = Math.Max(1, meta.day);

            if (!meta.hasPlayer)
            {
                var s = _world.Gen.FindSpawn();
                meta.sx = s.x + 0.5f; meta.sy = s.y; meta.sz = s.z + 0.5f;
                meta.px = meta.sx; meta.py = meta.sy; meta.pz = meta.sz;
            }
            var feet = new Vector3(meta.px, meta.py, meta.pz);
            _world.SetFocus(feet);

            _player = Player.Create(_world, _cam);
            _player.Creative = meta.creative;
            _player.Feet = feet;
            _player.Yaw = meta.yaw; _player.Pitch = meta.pitch;
            _player.Health = meta.hasPlayer ? Mathf.Max(1, meta.health) : 20;
            _player.Food = meta.hasPlayer ? Mathf.Clamp(meta.food, 0, 20) : 20;
            _player.Saturation = meta.hasPlayer ? Mathf.Clamp(meta.saturation, 0, 20) : 5;
            _hasBed = meta.hasBed; _bed = new Vector3Int(meta.bx, meta.by, meta.bz);
            _player.XpLevel = meta.xpLevel; _player.XpPoints = meta.xpPoints;
            _sky.Weather = meta.weather;
            _player.Flying = meta.creative && meta.flying;
            _player.Inv.Selected = Mathf.Clamp(meta.selected, 0, 8);
            for (int i = 0; i < meta.slots.Count && i < Inventory.Size; i++)
            {
                var s = meta.slots[i];
                if (!string.IsNullOrEmpty(s.item) && s.count > 0 && Items.Get(s.item) != null) _player.Inv.Slots[i] = new Core.Stack(s.item, s.count) { Wear = s.wear };
            }
            _looseBack.Clear();
            for (int i = Inventory.Size; i < meta.slots.Count && i < Inventory.Size + MaxLoose; i++)
            {
                var s = meta.slots[i];
                if (!string.IsNullOrEmpty(s.item) && s.count > 0 && Items.Get(s.item) != null) _looseBack.Add(new Core.Stack(s.item, s.count) { Wear = s.wear });
            }
            if (!meta.hasPlayer && meta.creative) StarterCreative(_player.Inv);
            _player.Died = OnDied;
            _player.InputEnabled = false;
            _act = Interaction.Create(_player, _world, _blockMat);
            _act.Sky = _sky;
            _act.InputEnabled = false;
            _act.OpenStation = s => OpenInventory(s);
            _hud = Hud.Create(_player, _act, _sky, _world);
            _hud.SendChat = LocalChat;
            _hud.gameObject.SetActive(false);
            SetupSurvival(new LocalLink(_world, _player, _sky, meta.Folder, meta.seed, meta.difficulty), meta.creative ? 0 : meta.difficulty);
            if (_link is LocalLink lw) lw.Weather.Set(meta.weather, meta.weatherLeft);

            // Wait for the ground around the player.
            _loadTime = 0;
            while (true)
            {
                _world.SetFocus(_player.Feet);
                float p = _world.Progress(3);
                _menus.SetLoading(p, p < 0.5f ? "Shaping the land" : p < 0.95f ? "Growing the forests" : "Lighting the world");
                if (p >= 1 && _world.IsMeshed(Mathf.FloorToInt(feet.x), Mathf.FloorToInt(feet.z))) break;
                _loadTime += Time.unscaledDeltaTime;
                yield return null;
            }
            if (!meta.hasPlayer) PlaceOnGround();
            meta.hasPlayer = true;
            meta.lastPlayed = DateTime.UtcNow.Ticks;
            Save();

            _menus.HideAll();
            _hud.gameObject.SetActive(true);
            _player.InputEnabled = true;
            _act.InputEnabled = true;
            _state = State.Playing;
            Sfx.I.InGame = true;
            SetCursor(true);
            _autosave = 120;
            _hud.Toast(meta.creative ? "Creative: double-tap Space to fly" : "Press E to open your backpack");
            if (!meta.creative) _hud.AddChat("", $"Difficulty: {DifficultyNames[_difficulty]}. Press T to chat, /help for commands.");
        }

        void StarterCreative(Inventory inv)
        {
            string[] kit = { "grass", "stone_bricks", "oak_planks", "glass", "oak_log", "torch", "sunstone", "cobble", "sand" };
            for (int i = 0; i < kit.Length; i++) inv.Slots[i] = new Core.Stack(kit[i], 64);
        }

        void PlaceOnGround()
        {
            var f = _player.Feet;
            int x = Mathf.FloorToInt(f.x), z = Mathf.FloorToInt(f.z);
            for (int y = Chunk.Height - 2; y > 1; y--)
            {
                var b = Blocks.Get(_world.Get(x, y, z));
                if (b.Sway || b.Name.EndsWith(" Log")) continue;      // a new world starts on the ground, not in a tree
                if (b.Solid || b.Id == Blocks.Water)
                {
                    _player.Feet = new Vector3(x + 0.5f, y + 1, z + 0.5f);
                    _meta.sx = x + 0.5f; _meta.sy = y + 1; _meta.sz = z + 0.5f;
                    return;
                }
            }
        }

        // ------------------------------------------------------------------ playing

        void Update()
        {
            var kb = Keyboard.current;
            if (Online && _state != State.Loading) PumpNet();
            if (_looseBack.Count > 0 && _state == State.Playing) ReturnLoose();
            switch (_state)
            {
                case State.Title:
                    _orbit += Time.deltaTime * 2.2f;
                    var c = _titleFocus + new Vector3(0, 28, 0);
                    var off = Quaternion.Euler(0, _orbit, 0) * new Vector3(0, 0, -46);
                    _cam.transform.position = c + off;
                    _cam.transform.LookAt(_titleFocus + new Vector3(0, 6, 0));
                    if (kb != null && kb.escapeKey.wasPressedThisFrame) _menus.Back();
                    break;
                case State.Playing:
                    Playing(kb);
                    break;
                case State.Dead:
                    TickSurvival(null);
                    break;
                case State.Paused:
                    if (kb != null && kb.escapeKey.wasPressedThisFrame) _menus.Back();
                    break;
            }
            if (kb != null && kb.f11Key.wasPressedThisFrame) { Settings.Fullscreen = !Settings.Fullscreen; ApplyDisplaySettings(); }
            if (kb != null && kb.f2Key.wasPressedThisFrame) Screenshot();
        }

        void Playing(Keyboard kb)
        {
            _world.SetFocus(_player.Feet);
            _sky.Underwater = _player.HeadInWater;
            Sfx.I.Daylight = _sky.DayFactor;
            float depth = Mathf.Clamp01((Chunk.SeaLevel - 6 - _player.Feet.y) / 30f);
            Sfx.I.Depth = depth;
            if (_meta != null) _meta.minutesPlayed += Time.unscaledDeltaTime / 60.0;
            TickSurvival(kb);
            TickPortal(Time.deltaTime);
            if (_player.Asleep) return;

            // Writing on a sign.
            if (_hud.SignOpen)
            {
                _player.InputEnabled = false; _act.InputEnabled = false;
                if (kb != null && kb.escapeKey.wasPressedThisFrame) { _hud.CloseSign(); SetCursor(true); }
                return;
            }
            if (_signWasOpen) { _signWasOpen = false; SetCursor(true); }
            // Typing in chat: the keys are for the line, not for walking.
            if (_hud.ChatOpen)
            {
                _player.InputEnabled = false; _act.InputEnabled = false;
                if (kb != null && kb.escapeKey.wasPressedThisFrame) _hud.CloseChat();
                return;
            }
            if (!_hud.InventoryOpen && !_player.InputEnabled && !_player.Asleep) { _player.InputEnabled = true; _act.InputEnabled = true; }

            if (kb != null)
            {
                if (kb.escapeKey.wasPressedThisFrame)
                {
                    if (_hud.InventoryOpen) CloseInventory();
                    else Pause();
                    return;
                }
                if (_hud.CanChat && !_hud.InventoryOpen && _hud.ChatClosedFrame != Time.frameCount && (kb.tKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame)) { _hud.OpenChat(); return; }
                if (_hud.CanChat && !_hud.InventoryOpen && kb.slashKey.wasPressedThisFrame) { _hud.OpenChat("/"); return; }
                if (kb.eKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame)
                {
                    if (_hud.InventoryOpen) CloseInventory(); else OpenInventory(null);
                }
                if (kb.f1Key.wasPressedThisFrame) { _hudHidden = !_hudHidden; _hud.Hidden = _hudHidden; }
                if (kb.f3Key.wasPressedThisFrame) _hud.ShowDebug = !_hud.ShowDebug;
            }
            // Clicking back into the window grabs the mouse again.
            if (!_hud.InventoryOpen && Cursor.lockState != CursorLockMode.Locked && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) SetCursor(true);

            _act.HideHeld = _hud.InventoryOpen || _hudHidden;

            _autosave -= Time.unscaledDeltaTime;
            if (_autosave <= 0) { _autosave = 120; Save(); }
        }

        void OpenInventory(string station)
        {
            _hud.OpenInventory(station);
            _player.InputEnabled = false; _act.InputEnabled = false;
            SetCursor(false);
        }

        void CloseInventory()
        {
            _hud.CloseInventory();
            _player.InputEnabled = true; _act.InputEnabled = true;
            SetCursor(true);
        }

        void Pause()
        {
            _state = State.Paused;
            _player.InputEnabled = false; _act.InputEnabled = false;
            _menus.ShowPause();
            if (Online) _menus.SetOnline(true, OnlineLine());
            SetCursor(false);
            // A server's world goes on while we're in the menu.
            if (!Online) Time.timeScale = 0;
            Save();
        }

        void Resume()
        {
            Time.timeScale = 1;
            _menus.HideAll();
            _state = State.Playing;
            _player.InputEnabled = true; _act.InputEnabled = true;
            SetCursor(true);
        }

        void OnDied()
        {
            _player.GetOut();
            _state = State.Dead;
            if (_player.Asleep) WakeUp();
            _player.InputEnabled = false; _act.InputEnabled = false;
            if (_hud.InventoryOpen) _hud.CloseInventory();
            bool carried = false; foreach (var st in _player.Inv.Slots) if (!st.Empty) carried = true;
            if (!_keepInv) DropAll();
            _menus.SetDeathText(_player.Creative || !carried ? "" : _keepInv ? "You kept everything you were carrying." : "Everything you carried is in a satchel where you fell.");
            if (Online) { SendMove(true); SendKeep(); }
            _menus.ShowDeath();
            SetCursor(false);
        }

        void Respawn()
        {
            var at = RespawnPoint(Online ? GroundAt(_netSpawn) + new Vector3(0, 0.01f, 0) : new Vector3(_meta.sx, _meta.sy + 0.01f, _meta.sz), out _);
            _player.Respawn(at);
            if (Online) { SendMove(true); SendKeep(); }
            _menus.HideAll();
            _state = State.Playing;
            _player.InputEnabled = true; _act.InputEnabled = true;
            SetCursor(true);
        }

        static void SetCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        bool _keepInv;      // this world lets players keep their things when they die

        // A world pack from the Extras page: open the world made from it before, or make it now with its starting things.
        void UsePack(WorldPack pack)
        {
            var have = WorldStore.List().Find(w => w.name == pack.Name && w.seed == pack.Seed && w.kind == (int)pack.Kind);
            if (have == null)
            {
                have = WorldStore.Create(pack.Name, pack.Seed, pack.Creative, pack.Difficulty, pack.Kind, pack.Keep && !pack.Creative);
                foreach (var (item, n) in pack.Kit)
                    if (Items.Get(item) != null) have.slots.Add(new SlotSave { item = item, count = n });
                WorldStore.Save(have);
            }
            StartCoroutine(Enter(have));
        }

        // ------------------------------------------------------------------ saving

        // The pack as a save should record it: with whatever the cursor or a station holds tucked into free slots,
        // so a crash with the backpack open doesn't lose it.
        Core.Stack[] SaveSlots()
        {
            var a = (Core.Stack[])_player.Inv.Slots.Clone();
            if (_hud == null) return a;
            List<Core.Stack> over = null;
            foreach (var s in _hud.Loose())
            {
                bool put = false;
                for (int k = 0; k < Inventory.Main && !put; k++) if (a[k].Empty) { a[k] = s; put = true; }
                if (!put) (over ??= new List<Core.Stack>()).Add(s);
            }
            if (over == null) return a;
            // No free slot for these: they follow the pack's own slots in the save and come back on loading.
            if (over.Count > MaxLoose) over.RemoveRange(MaxLoose, over.Count - MaxLoose);
            var all = new Core.Stack[a.Length + over.Count];
            Array.Copy(a, all, a.Length); over.CopyTo(all, a.Length);
            return all;
        }

        public const int MaxLoose = 16;        // the cursor, two station slots and nine squares, with room to spare
        readonly List<Core.Stack> _looseBack = new List<Core.Stack>();

        // Things a save recorded beyond the pack (held by the cursor, a station or the grid when it was written):
        // back into the pack if there's room now, else into a satchel at the player's feet.
        void ReturnLoose()
        {
            if (_looseBack.Count == 0 || _link == null || _player == null) return;
            foreach (var s in _looseBack) Give(s.Item, s.Count, s.Wear);
            _looseBack.Clear();
            _hud.Toast("What you were holding is back with you");
            if (Online) SendKeep(); else Save();
        }


        void Save()
        {
            if (_meta == null || _world == null || _player == null || _state == State.Loading && !_meta.hasPlayer) return;
            try
            {
                var f = _player.Feet;
                _meta.px = f.x; _meta.py = f.y; _meta.pz = f.z;
                _meta.yaw = _player.Yaw; _meta.pitch = _player.Pitch; _meta.health = _player.Health; _meta.flying = _player.Flying;
                _meta.food = _player.Food; _meta.saturation = _player.Saturation;
                _meta.hasBed = _hasBed; _meta.bx = _bed.x; _meta.by = _bed.y; _meta.bz = _bed.z;
                _meta.xpLevel = _player.XpLevel; _meta.xpPoints = _player.XpPoints;
                if (_link is LocalLink wl) { _meta.weather = wl.Weather.State; _meta.weatherLeft = wl.Weather.Left; }
                _meta.time = _sky.Time01; _meta.day = _sky.Day;
                _meta.selected = _player.Inv.Selected;
                _meta.slots.Clear();
                foreach (var s in SaveSlots()) _meta.slots.Add(new SlotSave { item = s.Empty ? "" : s.Item, count = s.Empty ? 0 : s.Count, wear = s.Wear });
                _meta.lastPlayed = DateTime.UtcNow.Ticks;
                WorldStore.Save(_meta);
                _world.SaveAll();
                _link?.Save();
                if (_state == State.Playing || _state == State.Paused) SaveThumbnail();
            }
            catch (Exception e) { Debug.LogWarning("save failed: " + e.Message); }
        }

        // The world list shows each world as it was last seen: the camera's view without the HUD or the hand.
        void SaveThumbnail()
        {
            if (_cam == null || _meta?.Folder == null) return;
            const int W = 384, H = 216;
            var rt = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var held = _act != null ? _act.HeldRenderer : null;
            bool heldOn = held != null && held.enabled;
            if (held != null) held.enabled = false;
            var prev = RenderTexture.active;
            try
            {
                var req = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
                if (!RenderPipeline.SupportsRenderRequest(_cam, req)) return;
                RenderPipeline.SubmitRenderRequest(_cam, req);
                RenderTexture.active = rt;
                var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                tex.Apply();
                File.WriteAllBytes(Path.Combine(_meta.Folder, "thumb.jpg"), tex.EncodeToJPG(88));
                Destroy(tex);
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                if (held != null) held.enabled = heldOn;
            }
        }

        // Alt-tabbing away opens the pause menu (the game keeps running in the background otherwise).
        void OnApplicationFocus(bool focused)
        {
            if (!focused && _state == State.Playing && _smokeDir == null && !Application.isEditor && !(_hud != null && _hud.ChatOpen)) Pause();
        }

        void OnApplicationQuit()
        {
            _player?.GetOut();
            if (Online) { SendMove(true); SendKeep(); _net.Close(200); System.Threading.Thread.Sleep(250); }
            if (_state == State.Playing || _state == State.Paused || _state == State.Dead) Save();
        }

        void Screenshot()
        {
            var dir = Path.Combine(Application.persistentDataPath, "screenshots");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".png");
            ScreenCapture.CaptureScreenshot(path);
            _hud?.Toast("Screenshot saved");
        }
    }
}
