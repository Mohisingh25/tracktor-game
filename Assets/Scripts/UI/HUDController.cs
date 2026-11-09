using UnityEngine;
using UnityEngine.UI;

namespace TractorSim
{
    /// <summary>
    /// Gameplay HUD: analog dashboard (RPM, speed), gear/mode, fuel/temp/damage, warning lamps,
    /// toasts, and all touch driving controls. Every control maps to real tractor functionality.
    /// </summary>
    public class HUDController : MonoBehaviour
    {
        public Canvas Canvas { get; private set; }
        TractorController tractor;
        MobileInputManager inp;
        GameSettings settings;

        RectTransform leftGroup, rightGroup, topLeft, dashboard, infoPanel, hintPanel;
        RectTransform rpmNeedle, speedNeedle;
        Text gearText, modeText, speedText, rpmText, infoText, toastText, fuelPct, tempText;
        Image fuelFill, tempFill, dmgFill;
        Text lampFuel, lampTemp, lampPark, lampDiff, lampIndL, lampIndR, lampBeam, lampOil, lampRange;
        TouchSteeringWheel wheel;
        TouchButton btnLeft, btnRight, btnGearUp, btnGearDown, btnRange, btnDir, btnStart, btnHorn, btnPark, btnDiff, btnIndL, btnIndR, btnLights, btnHaz, btnCam, btnPause, btnRecover;
        TouchPedal accel, brake, clutch, handThrottle;
        Text clutchLabelHint;
        float toastTimer;
        public System.Action onPause, onCamera, onRecover;

