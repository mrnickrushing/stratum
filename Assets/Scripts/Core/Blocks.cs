using System;
using System.Collections.Generic;

namespace Stratum.Core
{
    public enum Shape : byte { Cube, Cross, Torch, Liquid, Boxes }
    public enum Sound : byte { Stone, Dirt, Grass, Sand, Wood, Leaves, Glass, Snow, Metal, Cloth }
    public enum Tool : byte { None, Pickaxe, Axe, Shovel, Sword, Hoe, Shears }
    public enum Connect : byte { None, Fence, Wall, Pane }

    // Texture layers used by block faces. BlockTextures (Game) paints one layer per entry, in this order.
    public enum Tex : byte
    {
        Stone, Slate, Basalt, Bedrock, Dirt, GrassTop, GrassSide, Sand, Gravel, SandstoneSide, SandstoneTop,
        Snow, SnowSide, Ice, Water, Clay,
        OakLog, OakLogTop, OakLeaves, BirchLog, BirchLogTop, BirchLeaves, PineLog, PineLogTop, PineNeedles,
        CactusSide, CactusTop, OakPlanks, BirchPlanks, PinePlanks, Cobble, MossyCobble, StoneBricks, Glass,
        CoalOre, CopperOre, IronOre, GoldOre, SunstoneOre, Sunstone, Torch,
        TallGrass, RedFlower, YellowFlower, BlueFlower, DeadBush, Fern,
        BenchTop, BenchSide, FurnaceFront, FurnaceSide, FurnaceTop, CopperBlock, IronBlock, GoldBlock,
        ClayBricks, SlateTiles,
        // 0.2
        ChestFront, ChestSide, ChestTop, BedHead, BedFoot, BedSide, BedEnd,
        WoolWhite, WoolGrey, WoolBlack, WoolBrown, WoolRed, WoolYellow, WoolGreen, WoolBlue,
        Lava, Obsidian,
        DoorLower, DoorUpper, Trapdoor, Ladder, IronBars,
        EnchantTop, EnchantSide, AnvilTop, AnvilSide, JadeOre, JadeBlock,
        FarmlandDry, FarmlandWet, Wheat0, Wheat1, Wheat2, Wheat3, SaplingOak, SaplingBirch, SaplingPine, SugarCane, HayTop, HaySide,
        Count
    }

    public sealed class BlockDef
    {
        public byte Id;
        public string Name;
        public Shape Shape = Shape.Cube;
        public bool Solid = true;        // collides
        public bool Opaque = true;       // hides neighbouring faces, blocks light fully
        public byte Opacity = 15;        // light lost passing through (non-opaque blocks)
        public byte Emit;                // block light it gives off (0-15)
        public bool Sway;                // leaves and plants move in the wind
        public bool Cutout;              // texture has holes
        public float Hardness = 1;       // seconds to break by hand with the right tool type
        public Tool Tool = Tool.None;    // tool that speeds it up
        public int TierNeeded;           // 0 = drops by hand; else the pickaxe tier needed for a drop
        public Sound Sound = Sound.Stone;
        public Tex Top, Bottom, Side, Front;
        public bool HasFront;            // furnace-style block: Front on the facing side
        public byte Facing;              // 0..3 for facing variants (+Z, +X, -Z, -X)
        public byte BaseId;              // facing variants point at the first one
        public bool Replaceable;         // placing a block here just overwrites it (plants, water)
        public bool Creative = true;     // listed in the creative palette
        public string DropItem;          // item id dropped; null = itself; "" = nothing
        public int DropCount = 1;
        public Box[] Boxes;              // Shape.Boxes: the pieces, in block units, already turned for Facing
        public int Variant;              // per-family meaning (bed: 0 foot, 1 head)
        public string Bonus;             // an item it sometimes drops as well
        public float BonusChance;
        public bool Container;           // holds things (chests, lost satchels)
        public Connect Connect;          // fences, walls and panes reach out to their neighbours
        public bool Tall;                // collides up to 1.5 blocks high (fences, walls, closed gates)
        public bool Climb;               // ladders
        public bool FlatIcon;            // shown in the hand and the pack as a flat picture
        public byte Fluid;               // 1 water, 2 lava
        public byte Level;               // fluids: 0 a source, then 1, 2... further from it
        public byte Pair;                // slabs: the whole block two of them make; doors: unused
        public bool Opens;               // doors, trapdoors, gates: right-click opens and closes
        public string Bonus2;            // a second thing it sometimes drops
        public float Bonus2Chance;
        public byte Grows;               // 1 wheat, 2 sapling, 3 sugar cane: grows by itself over time

        public Tex Face(int face)        // 0 +X, 1 -X, 2 +Y, 3 -Y, 4 +Z, 5 -Z
        {
            if (face == 2) return Top;
            if (face == 3) return Bottom;
            if (HasFront)
            {
                int f = Facing == 0 ? 4 : Facing == 1 ? 0 : Facing == 2 ? 5 : 1;
                if (face == f) return Front;
            }
            return Side;
        }
    }

