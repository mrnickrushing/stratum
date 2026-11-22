using System;
using System.Collections.Generic;

namespace Stratum.Core
{
    public sealed class ItemDef
    {
        public string Id;
        public string Name;
        public ushort Block;           // the block it places (0 = not a block)
        public int MaxStack = 64;
        public Tool Tool;            // tools: what they're for
        public int Tier;             // tools: 1 wood, 2 stone, 3 copper, 4 iron, 5 starstone
        public int Durability;       // tools: uses before it breaks
        public float Speed = 1;      // tools: how much faster than a hand
        public bool Creative = true;
        public float Damage = 1;     // a hit with it (a bare hand is 1)
        public int Food;             // hunger it fills when eaten (0: not food)
        public float Saturation;     // how long that keeps hunger away
        public int ArmourSlot = -1;  // 0 head, 1 body, 2 legs, 3 feet
        public int Armour;           // protection points (20 in all stops 80% of a hit)
        public bool Ranged;          // the bow
        public bool Drink;           // milk and potions: drunk whatever your hunger; what held it comes back (Leaves)
        public string Leaves;        // what's left in the hand after drinking
        public string Effect;        // potions: what it does (Player.Fx by name), for EffectSecs seconds
        public float EffectSecs;
        public Dictionary<char, int> Enchants;   // enchanted copies only
        public string Base;                      // enchanted copies: the plain item
        public ItemDef MemberwiseCopy() => (ItemDef)MemberwiseClone();
        public int Ench(char c) => Enchants != null && Enchants.TryGetValue(c, out int l) ? l : 0;
    }

    // Experience: points from creatures, ores and the furnace fill levels; levels pay for enchanting.
    public static class Xp
    {
        public static int Next(int level) => level < 16 ? 2 * level + 7 : level < 31 ? 5 * level - 38 : 9 * level - 158;

        public static void Add(ref int level, ref int points, int n)
        {
            points += n;
            while (points >= Next(level)) { points -= Next(level); level++; }
        }

        // What dying leaves behind (the rest is lost).
        public static int Dropped(int level) => Math.Min(100, level * 7);
    }

    public struct Stack
    {
        public string Item; public int Count; public int Wear;
        public bool Empty => Item == null || Count <= 0;
        public Stack(string item, int count) { Item = item; Count = count; Wear = 0; }
        public static readonly Stack None = default;
    }

    public sealed class Recipe
    {
        public string Out; public int Count;
        public (string item, int count)[] In;
        public string Station;       // null = by hand, "workbench", "furnace"
        public string[] Shape;       // the pattern in the crafting grid (Crafting.Patterns); null: any squares
    }

    public static class Items
    {
        public static readonly Dictionary<string, ItemDef> Defs = new Dictionary<string, ItemDef>();
        public static readonly List<ItemDef> Order = new List<ItemDef>();
        public static readonly List<Recipe> Recipes = new List<Recipe>();

        static ItemDef Add(ItemDef d) { Defs[d.Id] = d; Order.Add(d); return d; }

        // Potions: what each is called, what it does and for how long, and the thing that goes in with the ember.
        public static readonly (string key, string name, string effect, float secs, string with)[] Potions =
        {
            ("swift", "Swiftness", "Swift", 180, "sugar_cane"), ("leap", "Leaping", "Leap", 180, "egg"), ("strength", "Strength", "Strength", 180, "bone"),
            ("regen", "Regeneration", "Regen", 45, "apple"), ("heal", "Healing", "Heal", 0, "berries"), ("fire", "Fire Resistance", "FireProof", 180, "coal"),
            ("gills", "Water Breathing", "Gills", 180, "raw_fish"), ("night", "Night Vision", "NightEyes", 180, "carrot"), ("feather", "Slow Falling", "Feather", 90, "feather"),
            ("haste", "Haste", "Haste", 180, "sunstone_shard"),
        };

        static void ToolSet(string mat, string matName, int tier, int dur, float speed)
        {
            Add(new ItemDef { Id = mat + "_pickaxe", Name = matName + " Pickaxe", MaxStack = 1, Tool = Tool.Pickaxe, Tier = tier, Durability = dur, Speed = speed });
            Add(new ItemDef { Id = mat + "_axe", Name = matName + " Axe", MaxStack = 1, Tool = Tool.Axe, Tier = tier, Durability = dur, Speed = speed });
            Add(new ItemDef { Id = mat + "_shovel", Name = matName + " Shovel", MaxStack = 1, Tool = Tool.Shovel, Tier = tier, Durability = dur, Speed = speed });
        }

        static void R(string output, int count, string station, params (string, int)[] inputs) =>
            Recipes.Add(new Recipe { Out = output, Count = count, In = inputs, Station = station });

