using System.Collections.Generic;
using Stratum.Core;
using UnityEngine;

namespace Stratum.Game
{
    // The Extras page: free packs to try with one button. World packs are ready-made worlds (a kind, a seed picked for
    // its start, a way to play and a few things in the pack); outfits change how other players see you.
    public sealed class WorldPack
    {
        public string Id, Name, Tag, Desc;
        public WorldKind Kind; public int Seed; public bool Creative, Keep; public int Difficulty = 1;
        public (string item, int n)[] Kit = new (string, int)[0];
    }

    public sealed class Outfit
    {
        public string Name, Desc;
        public Color Tunic, Hood, Trim, Trousers, Boots;
    }

    // An outfit made piece by piece on the Extras page. It travels to other players as a few bytes.
    public sealed class CustomLook
    {
        public Color Tunic = new Color(0.34f, 0.46f, 0.63f), Hood = new Color(0.23f, 0.31f, 0.43f), Trim = new Color(0.82f, 0.75f, 0.59f),
                     Trousers = new Color(0.2f, 0.19f, 0.23f), Boots = new Color(0.3f, 0.2f, 0.13f), Hair = new Color(0.25f, 0.17f, 0.1f);
        public int Skin, Hat;               // skin 0: by name, 1-4 a tone; hat 0 hood, 1 none, 2 cap, 3 crown, 4 pointed hat, 5 helmet
        public bool Cape;
        public static readonly string[] HatNames = { "Hood", "No hat", "Cap", "Crown", "Pointed hat", "Helmet" };

        public byte[] Pack()
        {
            var b = new byte[22]; b[0] = 1; int i = 1;
            foreach (var c in new[] { Tunic, Hood, Trim, Trousers, Boots, Hair })
            { b[i++] = (byte)Mathf.RoundToInt(Mathf.Clamp01(c.r) * 255); b[i++] = (byte)Mathf.RoundToInt(Mathf.Clamp01(c.g) * 255); b[i++] = (byte)Mathf.RoundToInt(Mathf.Clamp01(c.b) * 255); }
            b[19] = (byte)Skin; b[20] = (byte)Hat; b[21] = (byte)(Cape ? 1 : 0);
            return b;
        }

        // Null for anything that isn't one of ours (another player's game sent it: trust nothing).
        public static CustomLook Unpack(byte[] b)
        {
            if (b == null || b.Length != 22 || b[0] != 1) return null;
            var o = new CustomLook(); int i = 1;
            Color Next() { var c = new Color(b[i] / 255f, b[i + 1] / 255f, b[i + 2] / 255f, 1); i += 3; return c; }
            o.Tunic = Next(); o.Hood = Next(); o.Trim = Next(); o.Trousers = Next(); o.Boots = Next(); o.Hair = Next();
            o.Skin = Mathf.Clamp(b[19], 0, 4); o.Hat = Mathf.Clamp(b[20], 0, HatNames.Length - 1); o.Cape = b[21] == 1;
            return o;
        }

        static CustomLook _mine;
        public static CustomLook Mine
        {
            get
            {
                if (_mine != null) return _mine;
                try { _mine = Unpack(System.Convert.FromBase64String(PlayerPrefs.GetString("outfit_made", ""))); } catch { _mine = null; }
                return _mine ??= new CustomLook();
            }
        }
        public static void SaveMine() { PlayerPrefs.SetString("outfit_made", System.Convert.ToBase64String(Mine.Pack())); PlayerPrefs.Save(); }
    }

    // A bundle: a world, a look and an outfit that go together, switched on with one button.
    public sealed class Bundle
    {
        public string Name, Desc, World; public int Look, Outfit;
    }