    // A piece of a block that isn't a whole cube (chests, beds, slabs...), in block units 0..1.
    public struct Box
    {
        public float X0, Y0, Z0, X1, Y1, Z1;
        public Box(float x0, float y0, float z0, float x1, float y1, float z1) { X0 = x0; Y0 = y0; Z0 = z0; X1 = x1; Y1 = y1; Z1 = z1; }
        public static Box Px(int x0, int y0, int z0, int x1, int y1, int z1) => new Box(x0 / 16f, y0 / 16f, z0 / 16f, x1 / 16f, y1 / 16f, z1 / 16f);
        // A quarter turn clockwise seen from above (+Z front becomes +X front).
        public Box Turn() => new Box(Z0, Y0, 1 - X1, Z1, Y1, 1 - X0);
        public Box Turn(int times) { var b = this; for (int i = 0; i < (times & 3); i++) b = b.Turn(); return b; }
    }

    public static class Blocks
    {
        public const byte Air = 0;
        public static readonly BlockDef[] Defs = new BlockDef[256];
        public static readonly Dictionary<string, byte> ByKey = new Dictionary<string, byte>();
        public static readonly List<byte> Palette = new List<byte>();   // creative order

        public static byte Bedrock, Stone, Slate, Basalt, Dirt, Grass, Sand, Gravel, Sandstone, Snow, SnowyGrass, Ice,
            Water, Clay, OakLog, OakLeaves, BirchLog, BirchLeaves, PineLog, PineNeedles, Cactus, OakPlanks, BirchPlanks,
            PinePlanks, Cobble, MossyCobble, StoneBricks, Glass, CoalOre, CopperOre, IronOre, GoldOre, SunstoneOre,
            Sunstone, Torch, TallGrass, RedFlower, YellowFlower, BlueFlower, DeadBush, Fern, Workbench, Furnace,
            CopperBlock, IronBlock, GoldBlock, ClayBricks, SlateTiles,
            Chest, Bed, Satchel, Lava, Obsidian,
            OakDoor, Trapdoor, Ladder, OakFence, FenceGate, CobbleWall, GlassPane, IronBars, Sign, WallSign,
            EnchantingTable, Anvil, JadeOre, JadeBlock,
            Farmland, FarmlandWet, Wheat, SaplingOak, SaplingBirch, SaplingPine, SugarCane, HayBale;
        public const int WheatStages = 4;
        public static readonly byte[] WaterAt = new byte[8], LavaAt = new byte[4];   // by level (0 = the source)
        public const int WaterReach = 7, LavaReach = 3;
        public static readonly List<byte> Slabs = new List<byte>(), Stairs = new List<byte>();
        public static readonly byte[] Wool = new byte[8];
        public static readonly string[] WoolColours = { "white", "grey", "black", "brown", "red", "yellow", "green", "blue" };

        static byte _next = 1;

        static BlockDef Add(string key, string name, Tex all) => Add(key, name, all, all, all);

        static BlockDef Add(string key, string name, Tex top, Tex side, Tex bottom)
        {
            var d = new BlockDef { Id = _next++, Name = name, Top = top, Side = side, Bottom = bottom };
            d.BaseId = d.Id;
            Defs[d.Id] = d;
            ByKey[key] = d.Id;
            Palette.Add(d.Id);
            Keys[d.Id] = key;
            return d;
        }

        public static readonly string[] Keys = new string[256];

        static BlockDef Plant(string key, string name, Tex t, Sound s = Sound.Grass)
        {
            var d = Add(key, name, t);
            d.Shape = Shape.Cross; d.Solid = false; d.Opaque = false; d.Opacity = 0; d.Cutout = true; d.Sway = true;
            d.Hardness = 0; d.Sound = s; d.Replaceable = true;
            return d;
        }

        static BlockDef Leaves(string key, string name, Tex t)
        {
            var d = Add(key, name, t);
            d.Opaque = false; d.Opacity = 1; d.Cutout = true; d.Sway = true; d.Hardness = 0.3f; d.Sound = Sound.Leaves;
            d.DropItem = "";
            return d;
        }

        static BlockDef Rock(string key, string name, Tex t, float hard, int tier, Sound s = Sound.Stone)
        {
            var d = Add(key, name, t);
            d.Hardness = hard; d.Tool = Tool.Pickaxe; d.TierNeeded = tier; d.Sound = s;
            return d;
        }