        static Items()
        {
            for (int i = 1; i < Blocks.Count; i++)
            {
                var b = Blocks.Defs[i];
                if (b == null || !b.Creative) continue;
                Add(new ItemDef { Id = Blocks.Keys[i], Name = b.Name, Block = (ushort)i });
            }
            Add(new ItemDef { Id = "stick", Name = "Stick" });
            Add(new ItemDef { Id = "coal", Name = "Coal" });
            Add(new ItemDef { Id = "copper_ingot", Name = "Copper Ingot" });
            Add(new ItemDef { Id = "iron_ingot", Name = "Iron Ingot" });
            Add(new ItemDef { Id = "gold_ingot", Name = "Gold Ingot" });
            Add(new ItemDef { Id = "sunstone_shard", Name = "Sunstone Shard" });
            ToolSet("wood", "Wooden", 1, 60, 2f);
            ToolSet("stone", "Stone", 2, 130, 3.5f);
            ToolSet("copper", "Copper", 3, 220, 5f);
            ToolSet("iron", "Iron", 4, 400, 7f);
            ToolSet("star", "Starstone", 5, 1200, 9f);
            ToolSet("gold", "Golden", 1, 40, 11f);           // soft: quick while it lasts
            Add(new ItemDef { Id = "starstone", Name = "Starstone" });
            foreach (var (mat, name, tier, dur, dmg) in new[] { ("wood", "Wooden", 1, 60, 4f), ("stone", "Stone", 2, 130, 5f), ("copper", "Copper", 3, 220, 6f), ("iron", "Iron", 4, 400, 7f), ("star", "Starstone", 5, 1200, 8f), ("gold", "Golden", 1, 40, 4f) })
            {
                Add(new ItemDef { Id = mat + "_sword", Name = name + " Sword", MaxStack = 1, Tool = Tool.Sword, Tier = tier, Durability = dur, Speed = 1.5f, Damage = dmg });
                // Tools hit a little harder than a fist too.
                Defs[mat + "_axe"].Damage = dmg - 1; Defs[mat + "_pickaxe"].Damage = dmg - 2; Defs[mat + "_shovel"].Damage = dmg - 2.5f;
            }
            Add(new ItemDef { Id = "bow", Name = "Bow", MaxStack = 1, Durability = 300, Ranged = true });
            Add(new ItemDef { Id = "arrow", Name = "Arrow" });
            Add(new ItemDef { Id = "shears", Name = "Shears", MaxStack = 1, Tool = Tool.Shears, Tier = 1, Durability = 200, Speed = 5 });

            // Food: hunger filled, and how long it lasts.
            void Food(string id, string name, int food, float sat) => Add(new ItemDef { Id = id, Name = name, Food = food, Saturation = sat });
            Food("apple", "Apple", 4, 2.4f);
            Food("bread", "Bread", 5, 6f);
            Food("raw_beef", "Raw Beef", 3, 1.8f);
            Food("steak", "Steak", 8, 12.8f);
            Food("raw_pork", "Raw Pork", 3, 1.8f);
            Food("roast_pork", "Roast Pork", 8, 12.8f);
            Food("raw_mutton", "Raw Mutton", 2, 1.2f);
            Food("roast_mutton", "Roast Mutton", 6, 9.6f);
            Food("raw_chicken", "Raw Chicken", 2, 1.2f);
            Food("roast_chicken", "Roast Chicken", 6, 7.2f);
            Food("berries", "Wild Berries", 2, 0.4f);

            Add(new ItemDef { Id = "seeds", Name = "Wheat Seeds", Block = Blocks.Wheat });
            Add(new ItemDef { Id = "wheat", Name = "Wheat" });
            Add(new ItemDef { Id = "bone_meal", Name = "Bone Meal" });
            Add(new ItemDef { Id = "paper", Name = "Paper" });
            Add(new ItemDef { Id = "book", Name = "Book" });
            foreach (var (mat, name, tier, dur) in new[] { ("wood", "Wooden", 1, 60), ("stone", "Stone", 2, 130), ("copper", "Copper", 3, 220), ("iron", "Iron", 4, 400), ("star", "Starstone", 5, 1200), ("gold", "Golden", 1, 40) })
                Add(new ItemDef { Id = mat + "_hoe", Name = name + " Hoe", MaxStack = 1, Tool = Tool.Hoe, Tier = tier, Durability = dur, Speed = 1, Damage = 1 });
            Add(new ItemDef { Id = "bucket", Name = "Bucket", MaxStack = 16 });
            Add(new ItemDef { Id = "jade", Name = "Jade" });
            Add(new ItemDef { Id = "spark", Name = "Spark Dust", Block = Blocks.SparkWire });
            Add(new ItemDef { Id = "flint", Name = "Flint" });
            Add(new ItemDef { Id = "fire_striker", Name = "Fire Striker", MaxStack = 1, Durability = 64 });
            Add(new ItemDef { Id = "blast_powder", Name = "Blast Powder" });
            // 0.14: fishing, root crops, eggs
            // Potions: a bottle, an ember and one more thing, mixed at a brewing stand.
            Add(new ItemDef { Id = "bottle", Name = "Glass Bottle", MaxStack = 16 });
            foreach (var (key, name, effect, secs, with) in Potions)
                Add(new ItemDef { Id = "potion_" + key, Name = "Potion of " + name, MaxStack = 16, Drink = true, Leaves = "bottle", Effect = effect, EffectSecs = secs });
            // Dyes: sixteen colours, for wool and for sheep.
            foreach (var (key, name, rgb) in Blocks.Dyes) Add(new ItemDef { Id = "dye_" + key, Name = name + " Dye" });
            // Splash potions: thrown, they burst over everyone near.
            foreach (var (key, name, effect, secs, with) in Potions)
                Add(new ItemDef { Id = "splash_" + key, Name = "Splash Potion of " + name, MaxStack = 16, Effect = effect, EffectSecs = secs * 0.75f });
            Add(new ItemDef { Id = "jelly", Name = "Glob Jelly" });
            Add(new ItemDef { Id = "hollow_crown", Name = "Hollow Crown", MaxStack = 1, ArmourSlot = 0, Armour = 3, Durability = 600 });
            Add(new ItemDef { Id = "shade_shard", Name = "Shade Shard" });
            Add(new ItemDef { Id = "shield", Name = "Shield", MaxStack = 1, Durability = 300 });
            Add(new ItemDef { Id = "fishing_rod", Name = "Fishing Rod", MaxStack = 1, Durability = 64 });
            Food("raw_fish", "Raw Fish", 2, 0.4f);
            Food("cooked_fish", "Cooked Fish", 5, 6);
            Add(new ItemDef { Id = "carrot", Name = "Carrot", Block = Blocks.Carrot, Food = 3, Saturation = 3.6f });
            Add(new ItemDef { Id = "potato", Name = "Potato", Block = Blocks.Potato, Food = 1, Saturation = 0.6f });
            Food("baked_potato", "Baked Potato", 5, 6);
            Food("egg", "Egg", 1, 0.2f);
            Food("fried_egg", "Fried Egg", 4, 4);
            Add(new ItemDef { Id = "boat", Name = "Boat", MaxStack = 1 });
            Add(new ItemDef { Id = "minecart", Name = "Minecart", MaxStack = 1 });
            Add(new ItemDef { Id = "chest_cart", Name = "Chest Cart", MaxStack = 1 });
            Add(new ItemDef { Id = "compass", Name = "Compass", MaxStack = 1 });
            Add(new ItemDef { Id = "clock", Name = "Clock", MaxStack = 1 });
            Add(new ItemDef { Id = "map", Name = "Map", MaxStack = 1 });
            Add(new ItemDef { Id = "xp", Name = "Experience", MaxStack = 1000, Creative = false });
            Add(new ItemDef { Id = "water_bucket", Name = "Water Bucket", MaxStack = 1 });
            Add(new ItemDef { Id = "lava_bucket", Name = "Lava Bucket", MaxStack = 1 });
            Add(new ItemDef { Id = "milk_bucket", Name = "Milk Bucket", MaxStack = 1, Drink = true, Food = 1, Saturation = 1, Leaves = "bucket" });
            Add(new ItemDef { Id = "hide", Name = "Hide" });
            Add(new ItemDef { Id = "feather", Name = "Feather" });
            Add(new ItemDef { Id = "bone", Name = "Bone" });
            Add(new ItemDef { Id = "thread", Name = "Thread" });
            Add(new ItemDef { Id = "ember", Name = "Ember Shard" });
            Add(new ItemDef { Id = "colossus_heart", Name = "Heart of Cinder", MaxStack = 16 });

            // Armour: a cap, a coat, leggings and boots of hide, copper or iron.
            string[] pieces = { "cap", "coat", "leggings", "boots" }, pieceNames = { "Cap", "Coat", "Leggings", "Boots" };
            int[] need = { 5, 8, 7, 4 };
            foreach (var (mat, name, item, pts, dur) in new[] { ("hide", "Hide", "hide", new[] { 1, 3, 2, 1 }, 80), ("copper", "Copper", "copper_ingot", new[] { 2, 5, 4, 1 }, 180), ("iron", "Iron", "iron_ingot", new[] { 2, 6, 5, 2 }, 260), ("star", "Starstone", "starstone", new[] { 3, 8, 6, 3 }, 420), ("gold", "Golden", "gold_ingot", new[] { 2, 5, 3, 1 }, 90) })
                for (int i = 0; i < 4; i++)
                {
                    Add(new ItemDef { Id = mat + "_" + pieces[i], Name = name + " " + pieceNames[i], MaxStack = 1, ArmourSlot = i, Armour = pts[i], Durability = dur });
                    R(mat + "_" + pieces[i], 1, "workbench", (item, need[i]));
                }

            // By hand
            R("oak_planks", 4, null, ("oak_log", 1));
            R("birch_planks", 4, null, ("birch_log", 1));
            R("pine_planks", 4, null, ("pine_log", 1));
            R("stick", 4, null, ("@planks", 2));
            R("workbench", 1, null, ("@planks", 4));
            R("torch", 4, null, ("stick", 1), ("coal", 1));
            // At a workbench
            foreach (var (mat, item, n) in new[] { ("wood", "@planks", 3), ("stone", "cobble", 3), ("copper", "copper_ingot", 3), ("iron", "iron_ingot", 3), ("star", "starstone", 3), ("gold", "gold_ingot", 3) })
            {
                R(mat + "_pickaxe", 1, "workbench", (item, n), ("stick", 2));
                R(mat + "_axe", 1, "workbench", (item, n), ("stick", 2));
                R(mat + "_shovel", 1, "workbench", (item, 1), ("stick", 2));
            }
            R("furnace", 1, "workbench", ("cobble", 8));
            R("stone_bricks", 4, "workbench", ("stone", 4));
            R("slate_tiles", 4, "workbench", ("slate", 4));
            R("sandstone", 1, "workbench", ("sand", 4));
            R("sunstone", 1, "workbench", ("sunstone_shard", 4));
            R("copper_block", 1, "workbench", ("copper_ingot", 9));
            R("iron_block", 1, "workbench", ("iron_ingot", 9));
            R("gold_block", 1, "workbench", ("gold_ingot", 9));
            R("mossy_cobble", 2, "workbench", ("cobble", 2), ("tall_grass", 1));
            foreach (var (mat, item) in new[] { ("wood", "@planks"), ("stone", "cobble"), ("copper", "copper_ingot"), ("iron", "iron_ingot"), ("star", "starstone"), ("gold", "gold_ingot") })
                R(mat + "_sword", 1, "workbench", (item, 2), ("stick", 1));
            R("bow", 1, "workbench", ("stick", 3), ("thread", 3));
            R("arrow", 4, "workbench", ("stick", 1), ("feather", 1), ("cobble", 1));
            R("shears", 1, "workbench", ("copper_ingot", 2));
            R("chest", 1, "workbench", ("@planks", 8));
            R("bed", 1, "workbench", ("white_wool", 3), ("@planks", 3));
            R("white_wool", 1, null, ("thread", 4));
            // Building.
            foreach (var m in new[] { ("oak", "oak_planks"), ("birch", "birch_planks"), ("pine", "pine_planks"), ("cobble", "cobble"), ("stone", "stone"), ("stone_brick", "stone_bricks") })
                R(m.Item1 + "_slab", 6, "workbench", (m.Item2, 3));
            foreach (var m in new[] { ("oak", "oak_planks"), ("birch", "birch_planks"), ("pine", "pine_planks"), ("cobble", "cobble"), ("stone_brick", "stone_bricks") })
                R(m.Item1 + "_stairs", 4, "workbench", (m.Item2, 6));
            R("oak_door", 3, "workbench", ("@planks", 6));
            R("trapdoor", 2, "workbench", ("@planks", 6));
            R("ladder", 3, "workbench", ("stick", 7));
            R("oak_fence", 3, "workbench", ("@planks", 4), ("stick", 2));
            R("oak_fence_gate", 1, "workbench", ("stick", 4), ("@planks", 2));
            R("cobble_wall", 6, "workbench", ("cobble", 6));
            R("glass_pane", 16, "workbench", ("glass", 6));
            R("iron_bars", 16, "workbench", ("iron_ingot", 6));
            R("sign", 3, "workbench", ("@planks", 6), ("stick", 1));
            R("bucket", 1, "workbench", ("iron_ingot", 3));
            R("boat", 1, "workbench", ("oak_planks", 5));
            R("minecart", 1, "workbench", ("iron_ingot", 5));
            R("chest_cart", 1, "workbench", ("minecart", 1), ("chest", 1));
            R("rail", 16, "workbench", ("iron_ingot", 6), ("stick", 1));
            R("booster_rail", 6, "workbench", ("gold_ingot", 6), ("stick", 1), ("spark", 1));
            R("spark_torch", 1, null, ("spark", 1), ("stick", 1));
            R("fire_striker", 1, null, ("iron_ingot", 1), ("flint", 1));
            R("blast_keg", 1, "workbench", ("blast_powder", 5), ("sand", 4));
            R("fishing_rod", 1, "workbench", ("stick", 3), ("thread", 2));
            R("bottle", 3, "workbench", ("glass", 3));
            foreach (var (key, name, effect, secs, with) in Potions) R("splash_" + key, 1, "brewer", ("potion_" + key, 1), ("blast_powder", 1));
            // Dyes from flowers and the like, and from each other.
            R("dye_red", 2, null, ("red_flower", 1)); R("dye_yellow", 2, null, ("yellow_flower", 1)); R("dye_blue", 2, null, ("blue_flower", 1));
            R("dye_green", 2, null, ("cactus", 1)); R("dye_black", 2, null, ("coal", 1)); R("dye_white", 2, null, ("bone_meal", 1)); R("dye_brown", 2, null, ("dirt", 1), ("bone_meal", 1));
            R("dye_orange", 2, null, ("dye_red", 1), ("dye_yellow", 1)); R("dye_purple", 2, null, ("dye_red", 1), ("dye_blue", 1));
            R("dye_cyan", 2, null, ("dye_green", 1), ("dye_blue", 1)); R("dye_lime", 2, null, ("dye_green", 1), ("dye_white", 1));
            R("dye_pink", 2, null, ("dye_red", 1), ("dye_white", 1)); R("dye_light_blue", 2, null, ("dye_blue", 1), ("dye_white", 1));
            R("dye_grey", 2, null, ("dye_black", 1), ("dye_white", 1)); R("dye_light_grey", 2, null, ("dye_grey", 1), ("dye_white", 1));
            R("dye_magenta", 2, null, ("dye_purple", 1), ("dye_pink", 1));
            foreach (var (key, name, rgb) in Blocks.Dyes) if (key != "white") R(key + "_wool", 1, null, ("white_wool", 1), ("dye_" + key, 1));
            R("gate_stone", 1, "workbench", ("obsidian", 4), ("shade_shard", 4), ("starstone", 1));
            R("brewing_stand", 1, "workbench", ("cobble", 3), ("ember", 1));
            foreach (var (key, name, effect, secs, with) in Potions) R("potion_" + key, 1, "brewer", ("bottle", 1), ("ember", 1), (with, 1));
            R("detector_rail", 6, "workbench", ("iron_ingot", 6), ("pressure_plate", 1), ("spark", 1));
            R("daylight_sensor", 1, "workbench", ("glass", 3), ("sunstone_shard", 3), ("@planks", 3));
            R("note_block", 1, "workbench", ("@planks", 8), ("spark", 1));
            R("piston", 1, "workbench", ("@planks", 3), ("cobble", 4), ("iron_ingot", 1), ("spark", 1));
            R("sticky_piston", 1, "workbench", ("piston", 1), ("clay", 2));
            R("sticky_piston", 1, "workbench", ("piston", 1), ("jelly", 1));
            R("dispenser", 1, "workbench", ("cobble", 7), ("bow", 1), ("spark", 1));
            R("observer", 1, "workbench", ("cobble", 6), ("spark", 2), ("sunstone_shard", 1));
            R("comparator", 1, "workbench", ("stone", 3), ("spark_torch", 3), ("sunstone_shard", 1));
            R("cooked_fish", 1, "furnace", ("raw_fish", 1));
            R("baked_potato", 1, "furnace", ("potato", 1));
            R("fried_egg", 1, "furnace", ("egg", 1));
            R("cinder_bricks", 1, "furnace", ("cinderstone", 2));
            R("lever", 1, null, ("cobble", 1), ("stick", 1));
            R("button", 1, null, ("stone", 1));
            R("pressure_plate", 1, "workbench", ("stone", 2));
            R("spark_lamp", 1, "workbench", ("spark", 4), ("sunstone_shard", 1));
            R("repeater", 1, "workbench", ("spark", 1), ("spark_torch", 2), ("stone", 3));
            R("jade_block", 1, "workbench", ("jade", 9));
            R("jade", 9, null, ("jade_block", 1));
            R("compass", 1, "workbench", ("iron_ingot", 4), ("sunstone_shard", 1));
            R("clock", 1, "workbench", ("gold_ingot", 4), ("sunstone_shard", 1));
            R("map", 1, "workbench", ("paper", 8), ("compass", 1));
            R("enchanting_table", 1, "workbench", ("book", 1), ("sunstone_shard", 2), ("obsidian", 4));
            R("anvil", 1, "workbench", ("iron_block", 3), ("iron_ingot", 4));
            R("chute", 1, "workbench", ("iron_ingot", 5), ("chest", 1));
            // For houses.
            R("plaster", 4, "workbench", ("sand", 2), ("clay", 2));
            R("timber_frame", 4, "workbench", ("plaster", 4), ("stick", 2));
            R("roof_tiles", 4, "workbench", ("clay_bricks", 4));
            R("polished_stone", 4, "workbench", ("stone_bricks", 4));
            R("dark_planks", 4, "workbench", ("@planks", 4), ("coal", 1));
            R("white_planks", 4, "workbench", ("@planks", 4), ("bone_meal", 1));
            R("bookshelf", 1, "workbench", ("@planks", 6), ("book", 3));
            R("barrel", 1, "workbench", ("@planks", 6), ("iron_ingot", 2));
            R("hearth", 1, "workbench", ("cobble", 7), ("coal", 2));
            R("lantern", 1, "workbench", ("iron_ingot", 4), ("torch", 1));
            R("table", 1, "workbench", ("@planks", 3), ("stick", 2));
            R("stool", 2, "workbench", ("@planks", 1), ("stick", 2));
            R("carpet_red", 3, "workbench", ("red_wool", 2));
            R("carpet_blue", 3, "workbench", ("blue_wool", 2));
            R("flower_pot", 1, "workbench", ("clay_bricks", 3), ("red_flower", 1));
            R("lattice_window", 8, "workbench", ("glass", 4), ("stick", 2));
            R("stone_pillar", 2, "workbench", ("stone", 3));
            // The 0.11 building set. A colour comes from wool of that colour.
            R("orange_wool", 1, null, ("white_wool", 1), ("red_flower", 1), ("yellow_flower", 1));
            R("lime_wool", 1, null, ("white_wool", 1), ("cactus", 1), ("yellow_flower", 1));
            R("cyan_wool", 1, null, ("white_wool", 1), ("cactus", 1), ("blue_flower", 1));
            R("light_blue_wool", 1, null, ("white_wool", 1), ("blue_flower", 1), ("bone_meal", 1));
            R("purple_wool", 1, null, ("white_wool", 1), ("red_flower", 1), ("blue_flower", 1));
            R("magenta_wool", 1, null, ("purple_wool", 1), ("red_flower", 1));
            R("pink_wool", 1, null, ("white_wool", 1), ("red_flower", 1), ("bone_meal", 1));
            R("light_grey_wool", 1, null, ("grey_wool", 1), ("bone_meal", 1));
            foreach (var (key, _, _) in Blocks.Dyes)
            {
                if (key != "red" && key != "blue") R("carpet_" + key, 3, "workbench", (key + "_wool", 2));
                if (key != "white") R("plaster_" + key, 4, "workbench", ("plaster", 4), (key + "_wool", 1));
                R("tiles_" + key, 4, "workbench", ("polished_stone", 4), (key + "_wool", 1));
            }
            foreach (var c in Blocks.PaintedPlanks) R(c + "_planks", 4, "workbench", ("@planks", 4), (c + "_wool", 1));
            foreach (var m in Blocks.SlabMaterials) R(m.Split('|')[0] + "_slab", 6, "workbench", (m.Split('|')[1], 3));
            foreach (var m in Blocks.StairMaterials) R(m.Split('|')[0] + "_stairs", 4, "workbench", (m.Split('|')[1], 6));
            foreach (var m in Blocks.WallMaterials) R(m.Split('|')[0] + "_wall", 6, "workbench", (m.Split('|')[1], 6));
            foreach (var w in Blocks.Woods)
            {
                R(w + "_fence", 3, "workbench", (w + "_planks", 4), ("stick", 2));
                R(w + "_door", 3, "workbench", (w + "_planks", 6));
            }
            R("picket_fence", 3, "workbench", ("white_planks", 4), ("stick", 2));
            R("hedge", 4, "workbench", ("oak_leaves", 4));
            R("chair", 1, "workbench", ("@planks", 2), ("stick", 3));
            R("bench", 1, "workbench", ("@planks", 3), ("stick", 4));
            foreach (var c in Blocks.SofaColours) R("sofa_" + c, 1, "workbench", (c + "_wool", 4), ("@planks", 2));
            R("cabinet", 1, "workbench", ("@planks", 6), ("stick", 2));
            R("wardrobe", 1, "workbench", ("@planks", 6), ("white_wool", 2));
            R("crate", 1, "workbench", ("@planks", 4), ("stick", 4));
            R("counter", 1, "workbench", ("@planks", 4), ("polished_stone", 2));
            R("shelf", 2, "workbench", ("@planks", 3), ("stick", 1));
            R("stove", 1, "workbench", ("iron_ingot", 6), ("coal", 2));
            R("fridge", 1, "workbench", ("iron_ingot", 6), ("glass", 1));
            R("screen", 1, "workbench", ("iron_ingot", 2), ("glass", 2), ("spark", 1));
            R("piano", 1, "workbench", ("@planks", 6), ("thread", 3));
            R("bathtub", 1, "workbench", ("clay", 6));
            R("toilet", 1, "workbench", ("clay", 4));
            R("sink", 1, "workbench", ("clay", 3), ("iron_ingot", 1));
            R("mailbox", 1, "workbench", ("iron_ingot", 3), ("stick", 1));
            R("painting_sunset", 1, "workbench", ("stick", 4), ("white_wool", 1), ("red_flower", 1));
            R("painting_lake", 1, "workbench", ("stick", 4), ("white_wool", 1), ("blue_flower", 1));
            R("painting_forest", 1, "workbench", ("stick", 4), ("white_wool", 1), ("tall_grass", 1));
            R("mirror", 1, "workbench", ("glass", 3), ("stick", 2));
            R("wall_clock", 1, "workbench", ("gold_ingot", 2), ("@planks", 2));
            R("floor_lamp", 1, "workbench", ("torch", 1), ("stick", 2), ("white_wool", 1));
            R("candle", 2, "workbench", ("thread", 1), ("bone_meal", 2));
            R("chandelier", 1, "workbench", ("gold_ingot", 3), ("torch", 4));
            R("lamp_post", 1, "workbench", ("iron_ingot", 3), ("torch", 1));
            R("flower_pot_yellow", 1, "workbench", ("clay_bricks", 3), ("yellow_flower", 1));
            R("flower_pot_blue", 1, "workbench", ("clay_bricks", 3), ("blue_flower", 1));
            // Farming.
            R("bread", 1, "workbench", ("wheat", 3));
            R("hay_bale", 1, "workbench", ("wheat", 9));
            R("wheat", 9, null, ("hay_bale", 1));
            R("bone_meal", 3, null, ("bone", 1));
            R("paper", 3, "workbench", ("sugar_cane", 3));
            R("book", 1, "workbench", ("paper", 3), ("hide", 1));
            R("wood_hoe", 1, "workbench", ("@planks", 2), ("stick", 2));
            R("stone_hoe", 1, "workbench", ("cobble", 2), ("stick", 2));
            R("copper_hoe", 1, "workbench", ("copper_ingot", 2), ("stick", 2));
            R("iron_hoe", 1, "workbench", ("iron_ingot", 2), ("stick", 2));
            R("star_hoe", 1, "workbench", ("starstone", 2), ("stick", 2));
            R("gold_hoe", 1, "workbench", ("gold_ingot", 2), ("stick", 2));
            R("snow_layer", 6, "workbench", ("snow", 3));
            R("shield", 1, "workbench", ("@planks", 6), ("iron_ingot", 1));
            R("red_wool", 1, null, ("white_wool", 1), ("red_flower", 1));
            R("yellow_wool", 1, null, ("white_wool", 1), ("yellow_flower", 1));
            R("blue_wool", 1, null, ("white_wool", 1), ("blue_flower", 1));
            R("green_wool", 1, null, ("white_wool", 1), ("cactus", 1));
            R("black_wool", 1, null, ("white_wool", 1), ("coal", 1));
            R("grey_wool", 2, null, ("white_wool", 2), ("coal", 1));
            R("brown_wool", 1, null, ("white_wool", 1), ("dirt", 1));
            // Cooking: in a furnace, on fuel like the ores.
            R("steak", 1, "furnace", ("raw_beef", 1));
            R("roast_pork", 1, "furnace", ("raw_pork", 1));
            R("roast_mutton", 1, "furnace", ("raw_mutton", 1));
            R("roast_chicken", 1, "furnace", ("raw_chicken", 1));
            // In a furnace (coal is the fuel)
            R("stone", 1, "furnace", ("cobble", 1));
            R("glass", 1, "furnace", ("sand", 1));
            R("clay_bricks", 1, "furnace", ("clay", 1));
            R("copper_ingot", 1, "furnace", ("copper_ore", 1), ("coal", 1));
            R("iron_ingot", 1, "furnace", ("iron_ore", 1), ("coal", 1));
            R("gold_ingot", 1, "furnace", ("gold_ore", 1), ("coal", 1));
            R("coal", 1, "furnace", ("@log", 1));
            Crafting.Assign(Recipes);
        }

