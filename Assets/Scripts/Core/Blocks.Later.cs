using System;
using System.Collections.Generic;

namespace Stratum.Core
{
    // Blocks added from 0.14 on. Always after everything before them, so saved worlds keep their numbers.
    public static partial class Blocks
    {
        // A machine that can face six ways: twelve blocks, direction * 2 + on.
        static ushort SixWay(string key, string name, Tex face, Tex side, Tex back, Action<BlockDef, int, bool> more)
        {
            ushort first = 0;
            for (int i = 0; i < 12; i++)
            {
                int dir = i / 2; bool on = (i & 1) == 1;
                var d = Add(i == 0 ? key : key + "_" + i, name, side);
                if (i == 0) first = d.Id; else { d.Creative = false; Palette.Remove(d.Id); d.DropItem = key; }
                d.BaseId = first; d.Variant = i; d.Dir = dir; d.Hardness = 2; d.Tool = Tool.Pickaxe; d.Sound = Sound.Stone;
                if (dir < 4) { d.Facing = (ushort)dir; d.Front = face; d.HasFront = true; d.Top = d.Bottom = side; }
                else if (dir == 4) { d.Top = face; d.Bottom = back; }
                else { d.Bottom = face; d.Top = back; }
                more(d, dir, on);
            }
            return first;
        }

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

            // ---- spark machines
            Piston = SixWay("piston", "Piston", Tex.PistonFace, Tex.PistonSide, Tex.PistonBack, (d, dir, on) => { d.Machine = true; d.Piston = true; });
            StickyPiston = SixWay("sticky_piston", "Sticky Piston", Tex.PistonSticky, Tex.PistonSide, Tex.PistonBack, (d, dir, on) => { d.Machine = true; d.Piston = true; d.Sticky = true; });
            for (int dir = 0; dir < 6; dir++)
            {
                b = Add(dir == 0 ? "piston_arm" : "piston_arm_" + dir, "Piston Arm", Tex.PistonFace, Tex.PistonSide, Tex.PistonFace);
                b.Shape = Shape.Boxes; b.Opaque = false; b.Opacity = 0; b.Hardness = 2; b.Sound = Sound.Wood; b.DropItem = ""; b.Creative = false; Palette.Remove(b.Id);
                b.Arm = true; b.Dir = dir; b.Variant = dir;
                // A plate at the far end, a rod back to the body.
                b.Boxes = dir switch
                {
                    0 => new[] { Box.Px(0, 0, 12, 16, 16, 16), Box.Px(6, 6, 0, 10, 10, 12) },
                    1 => new[] { Box.Px(12, 0, 0, 16, 16, 16), Box.Px(0, 6, 6, 12, 10, 10) },
                    2 => new[] { Box.Px(0, 0, 0, 16, 16, 4), Box.Px(6, 6, 4, 10, 10, 16) },
                    3 => new[] { Box.Px(0, 0, 0, 4, 16, 16), Box.Px(4, 6, 6, 16, 10, 10) },
                    4 => new[] { Box.Px(0, 12, 0, 16, 16, 16), Box.Px(6, 0, 6, 10, 12, 10) },
                    _ => new[] { Box.Px(0, 0, 0, 16, 4, 16), Box.Px(6, 4, 6, 10, 16, 10) },
                };
                if (dir == 0) PistonArm = b.Id; else b.BaseId = PistonArm;
            }
            Dispenser = SixWay("dispenser", "Dispenser", Tex.DispenserFace, Tex.FurnaceSide, Tex.FurnaceTop, (d, dir, on) => { d.Machine = true; d.Container = true; d.Hardness = 3; });
            Observer = SixWay("observer", "Observer", Tex.ObserverFace, Tex.ObserverSide, Tex.ObserverBack, (d, dir, on) => { d.Watcher = true; d.Emitter = true; });
            for (int i = 0; i < 8; i++)
            {
                b = Add(i == 0 ? "comparator" : "comparator_" + i, "Comparator", Tex.Stone);
                if (i != 0) { b.Creative = false; Palette.Remove(b.Id); b.DropItem = "comparator"; }
                b.Variant = i; b.Dir = i / 2; b.Facing = (ushort)(i / 2); b.Shape = Shape.Boxes; b.Solid = false; b.Opaque = false; b.Opacity = 0; b.Hardness = 0; b.Sound = Sound.Stone;
                b.Top = (i & 1) == 1 ? Tex.ComparatorOn : Tex.ComparatorOff; b.Side = b.Bottom = Tex.Stone; b.FlatIcon = true;
                b.Boxes = new[] { Box.Px(0, 0, 0, 16, 2, 16), Box.Px(6, 2, 2, 10, 5, 5).Turn(i / 2), Box.Px(3, 2, 10, 6, 6, 13).Turn(i / 2), Box.Px(10, 2, 10, 13, 6, 13).Turn(i / 2) };
                b.Gauge = true; b.Emitter = true; if ((i & 1) == 1) b.Emit = 4;
                if (i == 0) Comparator = b.Id; else b.BaseId = Comparator;
            }

            // A brewing stand: potions are mixed beside it.
            b = Add("brewing_stand", "Brewing Stand", Tex.AnvilSide, Tex.AnvilSide, Tex.Stone);
            b.Shape = Shape.Boxes; b.Opaque = false; b.Opacity = 0; b.Emit = 3; b.Hardness = 1; b.Tool = Tool.Pickaxe; b.Sound = Sound.Stone;
            b.Boxes = new[] { Box.Px(1, 0, 1, 15, 2, 15), Box.Px(7, 2, 7, 9, 14, 9), Box.Px(2, 2, 6, 6, 8, 10), Box.Px(10, 2, 6, 14, 8, 10), Box.Px(6, 2, 10, 10, 8, 14) };
            BrewStand = b.Id;

            // A cage that monsters come out of while someone is near (found in vaults), and a dusty chest that
            // fills itself the first time it is opened.
            b = Add("cage", "Monster Cage", Tex.CageSide); b.Opaque = false; b.Opacity = 1; b.Cutout = true; b.Hardness = 5; b.Tool = Tool.Pickaxe; b.Sound = Sound.Metal; b.DropItem = "";
            b.Creative = false; Palette.Remove(b.Id); Cage = b.Id;
            b = Add("dusty_chest", "Dusty Chest", Tex.DustyTop, Tex.DustySide, Tex.DustyTop); b.Shape = Shape.Boxes; b.Opaque = false; b.Opacity = 1; b.Hardness = 2.5f; b.Tool = Tool.Axe; b.Sound = Sound.Wood;
            b.Boxes = new[] { Box.Px(1, 0, 1, 15, 14, 15) }; b.DropItem = "chest"; b.Creative = false; Palette.Remove(b.Id); LootChest = b.Id;

            // The Quiet's pale stone, and the gate stone that carries whoever touches it there and back.
            b = Rock("stillstone", "Stillstone", Tex.Stillstone, 2.5f, 1); Stillstone = b.Id;
            b = Add("gate_stone", "Gate Stone", Tex.GateStone); b.Hardness = 6; b.Tool = Tool.Pickaxe; b.TierNeeded = 4; b.Sound = Sound.Stone; b.Emit = 9; GateStone = b.Id;

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
