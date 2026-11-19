using System;
using System.Runtime.InteropServices;
using System.Threading;
using Stratum.Core;

namespace Stratum.Server
{
    // Settings come from environment variables (handy on Railway or in Docker), and command-line flags override them:
    //   STRATUM_PORT / --port          TCP port (default 25600)
    //   STRATUM_DATA / --data          world folder (default ./world; /data in the Docker image)
    //   STRATUM_SEED / --seed          seed for a new world (default random; ignored once the world exists)
    //   STRATUM_NAME / --name          server name shown to players
    //   STRATUM_MOTD / --motd          a line shown when joining
    //   STRATUM_MAX_PLAYERS / --max    players at once (default 10)
    //   STRATUM_PASSWORD / --password  needed to join, if set
    //   STRATUM_CREATIVE / --creative  1 for creative mode
    //   STRATUM_VIEW / --view          most chunks a player sees in each direction (default 10)
    // `--selftest` starts a private server with two scripted players and checks they see each other's world.
    public static class Program
    {
        public static int Main(string[] args)
        {
            if (Array.IndexOf(args, "--selftest") >= 0) return SelfTest.Run();
            var cfg = Read(args);
            var server = new GameServer(cfg);
            server.Start();

            var done = new ManualResetEventSlim(false);
            void Shutdown() { if (done.IsSet) return; Log.Info("stopping..."); server.Stop(); done.Set(); }
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; Shutdown(); };
            using var term = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx => { ctx.Cancel = true; Shutdown(); });
            AppDomain.CurrentDomain.ProcessExit += (_, _) => Shutdown();
            done.Wait();
            return 0;
        }

        static ServerConfig Read(string[] args)
        {
            string Get(string env, string flag)
            {
                int i = Array.IndexOf(args, flag);
                if (i >= 0 && i + 1 < args.Length) return args[i + 1];
                var v = Environment.GetEnvironmentVariable(env);
                return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
            }
            var c = new ServerConfig();
            if (int.TryParse(Get("STRATUM_PORT", "--port"), out int port)) c.Port = port;
            c.Dir = Get("STRATUM_DATA", "--data") ?? c.Dir;
            var seed = Get("STRATUM_SEED", "--seed");
            if (seed != null) c.Seed = SeedFrom(seed);
            c.Name = Get("STRATUM_NAME", "--name") ?? c.Name;
            c.Motd = Get("STRATUM_MOTD", "--motd") ?? "";
            if (int.TryParse(Get("STRATUM_MAX_PLAYERS", "--max"), out int max)) c.MaxPlayers = Math.Clamp(max, 1, 64);
            c.Password = Get("STRATUM_PASSWORD", "--password") ?? "";
            var cr = Get("STRATUM_CREATIVE", "--creative");
            c.Creative = cr == "1" || string.Equals(cr, "true", StringComparison.OrdinalIgnoreCase);
            if (int.TryParse(Get("STRATUM_VIEW", "--view"), out int view)) c.MaxView = Math.Clamp(view, 4, 16);
            return c;
        }

        // The same as the game's New World seed box (WorldStore.SeedFrom), so a seed typed in either makes the
        // same world. (string.GetHashCode changes every run in .NET, so it can't be used.)
        static int SeedFrom(string text)
        {
            if (int.TryParse(text.Trim(), out int n)) return n;
            unchecked { int h = 23; foreach (char ch in text.Trim()) h = h * 31 + ch; return h; }
        }
    }
}