        public static int DerivedCount { get { lock (Derived) return Derived.Count; } }
        public static ItemDef Get(string id)
        {
            if (id == null) return null;
            if (Defs.TryGetValue(id, out var d)) return d;
            if (id.IndexOf('@') < 0) return null;
            if (id.Length > 64) return null;
            lock (Derived)
            {
                if (Derived.TryGetValue(id, out d)) return d;
                d = Enchants.Derive(id);
                if (d != null) Derived[id] = d;
                return d;
            }
        }
        static readonly Dictionary<string, ItemDef> Derived = new Dictionary<string, ItemDef>();

        // "@planks" and "@log" accept any kind of wood.
        public static bool Matches(string want, string have)
        {
            if (have == null) return false;
            if (want == "@planks") return have.EndsWith("_planks", StringComparison.Ordinal);
            if (want == "@log") return have.EndsWith("_log", StringComparison.Ordinal);
            return want == have;
        }

        public static string DisplayName(string want)
        {
            if (want == "@planks") return "Planks";
            if (want == "@log") return "Log";
            return Get(want)?.Name ?? want;
        }
    }

    public sealed class Inventory
    {
        public const int Hotbar = 9, Main = 36, Size = 40;     // 36..39: armour (head, body, legs, feet)
        public const int ArmourStart = Main;
        public const int OffHand = Size;                        // the other hand (a shield): kept after the pack's own slots, saved as the last loose stack
        public readonly Stack[] Slots = new Stack[Size + 1];
        public int Selected;

