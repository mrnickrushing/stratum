using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Stratum.Game
{
    // The Multiplayer screen: log in to an account on a server, or make one. A successful login leaves a
    // "remember me" token on this computer (Logins), so next time the password can stay blank.
    public sealed partial class Menus
    {
        public const byte ModeLogin = 0, ModeCreate = 1, ModeToken = 2;     // Server/Accounts.Mode
        const int MinName = 3, MinPassword = 6;

        public Action<string, byte, string, string, string, string> Join;   // address, mode, username, password or token, invite code, world
        public Action<string> FindWorlds;                                   // ask a server which worlds it has (answer: SetWorlds)

        // A world on the server, as its ping lists it.
        public struct WorldInfo { public string Id, Name, Desc; public bool Creative; public int Difficulty, Online; public Core.WorldKind Kind; }
        System.Collections.Generic.List<WorldInfo> _worldsOn;              // null: not known (yet)
        string _worldsFor = "", _pickedWorld;
        float _findAt = -1;
        RectTransform _serverWorlds;
        Text _worldNote;
        public bool ManyWorlds => _worldsOn != null && _worldsOn.Count > 1;
        const float FormX = -250;                                           // the form sits left, the worlds right
        public Action CancelJoin;

        InputField _joinAddr, _joinUser, _joinPass, _joinPass2, _joinInvite;
        Text _joinStatus, _joinPassLabel, _joinHint;
        Button _joinBtn, _tabLogin, _tabCreate, _forgetBtn;
        RectTransform _joinCard;
        GameObject _rowPass2, _rowInvite;
        bool _joining, _creating;
        float _joinStatusY;

        void BuildJoin()
        {
            _join = Screen("Multiplayer");
            var card = Card(_join, 1440, 800, "Multiplayer");
            _joinCard = card.rectTransform;

            _tabLogin = Tab(card.transform, "Log In", FormX - 192, () => SetJoinMode(false, true));
            _tabCreate = Tab(card.transform, "Create Account", FormX + 192, () => SetJoinMode(true, true));
            BuildWorldList(card.transform);

            float y = -196;
            (InputField f, GameObject row, Text label) Row(string label, string placeholder, string value)
            {
                var row = UIKit.Rect(card.transform, "Row " + label).Place(0.5f, 1, FormX, y, 800, 98);
                var l = UIKit.Label(row, label, 24, UIKit.Dim); l.rectTransform.Place(0, 1, 0, 0, 800, 30);
                var f = UIKit.Field(row, placeholder, value); ((RectTransform)f.transform).Place(0, 1, 0, -34, 800, 60);
                y -= 108;
                return (f, row.gameObject, l);
            }
            _joinAddr = Row("Server address (our server: just type play)", "play", SavedAddress()).f;
            _joinUser = Row($"Username ({MinName}-{Core.Net.MaxNameLength} letters, numbers, - or _)", "Username", PlayerPrefs.GetString("mp_user", PlayerPrefs.GetString("mp_name", ""))).f;
            _joinUser.characterLimit = Core.Net.MaxNameLength;
            (_joinPass, _, _joinPassLabel) = Row("Password", "Password", "");
            _joinPass.contentType = InputField.ContentType.Password;
            _joinPass.characterLimit = 64;
            (_joinPass2, _rowPass2, _) = Row("Type the password again", "Password again", "");
            _joinPass2.contentType = InputField.ContentType.Password;
            _joinPass2.characterLimit = 64;
            (_joinInvite, _rowInvite, _) = Row("Invite code (only if the server has one)", "none", "");

            _forgetBtn = UIKit.Button(_joinPass.transform.parent, "Forget me", ForgetLogin, 20);
            ((RectTransform)_forgetBtn.transform).Place(1, 1, 0, 2, 150, 32);

            _joinAddr.onValueChanged.AddListener(_ => { RefreshRemembered(); _findAt = Time.unscaledTime + 0.8f; });
            _joinUser.onValueChanged.AddListener(_ => RefreshRemembered());

            _joinHint = UIKit.Label(card.transform, "", 22, UIKit.Dim, TextAnchor.MiddleCenter, FontStyle.Italic);
            _joinStatus = UIKit.Label(card.transform, "", 25, UIKit.Dim, TextAnchor.MiddleCenter);
            _joinBtn = UIKit.Button(card.transform, "Log In", DoJoin, 30, true);
            ((RectTransform)_joinBtn.transform).Place(0.5f, 0, FormX + 180, 36, 330, 72);
            var back = UIKit.Button(card.transform, "Back", () => { if (_joining) CancelJoin?.Invoke(); _joining = false; ShowMain(); }, 30);
            ((RectTransform)back.transform).Place(0.5f, 0, FormX - 180, 36, 330, 72);

            SetJoinMode(!PlayerPrefs.HasKey("mp_user"), false);
        }

        Button Tab(Transform card, string text, float x, Action click)
        {
            var b = UIKit.Button(card, text, click, 28);
            ((RectTransform)b.transform).Place(0.5f, 1, x, -102, 376, 62);
            return b;
        }

        static void StyleTab(Button b, bool on)
        {
            ((Image)b.targetGraphic).color = on ? new Color(UIKit.Accent.r / 0.9f, UIKit.Accent.g / 0.9f, UIKit.Accent.b / 0.9f) : new Color(0.15f, 0.16f, 0.19f, 0.95f);
            b.GetComponentInChildren<Text>().color = on ? new Color(0.12f, 0.08f, 0.04f) : UIKit.Dim;
        }

        void SetJoinMode(bool creating, bool clearStatus)
        {
            _creating = creating;
            StyleTab(_tabLogin, !creating);
            StyleTab(_tabCreate, creating);
            _rowPass2.SetActive(creating);
            _rowInvite.SetActive(creating);
            _joinBtn.SetText(creating ? "Create & Join" : "Log In");
            float h = creating ? 1000 : 780;
            _joinCard.sizeDelta = new Vector2(_joinCard.sizeDelta.x, h);
            _joinHint.rectTransform.Place(0.5f, 0, FormX, 196, 820, 34);
            _joinStatus.rectTransform.Place(0.5f, 0, FormX, 122, 820, 70);
            _joinPassLabel.text = creating ? $"Password (at least {MinPassword} characters)" : "Password";
            if (clearStatus) SetJoinStatus(creating ? "Pick a username and password. You'll use them to log in from any computer." : "Log in with your account.", false);
            RefreshRemembered();
        }

        // In Log In, a login remembered on this computer lets the password stay blank.
        void RefreshRemembered()
        {
            if (_joinPass == null) return;
            bool saved = !_creating && Logins.Token(_joinAddr.text, _joinUser.text) != "";
            ((Text)_joinPass.placeholder).text = saved ? "Remembered on this computer" : "Password";
            _forgetBtn.gameObject.SetActive(saved);
            _joinHint.text = _creating ? "Ask a grown-up before sharing your password. Nobody from Stratum will ever ask for it."
                                       : saved ? "Just press Log In. Your password is remembered here." : "";
        }

        void ForgetLogin()
        {
            Logins.Forget(_joinAddr.text, _joinUser.text);
            RefreshRemembered();
            SetJoinStatus("This computer won't remember that login any more.", false);
        }

        void DoJoin()
        {
            if (_joining) return;
            var addr = _joinAddr.text.Trim();
            var user = _joinUser.text.Trim();
            var pass = _joinPass.text;
            if (!NetClient.ParseAddress(addr, out _, out _)) { SetJoinStatus("Type the server's address, like play.example.com:25600.", true); return; }
            if (!Core.Net.ValidName(user) || user.Length < MinName) { SetJoinStatus($"Usernames are {MinName}-{Core.Net.MaxNameLength} letters, numbers, spaces, - or _.", true); return; }

            byte mode;
            string secret = pass;
            if (_creating)
            {
                if (pass.Length < MinPassword) { SetJoinStatus($"Passwords need at least {MinPassword} characters.", true); return; }
                if (pass != _joinPass2.text) { SetJoinStatus("The two passwords don't match. Type them again.", true); _joinPass2.text = ""; return; }
                mode = ModeCreate;
            }
            else if (pass == "")
            {
                secret = Logins.Token(addr, user);
                if (secret == "") { SetJoinStatus("Type your password.", true); return; }
                mode = ModeToken;
            }
            else mode = ModeLogin;

            PlayerPrefs.SetString("mp_user", user);
            // Only an address the player typed is remembered; the default one keeps following DefaultServer.
            if (addr == DefaultServer.Address) PlayerPrefs.DeleteKey("mp_addr"); else PlayerPrefs.SetString("mp_addr", addr);
            PlayerPrefs.Save();
            _joining = true;
            _joinBtn.interactable = false;
            SetJoinStatus("Connecting...", false);
            string world = _worldsOn != null && _worldsFor == addr && _pickedWorld != null ? _pickedWorld : "";
            if (world != "") PlayerPrefs.SetString("mp_world", world);
            Join?.Invoke(addr, mode, user, secret, _creating ? _joinInvite.text.Trim() : "", world);
        }

        // Shows the join screen; with a message (why the last attempt or game ended) when there is one.
        // The address to show: one the player chose, else the default server. Before 0.2.1 every join saved the
        // address, so a saved copy of the built-in default counts as no choice.
        static string SavedAddress()
        {
            string a = PlayerPrefs.GetString("mp_addr", "").Trim();
            return a == "" || a == DefaultServer.BuiltIn ? DefaultServer.Address : a;
        }

        // The site says the default server has moved: follow it, unless the player picked a server of their own.
        // It's the same server under a new address, so a remembered login comes along.
        public void DefaultServerChanged(string old, string now)
        {
            string user = PlayerPrefs.GetString("mp_user", "");
            string token = Logins.Token(old, user);
            if (user != "" && token != "") Logins.Remember(now, user, token);
            string saved = PlayerPrefs.GetString("mp_addr", "").Trim();
            if (saved == old || saved == DefaultServer.BuiltIn) { PlayerPrefs.DeleteKey("mp_addr"); PlayerPrefs.Save(); }
            if (_joinAddr != null && !_joining)
            {
                string shown = _joinAddr.text.Trim();
                if (shown == "" || shown == old || shown == DefaultServer.BuiltIn) _joinAddr.text = now;
            }
        }

        // ------------------------------------------------------------------ choosing a world

        void BuildWorldList(Transform card)
        {
            var head = UIKit.Label(card, "World", 24, UIKit.Dim);
            head.rectTransform.Place(0.5f, 1, 440, -170, 470, 30);
            _serverWorlds = UIKit.Rect(card, "Worlds").Place(0.5f, 1, 440, -206, 470, 540);
            _worldNote = UIKit.Label(_serverWorlds, "", 22, UIKit.Dim, TextAnchor.UpperLeft, FontStyle.Italic);
            _worldNote.rectTransform.Place(0, 1, 0, 0, 470, 120);
        }

        // What the server said it has (null and an error if it couldn't be asked).
        public void SetWorlds(string address, System.Collections.Generic.List<WorldInfo> list, string error)
        {
            if (_joinAddr == null || address != _joinAddr.text.Trim()) return;     // the address has changed since
            _worldsFor = address;
            _worldsOn = list;
            if (list != null && list.Count > 0 && (_pickedWorld == null || !list.Exists(w => w.Id == _pickedWorld)))
            {
                string saved = PlayerPrefs.GetString("mp_world", "");
                _pickedWorld = list.Exists(w => w.Id == saved) ? saved : list[0].Id;
            }
            // Open the page the chosen world is on.
            int at = list == null ? -1 : list.FindIndex(w => w.Id == _pickedWorld);
            _worldTop = at > 0 && list.Count > 9 ? at / 8 * 8 : 0;
            DrawWorlds(error);
        }

        void DrawWorlds(string error)
        {
            for (int i = _serverWorlds.childCount - 1; i >= 0; i--)
                if (_serverWorlds.GetChild(i) != _worldNote.transform) Destroy(_serverWorlds.GetChild(i).gameObject);
            var list = _worldsOn;
            _worldNote.text = list == null ? (error ?? "Looking for this server's worlds...")
                            : list.Count == 0 ? "This server has one world." : "";
            if (list == null || list.Count == 0) return;
            // Four or fewer: a roomy card each. More: slim rows, with the chosen one opened up to say what it's like.
            // More than nine: eight at a time, and a row at the bottom that turns the page.
            bool roomy = list.Count <= 4;
            bool paged = list.Count > 9;
            const int Page = 8;
            if (!paged || _worldTop >= list.Count) _worldTop = 0;
            int last = paged ? Math.Min(list.Count, _worldTop + Page) : list.Count;
            float y = 0;
            for (int i = _worldTop; i < last; i++)
            {
                var w = list[i];
                bool on = w.Id == _pickedWorld, open = roomy || on;
                float hgt = roomy ? 124 : on ? 108 : 48;
                var b = UIKit.Button(_serverWorlds, "", () => { _pickedWorld = w.Id; DrawWorlds(null); }, 20);
                var img = (Image)b.targetGraphic;
                img.color = on ? new Color(0.36f, 0.29f, 0.14f, 1) : new Color(0.17f, 0.18f, 0.22f, 0.95f);
                if (on) { var o = b.gameObject.AddComponent<Outline>(); o.effectColor = UIKit.Accent; o.effectDistance = new Vector2(3, -3); }
                ((RectTransform)b.transform).Place(0, 1, 0, y, 470, hgt);
                var name = UIKit.Label(b.transform, w.Name, open ? 28 : 24, on ? UIKit.Accent : UIKit.Ink, TextAnchor.UpperLeft, FontStyle.Bold);
                name.rectTransform.Place(0, 1, 18, open ? -11 : -9, 230, 40);
                var info = UIKit.Label(b.transform, WorldLine(w), 18, UIKit.Dim, TextAnchor.UpperRight);
                info.rectTransform.Place(1, 1, -16, open ? -17 : -14, 250, 28);
                if (open)
                {
                    var desc = UIKit.Label(b.transform, w.Desc, 20, UIKit.Ink, TextAnchor.UpperLeft);
                    desc.rectTransform.Place(0, 1, 18, -50, 320, 56);
                }
                if (open || w.Online > 0)
                {
                    var here = UIKit.Label(b.transform, w.Online == 0 ? "nobody here" : w.Online == 1 ? "1 playing" : $"{w.Online} playing", 18,
                                           w.Online > 0 ? new Color(0.55f, 0.85f, 0.45f) : UIKit.Dim, open ? TextAnchor.LowerRight : TextAnchor.UpperRight);
                    if (open) here.rectTransform.Place(1, 0, -16, 10, 140, 28);
                    else { here.rectTransform.Place(1, 1, -16, -14, 90, 28); info.rectTransform.Place(1, 1, -110, -14, 210, 28); }
                }
                y -= hgt + (roomy ? 12 : 6);
            }
            if (paged)
            {
                int next = last >= list.Count ? 0 : last;
                var more = UIKit.Button(_serverWorlds, $"More worlds  ({_worldTop + 1}-{last} of {list.Count})  ›", () => { _worldTop = next; DrawWorlds(null); }, 20);
                ((RectTransform)more.transform).Place(0, 1, 0, y, 470, 48);
            }
        }
        int _worldTop;

        // "Sky Islands · Survival · Easy": the sort of world (unless its name says it), then how it's played.
        static string WorldLine(WorldInfo w)
        {
            string mode = w.Creative ? "Creative" : "Survival · " + new[] { "Peaceful", "Easy", "Normal" }[Mathf.Clamp(w.Difficulty, 0, 2)];
            string kind = Core.WorldGen.KindName(w.Kind);
            return w.Kind == Core.WorldKind.Normal || w.Name.IndexOf(kind, StringComparison.OrdinalIgnoreCase) >= 0 ? mode : kind + " · " + mode;
        }

        public void ShowJoin(string status)
        {
            Show(_join, true);
            _findAt = Time.unscaledTime + 0.5f;     // ask again (who is in which world changes), once a world we just left has noticed
            _joining = false;
            _joinBtn.interactable = true;
            _joinPass.text = ""; _joinPass2.text = "";
            RefreshRemembered();
            if (status != null) SetJoinStatus(status, true);
            else SetJoinMode(_creating, true);
        }

        // After a successful account creation the screen should next open on Log In.
        public void JoinedAs(string user)
        {
            _joinUser.text = user;
            PlayerPrefs.SetString("mp_user", user);
            if (_creating) SetJoinMode(false, false);
        }

        public void SetJoinStatus(string text, bool bad)
        {
            _joinStatus.text = text;
            _joinStatus.color = bad ? UIKit.Bad : UIKit.Dim;
        }

        // Tab moves between the boxes, Enter presses the button.
        void Update()
        {
            SettingsUpdate();
            if (_join == null || !_join.activeSelf || _joining) return;
            if (_findAt >= 0 && Time.unscaledTime >= _findAt)
            {
                _findAt = -1;
                string a = _joinAddr.text.Trim();
                if (a != _worldsFor) { _worldsOn = null; DrawWorlds(null); }
                if (NetClient.ParseAddress(a, out _, out _)) FindWorlds?.Invoke(a);
                else DrawWorlds("Type the server's address to see its worlds.");
            }
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.tabKey.wasPressedThisFrame)
            {
                var order = _creating ? new[] { _joinAddr, _joinUser, _joinPass, _joinPass2, _joinInvite } : new[] { _joinAddr, _joinUser, _joinPass };
                int at = Array.FindIndex(order, f => f.isFocused);
                bool back = kb.shiftKey.isPressed;
                int next = at < 0 ? 0 : (at + (back ? order.Length - 1 : 1)) % order.Length;
                EventSystem.current?.SetSelectedGameObject(order[next].gameObject);
                order[next].ActivateInputField();
            }
            else if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) DoJoin();
        }
    }

    // "Remember me" tokens the server hands out, one per server and username, kept in PlayerPrefs.
    public static class Logins
    {
        static string Key(string address, string user) => "mp_tok_" + (address ?? "").Trim().ToLowerInvariant() + "|" + (user ?? "").Trim().ToLowerInvariant();
        public static string Token(string address, string user) => PlayerPrefs.GetString(Key(address, user), "");
        public static void Remember(string address, string user, string token)
        {
            if (string.IsNullOrEmpty(token)) return;
            PlayerPrefs.SetString(Key(address, user), token);
            PlayerPrefs.Save();
        }
        public static void Forget(string address, string user) { PlayerPrefs.DeleteKey(Key(address, user)); PlayerPrefs.Save(); }
    }
}
