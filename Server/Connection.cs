using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using Stratum.Core;

namespace Stratum.Server
{
    // One TCP connection: a reader thread that cuts the stream into frames for the game loop, and a writer thread
    // that drains a send queue. Either side failing closes it; the loop is told once.
    public sealed class Connection
    {
        public readonly int Id;
        public readonly string Address;
        readonly TcpClient _tcp;
        readonly NetworkStream _stream;
        readonly BlockingCollection<byte[]> _out = new BlockingCollection<byte[]>();
        volatile Action<Connection, byte[]> _onFrame;
        volatile Action<Connection, string> _onClosed;
        long _queued;
        int _closed;
        public long LastHeard = Environment.TickCount64;
        public long BytesSent, BytesReceived;

        const long MaxQueued = 48L << 20;      // a client this far behind is dropped rather than buffered forever

        public Connection(int id, TcpClient tcp, Action<Connection, byte[]> onFrame, Action<Connection, string> onClosed)
        {
            Id = id; _tcp = tcp; _onFrame = onFrame; _onClosed = onClosed;
            Address = tcp.Client.RemoteEndPoint?.ToString() ?? "?";
            tcp.NoDelay = true;
            tcp.SendBufferSize = 1 << 18;
            tcp.ReceiveBufferSize = 1 << 16;
            _stream = tcp.GetStream();
            new Thread(ReadLoop) { IsBackground = true, Name = "read " + id }.Start();
            new Thread(WriteLoop) { IsBackground = true, Name = "write " + id }.Start();
        }

        public bool Open => Volatile.Read(ref _closed) == 0;

        // Hands the connection to whoever handles it next (the front door, then a world).
        public volatile bool Routed;          // handed on from the front door to a world
        public void Route(Action<Connection, byte[]> onFrame, Action<Connection, string> onClosed) { Routed = true; _onClosed = onClosed; _onFrame = onFrame; }

        public void Send(byte[] frame)
        {
            if (!Open) return;
            if (Interlocked.Add(ref _queued, frame.Length) > MaxQueued) { Close("too far behind"); return; }
            try { _out.Add(frame); } catch (InvalidOperationException) { }
        }

        public void Send(Packet p) => Send(p.Bytes());

        void ReadLoop()
        {
            var buf = new byte[1 << 16];
            var framer = new Framer();
            var frames = new List<byte[]>();
            try
            {
                while (Open)
                {
                    int n = _stream.Read(buf, 0, buf.Length);
                    if (n <= 0) { Close("disconnected"); return; }
                    BytesReceived += n;
                    LastHeard = Environment.TickCount64;
                    frames.Clear();
                    framer.Feed(buf, n, frames);
                    foreach (var f in frames) _onFrame(this, f);
                }
            }
            catch (Exception e) { Close(e is System.IO.IOException ? "connection lost" : "bad data: " + e.Message); }
        }

        void WriteLoop()
        {
            try
            {
                foreach (var f in _out.GetConsumingEnumerable())
                {
                    _stream.Write(f, 0, f.Length);
                    Interlocked.Add(ref _queued, -f.Length);
                    BytesSent += f.Length;
                }
            }
            catch (Exception) { Close("connection lost"); }
        }

        // Sends what's queued (briefly), then closes.
        public void CloseAfterSending(string why)
        {
            _out.CompleteAdding();
            new Thread(() => { Thread.Sleep(500); Close(why); }) { IsBackground = true }.Start();
        }

        public void Close(string why)
        {
            if (Interlocked.Exchange(ref _closed, 1) != 0) return;
            try { _out.CompleteAdding(); } catch { }
            try { _tcp.Close(); } catch { }
            _onClosed(this, why);
        }
    }
}