        public Stack Held => Slots[Selected];

        public int Count(string want)
        {
            int n = 0;
            for (int i = 0; i < Main; i++) { var s = Slots[i]; if (!s.Empty && Items.Matches(want, s.Item)) n += s.Count; }
            return n;
        }

        // Adds as much as fits; returns what's left over.
        public int Add(string item, int count)
        {
            var def = Items.Get(item);
            if (def == null || count <= 0) return count;
            for (int pass = 0; pass < 2 && count > 0; pass++)
                for (int i = 0; i < Main && count > 0; i++)
                {
                    ref var s = ref Slots[i];
                    if (pass == 0 && !s.Empty && s.Item == item && s.Count < def.MaxStack)
                    {
                        int k = Math.Min(count, def.MaxStack - s.Count); s.Count += k; count -= k;
                    }
                    else if (pass == 1 && s.Empty)
                    {
                        int k = Math.Min(count, def.MaxStack); s = new Stack(item, k); count -= k;
                    }
                }
            return count;
        }

        public bool Take(string want, int count)
        {
            if (Count(want) < count) return false;
            for (int i = Main - 1; i >= 0 && count > 0; i--)
            {
                ref var s = ref Slots[i];
                if (s.Empty || !Items.Matches(want, s.Item)) continue;
                int k = Math.Min(count, s.Count); s.Count -= k; count -= k;
                if (s.Count <= 0) s = Stack.None;
            }
            return true;
        }

