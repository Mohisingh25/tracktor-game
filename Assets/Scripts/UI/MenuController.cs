using UnityEngine;
using UnityEngine.UI;

namespace TractorSim
{
    /// <summary>Main menu, pause menu, settings and the first-run controls guide. Every button does something real.</summary>
    public class MenuController : MonoBehaviour
    {
        Canvas canvas;
        RectTransform main, pause, settingsPanel, guide;
        System.Action<bool> settingsBack;
        public System.Action onPlay, onResume, onRestart, onMainMenu, onRecover, onService, onQuit, onSettingsChanged;
        Text statsText;
        GameSettings s;

        public void Build(TractorSpec spec)
        {
            s = SaveSystem.Settings;
            canvas = UIFactory.CreateCanvas("Menus", 20);
            canvas.transform.SetParent(transform, false);
            var root = canvas.transform;
            BuildMain(root, spec);
            BuildPause(root);
            BuildSettings(root);
            BuildGuide(root);
            HideAll();
        }

        RectTransform Panel(Transform root, string name, Vector2 size, Vector2 anchor, Vector2 pos)
        {
            var p = UIFactory.Rect(root, name, anchor, pos, size);
            UIFactory.Img(p, UIFactory.PanelColor, ProcTex.RoundRect(), true);
            return p;
        }

