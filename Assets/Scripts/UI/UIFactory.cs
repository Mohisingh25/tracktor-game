using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TractorSim
{
    /// <summary>Builds uGUI elements in code (fonts, panels, labels, buttons).</summary>
    public static class UIFactory
    {
        static Font font;
        public static Font DefaultFont
        {
            get
            {
                if (font == null)
                {
                    font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    if (font == null) font = Font.CreateDynamicFontFromOSFont("Arial", 32);
                }
                return font;
            }
        }

        public static readonly Color PanelColor = new Color(0.05f, 0.06f, 0.07f, 0.78f);
        public static readonly Color ButtonColor = new Color(0.12f, 0.13f, 0.15f, 0.72f);
        public static readonly Color Accent = new Color(0.95f, 0.62f, 0.12f);     // marigold
        public static readonly Color AccentGreen = new Color(0.25f, 0.7f, 0.3f);
        public static readonly Color Danger = new Color(0.85f, 0.2f, 0.15f);

        public static Canvas CreateCanvas(string name, int order)
        {
            var go = new GameObject(name);
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = order;
            var s = go.AddComponent<CanvasScaler>();
            s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            s.referenceResolution = new Vector2(1920, 1080);
            s.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            s.matchWidthOrHeight = 1f;
            go.AddComponent<GraphicRaycaster>();
            return c;
        }

        public static void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem");
            var es = go.AddComponent<EventSystem>();
            es.pixelDragThreshold = 4;
            go.AddComponent<StandaloneInputModule>();
        }

        public static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, Vector2? pivot = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot ?? new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public static RectTransform Stretch(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        public static Image Img(RectTransform rt, Color c, Sprite sprite = null, bool raycast = false)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.color = c;
            img.sprite = sprite;
            if (sprite != null && sprite.border != Vector4.zero) img.type = Image.Type.Sliced;
            img.raycastTarget = raycast;
            return img;
        }

        public static Text Label(Transform parent, string text, int size, Vector2 anchor, Vector2 pos, Vector2 box, TextAnchor align = TextAnchor.MiddleCenter, Color? color = null, FontStyle style = FontStyle.Normal)
        {
            var rt = Rect(parent, "Text", anchor, pos, box);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = DefaultFont;
            t.text = text;
            t.fontSize = size;
            t.alignment = align;
            t.color = color ?? Color.white;
            t.fontStyle = style;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        public static Text FillLabel(RectTransform rt, string text, int size, Color? color = null, FontStyle style = FontStyle.Bold)
        {
            var l = Label(rt, text, size, new Vector2(0.5f, 0.5f), Vector2.zero, rt.sizeDelta, TextAnchor.MiddleCenter, color, style);
            var lrt = (RectTransform)l.transform;
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.offsetMin = new Vector2(4, 2); lrt.offsetMax = new Vector2(-4, -2);
            return l;
        }

        /// <summary>A normal tap button (menus).</summary>
        public static Button MenuButton(Transform parent, string text, Vector2 anchor, Vector2 pos, Vector2 size, System.Action onClick, Color? bg = null, int fontSize = 40)
        {
            var rt = Rect(parent, "Btn_" + text, anchor, pos, size);
            var img = Img(rt, bg ?? new Color(0.16f, 0.17f, 0.2f, 0.95f), ProcTex.RoundRect(), true);
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            var cb = b.colors;
            cb.highlightedColor = new Color(1f, 1f, 1f, 1f);
            cb.pressedColor = new Color(0.8f, 0.6f, 0.3f, 1f);
            b.colors = cb;
            b.onClick.AddListener(() => { if (onClick != null) onClick(); });
            FillLabel(rt, text, fontSize);
            return b;
        }
    }
}
