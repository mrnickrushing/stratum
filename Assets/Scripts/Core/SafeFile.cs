using System.IO;

namespace Stratum.Core
{
    // Putting a freshly written file in place of the old one without a moment where neither exists: a crash or a
    // power cut mid-save leaves the old file, the new one, or (with keep) the old one beside it as ".bak".
    public static class SafeFile
    {
        public static void Swap(string tmp, string path, bool keep = false)
        {
            if (File.Exists(path)) File.Replace(tmp, path, keep ? path + ".bak" : null);
            else File.Move(tmp, path);
        }

        // The file to read: the real one, or the backup if the real one is missing or empty.
        public static string Pick(string path)
        {
            try { if (File.Exists(path) && new FileInfo(path).Length > 0) return path; } catch { }
            return File.Exists(path + ".bak") ? path + ".bak" : path;
        }
    }
}
