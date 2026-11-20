using System;
using System.IO;
using System.Linq;

namespace Stratum.Server
{
    // Copies of everything the server keeps (accounts, every world, players' things) in <data>/backups/<date-time>.
    // One is taken at start and then twice a day; the newest six are kept. To go back to one: stop the server and
    // copy that folder's contents over the data folder.
    public static class Backups
    {
        public const int Keep = 6;

        public static void Take(string dir)
        {
            try
            {
                if (!Directory.Exists(dir) || !Directory.EnumerateFileSystemEntries(dir).Any(e => Path.GetFileName(e) != "backups")) return;
                string root = Path.Combine(dir, "backups");
                string dest = Path.Combine(root, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
                if (Directory.Exists(dest)) return;
                int files = Copy(dir, dest + ".part", true);
                Directory.Move(dest + ".part", dest);
                var all = Directory.GetDirectories(root).Where(d => !d.EndsWith(".part")).OrderByDescending(d => Path.GetFileName(d), StringComparer.Ordinal).ToList();
                for (int i = Keep; i < all.Count; i++) Directory.Delete(all[i], true);
                foreach (var part in Directory.GetDirectories(root, "*.part")) Directory.Delete(part, true);
                Log.Info($"backup saved: {dest} ({files} files)");
            }
            catch (Exception e) { Log.Warn("backup failed: " + e.Message); }
        }

        static int Copy(string from, string to, bool top)
        {
            if (Directory.Exists(to)) Directory.Delete(to, true);
            Directory.CreateDirectory(to);
            int n = 0;
            foreach (var f in Directory.GetFiles(from))
            {
                if (f.EndsWith(".tmp")) continue;
                try { File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true); n++; } catch (IOException) { }   // being swapped right now: next time
            }
            foreach (var d in Directory.GetDirectories(from))
                if (!(top && Path.GetFileName(d) == "backups")) n += Copy(d, Path.Combine(to, Path.GetFileName(d)), false);
            return n;
        }
    }
}