        static Blocks()
        {
            Defs[0] = new BlockDef { Id = 0, Name = "Air", Solid = false, Opaque = false, Opacity = 0, Replaceable = true, Creative = false };
            Keys[0] = "air";

            var b = Add("bedrock", "Bedrock", Tex.Bedrock); b.Hardness = -1; b.Creative = true; Bedrock = b.Id;
            b = Rock("stone", "Stone", Tex.Stone, 1.5f, 1); b.DropItem = "cobble"; Stone = b.Id;
            b = Rock("slate", "Slate", Tex.Slate, 2.2f, 1); Slate = b.Id;
            b = Rock("basalt", "Basalt", Tex.Basalt, 3f, 1); Basalt = b.Id;
            b = Add("dirt", "Dirt", Tex.Dirt); b.Hardness = 0.5f; b.Tool = Tool.Shovel; b.Sound = Sound.Dirt; Dirt = b.Id;
            b = Add("grass", "Grass", Tex.GrassTop, Tex.GrassSide, Tex.Dirt); b.Hardness = 0.6f; b.Tool = Tool.Shovel; b.Sound = Sound.Grass; b.DropItem = "dirt"; Grass = b.Id;
            b = Add("snowy_grass", "Snowy Grass", Tex.Snow, Tex.SnowSide, Tex.Dirt); b.Hardness = 0.6f; b.Tool = Tool.Shovel; b.Sound = Sound.Snow; b.DropItem = "dirt"; SnowyGrass = b.Id;
            b = Add("sand", "Sand", Tex.Sand); b.Hardness = 0.5f; b.Tool = Tool.Shovel; b.Sound = Sound.Sand; Sand = b.Id;
            b = Add("gravel", "Gravel", Tex.Gravel); b.Hardness = 0.6f; b.Tool = Tool.Shovel; b.Sound = Sound.Sand; Gravel = b.Id;
            b = Add("sandstone", "Sandstone", Tex.SandstoneTop, Tex.SandstoneSide, Tex.SandstoneTop); b.Hardness = 0.8f; b.Tool = Tool.Pickaxe; b.TierNeeded = 1; Sandstone = b.Id;
            b = Add("snow", "Snow", Tex.Snow); b.Hardness = 0.3f; b.Tool = Tool.Shovel; b.Sound = Sound.Snow; Snow = b.Id;
            b = Add("ice", "Ice", Tex.Ice); b.Hardness = 0.5f; b.Tool = Tool.Pickaxe; b.Opaque = false; b.Opacity = 2; b.Sound = Sound.Glass; b.DropItem = ""; Ice = b.Id;
            b = Add("water", "Water", Tex.Water); b.Shape = Shape.Liquid; b.Solid = false; b.Opaque = false; b.Opacity = 2; b.Hardness = -1; b.Replaceable = true; b.Creative = false; Water = b.Id;
            b = Add("clay", "Clay", Tex.Clay); b.Hardness = 0.6f; b.Tool = Tool.Shovel; b.Sound = Sound.Dirt; Clay = b.Id;

            b = Add("oak_log", "Oak Log", Tex.OakLogTop, Tex.OakLog, Tex.OakLogTop); b.Hardness = 2; b.Tool = Tool.Axe; b.Sound = Sound.Wood; OakLog = b.Id;
            OakLeaves = Leaves("oak_leaves", "Oak Leaves", Tex.OakLeaves).Id; Defs[OakLeaves].Bonus = "apple"; Defs[OakLeaves].BonusChance = 0.06f;
            b = Add("birch_log", "Birch Log", Tex.BirchLogTop, Tex.BirchLog, Tex.BirchLogTop); b.Hardness = 2; b.Tool = Tool.Axe; b.Sound = Sound.Wood; BirchLog = b.Id;
            BirchLeaves = Leaves("birch_leaves", "Birch Leaves", Tex.BirchLeaves).Id;
            b = Add("pine_log", "Pine Log", Tex.PineLogTop, Tex.PineLog, Tex.PineLogTop); b.Hardness = 2; b.Tool = Tool.Axe; b.Sound = Sound.Wood; PineLog = b.Id;
            PineNeedles = Leaves("pine_needles", "Pine Needles", Tex.PineNeedles).Id;
            b = Add("cactus", "Cactus", Tex.CactusTop, Tex.CactusSide, Tex.CactusTop); b.Hardness = 0.4f; b.Sound = Sound.Cloth; b.Opaque = false; b.Opacity = 1; b.Cutout = true; Cactus = b.Id;

            b = Add("oak_planks", "Oak Planks", Tex.OakPlanks); b.Hardness = 1.5f; b.Tool = Tool.Axe; b.Sound = Sound.Wood; OakPlanks = b.Id;
            b = Add("birch_planks", "Birch Planks", Tex.BirchPlanks); b.Hardness = 1.5f; b.Tool = Tool.Axe; b.Sound = Sound.Wood; BirchPlanks = b.Id;
            b = Add("pine_planks", "Pine Planks", Tex.PinePlanks); b.Hardness = 1.5f; b.Tool = Tool.Axe; b.Sound = Sound.Wood; PinePlanks = b.Id;
            Cobble = Rock("cobble", "Cobblestone", Tex.Cobble, 2, 1).Id;
            MossyCobble = Rock("mossy_cobble", "Mossy Cobblestone", Tex.MossyCobble, 2, 1).Id;
            StoneBricks = Rock("stone_bricks", "Stone Bricks", Tex.StoneBricks, 1.8f, 1).Id;
            ClayBricks = Rock("clay_bricks", "Clay Bricks", Tex.ClayBricks, 2, 1).Id;
            SlateTiles = Rock("slate_tiles", "Slate Tiles", Tex.SlateTiles, 2, 1).Id;
            b = Add("glass", "Glass", Tex.Glass); b.Hardness = 0.3f; b.Opaque = false; b.Opacity = 0; b.Cutout = true; b.Sound = Sound.Glass; b.DropItem = ""; Glass = b.Id;

            b = Rock("coal_ore", "Coal Ore", Tex.CoalOre, 3, 1); b.DropItem = "coal"; CoalOre = b.Id;
            CopperOre = Rock("copper_ore", "Copper Ore", Tex.CopperOre, 3, 2).Id;
            IronOre = Rock("iron_ore", "Iron Ore", Tex.IronOre, 3.5f, 3).Id;
            GoldOre = Rock("gold_ore", "Gold Ore", Tex.GoldOre, 3.5f, 4).Id;
            b = Rock("sunstone_ore", "Sunstone Ore", Tex.SunstoneOre, 4, 4); b.Emit = 7; b.DropItem = "sunstone_shard"; b.DropCount = 2; SunstoneOre = b.Id;
            b = Add("sunstone", "Sunstone Lamp", Tex.Sunstone); b.Emit = 15; b.Hardness = 0.5f; b.Sound = Sound.Glass; Sunstone = b.Id;
            b = Add("torch", "Torch", Tex.Torch); b.Shape = Shape.Torch; b.Solid = false; b.Opaque = false; b.Opacity = 0; b.Emit = 14; b.Hardness = 0; b.Cutout = true; b.Sound = Sound.Wood; Torch = b.Id;

            TallGrass = Plant("tall_grass", "Tall Grass", Tex.TallGrass).Id; Defs[TallGrass].DropItem = ""; Defs[TallGrass].Bonus = "berries"; Defs[TallGrass].BonusChance = 0.05f;
            RedFlower = Plant("red_flower", "Poppy", Tex.RedFlower).Id;
            YellowFlower = Plant("yellow_flower", "Buttercup", Tex.YellowFlower).Id;
            BlueFlower = Plant("blue_flower", "Cornflower", Tex.BlueFlower).Id;
            DeadBush = Plant("dead_bush", "Dry Shrub", Tex.DeadBush, Sound.Wood).Id; Defs[DeadBush].DropItem = "stick";
            Fern = Plant("fern", "Fern", Tex.Fern).Id; Defs[Fern].DropItem = "";

            b = Add("workbench", "Workbench", Tex.BenchTop, Tex.BenchSide, Tex.OakPlanks); b.Hardness = 2; b.Tool = Tool.Axe; b.Sound = Sound.Wood; Workbench = b.Id;
            // Furnace: four ids, one per facing; the first is the item.
            byte first = 0;
            for (byte f = 0; f < 4; f++)
            {
                b = Add(f == 0 ? "furnace" : "furnace_" + f, "Furnace", Tex.FurnaceTop, Tex.FurnaceSide, Tex.FurnaceTop);
                b.Front = Tex.FurnaceFront; b.HasFront = true; b.Facing = f; b.Hardness = 3; b.Tool = Tool.Pickaxe; b.TierNeeded = 1;
                if (f == 0) first = b.Id; else { b.Creative = false; Palette.Remove(b.Id); b.DropItem = "furnace"; }
                b.BaseId = first;
            }
            Furnace = first;
            CopperBlock = Rock("copper_block", "Block of Copper", Tex.CopperBlock, 4, 2, Sound.Metal).Id;
            IronBlock = Rock("iron_block", "Block of Iron", Tex.IronBlock, 4, 3, Sound.Metal).Id;
            GoldBlock = Rock("gold_block", "Block of Gold", Tex.GoldBlock, 4, 3, Sound.Metal).Id;

            // ---------------------------------------------------------------- 0.2: survival
            // Every new block goes after the ones before it, so saved worlds keep their ids.
            Chest = Facings("chest", "Chest", 4, (d, f) =>
            {
                d.Shape = Shape.Boxes; d.Opaque = false; d.Opacity = 1; d.Hardness = 2.5f; d.Tool = Tool.Axe; d.Sound = Sound.Wood; d.Container = true;
                d.Top = d.Bottom = Tex.ChestTop; d.Side = Tex.ChestSide; d.Front = Tex.ChestFront; d.HasFront = true;
                d.Boxes = new[] { Box.Px(1, 0, 1, 15, 14, 15) };
            });
            // A bed is two blocks: the foot (where it was placed) and the head (in the direction it faces).
            Bed = Facings("bed", "Bed", 8, (d, i) =>
            {
                int f = i / 2; d.Variant = i % 2;
                d.Facing = (byte)f;
                d.Shape = Shape.Boxes; d.Opaque = false; d.Opacity = 1; d.Hardness = 0.4f; d.Sound = Sound.Cloth; d.DropItem = "bed";
                d.Top = d.Variant == 1 ? Tex.BedHead : Tex.BedFoot; d.Bottom = Tex.OakPlanks; d.Side = Tex.BedSide; d.Front = Tex.BedEnd;
                d.HasFront = d.Variant == 1;
                d.Boxes = new[] { Box.Px(0, 3, 0, 16, 9, 16), Box.Px(1, 0, 1, 3, 3, 3), Box.Px(13, 0, 1, 15, 3, 3), Box.Px(1, 0, 13, 3, 3, 15), Box.Px(13, 0, 13, 15, 3, 15) };
            }, facingOf: i => i / 2);
            for (int i = 0; i < 8; i++)
            {
                b = Add(WoolColours[i] + "_wool", char.ToUpper(WoolColours[i][0]) + WoolColours[i].Substring(1) + " Wool", (Tex)((int)Tex.WoolWhite + i));
                b.Hardness = 0.8f; b.Tool = Tool.Shears; b.Sound = Sound.Cloth; Wool[i] = b.Id;
            }
            // What a player had when they fell: a sack that hands it all back to whoever opens it.
            b = Add("satchel", "Lost Satchel", Tex.WoolBrown); b.Shape = Shape.Boxes; b.Opaque = false; b.Opacity = 0; b.Hardness = 0.2f;
            b.Sound = Sound.Cloth; b.Container = true; b.Creative = false; b.DropItem = ""; Palette.Remove(b.Id);
            b.Boxes = new[] { Box.Px(3, 0, 3, 13, 8, 13), Box.Px(5, 8, 5, 11, 10, 11), Box.Px(6, 10, 6, 10, 12, 10) };
            Satchel = b.Id;
            b = Add("lava", "Lava", Tex.Lava); b.Shape = Shape.Liquid; b.Solid = false; b.Opaque = false; b.Opacity = 1; b.Emit = 15; b.Hardness = -1;
            b.Replaceable = true; b.Creative = false; Palette.Remove(b.Id); Lava = b.Id;
            b = Rock("obsidian", "Obsidian", Tex.Obsidian, 18, 4); Obsidian = b.Id;

            // ---------------------------------------------------------------- 0.2: building
            // Slabs: a bottom and a top half; a second slab on the first makes the whole block.
            foreach (var (key, name, tex, full) in new[] {
                ("oak", "Oak", Tex.OakPlanks, OakPlanks), ("birch", "Birch", Tex.BirchPlanks, BirchPlanks), ("pine", "Pine", Tex.PinePlanks, PinePlanks),
                ("cobble", "Cobblestone", Tex.Cobble, Cobble), ("stone", "Stone", Tex.Stone, Stone), ("stone_brick", "Stone Brick", Tex.StoneBricks, StoneBricks) })
            {
                var whole = Defs[full];
                for (int top = 0; top < 2; top++)
                {
                    b = Add(key + (top == 0 ? "_slab" : "_slab_top"), name + " Slab", tex);
                    b.Shape = Shape.Boxes; b.Opaque = false; b.Opacity = 15; b.Variant = top; b.Pair = full;
                    b.Hardness = whole.Hardness; b.Tool = whole.Tool; b.TierNeeded = whole.TierNeeded; b.Sound = whole.Sound;
                    b.Boxes = new[] { top == 0 ? Box.Px(0, 0, 0, 16, 8, 16) : Box.Px(0, 8, 0, 16, 16, 16) };
                    if (top == 0) { Slabs.Add(b.Id); b.BaseId = b.Id; }
                    else { b.BaseId = (byte)(b.Id - 1); b.DropItem = key + "_slab"; b.Creative = false; Palette.Remove(b.Id); }
                }
            }
            // Stairs: a bottom half and a raised back half; they climb toward where the player was looking.
            foreach (var (key, name, tex, full) in new[] {
                ("oak", "Oak", Tex.OakPlanks, OakPlanks), ("birch", "Birch", Tex.BirchPlanks, BirchPlanks), ("pine", "Pine", Tex.PinePlanks, PinePlanks),
                ("cobble", "Cobblestone", Tex.Cobble, Cobble), ("stone_brick", "Stone Brick", Tex.StoneBricks, StoneBricks) })
            {
                var whole = Defs[full];
                Stairs.Add(Facings(key + "_stairs", name + " Stairs", 4, (d, f) =>
                {
                    d.Shape = Shape.Boxes; d.Opaque = false; d.Opacity = 15; d.Top = d.Side = d.Bottom = tex;
                    d.Hardness = whole.Hardness; d.Tool = whole.Tool; d.TierNeeded = whole.TierNeeded; d.Sound = whole.Sound;
                    d.Boxes = new[] { Box.Px(0, 0, 0, 16, 8, 16), Box.Px(0, 8, 8, 16, 16, 16) };
                }));
            }
            // A door is two blocks (lower, upper); id = door + facing*4 + open*2 + upper. Closed it stands on the side
            // nearest the player who placed it; open it swings to the left edge.
            OakDoor = Facings("oak_door", "Oak Door", 16, (d, i) =>
            {
                bool open = (i / 2) % 2 == 1, upper = i % 2 == 1;
                d.Variant = i % 4; d.Opens = true;
                d.Shape = Shape.Boxes; d.Opaque = false; d.Opacity = 0; d.Cutout = true; d.Hardness = 3; d.Tool = Tool.Axe; d.Sound = Sound.Wood;
                d.Side = upper ? Tex.DoorUpper : Tex.DoorLower; d.Top = d.Bottom = Tex.OakPlanks; d.FlatIcon = true;
                d.Boxes = new[] { open ? Box.Px(0, 0, 0, 3, 16, 16) : Box.Px(0, 0, 0, 16, 16, 3) };
            }, facingOf: i => i / 4);
            Trapdoor = Facings("trapdoor", "Trapdoor", 8, (d, i) =>
            {
                bool open = i % 2 == 1;
                d.Variant = i % 2; d.Opens = true;
                d.Shape = Shape.Boxes; d.Opaque = false; d.Opacity = 0; d.Cutout = true; d.Hardness = 3; d.Tool = Tool.Axe; d.Sound = Sound.Wood;
                d.Top = d.Bottom = d.Side = Tex.Trapdoor;
                d.Boxes = new[] { open ? Box.Px(0, 0, 13, 16, 16, 16) : Box.Px(0, 0, 0, 16, 3, 16) };
            }, facingOf: i => i / 2);
            // A ladder hangs on the wall at the facing side of its cell.
            Ladder = Facings("ladder", "Ladder", 4, (d, f) =>
            {
                d.Shape = Shape.Boxes; d.Solid = false; d.Opaque = false; d.Opacity = 0; d.Cutout = true; d.Climb = true; d.FlatIcon = true;
                d.Hardness = 0.4f; d.Tool = Tool.Axe; d.Sound = Sound.Wood; d.Top = d.Bottom = d.Side = Tex.Ladder;
                d.Boxes = new[] { Box.Px(0, 0, 14, 16, 16, 16) };
            });
            b = Add("oak_fence", "Oak Fence", Tex.OakPlanks); b.Shape = Shape.Boxes; b.Opaque = false; b.Opacity = 0; b.Connect = Connect.Fence; b.Tall = true;
            b.Hardness = 2; b.Tool = Tool.Axe; b.Sound = Sound.Wood; b.Boxes = ConnectBoxes(Connect.Fence, 0); OakFence = b.Id;
            FenceGate = Facings("oak_fence_gate", "Oak Fence Gate", 8, (d, i) =>
            {
                bool open = i % 2 == 1;
                d.Variant = i % 2; d.Opens = true; d.Tall = !open;
                d.Shape = Shape.Boxes; d.Opaque = false; d.Opacity = 0; d.Hardness = 2; d.Tool = Tool.Axe; d.Sound = Sound.Wood;
                d.Top = d.Bottom = d.Side = Tex.OakPlanks;
                d.Boxes = open
                    ? new[] { Box.Px(0, 5, 7, 2, 16, 9), Box.Px(14, 5, 7, 16, 16, 9), Box.Px(0, 6, 9, 2, 15, 15), Box.Px(14, 6, 9, 16, 15, 15) }
                    : new[] { Box.Px(0, 5, 7, 2, 16, 9), Box.Px(14, 5, 7, 16, 16, 9), Box.Px(2, 6, 7, 14, 9, 9), Box.Px(2, 12, 7, 14, 15, 9), Box.Px(7, 9, 7, 9, 12, 9) };
            }, facingOf: i => i / 2);
            b = Rock("cobble_wall", "Cobblestone Wall", Tex.Cobble, 2, 1); b.Shape = Shape.Boxes; b.Opaque = false; b.Opacity = 0; b.Connect = Connect.Wall; b.Tall = true;
            b.Boxes = ConnectBoxes(Connect.Wall, 0); CobbleWall = b.Id;
            b = Add("glass_pane", "Glass Pane", Tex.Glass); b.Shape = Shape.Boxes; b.Opaque = false; b.Opacity = 0; b.Cutout = true; b.Connect = Connect.Pane;
            b.Hardness = 0.3f; b.Sound = Sound.Glass; b.DropItem = ""; b.Boxes = ConnectBoxes(Connect.Pane, 0); GlassPane = b.Id;
            b = Rock("iron_bars", "Iron Bars", Tex.IronBars, 3, 1, Sound.Metal); b.Shape = Shape.Boxes; b.Opaque = false; b.Opacity = 0; b.Cutout = true; b.Connect = Connect.Pane;
            b.Boxes = ConnectBoxes(Connect.Pane, 0); IronBars = b.Id;
            // Signs: standing on the ground facing the player, or hung on a wall.
            Sign = Facings("sign", "Sign", 4, (d, f) =>
            {
                d.Shape = Shape.Boxes; d.Solid = false; d.Opaque = false; d.Opacity = 0; d.Hardness = 1; d.Tool = Tool.Axe; d.Sound = Sound.Wood; d.FlatIcon = true;
                d.Top = d.Bottom = d.Side = Tex.OakPlanks;
                d.Boxes = new[] { Box.Px(7, 0, 7, 9, 8, 9), Box.Px(0, 8, 7, 16, 16, 9) };
            });
            WallSign = Facings("wall_sign", "Sign", 4, (d, f) =>
            {
                d.Shape = Shape.Boxes; d.Solid = false; d.Opaque = false; d.Opacity = 0; d.Hardness = 1; d.Tool = Tool.Axe; d.Sound = Sound.Wood; d.DropItem = "sign";
                d.Top = d.Bottom = d.Side = Tex.OakPlanks;
                d.Boxes = new[] { Box.Px(0, 4, 14, 16, 12, 16) };
            });
            Defs[WallSign].Creative = false; Palette.Remove(WallSign);
            // Flowing water and lava, by how far they are from their source.
            Defs[Water].Fluid = 1; WaterAt[0] = Water;
            Defs[Lava].Fluid = 2; LavaAt[0] = Lava;
            for (int l = 1; l <= WaterReach; l++)
            {
                b = Add("water_" + l, "Water", Tex.Water); b.Shape = Shape.Liquid; b.Solid = false; b.Opaque = false; b.Opacity = 2; b.Hardness = -1;
                b.Replaceable = true; b.Creative = false; b.Fluid = 1; b.Level = (byte)l; b.BaseId = Water; Palette.Remove(b.Id); WaterAt[l] = b.Id;
            }
            for (int l = 1; l <= LavaReach; l++)
            {
                b = Add("lava_" + l, "Lava", Tex.Lava); b.Shape = Shape.Liquid; b.Solid = false; b.Opaque = false; b.Opacity = 1; b.Emit = 15; b.Hardness = -1;
                b.Replaceable = true; b.Creative = false; b.Fluid = 2; b.Level = (byte)l; b.BaseId = Lava; Palette.Remove(b.Id); LavaAt[l] = b.Id;
            }

            // ---------------------------------------------------------------- 0.2: farming
            for (int wet = 0; wet < 2; wet++)
            {
                b = Add(wet == 0 ? "farmland" : "farmland_wet", "Farmland", wet == 0 ? Tex.FarmlandDry : Tex.FarmlandWet, Tex.Dirt, Tex.Dirt);
                b.Shape = Shape.Boxes; b.Opaque = false; b.Opacity = 15; b.Hardness = 0.6f; b.Tool = Tool.Shovel; b.Sound = Sound.Dirt; b.DropItem = "dirt";
                b.Boxes = new[] { Box.Px(0, 0, 0, 16, 15, 16) }; b.Variant = wet; b.Creative = false; Palette.Remove(b.Id);
                if (wet == 0) Farmland = b.Id; else { FarmlandWet = b.Id; b.BaseId = Farmland; }
            }
            for (int st = 0; st < WheatStages; st++)
            {
                b = Plant("wheat_" + st, "Wheat", (Tex)((int)Tex.Wheat0 + st)); b.Replaceable = false; b.Sway = st > 0; b.Grows = 1; b.Variant = st;
                b.Creative = false; Palette.Remove(b.Id);
                if (st < WheatStages - 1) b.DropItem = "seeds";
                else { b.DropItem = "wheat"; b.Bonus = "seeds"; b.BonusChance = 1; b.Bonus2 = "seeds"; b.Bonus2Chance = 0.5f; }
                if (st == 0) Wheat = b.Id; else b.BaseId = Wheat;
            }
            SaplingOak = Plant("oak_sapling", "Oak Sapling", Tex.SaplingOak).Id;
            SaplingBirch = Plant("birch_sapling", "Birch Sapling", Tex.SaplingBirch).Id;
            SaplingPine = Plant("pine_sapling", "Pine Sapling", Tex.SaplingPine).Id;
            foreach (var sp in new[] { SaplingOak, SaplingBirch, SaplingPine }) { Defs[sp].Replaceable = false; Defs[sp].Grows = 2; }
            b = Plant("sugar_cane", "Sugar Cane", Tex.SugarCane); b.Replaceable = false; b.Sway = false; b.Grows = 3; SugarCane = b.Id;
            b = Add("hay_bale", "Hay Bale", Tex.HayTop, Tex.HaySide, Tex.HayTop); b.Hardness = 0.5f; b.Sound = Sound.Grass; HayBale = b.Id;
            Defs[TallGrass].Bonus = "seeds"; Defs[TallGrass].BonusChance = 0.125f; Defs[TallGrass].Bonus2 = "berries"; Defs[TallGrass].Bonus2Chance = 0.05f;
            Defs[OakLeaves].Bonus2 = "oak_sapling"; Defs[OakLeaves].Bonus2Chance = 0.05f;
            Defs[BirchLeaves].Bonus2 = "birch_sapling"; Defs[BirchLeaves].Bonus2Chance = 0.05f;
            Defs[PineNeedles].Bonus2 = "pine_sapling"; Defs[PineNeedles].Bonus2Chance = 0.05f;

            // ---------------------------------------------------------------- 0.2: villages
            b = Rock("jade_ore", "Jade Ore", Tex.JadeOre, 3.5f, 4); b.TierNeeded = 3; b.DropItem = "jade"; JadeOre = b.Id;
            b = Add("jade_block", "Block of Jade", Tex.JadeBlock); b.Hardness = 5; b.Tool = Tool.Pickaxe; b.TierNeeded = 1; b.Sound = Sound.Stone; JadeBlock = b.Id;

            // ---------------------------------------------------------------- 0.2: enchanting
            b = Add("enchanting_table", "Enchanting Table", Tex.EnchantTop, Tex.EnchantSide, Tex.Obsidian);
            b.Shape = Shape.Boxes; b.Opaque = false; b.Opacity = 0; b.Hardness = 5; b.Tool = Tool.Pickaxe; b.TierNeeded = 1; b.Sound = Sound.Stone; b.Emit = 7;
            b.Boxes = new[] { Box.Px(0, 0, 0, 16, 12, 16) }; EnchantingTable = b.Id;
            Anvil = Facings("anvil", "Anvil", 4, (d, i) =>
            {
                d.Shape = Shape.Boxes; d.Opaque = false; d.Opacity = 1; d.Hardness = 5; d.Tool = Tool.Pickaxe; d.TierNeeded = 1; d.Sound = Sound.Metal;
                d.Top = Tex.AnvilTop; d.Side = d.Bottom = Tex.AnvilSide;
                d.Boxes = new[] { Box.Px(2, 0, 2, 14, 4, 14), Box.Px(4, 4, 5, 12, 10, 11), Box.Px(0, 10, 3, 16, 16, 13) };
            });

            Palette.Remove(Water);
        }

