using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Stratum.Core
{
    // The multiplayer protocol, shared by the game and the dedicated server (Server/). Every message travels over
    // one TCP connection as [u32 length][u8 type][payload], little-endian; strings are UTF-8 with a u16 length.
    // The server owns the world: it sends whole chunks, applies block edits and tells everyone about them. Each
    // player moves themselves and keeps their own backpack, which the server saves for them.
    public static class Net
    {
        public const int Protocol = 5;                  // 2: accounts, voice chat. 3: survival, villages, vehicles, spark power, the Ember Deep
        public const int DefaultPort = 25600;
        public const int MaxMessage = 1 << 20;          // a chunk is ~4-12 KB compressed; nothing should come near
        public const int MaxNameLength = 16;
        public const float Reach = 9;                   // how far from a player the server accepts their edits

        public enum Msg : byte
        {
            // client -> server
            Hello = 1, Move = 2, WantChunks = 3, SetBlock = 4, Chat = 5, Keep = 6, Ping = 7, Voice = 8,
            Strike = 9, Shoot = 10, UseMob = 11, OpenBox = 12, BoxSlot = 13, CloseBox = 14, Sleep = 15, Drop = 16, SignText = 17, Fertilize = 18, Mount = 19, Park = 20, Portal = 21,
            // server -> client
            Welcome = 64, Refuse = 65, Chunk = 66, Block = 67, Joined = 68, Left = 69, Moved = 70, Clock = 71, Said = 72, Pong = 73, Heard = 74,
            Mobs = 75, Hurt = 76, Give = 77, Box = 78, BoxSet = 79, Sleepers = 80, MobSound = 81, Sign = 82, Mounted = 83, Arrived = 84,
            TakeBack = 85,       // 0.7.3: things the player took that weren't there any more (item, count)
        }

        // Player flags in Move/Moved.
        public const byte Sneaking = 1, Flying = 2, Swinging = 4, InWater = 8, Dead = 16, Asleep = 32, InBoat = 64, InCart = 128;

        // Creature sounds in MobSound.
        // Stacks a player may have beyond their pack's 40 slots (held by the cursor, waiting for room, set aside by a
        // load): as many as fit in one list of stacks, whose count is one byte.
        public const int MaxExtraStacks = 255 - 40;
        public static readonly string[] MobSounds = { "hurt", "die", "say", "shoot", "stick" };

        public static bool ValidName(string n)
        {
            if (string.IsNullOrWhiteSpace(n) || n.Length > MaxNameLength) return false;
            foreach (char c in n) if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == ' ')) return false;
            return n.Trim() == n;
        }

