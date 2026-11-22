using System;

namespace Stratum.Core
{
    // The Quiet: pale islands hanging in a dark, starlit emptiness, where the Hollow King waits. Like the Ember
    // Deep it is a strip of the same world, far to the west (x -64000 to -56000), so saving and playing together
    // need nothing new. A gate stone carries whoever touches it to the landing on the great island, and the gate
    // stone there carries them home.
    public static class Quiet
    {
        public const int X0 = -64000, X1 = -56000, Mid = -60000;
        public const int Top = 80, Great = 62;           // the great island: its flat top and how far it reaches
        public const int IsleCell = 72;

        public static bool In(float x) => x >= X0 && x < X1;
        public static bool InChunk(int cx) => cx * 16 >= X0 && cx * 16 < X1;

        // Where travellers land (on the great island's south side), and the gate stone beside it that takes them back.
        public static (float x, float y, float z) Landing => (Mid + 0.5f, Top + 1, 46.5f);
        public static (int x, int y, int z) HomeGate => (Mid, Top + 1, 50);
        public static (int x, int y, int z) Throne => (Mid, Top + 1, 0);

        static int FloorDiv(int a, int b) => (a >= 0 ? a : a - b + 1) / b;

        public static void Generate(Chunk ch, int seed)
        {
            int bx = ch.X * 16, bz = ch.Z * 16;
            var b = ch.Blocks;
            void Put(int x, int y, int z, ushort id)
            {
                int lx = x - bx, lz = z - bz;
                if ((uint)lx >= 16 || (uint)lz >= 16 || y < 1 || y >= Chunk.Height) return;
                b[lx | (lz << 4) | (y << 8)] = id;
            }
            // An island: flat on top, hanging down to a ragged point underneath.
            void Isle(int cx, int cz, int top, int r, int depth, int salt)
            {
                if (cx + r < bx || cx - r > bx + 15 || cz + r < bz || cz - r > bz + 15) return;
                for (int z = Math.Max(bz, cz - r); z <= Math.Min(bz + 15, cz + r); z++)
                    for (int x = Math.Max(bx, cx - r); x <= Math.Min(bx + 15, cx + r); x++)
                    {
                        float d = (float)Math.Sqrt((x - cx) * (x - cx) + (z - cz) * (z - cz)) / r;
                        float edge = 1 + (Noise.Hash01(x >> 2, salt, z >> 2, seed) - 0.5f) * 0.18f;
                        if (d > edge) continue;
                        float t = 1 - d / edge;
                        int down = 1 + (int)(depth * t * (0.6f + 0.4f * t) + Noise.Hash01(x, salt + 1, z, seed) * 2.5f);
                        for (int y = top - down; y <= top; y++) Put(x, y, z, Blocks.Stillstone);
                    }
            }

            Isle(Mid, 0, Top, Great, 26, 11);
            // Six dark spires round the throne, each with a light at its head.
            for (int i = 0; i < 6; i++)
            {
                double a = i * Math.PI / 3 + 0.5;
                int sx = Mid + (int)Math.Round(Math.Cos(a) * 30), sz = (int)Math.Round(Math.Sin(a) * 30);
                int h = 12 + (int)(Noise.Hash(i, 5, 9, seed) % 12);
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                        for (int y = Top + 1; y <= Top + h; y++) Put(sx + dx, y, sz + dz, Blocks.Obsidian);
                Put(sx, Top + h + 1, sz, Blocks.Sunstone);
            }
            // The throne dais, the landing and the way home.
            for (int dz = -3; dz <= 3; dz++) for (int dx = -3; dx <= 3; dx++) Put(Mid + dx, Top, dz, Blocks.Obsidian);
            var (gx, gy, gz) = HomeGate;
            for (int dz = -2; dz <= 2; dz++) for (int dx = -2; dx <= 2; dx++) Put(gx + dx, Top, gz - 2 + dz, Blocks.Obsidian);
            Put(gx, gy, gz, Blocks.GateStone);
            Put(gx - 2, gy, gz, Blocks.Sunstone); Put(gx + 2, gy, gz, Blocks.Sunstone);

            // Smaller islands scattered all about.
            for (int cz = FloorDiv(bz - 30, IsleCell); cz <= FloorDiv(bz + 46, IsleCell); cz++)
                for (int cx = FloorDiv(bx - 30, IsleCell); cx <= FloorDiv(bx + 46, IsleCell); cx++)
                {
                    uint h = Noise.Hash(cx, 41, cz, seed);
                    if ((h & 3) == 0) continue;
                    int ix = cx * IsleCell + 14 + (int)((h >> 4) % 44), iz = cz * IsleCell + 14 + (int)((h >> 12) % 44);
                    int r = 7 + (int)((h >> 20) % 15);
                    long dx = ix - Mid, dz = iz;
                    if (dx * dx + dz * dz < (Great + r + 14) * (Great + r + 14)) continue;      // clear air round the great island
                    if (ix - r <= X0 + 2 || ix + r >= X1 - 2) continue;
                    Isle(ix, iz, 58 + (int)((h >> 26) % 40), r, 6 + r / 2, 20 + (int)(h & 0xFF));
                }
            ch.RecalcMaxY();
        }
    }
}
