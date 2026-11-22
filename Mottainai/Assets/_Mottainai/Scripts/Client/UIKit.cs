using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Mottainai.Client
{
    /// Press feedback for v3 pill buttons: face down 4 px, shadow band shrinks to 2 px.
    public class PressFace : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public RectTransform face;
        void Set(bool down) { if (face == null) return; face.offsetMin = new Vector2(face.offsetMin.x, down ? 2f : 6f); face.offsetMax = new Vector2(face.offsetMax.x, down ? -4f : 0f); }
        public void OnPointerDown(PointerEventData e) => Set(true);
        public void OnPointerUp(PointerEventData e) => Set(false);
        public void OnPointerExit(PointerEventData e) => Set(false);
    }

    /// Code-only uGUI/TMP builder (no prefabs).
    public static class UIKit
    {
        // v3 §9 UI tokens
        public static readonly Color Dark = new Color(0x1E / 255f, 0x2A / 255f, 0x34 / 255f, 0.92f);
        public static readonly Color Panel = new Color(0x1E / 255f, 0x2A / 255f, 0x34 / 255f, 0.8f);
        public static readonly Color Accent = new Color(0x4C / 255f, 0xC9 / 255f, 0x4C / 255f);
        public static readonly Color Accent2 = new Color(0x2F / 255f, 0x8B / 255f, 0xFF / 255f);
        public static readonly Color Warn = new Color(0xFF / 255f, 0x7A / 255f, 0x1A / 255f);

        static TMP_FontAsset font;
        static Material fontMat;
        static bool fontTried;

        /// Fredoka SemiBold (OFL) as a runtime TMP SDF asset with a shared 0.15 outline (#0E161C). Fallback: TMP default font.
        public static void ApplyFont(TMP_Text t)
        {
            if (!fontTried)
            {
                fontTried = true;
                try
                {
                    var ttf = Resources.Load<Font>("Fonts/Fredoka-SemiBold");
                    if (ttf != null) font = TMP_FontAsset.CreateFontAsset(ttf, 90, 9, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024);
                }
                catch (Exception e) { Debug.LogWarning("[UI] font: " + e.Message); }
                var baseFont = font != null ? font : TMP_Settings.defaultFontAsset;
                if (baseFont != null)
                {
                    fontMat = new Material(baseFont.material) { name = "UI_Outline" };
                    fontMat.EnableKeyword("OUTLINE_ON");
                    fontMat.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.15f);
                    fontMat.SetColor(ShaderUtilities.ID_OutlineColor, new Color32(0x0E, 0x16, 0x1C, 255));
                    fontMat.SetFloat(ShaderUtilities.ID_FaceDilate, 0.1f);
                }
            }
            if (font != null) t.font = font;
            if (fontMat != null) t.fontSharedMaterial = fontMat;
        }
        public static readonly Color Muted = new Color(0.35f, 0.42f, 0.46f);
        public static readonly Color TextLight = new Color(0.95f, 0.98f, 0.96f);

        static Sprite _star;
        public static Sprite Star => _star != null ? _star : (_star = MakeStar(128));
        static Sprite _rounded, _circle;
        public static Sprite Rounded => _rounded != null ? _rounded : (_rounded = MakeRounded(64, 24));
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
            ApplyFont(t);
            return t;
        }

        /// v3 chunky pill: root = shadow band color, child "Face" inset 6 px at the bottom; press moves the face down 4 px.
        public static Button Button(Transform parent, string label, Color bg, Action onClick, float fontSize = 40, string name = null)
        {
            Color shadow = bg == Accent ? new Color(0x2E / 255f, 0x8A / 255f, 0x2E / 255f) : bg == Accent2 ? new Color(0x1E / 255f, 0x5F / 255f, 0xB3 / 255f) : new Color(bg.r * 0.62f, bg.g * 0.62f, bg.b * 0.62f, bg.a);
            var img = Image(parent, shadow, null, name ?? ("Btn_" + label));
            var face = Image(img.transform, bg, null, "Face");
            face.raycastTarget = false;
            Stretch(face.rectTransform, 0, 0, 0, 6);
            var b = img.gameObject.AddComponent<Button>();
            b.targetGraphic = face;
            var colors = b.colors;
            colors.highlightedColor = new Color(1.06f, 1.06f, 1.06f);
            colors.pressedColor = new Color(0.92f, 0.92f, 0.92f);
            colors.disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.7f);
            b.colors = colors;
            img.gameObject.AddComponent<PressFace>().face = face.rectTransform;
            var t = Text(face.transform, label, fontSize, Color.white);
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