        public void UseHeld(int n = 1)
        {
            ref var s = ref Slots[Selected];
            if (s.Empty) return;
            s.Count -= n;
            if (s.Count <= 0) s = Stack.None;
        }

        static readonly Random Luck = new Random();
        // Lasting: each level is another chance to shrug off the wear.
        static bool Spared(ItemDef d) { int u = d.Ench('u'); return u > 0 && Luck.Next(u + 1) != 0; }

        public int Warding { get { int n = 0; for (int i = ArmourStart; i < Size; i++) { var d = Items.Get(Slots[i].Item); if (d != null) n += d.Ench('p'); } return n; } }
        public int SoftLanding => Items.Get(Slots[Size - 1].Item)?.Ench('g') ?? 0;

        // Wears the held tool; true when it broke.
        // The shield took a blow. True if that broke it.
        public bool WearShield(int by)
        {
            ref var s = ref Slots[OffHand];
            var d = Items.Get(s.Item);
            if (d == null || d.Durability <= 0) return false;
            s.Wear += Math.Max(1, by);
            if (s.Wear >= d.Durability) { s = Stack.None; return true; }
            return false;
        }

        public bool HasShield => Slots[OffHand].Item == "shield";

        public bool WearHeld()
        {
            ref var s = ref Slots[Selected];
            var d = Items.Get(s.Item);
            if (d == null || d.Durability <= 0) return false;
            if (Spared(d)) return false;
            s.Wear++;
            if (s.Wear >= d.Durability) { s = Stack.None; return true; }
            return false;
        }

