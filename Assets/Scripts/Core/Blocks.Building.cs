using System;
using System.Collections.Generic;

namespace Stratum.Core
{
    // The big building set (0.11): sixteen colours of wool, carpet, plaster, floor tiles and painted planks; slabs,
    // stairs and walls of the newer materials; fences and doors in the other woods; and furniture. Everything here
    // is made from tables, so a new colour or material is one more row. These come after every older block, so
    // worlds saved before keep their block numbers.
    public static partial class Blocks
    {
        // The sixteen colours, with the paint each is drawn in.
        public static readonly (string key, string name, uint rgb)[] Dyes =
        {
            ("white", "White", 0xe8e6e0), ("light_grey", "Light Grey", 0xb4b6ba), ("grey", "Grey", 0x8a8c90), ("black", "Black", 0x2a2a30),
            ("brown", "Brown", 0x7a5636), ("red", "Red", 0xb0302c), ("orange", "Orange", 0xe0802a), ("yellow", "Yellow", 0xe8c03a),
            ("lime", "Lime", 0x86c43a), ("green", "Green", 0x4f7a2e), ("cyan", "Cyan", 0x2a9a9c), ("light_blue", "Light Blue", 0x6aa8e0),
            ("blue", "Blue", 0x34509e), ("purple", "Purple", 0x7a3aa8), ("magenta", "Magenta", 0xc04aa8), ("pink", "Pink", 0xe89ab0),
        };
        public static int DyeIndex(string key) { for (int i = 0; i < Dyes.Length; i++) if (Dyes[i].key == key) return i; return -1; }

        // Wool colours added in 0.11, in the order of their textures (Tex.WoolOrange onward).
        public static readonly string[] NewWool = { "orange", "lime", "cyan", "light_blue", "purple", "magenta", "pink", "light_grey" };
        // Painted planks, in the order of Tex.PaintedPlanks0 onward.
        public static readonly string[] PaintedPlanks = { "red", "blue", "green", "yellow", "grey", "black" };
        public static readonly string[] SofaColours = { "red", "blue", "white", "green" };

        // The wool texture for a colour (older colours and newer ones live in two runs).
        public static Tex WoolTex(string colour)
        {
            int old = Array.IndexOf(WoolColours, colour);
            if (old >= 0) return (Tex)((int)Tex.WoolWhite + old);
            return (Tex)((int)Tex.WoolOrange + Array.IndexOf(NewWool, colour));
        }

        public static readonly List<string> SlabMaterials = new List<string>(), StairMaterials = new List<string>(), WallMaterials = new List<string>();
        public static readonly List<string> Woods = new List<string> { "birch", "pine", "dark" };


