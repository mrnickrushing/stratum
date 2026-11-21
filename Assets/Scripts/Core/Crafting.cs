using System;
using System.Collections.Generic;

namespace Stratum.Core
{
    // What the crafting grid makes. Tools and building pieces have a pattern (a pickaxe is three across the top
    // with a handle below; an axe is an L), so things made of the same materials can be told apart. The pattern may
    // sit anywhere in the grid and may be mirrored left to right. Everything else is made from the right things in
    // any squares.
    public static class Crafting
    {
        // Patterns by what is made. Letters stand for the recipe's ingredients in order (A the first, B the second,
        // C the third); a space is an empty square. A key starting with '*' matches the end of the item's name.
        static readonly (string key, string[] rows)[] Patterns =
        {
            ("*_pickaxe", new[] { "AAA", " B ", " B " }),
            ("*_axe", new[] { "AA", "AB", " B" }),
            ("*_shovel", new[] { "A", "B", "B" }),
            ("*_sword", new[] { "A", "A", "B" }),
            ("*_hoe", new[] { "AA", " B", " B" }),
            ("*_cap", new[] { "AAA", "A A" }),
            ("*_coat", new[] { "A A", "AAA", "AAA" }),
            ("*_leggings", new[] { "AAA", "A A", "A A" }),
            ("*_boots", new[] { "A A", "A A" }),
            ("stick", new[] { "A", "A" }),
            ("workbench", new[] { "AA", "AA" }),
            ("torch", new[] { "B", "A" }),
            ("spark_torch", new[] { "A", "B" }),
            ("lever", new[] { "B", "A" }),
            ("white_wool", new[] { "AA", "AA" }),
            ("furnace", new[] { "AAA", "A A", "AAA" }),
            ("chest", new[] { "AAA", "A A", "AAA" }),
            ("stone_bricks", new[] { "AA", "AA" }),
            ("slate_tiles", new[] { "AA", "AA" }),
            ("sandstone", new[] { "AA", "AA" }),
            ("sunstone", new[] { "AA", "AA" }),
            ("*_block", new[] { "AAA", "AAA", "AAA" }),
            ("hay_bale", new[] { "AAA", "AAA", "AAA" }),
            ("*_slab", new[] { "AAA" }),
            ("*_stairs", new[] { "A  ", "AA ", "AAA" }),
            ("*_door", new[] { "AA", "AA", "AA" }),
            ("trapdoor", new[] { "AAA", "AAA" }),
            ("*_wall", new[] { "AAA", "AAA" }),
            ("glass_pane", new[] { "AAA", "AAA" }),
            ("iron_bars", new[] { "AAA", "AAA" }),
            ("ladder", new[] { "A A", "AAA", "A A" }),
            ("*_fence", new[] { "ABA", "ABA" }),
            ("oak_fence_gate", new[] { "ABA", "ABA" }),
            ("sign", new[] { "AAA", "AAA", " B " }),
            ("bucket", new[] { "A A", " A " }),
            ("boat", new[] { "A A", "AAA" }),
            ("minecart", new[] { "A A", "AAA" }),
            ("rail", new[] { "A A", "ABA", "A A" }),
            ("booster_rail", new[] { "A A", "ABA", "ACA" }),
            ("bow", new[] { " AB", "A B", " AB" }),
            ("arrow", new[] { "C", "A", "B" }),
            ("shears", new[] { " A", "A " }),
            ("bed", new[] { "AAA", "BBB" }),
            ("bread", new[] { "AAA" }),
            ("paper", new[] { "AAA" }),
            ("pressure_plate", new[] { "AA" }),
            ("compass", new[] { " A ", "ABA", " A " }),
            ("clock", new[] { " A ", "ABA", " A " }),
            ("map", new[] { "AAA", "ABA", "AAA" }),
            ("spark_lamp", new[] { " A ", "ABA", " A " }),
            ("repeater", new[] { "BAB", "CCC" }),
            ("enchanting_table", new[] { " A ", "BCB", "CCC" }),
            ("anvil", new[] { "AAA", " B ", "BBB" }),
            ("chute", new[] { "A A", "ABA", " A " }),
            ("roof_tiles", new[] { "AA", "AA" }),
            ("polished_stone", new[] { "AA", "AA" }),
            ("bookshelf", new[] { "AAA", "BBB", "AAA" }),
            ("barrel", new[] { "ABA", "A A", "ABA" }),
            ("hearth", new[] { "AAA", "ABA", "ABA" }),
            ("lantern", new[] { " A ", "ABA", " A " }),
            ("table", new[] { "AAA", "B B" }),
            ("stool", new[] { " A ", "B B" }),
            ("carpet_red", new[] { "AA" }),
            ("carpet_blue", new[] { "AA" }),
            ("flower_pot", new[] { "ABA", " A " }),
            ("lattice_window", new[] { "ABA", "ABA" }),
            ("stone_pillar", new[] { "A", "A", "A" }),
        };

