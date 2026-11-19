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
            // In a furnace (coal is the fuel)
            R("stone", 1, "furnace", ("cobble", 1));
            R("glass", 1, "furnace", ("sand", 1));
            R("clay_bricks", 1, "furnace", ("clay", 1));
            R("copper_ingot", 1, "furnace", ("copper_ore", 1), ("coal", 1));
            R("iron_ingot", 1, "furnace", ("iron_ore", 1), ("coal", 1));
            R("gold_ingot", 1, "furnace", ("gold_ore", 1), ("coal", 1));
            R("coal", 1, "furnace", ("@log", 1));
        }

        public static ItemDef Get(string id) => id != null && Defs.TryGetValue(id, out var d) ? d : null;

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
        public const int Hotbar = 9, Size = 36;
        public readonly Stack[] Slots = new Stack[Size];
        public int Selected;

        public Stack Held => Slots[Selected];

        public int Count(string want)
        {
            int n = 0;
            foreach (var s in Slots) if (!s.Empty && Items.Matches(want, s.Item)) n += s.Count;
            return n;
        }

        // Adds as much as fits; returns what's left over.
        public int Add(string item, int count)
        {
            var def = Items.Get(item);
            if (def == null || count <= 0) return count;
            for (int pass = 0; pass < 2 && count > 0; pass++)
                for (int i = 0; i < Size && count > 0; i++)
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
            for (int i = Size - 1; i >= 0 && count > 0; i--)
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

        // Wears the held tool; true when it broke.
        public bool WearHeld()
        {
            ref var s = ref Slots[Selected];
            var d = Items.Get(s.Item);
            if (d == null || d.Durability <= 0) return false;
            s.Wear++;
            if (s.Wear >= d.Durability) { s = Stack.None; return true; }
            return false;
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

        public static (string item, int count) Drop(BlockDef b)
        {
            if (b.DropItem == "") return (null, 0);
            return (b.DropItem ?? Blocks.Keys[b.BaseId], b.DropCount);
        }
    }
}
