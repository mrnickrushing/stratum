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

        [Serializable] class Answer { public string address; }

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
