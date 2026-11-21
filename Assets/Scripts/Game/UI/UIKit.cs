using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Stratum.Game
{
    // Builds the interface in code: panels with rounded corners, labels, buttons, sliders and fields in one style.
    public static class UIKit
    {
        public static readonly Color Ink = new Color(0.93f, 0.90f, 0.85f);
        public static readonly Color Dim = new Color(0.66f, 0.64f, 0.60f);
        public static readonly Color Accent = new Color(0.96f, 0.71f, 0.29f);
        public static readonly Color PanelCol = new Color(0.07f, 0.08f, 0.10f, 0.95f);
        public static readonly Color Slot = new Color(0.16f, 0.17f, 0.20f, 0.92f);
        public static readonly Color Good = new Color(0.55f, 0.85f, 0.45f);
        public static readonly Color Bad = new Color(0.95f, 0.42f, 0.38f);

        // Lato ships with the game (Resources/Fonts, SIL OFL) so text never depends on the fonts a machine has
        // installed; each style is its own face rather than Unity's faked bold and slant.
        static Font _regular, _bold, _italic, _boldItalic;
        static Font Load(string name) => Resources.Load<Font>("Fonts/" + name) ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        public static Font Font => _regular ??= Load("Lato-Regular");
        public static Font FontFor(FontStyle style) => style switch
        {
            FontStyle.Bold => _bold ??= Load("Lato-Bold"),
            FontStyle.Italic => _italic ??= Load("Lato-Italic"),
            FontStyle.BoldAndItalic => _boldItalic ??= Load("Lato-BoldItalic"),
            _ => Font,
        };
        static Sprite _round, _roundSmall, _white;

        public static Sprite Round => _round ??= RoundSprite(64, 18);
        public static Sprite Circle => _circle ??= RoundSprite(64, 32);
        static Sprite _circle;
        public static Sprite RoundSmall => _roundSmall ??= RoundSprite(32, 8);
        public static Sprite White
        {
            get
            {
                if (_white != null) return _white;
                var t = new Texture2D(4, 4); var px = new Color[16]; for (int i = 0; i < 16; i++) px[i] = Color.white; t.SetPixels(px); t.Apply();
                return _white = Sprite.Create(t, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            }
        }

        static Sprite RoundSprite(int size, int r)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, r, size - r), cy = Mathf.Clamp(y + 0.5f, r, size - r);
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                    px[x + y * size] = new Color(1, 1, 1, Mathf.Clamp01(r - d + 0.5f));
                }
            t.SetPixels(px); t.Apply();
            return Sprite.Create(t, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
        }

        // Every canvas follows the UI size setting (a bigger number draws everything bigger).
        static readonly List<CanvasScaler> _scalers = new List<CanvasScaler>();
        public static void ApplyScale()
        {
            _scalers.RemoveAll(s => s == null);
            foreach (var s in _scalers) s.referenceResolution = new Vector2(1920, 1080) / Settings.UiScale;
        }

        public static Canvas Root(string name, int order)
        {
            var go = new GameObject(name);
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = order;
            var s = go.AddComponent<CanvasScaler>();
            s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            s.referenceResolution = new Vector2(1920, 1080) / Settings.UiScale;
            _scalers.Add(s);
            s.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            return c;
        }

        public static void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
            UnityEngine.Object.DontDestroyOnLoad(go);
        }

        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        // Anchor to a point of the parent (0..1) and size in pixels at the reference resolution.
        public static RectTransform Place(this RectTransform r, float ax, float ay, float x, float y, float w, float h)
        {
            r.anchorMin = r.anchorMax = new Vector2(ax, ay);
            r.pivot = new Vector2(ax, ay);
            r.anchoredPosition = new Vector2(x, y);
            r.sizeDelta = new Vector2(w, h);
            return r;
        }

        public static RectTransform Fill(this RectTransform r, float inset = 0)
        {
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(inset, inset); r.offsetMax = new Vector2(-inset, -inset);
            return r;
        }

        public static Image Panel(Transform parent, string name, Color c, Sprite sprite = null)
        {
            var r = Rect(parent, name);
            var img = r.gameObject.AddComponent<Image>();
            img.sprite = sprite ?? Round;
            img.type = Image.Type.Sliced;
            img.color = c;
            return img;
        }

        public static Text Label(Transform parent, string text, int size, Color c, TextAnchor align = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal)
        {
            var r = Rect(parent, "Label");
            var t = r.gameObject.AddComponent<Text>();
            t.font = FontFor(style); t.text = text; t.fontSize = size; t.color = c; t.alignment = align; t.fontStyle = FontStyle.Normal;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        public static Text Shadowed(this Text t, float d = 2)
        {
            var s = t.gameObject.AddComponent<Shadow>();
            s.effectColor = new Color(0, 0, 0, 0.7f); s.effectDistance = new Vector2(d, -d);
            return t;
        }

        public static Button Button(Transform parent, string text, Action onClick, int size = 30, bool primary = false)
        {
            var img = Panel(parent, "Button " + text, primary ? new Color(Accent.r / 0.9f, Accent.g / 0.9f, Accent.b / 0.9f) : new Color(0.22f, 0.23f, 0.28f, 0.95f));
            var b = img.gameObject.AddComponent<Button>();
            var cb = b.colors;
            cb.normalColor = new Color(0.9f, 0.9f, 0.9f); cb.highlightedColor = Color.white; cb.pressedColor = new Color(0.72f, 0.72f, 0.72f);
            cb.selectedColor = new Color(0.9f, 0.9f, 0.9f); cb.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.6f); cb.fadeDuration = 0.08f;
            cb.colorMultiplier = 1f;
            b.colors = cb;
            b.onClick.AddListener(() => { Sfx.I?.Play("click", 0.5f); onClick?.Invoke(); });
            var l = Label(img.transform, text, size, primary ? new Color(0.12f, 0.08f, 0.04f) : Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            l.rectTransform.Fill();
            return b;
        }

        public static void SetText(this Button b, string s) => b.GetComponentInChildren<Text>().text = s;

        public static Slider Slider(Transform parent, float min, float max, float value, bool whole, Action<float> changed)
        {
            var r = Rect(parent, "Slider");
            var s = r.gameObject.AddComponent<Slider>();
            var bg = Panel(r, "Track", new Color(0.18f, 0.19f, 0.22f, 1), RoundSmall);
            bg.rectTransform.anchorMin = new Vector2(0, 0.35f); bg.rectTransform.anchorMax = new Vector2(1, 0.65f); bg.rectTransform.offsetMin = bg.rectTransform.offsetMax = Vector2.zero;
            var fillArea = Rect(r, "Fill Area"); fillArea.anchorMin = new Vector2(0, 0.35f); fillArea.anchorMax = new Vector2(1, 0.65f); fillArea.offsetMin = new Vector2(4, 0); fillArea.offsetMax = new Vector2(-4, 0);
            var fill = Panel(fillArea, "Fill", Accent, RoundSmall); fill.rectTransform.Fill();
            var handleArea = Rect(r, "Handle Area"); handleArea.Fill(); handleArea.offsetMin = new Vector2(10, 0); handleArea.offsetMax = new Vector2(-10, 0);
            var handle = Panel(handleArea, "Handle", Ink, RoundSmall); handle.rectTransform.sizeDelta = new Vector2(22, 0);
            handle.rectTransform.anchorMin = new Vector2(0, 0); handle.rectTransform.anchorMax = new Vector2(0, 1);
            s.fillRect = fill.rectTransform; s.handleRect = handle.rectTransform; s.targetGraphic = handle;
            s.minValue = min; s.maxValue = max; s.wholeNumbers = whole; s.value = value;
            s.onValueChanged.AddListener(v => changed?.Invoke(v));
            return s;
        }

        public static InputField Field(Transform parent, string placeholder, string value, int size = 28)
        {
            var img = Panel(parent, "Field", new Color(0.13f, 0.14f, 0.17f, 1), RoundSmall);
            var f = img.gameObject.AddComponent<InputField>();
            var text = Label(img.transform, "", size, Ink);
            text.rectTransform.Fill(); text.rectTransform.offsetMin = new Vector2(16, 4); text.rectTransform.offsetMax = new Vector2(-16, -4);
            text.supportRichText = false;
            var ph = Label(img.transform, placeholder, size, new Color(Dim.r, Dim.g, Dim.b, 0.6f), TextAnchor.MiddleLeft, FontStyle.Italic);
            ph.rectTransform.Fill(); ph.rectTransform.offsetMin = new Vector2(16, 4); ph.rectTransform.offsetMax = new Vector2(-16, -4);
            f.textComponent = text; f.placeholder = ph; f.text = value; f.caretColor = Accent; f.selectionColor = new Color(Accent.r, Accent.g, Accent.b, 0.35f);
            return f;
        }

        public static Image Icon(Transform parent, Sprite s)
        {
            var r = Rect(parent, "Icon");
            var i = r.gameObject.AddComponent<Image>();
            i.sprite = s; i.preserveAspect = true; i.raycastTarget = false;
            i.enabled = s != null;
            return i;
        }

        public static ScrollRect Scroll(Transform parent, out RectTransform content)
        {
            var r = Rect(parent, "Scroll");
            var sr = r.gameObject.AddComponent<ScrollRect>();
            var vp = Rect(r, "Viewport"); vp.Fill();
            vp.gameObject.AddComponent<RectMask2D>();
            var img = vp.gameObject.AddComponent<Image>(); img.color = new Color(0, 0, 0, 0.001f);
            content = Rect(vp, "Content");
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1);
            content.offsetMin = content.offsetMax = Vector2.zero;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>(); fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.viewport = vp; sr.content = content; sr.horizontal = false; sr.scrollSensitivity = 40; sr.movementType = ScrollRect.MovementType.Clamped;
            return sr;
        }
    }

    // A slot that reports left and right clicks and hovering.
    // Hover reports for something that isn't a slot (a picture in a trade offer).
    public sealed class SlotHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Action<bool> Hover;
        public void OnPointerEnter(PointerEventData e) => Hover?.Invoke(true);
        public void OnPointerExit(PointerEventData e) => Hover?.Invoke(false);
    }

    public sealed class SlotView : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public Image Back, Icon, Frame; public Text Count; public Image Wear;
        public Action<PointerEventData.InputButton> Click;
        public Action<bool> Hover;
        public void OnPointerClick(PointerEventData e) => Click?.Invoke(e.button);
        public void OnPointerEnter(PointerEventData e) { Hover?.Invoke(true); if (Back) Back.color = new Color(0.26f, 0.27f, 0.31f, 0.95f); }
        public void OnPointerExit(PointerEventData e) { Hover?.Invoke(false); if (Back) Back.color = UIKit.Slot; }

        public static SlotView Make(Transform parent, float size)
        {
            var bg = UIKit.Panel(parent, "Slot", UIKit.Slot, UIKit.RoundSmall);
            bg.rectTransform.sizeDelta = new Vector2(size, size);
            var v = bg.gameObject.AddComponent<SlotView>();
            v.Back = bg;
            v.Icon = UIKit.Icon(bg.transform, null); v.Icon.rectTransform.Fill(size * 0.12f);
            v.Count = UIKit.Label(bg.transform, "", (int)(size * 0.3f), Color.white, TextAnchor.LowerRight, FontStyle.Bold).Shadowed(2);
            v.Count.rectTransform.Fill(4);
            var wear = UIKit.Panel(bg.transform, "Wear", UIKit.Good, UIKit.White);
            wear.rectTransform.anchorMin = new Vector2(0.15f, 0.08f); wear.rectTransform.anchorMax = new Vector2(0.85f, 0.08f);
            wear.rectTransform.offsetMin = new Vector2(0, 0); wear.rectTransform.offsetMax = new Vector2(0, 5);
            v.Wear = wear; wear.gameObject.SetActive(false);
            return v;
        }

        public void Show(Core.Stack s)
        {
            var sp = s.Empty ? null : Icons.Get(s.Item);
            Icon.sprite = sp; Icon.enabled = sp != null;
            Count.text = !s.Empty && s.Count > 1 ? s.Count.ToString() : "";
            var d = s.Empty ? null : Core.Items.Get(s.Item);
            bool worn = d != null && d.Durability > 0 && s.Wear > 0;
            Wear.gameObject.SetActive(worn);
            if (worn)
            {
                float k = 1 - s.Wear / (float)d.Durability;
                Wear.rectTransform.anchorMax = new Vector2(0.15f + 0.7f * k, 0.08f);
                Wear.color = Color.Lerp(UIKit.Bad, UIKit.Good, k);
            }
        }
    }
}