        // Room for this many more of an item?
        public int Room(string item)
        {
            var def = Items.Get(item);
            if (def == null) return 0;
            int n = 0;
            for (int i = 0; i < Main; i++)
            {
                var s = Slots[i];
                if (s.Empty) n += def.MaxStack;
                else if (s.Item == item && s.Wear == 0) n += Math.Max(0, def.MaxStack - s.Count);
            }
            return n;
        }

        public int ArmourPoints
        {
            get
            {
                int n = 0;
                for (int i = ArmourStart; i < Size; i++) { var d = Items.Get(Slots[i].Item); if (d != null) n += d.Armour; }
                return n;
            }
        }

        // A hit wears every piece worn; returns the names of any that broke.
        public string WearArmour()
        {
            string broke = null;
            for (int i = ArmourStart; i < Size; i++)
            {
                ref var s = ref Slots[i];
                var d = Items.Get(s.Item);
                if (d == null || d.Durability <= 0 || Spared(d)) continue;
                if (++s.Wear >= d.Durability) { broke = d.Name; s = Stack.None; }
            }
            return broke;
        }

        public bool CanCraft(Recipe r)
        {
            foreach (var (item, n) in r.In) if (Count(item) < n) return false;
            return true;
        }

        public bool Craft(Recipe r)
        {
            if (!CanCraft(r)) return false;
            // Make sure the result fits before using anything up.
            var copy = (Stack[])Slots.Clone();
            foreach (var (item, n) in r.In) Take(item, n);
            if (Add(r.Out, r.Count) > 0) { Array.Copy(copy, Slots, Size); return false; }
            return true;
        }
    }