        void BuildMain(Transform root, TractorSpec spec)
        {
            main = UIFactory.Stretch(root, "MainMenu");
            // left gradient strip
            var strip = UIFactory.Rect(main, "Strip", new Vector2(0f, 0.5f), new Vector2(0, 0), new Vector2(820f, 1080f), new Vector2(0f, 0.5f));
            UIFactory.Img(strip, new Color(0.03f, 0.03f, 0.04f, 0.72f));
            UIFactory.Label(main, "INDIAN TRACTOR", 76, new Vector2(0f, 1f), new Vector2(410f, -130f), new Vector2(760f, 90f), TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
            UIFactory.Label(main, "SIMULATOR 3D", 76, new Vector2(0f, 1f), new Vector2(410f, -215f), new Vector2(760f, 90f), TextAnchor.MiddleLeft, UIFactory.Accent, FontStyle.Bold);
            UIFactory.Label(main, "Stage 1  ·  Proving Ground", 30, new Vector2(0f, 1f), new Vector2(410f, -285f), new Vector2(760f, 40f), TextAnchor.MiddleLeft, new Color(1, 1, 1, 0.7f));

            var a = new Vector2(0f, 1f);
            UIFactory.MenuButton(main, "TEST DRIVE", a, new Vector2(270f, -400f), new Vector2(460f, 100f), () => { if (onPlay != null) onPlay(); }, new Color(0.75f, 0.42f, 0.05f, 0.95f), 44);
            UIFactory.MenuButton(main, "SETTINGS", a, new Vector2(270f, -520f), new Vector2(460f, 90f), () => ShowSettings(false));
            UIFactory.MenuButton(main, "HOW TO DRIVE", a, new Vector2(270f, -630f), new Vector2(460f, 90f), () => ShowGuide());
            UIFactory.MenuButton(main, "QUIT", a, new Vector2(270f, -740f), new Vector2(460f, 90f), () => { if (onQuit != null) onQuit(); });

            // spec card – real numbers from the tractor in the game
            var card = Panel(main, "SpecCard", new Vector2(520f, 420f), new Vector2(1f, 0f), new Vector2(-290f, 250f));
            UIFactory.Label(card, spec.displayName, 40, new Vector2(0.5f, 1f), new Vector2(0, -45f), new Vector2(480f, 50f), TextAnchor.MiddleLeft, UIFactory.Accent, FontStyle.Bold);
            UIFactory.Label(card,
                "Engine      " + spec.ratedPowerHp + " HP · " + spec.cylinders + "-cyl diesel\n" +
                "Max torque  " + Mathf.RoundToInt(Max(spec.torqueNm)) + " Nm\n" +
                "Gearbox     8F + 2R (L/H range)\n" +
                "Top speed   " + spec.forwardSpeedsKmh[spec.forwardSpeedsKmh.Length - 1].ToString("0.0") + " km/h\n" +
                "Weight      " + spec.massKg + " kg\n" +
                "Fuel tank   " + spec.fuelTankL + " L\n" +
                "Drive       2WD (rear), diff lock",
                24, new Vector2(0.5f, 1f), new Vector2(0, -215f), new Vector2(480f, 280f), TextAnchor.UpperLeft, new Color(1, 1, 1, 0.9f));
            statsText = UIFactory.Label(card, "", 20, new Vector2(0.5f, 0f), new Vector2(0, 30f), new Vector2(480f, 30f), TextAnchor.MiddleLeft, new Color(1, 1, 1, 0.6f));
        }

        static float Max(float[] a) { float m = 0; foreach (var v in a) m = Mathf.Max(m, v); return m; }

        void BuildPause(Transform root)
        {
            pause = UIFactory.Stretch(root, "Pause");
            UIFactory.Img(pause, new Color(0, 0, 0, 0.55f), null, true);
            var p = Panel(pause, "PausePanel", new Vector2(600f, 820f), new Vector2(0.5f, 0.5f), Vector2.zero);
            UIFactory.Label(p, "PAUSED", 56, new Vector2(0.5f, 1f), new Vector2(0, -70f), new Vector2(560, 70), TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            var a = new Vector2(0.5f, 1f);
            Vector2 bs = new Vector2(480f, 84f);
            UIFactory.MenuButton(p, "RESUME", a, new Vector2(0, -170f), bs, () => { if (onResume != null) onResume(); }, new Color(0.75f, 0.42f, 0.05f, 0.95f));
            UIFactory.MenuButton(p, "RESTART", a, new Vector2(0, -270f), bs, () => { if (onRestart != null) onRestart(); });
            UIFactory.MenuButton(p, "RECOVER TRACTOR", a, new Vector2(0, -370f), bs, () => { if (onRecover != null) onRecover(); }, null, 34);
            UIFactory.MenuButton(p, "REFUEL & REPAIR", a, new Vector2(0, -470f), bs, () => { if (onService != null) onService(); }, null, 34);
            UIFactory.MenuButton(p, "SETTINGS", a, new Vector2(0, -570f), bs, () => ShowSettings(true));
            UIFactory.MenuButton(p, "MAIN MENU", a, new Vector2(0, -670f), bs, () => { if (onMainMenu != null) onMainMenu(); });
        }

        // ---------------- settings ----------------
        RectTransform settingsList;
        void BuildSettings(Transform root)
        {
            settingsPanel = UIFactory.Stretch(root, "Settings");
            UIFactory.Img(settingsPanel, new Color(0, 0, 0, 0.6f), null, true);
            var p = Panel(settingsPanel, "SettingsPanel", new Vector2(1180f, 980f), new Vector2(0.5f, 0.5f), Vector2.zero);
            UIFactory.Label(p, "SETTINGS", 52, new Vector2(0.5f, 1f), new Vector2(0, -60f), new Vector2(800, 70), TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            settingsList = UIFactory.Rect(p, "List", new Vector2(0.5f, 1f), new Vector2(0, -120f), new Vector2(1100f, 760f), new Vector2(0.5f, 1f));
            int row = 0;
            Row(ref row, "Transmission", () => s.automatic ? "Automatic" : "Manual (clutch + gears)", d => s.automatic = !s.automatic);
            Row(ref row, "Steering control", () => new[] { "Steering wheel", "Left / right arrows", "Tilt (gyro)" }[s.steeringMode], d => s.steeringMode = (s.steeringMode + d + 3) % 3);
            Row(ref row, "Steering sensitivity", () => s.steerSensitivity.ToString("0.0") + "x", d => s.steerSensitivity = Mathf.Clamp(Mathf.Round((s.steerSensitivity + d * 0.1f) * 10f) / 10f, 0.5f, 2f));
            Row(ref row, "Control size", () => Mathf.RoundToInt(s.controlScale * 100f) + "%", d => s.controlScale = Mathf.Clamp(s.controlScale + d * 0.05f, 0.7f, 1.4f));
            Row(ref row, "Graphics quality", () => new[] { "Low", "Medium", "High", "Ultra" }[s.graphics], d => s.graphics = Mathf.Clamp(s.graphics + d, 0, 3));
            Row(ref row, "Frame rate", () => s.fps + " FPS", d => s.fps = s.fps == 30 ? 60 : 30);
            Row(ref row, "Render resolution", () => Mathf.RoundToInt(s.renderScale * 100f) + "%", d => s.renderScale = Mathf.Clamp(s.renderScale + d * 0.05f, 0.5f, 1f));
            Row(ref row, "Time of day", () => new[] { "Day", "Evening", "Night" }[s.timeOfDay], d => s.timeOfDay = (s.timeOfDay + d + 3) % 3);
            Row(ref row, "Volume", () => Mathf.RoundToInt(s.volume * 100f) + "%", d => s.volume = Mathf.Clamp01(s.volume + d * 0.1f));
            Row(ref row, "Control hints", () => s.showHints ? "On" : "Off", d => s.showHints = !s.showHints);
            UIFactory.MenuButton(p, "CALIBRATE TILT", new Vector2(0.5f, 0f), new Vector2(-260f, 70f), new Vector2(420f, 84f), () => { if (MobileInputManager.I != null) MobileInputManager.I.CalibrateTilt(); }, null, 32);
            UIFactory.MenuButton(p, "BACK", new Vector2(0.5f, 0f), new Vector2(260f, 70f), new Vector2(420f, 84f), () =>
            {
                SaveSystem.SaveSettings();
                settingsPanel.gameObject.SetActive(false);
                if (settingsBack != null) settingsBack(true);
            }, new Color(0.75f, 0.42f, 0.05f, 0.95f));
        }

        void Row(ref int row, string label, System.Func<string> value, System.Action<int> change)
        {
            float y = -row * 74f - 36f;
            UIFactory.Label(settingsList, label, 32, new Vector2(0f, 1f), new Vector2(240f, y), new Vector2(460f, 60f), TextAnchor.MiddleLeft, new Color(1, 1, 1, 0.9f));
            var val = UIFactory.Label(settingsList, value(), 32, new Vector2(1f, 1f), new Vector2(-280f, y), new Vector2(380f, 60f), TextAnchor.MiddleCenter, UIFactory.Accent, FontStyle.Bold);
            UIFactory.MenuButton(settingsList, "◄", new Vector2(1f, 1f), new Vector2(-510f, y), new Vector2(80f, 62f), () => { change(-1); val.text = value(); Changed(); }, null, 34);
            UIFactory.MenuButton(settingsList, "►", new Vector2(1f, 1f), new Vector2(-50f, y), new Vector2(80f, 62f), () => { change(1); val.text = value(); Changed(); }, null, 34);
            row++;
        }

        void Changed() { if (onSettingsChanged != null) onSettingsChanged(); }

        // ---------------- guide ----------------
        void BuildGuide(Transform root)
        {
            guide = UIFactory.Stretch(root, "Guide");
            UIFactory.Img(guide, new Color(0, 0, 0, 0.6f), null, true);
            var p = Panel(guide, "GuidePanel", new Vector2(1500f, 920f), new Vector2(0.5f, 0.5f), Vector2.zero);
            UIFactory.Label(p, "HOW TO DRIVE", 50, new Vector2(0.5f, 1f), new Vector2(0, -60f), new Vector2(900, 70), TextAnchor.MiddleCenter, UIFactory.Accent, FontStyle.Bold);
            UIFactory.Label(p,
                "MANUAL (default)\n" +
                "1. Press START. The diesel cranks and settles at ~850 rpm.\n" +
                "2. Hold the CLUTCH (top of the pedal = fully pressed), tap GEAR ▲ for 1st. Use RANGE for Low/High (only when stopped).\n" +
                "3. Release the P-BRAKE. Give a little ACCEL, then slide your finger DOWN the clutch to let it bite gently.\n" +
                "   Dump the clutch in a high gear and the engine will stall – just like a real tractor.\n" +
                "4. To change gear: clutch in, GEAR ▲/▼, clutch out. Shifting without the clutch grinds the gears.\n" +
                "5. HAND THR is a sticky throttle lever – set it and the governor holds that rpm, great for crawling in mud.\n\n" +
                "AUTOMATIC (Settings → Transmission)\n" +
                "Just ACCEL and BRAKE. The clutch and all 8 gears are handled for you. F / R changes direction (it brakes to a stop first).\n\n" +
                "TERRAIN TIPS\n" +
                "Mud & sand: low range, steady throttle, DIFF LOCK to stop one wheel spinning. Hills: pick a low gear before the climb.\n" +
                "Stuck or rolled over? Use RECOVER.   Drag on empty screen to look around.   CAMERA cycles six views.\n\n" +
                "Keyboard (editor): W/S accel/brake · A/D steer · Shift clutch · E/Q gear · R range · T F/R · I ignition · Space P-brake · F diff · L lights · H horn · C camera",
                25, new Vector2(0.5f, 1f), new Vector2(0, -470f), new Vector2(1400f, 720f), TextAnchor.UpperLeft, new Color(1, 1, 1, 0.92f));
            UIFactory.MenuButton(p, "GOT IT", new Vector2(0.5f, 0f), new Vector2(0, 70f), new Vector2(420f, 84f), () => guide.gameObject.SetActive(false), new Color(0.75f, 0.42f, 0.05f, 0.95f));
        }

        // ---------------- API ----------------
        public void HideAll()
        {
            main.gameObject.SetActive(false);
            pause.gameObject.SetActive(false);
            settingsPanel.gameObject.SetActive(false);
            guide.gameObject.SetActive(false);
        }

        public void ShowMain()
        {
            HideAll();
            main.gameObject.SetActive(true);
            var d = SaveSystem.Data;
            statsText.text = "Driven so far: " + d.odometerKm.ToString("0.0") + " km  ·  sessions: " + d.sessions;
        }

        public void ShowPause() { HideAll(); pause.gameObject.SetActive(true); }

        public void ShowSettings(bool fromPause)
        {
            settingsPanel.gameObject.SetActive(true);
            settingsPanel.SetAsLastSibling();
            settingsBack = ok => { if (fromPause) pause.gameObject.SetActive(true); };
        }

        public void ShowGuide() { guide.gameObject.SetActive(true); guide.SetAsLastSibling(); }
        public bool AnyOpen { get { return main.gameObject.activeSelf || pause.gameObject.activeSelf || settingsPanel.gameObject.activeSelf || guide.gameObject.activeSelf; } }
        public bool PauseOpen { get { return pause.gameObject.activeSelf || settingsPanel.gameObject.activeSelf; } }
    }
}
