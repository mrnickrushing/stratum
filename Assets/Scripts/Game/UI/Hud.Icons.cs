using UnityEngine;
using UnityEngine.UI;

namespace Stratum.Game
{
    // Health, hunger, armour and air as rows of ten little pictures (each worth two points) in place of plain bars.
    public sealed partial class Hud
    {
        sealed class IconRow
        {
            readonly Image[] _cells = new Image[10];
            readonly Sprite _full, _half, _empty;
            readonly bool _fromRight;
            int _last = -1;

            public IconRow(Image back, Image fill, Sprite full, Sprite half, Sprite empty, bool fromRight)
            {
                _full = full; _half = half; _empty = empty; _fromRight = fromRight;
                back.enabled = false; fill.enabled = false;
                float w = back.rectTransform.sizeDelta.x, size = Mathf.Min(24, w / 10f);
                for (int i = 0; i < 10; i++)
                {
                    var go = new GameObject("Icon", typeof(RectTransform));
                    go.transform.SetParent(back.transform, false);
                    var img = go.AddComponent<Image>(); img.raycastTarget = false; img.sprite = empty;
                    var rt = img.rectTransform;
                    rt.anchorMin = rt.anchorMax = new Vector2(fromRight ? 1 : 0, 0.5f);
                    rt.pivot = new Vector2(fromRight ? 1 : 0, 0.5f);
                    rt.sizeDelta = new Vector2(size, size);
                    rt.anchoredPosition = new Vector2((fromRight ? -1 : 1) * i * (w / 10f), 0);
                    _cells[i] = img;
                }
            }

            // points: 0..20.
            public void Set(float points, bool shake = false)
            {
                int p = Mathf.Clamp(Mathf.CeilToInt(points - 0.001f), 0, 20);
                if (p != _last)
                {
                    _last = p;
                    for (int i = 0; i < 10; i++) _cells[i].sprite = p >= i * 2 + 2 ? _full : p == i * 2 + 1 ? _half : _empty;
                }
                for (int i = 0; i < 10; i++)
                {
                    var rt = _cells[i].rectTransform;
                    float y = shake ? Mathf.Sin(Time.unscaledTime * 22 + i * 1.7f) * 2f : 0;
                    if (Mathf.Abs(rt.anchoredPosition.y - y) > 0.01f) rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, y);
                }
            }
        }

        IconRow _hearts, _meals, _plates, _bubbles;

        // Nine by nine pixel pictures, drawn here: '#' the colour, '+' its highlight, '-' its shade, 'o' the outline.
        static Sprite Pixel(string[] rows, Color main, bool half = false, bool empty = false)
        {
            int n = rows.Length;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[n * n];
            Color hollow = new Color(0.16f, 0.16f, 0.18f, 0.85f);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    char ch = rows[y][x];
                    Color c = Color.clear;
                    if (ch == 'o') c = new Color(0.07f, 0.06f, 0.07f, 1);
                    else if (ch != '.')
                    {
                        bool lit = !empty && (!half || x < n / 2);
                        c = !lit ? hollow : ch == '+' ? Color.Lerp(main, Color.white, 0.55f) : ch == '-' ? main * 0.7f : main;
                        c.a = lit ? 1 : hollow.a;
                    }
                    px[x + (n - 1 - y) * n] = c;
                }
            t.SetPixels(px); t.Apply();
            return Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
        }

        static readonly string[] HeartRows =
        {
            ".oo...oo.",
            "o+#o.o##o",
            "o+####-#o",
            "o######-o",
            "o#####--o",
            ".o###--o.",
            "..o#--o..",
            "...o-o...",
            "....o....",
        };
        static readonly string[] LoafRows =
        {
            ".........",
            "..ooooo..",
            ".o+++##o.",
            "o+#####-o",
            "o##-#-#-o",
            "o#######o",
            "o------no".Replace('n', '-'),
            ".ooooooo.",
            ".........",
        };
        static readonly string[] PlateRows =
        {
            "ooooooooo",
            "o+++####o",
            "o+#####-o",
            "o######-o",
            "o#####--o",
            ".o###--o.",
            ".o##--o..".Substring(0, 9),
            "..o#-o...",
            "...ooo...",
        };
        static readonly string[] BubbleRows =
        {
            "...ooo...",
            "..o###o..",
            ".o#++##o.",
            "o#+####-o",
            "o#+####-o",
            "o######-o",
            ".o####-o.",
            "..o---o..",
            "...ooo...",
        };

        void BuildIconRows()
        {
            Color red = new Color(0.9f, 0.16f, 0.18f), bread = new Color(0.84f, 0.56f, 0.24f), steel = new Color(0.78f, 0.8f, 0.86f), blue = new Color(0.5f, 0.78f, 1f);
            _hearts = new IconRow(_healthBack, _health, Pixel(HeartRows, red), Pixel(HeartRows, red, true), Pixel(HeartRows, red, false, true), false);
            _meals = new IconRow(_foodBack, _food, Pixel(LoafRows, bread), Pixel(LoafRows, bread, true), Pixel(LoafRows, bread, false, true), true);
            _plates = new IconRow(_armourBack, _armour, Pixel(PlateRows, steel), Pixel(PlateRows, steel, true), Pixel(PlateRows, steel, false, true), false);
            _bubbles = new IconRow(_airBack, _air, Pixel(BubbleRows, blue), Pixel(BubbleRows, blue, true), Pixel(BubbleRows, blue, false, true), true);
        }

        Text _fxText;
        readonly System.Text.StringBuilder _fxLine = new System.Text.StringBuilder();

        // What's working in the body just now, down the left side.
        void ShowEffects()
        {
            if (_fxText == null)
            {
                _fxText = UIKit.Label(_root, "", 22, Color.white, TextAnchor.UpperLeft).Shadowed();
                _fxText.rectTransform.Place(0, 0.5f, 24, 60, 420, 300);
                _fxText.raycastTarget = false; _fxText.supportRichText = true;
            }
            _fxLine.Clear();
            for (int i = 0; i < P.FxLeft.Length; i++)
            {
                float left = P.FxLeft[i];
                if (left <= 0) continue;
                string col = i == (int)Player.Fx.Poison ? "#9be06a" : "#ffe9a8";
                _fxLine.Append("<color=").Append(col).Append('>').Append(Player.FxNames[i]).Append("</color>  ").Append((int)left / 60).Append(':').Append(((int)left % 60).ToString("00")).Append('\n');
            }
            string s = _fxLine.ToString();
            if (_fxText.text != s) _fxText.text = s;
        }

        void ShowIconRows(int armour)
        {
            ShowEffects();
            _hearts.Set(P.Health / P.MaxHealth * 20, P.Health <= 4);
            _meals.Set(P.Food, P.Food <= 6);
            _plates.Set(armour);
            _bubbles.Set(P.Air / P.MaxAir * 20);
        }
    }
}
