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

        public Action<string, byte, string, string, string> Join;           // address, mode, username, password or token, invite code
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
            var card = Card(_join, 940, 800, "Multiplayer");
            _joinCard = card.rectTransform;

            _tabLogin = Tab(card.transform, "Log In", -192, () => SetJoinMode(false, true));
            _tabCreate = Tab(card.transform, "Create Account", 192, () => SetJoinMode(true, true));

            float y = -196;
            (InputField f, GameObject row, Text label) Row(string label, string placeholder, string value)
            {
                var row = UIKit.Rect(card.transform, "Row " + label).Place(0.5f, 1, 0, y, 800, 98);
                var l = UIKit.Label(row, label, 24, UIKit.Dim); l.rectTransform.Place(0, 1, 0, 0, 800, 30);
                var f = UIKit.Field(row, placeholder, value); ((RectTransform)f.transform).Place(0, 1, 0, -34, 800, 60);
                y -= 108;
                return (f, row.gameObject, l);
            }
            _joinAddr = Row("Server address", "host:port", SavedAddress()).f;
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

            _joinAddr.onValueChanged.AddListener(_ => RefreshRemembered());
            _joinUser.onValueChanged.AddListener(_ => RefreshRemembered());

            _joinHint = UIKit.Label(card.transform, "", 22, UIKit.Dim, TextAnchor.MiddleCenter, FontStyle.Italic);
            _joinStatus = UIKit.Label(card.transform, "", 25, UIKit.Dim, TextAnchor.MiddleCenter);
            _joinBtn = UIKit.Button(card.transform, "Log In", DoJoin, 30, true);
            ((RectTransform)_joinBtn.transform).Place(0.5f, 0, 180, 36, 330, 72);
            var back = UIKit.Button(card.transform, "Back", () => { if (_joining) CancelJoin?.Invoke(); _joining = false; ShowMain(); }, 30);
            ((RectTransform)back.transform).Place(0.5f, 0, -180, 36, 330, 72);

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
            _joinHint.rectTransform.Place(0.5f, 0, 0, 196, 840, 34);
            _joinStatus.rectTransform.Place(0.5f, 0, 0, 122, 860, 70);
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
            Join?.Invoke(addr, mode, user, secret, _creating ? _joinInvite.text.Trim() : "");
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

        public void ShowJoin(string status)
        {
            Show(_join, true);
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
