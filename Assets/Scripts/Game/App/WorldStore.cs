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

        public static WorldMeta Create(string name, int seed, bool creative, int difficulty = 2, Core.WorldKind kind = Core.WorldKind.Normal)
        {
            Directory.CreateDirectory(Root);
            string safe = new string((name ?? "World").Where(c => char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_').ToArray()).Trim();
            if (safe.Length == 0) safe = "World";
            string folder = Path.Combine(Root, safe);
            for (int i = 2; Directory.Exists(folder); i++) folder = Path.Combine(Root, safe + " " + i);
            Directory.CreateDirectory(folder);
            var m = new WorldMeta { name = string.IsNullOrWhiteSpace(name) ? "New World" : name.Trim(), seed = seed, creative = creative, difficulty = difficulty, kind = (int)kind, created = DateTime.UtcNow.Ticks, lastPlayed = DateTime.UtcNow.Ticks, Folder = folder };
            Save(m);
            return m;
        }

        public static void Save(WorldMeta m)
        {
            var path = Path.Combine(m.Folder, "world.json");
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonUtility.ToJson(m, true));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        public static void Delete(WorldMeta m)
        {
            if (m?.Folder == null || !m.Folder.StartsWith(Root)) return;
            Directory.Delete(m.Folder, true);
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