        public void Build(TractorController t, GameSettings s)
        {
            tractor = t; settings = s; inp = MobileInputManager.I;
            Canvas = UIFactory.CreateCanvas("HUD", 10);
            Canvas.transform.SetParent(transform, false);
            var root = Canvas.transform;

            // camera look layer (behind everything)
            var look = UIFactory.Stretch(root, "LookArea");
            UIFactory.Img(look, new Color(0, 0, 0, 0), null, true);
            var drag = look.gameObject.AddComponent<CameraDragArea>();
            drag.onDrag = d => { var cm = FindFirstObjectByType<CameraManager>(); if (cm != null) cm.Drag(d); };

            BuildDashboard(root);
            BuildInfo(root);
            BuildLeft(root);
            BuildRight(root);
            BuildTop(root);

            toastText = UIFactory.Label(root, "", 34, new Vector2(0.5f, 1f), new Vector2(0, -360f), new Vector2(1100f, 60f), TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            var sh = toastText.gameObject.AddComponent<Shadow>(); sh.effectDistance = new Vector2(2, -2);

            t.OnMessage += Toast;
            ApplySettings(s);
        }

        // ------------------------------------------------------------------ build
        void BuildDashboard(Transform root)
        {
            dashboard = UIFactory.Rect(root, "Dashboard", new Vector2(0.5f, 1f), new Vector2(0, -150f), new Vector2(760f, 280f));
            // RPM
            var rpm = UIFactory.Rect(dashboard, "RPM", new Vector2(0.5f, 0.5f), new Vector2(-235f, 0), new Vector2(260f, 260f));
            UIFactory.Img(rpm, Color.white, ProcTex.GaugeFace(10, 9));
            rpmNeedle = UIFactory.Rect(rpm, "Needle", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(240f, 240f));
            UIFactory.Img(rpmNeedle, new Color(1f, 0.55f, 0.1f), ProcTex.Needle());
            var cap = UIFactory.Rect(rpm, "Cap", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(34f, 34f));
            UIFactory.Img(cap, new Color(0.2f, 0.2f, 0.2f), ProcTex.Circle());
            UIFactory.Label(rpm, "RPM x100", 18, new Vector2(0.5f, 0.5f), new Vector2(0, -50f), new Vector2(200, 30), TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.6f));
            rpmText = UIFactory.Label(rpm, "0", 26, new Vector2(0.5f, 0.5f), new Vector2(0, -82f), new Vector2(200, 34), TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            for (int i = 0; i <= 10; i += 2) TickNumber(rpm, (i * 2.5f).ToString("0"), i / 10f, 88f);   // 0..25 (x100)

            // Speed
            var spd = UIFactory.Rect(dashboard, "Speed", new Vector2(0.5f, 0.5f), new Vector2(235f, 0), new Vector2(260f, 260f));
            UIFactory.Img(spd, Color.white, ProcTex.GaugeFace(8, 99));
            speedNeedle = UIFactory.Rect(spd, "Needle", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(240f, 240f));
            UIFactory.Img(speedNeedle, new Color(0.95f, 0.95f, 0.95f), ProcTex.Needle());
            var cap2 = UIFactory.Rect(spd, "Cap", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(34f, 34f));
            UIFactory.Img(cap2, new Color(0.2f, 0.2f, 0.2f), ProcTex.Circle());
            UIFactory.Label(spd, "km/h", 18, new Vector2(0.5f, 0.5f), new Vector2(0, -50f), new Vector2(200, 30), TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.6f));
            speedText = UIFactory.Label(spd, "0", 30, new Vector2(0.5f, 0.5f), new Vector2(0, -82f), new Vector2(200, 34), TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            for (int i = 0; i <= 8; i += 2) TickNumber(spd, (i * 5).ToString(), i / 8f, 88f);    // 0..40

            // centre column: gear, mode, fuel, temp
            var mid = UIFactory.Rect(dashboard, "Centre", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(200f, 260f));
            UIFactory.Img(mid, new Color(0.05f, 0.06f, 0.07f, 0.7f), ProcTex.RoundRect());
            gearText = UIFactory.Label(mid, "N", 78, new Vector2(0.5f, 1f), new Vector2(0, -58f), new Vector2(190, 90), TextAnchor.MiddleCenter, UIFactory.Accent, FontStyle.Bold);
            modeText = UIFactory.Label(mid, "MANUAL", 20, new Vector2(0.5f, 1f), new Vector2(0, -112f), new Vector2(190, 28), TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.75f));
            fuelFill = Bar(mid, "FUEL", new Vector2(0, -150f), new Color(0.3f, 0.75f, 0.35f), out fuelPct);
            tempFill = Bar(mid, "TEMP", new Vector2(0, -190f), new Color(0.3f, 0.6f, 0.95f), out tempText);
            Text dmgText;
            dmgFill = Bar(mid, "DMG", new Vector2(0, -230f), new Color(0.9f, 0.35f, 0.2f), out dmgText);

            // warning lamps row
            var lamps = UIFactory.Rect(dashboard, "Lamps", new Vector2(0.5f, 0f), new Vector2(0, -24f), new Vector2(760f, 40f));
            string[] names = { "◄", "FUEL", "TEMP", "OIL", "P", "DIFF", "BEAM", "LO", "►" };
            var arr = new Text[names.Length];
            for (int i = 0; i < names.Length; i++)
                arr[i] = UIFactory.Label(lamps, names[i], 22, new Vector2(0.5f, 0.5f), new Vector2((i - (names.Length - 1) * 0.5f) * 82f, 0), new Vector2(80, 36), TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.12f), FontStyle.Bold);
            lampIndL = arr[0]; lampFuel = arr[1]; lampTemp = arr[2]; lampOil = arr[3]; lampPark = arr[4]; lampDiff = arr[5]; lampBeam = arr[6]; lampRange = arr[7]; lampIndR = arr[8];
        }

