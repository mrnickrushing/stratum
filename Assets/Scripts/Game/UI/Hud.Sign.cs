using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Stratum.Game
{
    // Writing on a sign: up to four short lines, DONE (or Esc) puts them up.
    public sealed partial class Hud
    {
        GameObject _signPanel;
        InputField _signField;
        Action<string> _signDone;
        public bool SignOpen => _signPanel != null && _signPanel.activeSelf;

        void BuildSign()
        {
            var back = UIKit.Panel(_root, "SignEditor", new Color(0, 0, 0, 0.45f), UIKit.White);
            back.rectTransform.Fill();
            _signPanel = back.gameObject;
            var card = UIKit.Panel(back.transform, "Board", new Color(0.62f, 0.46f, 0.28f, 1), UIKit.RoundSmall);
            card.rectTransform.Place(0.5f, 0.5f, 0, 40, 720, 330);
            var title = UIKit.Label(back.transform, "Write on the sign", 30, UIKit.Ink, TextAnchor.MiddleCenter, FontStyle.Bold).Shadowed();
            title.rectTransform.Place(0.5f, 0.5f, 0, 250, 720, 44);
            _signField = UIKit.Field(card.transform, "Four lines, Enter for a new one", "", 34);
            _signField.lineType = InputField.LineType.MultiLineNewline;
            _signField.characterLimit = Core.Containers.SignChars;
            ((RectTransform)_signField.transform).Place(0.5f, 0.5f, 0, 0, 660, 270);
            _signField.textComponent.alignment = TextAnchor.MiddleCenter;
            var done = UIKit.Button(back.transform, "Done", CloseSign, 30, true);
            ((RectTransform)done.transform).Place(0.5f, 0.5f, 0, -190, 320, 72);
            _signPanel.SetActive(false);
        }

        public void EditSign(Vector3Int at, string current, Action<string> done)
        {
            if (_signPanel == null) BuildSign();
            _signDone = done;
            _signField.text = current ?? "";
            _signPanel.SetActive(true);
            EventSystem.current?.SetSelectedGameObject(_signField.gameObject);
            _signField.ActivateInputField();
        }

        public void CloseSign()
        {
            if (!SignOpen) return;
            _signPanel.SetActive(false);
            _signDone?.Invoke(Core.Containers.CleanSign(_signField.text));
            _signDone = null;
        }
    }
}