    public static class Extras
    {
        public static readonly WorldPack[] Worlds =
        {
            new WorldPack { Id = "castle", Name = "Castle Keep", Tag = "Adventure · Easy", Kind = WorldKind.Castle, Seed = 101, Keep = true,
                Desc = "A moated castle with towers, a great hall, a throne room and a dungeon. Hold it through the night.",
                Kit = new[] { ("iron_sword", 1), ("bow", 1), ("arrow", 32), ("bread", 8), ("torch", 16) } },
            new WorldPack { Id = "skyrun", Name = "Sky Run", Tag = "Adventure · Peaceful", Kind = WorldKind.Parkour, Seed = 102, Keep = true, Difficulty = 0,
                Desc = "A parkour run over water: six stages of jumps, each harder, up to a golden tower. Fall in and swim back.",
                Kit = new[] { ("bread", 8) } },
            new WorldPack { Id = "maze", Name = "Hedge Maze", Tag = "Adventure · Peaceful", Kind = WorldKind.Maze, Seed = 103, Keep = true, Difficulty = 0,
                Desc = "A big hedge maze with a prize in the middle. Climb the lookout first, then find your way.",
                Kit = new[] { ("bread", 6), ("compass", 1), ("torch", 8) } },
            new WorldPack { Id = "town", Name = "Market Town", Tag = "Adventure · Creative", Kind = WorldKind.Town, Seed = 104, Creative = true,
                Desc = "A furnished town with houses, an inn, a bakery, a library and a market square. Move in, or build it bigger." },
            new WorldPack { Id = "cloud", Name = "Cloud Hopper", Tag = "Sky Islands · Easy", Kind = WorldKind.Sky, Seed = 4242, Keep = true,
                Desc = "Start on a floating island with planks in your pack and bridge your way across the sky. Fall, and you keep your things.",
                Kit = new[] { ("oak_planks", 64), ("torch", 16), ("bread", 6) } },
            new WorldPack { Id = "castaway", Name = "Castaway Isles", Tag = "Archipelago · Normal", Kind = WorldKind.Islands, Seed = 17, Difficulty = 2,
                Desc = "Washed up on a small island with a boat and little else. Sail out, find a bigger island, build a harbour.",
                Kit = new[] { ("boat", 1), ("bread", 4), ("torch", 4) } },
            new WorldPack { Id = "frost", Name = "Frostbound", Tag = "Frozen Wilds · Normal", Kind = WorldKind.Frozen, Seed = 11, Difficulty = 2,
                Desc = "Snow in every direction and the sea frozen solid. Find wood, make fire, and last the first night.",
                Kit = new[] { ("torch", 8), ("fire_striker", 1), ("bread", 4) } },
            new WorldPack { Id = "sandsea", Name = "Sandsea Trek", Tag = "Endless Dunes · Normal", Kind = WorldKind.Dunes, Seed = 13, Difficulty = 2,
                Desc = "A desert crossing from river to river. Carry water, follow the compass, and watch for the night.",
                Kit = new[] { ("water_bucket", 1), ("compass", 1), ("bread", 8) } },
            new WorldPack { Id = "stair", Name = "Giants' Stair", Tag = "Towering Wilds · Easy", Kind = WorldKind.Towering, Seed = 19, Keep = true,
                Desc = "The tallest cliffs in Stratum, with arches and ledges all the way up. Climb to the snow line and build a lookout.",
                Kit = new[] { ("oak_planks", 32), ("torch", 16), ("bread", 6) } },
            new WorldPack { Id = "summit", Name = "Summit Camp", Tag = "Giant Peaks · Easy", Kind = WorldKind.Peaks, Seed = 19,
                Desc = "Mountain ranges and deep valleys. Pitch a camp, mine the ridges, and lay a rail line between the peaks.",
                Kit = new[] { ("bed", 1), ("torch", 16), ("bread", 6) } },
            new WorldPack { Id = "deepwood", Name = "The Deep Wood", Tag = "All Pinewood · Peaceful", Kind = WorldKind.OnlyPine, Seed = 43, Difficulty = 0,
                Desc = "Pine forest without end and no monsters at all. A quiet place for a cabin by the river.",
                Kit = new[] { ("bed", 1), ("torch", 8) } },
            new WorldPack { Id = "longroad", Name = "The Long Road", Tag = "Vast Lands · Normal", Kind = WorldKind.Vast, Seed = 23, Difficulty = 2,
                Desc = "Forests, deserts and seas four times as wide. A journey to the next region takes days: bring a map.",
                Kit = new[] { ("map", 1), ("compass", 1), ("bread", 8) } },
            new WorldPack { Id = "table", Name = "Builder's Table", Tag = "Flatlands · Creative", Kind = WorldKind.Flat, Seed = 7, Creative = true,
                Desc = "Perfectly flat grass, every block in the palette, and flight. Build a town, a castle or a railway." },
        };

        static Color C(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f);

