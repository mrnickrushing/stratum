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
    //   STRATUM_PASSWORD / --password  invite code needed to make an account (blank: anyone who has the address can)
    //   STRATUM_OPS / --ops            names, comma separated, who may use /give, /time set, /day and /night
    //   STRATUM_RESET / --reset        "Name1,Name2": clears those accounts' passwords once; each owner's next login
    //                                  sets a new one (for a forgotten password; everything they own is kept)
    //   STRATUM_CREATIVE / --creative  1 for creative mode
    //   STRATUM_VIEW / --view          most chunks a player sees in each direction (default 10)
    //   STRATUM_DIFFICULTY / --difficulty  peaceful (no monsters, no hunger), easy, normal (default) or hard
    //   STRATUM_KIND / --kind          a new world's sort: normal, sky, frozen, dunes, islands, peaks or flat
    //   STRATUM_WORLDS / --worlds      worlds to choose from: "id|Name|survival/creative|difficulty|seed|description|kind;..."
    //                                  ("main" is the world already in the data folder; others go in worlds/<id>).
    //                                  Unset: one world, from STRATUM_CREATIVE and STRATUM_DIFFICULTY.
    // `--selftest` starts a private server with two scripted players and checks they see each other's world.
    public static class Program
    {
        public static int Main(string[] args)
        {
            if (Array.IndexOf(args, "--selftest") >= 0) return SelfTest.Run();
            if (Array.IndexOf(args, "--balance") >= 0) return Balance.Run();
            var cfg = Read(args);
            var server = new GameServer(cfg);
            server.Start();

            var done = new ManualResetEventSlim(false);
            // Asked to stop (a new version is going up): anyone playing is told first and given a moment to get
            // somewhere safe. STRATUM_STOP_WARNING is how many seconds (20 unless set; 0 for none).
            int gate = 0;
            void Shutdown()
            {
                if (done.IsSet || Interlocked.Exchange(ref gate, 1) != 0) { done.Wait(); return; }
                int warn = int.TryParse(Environment.GetEnvironmentVariable("STRATUM_STOP_WARNING"), out int w) ? Math.Clamp(w, 0, 300) : 20;
                if (warn > 0 && server.Online > 0)
                {
                    Log.Info($"stopping in {warn} s: {server.Online} playing have been told");
                    server.Announce($"The server is restarting for an update in {warn} seconds. Everything is saved. You can log in again about a minute later.");
                    for (int left = warn; left > 0 && server.Online > 0; left--)
                    {
                        if (left == 5) server.Announce("Restarting in 5 seconds...");
                        Thread.Sleep(1000);
                    }
                }
                Log.Info("stopping...");
                server.Stop();
                done.Set();
            }
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
            foreach (var op in (Get("STRATUM_OPS", "--ops") ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)) c.Ops.Add(op.Trim().ToLowerInvariant());
            c.Reset = Get("STRATUM_RESET", "--reset") ?? "";
            var cr = Get("STRATUM_CREATIVE", "--creative");
            c.Creative = cr == "1" || string.Equals(cr, "true", StringComparison.OrdinalIgnoreCase);
            if (int.TryParse(Get("STRATUM_VIEW", "--view"), out int view)) c.MaxView = Math.Clamp(view, 4, 16);
            var diff = (Get("STRATUM_DIFFICULTY", "--difficulty") ?? "normal").ToLowerInvariant();
            c.Difficulty = diff.StartsWith("p") || diff == "0" ? 0 : diff.StartsWith("e") || diff == "1" ? 1 : diff.StartsWith("h") || diff == "3" ? 3 : 2;
            c.Kind = WorldGen.ParseKind(Get("STRATUM_KIND", "--kind"));
            var keep = Get("STRATUM_KEEP", "--keep");
            c.Keep = keep == "1" || string.Equals(keep, "true", StringComparison.OrdinalIgnoreCase);
            c.Backup = Get("STRATUM_BACKUPS", "--backups") != "0";
            c.LegacyLogin = Get("STRATUM_LEGACY_LOGIN", "--legacy-login") == "1";
            c.Worlds = WorldDef.Parse(Get("STRATUM_WORLDS", "--worlds"), SeedFrom);
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