// A chunk's blocks packed for sending or saving: two bytes a block (little-endian), deflated.
        public static byte[] Compress(ushort[] blocks)
        {
            var raw = new byte[blocks.Length * 2];
            Buffer.BlockCopy(blocks, 0, raw, 0, raw.Length);
            if (!BitConverter.IsLittleEndian) for (int i = 0; i < raw.Length; i += 2) { byte t = raw[i]; raw[i] = raw[i + 1]; raw[i + 1] = t; }
            using var ms = new MemoryStream(8192);
            using (var z = new DeflateStream(ms, CompressionLevel.Fastest, true)) z.Write(raw, 0, raw.Length);
            return ms.ToArray();
        }

        // Unpacks blocks packed by Compress, or by a game before 0.11 (one byte a block: half the length).
        public static void Decompress(byte[] data, ushort[] into)
        {
            using var ms = new MemoryStream(data);
            using var z = new DeflateStream(ms, CompressionMode.Decompress);
            var raw = new byte[into.Length * 2];
            int off = 0;
            while (off < raw.Length)
            {
                int n = z.Read(raw, off, raw.Length - off);
                if (n <= 0) break;
                off += n;
            }
            if (off == raw.Length)
            {
                if (!BitConverter.IsLittleEndian) for (int i = 0; i < raw.Length; i += 2) { byte t = raw[i]; raw[i] = raw[i + 1]; raw[i + 1] = t; }
                Buffer.BlockCopy(raw, 0, into, 0, raw.Length);
            }
            else if (off == into.Length) for (int i = 0; i < into.Length; i++) into[i] = raw[i];       // the old one-byte form
            else throw new EndOfStreamException("chunk data cut short");
        }
    }

    // Builds one framed message.
    public sealed class Packet
    {
        readonly MemoryStream _ms = new MemoryStream(64);
        readonly BinaryWriter _w;

        public Packet(Net.Msg type)
        {
            _w = new BinaryWriter(_ms, Encoding.UTF8, true);
            _w.Write(0u);               // length, filled in by Bytes()
            _w.Write((byte)type);
        }

        public Packet U8(byte v) { _w.Write(v); return this; }
        public Packet Bool(bool v) { _w.Write(v ? (byte)1 : (byte)0); return this; }
        public Packet I32(int v) { _w.Write(v); return this; }
        public Packet U16(int v) { _w.Write((ushort)v); return this; }
        public Packet F32(float v) { _w.Write(v); return this; }
        public Packet F64(double v) { _w.Write(v); return this; }
        public Packet Str(string s)
        {
            var b = Encoding.UTF8.GetBytes(s ?? "");
            if (b.Length > 0xFFFF) throw new ArgumentException("string too long");
            _w.Write((ushort)b.Length); _w.Write(b);
            return this;
        }
        public Packet Blob(byte[] b) { _w.Write(b.Length); _w.Write(b); return this; }

        public Packet Slot(Stack s)
        {
            Str(s.Empty ? "" : s.Item);
            _w.Write((ushort)(s.Empty ? 0 : Math.Min(s.Count, 0xFFFF)));
            _w.Write((ushort)Math.Clamp(s.Wear, 0, 0xFFFF));
            return this;
        }

        public Packet Stacks(Stack[] slots)
        {
            // The count travels in one byte: never more than 255, or the reader would lose its place.
            int n = Math.Min(slots.Length, 255);
            _w.Write((byte)n);
            for (int i = 0; i < n; i++)
            {
                var s = slots[i];
                Str(s.Empty ? "" : s.Item);
                _w.Write((ushort)(s.Empty ? 0 : Math.Min(s.Count, 0xFFFF)));
                _w.Write((ushort)Math.Clamp(s.Wear, 0, 0xFFFF));
            }
            return this;
        }

        public byte[] Bytes()
        {
            _w.Flush();
            var a = _ms.ToArray();
            BitConverter.TryWriteBytes(new Span<byte>(a, 0, 4), (uint)(a.Length - 4));
            if (!BitConverter.IsLittleEndian) Array.Reverse(a, 0, 4);
            return a;
        }
    }

    // Reads one message's payload (after the type byte).
    public sealed class Reader
    {
        readonly BinaryReader _r;
        public readonly Net.Msg Type;

        public Reader(byte[] frame)
        {
            _r = new BinaryReader(new MemoryStream(frame), Encoding.UTF8);
            Type = (Net.Msg)_r.ReadByte();
        }

        public bool More => _r.BaseStream.Position < _r.BaseStream.Length;      // fields added later in a message
        public byte U8() => _r.ReadByte();
        public bool Bool() => _r.ReadByte() != 0;
        public int I32() => _r.ReadInt32();
        public int U16() => _r.ReadUInt16();
        public float F32() { float f = _r.ReadSingle(); return float.IsFinite(f) ? f : 0; }
        public double F64() { double d = _r.ReadDouble(); return double.IsFinite(d) ? d : 0; }
        public string Str() { int n = _r.ReadUInt16(); return Encoding.UTF8.GetString(_r.ReadBytes(n)); }
        public byte[] Blob()
        {
            int n = _r.ReadInt32();
            if (n < 0 || n > Net.MaxMessage) throw new InvalidDataException("blob too big");
            return _r.ReadBytes(n);
        }

        public Stack Slot()
        {
            string item = Str(); int count = _r.ReadUInt16(); int wear = _r.ReadUInt16();
            return string.IsNullOrEmpty(item) || count == 0 || Items.Get(item) == null && !Smelting.Hidden(item) ? Stack.None : new Stack(item, count) { Wear = wear };
        }

        public Stack[] Stacks()
        {
            int n = _r.ReadByte();
            var a = new Stack[n];
            for (int i = 0; i < n; i++)
            {
                string item = Str(); int count = _r.ReadUInt16(); int wear = _r.ReadUInt16();
                a[i] = string.IsNullOrEmpty(item) || count == 0 ? Stack.None : new Stack(item, count) { Wear = wear };
            }
            return a;
        }
    }

    // Cuts a byte stream into frames. Not thread-safe; one per connection.
    public sealed class Framer
    {
        byte[] _buf = new byte[1 << 16];
        int _len;

        // Feed received bytes; returns complete frames (type byte + payload). Throws on a malformed stream.
        public void Feed(byte[] data, int count, List<byte[]> into)
        {
            if (_len + count > _buf.Length) Array.Resize(ref _buf, Math.Max(_buf.Length * 2, _len + count));
            Buffer.BlockCopy(data, 0, _buf, _len, count);
            _len += count;
            int pos = 0;
            while (_len - pos >= 4)
            {
                uint n = (uint)(_buf[pos] | _buf[pos + 1] << 8 | _buf[pos + 2] << 16 | _buf[pos + 3] << 24);
                if (n == 0 || n > Net.MaxMessage) throw new InvalidDataException("bad frame length " + n);
                if (_len - pos - 4 < n) break;
                var f = new byte[n];
                Buffer.BlockCopy(_buf, pos + 4, f, 0, (int)n);
                into.Add(f);
                pos += 4 + (int)n;
            }
            if (pos > 0) { Buffer.BlockCopy(_buf, pos, _buf, 0, _len - pos); _len -= pos; }
        }
    }
}
