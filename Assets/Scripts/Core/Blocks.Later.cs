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
        }
    }
}
