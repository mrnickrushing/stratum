using System;
using System.Collections.Generic;
using Stratum.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Stratum.Game
{
    // The Extras page: free packs, each with a USE button. Worlds open a ready-made world (made the first time,
    // reopened after that); looks repaint every block in another style; outfits change what other players see you
    // wearing (one of them you put together yourself, piece by piece); bundles switch on a world, a look and an
    // outfit that go together.
    public sealed partial class Menus
    {
        public Action<WorldPack> UseWorld;
        public Action LookChanged;

        GameObject _extras, _maker; RectTransform _extraGrid, _makerFigure, _makerRows; Text _extraNote; int _extraTab;
        readonly Button[] _tabs = new Button[4];
        static readonly string[] TabNames = { "Worlds", "Looks", "Outfits", "Bundles" };
        static readonly string[] TabNotes =
        {
            "Everything here is free. USE makes the world and takes you there.",
            "Everything here is free. A look repaints every block; your worlds stay the same.",
            "Everything here is free. Other players see the outfit you wear.",
            "Everything here is free. A bundle switches on a world, a look and an outfit together.",
        };
        readonly List<Texture2D> _lookPics = new List<Texture2D>();

        void BuildExtras()
        {
            _extras = Screen("Extras");
            var card = Card(_extras, 1420, 940, "Extras");
            for (int i = 0; i < 4; i++)
            {
                int tab = i;
                _tabs[i] = UIKit.Button(card.transform, TabNames[i], () => { _extraTab = tab; FillExtras(); }, 28);
                ((RectTransform)_tabs[i].transform).Place(0.5f, 1, -435 + i * 290, -100, 270, 60);
            }
            var scroll = UIKit.Scroll(card.transform, out _extraGrid);
            var sr = (RectTransform)scroll.transform; sr.Fill(); sr.offsetMin = new Vector2(50, 124); sr.offsetMax = new Vector2(-50, -180);
            var grid = _extraGrid.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(420, 410); grid.spacing = new Vector2(30, 24); grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = 3;
            grid.childAlignment = TextAnchor.UpperCenter; grid.padding = new RectOffset(0, 0, 4, 12);
            _extraNote = UIKit.Label(card.transform, "", 22, UIKit.Dim, TextAnchor.MiddleLeft);
            _extraNote.rectTransform.Place(0, 0, 56, 52, 900, 40);
            var back = UIKit.Button(card.transform, "Back", ShowMain, 30);
            ((RectTransform)back.transform).Place(1, 0, -56, 36, 320, 72);
            BuildMaker();
        }

        public void ShowExtras() { Show(_extras, true); FillExtras(); }

        void FillExtras()
        {
            foreach (Transform t in _extraGrid) Destroy(t.gameObject);
            foreach (var t in _lookPics) Destroy(t);
            _lookPics.Clear();
            for (int i = 0; i < 4; i++)
                ((Image)_tabs[i].targetGraphic).color = i == _extraTab ? new Color(0.36f, 0.29f, 0.14f, 1) : new Color(0.17f, 0.18f, 0.22f, 0.95f);
            _extraNote.text = TabNotes[_extraTab];
            if (_extraTab == 0) FillWorldPacks(); else if (_extraTab == 1) FillLooks(); else if (_extraTab == 2) FillOutfits(); else FillBundles();
        }

        RectTransform Tile(string name, string tag, string desc, out RectTransform picture)
        {
            var tile = UIKit.Panel(_extraGrid, "Pack", new Color(0.15f, 0.16f, 0.2f, 0.97f), UIKit.RoundSmall);
            picture = UIKit.Rect(tile.transform, "Picture").Place(0.5f, 1, 0, -10, 400, 196);
            var n = UIKit.Label(tile.transform, name, 28, UIKit.Ink, TextAnchor.UpperLeft, FontStyle.Bold);
            n.rectTransform.Place(0, 1, 16, -212, 388, 36);
            var t = UIKit.Label(tile.transform, tag, 18, UIKit.Accent, TextAnchor.UpperLeft);
            t.rectTransform.Place(0, 1, 16, -248, 388, 24);
            var d = UIKit.Label(tile.transform, desc, 18, UIKit.Dim, TextAnchor.UpperLeft);
            d.rectTransform.Place(0, 1, 16, -274, 388, 70);
            d.horizontalOverflow = HorizontalWrapMode.Wrap; d.verticalOverflow = VerticalWrapMode.Truncate;
            return tile.rectTransform;
        }

        static void PackPicture(RectTransform pic, string id)
        {
            var raw = pic.gameObject.AddComponent<RawImage>();
            var tex = id != null ? Resources.Load<Texture2D>("Extras/" + id) : null;
            raw.texture = tex; raw.color = tex != null ? Color.white : new Color(0.22f, 0.24f, 0.28f, 1);
        }

        void FillWorldPacks()
        {
            var mine = ListWorlds?.Invoke() ?? new List<WorldMeta>();
            foreach (var p in Extras.Worlds)
            {
                var pack = p;
                var tile = Tile(p.Name, p.Tag, p.Desc, out var pic);
                PackPicture(pic, p.Id);
                bool made = mine.Exists(w => w.name == p.Name && w.seed == p.Seed && w.kind == (int)p.Kind);
                var b = UIKit.Button(tile, made ? "PLAY" : "USE", () => UseWorld?.Invoke(pack), 26, true);
                ((RectTransform)b.transform).Place(1, 0, -14, 12, 150, 50);
                if (made)
                {
                    var have = UIKit.Label(tile, "In your worlds", 18, new Color(0.55f, 0.85f, 0.45f), TextAnchor.MiddleLeft);
                    have.rectTransform.Place(0, 0, 16, 22, 220, 30);
                }
            }
        }

        // ------------------------------------------------------------------ looks
        static readonly Tex[] LookSample = { Tex.GrassSide, Tex.OakPlanks, Tex.ClayBricks, Tex.Stone, Tex.OakLog, Tex.StoneBricks, Tex.WoolRed, Tex.Cobble };

        void FillLooks()
        {
            int now = Looks.Current;
            // The usual look first, then the rest in their order.
            for (int n = 0; n < Looks.All.Length; n++)
            {
                int i = n == 0 ? Looks.Default : n <= Looks.Default ? n - 1 : n;
                int idx = i; var look = Looks.All[i];
                var tile = Tile(look.Name, i == Looks.Default ? "Look · the usual one" : "Look", look.Desc, out var pic);
                // Eight blocks side by side in this look.
                const int S = Paint.S;
                var tex = new Texture2D(S * 4, S * 2, TextureFormat.RGBA32, false) { filterMode = i == 1 || i == 5 ? FilterMode.Point : FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                for (int k = 0; k < 8; k++)
                {
                    var px = BlockTextures.Sample(LookSample[k], i);
                    for (int j = 0; j < px.Length; j++)
                    {
                        if (LookSample[k] == Tex.GrassSide && px[j].a < 0.5f) px[j] = new Color(0.36f, 0.6f, 0.24f, 1);
                        px[j].a = 1;
                    }
                    tex.SetPixels((k % 4) * S, (1 - k / 4) * S, S, S, px);
                }
                tex.Apply(false, true);
                _lookPics.Add(tex);
                pic.gameObject.AddComponent<RawImage>().texture = tex;
                bool on = i == now;
                var b = UIKit.Button(tile, on ? "IN USE" : "USE", () => { Looks.Current = idx; LookChanged?.Invoke(); FillExtras(); }, 26, !on);
                ((RectTransform)b.transform).Place(1, 0, -14, 12, 170, 50);
                b.interactable = !on;
            }
        }

        // ------------------------------------------------------------------ outfits
        void FillOutfits()
        {
            int wearing = Extras.Outfit;
            {
                // The one you make yourself comes first.
                var mine = CustomLook.Mine;
                var tile = Tile("Make Your Own", "Outfit · piece by piece", "Pick the hat, the colours of every piece, your hair and a cape.", out var pic);
                Backdrop(pic);
                Figure(pic, mine.Tunic, mine.Hood, mine.Trim, mine.Trousers, mine.Boots, mine.Hat, mine.Cape, mine.Hair, SkinOf(mine.Skin));
                bool on = wearing == Extras.Custom;
                var edit = UIKit.Button(tile, "EDIT", ShowMaker, 26, true);
                ((RectTransform)edit.transform).Place(1, 0, -14, 12, 130, 50);
                var wear = UIKit.Button(tile, on ? "WEARING" : "USE", () => { Extras.Outfit = Extras.Custom; CustomLook.SaveMine(); FillExtras(); }, 26, !on);
                ((RectTransform)wear.transform).Place(1, 0, -154, 12, 170, 50);
                wear.interactable = !on;
            }
            for (int i = 0; i < Extras.Outfits.Length; i++)
            {
                int idx = i; var o = Extras.Outfits[i];
                var tile = Tile(o.Name, i == 0 ? "Outfit · the usual one" : "Outfit", o.Desc, out var pic);
                Backdrop(pic);
                Figure(pic, o.Tunic, o.Hood, o.Trim, o.Trousers, o.Boots, 0, false, Color.black, SkinTones[0]);
                bool on = i == wearing;
                var b = UIKit.Button(tile, on ? "WEARING" : "USE", () => { Extras.Outfit = idx; FillExtras(); }, 26, !on);
                ((RectTransform)b.transform).Place(1, 0, -14, 12, 170, 50);
                b.interactable = !on;
            }
        }

        static void Backdrop(RectTransform pic)
        {
            var back = pic.gameObject.AddComponent<Image>(); back.sprite = UIKit.RoundSmall; back.type = Image.Type.Sliced;
            back.color = new Color(0.09f, 0.10f, 0.13f, 1);
        }

        static readonly Color[] SkinTones = { new Color(0.86f, 0.68f, 0.54f), new Color(0.93f, 0.76f, 0.62f), new Color(0.80f, 0.60f, 0.45f), new Color(0.62f, 0.44f, 0.30f), new Color(0.42f, 0.29f, 0.20f) };
        static Color SkinOf(int skin) => SkinTones[Mathf.Clamp(skin, 0, SkinTones.Length - 1)];

        // A little blocky figure: hat or hood, face, tunic with a clasp and belt, arms, legs, boots, perhaps a cape.
        static void Figure(RectTransform parent, Color tunic, Color hood, Color trim, Color trousers, Color boots, int hat, bool cape, Color hair, Color skin, float k = 1)
        {
            void Box(float x, float y, float w, float h, Color c)
            {
                var r = UIKit.Rect(parent, "Part").Place(0.5f, 0, x * k, y * k, w * k, h * k);
                r.pivot = new Vector2(0.5f, 0); r.anchoredPosition = new Vector2(x * k, y * k);
                var img = r.gameObject.AddComponent<Image>(); c.a = 1; img.color = c; img.raycastTarget = false;
            }
            var dark = new Color(0.08f, 0.07f, 0.07f);
            if (cape) Box(0, 22, 66, 108, trim * 0.8f);                                         // behind everything
            Box(-13, 12, 22, 14, boots); Box(13, 12, 22, 14, boots);                            // boots
            Box(-13, 26, 20, 44, trousers); Box(13, 26, 20, 44, trousers);                      // legs
            Box(0, 70, 50, 62, tunic);                                                          // tunic
            Box(0, 70, 52, 8, boots);                                                           // belt
            Box(0, 116, 18, 10, trim);                                                          // clasp
            Box(-36, 76, 18, 56, tunic * 0.9f); Box(36, 76, 18, 56, tunic * 0.9f);              // arms
            Box(-36, 66, 16, 12, skin); Box(36, 66, 16, 12, skin);                              // hands
            if (hat == 0 || hat == 5)
            {
                Box(0, 132, 48, 46, hood);                                                      // hood or helmet
                Box(0, 136, 36, 30, skin);
                if (hat == 5) Box(0, 136, 6, 30, hood);
            }
            else
            {
                Box(0, 132, 44, 44, skin);
                Box(0, 166, 46, 12, hair); Box(-21, 146, 6, 26, hair); Box(21, 146, 6, 26, hair);
                if (hat == 2) { Box(0, 170, 48, 12, hood); Box(14, 166, 34, 6, hood); }
                else if (hat == 3) { Box(0, 172, 46, 8, trim); Box(-16, 180, 8, 8, trim); Box(0, 180, 8, 10, trim); Box(16, 180, 8, 8, trim); }
                else if (hat == 4) { Box(0, 172, 70, 6, hood); Box(0, 178, 40, 14, hood); Box(0, 178, 42, 5, trim); Box(2, 192, 26, 14, hood); Box(5, 206, 12, 12, hood); }
            }
            Box(-9, 150, 7, 7, dark); Box(9, 150, 7, 7, dark);                                  // eyes
        }

        // ------------------------------------------------------------------ the outfit maker
        static readonly Color[] MakerColours = MakeColours();
        static Color[] MakeColours()
        {
            var list = new List<Color>();
            foreach (var (_, _, rgb) in Blocks.Dyes) list.Add(new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1));
            list.Add(new Color(0.96f, 0.80f, 0.28f)); list.Add(new Color(0.84f, 0.76f, 0.60f)); list.Add(new Color(0.25f, 0.17f, 0.10f)); list.Add(new Color(0.55f, 0.12f, 0.16f));
            return list.ToArray();
        }

        void BuildMaker()
        {
            _maker = Screen("OutfitMaker");
            var card = Card(_maker, 1420, 940, "Make Your Own Outfit");
            var stage = UIKit.Panel(card.transform, "Stage", new Color(0.09f, 0.10f, 0.13f, 1), UIKit.RoundSmall);
            stage.rectTransform.Place(0, 1, 60, -120, 420, 640);
            _makerFigure = UIKit.Rect(stage.transform, "Figure"); _makerFigure.Place(0.5f, 0, 0, 40, 10, 10);
            _makerRows = UIKit.Rect(card.transform, "Rows"); _makerRows.Place(0, 1, 520, -120, 840, 640);
            var wear = UIKit.Button(card.transform, "Wear It", () => { Extras.Outfit = Extras.Custom; CustomLook.SaveMine(); _extraTab = 2; ShowExtras(); }, 30, true);
            ((RectTransform)wear.transform).Place(1, 0, -56, 36, 320, 72);
            var back = UIKit.Button(card.transform, "Back", () => { CustomLook.SaveMine(); _extraTab = 2; ShowExtras(); }, 30);
            ((RectTransform)back.transform).Place(1, 0, -396, 36, 260, 72);
            var note = UIKit.Label(card.transform, "Other players see this when you wear it.", 22, UIKit.Dim, TextAnchor.MiddleLeft);
            note.rectTransform.Place(0, 0, 60, 52, 600, 40);
        }

        void ShowMaker() { Show(_maker, true); FillMaker(); }

        void FillMaker()
        {
            var o = CustomLook.Mine;
            foreach (Transform t in _makerFigure) Destroy(t.gameObject);
            foreach (Transform t in _makerRows) Destroy(t.gameObject);
            Figure(_makerFigure, o.Tunic, o.Hood, o.Trim, o.Trousers, o.Boots, o.Hat, o.Cape, o.Hair, SkinOf(o.Skin), 2.5f);

            float y = 0;
            // Hat and cape: buttons that step through the choices.
            var hatLabel = UIKit.Label(_makerRows, "Hat", 24, UIKit.Ink, TextAnchor.MiddleLeft, FontStyle.Bold); hatLabel.rectTransform.Place(0, 1, 0, y, 150, 56);
            var hat = UIKit.Button(_makerRows, CustomLook.HatNames[o.Hat] + "   ›", () => { o.Hat = (o.Hat + 1) % CustomLook.HatNames.Length; FillMaker(); }, 24);
            ((RectTransform)hat.transform).Place(0, 1, 150, y, 300, 56);
            var cape = UIKit.Button(_makerRows, o.Cape ? "Cape: yes" : "Cape: no", () => { o.Cape = !o.Cape; FillMaker(); }, 24);
            ((RectTransform)cape.transform).Place(0, 1, 470, y, 220, 56);
            y -= 76;

            // Skin: by name, or one of four tones.
            var skinLabel = UIKit.Label(_makerRows, "Skin", 24, UIKit.Ink, TextAnchor.MiddleLeft, FontStyle.Bold); skinLabel.rectTransform.Place(0, 1, 0, y, 150, 44);
            for (int i = 0; i < 5; i++)
            {
                int idx = i;
                var b = Swatch(_makerRows, 150 + i * 52, y, i == 0 ? new Color(0.3f, 0.32f, 0.38f) : SkinTones[i], o.Skin == i, () => { o.Skin = idx; FillMaker(); });
                if (i == 0) { var q = UIKit.Label(b.transform, "?", 24, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold); q.rectTransform.Fill(); q.raycastTarget = false; }
            }
            var auto = UIKit.Label(_makerRows, "?  picks one from your name", 18, UIKit.Dim, TextAnchor.MiddleLeft); auto.rectTransform.Place(0, 1, 420, y, 400, 44);
            y -= 66;

            Row("Tunic", o.Tunic, c => o.Tunic = c, ref y);
            Row(o.Hat == 0 ? "Hood" : o.Hat == 5 ? "Helmet" : "Hat", o.Hood, c => o.Hood = c, ref y);
            Row("Trim", o.Trim, c => o.Trim = c, ref y);
            Row("Trousers", o.Trousers, c => o.Trousers = c, ref y);
            Row("Boots", o.Boots, c => o.Boots = c, ref y);
            Row("Hair", o.Hair, c => o.Hair = c, ref y);
        }

        // One piece: its name and a strip of colours to pick from.
        void Row(string name, Color now, Action<Color> set, ref float y)
        {
            var label = UIKit.Label(_makerRows, name, 24, UIKit.Ink, TextAnchor.MiddleLeft, FontStyle.Bold); label.rectTransform.Place(0, 1, 0, y - 14, 150, 44);
            for (int i = 0; i < MakerColours.Length; i++)
            {
                var c = MakerColours[i];
                bool on = Mathf.Abs(c.r - now.r) + Mathf.Abs(c.g - now.g) + Mathf.Abs(c.b - now.b) < 0.02f;
                Swatch(_makerRows, 150 + (i % 10) * 52, y - (i / 10) * 40, c, on, () => { set(c); FillMaker(); }, 36);
            }
            y -= 84;
        }

        static Button Swatch(RectTransform parent, float x, float y, Color c, bool on, Action click, float size = 44)
        {
            var ring = UIKit.Panel(parent, "Swatch", on ? Color.white : new Color(0, 0, 0, 0.45f), UIKit.RoundSmall);
            ring.rectTransform.Place(0, 1, x, y, 46, size);
            var fill = UIKit.Panel(ring.transform, "Colour", c, UIKit.RoundSmall);
            fill.rectTransform.Fill(); fill.rectTransform.offsetMin = new Vector2(on ? 4 : 2, on ? 4 : 2); fill.rectTransform.offsetMax = new Vector2(on ? -4 : -2, on ? -4 : -2);
            fill.raycastTarget = false;
            var b = ring.gameObject.AddComponent<Button>(); b.targetGraphic = ring; b.onClick.AddListener(() => { Sfx.I?.Play("click", 0.3f); click(); });
            return b;
        }

        // ------------------------------------------------------------------ bundles
        void FillBundles()
        {
            foreach (var bundle in Extras.Bundles)
            {
                var bu = bundle;
                var pack = bu.World != null ? Array.Find(Extras.Worlds, w => w.Id == bu.World) : null;
                string tag = (pack != null ? pack.Name + " · " : "") + Looks.All[bu.Look].Name + " look · " + Extras.Outfits[bu.Outfit].Name;
                var tile = Tile(bu.Name, tag, bu.Desc, out var pic);
                if (pack != null) PackPicture(pic, pack.Id); else Backdrop(pic);
                // The outfit stands in the corner of the picture.
                var o = Extras.Outfits[bu.Outfit];
                var corner = UIKit.Rect(pic, "Figure"); corner.Place(pack != null ? 0.86f : 0.5f, 0, 0, 2, 10, 10);
                Figure(corner, o.Tunic, o.Hood, o.Trim, o.Trousers, o.Boots, 0, false, Color.black, SkinTones[0], pack != null ? 0.55f : 1);
                bool on = Looks.Current == bu.Look && Extras.Outfit == bu.Outfit;
                var b = UIKit.Button(tile, pack != null ? "USE" : on ? "IN USE" : "USE", () =>
                {
                    bool repaint = Looks.Current != bu.Look;
                    Looks.Current = bu.Look; Extras.Outfit = bu.Outfit;
                    if (repaint) LookChanged?.Invoke();
                    if (pack != null) UseWorld?.Invoke(pack); else FillExtras();
                }, 26, pack != null || !on);
                ((RectTransform)b.transform).Place(1, 0, -14, 12, 170, 50);
                b.interactable = pack != null || !on;
            }
        }
    }
}