        public static bool IsWater(byte id) => Defs[id] != null && Defs[id].Fluid == 1;
        public static bool IsLava(byte id) => Defs[id] != null && Defs[id].Fluid == 2;
        public static bool IsFluid(byte id) => Defs[id] != null && Defs[id].Fluid != 0;

        // ------------------------------------------------------------------ connecting shapes

        static readonly Box[][][] _connect = new Box[4][][];

        // The pieces of a fence, wall or pane joined toward the sides in `mask` (1 +Z, 2 +X, 4 -Z, 8 -X).
        public static Box[] ConnectBoxes(Connect kind, int mask)
        {
            int k = (int)kind;
            if (_connect[k] == null) _connect[k] = new Box[16][];
            var cached = _connect[k][mask & 15];
            if (cached != null) return cached;
            var list = new List<Box>();
            Box post, arm1, arm2 = default; bool two = false;
            switch (kind)
            {
                case Connect.Fence: post = Box.Px(6, 0, 6, 10, 16, 10); arm1 = Box.Px(7, 6, 10, 9, 9, 16); arm2 = Box.Px(7, 12, 10, 9, 15, 16); two = true; break;
                case Connect.Wall: post = Box.Px(4, 0, 4, 12, 16, 12); arm1 = Box.Px(5, 0, 12, 11, 13, 16); break;
                default: post = Box.Px(7, 0, 7, 9, 16, 9); arm1 = Box.Px(7, 0, 9, 9, 16, 16); break;
            }
            list.Add(post);
            for (int dir = 0; dir < 4; dir++)
            {
                if ((mask & (1 << dir)) == 0) continue;
                list.Add(arm1.Turn(dir));
                if (two) list.Add(arm2.Turn(dir));
            }
            return _connect[k][mask & 15] = list.ToArray();
        }

