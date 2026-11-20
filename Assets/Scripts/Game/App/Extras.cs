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

    public static class Extras
    {
        public static readonly WorldPack[] Worlds =
        {
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
        };

        public static int Outfit
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt("outfit", 0), 0, Outfits.Length - 1);
            set { PlayerPrefs.SetInt("outfit", Mathf.Clamp(value, 0, Outfits.Length - 1)); PlayerPrefs.Save(); }
        }
    }
}
