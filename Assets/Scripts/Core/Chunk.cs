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
        public readonly byte[] Blocks = new byte[Volume];
        public readonly byte[] Tint = new byte[Size * Size * 4];   // per column: grass rgb, foliage brightness
        public readonly byte[] Biome = new byte[Size * Size];
        public int MaxY;                                // highest non-air block
        public bool Modified;                           // changed by the player since it was made
        public bool Unsaved;                            // changed since it was last written to disk
        public volatile bool Ready;                     // blocks are filled in

        public Chunk(int x, int z) { X = x; Z = z; }

        public static int Index(int x, int y, int z) => x | (z << 4) | (y << 8);

        public byte Get(int x, int y, int z) => (uint)y < Height ? Blocks[x | (z << 4) | (y << 8)] : (byte)0;

        public void Set(int x, int y, int z, byte id)
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

        public void Write(Stream s)
        {
            using var z = new DeflateStream(s, CompressionLevel.Fastest, true);
            z.Write(Blocks, 0, Blocks.Length);
        }

        public void Read(Stream s)
        {
            using var z = new DeflateStream(s, CompressionMode.Decompress, true);
            int off = 0;
            while (off < Blocks.Length)
            {
                int n = z.Read(Blocks, off, Blocks.Length - off);
                if (n <= 0) throw new EndOfStreamException("chunk data cut short");
                off += n;
            }
            RecalcMaxY();
        }

        public static long Key(int x, int z) => ((long)x << 32) | (uint)z;
    }
}
