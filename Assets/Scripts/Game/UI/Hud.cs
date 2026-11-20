using System.Collections.Generic;
using System.Linq;
using Stratum.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Stratum.Game
{
    // The in-game screen: crosshair, hotbar, health and air, messages, the F3 readout, and the backpack with
    // crafting (E).
    public sealed partial class Hud : MonoBehaviour
    {
        public Player P; public Interaction Act; public Sky Sky; public VoxelWorld World;
        Canvas _canvas;
        RectTransform _root;
        readonly SlotView[] _hot = new SlotView[9];
        RectTransform _select;
        Image _health, _healthBack, _air, _airBack, _water, _hurt, _food, _foodBack, _armour, _armourBack, _use, _useBack, _mobBar, _mobBack, _bossBar, _bossBack, _sleep;
        Text _mobName, _bossName, _sleepText;
        public Link Link;
        float _sleepFade; bool _sleeping; string _sleepLine = "";
        Text _name, _debug, _fps, _toast;
        float _nameTimer, _toastTimer, _hurtFlash;
        int _lastSel = -1; string _lastHeld;
        readonly Queue<string> _toasts = new Queue<string>();
        public bool Hidden;
        public bool ShowDebug;

        // Backpack
        GameObject _inv;
        RectTransform _invGrid, _craftList, _palette;
        Text _craftTitle;
        readonly List<SlotView> _invSlots = new List<SlotView>();
        Image _cursorIcon; Text _cursorCount;
        Image _tag; Text _tagText; string _tagFor = "";
        Image _blockTag; Text _blockTagText;
        Text _tip;
        Stack _cursor;
        string _station;
        // An open chest: where, and what's in it (kept in step with the host).
        Vector3Int _boxAt; Stack[] _box; RectTransform _boxGrid; readonly List<SlotView> _boxSlots = new List<SlotView>(); GameObject _craftArea;
        readonly List<SlotView> _armSlots = new List<SlotView>(); Text _armText;
        public System.Action CloseBoxed;
        float _rebuildTimer;
        public bool InventoryOpen => _inv != null && _inv.activeSelf;

        public static Hud Create(Player p, Interaction act, Sky sky, VoxelWorld world)
        {
            var go = new GameObject("Hud");
            var h = go.AddComponent<Hud>();
            h.P = p; h.Act = act; h.Sky = sky; h.World = world;
            h.Build();
            return h;
        }

        void Build()
        {
            _canvas = UIKit.Root("HudCanvas", 10);
            _canvas.transform.SetParent(transform, false);
            _root = (RectTransform)_canvas.transform;

            _water = UIKit.Panel(_root, "Underwater", new Color(0.05f, 0.25f, 0.45f, 0.35f), UIKit.White); _water.rectTransform.Fill(); _water.raycastTarget = false;
            _portal = UIKit.Panel(_root, "Portal", new Color(1f, 0.38f, 0.12f, 0), UIKit.White); _portal.rectTransform.Fill(); _portal.raycastTarget = false;
            _hurt = UIKit.Panel(_root, "Hurt", new Color(0.7f, 0.05f, 0.02f, 0), UIKit.White); _hurt.rectTransform.Fill(); _hurt.raycastTarget = false;

            // Crosshair
            var ch = UIKit.Rect(_root, "Crosshair").Place(0.5f, 0.5f, 0, 0, 26, 26);
            var h1 = UIKit.Panel(ch, "h", new Color(1, 1, 1, 0.85f), UIKit.White); h1.rectTransform.Place(0.5f, 0.5f, 0, 0, 22, 2.5f); h1.raycastTarget = false;
            var v1 = UIKit.Panel(ch, "v", new Color(1, 1, 1, 0.85f), UIKit.White); v1.rectTransform.Place(0.5f, 0.5f, 0, 0, 2.5f, 22); v1.raycastTarget = false;
            h1.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.5f);
            v1.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.5f);

            // Hotbar
            const float S = 84, G = 6;
            var bar = UIKit.Panel(_root, "Hotbar", new Color(0.05f, 0.06f, 0.08f, 0.6f));
            bar.rectTransform.Place(0.5f, 0, 0, 18, 9 * (S + G) + 14, S + 14);
            for (int i = 0; i < 9; i++)
            {
                var s = SlotView.Make(bar.transform, S);
                s.Back.raycastTarget = false;
                s.GetComponent<RectTransform>().Place(0, 0.5f, 7 + i * (S + G), 0, S, S);
                _hot[i] = s;
                var key = UIKit.Label(s.transform, (i + 1).ToString(), 16, new Color(1, 1, 1, 0.45f), TextAnchor.UpperLeft);
                key.rectTransform.Fill(6);
            }
            var sel = UIKit.Panel(bar.transform, "Selected", new Color(0, 0, 0, 0), UIKit.RoundSmall);
            sel.rectTransform.Place(0, 0.5f, 3, 0, S + 8, S + 8);
            var o = sel.gameObject.AddComponent<Outline>(); o.effectColor = UIKit.Accent; o.effectDistance = new Vector2(3, -3);
            var o2 = sel.gameObject.AddComponent<Outline>(); o2.effectColor = UIKit.Accent; o2.effectDistance = new Vector2(-3, 3);
            sel.raycastTarget = false;
            _select = sel.rectTransform;

            // Health and air above the hotbar
            float bw = (9 * (S + G) + 14) / 2 - 10;
            _healthBack = UIKit.Panel(_root, "HealthBack", new Color(0.05f, 0.05f, 0.06f, 0.7f), UIKit.RoundSmall);
            _healthBack.rectTransform.Place(0.5f, 0, -bw / 2 - 5, 18 + S + 40, bw, 18);
            _health = UIKit.Panel(_healthBack.transform, "Health", new Color(0.86f, 0.22f, 0.2f), UIKit.RoundSmall);
            _health.rectTransform.anchorMin = Vector2.zero; _health.rectTransform.anchorMax = new Vector2(1, 1); _health.rectTransform.offsetMin = new Vector2(3, 3); _health.rectTransform.offsetMax = new Vector2(-3, -3);
            _airBack = UIKit.Panel(_root, "AirBack", new Color(0.05f, 0.05f, 0.06f, 0.7f), UIKit.RoundSmall);
            _airBack.rectTransform.Place(0.5f, 0, bw / 2 + 5, 18 + S + 40, bw, 18);
            _air = UIKit.Panel(_airBack.transform, "Air", new Color(0.45f, 0.75f, 1f), UIKit.RoundSmall);
            _air.rectTransform.anchorMin = Vector2.zero; _air.rectTransform.anchorMax = new Vector2(1, 1); _air.rectTransform.offsetMin = new Vector2(3, 3); _air.rectTransform.offsetMax = new Vector2(-3, -3);
            // Hunger beside health; armour over health; air over hunger when it's needed.
            _airBack.rectTransform.anchoredPosition = new Vector2(bw / 2 + 5, 18 + S + 40 + 26);
            _foodBack = Bar("Food", new Color(0.86f, 0.58f, 0.22f), bw / 2 + 5, 18 + S + 40, bw, out _food);
            _armourBack = Bar("Armour", new Color(0.78f, 0.80f, 0.86f), -bw / 2 - 5, 18 + S + 40 + 26, bw, out _armour);
            _xpBack = Bar("Xp", new Color(0.45f, 0.9f, 0.25f), 0, 18 + S + 20, bw * 2 + 10, out _xp);
            _xpBack.rectTransform.sizeDelta = new Vector2(bw * 2 + 10, 12);
            _xpLevel = UIKit.Label(_xpBack.transform, "", 22, new Color(0.6f, 1f, 0.35f), TextAnchor.MiddleCenter, FontStyle.Bold).Shadowed(2);
            _xpLevel.rectTransform.Place(0.5f, 0.5f, 0, 2, 120, 30);
            _useBack = Bar("Use", new Color(1, 1, 1, 0.9f), 0, 0, 120, out _use);
            _useBack.rectTransform.anchorMin = _useBack.rectTransform.anchorMax = new Vector2(0.5f, 0.5f); _useBack.rectTransform.anchoredPosition = new Vector2(0, -34);
            _useBack.rectTransform.sizeDelta = new Vector2(120, 12);
            // What's in the crosshair, and a boss's health along the top.
            _mobName = UIKit.Label(_root, "", 24, UIKit.Ink, TextAnchor.MiddleCenter, FontStyle.Bold).Shadowed();
            _mobName.rectTransform.Place(0.5f, 0.5f, 0, 70, 500, 32);
            _mobBack = Bar("MobHealth", new Color(0.86f, 0.22f, 0.2f), 0, 0, 200, out _mobBar);
            _mobBack.rectTransform.anchorMin = _mobBack.rectTransform.anchorMax = new Vector2(0.5f, 0.5f); _mobBack.rectTransform.anchoredPosition = new Vector2(0, 48);
            _mobBack.rectTransform.sizeDelta = new Vector2(200, 12);
            _blockTag = UIKit.Panel(_root, "BlockTag", new Color(0.06f, 0.06f, 0.08f, 0.6f));
            _blockTag.raycastTarget = false;
            _blockTag.rectTransform.Place(0.5f, 0.5f, 0, -64, 120, 28);
            _blockTagText = UIKit.Label(_blockTag.transform, "", 18, new Color(1, 1, 1, 0.92f), TextAnchor.MiddleCenter);
            _blockTagText.raycastTarget = false; _blockTagText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _blockTagText.rectTransform.Fill();
            _blockTag.gameObject.SetActive(false);
            _bossName = UIKit.Label(_root, "", 28, new Color(1, 0.8f, 0.6f), TextAnchor.MiddleCenter, FontStyle.Bold).Shadowed();
            _bossName.rectTransform.Place(0.5f, 1, 0, -6, 900, 40);
            _bossBack = Bar("Boss", new Color(0.9f, 0.35f, 0.1f), 0, 0, 900, out _bossBar);
            _bossBack.rectTransform.anchorMin = _bossBack.rectTransform.anchorMax = new Vector2(0.5f, 1); _bossBack.rectTransform.anchoredPosition = new Vector2(0, -74);
            _bossBack.rectTransform.sizeDelta = new Vector2(900, 20);
            _sleep = UIKit.Panel(_root, "Sleep", new Color(0.01f, 0.01f, 0.04f, 0), UIKit.White); _sleep.rectTransform.Fill(); _sleep.raycastTarget = false;
            _sleepText = UIKit.Label(_sleep.transform, "", 34, new Color(0.85f, 0.88f, 1f), TextAnchor.MiddleCenter, FontStyle.Bold).Shadowed();
            _sleepText.rectTransform.Fill();

            _name = UIKit.Label(_root, "", 30, UIKit.Ink, TextAnchor.MiddleCenter, FontStyle.Bold).Shadowed();
            _name.rectTransform.Place(0.5f, 0, 0, 18 + S + 72, 800, 40);
            _toast = UIKit.Label(_root, "", 28, UIKit.Accent, TextAnchor.MiddleCenter, FontStyle.Bold).Shadowed();
            _toast.rectTransform.Place(0.5f, 0, 0, 18 + S + 116, 900, 40);

            _debug = UIKit.Label(_root, "", 20, Color.white, TextAnchor.UpperLeft).Shadowed(1.5f);
            _debug.rectTransform.Place(0, 1, 16, -12, 900, 400);
            _fps = UIKit.Label(_root, "", 20, Color.white, TextAnchor.UpperRight).Shadowed(1.5f);
            _fps.rectTransform.Place(1, 1, -16, -12, 300, 30);

            BuildInventory();
            BuildChat();
            P.Hurt += amt => { _hurtFlash = Mathf.Clamp01(0.25f + amt * 0.06f); };
            Act.Toast = Toast;
        }

        Image _xpBack, _xp; Text _xpLevel, _craftSub;
        bool Held(Transform t) => _compass != null && (t == _compass || t == _clockFace.transform || t == _map);

        Image Bar(string name, Color col, float x, float y, float w, out Image fill)
        {
            var back = UIKit.Panel(_root, name + "Back", new Color(0.05f, 0.05f, 0.06f, 0.7f), UIKit.RoundSmall);
            back.rectTransform.Place(0.5f, 0, x, y, w, 18);
            back.raycastTarget = false;
            fill = UIKit.Panel(back.transform, name, col, UIKit.RoundSmall);
            fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = new Vector2(1, 1); fill.rectTransform.offsetMin = new Vector2(3, 3); fill.rectTransform.offsetMax = new Vector2(-3, -3);
            fill.raycastTarget = false;
            return back;
        }

        // Lying in bed: the screen fades to dark while the night passes.
        public void Sleeping(bool on, string line = "")
        {
            _sleeping = on; _sleepLine = line;
        }

        Image _portal;
        // The swirl of a portal taking hold (0 none, 1 about to go).
        public void PortalGlow(float k)
        {
            if (_portal == null) return;
            k = Mathf.Clamp01(k);
            _portal.color = new Color(1f, 0.38f + Mathf.Sin(Time.time * 5) * 0.06f, 0.12f, k * k * 0.75f);
            _portal.gameObject.SetActive(k > 0.001f);
        }

        public void Toast(string s) { if (_toasts.Count < 4 && !_toasts.Contains(s)) _toasts.Enqueue(s); }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _root.gameObject.SetActive(true);
            bool show = !Hidden && !InventoryOpen;
            foreach (Transform t in _root) if (t.gameObject != _inv && t.gameObject != _signPanel && !Held(t)) t.gameObject.SetActive(show || t == _hurt.transform || t == _water.transform || t == _sleep.transform || t == _portal.transform);
            UpdateHeld(dt, Enchants.BaseOf(P.Inv.Held.Item), show);

            // Hotbar
            for (int i = 0; i < 9; i++) _hot[i].Show(P.Inv.Slots[i]);
            _select.anchoredPosition = new Vector2(3 + P.Inv.Selected * 90, 0);
            var held = P.Inv.Held;
            if (P.Inv.Selected != _lastSel || held.Item != _lastHeld)
            {
                _lastSel = P.Inv.Selected; _lastHeld = held.Item;
                _name.text = held.Empty ? "" : Items.Get(held.Item)?.Name ?? "";
                _nameTimer = 2f;
            }
            _nameTimer -= dt;
            _name.color = new Color(UIKit.Ink.r, UIKit.Ink.g, UIKit.Ink.b, Mathf.Clamp01(_nameTimer));

            if (_toastTimer <= 0 && _toasts.Count > 0) { _toast.text = _toasts.Dequeue(); _toastTimer = 2.6f; }
            _toastTimer -= dt;
            _toast.color = new Color(UIKit.Accent.r, UIKit.Accent.g, UIKit.Accent.b, Mathf.Clamp01(_toastTimer * 2));

            bool survival = !P.Creative;
            _healthBack.gameObject.SetActive(survival && show);
            _health.rectTransform.anchorMax = new Vector2(Mathf.Max(0.001f, P.Health / P.MaxHealth), 1);
            bool airVisible = survival && show && (P.HeadInWater || P.Air < P.MaxAir - 0.01f);
            _airBack.gameObject.SetActive(airVisible);
            _air.rectTransform.anchorMax = new Vector2(Mathf.Max(0.001f, P.Air / P.MaxAir), 1);
            _foodBack.gameObject.SetActive(survival && show);
            _food.rectTransform.anchorMax = new Vector2(Mathf.Max(0.001f, P.Food / 20f), 1);
            _xpBack.gameObject.SetActive(survival && show);
            _xp.rectTransform.anchorMax = new Vector2(Mathf.Max(0.001f, P.XpPoints / (float)Xp.Next(P.XpLevel)), 1);
            _xpLevel.text = P.XpLevel > 0 ? P.XpLevel.ToString() : "";
            _food.color = P.Food <= 6 ? Color.Lerp(new Color(0.86f, 0.58f, 0.22f), new Color(0.5f, 0.3f, 0.1f), Mathf.PingPong(Time.time * 2, 1)) : new Color(0.86f, 0.58f, 0.22f);
            int arm = P.Inv.ArmourPoints;
            _armourBack.gameObject.SetActive(survival && show && arm > 0);
            _armour.rectTransform.anchorMax = new Vector2(Mathf.Max(0.001f, arm / 20f), 1);
            float use = Act.EatProgress > 0 ? Act.EatProgress : Act.DrawProgress > 0 ? Act.DrawProgress : Act.AttackReady < 1 && Act.HasMob ? Act.AttackReady : 0;
            _useBack.gameObject.SetActive(show && use > 0);
            _use.rectTransform.anchorMax = new Vector2(Mathf.Max(0.001f, use), 1);
            MobInfo(show);
            _sleepFade = Mathf.MoveTowards(_sleepFade, _sleeping ? 1 : 0, dt * (_sleeping ? 0.6f : 2f));
            _sleep.color = new Color(0.01f, 0.01f, 0.04f, _sleepFade * 0.92f);
            _sleepText.text = _sleepFade > 0.3f ? _sleepLine : "";

            _water.gameObject.SetActive(P.HeadInWater);
            _hurtFlash = Mathf.MoveTowards(_hurtFlash, 0, dt * 1.2f);
            _hurt.color = new Color(0.7f, 0.05f, 0.02f, _hurtFlash * 0.45f);

            _fps.gameObject.SetActive(Settings.ShowFps && show && !ShowDebug);
            if (_fps.gameObject.activeSelf) _fps.text = Mathf.RoundToInt(1f / Mathf.Max(0.0001f, Time.smoothDeltaTime)) + " fps";
            _debug.gameObject.SetActive(ShowDebug && show);
            if (ShowDebug) _debug.text = DebugText();

            if (InventoryOpen) UpdateInventory(dt);
            UpdateChat();
        }

        void MobInfo(bool show)
        {
            bool has = show && Act.HasMob && Act.Creatures != null && Act.Creatures.TryGet(Act.TargetMob, out var ms);
            MobState m = default;
            if (has) Act.Creatures.TryGet(Act.TargetMob, out m);
            _mobName.gameObject.SetActive(has); _mobBack.gameObject.SetActive(has);
            if (has)
            {
                _mobName.text = MobSim.Def(m.Kind).Name;
                _mobBar.rectTransform.anchorMax = new Vector2(Mathf.Max(0.001f, m.Health), 1);
            }
            // The block being pointed at gets a small name under the crosshair.
            bool block = show && !has && Act.HasTarget;
            string bn = block ? Blocks.Get(World.Get(Act.Target.x, Act.Target.y, Act.Target.z)).Name : null;
            block = !string.IsNullOrEmpty(bn);
            if (_blockTag.gameObject.activeSelf != block) _blockTag.gameObject.SetActive(block);
            if (block && _blockTagText.text != bn)
            {
                _blockTagText.text = bn;
                _blockTag.rectTransform.sizeDelta = new Vector2(_blockTagText.preferredWidth + 22, 28);
            }
            // A boss nearby shows its health across the top.
            MobState boss = default; bool any = false;
            if (Link != null)
                foreach (var b in Link.Mobs)
                    if (b.Kind == MobKind.Colossus && (b.Pos - P.Feet).sqrMagnitude < 80 * 80) { boss = b; any = true; break; }
            _bossName.gameObject.SetActive(show && any); _bossBack.gameObject.SetActive(show && any);
            if (any) { _bossName.text = MobSim.Def(boss.Kind).Name; _bossBar.rectTransform.anchorMax = new Vector2(Mathf.Max(0.001f, boss.Health), 1); }
        }

        string DebugText()
        {
            var f = P.Feet;
            int bx = Mathf.FloorToInt(f.x), by = Mathf.FloorToInt(f.y), bz = Mathf.FloorToInt(f.z);
            var biome = World.Gen.BiomeAt(bx, bz);
            string facing = Mathf.Repeat(P.Yaw + 45, 360) switch { < 90 => "north (+Z)", < 180 => "east (+X)", < 270 => "south (-Z)", _ => "west (-X)" };
            string target = Act.HasTarget ? $"{Blocks.Get(World.Get(Act.Target.x, Act.Target.y, Act.Target.z)).Name} at {Act.Target.x} {Act.Target.y} {Act.Target.z}" : "nothing";
            return $"Stratum {Application.version}   {Mathf.RoundToInt(1f / Mathf.Max(0.0001f, Time.smoothDeltaTime))} fps\n" +
                   $"XYZ {f.x:0.0} {f.y:0.0} {f.z:0.0}   chunk {bx >> 4} {bz >> 4}\n" +
                   $"Facing {facing}\nBiome {WorldGen.BiomeNames[(int)biome]}\n" +
                   $"Looking at {target}\n" +
                   $"{Sky.Clock()}   seed {World.Gen.Seed}\n" +
                   $"Chunks {World.LoadedChunks}  jobs {World.PendingJobs}   view {World.ViewDistance}\n" +
                   $"Creatures {(Link != null ? Link.Mobs.Count : 0)}   food {P.Food:0}/{P.Saturation:0.0}   armour {P.Inv.ArmourPoints}\n" +
                   (P.Creative ? "Creative" + (P.Flying ? " (flying)" : "") : "Survival") +
                   (OnlineInfo != null ? "\n" + OnlineInfo() : "");
        }

        // ------------------------------------------------------------------ backpack and crafting

        void BuildInventory()
        {
            _inv = new GameObject("Backpack", typeof(RectTransform));
            _inv.transform.SetParent(_root, false);
            var r = (RectTransform)_inv.transform; r.Fill();
            var dim = _inv.AddComponent<Image>(); dim.color = new Color(0, 0, 0, 0.5f);

            var panel = UIKit.Panel(r, "Panel", UIKit.PanelCol);
            panel.rectTransform.Place(0.5f, 0.5f, 0, 0, 1560, 860);
            var title = UIKit.Label(panel.transform, "Backpack", 40, UIKit.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            title.rectTransform.Place(0, 1, 44, -24, 600, 50);
            _tip = UIKit.Label(panel.transform, "", 22, UIKit.Dim, TextAnchor.MiddleLeft);
            _tip.rectTransform.Place(0, 0, 44, 18, 800, 30);

            _invGrid = UIKit.Rect(panel.transform, "Grid").Place(0, 1, 44, -96, 800, 700);
            _palette = UIKit.Rect(panel.transform, "PaletteArea").Place(0, 1, 44, -96, 800, 520);

            var right = UIKit.Panel(panel.transform, "Crafting", new Color(0.11f, 0.12f, 0.15f, 0.9f));
            right.rectTransform.Place(1, 1, -40, -30, 640, 800);
            _craftTitle = UIKit.Label(right.transform, "Crafting", 34, UIKit.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            _craftTitle.rectTransform.Place(0, 1, 28, -16, 600, 46);
            _craftSub = UIKit.Label(right.transform, "", 19, UIKit.Dim);
            _craftSub.rectTransform.Place(0, 1, 28, -60, 600, 28);
            _boxGrid = UIKit.Rect(right.transform, "Chest").Place(0, 1, 26, -110, 600, 260);
            var scroll = UIKit.Scroll(right.transform, out _craftList);
            _craftArea = scroll.gameObject;
            ((RectTransform)scroll.transform).Fill(); ((RectTransform)scroll.transform).offsetMax = new Vector2(-14, -100); ((RectTransform)scroll.transform).offsetMin = new Vector2(14, 14);
            var vl = _craftList.gameObject.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 8; vl.childControlHeight = false; vl.childControlWidth = true; vl.childForceExpandHeight = false; vl.padding = new RectOffset(6, 6, 6, 6);

            // Cursor stack follows the mouse.
            _cursorIcon = UIKit.Icon(r, null); _cursorIcon.rectTransform.sizeDelta = new Vector2(72, 72);
            _cursorCount = UIKit.Label(_cursorIcon.transform, "", 24, Color.white, TextAnchor.LowerRight, FontStyle.Bold).Shadowed();
            _cursorCount.rectTransform.Fill(-4);
            // A small name tag beside the mouse for whatever it's over.
            _tag = UIKit.Panel(r, "NameTag", new Color(0.06f, 0.06f, 0.08f, 0.96f));
            _tag.raycastTarget = false;
            var edge = _tag.gameObject.AddComponent<Outline>(); edge.effectColor = new Color(1, 1, 1, 0.16f); edge.effectDistance = new Vector2(1, -1);
            _tagText = UIKit.Label(_tag.transform, "", 21, UIKit.Ink, TextAnchor.MiddleCenter);
            _tagText.raycastTarget = false; _tagText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _tagText.rectTransform.Fill();
            _tag.gameObject.SetActive(false);
            _inv.SetActive(false);
        }

        public void OpenBox(Vector3Int at, Stack[] contents)
        {
            _boxAt = at;
            _box = (Stack[])contents.Clone();
            if (_box.Length < Containers.ChestSlots) System.Array.Resize(ref _box, Containers.ChestSlots);
            if (InventoryOpen && _station == "chest") return;
            OpenInventory("chest");
            Sfx.I?.Play("chest_open", 0.7f);
        }

        // Someone else changed a slot of the chest we have open.
        public void BoxChanged(Vector3Int at, int slot, Stack s)
        {
            if (_box == null || at != _boxAt || (uint)slot >= (uint)_box.Length) return;
            _box[slot] = s;
        }

        public bool BoxOpen => InventoryOpen && _station == "chest";
        public Vector3Int BoxAt => _boxAt;

        public void OpenInventory(string station)
        {
            _station = station;
            _inv.SetActive(true);
            LayoutSlots();
            RebuildCrafting();
        }

        public void CloseInventory()
        {
            _tagFor = ""; if (_tag != null) _tag.gameObject.SetActive(false);
            if (!_cursor.Empty)
            {
                if (!P.Creative) GiveBack(_cursor);
                _cursor = Stack.None;
            }
            if (_station == "chest") { _box = null; Sfx.I?.Play("chest_close", 0.6f); CloseBoxed?.Invoke(); }
            if (WorkStation) ReturnWork();
            _inv.SetActive(false);
        }

        void LayoutSlots()
        {
            foreach (Transform t in _invGrid) Destroy(t.gameObject);
            _invSlots.Clear();
            foreach (Transform t in _palette) Destroy(t.gameObject);
            const float S = 80, G = 8;
            bool creative = P.Creative;
            _palette.gameObject.SetActive(creative);
            // Main 27 slots (Survival) and the hotbar row.
            if (!creative)
                for (int i = 9; i < 36; i++)
                {
                    int idx = i;
                    var v = SlotView.Make(_invGrid, S);
                    int row = (i - 9) / 9, col = (i - 9) % 9;
                    v.GetComponent<RectTransform>().Place(0, 1, col * (S + G), -row * (S + G), S, S);
                    v.Click = b => SlotClick(idx, b);
                    v.Hover = on => Tip(on ? P.Inv.Slots[idx] : Stack.None);
                    _invSlots.Add(v);
                }
            var hotLabel = UIKit.Label(_invGrid, "Hotbar", 22, UIKit.Dim);
            hotLabel.rectTransform.Place(0, 1, 0, -(creative ? 548 : 290), 300, 30);
            for (int i = 0; i < 9; i++)
            {
                int idx = i;
                var v = SlotView.Make(_invGrid, S);
                v.GetComponent<RectTransform>().Place(0, 1, i * (S + G), -(creative ? 584 : 326), S, S);
                v.Click = b => SlotClick(idx, b);
                v.Hover = on => Tip(on ? P.Inv.Slots[idx] : Stack.None);
                _invSlots.Add(v);
            }
            // Armour: head, body, legs, feet.
            _armSlots.Clear();
            if (!creative)
            {
                var al = UIKit.Label(_invGrid, "Armour", 22, UIKit.Dim);
                al.rectTransform.Place(0, 1, 0, -432, 300, 30);
                string[] names = { "Head", "Body", "Legs", "Feet" };
                for (int i = 0; i < 4; i++)
                {
                    int idx = Inventory.ArmourStart + i, slotType = i;
                    var v = SlotView.Make(_invGrid, S);
                    v.GetComponent<RectTransform>().Place(0, 1, i * (S + G), -468, S, S);
                    v.Click = b => ArmourClick(idx, slotType, b);
                    v.Hover = on => { if (on && P.Inv.Slots[idx].Empty) _tip.text = names[slotType] + " armour goes here"; else Tip(on ? P.Inv.Slots[idx] : Stack.None); };
                    _armSlots.Add(v);
                }
                _armText = UIKit.Label(_invGrid, "", 22, UIKit.Dim);
                _armText.rectTransform.Place(0, 1, 4 * (S + G) + 10, -490, 400, 40);
            }
            // A chest's slots take the crafting panel's place.
            foreach (Transform t in _boxGrid) Destroy(t.gameObject);
            _boxSlots.Clear();
            bool chest = _station == "chest" && _box != null;
            _boxGrid.gameObject.SetActive(chest);
            _craftArea.SetActive(!chest && !WorkStation);
            LayoutWork();
            if (chest)
            {
                const float CS = 62, CG = 5;
                int slots = Mathf.Max(Containers.ChestSlots, _box.Length);        // a satchel can hold a whole pack and more
                _boxGrid.sizeDelta = new Vector2(600, (slots + 8) / 9 * (CS + CG) + 40);
                for (int i = 0; i < slots; i++)
                {
                    int idx = i;
                    var v = SlotView.Make(_boxGrid, CS);
                    v.GetComponent<RectTransform>().Place(0, 1, (i % 9) * (CS + CG), -(i / 9) * (CS + CG), CS, CS);
                    v.Click = b => BoxClick(idx, b);
                    v.Hover = on => Tip(on && _box != null ? _box[idx] : Stack.None);
                    _boxSlots.Add(v);
                }
                var hint = UIKit.Label(_boxGrid, "Shift-click moves a stack between the chest and your pack.", 19, UIKit.Dim);
                hint.rectTransform.Place(0, 1, 0, -((slots + 8) / 9) * (CS + CG) - 10, 600, 30);
            }
            if (creative)
            {
                var sc = UIKit.Scroll(_palette, out var content);
                ((RectTransform)sc.transform).Fill();
                var grid = content.gameObject.AddComponent<GridLayoutGroup>();
                grid.cellSize = new Vector2(S, S); grid.spacing = new Vector2(G, G); grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = 9;
                foreach (var it in Items.Order)
                {
                    if (!it.Creative) continue;
                    var v = SlotView.Make(content, S);
                    var st = new Stack(it.Id, 1);
                    v.Show(st);
                    v.Click = b => { _cursor = _cursor.Empty ? new Stack(it.Id, b == PointerEventData.InputButton.Right ? 1 : it.MaxStack) : Stack.None; Sfx.I?.Play("click", 0.3f); };
                    v.Hover = on => Tip(on ? st : Stack.None);
                }
            }
        }

        void Tip(Stack s)
        {
            if (s.Empty) { _tagFor = ""; _tip.text = P.Creative ? "Pick anything up from the palette. Click empty space to clear." : ""; return; }
            var d = Items.Get(s.Item);
            string extra = d.Durability > 0 ? $"  ·  {d.Durability - s.Wear} uses left" : d.Tool != Tool.None ? "" : "";
            _tip.text = d.Name + (s.Count > 1 ? $" × {s.Count}" : "") + extra;
            _tagFor = d.Name;
        }

        // What a click does to a slot with whatever the cursor holds (pick up, put down, swap, split, add one).
        void CursorClick(ref Stack slot, PointerEventData.InputButton button)
        {
            if (button == PointerEventData.InputButton.Left)
            {
                if (_cursor.Empty) { _cursor = slot; slot = Stack.None; }
                else if (slot.Empty) { slot = _cursor; _cursor = Stack.None; }
                else if (slot.Item == _cursor.Item && slot.Wear == 0 && _cursor.Wear == 0)
                {
                    int max = Items.Get(slot.Item).MaxStack, n = Mathf.Min(max - slot.Count, _cursor.Count);
                    slot.Count += n; _cursor.Count -= n; if (_cursor.Count <= 0) _cursor = Stack.None;
                }
                else { var t = slot; slot = _cursor; _cursor = t; }
            }
            else if (button == PointerEventData.InputButton.Right)
            {
                if (_cursor.Empty && !slot.Empty) { int n = (slot.Count + 1) / 2; _cursor = slot; _cursor.Count = n; slot.Count -= n; if (slot.Count <= 0) slot = Stack.None; }
                else if (!_cursor.Empty && (slot.Empty || slot.Item == _cursor.Item && slot.Count < Items.Get(slot.Item).MaxStack))
                {
                    if (slot.Empty) { slot = _cursor; slot.Count = 1; } else slot.Count++;
                    _cursor.Count--; if (_cursor.Count <= 0) _cursor = Stack.None;
                }
            }
            Sfx.I?.Play("click", 0.25f);
            _tagFor = "";          // what was under the pointer has changed
        }

        void ArmourClick(int i, int type, PointerEventData.InputButton button)
        {
            ref var slot = ref P.Inv.Slots[i];
            if (!_cursor.Empty)
            {
                var d = Items.Get(_cursor.Item);
                if (d == null || d.ArmourSlot != type) { Toast("That doesn't go there"); return; }
                if (button == PointerEventData.InputButton.Right) button = PointerEventData.InputButton.Left;
            }
            CursorClick(ref slot, button);
        }

        void BoxClick(int i, PointerEventData.InputButton button)
        {
            if (_box == null) return;
            var kb = Keyboard.current;
            bool shift = kb != null && kb.leftShiftKey.isPressed;
            if (shift && button == PointerEventData.InputButton.Left && !_box[i].Empty)
            {
                var s = _box[i];
                int left;
                if (s.Wear > 0) { left = s.Count; for (int k = 0; k < Inventory.Main; k++) if (P.Inv.Slots[k].Empty) { P.Inv.Slots[k] = s; left = 0; break; } }
                else left = P.Inv.Add(s.Item, s.Count);
                _box[i] = left > 0 ? new Stack(s.Item, left) { Wear = s.Wear } : Stack.None;
                Sfx.I?.Play("click", 0.3f);
            }
            else CursorClick(ref _box[i], button);
            Link?.BoxSlot(_boxAt, i, _box[i]);
            RebuildCraftingSoon();
        }

        // Shift-click in the pack with a chest open: the stack goes into the chest.
        bool ToBox(int i)
        {
            if (_box == null || _station != "chest") return false;
            ref var slot = ref P.Inv.Slots[i];
            var s = slot;
            int max = Items.Get(s.Item).MaxStack;
            for (int k = 0; k < _box.Length && s.Count > 0; k++)
            {
                if (_box[k].Empty || _box[k].Item != s.Item || _box[k].Wear != 0 || s.Wear != 0 || _box[k].Count >= max) continue;
                int n = Mathf.Min(max - _box[k].Count, s.Count); _box[k].Count += n; s.Count -= n; Link?.BoxSlot(_boxAt, k, _box[k]);
            }
            for (int k = 0; k < _box.Length && s.Count > 0; k++)
                if (_box[k].Empty) { _box[k] = s; s.Count = 0; Link?.BoxSlot(_boxAt, k, _box[k]); }
            slot = s.Count > 0 ? s : Stack.None;
            Sfx.I?.Play("click", 0.3f);
            return true;
        }

        void SlotClick(int i, PointerEventData.InputButton button)
        {
            var kb = Keyboard.current;
            bool shift = kb != null && kb.leftShiftKey.isPressed;
            ref var slot = ref P.Inv.Slots[i];
            if (shift && button == PointerEventData.InputButton.Left && !slot.Empty && ToBox(i)) { RebuildCraftingSoon(); return; }
            if (shift && button == PointerEventData.InputButton.Left && !slot.Empty && Items.Get(slot.Item).ArmourSlot >= 0 && P.Inv.Slots[Inventory.ArmourStart + Items.Get(slot.Item).ArmourSlot].Empty)
            {
                P.Inv.Slots[Inventory.ArmourStart + Items.Get(slot.Item).ArmourSlot] = slot; slot = Stack.None;
                Sfx.I?.Play("click", 0.3f);
                return;
            }
            if (shift && button == PointerEventData.InputButton.Left && !slot.Empty)
            {
                // Quick move between hotbar and pack.
                var s = slot; slot = Stack.None;
                int from = i < 9 ? 9 : 0, to = i < 9 ? 36 : 9;
                for (int k = from; k < to && s.Count > 0; k++)
                {
                    ref var t = ref P.Inv.Slots[k];
                    int max = Items.Get(s.Item).MaxStack;
                    if (!t.Empty && t.Item == s.Item && t.Count < max) { int n = Mathf.Min(max - t.Count, s.Count); t.Count += n; s.Count -= n; }
                }
                for (int k = from; k < to && s.Count > 0; k++) { ref var t = ref P.Inv.Slots[k]; if (t.Empty) { t = s; s.Count = 0; } }
                if (s.Count > 0) slot = s;
                Sfx.I?.Play("click", 0.3f);
                return;
            }
            CursorClick(ref slot, button);
            RebuildCraftingSoon();
        }

        void RebuildCraftingSoon() => _rebuildTimer = 0.05f;

        bool Near(byte block, int radius)
        {
            var f = P.Feet;
            int x = Mathf.FloorToInt(f.x), y = Mathf.FloorToInt(f.y), z = Mathf.FloorToInt(f.z);
            for (int dy = -2; dy <= 3; dy++)
                for (int dz = -radius; dz <= radius; dz++)
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        byte b = World.Get(x + dx, y + dy, z + dz);
                        if (b == block || Blocks.Get(b).BaseId == block) return true;
                    }
            return false;
        }

        void RebuildCrafting()
        {
            foreach (Transform t in _craftList) Destroy(t.gameObject);
            if (P.Creative)
            {
                _craftTitle.text = "Creative";
                var l = UIKit.Label(_craftList, "Every block and tool is in the palette.\n\nDouble-tap Space to fly, Space and Ctrl to rise and sink.\nMiddle-click a block to pick it.", 24, UIKit.Dim);
                l.rectTransform.sizeDelta = new Vector2(560, 300);
                return;
            }
            bool bench = _station == "workbench" || Near(Blocks.Workbench, 4);
            bool furnace = _station == "furnace" || Near(Blocks.Furnace, 4);
            _craftSub.text = _station == "enchant" ? "Each offer costs levels and sunstone shards." : _station == "anvil" ? "Mend tools and add enchanted books, for levels." : _station == "trade" ? "Jade is what villagers count in." : _station == "chest" ? "" : "Click to make one, Shift-click to make as many as you can.";
            if (WorkStation) { _craftTitle.text = _station == "enchant" ? "Enchanting Table" : _station == "anvil" ? "Anvil" : Trades.Jobs[Mathf.Clamp(_tradeJob, 0, Trades.Jobs.Length - 1)]; return; }
            _craftTitle.text = _station == "chest" ? (World.Get(_boxAt.x, _boxAt.y, _boxAt.z) == Blocks.Satchel ? "Satchel" : "Chest") : furnace && _station == "furnace" ? "Furnace" : bench ? "Workbench" : "Crafting";
            var list = Items.Recipes
                .Select(r => (r, can: P.Inv.CanCraft(r), here: r.Station == null || r.Station == "workbench" && bench || r.Station == "furnace" && furnace))
                .Select(x => (x.r, x.can, x.here, sec: Section(x.r, x.here)))
                .OrderBy(x => x.sec).ThenByDescending(x => x.can).ToList();
            int shown = -1;
            bool several = list.Select(x => x.sec).Distinct().Count() > 1;
            foreach (var (r, can, here, sec) in list)
            {
                if (several && sec != shown)
                {
                    shown = sec;
                    var head = UIKit.Label(_craftList, sec == 3 ? "By hand" : sec == 4 ? "Needs a station nearby" : r.Station == "furnace" ? "Furnace" : "Workbench", 20, UIKit.Accent, TextAnchor.LowerLeft, FontStyle.Bold);
                    head.rectTransform.sizeDelta = new Vector2(0, 34);
                }
                var row = UIKit.Panel(_craftList, "Recipe", new Color(0.16f, 0.17f, 0.21f, here ? 0.95f : 0.5f), UIKit.RoundSmall);
                row.rectTransform.sizeDelta = new Vector2(0, 84);
                var icon = UIKit.Icon(row.transform, Icons.Get(r.Out)); icon.rectTransform.Place(0, 0.5f, 10, 0, 64, 64);
                if (!here) icon.color = new Color(1, 1, 1, 0.45f);
                var name = UIKit.Label(row.transform, Items.Get(r.Out).Name + (r.Count > 1 ? $"  ×{r.Count}" : ""), 26, here ? UIKit.Ink : UIKit.Dim, TextAnchor.UpperLeft, FontStyle.Bold);
                name.rectTransform.Place(0, 1, 88, -10, 360, 34);
                string need = string.Join("   ", r.In.Select(x =>
                {
                    bool ok = P.Inv.Count(x.item) >= x.count;
                    return $"<color=#{ColorUtility.ToHtmlStringRGB(ok ? UIKit.Good : UIKit.Bad)}>{x.count} {Items.DisplayName(x.item)}</color>";
                }));
                if (!here) need = r.Station == "furnace" ? "at a Furnace" : "at a Workbench";
                var needL = UIKit.Label(row.transform, need, 20, UIKit.Dim, TextAnchor.UpperLeft);
                needL.supportRichText = true;
                needL.rectTransform.Place(0, 1, 88, -46, 380, 30);
                if (here)
                {
                    var rr = r;
                    var b = UIKit.Button(row.transform, "MAKE", () => Craft(rr), 22, can);
                    b.interactable = can;
                    ((RectTransform)b.transform).Place(1, 0.5f, -12, 0, 110, 52);
                }
            }
        }

        // The open station's recipes first, then any other station in reach, by hand, and what needs a station.
        int Section(Recipe r, bool here) => !here ? 4 : r.Station == null ? 3 : r.Station == _station ? 0 : r.Station == "workbench" ? 1 : 2;

        void Craft(Recipe r)
        {
            var kb = Keyboard.current;
            bool all = kb != null && kb.leftShiftKey.isPressed;
            int made = 0;
            do { if (!P.Inv.Craft(r)) break; made++; } while (all && made < 64);
            if (made > 0) Sfx.I?.Play("craft", 0.6f);
            if (made > 0 && r.Station == "furnace") P.AddXp((made + Random.Range(0, 2)) / 2);
            else Toast("No room for it");
            RebuildCraftingSoon();
        }

        void UpdateInventory(float dt)
        {
            int k = 0;
            if (!P.Creative) for (int i = 9; i < 36; i++) _invSlots[k++].Show(P.Inv.Slots[i]);
            for (int i = 0; i < 9; i++) _invSlots[k++].Show(P.Inv.Slots[i]);
            for (int i = 0; i < _armSlots.Count; i++) _armSlots[i].Show(P.Inv.Slots[Inventory.ArmourStart + i]);
            if (_armText != null) _armText.text = P.Inv.ArmourPoints > 0 ? $"Protection {P.Inv.ArmourPoints * 4}%" : "";
            if (_box != null) for (int i = 0; i < _boxSlots.Count; i++) _boxSlots[i].Show(_box[i]);
            UpdateWork();
            var sp = _cursor.Empty ? null : Icons.Get(_cursor.Item);
            _cursorIcon.sprite = sp; _cursorIcon.enabled = sp != null;
            _cursorCount.text = !_cursor.Empty && _cursor.Count > 1 ? _cursor.Count.ToString() : "";
            if (Mouse.current != null)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)_inv.transform, Mouse.current.position.ReadValue(), null, out var lp);
                _cursorIcon.rectTransform.anchoredPosition = lp;
                _cursorIcon.rectTransform.anchorMin = _cursorIcon.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                _cursorIcon.transform.SetAsLastSibling();
                // The name tag sits up and to the right of the pointer, kept on the screen; hidden while carrying something.
                bool show = _tagFor != "" && _cursor.Empty;
                if (_tag.gameObject.activeSelf != show) _tag.gameObject.SetActive(show);
                if (show)
                {
                    if (_tagText.text != _tagFor) _tagText.text = _tagFor;
                    var box = (RectTransform)_inv.transform; var tr = _tag.rectTransform;
                    float w = _tagText.preferredWidth + 26, h = 36;
                    tr.anchorMin = tr.anchorMax = new Vector2(0.5f, 0.5f); tr.pivot = new Vector2(0, 0); tr.sizeDelta = new Vector2(w, h);
                    float hw = box.rect.width / 2, hh = box.rect.height / 2;
                    tr.anchoredPosition = new Vector2(Mathf.Min(lp.x + 18, hw - w - 6), Mathf.Min(lp.y + 14, hh - h - 6));
                    _tag.transform.SetAsLastSibling();
                }
                // Clicking outside every slot in Creative clears the cursor.
                if (P.Creative && Mouse.current.leftButton.wasPressedThisFrame && !_cursor.Empty && EventSystem.current != null && !EventSystem.current.IsPointerOverGameObject())
                    _cursor = Stack.None;
            }
            if (_rebuildTimer > 0) { _rebuildTimer -= dt; if (_rebuildTimer <= 0) RebuildCrafting(); }
        }
    }
}