        // Gives every recipe that has one its pattern (Items calls this once its recipes are listed). A pattern
        // that doesn't use exactly the recipe's amounts is a mistake in the table above and is refused.
        public static void Assign(List<Recipe> recipes)
        {
            foreach (var r in recipes)
            {
                if (r.Station == "furnace") continue;
                foreach (var (key, rows) in Patterns)
                {
                    if (!(key[0] == '*' ? r.Out.EndsWith(key.Substring(1), StringComparison.Ordinal) : r.Out == key)) continue;
                    var counts = new int[r.In.Length]; bool ok = true;
                    foreach (var row in rows) foreach (char ch in row) if (ch != ' ') { int i = ch - 'A'; if (i < 0 || i >= counts.Length) ok = false; else counts[i]++; }
                    for (int i = 0; ok && i < counts.Length; i++) if (counts[i] != r.In[i].count) ok = false;
                    if (ok) r.Shape = rows;
                    break;
                }
            }
        }

        // The recipe a grid spells out. `grid` is three by three, row by row; `n` is how much of it is in use (2 in
        // the backpack, 3 at a workbench, which also allows workbench recipes).
        public static Recipe Match(Stack[] grid, int n)
        {
            int r0 = 3, r1 = -1, c0 = 3, c1 = -1, filled = 0;
            for (int r = 0; r < n; r++)
                for (int c = 0; c < n; c++)
                {
                    var s = grid[r * 3 + c];
                    if (s.Empty) continue;
                    if (s.Wear > 0) return null;
                    filled++; r0 = Math.Min(r0, r); r1 = Math.Max(r1, r); c0 = Math.Min(c0, c); c1 = Math.Max(c1, c);
                }
            if (filled == 0) return null;
            int h = r1 - r0 + 1, w = c1 - c0 + 1;
            Recipe loose = null, wild = null;
            foreach (var r in Items.Recipes)
            {
                if (r.Station == "furnace" || r.Station == "workbench" && n < 3) continue;
                if (r.Shape != null)
                {
                    if (r.Shape.Length != h || r.Shape[0].Length != w) continue;
                    for (int flip = 0; flip < 2; flip++)
                    {
                        bool ok = true;
                        for (int y = 0; y < h && ok; y++)
                            for (int x = 0; x < w && ok; x++)
                            {
                                char ch = r.Shape[y][flip == 0 ? x : w - 1 - x];
                                var s = grid[(r0 + y) * 3 + c0 + x];
                                ok = ch == ' ' ? s.Empty : !s.Empty && Items.Matches(r.In[ch - 'A'].item, s.Item);
                            }
                        // A recipe that names its wood (a birch door) comes before one that takes any planks.
                        if (ok) { if (!Wild(r)) return r; wild ??= r; break; }
                    }
                }
                else if (loose == null && Loose(r, grid, n, filled)) loose = r;
            }
            return wild ?? loose;
        }

        static bool Wild(Recipe r) { foreach (var (item, _) in r.In) if (item[0] == '@') return true; return false; }

        [ThreadStatic] static List<string> _have;
        [ThreadStatic] static List<bool> _used;

        // Every filled square is one of its thing, and the recipe must use them all. Named things are matched
        // before "any planks" or "any log".
        static bool Loose(Recipe r, Stack[] grid, int n, int filled)
        {
            int total = 0; foreach (var (_, k) in r.In) total += k;
            if (total != filled) return false;
            var have = _have ??= new List<string>(); var used = _used ??= new List<bool>();
            have.Clear(); used.Clear();
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) if (!grid[y * 3 + x].Empty) { have.Add(grid[y * 3 + x].Item); used.Add(false); }
            for (int pass = 0; pass < 2; pass++)
                foreach (var (item, k) in r.In)
                {
                    if (item.StartsWith("@") != (pass == 1)) continue;
                    int left = k;
                    for (int i = 0; i < have.Count && left > 0; i++)
                        if (!used[i] && Items.Matches(item, have[i])) { used[i] = true; left--; }
                    if (left > 0) return false;
                }
            return true;
        }

        // Where a recipe's things go in the grid (row by row, three wide): its pattern, or its ingredients in order.
        public static string[] Layout(Recipe r)
        {
            var cells = new string[9];
            if (r.Shape != null)
            {
                for (int y = 0; y < r.Shape.Length; y++)
                    for (int x = 0; x < r.Shape[y].Length; x++)
                        if (r.Shape[y][x] != ' ') cells[y * 3 + x] = r.In[r.Shape[y][x] - 'A'].item;
                return cells;
            }
            int total = 0; foreach (var (_, k) in r.In) total += k;
            int side = total <= 4 && r.Station == null ? 2 : 3, at = 0;
            foreach (var (item, k) in r.In)
                for (int i = 0; i < k; i++, at++) cells[at / side * 3 + at % side] = item;
            return cells;
        }

        // How big a grid a recipe needs.
        public static int Size(Recipe r)
        {
            if (r.Station == "workbench") return 3;
            if (r.Shape != null) return r.Shape.Length > 2 || r.Shape[0].Length > 2 ? 3 : 2;
            int total = 0; foreach (var (_, k) in r.In) total += k;
            return total > 4 ? 3 : 2;
        }
    }
}
