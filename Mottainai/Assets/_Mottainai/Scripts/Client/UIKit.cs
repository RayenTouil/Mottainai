using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Mottainai.Client
{
    /// Code-only uGUI/TMP builder (no prefabs).
    public static class UIKit
    {
        public static readonly Color Dark = new Color(0.06f, 0.12f, 0.16f, 0.92f);
        public static readonly Color Panel = new Color(0.08f, 0.2f, 0.24f, 0.88f);
        public static readonly Color Accent = new Color(0.18f, 0.72f, 0.45f);
        public static readonly Color Accent2 = new Color(0.12f, 0.5f, 0.85f);
        public static readonly Color Warn = new Color(0.9f, 0.35f, 0.2f);
        public static readonly Color Muted = new Color(0.35f, 0.42f, 0.46f);
        public static readonly Color TextLight = new Color(0.95f, 0.98f, 0.96f);

        static Sprite _star;
        public static Sprite Star => _star != null ? _star : (_star = MakeStar(128));
        static Sprite _rounded, _circle;
        public static Sprite Rounded => _rounded != null ? _rounded : (_rounded = MakeRounded(64, 18));
        public static Sprite Circle => _circle != null ? _circle : (_circle = MakeCircle(128));

        static Sprite MakeRounded(int size, int radius)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, radius, size - radius), cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                    float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                    byte a = (byte)(Mathf.Clamp01(radius - d + 0.5f) * 255);
                    px[y * size + x] = new Color32(255, 255, 255, a);
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        }

        static Sprite MakeStar(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            var poly = new Vector2[10];
            for (int i = 0; i < 10; i++)
            {
                float a = Mathf.PI * 0.5f + i * Mathf.PI / 5f;
                float r = (i % 2 == 0 ? 0.48f : 0.2f) * size;
                poly[i] = new Vector2(size * 0.5f + Mathf.Cos(a) * r, size * 0.52f + Mathf.Sin(a) * r);
            }
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < 3; sy++)
                        for (int sx = 0; sx < 3; sx++)
                            if (Inside(poly, new Vector2(x + (sx + 0.5f) / 3f, y + (sy + 0.5f) / 3f))) hits++;
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(hits * 255 / 9));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        static bool Inside(Vector2[] poly, Vector2 p)
        {
            bool c = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                if (((poly[i].y > p.y) != (poly[j].y > p.y)) && (p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)) c = !c;
            return c;
        }

        static Sprite MakeCircle(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - r) * (x + 0.5f - r) + (y + 0.5f - r) * (y + 0.5f - r));
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(r - d) * 255));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        public static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            var go = new GameObject("EventSystem");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }

        public static Canvas Canvas(string name, int order)
        {
            var go = new GameObject(name);
            UnityEngine.Object.DontDestroyOnLoad(go);
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = order;
            var s = go.AddComponent<CanvasScaler>();
            s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            s.referenceResolution = new Vector2(1920, 1080);
            s.matchWidthOrHeight = 1f;
            go.AddComponent<GraphicRaycaster>();
            return c;
        }

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static RectTransform Stretch(RectTransform rt, float l = 0, float t = 0, float r = 0, float b = 0)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(l, b); rt.offsetMax = new Vector2(-r, -t);
            return rt;
        }

        public static RectTransform Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public static Image Image(Transform parent, Color c, Sprite s = null, string name = "Image")
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = s ?? Rounded;
            img.type = s == null || s == Rounded ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
            img.color = c;
            return img;
        }

        public static TextMeshProUGUI Text(Transform parent, string text, float size, Color c, TextAlignmentOptions align = TextAlignmentOptions.Center, string name = "Text")
        {
            var rt = Rect(name, parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.color = c;
            t.alignment = align;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.Normal;
            return t;
        }

        public static Button Button(Transform parent, string label, Color bg, Action onClick, float fontSize = 40, string name = null)
        {
            var img = Image(parent, bg, null, name ?? ("Btn_" + label));
            var b = img.gameObject.AddComponent<Button>();
            var colors = b.colors;
            colors.highlightedColor = new Color(1.1f, 1.1f, 1.1f);
            colors.pressedColor = new Color(0.75f, 0.75f, 0.75f);
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);
            b.colors = colors;
            var t = Text(img.transform, label, fontSize, Color.white);
            t.fontStyle = FontStyles.Bold;
            Stretch(t.rectTransform, 12, 4, 12, 4);
            if (onClick != null) b.onClick.AddListener(() => onClick());
            return b;
        }

        public static TMP_Text Label(Button b) => b.GetComponentInChildren<TMP_Text>();

        public static TMP_InputField Input(Transform parent, string placeholder, string value, float fontSize = 40)
        {
            var bg = Image(parent, new Color(1f, 1f, 1f, 0.95f), null, "Input");
            var area = Rect("TextArea", bg.transform);
            Stretch(area, 20, 6, 20, 6);
            area.gameObject.AddComponent<RectMask2D>();
            var ph = Text(area, placeholder, fontSize, new Color(0.45f, 0.5f, 0.55f), TextAlignmentOptions.Left, "Placeholder");
            ph.fontStyle = FontStyles.Italic;
            ph.textWrappingMode = TextWrappingModes.NoWrap;
            Stretch(ph.rectTransform);
            var tx = Text(area, "", fontSize, new Color(0.08f, 0.12f, 0.15f), TextAlignmentOptions.Left, "Text");
            tx.textWrappingMode = TextWrappingModes.NoWrap;
            Stretch(tx.rectTransform);
            var input = bg.gameObject.AddComponent<TMP_InputField>();
            input.textViewport = area;
            input.textComponent = tx;
            input.placeholder = ph;
            input.pointSize = fontSize;
            input.text = value ?? "";
            return input;
        }

        public static VerticalLayoutGroup VLayout(Component c, float spacing, int pad, TextAnchor align = TextAnchor.UpperCenter)
        {
            var v = c.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = new RectOffset(pad, pad, pad, pad);
            v.childAlignment = align;
            v.childControlHeight = true; v.childControlWidth = true;
            v.childForceExpandHeight = false; v.childForceExpandWidth = true;
            return v;
        }

        public static HorizontalLayoutGroup HLayout(Component c, float spacing, int pad, TextAnchor align = TextAnchor.MiddleCenter)
        {
            var h = c.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.padding = new RectOffset(pad, pad, pad, pad);
            h.childAlignment = align;
            h.childControlHeight = true; h.childControlWidth = true;
            h.childForceExpandHeight = true; h.childForceExpandWidth = true;
            return h;
        }

        public static T Size<T>(T c, float h = -1, float w = -1, float flexW = -1) where T : Component
        {
            if (!c.gameObject.TryGetComponent<LayoutElement>(out var le)) le = c.gameObject.AddComponent<LayoutElement>();
            if (h >= 0) { le.preferredHeight = h; le.minHeight = h; }
            if (w >= 0) { le.preferredWidth = w; le.minWidth = w; }
            if (flexW >= 0) le.flexibleWidth = flexW;
            return c;
        }

        public static string Hex(Color32 c) => $"#{c.r:X2}{c.g:X2}{c.b:X2}";
    }
}
