using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Stratum.Core;

namespace Stratum.Game
{
    // The game's end of a connection to a Stratum server: a reader thread cuts the stream into messages for the
    // main thread to take with Poll, and a writer thread sends what Send queues.
    public sealed class NetClient : IDisposable
    {
        readonly TcpClient _tcp = new TcpClient { NoDelay = true };
        NetworkStream _stream;
        readonly BlockingCollection<byte[]> _out = new BlockingCollection<byte[]>();
        readonly ConcurrentQueue<byte[]> _in = new ConcurrentQueue<byte[]>();
        Task _connect;
        int _closed;
        public string Error { get; private set; }
        public long BytesIn, BytesOut;

        public NetClient(string host, int port)
        {
            _tcp.SendBufferSize = 1 << 16;
            _tcp.ReceiveBufferSize = 1 << 18;
            _connect = _tcp.ConnectAsync(host, port);
        }

        // Splits "host", "host:port", "[v6]:port" (whitespace and a stray "stratum://" are fine).
        public static bool ParseAddress(string text, out string host, out int port)
        {
            host = null; port = Net.DefaultPort;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var s = text.Trim();
            if (s.StartsWith("stratum://", StringComparison.OrdinalIgnoreCase)) s = s.Substring(10);
            s = s.TrimEnd('/');
            if (s.StartsWith("["))
            {
                int close = s.IndexOf(']');
                if (close < 0) return false;
                host = s.Substring(1, close - 1);
                var rest = s.Substring(close + 1);
                if (rest.StartsWith(":") && !int.TryParse(rest.Substring(1), out port)) return false;
            }
            else
            {
                int colon = s.LastIndexOf(':');
                if (colon > 0 && s.IndexOf(':') == colon)
                {
                    if (!int.TryParse(s.Substring(colon + 1), out port)) return false;
                    host = s.Substring(0, colon);
                }
                else host = s;
            }
            return host.Length > 0 && port > 0 && port < 65536;
        }

        // null while still connecting, then true or false (see Error).
        public bool? Connected
        {
            get
            {
                if (_closed != 0) return false;
                if (_connect == null) return true;
                if (!_connect.IsCompleted) return null;
                if (_connect.IsFaulted || !_tcp.Connected)
                {
                    var e = _connect.Exception?.GetBaseException();
                    Fail(e is SocketException se ? Explain(se) : "Couldn't reach the server.");
                    return false;
                }
                _connect = null;
                _stream = _tcp.GetStream();
                new Thread(ReadLoop) { IsBackground = true, Name = "net read" }.Start();
                new Thread(WriteLoop) { IsBackground = true, Name = "net write" }.Start();
                return true;
            }
        }

        static string Explain(SocketException e) => e.SocketErrorCode switch
        {
            SocketError.ConnectionRefused => "The server isn't running at that address (connection refused).",
            SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain => "No server by that name. Check the address.",
            SocketError.TimedOut => "The server didn't answer.",
            SocketError.NetworkUnreachable or SocketError.HostUnreachable => "Can't reach that address from this network.",
            _ => "Couldn't connect: " + e.SocketErrorCode,
        };

        public bool Open => _closed == 0 && Error == null;

        public void Send(Packet p)
        {
            if (!Open) return;
            try { _out.Add(p.Bytes()); } catch (InvalidOperationException) { }
        }

        // The next message from the server, or null.
        public Reader Poll() => _in.TryDequeue(out var f) ? new Reader(f) : null;

        void ReadLoop()
        {
            var buf = new byte[1 << 16];
            var framer = new Framer();
            var frames = new List<byte[]>();
            try
            {
                while (_closed == 0)
                {
                    int n = _stream.Read(buf, 0, buf.Length);
                    if (n <= 0) { Fail("The server closed the connection."); return; }
                    Interlocked.Add(ref BytesIn, n);
                    frames.Clear();
                    framer.Feed(buf, n, frames);
                    foreach (var f in frames) _in.Enqueue(f);
                }
            }
            catch (Exception e) { if (_closed == 0) Fail(e is System.IO.IOException ? "Lost the connection to the server." : "The server sent something unreadable."); }
        }

        void WriteLoop()
        {
            try
            {
                foreach (var f in _out.GetConsumingEnumerable())
                {
                    _stream.Write(f, 0, f.Length);
                    Interlocked.Add(ref BytesOut, f.Length);
                }
            }
            catch (Exception) { if (_closed == 0) Fail("Lost the connection to the server."); }
        }

        void Fail(string why)
        {
            if (Error == null) Error = why;
            Dispose();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _closed, 1) != 0) return;
            try { _out.CompleteAdding(); } catch { }
            try { _tcp.Close(); } catch { }
        }

        // Lets queued messages go out (up to `ms`), then closes.
        public void Close(int ms = 400)
        {
            if (_closed != 0) return;
            try { _out.CompleteAdding(); } catch { }
            var t = new Thread(() => { Thread.Sleep(ms); Dispose(); }) { IsBackground = true };
            t.Start();
        }
    }
}
