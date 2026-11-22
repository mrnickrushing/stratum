using System;
using System.Collections.Generic;

namespace Stratum.Core
{
    // Places under the ground with something to find: vaults (a mossy stone room with a cage that monsters come out
    // of and two dusty chests) and old workings (propped tunnels with the odd chest left behind). Where they are
    // follows from the seed alone, so every chunk agrees. A dusty chest fills itself the first time it is opened.
    public static class Ruins
    {
        public const int VaultCell = 80, WorksCell = 208;

        static int FloorDiv(int a, int b) => (a >= 0 ? a : a - b + 1) / b;

        public static bool Vault(int seed, int cx, int cz, out int x, out int y, out int z)
        {
            uint h = Noise.Hash(cx, 301, cz, seed);
            x = cx * VaultCell + 12 + (int)((h >> 8) % 56); z = cz * VaultCell + 12 + (int)((h >> 16) % 56); y = 12 + (int)((h >> 24) % 22);
            return (h & 0xFF) < 140;
        }

        public static bool Works(int seed, int cx, int cz, out int x, out int y, out int z)
        {
            uint h = Noise.Hash(cx, 777, cz, seed);
            x = cx * WorksCell + 60 + (int)((h >> 8) % 88); z = cz * WorksCell + 60 + (int)((h >> 16) % 88); y = 16 + (int)((h >> 24) % 20);
            return (h & 0xFF) < 115;
        }

        // The monster cages within reach of a spot (a vault's cage stands in the middle of its floor).
        public static void CagesNear(int seed, float px, float pz, float r, List<(int x, int y, int z)> into)
        {
            int cx0 = FloorDiv((int)(px - r), VaultCell), cx1 = FloorDiv((int)(px + r), VaultCell), cz0 = FloorDiv((int)(pz - r), VaultCell), cz1 = FloorDiv((int)(pz + r), VaultCell);
            for (int cz = cz0; cz <= cz1; cz++)
                for (int cx = cx0; cx <= cx1; cx++)
                    if (Vault(seed, cx, cz, out int x, out int y, out int z) && Math.Abs(x + 0.5f - px) <= r && Math.Abs(z + 0.5f - pz) <= r) into.Add((x, y, z));
        }

        // Which creature a cage breeds.
        public static MobKind CageKind(int seed, int x, int y, int z)
        {
            uint h = Noise.Hash(x, y, z, seed ^ 0x5CA6E) % 10;
            return h < 5 ? MobKind.Ghoul : h < 8 ? MobKind.BoneArcher : MobKind.Crawler;
        }

