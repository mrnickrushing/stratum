using System;
using System.Collections.Generic;
using System.Text;
using Stratum.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Stratum.Game
{
    // Multiplayer chat: the last few lines bottom-left, fading after a while; T (or /) opens a line to type in,
    // Enter sends, Esc closes. Only there when SendChat is set (playing on a server).
    public sealed partial class Hud
    {
        public Action<string> SendChat;
        public Func<string> OnlineInfo;
        public bool ChatOpen { get; private set; }
        public bool CanChat => SendChat != null;
        public int ChatClosedFrame = -1;          // Enter that sent a line mustn't open a new one the same frame

        sealed class Line { public string Text; public float Age; }
        readonly List<Line> _lines = new List<Line>();
        RectTransform _chat;
        Text _chatText;
        Image _chatBack;
        InputField _chatField;
        string _chatPrefill;
        int _chatOpenFrames;
        int _chatKey = -1;
        readonly StringBuilder _sb = new StringBuilder();
        const int ShowLines = 9;
        const float Fade = 10;

        void BuildChat()
        {
            _chat = UIKit.Rect(_root, "Chat").Place(0, 0, 16, 112, 560, 520);
            _chat.pivot = Vector2.zero;
            _chatBack = UIKit.Panel(_chat, "Back", new Color(0, 0, 0, 0.38f), UIKit.RoundSmall);
            _chatBack.rectTransform.anchorMin = Vector2.zero; _chatBack.rectTransform.anchorMax = new Vector2(1, 0);
            _chatBack.rectTransform.pivot = Vector2.zero;
            _chatBack.raycastTarget = false;
            _chatText = UIKit.Label(_chat, "", 24, Color.white, TextAnchor.LowerLeft).Shadowed(1.5f);
            var tr = _chatText.rectTransform;
            tr.anchorMin = Vector2.zero; tr.anchorMax = new Vector2(1, 0); tr.pivot = Vector2.zero;
            tr.anchoredPosition = new Vector2(12, 64); tr.sizeDelta = new Vector2(-24, 400);
            _chatText.lineSpacing = 1.05f;

            _chatField = UIKit.Field(_chat, "Say something (Enter to send, Esc to close)", "", 24);
            var fr = _chatField.GetComponent<RectTransform>();
            fr.anchorMin = Vector2.zero; fr.anchorMax = new Vector2(1, 0); fr.pivot = Vector2.zero;
            fr.anchoredPosition = Vector2.zero; fr.sizeDelta = new Vector2(0, 52);
            _chatField.characterLimit = 200;
            _chatField.lineType = InputField.LineType.SingleLine;
            _chatField.gameObject.SetActive(false);
        }

        public void AddChat(string from, string text)
        {
            // Legacy Text has no way to escape rich text, so a typed "<" is swapped for a look-alike.
            text = text.Replace('<', '‹');
            string line = string.IsNullOrEmpty(from)
                ? $"<color=#F5D58AFF><i>{text}</i></color>"
                : $"<color=#F5B54AFF><b>{from.Replace('<', '‹')}</b></color>  {text}";
            _lines.Add(new Line { Text = line });
            if (_lines.Count > 60) _lines.RemoveAt(0);
            _chatKey = -1;
        }

        public void OpenChat(string prefill = "")
        {
            if (!CanChat || ChatOpen) return;
            ChatOpen = true;
            _chatPrefill = prefill;
            _chatOpenFrames = 0;
            _chatField.gameObject.SetActive(true);
            _chatField.text = prefill;
            EventSystem.current?.SetSelectedGameObject(_chatField.gameObject);
            _chatField.ActivateInputField();
            _chatKey = -1;
        }

        public void CloseChat()
        {
            if (!ChatOpen) return;
            ChatOpen = false;
            ChatClosedFrame = Time.frameCount;
            _chatField.DeactivateInputField();
            _chatField.text = "";
            _chatField.gameObject.SetActive(false);
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == _chatField.gameObject)
                EventSystem.current.SetSelectedGameObject(null);
            _chatKey = -1;
        }

        void UpdateChat()
        {
            if (_chat == null) return;
            float dt = Time.unscaledDeltaTime;
            foreach (var l in _lines) l.Age += dt;
            bool visible = CanChat && !Hidden && !InventoryOpen;
            _chat.gameObject.SetActive(visible);
            if (!visible) { if (ChatOpen) CloseChat(); return; }

            if (ChatOpen)
            {
                // The key that opened the line arrives as typing a frame later; put the prefill back over it.
                if (++_chatOpenFrames <= 2 && _chatField.text != _chatPrefill) { _chatField.text = _chatPrefill; _chatField.MoveTextEnd(false); }
                if (!_chatField.isFocused && _chatOpenFrames > 2) _chatField.ActivateInputField();
                var kb = Keyboard.current;
                if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame))
                {
                    var t = _chatField.text.Trim();
                    if (t.Length > 0) SendChat?.Invoke(t);
                    CloseChat();
                }
            }

            // Rebuild the text only when what shows changes (new lines, or a fade step).
            int shown = 0; int key = ChatOpen ? 1 : 0;
            for (int i = _lines.Count - 1; i >= 0 && shown < ShowLines; i--, shown++)
                key = key * 31 + (ChatOpen ? 255 : Alpha(_lines[i].Age));
            key = key * 31 + _lines.Count;
            if (key == _chatKey) return;
            _chatKey = key;
            _sb.Clear();
            int first = Mathf.Max(0, _lines.Count - ShowLines);
            int visibleLines = 0, strongest = 0;
            for (int i = first; i < _lines.Count; i++)
            {
                int a = ChatOpen ? 255 : Alpha(_lines[i].Age);
                if (a <= 0) continue;
                strongest = Mathf.Max(strongest, a);
                if (_sb.Length > 0) _sb.Append('\n');
                // Fade every colour in the line together: wrap it, and scale each inner colour's alpha.
                _sb.Append(Faded(_lines[i].Text, a));
                visibleLines++;
            }
            _chatText.text = _sb.ToString();
            // A dark panel behind the lines, open or not: pale words on bright grass or snow can't be read without it.
            _chatBack.gameObject.SetActive(visibleLines > 0);
            if (_chatBack.gameObject.activeSelf)
            {
                _chatBack.color = new Color(0, 0, 0, (ChatOpen ? 0.45f : 0.32f) * strongest / 255f);
                float h = Mathf.Min(_chatText.preferredHeight, 400) + 18;
                _chatBack.rectTransform.anchoredPosition = new Vector2(0, 58);
                _chatBack.rectTransform.sizeDelta = new Vector2(0, h);
            }
        }

        static int Alpha(float age) => age < Fade ? 255 : Mathf.Clamp(Mathf.RoundToInt((1 - (age - Fade) / 1.5f) * 16) * 16, 0, 255);

        static string Faded(string line, int a)
        {
            if (a >= 255) return $"<color=#FFFFFFFF>{line}</color>";
            string hex = a.ToString("X2");
            // Every colour tag ends in FF; give it this alpha instead.
            return $"<color=#FFFFFF{hex}>" + line.Replace("FF>", hex + ">") + "</color>";
        }
    }
}
