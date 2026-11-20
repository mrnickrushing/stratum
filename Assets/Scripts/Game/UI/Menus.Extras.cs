using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Stratum.Game
{
    // The Extras page: free packs, each with a USE button. Worlds open a ready-made world (made the first time,
    // reopened after that); outfits change what other players see you wearing.
    public sealed partial class Menus
    {
        public Action<WorldPack> UseWorld;

        GameObject _extras; RectTransform _extraGrid; Button _tabWorlds, _tabOutfits; Text _extraNote; bool _extraOutfits;
        readonly List<Texture2D> _extraPics = new List<Texture2D>();

        void BuildExtras()
        {
            _extras = Screen("Extras");
            var card = Card(_extras, 1420, 940, "Extras");
            _tabWorlds = UIKit.Button(card.transform, "Worlds", () => { _extraOutfits = false; FillExtras(); }, 28);
            ((RectTransform)_tabWorlds.transform).Place(0.5f, 1, -150, -100, 280, 60);
            _tabOutfits = UIKit.Button(card.transform, "Outfits", () => { _extraOutfits = true; FillExtras(); }, 28);
            ((RectTransform)_tabOutfits.transform).Place(0.5f, 1, 150, -100, 280, 60);
            var scroll = UIKit.Scroll(card.transform, out _extraGrid);
            var sr = (RectTransform)scroll.transform; sr.Fill(); sr.offsetMin = new Vector2(50, 124); sr.offsetMax = new Vector2(-50, -180);
            var grid = _extraGrid.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(420, 410); grid.spacing = new Vector2(30, 24); grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = 3;
            grid.childAlignment = TextAnchor.UpperCenter; grid.padding = new RectOffset(0, 0, 4, 12);
            _extraNote = UIKit.Label(card.transform, "", 22, UIKit.Dim, TextAnchor.MiddleLeft);
            _extraNote.rectTransform.Place(0, 0, 56, 52, 800, 40);
            var back = UIKit.Button(card.transform, "Back", ShowMain, 30);
            ((RectTransform)back.transform).Place(1, 0, -56, 36, 320, 72);
        }

        public void ShowExtras() { Show(_extras, true); FillExtras(); }

        void FillExtras()
        {
            foreach (Transform t in _extraGrid) Destroy(t.gameObject);
            ((Image)_tabWorlds.targetGraphic).color = !_extraOutfits ? new Color(0.36f, 0.29f, 0.14f, 1) : new Color(0.17f, 0.18f, 0.22f, 0.95f);
            ((Image)_tabOutfits.targetGraphic).color = _extraOutfits ? new Color(0.36f, 0.29f, 0.14f, 1) : new Color(0.17f, 0.18f, 0.22f, 0.95f);
            _extraNote.text = _extraOutfits ? "Everything here is free. Other players see the outfit you wear." : "Everything here is free. USE makes the world and takes you there.";
            if (_extraOutfits) FillOutfits(); else FillWorldPacks();
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

        void FillWorldPacks()
        {
            var mine = ListWorlds?.Invoke() ?? new List<WorldMeta>();
            foreach (var p in Extras.Worlds)
            {
                var pack = p;
                var tile = Tile(p.Name, p.Tag, p.Desc, out var pic);
                var raw = pic.gameObject.AddComponent<RawImage>();
                var tex = Resources.Load<Texture2D>("Extras/" + p.Id);
                raw.texture = tex; raw.color = tex != null ? Color.white : new Color(0.22f, 0.24f, 0.28f, 1);
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

        void FillOutfits()
        {
            int wearing = Extras.Outfit;
            for (int i = 0; i < Extras.Outfits.Length; i++)
            {
                int idx = i; var o = Extras.Outfits[i];
                var tile = Tile(o.Name, i == 0 ? "Outfit · the usual one" : "Outfit", o.Desc, out var pic);
                var back = pic.gameObject.AddComponent<Image>(); back.sprite = UIKit.RoundSmall; back.type = Image.Type.Sliced;
                back.color = new Color(0.09f, 0.10f, 0.13f, 1);
                Figure(pic, o);
                bool on = i == wearing;
                var b = UIKit.Button(tile, on ? "WEARING" : "USE", () => { Extras.Outfit = idx; FillExtras(); }, 26, !on);
                ((RectTransform)b.transform).Place(1, 0, -14, 12, 170, 50);
                b.interactable = !on;
            }
        }

        // A little blocky figure in the outfit's colours: hood, face, tunic with a clasp and belt, arms, legs, boots.
        static void Figure(RectTransform parent, Outfit o)
        {
            void Box(float x, float y, float w, float h, Color c)
            {
                var r = UIKit.Rect(parent, "Part").Place(0.5f, 0, x, y, w, h);
                r.pivot = new Vector2(0.5f, 0); r.anchoredPosition = new Vector2(x, y);
                var img = r.gameObject.AddComponent<Image>(); img.color = c; img.raycastTarget = false;
            }
            var skin = new Color(0.86f, 0.68f, 0.54f); var dark = new Color(0.08f, 0.07f, 0.07f);
            Box(-13, 12, 22, 14, o.Boots); Box(13, 12, 22, 14, o.Boots);                    // boots
            Box(-13, 26, 20, 44, o.Trousers); Box(13, 26, 20, 44, o.Trousers);              // legs
            Box(0, 70, 50, 62, o.Tunic);                                                    // tunic
            Box(0, 70, 52, 8, o.Boots);                                                     // belt
            Box(0, 116, 18, 10, o.Trim);                                                    // clasp
            Box(-36, 76, 18, 56, o.Tunic * 0.9f + new Color(0, 0, 0, 0.1f)); Box(36, 76, 18, 56, o.Tunic * 0.9f + new Color(0, 0, 0, 0.1f));   // arms
            Box(-36, 66, 16, 12, skin); Box(36, 66, 16, 12, skin);                          // hands
            Box(0, 132, 48, 46, o.Hood);                                                    // hood
            Box(0, 136, 36, 30, skin);                                                      // face
            Box(-9, 150, 7, 7, dark); Box(9, 150, 7, 7, dark);                              // eyes
        }
    }
}