        static readonly int[] DX = { 0, 1, 0, -1 }, DZ = { 1, 0, -1, 0 };

        // Does a fence, wall or pane reach toward this neighbour?
        public static bool Joins(BlockDef self, byte other)
        {
            if (other == 0) return false;
            var o = Get(other);
            if (o.Connect == self.Connect) return true;
            if (self.Connect == Connect.Fence && o.BaseId == FenceGate) return true;
            if (self.Connect == Connect.Pane && o.Connect == Connect.Pane) return true;
            return o.Shape == Shape.Cube && o.Solid && o.Opaque;
        }

        // The shape of the block at a position: its own boxes, or for joining blocks the ones that fit its neighbours.
        public static Box[] ShapeAt(Func<int, int, int, byte> get, int x, int y, int z, BlockDef d)
        {
            if (d.Connect == Connect.None) return d.Boxes;
            int mask = 0;
            for (int dir = 0; dir < 4; dir++) if (Joins(d, get(x + DX[dir], y, z + DZ[dir]))) mask |= 1 << dir;
            return ConnectBoxes(d.Connect, mask);
        }

        // A family of blocks sharing one item: `count` ids; the first is the item and the rest drop it. Their boxes
        // are given for facing 0 (front toward +Z) and turned for each facing.
        static byte Facings(string key, string name, int count, Action<BlockDef, int> make, Func<int, int> facingOf = null)
        {
            byte first = 0;
            for (int i = 0; i < count; i++)
            {
                var d = Add(i == 0 ? key : key + "_" + i, name, Tex.OakPlanks);
                if (i == 0) first = d.Id; else { d.Creative = false; Palette.Remove(d.Id); d.DropItem ??= key; }
                d.BaseId = first;
                int f = facingOf != null ? facingOf(i) : i;
                d.Facing = (byte)(f & 3);
                make(d, i);
                if (i != 0 && d.DropItem == null) d.DropItem = key;
                if (d.Boxes != null) for (int k = 0; k < d.Boxes.Length; k++) d.Boxes[k] = d.Boxes[k].Turn(d.Facing);
            }
            return first;
        }

        public static BlockDef Get(byte id) => Defs[id] ?? Defs[0];
        public static byte Id(string key) => ByKey.TryGetValue(key, out var v) ? v : (byte)0;
        public static int Count => _next;

        // The facing variant of a block placed looking along (dx, dz): its front faces the player.
        public static byte Facing(byte id, float lookX, float lookZ)
        {
            var d = Get(id);
            if (!d.HasFront) return id;
            int f;
            if (System.Math.Abs(lookX) > System.Math.Abs(lookZ)) f = lookX > 0 ? 3 : 1; else f = lookZ > 0 ? 2 : 0;
            return (byte)(d.BaseId + f);
        }
    }
}
