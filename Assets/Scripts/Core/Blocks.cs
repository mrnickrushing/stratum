using System.Collections.Generic;

namespace Stratum.Core
{
    public enum Shape : byte { Cube, Cross, Torch, Liquid }
    public enum Sound : byte { Stone, Dirt, Grass, Sand, Wood, Leaves, Glass, Snow, Metal, Cloth }
    public enum Tool : byte { None, Pickaxe, Axe, Shovel }

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
        ClayBricks, SlateTiles, Count
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
            CopperBlock, IronBlock, GoldBlock, ClayBricks, SlateTiles;

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
            OakLeaves = Leaves("oak_leaves", "Oak Leaves", Tex.OakLeaves).Id;
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

            TallGrass = Plant("tall_grass", "Tall Grass", Tex.TallGrass).Id; Defs[TallGrass].DropItem = "";
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

            Palette.Remove(Water);
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