        public static void Stamp(Chunk ch, int seed)
        {
            int bx = ch.X * 16, bz = ch.Z * 16;
            void Put(int x, int y, int z, ushort id)
            {
                int lx = x - bx, lz = z - bz;
                if ((uint)lx >= 16 || (uint)lz >= 16 || y < 2 || y >= Chunk.Height) return;
                ch.Blocks[lx | (lz << 4) | (y << 8)] = id;
            }
            ushort Get(int x, int y, int z)
            {
                int lx = x - bx, lz = z - bz;
                if ((uint)lx >= 16 || (uint)lz >= 16 || (uint)y >= Chunk.Height) return Blocks.Stone;
                return ch.Blocks[lx | (lz << 4) | (y << 8)];
            }

            for (int cz = FloorDiv(bz - 6, VaultCell); cz <= FloorDiv(bz + 22, VaultCell); cz++)
                for (int cx = FloorDiv(bx - 6, VaultCell); cx <= FloorDiv(bx + 22, VaultCell); cx++)
                {
                    if (!Vault(seed, cx, cz, out int x, out int y, out int z)) continue;
                    if (x + 5 < bx || x - 5 > bx + 15 || z + 5 < bz || z - 5 > bz + 15) continue;
                    for (int dy = -1; dy <= 5; dy++)
                        for (int dz = -4; dz <= 4; dz++)
                            for (int dx = -4; dx <= 4; dx++)
                            {
                                bool wall = Math.Abs(dx) == 4 || Math.Abs(dz) == 4 || dy == -1 || dy == 5;
                                uint h = Noise.Hash(x + dx, y + dy, z + dz, seed ^ 0x33);
                                Put(x + dx, y + dy, z + dz, !wall ? (ushort)0 : h % 3 == 0 || dy == -1 && h % 2 == 0 ? Blocks.MossyCobble : Blocks.Cobble);
                            }
                    Put(x, y, z, Blocks.Cage);
                    Put(x - 3, y, z, Blocks.LootChest); Put(x + 3, y, z + 1, Blocks.LootChest);
                }

            for (int cz = FloorDiv(bz - 48, WorksCell); cz <= FloorDiv(bz + 64, WorksCell); cz++)
                for (int cx = FloorDiv(bx - 48, WorksCell); cx <= FloorDiv(bx + 64, WorksCell); cx++)
                {
                    if (!Works(seed, cx, cz, out int x, out int y, out int z)) continue;
                    if (x + 46 < bx || x - 46 > bx + 15 || z + 46 < bz || z - 46 > bz + 15) continue;
                    uint hh = Noise.Hash(cx, 778, cz, seed);
                    for (int arm = 0; arm < 4; arm++)
                    {
                        int ax = arm == 0 ? 1 : arm == 1 ? -1 : 0, az = arm == 2 ? 1 : arm == 3 ? -1 : 0;
                        int len = 18 + (int)((hh >> (arm * 5)) % 24);
                        for (int s = 0; s <= len; s++)
                        {
                            int mx = x + ax * s, mz = z + az * s;
                            if (mx + 1 < bx || mx - 1 > bx + 15 || mz + 1 < bz || mz - 1 > bz + 15) continue;
                            for (int p = -1; p <= 1; p++)
                            {
                                int wx = mx + az * p, wz = mz + ax * p;
                                for (int dy = 0; dy <= 2; dy++) Put(wx, y + dy, wz, 0);
                                var under = Blocks.Get(Get(wx, y - 1, wz));
                                if (!under.Solid) Put(wx, y - 1, wz, Blocks.OakPlanks);                // a plank bridge over a cave or water
                            }
                            if (s % 4 == 2)
                            {
                                // A prop: a post each side and a beam across.
                                for (int p = -1; p <= 1; p += 2) { Put(mx + az * p, y, mz + ax * p, Blocks.OakFence); Put(mx + az * p, y + 1, mz + ax * p, Blocks.OakFence); }
                                for (int p = -1; p <= 1; p++) Put(mx + az * p, y + 2, mz + ax * p, Blocks.OakPlanks);
                            }
                            else if (s > 5 && Noise.Hash(mx, y, mz, seed ^ 0x77) % 17 == 0) Put(mx + az, y, mz + ax, Blocks.LootChest);
                        }
                    }
                }
        }

        static readonly (string item, int min, int max, int weight)[] Loot =
        {
            ("bread", 1, 3, 12), ("coal", 2, 6, 12), ("iron_ingot", 1, 4, 10), ("bone", 1, 4, 8), ("thread", 1, 4, 8), ("arrow", 3, 9, 8), ("seeds", 2, 5, 6),
            ("carrot", 1, 3, 5), ("potato", 1, 3, 5), ("blast_powder", 1, 4, 6), ("gold_ingot", 1, 3, 6), ("jade", 1, 3, 4), ("bucket", 1, 1, 3), ("ember", 1, 2, 3),
            ("sunstone_shard", 1, 3, 4), ("bottle", 1, 3, 3), ("starstone", 1, 2, 2), ("book", 1, 2, 4), ("copper_ingot", 2, 6, 8), ("rail", 4, 10, 4), ("torch", 2, 8, 8),
        };

        // What a dusty chest at this spot holds: five to eight stacks scattered through its slots.
        public static Stack[] Roll(int seed, int x, int y, int z)
        {
            var slots = new Stack[Containers.ChestSlots];
            uint s = Noise.Hash(x, y, z, seed ^ 0x100715);
            uint Next() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return s; }
            int total = 0; foreach (var l in Loot) total += l.weight;
            int n = 5 + (int)(Next() % 4);
            for (int i = 0; i < n; i++)
            {
                int roll = (int)(Next() % (uint)total); var pick = Loot[0];
                foreach (var l in Loot) { if (roll < l.weight) { pick = l; break; } roll -= l.weight; }
                if (Items.Get(pick.item) == null) continue;
                int slot = (int)(Next() % (uint)slots.Length);
                if (!slots[slot].Empty) continue;
                slots[slot] = new Stack(pick.item, pick.min + (int)(Next() % (uint)(pick.max - pick.min + 1)));
            }
            return slots;
        }
    }
}