        void TickNumber(RectTransform gauge, string txt, float t, float radius)
        {
            float ang = Mathf.Lerp(-120f, 120f, t) * Mathf.Deg2Rad;
            UIFactory.Label(gauge, txt, 20, new Vector2(0.5f, 0.5f), new Vector2(Mathf.Sin(ang) * radius, Mathf.Cos(ang) * radius), new Vector2(50, 26), TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.85f));
        }

        Image Bar(RectTransform parent, string label, Vector2 pos, Color c, out Text valueText)
        {
            UIFactory.Label(parent, label, 16, new Vector2(0.5f, 1f), pos + new Vector2(-62f, 0), new Vector2(60, 24), TextAnchor.MiddleLeft, new Color(1, 1, 1, 0.7f));
            var bg = UIFactory.Rect(parent, label + "Bg", new Vector2(0.5f, 1f), pos + new Vector2(18f, 0), new Vector2(100f, 14f));
            UIFactory.Img(bg, new Color(1, 1, 1, 0.12f));
            var fill = UIFactory.Rect(bg, "Fill", new Vector2(0f, 0.5f), Vector2.zero, new Vector2(100f, 14f), new Vector2(0f, 0.5f));
            var img = UIFactory.Img(fill, c);
            img.type = Image.Type.Filled; img.fillMethod = Image.FillMethod.Horizontal; img.fillAmount = 0.5f;
            img.sprite = ProcTex.RoundRect();
            valueText = UIFactory.Label(parent, "", 15, new Vector2(0.5f, 1f), pos + new Vector2(86f, 0), new Vector2(50, 24), TextAnchor.MiddleLeft, new Color(1, 1, 1, 0.7f));
            return img;
        }

        void BuildInfo(Transform root)
        {
            infoPanel = UIFactory.Rect(root, "Info", new Vector2(1f, 1f), new Vector2(-20f, -20f), new Vector2(330f, 210f), new Vector2(1f, 1f));
            UIFactory.Img(infoPanel, UIFactory.PanelColor, ProcTex.RoundRect());
            infoText = UIFactory.Label(infoPanel, "", 22, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300f, 190f), TextAnchor.UpperLeft, new Color(1, 1, 1, 0.9f));
        }

        void BuildLeft(Transform root)
        {
            leftGroup = UIFactory.Rect(root, "LeftControls", Vector2.zero, Vector2.zero, new Vector2(760f, 760f), Vector2.zero);
            var a = Vector2.zero;
            wheel = TouchSteeringWheel.Create(leftGroup, a, new Vector2(255f, 245f), 420f);
            wheel.sensitivity = () => settings.steerSensitivity;
            btnLeft = TouchButton.Create(leftGroup, "", a, new Vector2(140f, 170f), new Vector2(210f, 230f));
            btnRight = TouchButton.Create(leftGroup, "", a, new Vector2(380f, 170f), new Vector2(210f, 230f));
            ArrowIcon(btnLeft, false); ArrowIcon(btnRight, true);
            btnLeft.onDown = () => inp.touchLeft = true; btnLeft.onUp = () => inp.touchLeft = false;
            btnRight.onDown = () => inp.touchRight = true; btnRight.onUp = () => inp.touchRight = false;

            // clutch sits on the LEFT so one thumb works the clutch while the other works throttle / gears
            clutch = TouchPedal.Create(leftGroup, "CLUTCH", a, new Vector2(565f, 205f), new Vector2(150f, 260f), new Color(0.3f, 0.6f, 1f), 0.15f);
            clutch.onValue = v => inp.touchClutch = v;
            clutchLabelHint = UIFactory.Label(clutch.transform, "slide down\nto release", 16, new Vector2(0.5f, 1f), new Vector2(0, 26f), new Vector2(160f, 44f), TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.55f));

            Vector2 bs = new Vector2(125f, 100f);
            btnStart = TouchButton.Create(leftGroup, "START", a, new Vector2(85f, 520f), bs, 24);
            btnStart.onDown = () => inp.Press("ignition");
            btnHorn = TouchButton.Create(leftGroup, "HORN", a, new Vector2(220f, 520f), bs, 24);
            btnHorn.onDown = () => inp.horn = true; btnHorn.onUp = () => inp.horn = false;
            btnPark = TouchButton.Create(leftGroup, "P-BRAKE", a, new Vector2(355f, 520f), bs, 22);
            btnPark.onDown = () => inp.Press("handbrake");
            btnDiff = TouchButton.Create(leftGroup, "DIFF\nLOCK", a, new Vector2(490f, 520f), bs, 20);
            btnDiff.onDown = () => inp.Press("diff");

            btnIndL = TouchButton.Create(leftGroup, "◄", a, new Vector2(85f, 630f), bs, 38);
            btnIndL.onDown = () => inp.Press("indL");
            btnLights = TouchButton.Create(leftGroup, "LIGHTS", a, new Vector2(220f, 630f), bs, 22);
            btnLights.onDown = () => inp.Press("lights");
            btnIndR = TouchButton.Create(leftGroup, "►", a, new Vector2(355f, 630f), bs, 38);
            btnIndR.onDown = () => inp.Press("indR");
            btnHaz = TouchButton.Create(leftGroup, "HAZARD", a, new Vector2(490f, 630f), bs, 20);
            btnHaz.onDown = () => inp.Press("hazard");
            btnIndL.litColor = btnIndR.litColor = btnHaz.litColor = new Color(1f, 0.6f, 0f);
            btnPark.litColor = UIFactory.Danger;
            btnStart.litColor = UIFactory.AccentGreen;
        }

        void ArrowIcon(TouchButton b, bool right)
        {
            var rt = UIFactory.Rect(b.transform, "Arrow", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(110f, 110f));
            var img = UIFactory.Img(rt, new Color(1, 1, 1, 0.85f), ProcTex.Arrow());
            if (right) rt.localEulerAngles = new Vector3(0, 0, 180f);
        }

        void BuildRight(Transform root)
        {
            rightGroup = UIFactory.Rect(root, "RightControls", new Vector2(1f, 0f), Vector2.zero, new Vector2(820f, 760f), new Vector2(1f, 0f));
            var a = new Vector2(1f, 0f);
            accel = TouchPedal.Create(rightGroup, "ACCEL", a, new Vector2(-105f, 235f), new Vector2(150f, 340f), new Color(0.3f, 0.85f, 0.35f), 0.35f);
            accel.onValue = v => inp.touchThrottle = v;
            brake = TouchPedal.Create(rightGroup, "BRAKE", a, new Vector2(-285f, 205f), new Vector2(170f, 260f), new Color(0.95f, 0.25f, 0.2f), 0.4f);
            brake.onValue = v => inp.touchBrake = v;
            handThrottle = TouchPedal.Create(rightGroup, "HAND\nTHR", a, new Vector2(-445f, 205f), new Vector2(90f, 260f), new Color(1f, 0.75f, 0.2f), 0f, true);
            handThrottle.onValue = v => inp.HandThrottle = v;

            Vector2 bs = new Vector2(140f, 110f);
            btnGearUp = TouchButton.Create(rightGroup, "GEAR ▲", a, new Vector2(-105f, 495f), bs, 24);
            btnGearUp.onDown = () => inp.Press("gearUp");
            btnGearDown = TouchButton.Create(rightGroup, "GEAR ▼", a, new Vector2(-260f, 495f), bs, 24);
            btnGearDown.onDown = () => inp.Press("gearDown");
            btnRange = TouchButton.Create(rightGroup, "RANGE\nL / H", a, new Vector2(-415f, 495f), bs, 22);
            btnRange.onDown = () => inp.Press("range");
            btnDir = TouchButton.Create(rightGroup, "F / R", a, new Vector2(-105f, 495f), bs, 30);
            btnDir.onDown = () => inp.Press("direction");
            btnDir.litColor = UIFactory.Danger;
        }

        void BuildTop(Transform root)
        {
            topLeft = UIFactory.Rect(root, "TopLeft", new Vector2(0f, 1f), new Vector2(20f, -20f), new Vector2(520f, 110f), new Vector2(0f, 1f));
            btnPause = TouchButton.Create(topLeft, "II", new Vector2(0f, 1f), new Vector2(60f, -50f), new Vector2(110f, 95f), 40);
            btnPause.onDown = () => { if (onPause != null) onPause(); };
            btnCam = TouchButton.Create(topLeft, "CAMERA", new Vector2(0f, 1f), new Vector2(195f, -50f), new Vector2(140f, 95f), 24);
            btnCam.onDown = () => { if (onCamera != null) onCamera(); };
            btnRecover = TouchButton.Create(topLeft, "RECOVER", new Vector2(0f, 1f), new Vector2(355f, -50f), new Vector2(160f, 95f), 24);
            btnRecover.onDown = () => { if (onRecover != null) onRecover(); };
            btnRecover.Lit = true; btnRecover.litColor = UIFactory.Danger;
        }

        // ------------------------------------------------------------------ runtime
        public void ApplySettings(GameSettings s)
        {
            settings = s;
            bool manual = !s.automatic;
            clutch.gameObject.SetActive(manual);
            btnGearUp.gameObject.SetActive(manual);
            btnGearDown.gameObject.SetActive(manual);
            btnRange.gameObject.SetActive(manual);
            btnDir.gameObject.SetActive(!manual);
            var mode = (SteeringMode)s.steeringMode;
            wheel.gameObject.SetActive(mode == SteeringMode.Wheel);
            btnLeft.gameObject.SetActive(mode == SteeringMode.Arrows);
            btnRight.gameObject.SetActive(mode == SteeringMode.Arrows);
            float k = Mathf.Clamp(s.controlScale, 0.7f, 1.4f);
            leftGroup.localScale = rightGroup.localScale = Vector3.one * k;
            if (inp != null) { inp.steeringMode = mode; inp.sensitivity = s.steerSensitivity; }
        }

        public void SetVisible(bool v) { Canvas.enabled = v; }

        public void Toast(string msg, bool warn)
        {
            toastText.text = msg;
            toastText.color = warn ? new Color(1f, 0.45f, 0.3f) : Color.white;
            toastTimer = warn ? 3.5f : 2f;
            if (warn && msg.Contains("grind") && AudioManager.I != null) { }
        }

        void Update()
        {
            if (tractor == null || !Canvas.enabled) return;
            var e = tractor.Engine;
            var spec = tractor.Spec;
            float kmh = Mathf.Abs(tractor.Physics.SpeedKmh);

            rpmNeedle.localEulerAngles = new Vector3(0, 0, -Mathf.Lerp(-120f, 120f, e.Rpm / 2500f));
            speedNeedle.localEulerAngles = new Vector3(0, 0, -Mathf.Lerp(-120f, 120f, kmh / 40f));
            rpmText.text = Mathf.RoundToInt(e.Rpm / 10f) * 10 + "";
            speedText.text = kmh.ToString("0.0");

            var dtn = tractor.Drivetrain;
            if (dtn.Mode == TransmissionMode.Automatic)
            {
                string dir = dtn.RequestedDirection > 0 ? "D" : "R";
                gearText.text = e.Running ? (dtn.Gearbox.InNeutral ? dir : dir + " " + dtn.Gearbox.Label) : "—";
                modeText.text = "AUTOMATIC";
                btnDir.Lit = dtn.RequestedDirection < 0;
            }
            else
            {
                gearText.text = dtn.Gearbox.Label;
                string cl = dtn.ClutchPedal > 0.75f ? "CLUTCH IN" : (dtn.ClutchEngagement < 0.97f && dtn.ClutchEngagement > 0.03f ? "SLIPPING" : "MANUAL");
                modeText.text = cl;
            }
            gearText.color = dtn.Gearbox.InReverse ? new Color(1f, 0.35f, 0.3f) : UIFactory.Accent;

            float fuel = e.FuelL / spec.fuelTankL;
            fuelFill.fillAmount = fuel;
            fuelFill.color = fuel < 0.15f ? UIFactory.Danger : new Color(0.3f, 0.75f, 0.35f);
            fuelPct.text = Mathf.RoundToInt(e.FuelL) + "L";
            tempFill.fillAmount = Mathf.InverseLerp(30f, 125f, e.TempC);
            tempFill.color = e.TempC > 108f ? UIFactory.Danger : (e.TempC > 98f ? UIFactory.Accent : new Color(0.3f, 0.6f, 0.95f));
            tempText.text = Mathf.RoundToInt(e.TempC) + "°";
            dmgFill.fillAmount = tractor.Damage / 100f;

            bool blink = tractor.BlinkOn;
            Lamp(lampIndL, blink && (tractor.Indicator == -1 || tractor.Indicator == 2), new Color(0.2f, 1f, 0.3f));
            Lamp(lampIndR, blink && (tractor.Indicator == 1 || tractor.Indicator == 2), new Color(0.2f, 1f, 0.3f));
            Lamp(lampFuel, e.LowFuel, UIFactory.Accent);
            Lamp(lampTemp, e.Overheating, UIFactory.Danger);
            Lamp(lampOil, !e.Running && (e.Cranking || tractor.InputEnabled), UIFactory.Danger);  // oil pressure lamp is lit whenever the engine isn't running
            Lamp(lampPark, tractor.Brakes.Handbrake, UIFactory.Danger);
            Lamp(lampDiff, dtn.DiffLock, UIFactory.Accent);
            Lamp(lampBeam, tractor.Headlights, new Color(0.3f, 0.6f, 1f));
            Lamp(lampRange, dtn.Gearbox.Range == 0 && !dtn.Gearbox.InNeutral, new Color(0.9f, 0.9f, 0.9f));

            btnStart.label.text = e.Running ? "STOP" : (e.Cranking ? "..." : "START");
            btnStart.Lit = e.Running;
            btnPark.Lit = tractor.Brakes.Handbrake;
            btnDiff.Lit = dtn.DiffLock;
            btnLights.Lit = tractor.Headlights;
            btnIndL.Lit = tractor.Indicator == -1 && blink;
            btnIndR.Lit = tractor.Indicator == 1 && blink;
            btnHaz.Lit = tractor.Indicator == 2 && blink;
            btnRecover.gameObject.SetActive(tractor.Overturned || tractor.Stuck);
            if (clutchLabelHint != null) clutchLabelHint.enabled = settings.showHints;

            var w = tractor.Physics.Wheels[2];
            float lph = e.FuelRateLph;
            infoText.text =
                "Surface: " + w.props.name + "\n" +
                "Slope: " + Mathf.RoundToInt(tractor.Physics.SlopeDeg) + "°\n" +
                "Fuel use: " + lph.ToString("0.0") + " L/h\n" +
                "Load: " + Mathf.RoundToInt(e.Rack * 100f) + "%   " + (e.PowerKw * 1.341f).ToString("0") + " hp\n" +
                "Odometer: " + (tractor.Physics.Odometer / 1000f + SaveSystem.Data.odometerKm).ToString("0.00") + " km\n" +
                "Hand throttle: " + Mathf.RoundToInt(inp.HandThrottle * 100f) + "%";

            if (toastTimer > 0f)
            {
                toastTimer -= Time.unscaledDeltaTime;
                var c = toastText.color; c.a = Mathf.Clamp01(toastTimer * 2f); toastText.color = c;
            }
        }

        static void Lamp(Text t, bool on, Color c)
        {
            t.color = on ? c : new Color(1f, 1f, 1f, 0.12f);
        }

        public void ResetControls()
        {
            if (handThrottle != null) handThrottle.Set(0f);
            if (inp != null) inp.ClearAll();
        }
    }
}
