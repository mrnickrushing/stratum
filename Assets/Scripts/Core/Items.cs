using System;
using System.Collections.Generic;

namespace Stratum.Core
{
    public sealed class ItemDef
    {
        public string Id;
        public string Name;
        public byte Block;           // the block it places (0 = not a block)
        public int MaxStack = 64;
        public Tool Tool;            // tools: what they're for
        public int Tier;             // tools: 1 wood, 2 stone, 3 copper, 4 iron
        public int Durability;       // tools: uses before it breaks
        public float Speed = 1;      // tools: how much faster than a hand
        public bool Creative = true;
        public float Damage = 1;     // a hit with it (a bare hand is 1)
        public int Food;             // hunger it fills when eaten (0: not food)
        public float Saturation;     // how long that keeps hunger away
        public int ArmourSlot = -1;  // 0 head, 1 body, 2 legs, 3 feet
        public int Armour;           // protection points (20 in all stops 80% of a hit)
        public bool Ranged;          // the bow
        public bool Drink;           // milk: drunk whatever your hunger, gives the bucket back
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
    }

    public static class Items
    {
        public static readonly Dictionary<string, ItemDef> Defs = new Dictionary<string, ItemDef>();
        public static readonly List<ItemDef> Order = new List<ItemDef>();
        public static readonly List<Recipe> Recipes = new List<Recipe>();

        static ItemDef Add(ItemDef d) { Defs[d.Id] = d; Order.Add(d); return d; }

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
                Add(new ItemDef { Id = Blocks.Keys[i], Name = b.Name, Block = (byte)i });
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
            foreach (var (mat, name, tier, dur, dmg) in new[] { ("wood", "Wooden", 1, 60, 4f), ("stone", "Stone", 2, 130, 5f), ("copper", "Copper", 3, 220, 6f), ("iron", "Iron", 4, 400, 7f) })
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
            foreach (var (mat, name, tier, dur) in new[] { ("wood", "Wooden", 1, 60), ("stone", "Stone", 2, 130), ("copper", "Copper", 3, 220), ("iron", "Iron", 4, 400) })
                Add(new ItemDef { Id = mat + "_hoe", Name = name + " Hoe", MaxStack = 1, Tool = Tool.Hoe, Tier = tier, Durability = dur, Speed = 1, Damage = 1 });
            Add(new ItemDef { Id = "bucket", Name = "Bucket", MaxStack = 16 });
            Add(new ItemDef { Id = "jade", Name = "Jade" });
            Add(new ItemDef { Id = "boat", Name = "Boat", MaxStack = 1 });
            Add(new ItemDef { Id = "minecart", Name = "Minecart", MaxStack = 1 });
            Add(new ItemDef { Id = "compass", Name = "Compass", MaxStack = 1 });
            Add(new ItemDef { Id = "clock", Name = "Clock", MaxStack = 1 });
            Add(new ItemDef { Id = "map", Name = "Map", MaxStack = 1 });
            Add(new ItemDef { Id = "xp", Name = "Experience", MaxStack = 1000, Creative = false });
            Add(new ItemDef { Id = "water_bucket", Name = "Water Bucket", MaxStack = 1 });
            Add(new ItemDef { Id = "lava_bucket", Name = "Lava Bucket", MaxStack = 1 });
            Add(new ItemDef { Id = "milk_bucket", Name = "Milk Bucket", MaxStack = 1, Drink = true, Food = 1, Saturation = 1 });
            Add(new ItemDef { Id = "hide", Name = "Hide" });
            Add(new ItemDef { Id = "feather", Name = "Feather" });
            Add(new ItemDef { Id = "bone", Name = "Bone" });
            Add(new ItemDef { Id = "thread", Name = "Thread" });
            Add(new ItemDef { Id = "ember", Name = "Ember Shard" });
            Add(new ItemDef { Id = "colossus_heart", Name = "Heart of Cinder", MaxStack = 16 });

            // Armour: a cap, a coat, leggings and boots of hide, copper or iron.
            string[] pieces = { "cap", "coat", "leggings", "boots" }, pieceNames = { "Cap", "Coat", "Leggings", "Boots" };
            int[] need = { 5, 8, 7, 4 };
            foreach (var (mat, name, item, pts, dur) in new[] { ("hide", "Hide", "hide", new[] { 1, 3, 2, 1 }, 80), ("copper", "Copper", "copper_ingot", new[] { 2, 5, 4, 1 }, 180), ("iron", "Iron", "iron_ingot", new[] { 2, 6, 5, 2 }, 260) })
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
            foreach (var (mat, item, n) in new[] { ("wood", "@planks", 3), ("stone", "cobble", 3), ("copper", "copper_ingot", 3), ("iron", "iron_ingot", 3) })
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
            foreach (var (mat, item) in new[] { ("wood", "@planks"), ("stone", "cobble"), ("copper", "copper_ingot"), ("iron", "iron_ingot") })
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
            R("rail", 16, "workbench", ("iron_ingot", 6), ("stick", 1));
            R("booster_rail", 6, "workbench", ("gold_ingot", 6), ("stick", 1), ("sunstone_shard", 1));
            R("jade_block", 1, "workbench", ("jade", 9));
            R("jade", 9, null, ("jade_block", 1));
            R("compass", 1, "workbench", ("iron_ingot", 4), ("sunstone_shard", 1));
            R("clock", 1, "workbench", ("gold_ingot", 4), ("sunstone_shard", 1));
            R("map", 1, "workbench", ("paper", 8), ("compass", 1));
            R("enchanting_table", 1, "workbench", ("book", 1), ("sunstone_shard", 2), ("obsidian", 4));
            R("anvil", 1, "workbench", ("iron_block", 3), ("iron_ingot", 4));
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
            R("red_wool", 1, null, ("white_wool", 1), ("red_flower", 1));
            R("yellow_wool", 1, null, ("white_wool", 1), ("yellow_flower", 1));
            R("blue_wool", 1, null, ("white_wool", 1), ("blue_flower", 1));
            R("green_wool", 1, null, ("white_wool", 1), ("cactus", 1));
            R("black_wool", 1, null, ("white_wool", 1), ("coal", 1));
            R("grey_wool", 2, null, ("white_wool", 2), ("coal", 1));
            R("brown_wool", 1, null, ("white_wool", 1), ("dirt", 1));
            // Cooking needs no fuel: a furnace is warm enough.
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
        }

        public static ItemDef Get(string id)
        {
            if (id == null) return null;
            if (Defs.TryGetValue(id, out var d)) return d;
            if (id.IndexOf('@') < 0) return null;
            lock (Derived)
            {
                if (Derived.TryGetValue(id, out d)) return d;
                d = Enchants.Derive(id);
                Derived[id] = d;
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
        public readonly Stack[] Slots = new Stack[Size];
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
            b.Id == Blocks.CoalOre ? r.Next(0, 3) : b.Id == Blocks.SunstoneOre ? r.Next(2, 6) : b.Id == Blocks.JadeOre ? r.Next(3, 8) : 0;

        public static (string item, int count) Drop(BlockDef b)
        {
            if (b.DropItem == "") return (null, 0);
            return (b.DropItem ?? Blocks.Keys[b.BaseId], b.DropCount);
        }
    }
}
