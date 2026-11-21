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

        // Reads a file with `parse`, falling back to its ".bak" when the file is missing, empty or won't parse (parse
        // may throw or return null). A file that exists but can't be read is copied to ".bad" first, so nothing
        // written later can destroy the evidence. `from` says which copy was used ("" when neither could be).
        public static T Load<T>(string path, System.Func<string, T> parse, out string from) where T : class
        {
            from = "";
            foreach (var cand in new[] { path, path + ".bak" })
            {
                if (!File.Exists(cand)) continue;
                try
                {
                    var v = new FileInfo(cand).Length > 0 ? parse(cand) : null;
                    if (v != null) { from = cand; return v; }
                }
                catch { }
                if (cand == path) try { File.Copy(path, path + ".bad", true); } catch { }
            }
            return null;
        }

        // The file to read: the real one, or the backup if the real one is missing or empty.
        public static string Pick(string path)
        {
            try { if (File.Exists(path) && new FileInfo(path).Length > 0) return path; } catch { }
            return File.Exists(path + ".bak") ? path + ".bak" : path;
        }
    }
}
