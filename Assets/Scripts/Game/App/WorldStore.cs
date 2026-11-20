using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Stratum.Game
{
    [Serializable]
    public sealed class SlotSave { public string item; public int count; public int wear; }

    // world.json in each world's folder. Edited chunks sit beside it in chunks/.
    [Serializable]
    public sealed class WorldMeta
    {
        public int version = 1;
        public string name;
        public int seed;
        public bool creative;
        public int kind;                   // WorldKind (0 normal); worlds made before 0.4 have none, so normal
        public float time = 0.3f;
        public int day = 1;
        public bool hasPlayer;
        public float px, py, pz, yaw, pitch, health = 20;
        public bool flying;
        public float sx, sy, sz;
        public int selected;
        public List<SlotSave> slots = new List<SlotSave>();
        public long created, lastPlayed;
        public float food = 20, saturation = 5;
        public int difficulty = 2;
        public bool hasBed;
        public int xpLevel, xpPoints;
        public int weather; public float weatherLeft = 900;
        public int bx, by, bz;
        public double minutesPlayed;
        public bool keepInventory;         // nothing is dropped on death
        [NonSerialized] public string Folder;
    }

    public static class WorldStore
    {
        public static string Root => Path.Combine(Application.persistentDataPath, "worlds");

        public static List<WorldMeta> List()
        {
            var list = new List<WorldMeta>();
            if (!Directory.Exists(Root)) return list;
            foreach (var dir in Directory.GetDirectories(Root))
            {
                var f = Path.Combine(dir, "world.json");
                if (!File.Exists(f)) continue;
                try
                {
                    var m = JsonUtility.FromJson<WorldMeta>(File.ReadAllText(f));
                    m.Folder = dir;
                    list.Add(m);
                }
                catch (Exception e) { Debug.LogWarning("skipping world " + dir + ": " + e.Message); }
            }
            return list.OrderByDescending(m => m.lastPlayed).ToList();
        }

        public static WorldMeta Create(string name, int seed, bool creative, int difficulty = 2, Core.WorldKind kind = Core.WorldKind.Normal, bool keep = false)
        {
            Directory.CreateDirectory(Root);
            string safe = new string((name ?? "World").Where(c => char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_').ToArray()).Trim();
            if (safe.Length == 0) safe = "World";
            string folder = Path.Combine(Root, safe);
            for (int i = 2; Directory.Exists(folder); i++) folder = Path.Combine(Root, safe + " " + i);
            Directory.CreateDirectory(folder);
            var m = new WorldMeta { name = string.IsNullOrWhiteSpace(name) ? "New World" : name.Trim(), seed = seed, creative = creative, difficulty = difficulty, kind = (int)kind, keepInventory = keep, created = DateTime.UtcNow.Ticks, lastPlayed = DateTime.UtcNow.Ticks, Folder = folder };
            Save(m);
            return m;
        }

        public static void Save(WorldMeta m)
        {
            var path = Path.Combine(m.Folder, "world.json");
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonUtility.ToJson(m, true));
            Core.SafeFile.Swap(tmp, path, keep: true);
        }

        public static void Delete(WorldMeta m)
        {
            if (m?.Folder == null || !m.Folder.StartsWith(Root)) return;
            var backups = BackupDir(m);
            Directory.Delete(m.Folder, true);
            try { if (Directory.Exists(backups)) Directory.Delete(backups, true); } catch { }
        }

        // ------------------------------------------------------------------ backups
        // A copy of the whole world folder is taken each time the world is opened (at most one every ten minutes),
        // and the newest five are kept, in <data>/backups/<world folder>/<date-time>.

        public const int KeepBackups = 5;
        public static string BackupRoot => Path.Combine(Application.persistentDataPath, "backups");
        static string BackupDir(WorldMeta m) => Path.Combine(BackupRoot, Path.GetFileName(m.Folder.TrimEnd(Path.DirectorySeparatorChar)));

        public struct BackupInfo { public string Path; public DateTime When; public int Day; }

        public static List<BackupInfo> Backups(WorldMeta m)
        {
            var list = new List<BackupInfo>();
            if (m?.Folder == null) return list;
            var dir = BackupDir(m);
            if (!Directory.Exists(dir)) return list;
            foreach (var d in Directory.GetDirectories(dir))
            {
                if (!DateTime.TryParseExact(Path.GetFileName(d), "yyyyMMdd-HHmmss", null, System.Globalization.DateTimeStyles.AssumeLocal, out var when)) continue;
                int day = 0;
                try { day = JsonUtility.FromJson<WorldMeta>(File.ReadAllText(Path.Combine(d, "world.json"))).day; } catch { continue; }   // not a whole copy
                list.Add(new BackupInfo { Path = d, When = when, Day = day });
            }
            return list.OrderByDescending(b => b.When).ToList();
        }

        public static void Backup(WorldMeta m, bool force = false)
        {
            try
            {
                if (m?.Folder == null || !File.Exists(Path.Combine(m.Folder, "world.json"))) return;
                var have = Backups(m);
                if (!force && have.Count > 0 && (DateTime.Now - have[0].When).TotalMinutes < 10) return;
                var dest = Path.Combine(BackupDir(m), DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                if (Directory.Exists(dest)) return;
                CopyDir(m.Folder, dest + ".part");
                Directory.Move(dest + ".part", dest);            // only a finished copy gets the real name
                have = Backups(m);
                for (int i = KeepBackups; i < have.Count; i++) Directory.Delete(have[i].Path, true);
            }
            catch (Exception e) { Debug.LogWarning("backup failed: " + e.Message); }
        }

        // Puts a backup in the world's place. The world as it is now is backed up first, so this can be undone.
        public static bool Restore(WorldMeta m, BackupInfo b)
        {
            try
            {
                if (m?.Folder == null || !m.Folder.StartsWith(Root) || !Directory.Exists(b.Path)) return false;
                Backup(m, true);
                var old = m.Folder + ".old";
                if (Directory.Exists(old)) Directory.Delete(old, true);
                CopyDir(b.Path, m.Folder + ".new");
                Directory.Move(m.Folder, old);
                Directory.Move(m.Folder + ".new", m.Folder);
                Directory.Delete(old, true);
                return true;
            }
            catch (Exception e) { Debug.LogWarning("restore failed: " + e.Message); return false; }
        }

        static void CopyDir(string from, string to)
        {
            if (Directory.Exists(to)) Directory.Delete(to, true);
            Directory.CreateDirectory(to);
            foreach (var f in Directory.GetFiles(from)) if (!f.EndsWith(".tmp")) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
            foreach (var d in Directory.GetDirectories(from)) CopyDir(d, Path.Combine(to, Path.GetFileName(d)));
        }

        // Text seeds turn into numbers the same way every time; blank picks one at random.
        public static int SeedFrom(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return UnityEngine.Random.Range(int.MinValue / 2, int.MaxValue / 2);
            if (int.TryParse(text.Trim(), out int n)) return n;
            unchecked
            {
                int h = 23;
                foreach (char c in text.Trim()) h = h * 31 + c;
                return h;
            }
        }
    }
}
