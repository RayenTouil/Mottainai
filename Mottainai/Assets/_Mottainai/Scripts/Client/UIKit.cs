using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Mottainai.Client
{
    /// Code-only uGUI/TMP builder (no prefabs) styled from DesignTokens (ART_DIRECTION §2/§6):
    /// Fredoka (titles/buttons) + Nunito Sans (HUD/text), gradient buttons with soft shadow and 0.96 press scale,
    /// generated icon sprites (leaf, recycle, clock, menu, hand, star).
    public static class UIKit
    {
        static DesignTokens T => DesignTokens.Current;
        public static Color Primary => T.primary;
        public static Color Secondary => T.secondary;
        public static Color Utility => T.textDark;
        public static Color Reward => T.reward;
        public static Color Alert => T.alert;
        public static Color Cloud => T.panelLight;
        public static Color DeepTeal => T.textDark;
        public static Color PanelDark => new Color(T.textDark.r, T.textDark.g, T.textDark.b, 0.9f);

        // legacy names (kept for callers)
        public static Color Dark => PanelDark;
        public static Color Panel => PanelDark;
        public static Color Accent => Primary;
        public static Color Accent2 => Secondary;
        public static Color Warn => Alert;
        public static Color Muted => new Color(0.3f, 0.42f, 0.46f);
        public static readonly Color TextLight = new Color(0.97f, 0.99f, 0.97f);

        // ---------------- fonts ----------------
        static TMP_FontAsset title, body, bodyBold;
        static bool fontsTried;
        public static TMP_FontAsset TitleFont { get { InitFonts(); return title; } }
        public static TMP_FontAsset BodyFont { get { InitFonts(); return body; } }
        public static TMP_FontAsset BodyBold { get { InitFonts(); return bodyBold; } }

        static void InitFonts()
        {
            if (fontsTried) return;
            fontsTried = true;
            title = LoadFont("Fredoka-SemiBold");
            body = LoadFont("NunitoSans-Regular");
            bodyBold = LoadFont("NunitoSans-ExtraBold") ?? body;
            if (title == null || body == null) Debug.LogWarning("[UI] Fredoka/Nunito Sans missing -> default TMP font");
        }

        static TMP_FontAsset LoadFont(string res)
        {
            try
            {
                var f = Resources.Load<Font>("Fonts/" + res);
                if (f == null) return null;
                var fa = TMP_FontAsset.CreateFontAsset(f);
                if (fa == null) return null;
                fa.name = res;
                var def = TMP_Settings.defaultFontAsset;
                if (def != null) { fa.fallbackFontAssetTable ??= new List<TMP_FontAsset>(); fa.fallbackFontAssetTable.Add(def); }
                return fa;
            }
            catch (Exception e) { Debug.LogWarning("[UI] font " + res + ": " + e.Message); return null; }
        }

        // ---------------- sprites ----------------
        static Sprite _star, _rounded, _roundedL, _circle, _leaf, _recycle, _clock, _hand, _ring;
        public static Sprite Star => _star != null ? _star : (_star = Raster(128, p => InsidePoly(StarPoly(128), p) ? 1f : 0f, false));
        public static Sprite Rounded => _rounded != null ? _rounded : (_rounded = MakeRounded(64, 18));
        public static Sprite RoundedL => _roundedL != null ? _roundedL : (_roundedL = MakeRounded(96, 32));
        public static Sprite Circle => _circle != null ? _circle : (_circle = Raster(128, p => Mathf.Clamp01(64f - (p - new Vector2(64, 64)).magnitude), false));
        public static Sprite RingSprite => _ring != null ? _ring : (_ring = Raster(128, p => { float d = (p - new Vector2(64, 64)).magnitude; return Mathf.Clamp01(62f - d) * Mathf.Clamp01(d - 50f); }, false));
        /// Leaf: vesica with a center vein, tip up-right.
        public static Sprite Leaf => _leaf != null ? _leaf : (_leaf = Raster(128, p =>
        {
            var q = Rot(p - new Vector2(64, 64), -45f);
            float a = 1f - Mathf.Pow(q.y / 58f, 2f);
            float w = a > 0f ? 30f * a : -1f;
            float inside = Mathf.Clamp01(w - Mathf.Abs(q.x) + 0.5f);
            float vein = Mathf.Abs(q.x) < 2.2f && Mathf.Abs(q.y) < 44f ? 0.45f : 1f;
            return inside * vein;
        }, false));
        /// Three chasing arrows (rasterized from the 3D recycle icon geometry).
        public static Sprite Recycle => _recycle != null ? _recycle : (_recycle = MakeRecycle(128));
        public static Sprite Clock => _clock != null ? _clock : (_clock = Raster(96, p =>
        {
            var d = p - new Vector2(48, 48); float r = d.magnitude;
            float ring = Mathf.Clamp01(44f - r) * Mathf.Clamp01(r - 35f);
            float hand1 = Mathf.Abs(d.x) < 3.5f && d.y > -2f && d.y < 26f ? 1f : 0f;
            float hand2 = Mathf.Abs(d.y) < 3.5f && d.x > -2f && d.x < 20f ? 1f : 0f;
            return Mathf.Max(ring, Mathf.Max(hand1, hand2));
        }, false));
        /// Pointing hand for the onboarding hint (finger up, palm below).
        public static Sprite Hand => _hand != null ? _hand : (_hand = Raster(128, p =>
        {
            float finger = RoundRect(p, new Vector2(52, 58), new Vector2(76, 122), 12f);
            float palm = RoundRect(p, new Vector2(34, 8), new Vector2(98, 74), 22f);
            float thumb = RoundRect(p, new Vector2(22, 36), new Vector2(46, 70), 11f);
            return Mathf.Max(finger, Mathf.Max(palm, thumb));
        }, false));

        static float RoundRect(Vector2 p, Vector2 min, Vector2 max, float r)
        {
            var c = new Vector2(Mathf.Clamp(p.x, min.x + r, max.x - r), Mathf.Clamp(p.y, min.y + r, max.y - r));
            return Mathf.Clamp01(r - (p - c).magnitude + 0.5f);
        }

        static Vector2 Rot(Vector2 v, float deg) { float a = deg * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a); return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c); }

        static Sprite Raster(int size, Func<Vector2, float> alpha, bool sliced)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float a = 0f;
                    for (int s = 0; s < 4; s++) a += alpha(new Vector2(x + (s % 2 == 0 ? 0.3f : 0.7f), y + (s < 2 ? 0.3f : 0.7f)));
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a * 0.25f) * 255));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        static Vector2[] StarPoly(int size)
        {
            var poly = new Vector2[10];
            for (int i = 0; i < 10; i++)
            {
                float a = Mathf.PI * 0.5f + i * Mathf.PI / 5f;
                float r = (i % 2 == 0 ? 0.48f : 0.2f) * size;
                poly[i] = new Vector2(size * 0.5f + Mathf.Cos(a) * r, size * 0.52f + Mathf.Sin(a) * r);
            }
            return poly;
        }

        static bool InsidePoly(Vector2[] poly, Vector2 p)
        {
            bool c = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                if (((poly[i].y > p.y) != (poly[j].y > p.y)) && (p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)) c = !c;
            return c;
        }

        static Sprite MakeRecycle(int size)
        {
            var m = ParkKit.RecycleIcon();
            var v = m.vertices; var t = m.triangles;
            var tris = new List<Vector2[]>();
            for (int i = 0; i < t.Length; i += 6) // every other triangle is the back face
                tris.Add(new[] { Map(v[t[i]], size), Map(v[t[i + 1]], size), Map(v[t[i + 2]], size) });
            return Raster(size, p => { foreach (var tr in tris) if (InTri(p, tr[0], tr[1], tr[2])) return 1f; return 0f; }, false);
        }

        static Vector2 Map(Vector3 v, int size) => new Vector2((v.x + 0.5f) * size * 0.92f + size * 0.04f, (v.y + 0.47f) * size * 0.92f + size * 0.04f);

        static bool InTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
            float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
            float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        static Sprite MakeRounded(int size, int radius)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, radius, size - radius), cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                    float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(radius - d + 0.5f) * 255));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        }

        static readonly Dictionary<long, Sprite> gradients = new Dictionary<long, Sprite>();

        /// Rounded 9-sliced sprite with a baked vertical gradient (top -> bottom); tint the Image white.
        public static Sprite Gradient(Color top, Color bottom, int radius = 26)
        {
            long key = ((Color32)top).GetHashCode() * 31L + ((Color32)bottom).GetHashCode() * 7L + radius;
            if (gradients.TryGetValue(key, out var sp)) return sp;
            int size = radius * 2 + 8;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                var c = Color.Lerp(bottom, top, (y + 0.5f) / size);
                for (int x = 0; x < size; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, radius, size - radius), cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                    float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                    px[y * size + x] = new Color(c.r, c.g, c.b, c.a * Mathf.Clamp01(radius - d + 0.5f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            sp = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
            gradients[key] = sp;
            return sp;
        }

        public static Color Lighten(Color c, float k) => Color.Lerp(c, Color.white, k);
        public static Color Darken(Color c, float k) => Color.Lerp(c, Color.black, k);

        // ---------------- builders ----------------
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
            img.type = s == null || s == Rounded || s == RoundedL ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
            img.color = c;
            img.raycastTarget = false;
            return img;
        }

        /// Rounded card with a soft drop shadow.
        public static Image Card(Transform parent, Color c, string name = "Card")
        {
            var img = Image(parent, c, RoundedL, name);
            var sh = img.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0.08f, 0.1f, 0.28f);
            sh.effectDistance = new Vector2(0, -8);
            return img;
        }

        public static TextMeshProUGUI Text(Transform parent, string text, float size, Color c, TextAlignmentOptions align = TextAlignmentOptions.Center, string name = "Text")
        {
            var rt = Rect(name, parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (BodyFont != null) t.font = BodyFont;
            t.text = text;
            t.fontSize = size;
            t.color = c;
            t.alignment = align;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.richText = true;
            return t;
        }

        /// Fredoka title text.
        public static TextMeshProUGUI Title(Transform parent, string text, float size, Color c, TextAlignmentOptions align = TextAlignmentOptions.Center, string name = "Title")
        {
            var t = Text(parent, text, size, c, align, name);
            if (TitleFont != null) t.font = TitleFont; else t.fontStyle = FontStyles.Bold;
            return t;
        }

        /// Nunito Sans ExtraBold (HUD numbers).
        public static TextMeshProUGUI Bold(Transform parent, string text, float size, Color c, TextAlignmentOptions align = TextAlignmentOptions.Center, string name = "Bold")
        {
            var t = Text(parent, text, size, c, align, name);
            if (BodyBold != null) t.font = BodyBold; else t.fontStyle = FontStyles.Bold;
            return t;
        }

        /// Gradient button (top lighter, bottom deeper) with shadow + press scale; label in Fredoka.
        public static Button Button(Transform parent, string label, Color bg, Action onClick, float fontSize = 40, string name = null)
            => Button(parent, label, Lighten(bg, 0.18f), Darken(bg, 0.08f), onClick, fontSize, name);

        public static Button Button(Transform parent, string label, Color top, Color bottom, Action onClick, float fontSize = 40, string name = null)
        {
            var rt = Rect(name ?? ("Btn_" + label), parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = Gradient(top, bottom);
            img.type = UnityEngine.UI.Image.Type.Sliced;
            img.color = Color.white;
            var sh = rt.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0.1f, 0.12f, 0.3f);
            sh.effectDistance = new Vector2(0, -6);
            var b = rt.gameObject.AddComponent<Button>();
            var colors = b.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.05f, 1.05f, 1.05f);
            colors.pressedColor = new Color(0.86f, 0.86f, 0.86f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.65f, 0.65f, 0.65f, 0.6f);
            colors.fadeDuration = 0.08f;
            b.colors = colors;
            rt.gameObject.AddComponent<PressScale>();
            var t = Title(rt, label, fontSize, Color.white);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            Stretch(t.rectTransform, 14, 4, 14, 6);
            if (onClick != null) b.onClick.AddListener(() => onClick());
            return b;
        }

        public static void Recolor(Button b, Color bg)
        {
            b.image.sprite = Gradient(Lighten(bg, 0.18f), Darken(bg, 0.08f));
        }

        public static TMP_Text Label(Button b) => b.GetComponentInChildren<TMP_Text>();

        /// Light input field; border turns Eco Green on focus.
        public static TMP_InputField Input(Transform parent, string placeholder, string value, float fontSize = 40)
        {
            var border = Image(parent, new Color(1f, 1f, 1f, 0.55f), RoundedL, "InputBorder");
            var bg = Image(border.transform, Cloud, RoundedL, "Input");
            Stretch(bg.rectTransform, 4, 4, 4, 4);
            var area = Rect("TextArea", bg.transform);
            Stretch(area, 26, 6, 26, 6);
            area.gameObject.AddComponent<RectMask2D>();
            var ph = Text(area, placeholder, fontSize, new Color(0.45f, 0.55f, 0.58f), TextAlignmentOptions.Left, "Placeholder");
            ph.fontStyle = FontStyles.Italic;
            ph.textWrappingMode = TextWrappingModes.NoWrap;
            Stretch(ph.rectTransform);
            var tx = Bold(area, "", fontSize, DeepTeal, TextAlignmentOptions.Left, "Text");
            tx.textWrappingMode = TextWrappingModes.NoWrap;
            Stretch(tx.rectTransform);
            bg.raycastTarget = true;
            var input = bg.gameObject.AddComponent<TMP_InputField>();
            input.textViewport = area;
            input.textComponent = tx;
            input.placeholder = ph;
            input.pointSize = fontSize;
            input.text = value ?? "";
            input.onSelect.AddListener(_ => border.color = Primary);
            input.onDeselect.AddListener(_ => border.color = new Color(1f, 1f, 1f, 0.55f));
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
            var go = c is TMP_InputField f && f.transform.parent != null && f.transform.parent.name == "InputBorder" ? f.transform.parent.gameObject : c.gameObject;
            if (!go.TryGetComponent<LayoutElement>(out var le)) le = go.AddComponent<LayoutElement>();
            if (h >= 0) { le.preferredHeight = h; le.minHeight = h; }
            if (w >= 0) { le.preferredWidth = w; le.minWidth = w; }
            if (flexW >= 0) le.flexibleWidth = flexW;
            return c;
        }

        public static string Hex(Color32 c) => $"#{c.r:X2}{c.g:X2}{c.b:X2}";
    }

    /// 0.96 press scale (DesignTokens.pressScale), eased back on release.
    public class PressScale : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        float target = 1f;
        public void OnPointerDown(PointerEventData e) => target = DesignTokens.Current.pressScale;
        public void OnPointerUp(PointerEventData e) => target = 1f;
        public void OnPointerExit(PointerEventData e) => target = 1f;
        void OnDisable() { target = 1f; transform.localScale = Vector3.one; }
        void Update()
        {
            float s = transform.localScale.x;
            if (Mathf.Abs(s - target) < 0.001f) return;
            s = Mathf.MoveTowards(s, target, Time.unscaledDeltaTime / Mathf.Max(0.05f, DesignTokens.Current.transition) * 0.5f);
            transform.localScale = new Vector3(s, s, 1f);
        }
    }
}
