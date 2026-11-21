using System;
using System.IO;
using System.IO.Compression;

namespace Stratum.Core
{
    // One 16 x 256 x 16 column of the world.
    public sealed class Chunk
    {
        public const int Size = 16, Height = 256, Volume = Size * Size * Height;
        public const int SeaLevel = 63;

        public readonly int X, Z;                       // chunk coordinates (block = chunk * 16 + local)
        public readonly ushort[] Blocks = new ushort[Volume];       // block numbers (two bytes each since 0.11: room for thousands)
        public readonly byte[] Tint = new byte[Size * Size * 4];   // per column: grass rgb, foliage brightness
        public readonly byte[] Biome = new byte[Size * Size];
        public int MaxY;                                // highest non-air block
        public bool Modified;                           // changed by the player since it was made
        public bool Unsaved;                            // changed since it was last written to disk
        public volatile bool Ready;                     // blocks are filled in

        public Chunk(int x, int z) { X = x; Z = z; }

        public static int Index(int x, int y, int z) => x | (z << 4) | (y << 8);

        public ushort Get(int x, int y, int z) => (uint)y < Height ? Blocks[x | (z << 4) | (y << 8)] : (ushort)0;

        public void Set(int x, int y, int z, ushort id)
        {
            if ((uint)y >= Height) return;
            Blocks[x | (z << 4) | (y << 8)] = id;
            if (id != 0 && y > MaxY) MaxY = y;
        }

        public void RecalcMaxY()
        {
            for (int y = Height - 1; y >= 0; y--)
            {
                int b = y << 8;
                for (int i = 0; i < 256; i++) if (Blocks[b + i] != 0) { MaxY = y; return; }
            }
            MaxY = 0;
        }

// On disk: the blocks, two bytes each, compressed. Files written before 0.11 hold one byte a block and are
        // read as they are (told apart by their length).
        public void Write(Stream s) { var raw = Net.Compress(Blocks); s.Write(raw, 0, raw.Length); }

        public void Read(Stream s)
        {
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            Net.Decompress(ms.ToArray(), Blocks);
            RecalcMaxY();
        }

        public static long Key(int x, int z) => ((long)x << 32) | (uint)z;
    }
}
