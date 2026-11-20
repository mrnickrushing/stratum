using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace Stratum.Game
{
    // The server the Multiplayer screen fills in by itself. It is built in, and at launch the game asks the download
    // site which address is current (site/public/server.json), so if the server ever moves, changing that one file
    // points every installed copy at it; the answer is kept for launches without internet. Nothing is sent but the
    // request itself.
    public static class DefaultServer
    {
        public const string BuiltIn = "switchback.proxy.rlwy.net:46511";
        public const string Url = "https://stratum.rushingtechnologies.com/server.json";
        const string Key = "mp_default";

        public static string Address
        {
            get
            {
                var a = PlayerPrefs.GetString(Key, "");
                return NetClient.ParseAddress(a, out _, out _) ? a : BuiltIn;
            }
        }

        [Serializable] class Name { public string name, address; }
        [Serializable] class Answer { public string address; public Name[] names; }

        // Short names: one easy word typed as the address ("play") stands for a server's real address. "play" and
        // "stratum" always mean the default server; the site's server.json can name more (kept for next time).
        const string NamesKey = "mp_names";

        public static string Resolve(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            string w = text.Trim().ToLowerInvariant();
            if (w.IndexOf('.') >= 0 || w.IndexOf(':') >= 0 || w == "localhost") return text;
            foreach (var pair in PlayerPrefs.GetString(NamesKey, "").Split(';'))
            {
                int eq = pair.IndexOf('=');
                if (eq > 0 && pair.Substring(0, eq) == w && NetClient.ParseAddress(pair.Substring(eq + 1), out _, out _)) return pair.Substring(eq + 1);
            }
            return w == "play" || w == "stratum" ? Address : text;
        }

        // Calls changed(old, now) if the site names a different server than the one in use.
        public static IEnumerator Refresh(Action<string, string> changed)
        {
            using (var req = UnityWebRequest.Get(Url))
            {
                req.timeout = 8;
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success) yield break;
                string now;
                try { now = JsonUtility.FromJson<Answer>(req.downloadHandler.text)?.address?.Trim(); }
                catch (Exception) { yield break; }
                try
                {
                    var names = JsonUtility.FromJson<Answer>(req.downloadHandler.text)?.names;
                    var sb = new System.Text.StringBuilder();
                    if (names != null)
                        foreach (var n in names)
                            if (!string.IsNullOrWhiteSpace(n?.name) && NetClient.ParseAddress(n.address, out _, out _) && n.name.IndexOfAny(new[] { ';', '=', '.', ':' }) < 0)
                                sb.Append(n.name.Trim().ToLowerInvariant()).Append('=').Append(n.address.Trim()).Append(';');
                    PlayerPrefs.SetString(NamesKey, sb.ToString());
                }
                catch (Exception) { }
                if (string.IsNullOrEmpty(now) || !NetClient.ParseAddress(now, out _, out _)) yield break;
                string old = Address;
                if (now == old) yield break;
                PlayerPrefs.SetString(Key, now);
                PlayerPrefs.Save();
                Debug.Log($"[Stratum] default server is now {now}");
                changed?.Invoke(old, now);
            }
        }
    }
}
