using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TractorSim
{
    /// <summary>Momentary or tap button that works with multi-touch (each finger is its own pointer).</summary>
    public class TouchButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler, IPointerEnterHandler
    {
        public System.Action onDown, onUp;
        public Image bg;
        public Text label;
        public bool Held { get; private set; }
        public bool Lit;                 // externally driven "active" state (e.g. lights on)
        public Color litColor = UIFactory.Accent;
        Color baseColor;
        int pointer = -99;

        public void Setup(Image img, Text txt) { bg = img; label = txt; baseColor = img.color; }

        public void OnPointerDown(PointerEventData e)
        {
            if (Held) return;
            Held = true; pointer = e.pointerId;
            if (onDown != null) onDown();
        }
        public void OnPointerUp(PointerEventData e) { if (e.pointerId == pointer) Release(); }
        public void OnPointerExit(PointerEventData e) { }
        public void OnPointerEnter(PointerEventData e) { }

        void Release()
        {
            if (!Held) return;
            Held = false; pointer = -99;
            if (onUp != null) onUp();
        }

        void OnDisable() { Release(); }

        void Update()
        {
            if (bg == null) return;
            Color c = Lit ? new Color(litColor.r, litColor.g, litColor.b, 0.85f) : baseColor;
            if (Held) c = Color.Lerp(c, Color.white, 0.35f);
            bg.color = c;
        }

        public static TouchButton Create(Transform parent, string text, Vector2 anchor, Vector2 pos, Vector2 size, int fontSize = 26)
        {
            var rt = UIFactory.Rect(parent, "Touch_" + text, anchor, pos, size);
            var img = UIFactory.Img(rt, UIFactory.ButtonColor, ProcTex.RoundRect(), true);
            var t = UIFactory.FillLabel(rt, text, fontSize);
            var b = rt.gameObject.AddComponent<TouchButton>();
            b.Setup(img, t);
            return b;
        }
    }

    /// <summary>
    /// Analog pedal. The higher you touch (or slide) on it, the further it is pressed.
    /// Slide your finger down to release it gradually – essential for feathering the clutch.
    /// </summary>
    public class TouchPedal : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
    {
        public System.Action<float> onValue;
        public float minPress = 0.3f;    // value at the bottom of the pedal
        public bool sticky;              // hand-throttle style: keeps its value after release
        public float Value { get; private set; }
        RectTransform rt, fill;
        int pointer = -99;

        public static TouchPedal Create(Transform parent, string text, Vector2 anchor, Vector2 pos, Vector2 size, Color fillColor, float minPress, bool sticky = false)
        {
            var rt = UIFactory.Rect(parent, "Pedal_" + text, anchor, pos, size);
            UIFactory.Img(rt, new Color(0.1f, 0.1f, 0.11f, 0.7f), ProcTex.RoundRect(), true);
            // ribbed pedal look
            for (int i = 0; i < 6; i++)
            {
                var rib = UIFactory.Rect(rt, "Rib", new Vector2(0.5f, 0f), new Vector2(0, size.y * (0.15f + i * 0.13f)), new Vector2(size.x * 0.7f, 5f));
                UIFactory.Img(rib, new Color(1f, 1f, 1f, 0.12f));
            }
            var f = UIFactory.Rect(rt, "Fill", new Vector2(0.5f, 0f), Vector2.zero, new Vector2(size.x - 14f, 0f), new Vector2(0.5f, 0f));
            UIFactory.Img(f, new Color(fillColor.r, fillColor.g, fillColor.b, 0.55f), ProcTex.RoundRect());
            UIFactory.Label(rt, text, 24, new Vector2(0.5f, 0f), new Vector2(0, -22f), new Vector2(size.x + 40f, 40f), TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.85f), FontStyle.Bold);
            var p = rt.gameObject.AddComponent<TouchPedal>();
            p.rt = rt; p.fill = f; p.minPress = minPress; p.sticky = sticky;
            return p;
        }

        float FromPointer(PointerEventData e)
        {
            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, e.pressEventCamera, out local);
            float n = Mathf.Clamp01((local.y - rt.rect.yMin) / rt.rect.height);
            return sticky ? n : Mathf.Lerp(minPress, 1f, n);
        }

        public void OnPointerDown(PointerEventData e)
        {
            pointer = e.pointerId;
            Set(FromPointer(e));
        }
        public void OnDrag(PointerEventData e) { if (e.pointerId == pointer) Set(FromPointer(e)); }
        public void OnPointerUp(PointerEventData e)
        {
            if (e.pointerId != pointer) return;
            pointer = -99;
            if (!sticky) Set(0f);
        }
        void OnDisable() { if (!sticky) Set(0f); pointer = -99; }

        public void Set(float v)
        {
            Value = Mathf.Clamp01(v);
            if (fill != null) fill.sizeDelta = new Vector2(fill.sizeDelta.x, rt.rect.height * Value);
            if (onValue != null) onValue(Value);
        }
    }

    /// <summary>On-screen steering wheel: grab and rotate; springs back to centre when released.</summary>
    public class TouchSteeringWheel : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
    {
        public float maxDegrees = 270f;
        public float returnSpeed = 420f;   // deg/s self-centring
        public float Value { get { return Mathf.Clamp(angle / maxDegrees, -1f, 1f); } }
        public System.Func<float> sensitivity;
        RectTransform rt, wheel;
        float angle, lastPointerAngle;
        int pointer = -99;

        public static TouchSteeringWheel Create(Transform parent, Vector2 anchor, Vector2 pos, float size)
        {
            var rt = UIFactory.Rect(parent, "SteeringWheel", anchor, pos, new Vector2(size, size));
            UIFactory.Img(rt, new Color(0, 0, 0, 0.18f), ProcTex.Circle(), true);
            var w = UIFactory.Rect(rt, "Wheel", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size * 0.96f, size * 0.96f));
            UIFactory.Img(w, new Color(1, 1, 1, 0.92f), ProcTex.SteeringWheel());
            var s = rt.gameObject.AddComponent<TouchSteeringWheel>();
            s.rt = rt; s.wheel = w;
            return s;
        }

        float PointerAngle(PointerEventData e)
        {
            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, e.pressEventCamera, out local);
            return Mathf.Atan2(local.x, local.y) * Mathf.Rad2Deg;   // 0 = up, + = clockwise
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (pointer != -99) return;
            pointer = e.pointerId;
            lastPointerAngle = PointerAngle(e);
        }

        public void OnDrag(PointerEventData e)
        {
            if (e.pointerId != pointer) return;
            float a = PointerAngle(e);
            float delta = Mathf.DeltaAngle(lastPointerAngle, a);
            lastPointerAngle = a;
            float sens = sensitivity != null ? sensitivity() : 1f;
            angle = Mathf.Clamp(angle + delta * sens, -maxDegrees, maxDegrees);
        }

        public void OnPointerUp(PointerEventData e) { if (e.pointerId == pointer) pointer = -99; }
        void OnDisable() { pointer = -99; angle = 0f; }

        void Update()
        {
            if (pointer == -99) angle = Mathf.MoveTowards(angle, 0f, returnSpeed * Time.unscaledDeltaTime);
            if (wheel != null) wheel.localEulerAngles = new Vector3(0, 0, -angle);
            if (MobileInputManager.I != null) MobileInputManager.I.touchSteerWheel = Value;
        }
    }

    /// <summary>Transparent full-screen layer behind the controls: drag to look around.</summary>
    public class CameraDragArea : MonoBehaviour, IDragHandler, IPointerDownHandler, IPointerUpHandler
    {
        public System.Action<Vector2> onDrag;
        public System.Action onRelease;
        int pointer = -99;
        public void OnPointerDown(PointerEventData e) { if (pointer == -99) pointer = e.pointerId; }
        public void OnDrag(PointerEventData e) { if (e.pointerId == pointer && onDrag != null) onDrag(e.delta); }
        public void OnPointerUp(PointerEventData e) { if (e.pointerId == pointer) { pointer = -99; if (onRelease != null) onRelease(); } }
    }
}
