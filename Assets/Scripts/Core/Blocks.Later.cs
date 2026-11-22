using System;
using System.Collections.Generic;

namespace Stratum.Core
{
    // Blocks added from 0.14 on. Always after everything before them, so saved worlds keep their numbers.
    public static partial class Blocks
    {
        static void Later()
        {
            // A torch on the side of a block, leaning out from the wall it faces.
            WallTorch = Facings("wall_torch", "Torch", 4, (d, f) =>
            {
                d.Shape = Shape.Torch; d.Solid = false; d.Opaque = false; d.Opacity = 0; d.Emit = 14; d.Hardness = 0; d.Cutout = true; d.Sound = Sound.Wood;
                d.OnWall = true; d.DropItem = "torch"; d.Top = d.Side = d.Bottom = Tex.Torch;
            });
            Defs[WallTorch].Creative = false; Palette.Remove(WallTorch);

            // Fire: stands on what it burns, eats wood, leaves, wool and plants, and goes out when they're gone.
            var b = Add("fire", "Fire", Tex.Fire); b.Shape = Shape.Cross; b.Solid = false; b.Opaque = false; b.Opacity = 0; b.Emit = 15; b.Hardness = 0; b.Cutout = true;
            b.Replaceable = true; b.DropItem = ""; b.Sound = Sound.Cloth; b.Sway = true; b.Creative = false; Palette.Remove(b.Id); Fire = b.Id;
            // A keg of blast powder: lit by a fire striker, fire, spark power or another blast, it goes off a few seconds later.
            b = Add("blast_keg", "Blast Keg", Tex.KegTop, Tex.KegSide, Tex.KegTop); b.Hardness = 0.6f; b.Tool = Tool.Axe; b.Sound = Sound.Wood; BlastKeg = b.Id;
            b = Add("blast_keg_lit", "Blast Keg", Tex.KegTop, Tex.KegLit, Tex.KegTop); b.Hardness = 0.6f; b.Sound = Sound.Wood; b.Emit = 10; b.DropItem = "blast_keg";
            b.Creative = false; Palette.Remove(b.Id); KegLit = b.Id;

            // Root crops: planted as themselves on farmland, four stages like wheat, several to a ripe plant.
            foreach (var (key, name, tex) in new[] { ("carrot", "Carrots", Tex.Carrot0), ("potato", "Potatoes", Tex.Potato0) })
                for (int st = 0; st < WheatStages; st++)
                {
                    b = Plant(key + "_crop_" + st, name, (Tex)((int)tex + st)); b.Replaceable = false; b.Sway = st > 0; b.Grows = 1; b.Variant = st;
                    b.Creative = false; Palette.Remove(b.Id); b.DropItem = key;
                    if (st == WheatStages - 1) { b.DropCount = 2; b.Bonus = key; b.BonusChance = 0.6f; b.Bonus2 = key; b.Bonus2Chance = 0.4f; }
                    if (st == 0) { if (key == "carrot") Carrot = b.Id; else Potato = b.Id; }
                    else b.BaseId = key == "carrot" ? Carrot : Potato;
                }

            // Starstone: the hardest thing in the ground, deep down, for the best tools.
            b = Rock("star_ore", "Starstone Ore", Tex.StarOre, 5, 4); b.DropItem = "starstone"; StarOre = b.Id;

            // What fire eats.
            for (int i = 1; i < _next; i++)
            {
                var d = Defs[i];
                if (d == null || d.Container || d.Fluid != 0 || d.Emit > 0 || i == BlastKeg) continue;
                if (d.Sound == Sound.Wood || d.Sound == Sound.Leaves || d.Sound == Sound.Cloth || d.Shape == Shape.Cross && !d.Replaceable || i == TallGrass) d.Burns = true;
            }
        }
    }
}