        static void Building()
        {
            BlockDef b;
            // ---- colours
            foreach (var c in NewWool)
            {
                b = Add(c + "_wool", Dyes[DyeIndex(c)].name + " Wool", WoolTex(c));
                b.Hardness = 0.8f; b.Tool = Tool.Shears; b.Sound = Sound.Cloth;
            }
            foreach (var (key, name, _) in Dyes)
            {
                if (key == "red" || key == "blue") continue;                 // those two carpets came with 0.10
                b = Add("carpet_" + key, name + " Carpet", WoolTex(key)); b.Shape = Shape.Boxes; b.Solid = false; b.Opaque = false; b.Opacity = 0; b.Hardness = 0.2f; b.Sound = Sound.Cloth;
                b.Boxes = new[] { new Box(0, 0, 0, 1, 1 / 16f, 1) };
            }
            for (int i = 1; i < Dyes.Length; i++)                            // white plaster is the plain one
            {
                b = Add("plaster_" + Dyes[i].key, Dyes[i].name + " Plaster", (Tex)((int)Tex.PlasterTint0 + i));
                b.Hardness = 1.2f; b.Sound = Sound.Stone; b.Tool = Tool.Pickaxe;
            }
            for (int i = 0; i < Dyes.Length; i++)
            {
                b = Add("tiles_" + Dyes[i].key, Dyes[i].name + " Floor Tiles", (Tex)((int)Tex.Tiles0 + i));
                b.Hardness = 1.5f; b.Sound = Sound.Stone; b.Tool = Tool.Pickaxe;
            }
            for (int i = 0; i < PaintedPlanks.Length; i++)
            {
                b = Add(PaintedPlanks[i] + "_planks", Dyes[DyeIndex(PaintedPlanks[i])].name + " Planks", (Tex)((int)Tex.PaintedPlanks0 + i));
                b.Hardness = 2; b.Sound = Sound.Wood; b.Tool = Tool.Axe;
            }

            // ---- slabs, stairs and walls of the newer materials
            var mats = new (string key, string name, string full, bool slab, bool stairs, bool wall)[]
            {
                ("stone", "Stone", "stone", false, true, false),             // its slab is older
                ("polished_stone", "Polished Stone", "polished_stone", true, true, true),
                ("sandstone", "Sandstone", "sandstone", true, true, true),
                ("clay_brick", "Brick", "clay_bricks", true, true, true),
                ("slate_tile", "Slate Tile", "slate_tiles", true, true, false),
                ("stone_brick", "Stone Brick", "stone_bricks", false, false, true),
                ("dark", "Dark", "dark_planks", true, true, false),
                ("white", "Whitewashed", "white_planks", true, true, false),
                ("roof_tile", "Roof Tile", "roof_tiles", true, true, false),
                ("plaster", "Plaster", "plaster", true, true, true),
            };
            foreach (var m in mats)
            {
                ushort full = ByKey[m.full]; var whole = Defs[full];
                if (m.slab)
                {
                    for (int top = 0; top < 2; top++)
                    {
                        b = Add(m.key + (top == 0 ? "_slab" : "_slab_top"), m.name + " Slab", whole.Side);
                        b.Top = whole.Top; b.Bottom = whole.Bottom;
                        b.Shape = Shape.Boxes; b.Opaque = false; b.Opacity = 15; b.Variant = top; b.Pair = full;
                        b.Hardness = whole.Hardness; b.Tool = whole.Tool; b.TierNeeded = whole.TierNeeded; b.Sound = whole.Sound;
                        b.Boxes = new[] { top == 0 ? Box.Px(0, 0, 0, 16, 8, 16) : Box.Px(0, 8, 0, 16, 16, 16) };
                        if (top == 0) { Slabs.Add(b.Id); b.BaseId = b.Id; }
                        else { b.BaseId = (ushort)(b.Id - 1); b.DropItem = m.key + "_slab"; b.Creative = false; Palette.Remove(b.Id); }
                    }
                    SlabMaterials.Add(m.key + "|" + m.full);
                }
                if (m.stairs)
                {
                    Stairs.Add(Facings(m.key + "_stairs", m.name + " Stairs", 4, (d, f) =>
                    {
                        d.Shape = Shape.Boxes; d.Opaque = false; d.Opacity = 15; d.Side = whole.Side; d.Top = whole.Top; d.Bottom = whole.Bottom;
                        d.Hardness = whole.Hardness; d.Tool = whole.Tool; d.TierNeeded = whole.TierNeeded; d.Sound = whole.Sound;
                        d.Boxes = new[] { Box.Px(0, 0, 0, 16, 8, 16), Box.Px(0, 8, 8, 16, 16, 16) };
                    }));
                    StairMaterials.Add(m.key + "|" + m.full);
                }
                if (m.wall)
                {
                    b = Add(m.key + "_wall", m.name + " Wall", whole.Side); b.Top = whole.Top; b.Bottom = whole.Bottom;
                    b.Shape = Shape.Boxes; b.Opaque = false; b.Opacity = 0; b.Connect = Connect.Wall; b.Tall = true;
                    b.Hardness = whole.Hardness; b.Tool = whole.Tool; b.TierNeeded = whole.TierNeeded; b.Sound = whole.Sound;
                    b.Boxes = ConnectBoxes(Connect.Wall, 0);
                    WallMaterials.Add(m.key + "|" + m.full);
                }
            }
            // A clipped hedge joins up like a wall.
            b = Add("hedge", "Hedge", Tex.OakLeaves); b.Shape = Shape.Boxes; b.Opaque = false; b.Opacity = 1; b.Cutout = true; b.Connect = Connect.Wall; b.Tall = true;
            b.Hardness = 0.3f; b.Sound = Sound.Leaves; b.Boxes = ConnectBoxes(Connect.Wall, 0);

            // ---- fences and doors in the other woods
            var woodTex = new Dictionary<string, (Tex planks, Tex lower, Tex upper, string name, string planksKey)>
            {
                ["birch"] = (Tex.BirchPlanks, Tex.DoorBirchLower, Tex.DoorBirchUpper, "Birch", "birch_planks"),
                ["pine"] = (Tex.PinePlanks, Tex.DoorPineLower, Tex.DoorPineUpper, "Pine", "pine_planks"),
                ["dark"] = (Tex.DarkPlanks, Tex.DoorDarkLower, Tex.DoorDarkUpper, "Dark", "dark_planks"),
            };
            foreach (var w in Woods)
            {
                var t = woodTex[w];
                b = Add(w + "_fence", t.name + " Fence", t.planks); b.Shape = Shape.Boxes; b.Opaque = false; b.Opacity = 0; b.Connect = Connect.Fence; b.Tall = true;
                b.Hardness = 2; b.Tool = Tool.Axe; b.Sound = Sound.Wood; b.Boxes = ConnectBoxes(Connect.Fence, 0);
                Facings(w + "_door", t.name + " Door", 16, (d, i) =>
                {
                    bool open = (i / 2) % 2 == 1, upper = i % 2 == 1;
                    d.Variant = i % 4; d.Opens = true; d.Door = true;
                    d.Shape = Shape.Boxes; d.Opaque = false; d.Opacity = 0; d.Cutout = true; d.Hardness = 3; d.Tool = Tool.Axe; d.Sound = Sound.Wood;
                    d.Side = upper ? t.upper : t.lower; d.Top = d.Bottom = t.planks; d.FlatIcon = true;
                    d.Boxes = new[] { open ? Box.Px(0, 0, 0, 3, 16, 16) : Box.Px(0, 0, 0, 16, 16, 3) };
                }, facingOf: i => i / 4);
            }
            b = Add("picket_fence", "Picket Fence", Tex.WhitePlanks); b.Shape = Shape.Boxes; b.Opaque = false; b.Opacity = 0; b.Connect = Connect.Fence; b.Tall = true;
            b.Hardness = 2; b.Tool = Tool.Axe; b.Sound = Sound.Wood; b.Boxes = ConnectBoxes(Connect.Fence, 0);

            // ---- furniture. Shapes are given facing +Z (the front toward whoever put it down).
            void Wood(BlockDef d, Tex t) { d.Shape = Shape.Boxes; d.Opaque = false; d.Opacity = 0; d.Hardness = 1.5f; d.Tool = Tool.Axe; d.Sound = Sound.Wood; d.Top = d.Side = d.Bottom = d.Front = t; d.HasFront = true; }
            Facings("chair", "Chair", 4, (d, f) =>
            {
                Wood(d, Tex.OakPlanks);
                d.Boxes = new[] { Box.Px(3, 7, 3, 13, 9, 13), Box.Px(3, 0, 3, 5, 7, 5), Box.Px(11, 0, 3, 13, 7, 5), Box.Px(3, 0, 11, 5, 7, 13), Box.Px(11, 0, 11, 13, 7, 13), Box.Px(3, 9, 3, 13, 16, 5) };
            });
            Facings("bench", "Bench", 4, (d, f) =>
            {
                Wood(d, Tex.OakPlanks);
                d.Boxes = new[] { Box.Px(0, 6, 3, 16, 8, 13), Box.Px(1, 0, 4, 3, 6, 12), Box.Px(13, 0, 4, 15, 6, 12), Box.Px(0, 8, 3, 16, 14, 5) };
            });
            foreach (var c in SofaColours)
                Facings("sofa_" + c, Dyes[DyeIndex(c)].name + " Sofa", 4, (d, f) =>
                {
                    Wood(d, WoolTex(c)); d.Sound = Sound.Cloth; d.Hardness = 1;
                    d.Boxes = new[] { Box.Px(0, 0, 1, 16, 7, 15), Box.Px(0, 7, 1, 16, 14, 5), Box.Px(0, 7, 5, 2, 11, 15), Box.Px(14, 7, 5, 16, 11, 15) };
                });
            Facings("cabinet", "Cabinet", 4, (d, f) =>
            {
                d.Hardness = 2; d.Tool = Tool.Axe; d.Sound = Sound.Wood; d.Container = true;
                d.Top = d.Bottom = d.Side = Tex.OakPlanks; d.Front = Tex.CabinetFront; d.HasFront = true;
            });
            Facings("wardrobe", "Wardrobe", 4, (d, f) =>
            {
                d.Hardness = 2; d.Tool = Tool.Axe; d.Sound = Sound.Wood; d.Container = true;
                d.Top = d.Bottom = d.Side = Tex.DarkPlanks; d.Front = Tex.CabinetFront; d.HasFront = true;
            });
            Facings("stove", "Stove", 4, (d, f) =>
            {
                d.Hardness = 3; d.Tool = Tool.Pickaxe; d.TierNeeded = 1; d.Sound = Sound.Metal; d.Emit = 9;
                d.Top = Tex.StoveTop; d.Bottom = d.Side = Tex.AnvilSide; d.Front = Tex.StoveFront; d.HasFront = true;
            });
            Facings("fridge", "Cold Box", 4, (d, f) =>
            {
                d.Hardness = 3; d.Tool = Tool.Pickaxe; d.TierNeeded = 1; d.Sound = Sound.Metal; d.Container = true;
                d.Top = d.Bottom = d.Side = Tex.IronBlock; d.Front = Tex.FridgeFront; d.HasFront = true;
            });
            Facings("screen", "Picture Box", 4, (d, f) =>
            {
                d.Shape = Shape.Boxes; d.Opaque = false; d.Opacity = 0; d.Hardness = 1; d.Tool = Tool.Pickaxe; d.Sound = Sound.Metal; d.Emit = 7;
                d.Top = d.Bottom = d.Side = Tex.AnvilSide; d.Front = Tex.TvFront; d.HasFront = true;
                d.Boxes = new[] { Box.Px(1, 3, 6, 15, 14, 9), Box.Px(6, 0, 5, 10, 3, 10) };
            });
            Facings("piano", "Piano", 4, (d, f) =>
            {
                d.Shape = Shape.Boxes; d.Opaque = false; d.Opacity = 0; d.Hardness = 2; d.Tool = Tool.Axe; d.Sound = Sound.Wood;
                d.Top = d.Bottom = d.Side = Tex.DarkPlanks; d.Front = Tex.PianoFront; d.HasFront = true;
                d.Boxes = new[] { Box.Px(0, 0, 0, 16, 16, 8), Box.Px(0, 8, 8, 16, 10, 14) };
            });
            Facings("bathtub", "Bathtub", 4, (d, f) =>
            {
                Wood(d, Tex.Porcelain); d.Tool = Tool.Pickaxe; d.Sound = Sound.Stone;
                d.Boxes = new[] { Box.Px(0, 0, 1, 16, 3, 15), Box.Px(0, 3, 1, 16, 10, 3), Box.Px(0, 3, 13, 16, 10, 15), Box.Px(0, 3, 3, 2, 10, 13), Box.Px(14, 3, 3, 16, 10, 13) };
            });
            Facings("toilet", "Toilet", 4, (d, f) =>
            {
                Wood(d, Tex.Porcelain); d.Tool = Tool.Pickaxe; d.Sound = Sound.Stone;
                d.Boxes = new[] { Box.Px(4, 0, 4, 12, 6, 13), Box.Px(3, 6, 3, 13, 8, 13), Box.Px(3, 6, 0, 13, 15, 4) };
            });
            Facings("mailbox", "Letter Box", 4, (d, f) =>
            {
                Wood(d, Tex.IronBlock); d.Tool = Tool.Pickaxe; d.Sound = Sound.Metal;
                d.Boxes = new[] { Box.Px(7, 0, 7, 9, 9, 9), Box.Px(5, 9, 3, 11, 14, 13) };
            });
            // On a wall.
            void Hung(BlockDef d, Tex t, Box box) { d.Shape = Shape.Boxes; d.Solid = false; d.Opaque = false; d.Opacity = 0; d.OnWall = true; d.FlatIcon = true; d.Hardness = 0.4f; d.Sound = Sound.Wood; d.Top = d.Bottom = d.Side = t; d.Boxes = new[] { box }; }
            for (int k = 0; k < 3; k++)
            {
                int pic = k;
                Facings("painting_" + new[] { "sunset", "lake", "forest" }[k], "Painting: " + new[] { "Sunset", "Lake", "Forest" }[k], 4, (d, f) => Hung(d, (Tex)((int)Tex.Picture0 + pic), Box.Px(0, 0, 15, 16, 16, 16)));
            }
            Facings("mirror", "Mirror", 4, (d, f) => Hung(d, Tex.MirrorGlass, Box.Px(2, 0, 15, 14, 16, 16)));
            Facings("wall_clock", "Wall Clock", 4, (d, f) => Hung(d, Tex.ClockFace, Box.Px(2, 2, 15, 14, 14, 16)));
            Facings("shelf", "Shelf", 4, (d, f) => { Hung(d, Tex.OakPlanks, Box.Px(0, 7, 9, 16, 9, 16)); d.Solid = true; d.FlatIcon = false; });
            // Standing by themselves.
            b = Add("counter", "Counter", Tex.PolishedStone, Tex.CabinetFront, Tex.OakPlanks); b.Hardness = 2; b.Tool = Tool.Axe; b.Sound = Sound.Wood;
            b = Add("sink", "Wash Basin", Tex.Porcelain); b.Shape = Shape.Boxes; b.Opaque = false; b.Opacity = 0; b.Hardness = 1.5f; b.Tool = Tool.Pickaxe; b.Sound = Sound.Stone;
            b.Boxes = new[] { Box.Px(6, 0, 6, 10, 10, 10), Box.Px(1, 10, 1, 15, 12, 15), Box.Px(1, 12, 1, 15, 15, 3), Box.Px(1, 12, 13, 15, 15, 15), Box.Px(1, 12, 3, 3, 15, 13), Box.Px(13, 12, 3, 15, 15, 13) };
            b = Add("crate", "Crate", Tex.OakPlanks, Tex.CrateSide, Tex.OakPlanks); b.Hardness = 2; b.Tool = Tool.Axe; b.Sound = Sound.Wood; b.Container = true;
            b = Add("floor_lamp", "Standing Lamp", Tex.LampShade); b.Shape = Shape.Boxes; b.Opaque = false; b.Opacity = 0; b.Emit = 14; b.Hardness = 0.5f; b.Sound = Sound.Cloth; b.Bottom = Tex.AnvilSide;
            b.Boxes = new[] { Box.Px(5, 0, 5, 11, 1, 11), Box.Px(7, 1, 7, 9, 10, 9), Box.Px(4, 10, 4, 12, 16, 12) };
            b = Add("candle", "Candle", Tex.Candle); b.Shape = Shape.Boxes; b.Solid = false; b.Opaque = false; b.Opacity = 0; b.Emit = 11; b.Hardness = 0.1f; b.Sound = Sound.Cloth; b.FlatIcon = true;
            b.Boxes = new[] { Box.Px(7, 0, 7, 9, 6, 9) };
            b = Add("chandelier", "Chandelier", Tex.Lantern); b.Shape = Shape.Boxes; b.Solid = false; b.Opaque = false; b.Opacity = 0; b.Emit = 15; b.Hardness = 0.5f; b.Sound = Sound.Metal;
            b.Boxes = new[] { Box.Px(7, 10, 7, 9, 16, 9), Box.Px(2, 7, 7, 14, 9, 9), Box.Px(7, 7, 2, 9, 9, 14), Box.Px(1, 5, 6, 4, 9, 10), Box.Px(12, 5, 6, 15, 9, 10), Box.Px(6, 5, 1, 10, 9, 4), Box.Px(6, 5, 12, 10, 9, 15) };
            b = Add("lamp_post", "Lamp Post", Tex.Lantern); b.Shape = Shape.Boxes; b.Opaque = false; b.Opacity = 0; b.Emit = 15; b.Hardness = 1.5f; b.Tool = Tool.Pickaxe; b.Sound = Sound.Metal; b.Bottom = Tex.AnvilSide;
            b.Boxes = new[] { Box.Px(6, 0, 6, 10, 2, 10), Box.Px(7, 2, 7, 9, 9, 9), Box.Px(4, 9, 4, 12, 15, 12), Box.Px(5, 15, 5, 11, 16, 11) };
            foreach (var (key, name, tex) in new[] { ("flower_pot_yellow", "Potted Yellow Flower", Tex.PottedYellow), ("flower_pot_blue", "Potted Blue Flower", Tex.PottedBlue) })
            {
                b = Add(key, name, tex); b.Shape = Shape.Cross; b.Solid = false; b.Opaque = false; b.Opacity = 0; b.Cutout = true; b.Hardness = 0.1f; b.Sound = Sound.Stone; b.FlatIcon = true;
            }
        }
    }
}
