using UnityEngine;
using UnityEngine.InputSystem;

namespace Stratum.Game
{
    // The keys for moving and the everyday actions, changeable from Settings > Controls and kept per computer.
    public enum Act { Forward, Back, Left, Right, Jump, Sprint, Sneak, Backpack, Chat, Talk, Drop, View }

    public static class Keys
    {
        public static readonly string[] Names = { "Walk forward", "Walk back", "Step left", "Step right", "Jump / fly up", "Sprint", "Sneak / fly down", "Backpack", "Chat", "Talk (voice chat)", "Drop what you hold", "Change view" };
        static readonly Key[] Defaults = { Key.W, Key.S, Key.A, Key.D, Key.Space, Key.LeftCtrl, Key.LeftShift, Key.E, Key.T, Key.V, Key.Q, Key.F5 };
        static Key[] _keys;

        static Key[] All
        {
            get
            {
                if (_keys != null) return _keys;
                _keys = new Key[Defaults.Length];
                for (int i = 0; i < _keys.Length; i++)
                {
                    int k = PlayerPrefs.GetInt("key_" + i, (int)Defaults[i]);
                    _keys[i] = k > 0 && k < (int)Key.IMESelected ? (Key)k : Defaults[i];
                }
                return _keys;
            }
        }

        public static Key Get(Act a) => All[(int)a];
        public static void Set(Act a, Key k) { All[(int)a] = k; PlayerPrefs.SetInt("key_" + (int)a, (int)k); PlayerPrefs.Save(); }
        public static void Reset() { for (int i = 0; i < Defaults.Length; i++) Set((Act)i, Defaults[i]); }
        public static bool IsDefault(Act a) => Get(a) == Defaults[(int)a];

        public static bool Down(Act a) { var kb = Keyboard.current; return kb != null && kb[Get(a)].isPressed; }
        public static bool Pressed(Act a) { var kb = Keyboard.current; return kb != null && kb[Get(a)].wasPressedThisFrame; }

        public static string Label(Act a) => Label(Get(a));
        public static string Label(Key k)
        {
            switch (k)
            {
                case Key.LeftShift: return "Left Shift"; case Key.RightShift: return "Right Shift";
                case Key.LeftCtrl: return "Left Ctrl"; case Key.RightCtrl: return "Right Ctrl";
                case Key.LeftAlt: return "Left Alt"; case Key.RightAlt: return "Right Alt";
                case Key.Space: return "Space"; case Key.Enter: return "Enter"; case Key.Tab: return "Tab";
                case Key.UpArrow: return "Up"; case Key.DownArrow: return "Down"; case Key.LeftArrow: return "Left"; case Key.RightArrow: return "Right";
            }
            string s = k.ToString();
            return s.StartsWith("Digit") ? s.Substring(5) : s.StartsWith("Numpad") ? "Numpad " + s.Substring(6) : s;
        }

        // The key going down this frame, for "press a key" (Escape is never offered: it backs out).
        public static Key Captured()
        {
            var kb = Keyboard.current;
            if (kb == null) return Key.None;
            foreach (var c in kb.allKeys) if (c != null && c.wasPressedThisFrame && c.keyCode != Key.Escape) return c.keyCode;
            return Key.None;
        }
    }
}