        // Outfit 0 is the usual tunic, coloured by the player's name.
        public static readonly Outfit[] Outfits =
        {
            new Outfit { Name = "Traveller", Desc = "The hooded tunic everyone starts with, in your own colour.", Tunic = C(86, 118, 160), Hood = C(58, 80, 110), Trim = C(210, 190, 150), Trousers = C(51, 48, 59), Boots = C(77, 51, 33) },
            new Outfit { Name = "Forest Ranger", Desc = "Moss green and bark brown, for the woods.", Tunic = C(74, 112, 58), Hood = C(46, 74, 40), Trim = C(196, 170, 96), Trousers = C(84, 62, 42), Boots = C(52, 38, 26) },
            new Outfit { Name = "Ember Knight", Desc = "Dark iron with glowing orange trim, for the Ember Deep.", Tunic = C(64, 60, 66), Hood = C(40, 38, 44), Trim = C(255, 128, 36), Trousers = C(38, 36, 40), Boots = C(28, 26, 28) },
            new Outfit { Name = "Frost Walker", Desc = "White furs and pale blue, for the Frozen Wilds.", Tunic = C(226, 234, 242), Hood = C(170, 196, 220), Trim = C(96, 160, 220), Trousers = C(120, 132, 150), Boots = C(240, 240, 244) },
            new Outfit { Name = "Sun Mason", Desc = "Sandstone and gold, for desert builders.", Tunic = C(224, 190, 120), Hood = C(196, 150, 84), Trim = C(255, 214, 80), Trousers = C(150, 110, 70), Boots = C(110, 78, 48) },
            new Outfit { Name = "Night Scout", Desc = "Deep indigo with a silver clasp. Hard to see after dark.", Tunic = C(44, 46, 84), Hood = C(28, 30, 56), Trim = C(200, 206, 220), Trousers = C(24, 24, 36), Boots = C(18, 18, 26) },
            new Outfit { Name = "River Guard", Desc = "Teal and cream, for sailors and bridge builders.", Tunic = C(40, 140, 140), Hood = C(26, 96, 100), Trim = C(240, 228, 196), Trousers = C(44, 60, 78), Boots = C(70, 52, 36) },
            new Outfit { Name = "Harvest Hand", Desc = "Straw yellow and barn red, for farmers.", Tunic = C(176, 58, 46), Hood = C(214, 178, 84), Trim = C(244, 226, 150), Trousers = C(70, 84, 120), Boots = C(84, 56, 34) },
            new Outfit { Name = "Royal Builder", Desc = "Purple and gold, for whoever built the biggest castle.", Tunic = C(110, 58, 150), Hood = C(76, 38, 108), Trim = C(250, 204, 70), Trousers = C(44, 34, 60), Boots = C(60, 40, 30) },
            new Outfit { Name = "Castle Guard", Desc = "Steel grey and royal red, for the watch on the wall.", Tunic = C(150, 38, 40), Hood = C(132, 138, 146), Trim = C(232, 196, 84), Trousers = C(70, 72, 80), Boots = C(46, 40, 36) },
            new Outfit { Name = "Sky Runner", Desc = "Bright lime and white. Light on the feet.", Tunic = C(134, 196, 58), Hood = C(240, 240, 236), Trim = C(40, 44, 52), Trousers = C(44, 48, 56), Boots = C(232, 232, 228) },
            new Outfit { Name = "Gardener", Desc = "Leaf green with a straw hood and muddy boots.", Tunic = C(96, 150, 70), Hood = C(222, 196, 120), Trim = C(232, 96, 110), Trousers = C(110, 84, 58), Boots = C(74, 56, 40) },
            new Outfit { Name = "Merchant", Desc = "Plum and gold, with good boots for the road.", Tunic = C(120, 50, 84), Hood = C(196, 150, 60), Trim = C(248, 220, 130), Trousers = C(58, 46, 40), Boots = C(96, 64, 38) },
            new Outfit { Name = "Deep Miner", Desc = "Dusty brown with a lamp-yellow hood.", Tunic = C(122, 96, 70), Hood = C(240, 200, 60), Trim = C(60, 60, 66), Trousers = C(72, 66, 60), Boots = C(40, 36, 34) },
            new Outfit { Name = "Baker", Desc = "Flour white with a cherry trim.", Tunic = C(240, 236, 226), Hood = C(250, 248, 240), Trim = C(200, 50, 60), Trousers = C(150, 150, 156), Boots = C(110, 84, 60) },
            new Outfit { Name = "Sea Captain", Desc = "Navy blue and brass buttons.", Tunic = C(36, 52, 96), Hood = C(24, 34, 64), Trim = C(214, 170, 70), Trousers = C(236, 232, 220), Boots = C(30, 26, 24) },
            new Outfit { Name = "Sunset", Desc = "Orange into pink, like the evening sky.", Tunic = C(240, 130, 60), Hood = C(232, 96, 130), Trim = C(255, 226, 150), Trousers = C(110, 60, 110), Boots = C(70, 40, 70) },
        };

        public static readonly Bundle[] Bundles =
        {
            new Bundle { Name = "A Knight's Tale", World = "castle", Look = 2, Outfit = 9, Desc = "Castle Keep in the Storybook look, dressed as a Castle Guard." },
            new Bundle { Name = "Retro Run", World = "skyrun", Look = 1, Outfit = 10, Desc = "Sky Run in the Chunky look, dressed as a Sky Runner." },
            new Bundle { Name = "Garden Party", World = "maze", Look = 3, Outfit = 11, Desc = "The Hedge Maze in the Pastel look, dressed as a Gardener." },
            new Bundle { Name = "Comic Town", World = "town", Look = 4, Outfit = 12, Desc = "Market Town in the Inked look, dressed as a Merchant." },
            new Bundle { Name = "Above the Clouds", World = "cloud", Look = 3, Outfit = 3, Desc = "Cloud Hopper in the Pastel look, dressed as a Frost Walker." },
            new Bundle { Name = "The Usual", World = null, Look = 0, Outfit = 0, Desc = "Back to how Stratum normally looks: the usual blocks and the Traveller's tunic." },
        };

        public const int Custom = 255;      // the outfit made piece by piece (CustomLook.Mine)

        public static int Outfit
        {
            get { int v = PlayerPrefs.GetInt("outfit", 0); return v == Custom ? Custom : Mathf.Clamp(v, 0, Outfits.Length - 1); }
            set { PlayerPrefs.SetInt("outfit", value == Custom ? Custom : Mathf.Clamp(value, 0, Outfits.Length - 1)); PlayerPrefs.Save(); }
        }
    }
}
