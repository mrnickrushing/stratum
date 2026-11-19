using System;
using System.Collections.Generic;
using System.Text;

namespace Stratum.Core
{
    // Enchantments ride along in the item's id: "iron_sword@s3u1" is an iron sword with Keen Edge III and
    // Lasting I. Saves, chests and the network already carry item ids, so nothing else had to change; Items.Get
    // builds (and keeps) a definition for each combination with the stronger numbers.
    public static class Enchants
    {
        public sealed class Kind
        {
            public char Code; public string Name; public int Max;
            public Func<ItemDef, bool> Fits;
        }

        static bool Weapon(ItemDef d) => d.Tool == Tool.Sword || d.Tool == Tool.Axe;
        static bool Digger(ItemDef d) => d.Tool == Tool.Pickaxe || d.Tool == Tool.Axe || d.Tool == Tool.Shovel || d.Tool == Tool.Hoe;
        static bool Wears(ItemDef d) => d.Durability > 0;

        public static readonly Kind[] All =
        {
            new Kind { Code = 's', Name = "Keen Edge", Max = 5, Fits = Weapon },
            new Kind { Code = 'e', Name = "Swiftness", Max = 5, Fits = Digger },
            new Kind { Code = 'f', Name = "Bounty", Max = 3, Fits = d => d.Tool == Tool.Pickaxe || d.Tool == Tool.Shovel },
            new Kind { Code = 'w', Name = "Draw Weight", Max = 5, Fits = d => d.Ranged },
            new Kind { Code = 'p', Name = "Warding", Max = 4, Fits = d => d.ArmourSlot >= 0 },
            new Kind { Code = 'g', Name = "Soft Landing", Max = 4, Fits = d => d.ArmourSlot == 3 },
            new Kind { Code = 'u', Name = "Lasting", Max = 3, Fits = Wears },
        };

        public static Kind Find(char c) { foreach (var k in All) if (k.Code == c) return k; return null; }

        static readonly string[] Roman = { "", "I", "II", "III", "IV", "V" };

        public static string BaseOf(string id) { int i = id?.IndexOf('@') ?? -1; return i < 0 ? id : id.Substring(0, i); }

        public static Dictionary<char, int> Parse(string id)
        {
            var d = new Dictionary<char, int>();
            int at = id?.IndexOf('@') ?? -1;
            if (at < 0) return d;
            for (int i = at + 1; i + 1 < id.Length; i += 2)
            {
                var k = Find(id[i]);
                int lvl = id[i + 1] - '0';
                if (k == null || lvl < 1) continue;
                d[k.Code] = Math.Min(k.Max, lvl);
            }
            return d;
        }

        public static string Make(string baseId, Dictionary<char, int> e)
        {
            if (e == null || e.Count == 0) return baseId;
            var sb = new StringBuilder(BaseOf(baseId)).Append('@');
            foreach (var k in All) if (e.TryGetValue(k.Code, out int l) && l > 0) sb.Append(k.Code).Append((char)('0' + Math.Min(k.Max, l)));
            return sb.ToString();
        }

        public static int Level(string id, char code) => Parse(id).TryGetValue(code, out int l) ? l : 0;

        public static string Describe(Dictionary<char, int> e)
        {
            var parts = new List<string>();
            foreach (var k in All) if (e.TryGetValue(k.Code, out int l)) parts.Add(k.Name + " " + Roman[Math.Min(5, l)]);
            return string.Join(", ", parts);
        }

        // Builds the definition of an enchanted item from its plain one.
        public static ItemDef Derive(string id)
        {
            var b = Items.Get(BaseOf(id));
            if (b == null || b.Id == id) return null;
            var e = Parse(id);
            if (e.Count == 0) return null;
            bool book = b.Id == "book";
            foreach (var c in e.Keys) if (!book && !Find(c).Fits(b)) return null;
            var d = (ItemDef)b.MemberwiseCopy();
            d.Id = id; d.Creative = false; d.Enchants = e; d.Base = b.Id;
            d.Name = (book ? "Enchanted Book" : b.Name) + " (" + Describe(e) + ")";
            if (book) d.MaxStack = 1;
            if (e.TryGetValue('s', out int s)) d.Damage = b.Damage + 1.25f * s;
            if (e.TryGetValue('e', out int ef)) d.Speed = b.Speed * (1 + 0.3f * ef);
            return d;
        }

        // What the enchanting table can put on an item for so many levels (a seeded roll, so the offer
        // shown is the one given).
        public static Dictionary<char, int> Roll(ItemDef item, int levels, int seed)
        {
            var r = new Random(seed);
            var fits = new List<Kind>();
            bool book = item.Id == "book";
            foreach (var k in All) if (book || k.Fits(item)) fits.Add(k);
            var e = new Dictionary<char, int>();
            if (fits.Count == 0) return e;
            int count = 1 + (levels >= 20 && r.NextDouble() < 0.5 ? 1 : 0) + (levels >= 30 && r.NextDouble() < 0.35 ? 1 : 0);
            for (int i = 0; i < count && fits.Count > 0; i++)
            {
                var k = fits[r.Next(fits.Count)];
                fits.Remove(k);
                float power = levels / 30f;
                int lvl = Math.Max(1, Math.Min(k.Max, (int)Math.Round(k.Max * power * (0.6 + r.NextDouble() * 0.6))));
                e[k.Code] = lvl;
            }
            return e;
        }

        public static bool CanEnchant(ItemDef d) => d != null && d.Enchants == null && (d.Id == "book" || d.Durability > 0);
    }
}
