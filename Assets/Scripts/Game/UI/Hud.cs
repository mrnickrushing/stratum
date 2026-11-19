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
    public sealed class Hud : MonoBehaviour
    {
        public Player P; public Interaction Act; public Sky Sky; public VoxelWorld World;
        Canvas _canvas;
        RectTransform _root;
        readonly SlotView[] _hot = new SlotView[9];
        RectTransform _select;
        Image _health, _healthBack, _air, _airBack, _water, _hurt;
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
        Text _tip;
        Stack _cursor;
        string _station;
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
            _healthBack.rectTransform.Place(0.5f, 0, -bw / 2 - 5, 18 + S + 24, bw, 18);
            _health = UIKit.Panel(_healthBack.transform, "Health", new Color(0.86f, 0.22f, 0.2f), UIKit.RoundSmall);
            _health.rectTransform.anchorMin = Vector2.zero; _health.rectTransform.anchorMax = new Vector2(1, 1); _health.rectTransform.offsetMin = new Vector2(3, 3); _health.rectTransform.offsetMax = new Vector2(-3, -3);
            _airBack = UIKit.Panel(_root, "AirBack", new Color(0.05f, 0.05f, 0.06f, 0.7f), UIKit.RoundSmall);
            _airBack.rectTransform.Place(0.5f, 0, bw / 2 + 5, 18 + S + 24, bw, 18);
            _air = UIKit.Panel(_airBack.transform, "Air", new Color(0.45f, 0.75f, 1f), UIKit.RoundSmall);
            _air.rectTransform.anchorMin = Vector2.zero; _air.rectTransform.anchorMax = new Vector2(1, 1); _air.rectTransform.offsetMin = new Vector2(3, 3); _air.rectTransform.offsetMax = new Vector2(-3, -3);

            _name = UIKit.Label(_root, "", 30, UIKit.Ink, TextAnchor.MiddleCenter, FontStyle.Bold).Shadowed();
            _name.rectTransform.Place(0.5f, 0, 0, 18 + S + 56, 800, 40);
            _toast = UIKit.Label(_root, "", 28, UIKit.Accent, TextAnchor.MiddleCenter, FontStyle.Bold).Shadowed();
            _toast.rectTransform.Place(0.5f, 0, 0, 18 + S + 100, 900, 40);

            _debug = UIKit.Label(_root, "", 20, Color.white, TextAnchor.UpperLeft).Shadowed(1.5f);
            _debug.rectTransform.Place(0, 1, 16, -12, 900, 400);
            _fps = UIKit.Label(_root, "", 20, Color.white, TextAnchor.UpperRight).Shadowed(1.5f);
            _fps.rectTransform.Place(1, 1, -16, -12, 300, 30);

            BuildInventory();
            P.Hurt += amt => { _hurtFlash = Mathf.Clamp01(0.25f + amt * 0.06f); };
            Act.Toast = Toast;
        }

        public void Toast(string s) { if (_toasts.Count < 4 && !_toasts.Contains(s)) _toasts.Enqueue(s); }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _root.gameObject.SetActive(true);
            bool show = !Hidden && !InventoryOpen;
            foreach (Transform t in _root) if (t.gameObject != _inv) t.gameObject.SetActive(show || t == _hurt.transform || t == _water.transform);

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

            _water.gameObject.SetActive(P.HeadInWater);
            _hurtFlash = Mathf.MoveTowards(_hurtFlash, 0, dt * 1.2f);
            _hurt.color = new Color(0.7f, 0.05f, 0.02f, _hurtFlash * 0.45f);

            _fps.gameObject.SetActive(Settings.ShowFps && show && !ShowDebug);
            if (_fps.gameObject.activeSelf) _fps.text = Mathf.RoundToInt(1f / Mathf.Max(0.0001f, Time.smoothDeltaTime)) + " fps";
            _debug.gameObject.SetActive(ShowDebug && show);
            if (ShowDebug) _debug.text = DebugText();

            if (InventoryOpen) UpdateInventory(dt);
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
                   (P.Creative ? "Creative" + (P.Flying ? " (flying)" : "") : "Survival");
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
            var sub = UIKit.Label(right.transform, "Click to make one, Shift-click to make as many as you can.", 19, UIKit.Dim);
            sub.rectTransform.Place(0, 1, 28, -60, 600, 28);
            var scroll = UIKit.Scroll(right.transform, out _craftList);
            ((RectTransform)scroll.transform).Fill(); ((RectTransform)scroll.transform).offsetMax = new Vector2(-14, -100); ((RectTransform)scroll.transform).offsetMin = new Vector2(14, 14);
            var vl = _craftList.gameObject.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 8; vl.childControlHeight = false; vl.childControlWidth = true; vl.childForceExpandHeight = false; vl.padding = new RectOffset(6, 6, 6, 6);

            // Cursor stack follows the mouse.
            _cursorIcon = UIKit.Icon(r, null); _cursorIcon.rectTransform.sizeDelta = new Vector2(72, 72);
            _cursorCount = UIKit.Label(_cursorIcon.transform, "", 24, Color.white, TextAnchor.LowerRight, FontStyle.Bold).Shadowed();
            _cursorCount.rectTransform.Fill(-4);
            _inv.SetActive(false);
        }

        public void OpenInventory(string station)
        {
            _station = station;
            _inv.SetActive(true);
            LayoutSlots();
            RebuildCrafting();
        }

        public void CloseInventory()
        {
            if (!_cursor.Empty)
            {
                if (!P.Creative) { int left = P.Inv.Add(_cursor.Item, _cursor.Count); if (left > 0) Toast("Your pack is full"); }
                _cursor = Stack.None;
            }
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
            if (s.Empty) { _tip.text = P.Creative ? "Pick anything up from the palette. Click empty space to clear." : ""; return; }
            var d = Items.Get(s.Item);
            string extra = d.Durability > 0 ? $"  ·  {d.Durability - s.Wear} uses left" : d.Tool != Tool.None ? "" : "";
            _tip.text = d.Name + (s.Count > 1 ? $" × {s.Count}" : "") + extra;
        }

        void SlotClick(int i, PointerEventData.InputButton button)
        {
            var kb = Keyboard.current;
            bool shift = kb != null && kb.leftShiftKey.isPressed;
            ref var slot = ref P.Inv.Slots[i];
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
            _craftTitle.text = furnace && _station == "furnace" ? "Furnace" : bench ? "Workbench" : "Crafting";
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
            else Toast("No room for it");
            RebuildCraftingSoon();
        }

        void UpdateInventory(float dt)
        {
            int k = 0;
            if (!P.Creative) for (int i = 9; i < 36; i++) _invSlots[k++].Show(P.Inv.Slots[i]);
            for (int i = 0; i < 9; i++) _invSlots[k++].Show(P.Inv.Slots[i]);
            var sp = _cursor.Empty ? null : Icons.Get(_cursor.Item);
            _cursorIcon.sprite = sp; _cursorIcon.enabled = sp != null;
            _cursorCount.text = !_cursor.Empty && _cursor.Count > 1 ? _cursor.Count.ToString() : "";
            if (Mouse.current != null)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)_inv.transform, Mouse.current.position.ReadValue(), null, out var lp);
                _cursorIcon.rectTransform.anchoredPosition = lp;
                _cursorIcon.rectTransform.anchorMin = _cursorIcon.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                _cursorIcon.transform.SetAsLastSibling();
                // Clicking outside every slot in Creative clears the cursor.
                if (P.Creative && Mouse.current.leftButton.wasPressedThisFrame && !_cursor.Empty && EventSystem.current != null && !EventSystem.current.IsPointerOverGameObject())
                    _cursor = Stack.None;
            }
            if (_rebuildTimer > 0) { _rebuildTimer -= dt; if (_rebuildTimer <= 0) RebuildCrafting(); }
        }
    }
}
