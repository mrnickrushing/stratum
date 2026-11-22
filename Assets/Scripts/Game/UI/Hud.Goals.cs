using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Stratum.Game
{
    // Goals to tick off (G), and who is here (hold Tab).
    public sealed partial class Hud
    {
        public static readonly (string id, string title, string how)[] Goals =
        {
            ("wood", "Timber!", "Cut a log from a tree"),
            ("bench", "A Place to Work", "Make a workbench"),
            ("pick", "Dig In", "Make a wooden pickaxe"),
            ("stone", "Stone Age", "Make a stone pickaxe"),
            ("furnace", "Hot Stuff", "Make a furnace"),
            ("iron", "Iron in the Fire", "Smelt an iron ingot"),
            ("bread", "Daily Bread", "Bake a loaf of bread"),
            ("bed", "Sweet Dreams", "Sleep in a bed"),
            ("fish", "Catch of the Day", "Catch a fish"),
            ("armour", "Dressed for It", "Wear armour on head, body, legs and feet"),
            ("shield", "Shields Up", "Hold a shield in your other hand"),
            ("hound", "Best Friend", "Make friends with a hound"),
            ("camel", "Ship of the Desert", "Ride a camel"),
            ("keg", "Handle with Care", "Make a blast keg"),
            ("piston", "Moving Parts", "Make a piston"),
            ("ember", "Into the Fire", "Step into the Ember Deep"),
            ("potion", "Something's Brewing", "Mix a potion"),
            ("star", "Star Struck", "Dig up a starstone"),
            ("level", "Seasoned", "Reach level 15"),
            ("heart", "Giant Slayer", "Take the heart of the Cinder Colossus"),
            ("quiet", "Past the Edge", "Touch a gate stone and stand in the Quiet"),
            ("crown", "Long Live the King", "Take the Hollow King's crown"),
        };

        Image _goalsPanel, _whoPanel;
        Text _goalsText, _goalsText2, _goalsTitle, _whoText;
        public bool GoalsOpen => _goalsPanel != null && _goalsPanel.gameObject.activeSelf;
        // Panels that show and hide themselves (the HUD's own sweep leaves them alone).
        bool Own(Transform t) => _goalsPanel != null && (t == _goalsPanel.transform || t == _whoPanel.transform);

        void BuildGoals()
        {
            _goalsPanel = UIKit.Panel(_root, "Goals", new Color(0.07f, 0.07f, 0.09f, 0.98f));
            _goalsPanel.rectTransform.Place(0.5f, 0.5f, 0, 20, 1180, 760);
            _goalsTitle = UIKit.Label(_goalsPanel.transform, "Goals", 40, Color.white, TextAnchor.UpperCenter, FontStyle.Bold);
            _goalsTitle.rectTransform.Place(0.5f, 1, 0, -22, 1100, 54);
            _goalsText = UIKit.Label(_goalsPanel.transform, "", 23, Color.white, TextAnchor.UpperLeft);
            _goalsText.rectTransform.Place(0, 1, 40, -96, 540, 640); _goalsText.supportRichText = true;
            _goalsText2 = UIKit.Label(_goalsPanel.transform, "", 23, Color.white, TextAnchor.UpperLeft);
            _goalsText2.rectTransform.Place(0, 1, 610, -96, 540, 640); _goalsText2.supportRichText = true;
            var hint = UIKit.Label(_goalsPanel.transform, "G turns the page (goals, statistics); Esc closes", 20, UIKit.Dim, TextAnchor.LowerCenter);
            hint.rectTransform.Place(0.5f, 0, 0, 14, 600, 30);
            _goalsPanel.transform.SetAsLastSibling();
            _goalsPanel.gameObject.SetActive(false);

            _whoPanel = UIKit.Panel(_root, "Who", new Color(0.05f, 0.05f, 0.07f, 0.85f));
            _whoPanel.rectTransform.Place(0.5f, 1, 0, -90, 460, 300);
            _whoPanel.raycastTarget = false;
            _whoText = UIKit.Label(_whoPanel.transform, "", 26, Color.white, TextAnchor.UpperCenter);
            _whoText.rectTransform.Fill(); _whoText.supportRichText = true; _whoText.raycastTarget = false;
            _whoPanel.gameObject.SetActive(false);
        }

        public void ShowGoals(bool on, HashSet<string> done)
        {
            if (_goalsPanel == null) BuildGoals();
            _goalsPanel.gameObject.SetActive(on);
            if (!on) return;
            var a = new System.Text.StringBuilder(); var b = new System.Text.StringBuilder(); int n = 0;
            for (int i = 0; i < Goals.Length; i++)
            {
                var (id, title, how) = Goals[i]; bool got = done.Contains(id); if (got) n++;
                var sb = i < (Goals.Length + 1) / 2 ? a : b;
                sb.Append(got ? "<color=#8fe06a>✓  <b>" : "<color=#9a9aa2>○  <b>").Append(title).Append("</b></color>\n<color=#").Append(got ? "c8d8c0" : "9a9aa4").Append(">      ").Append(how).Append("</color>\n");
            }
            _goalsText.text = a.ToString(); _goalsText2.text = b.ToString();
            _goalsTitle.text = $"Goals  {n} of {Goals.Length}";
        }

        public void ShowStats(string text)
        {
            if (_goalsPanel == null) BuildGoals();
            _goalsPanel.gameObject.SetActive(true);
            _goalsTitle.text = "Statistics";
            _goalsText.text = text; _goalsText2.text = "";
        }

        // Who is in this world, while Tab is held.
        public void ShowWho(bool on, string me, IEnumerable<string> others)
        {
            if (_whoPanel == null) BuildGoals();
            if (_whoPanel.gameObject.activeSelf != on) _whoPanel.gameObject.SetActive(on);
            if (!on) return;
            var sb = new System.Text.StringBuilder(); int n = 1;
            sb.Append("<b>Here now</b>\n<color=#ffe9a8>").Append(string.IsNullOrEmpty(me) ? "You" : me).Append("</color>\n");
            if (others != null) foreach (var o in others) { sb.Append(o).Append('\n'); n++; }
            _whoText.text = sb.ToString();
            _whoPanel.rectTransform.sizeDelta = new Vector2(460, 62 + n * 34);
        }
    }
}