    public static class Mining
    {
        // Seconds to break a block with what's in hand; float.PositiveInfinity if it can't be broken.
        public static float Time(BlockDef b, ItemDef held, bool creative)
        {
            if (creative) return 0.12f;
            if (b.Hardness < 0) return float.PositiveInfinity;
            if (b.Hardness == 0) return 0.05f;
            float t = b.Hardness * 1.5f;
            bool right = held != null && held.Tool != Tool.None && held.Tool == b.Tool;
            if (right) t /= held.Speed;
            if (!Drops(b, held)) t *= 3.3f;
            return Math.Max(0.05f, t);
        }

        public static bool Drops(BlockDef b, ItemDef held)
        {
            if (b.TierNeeded <= 0 || b.Tool != Tool.Pickaxe) return true;
            return held != null && held.Tool == Tool.Pickaxe && held.Tier >= b.TierNeeded;
        }

        // Something extra a block sometimes gives (an apple from oak leaves).
        public static string Bonus(BlockDef b, Random r) => b.Bonus != null && r.NextDouble() < b.BonusChance ? b.Bonus : null;
        public static string Bonus2(BlockDef b, Random r) => b.Bonus2 != null && r.NextDouble() < b.Bonus2Chance ? b.Bonus2 : null;

        // Bounty on a pickaxe or shovel: ores that drop something other than themselves may give more.
        public static int Bounty(BlockDef b, ItemDef held, Random r, int count)
        {
            int f = held?.Ench('f') ?? 0;
            if (f <= 0 || b.DropItem == null || b.DropItem == "" || b.Tool != Tool.Pickaxe) return count;
            return count * (1 + Math.Max(0, r.Next(f + 2) - 1));
        }

        public static int XpFor(BlockDef b, Random r) =>
            b.Id == Blocks.CoalOre ? r.Next(0, 3) : b.Id == Blocks.Cage ? r.Next(15, 40) : b.Id == Blocks.StarOre ? r.Next(3, 8) : b.Id == Blocks.SunstoneOre ? r.Next(2, 6) : b.Id == Blocks.JadeOre ? r.Next(3, 8) : b.Id == Blocks.EmberOre ? r.Next(2, 6) : b.Id == Blocks.SparkOre ? r.Next(1, 6) : 0;

        // Shears bring leaves, grass and the like away whole.
        public static (string item, int count) Drop(BlockDef b, ItemDef held)
        {
            if (held != null && held.Tool == Tool.Shears && b.DropItem == "" && (b.Sound == Sound.Leaves || b.Shape == Shape.Cross)) return (Blocks.Keys[b.BaseId], 1);
            return Drop(b);
        }

        public static (string item, int count) Drop(BlockDef b)
        {
            if (b.DropItem == "") return (null, 0);
            return (b.DropItem ?? Blocks.Keys[b.BaseId], b.DropCount);
        }
    }
}
