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
        public System.Action<string> onShowroomPick;
        public System.Action onShowroomConfirm, onShowroomBack, onMapBack;
        public System.Action<int> onMapChosen;
        RectTransform showroom, mapSelect;
        Text statsText;
        GameSettings s;

        public void Build()
        {
            s = SaveSystem.Settings;
            canvas = UIFactory.CreateCanvas("Menus", 20);
            canvas.transform.SetParent(transform, false);
            var root = canvas.transform;
            BuildMain(root);
            BuildShowroom(root);
            BuildMapSelect(root);
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

        void BuildMain(Transform root)
        {
            main = UIFactory.Stretch(root, "MainMenu");
            var strip = UIFactory.Rect(main, "Strip", new Vector2(0f, 0.5f), new Vector2(0, 0), new Vector2(820f, 1080f), new Vector2(0f, 0.5f));
            UIFactory.Img(strip, new Color(0.03f, 0.03f, 0.04f, 0.72f));
            UIFactory.Label(main, "INDIAN TRACTOR", 76, new Vector2(0f, 1f), new Vector2(410f, -130f), new Vector2(760f, 90f), TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
            UIFactory.Label(main, "SIMULATOR 3D", 76, new Vector2(0f, 1f), new Vector2(410f, -215f), new Vector2(760f, 90f), TextAnchor.MiddleLeft, UIFactory.Accent, FontStyle.Bold);
            UIFactory.Label(main, "5 tractors · implements & trolleys · Himalayan cargo route", 26, new Vector2(0f, 1f), new Vector2(410f, -285f), new Vector2(760f, 40f), TextAnchor.MiddleLeft, new Color(1, 1, 1, 0.7f));
            var a = new Vector2(0f, 1f);
            UIFactory.MenuButton(main, "PLAY", a, new Vector2(270f, -400f), new Vector2(460f, 100f), () => { if (onPlay != null) onPlay(); }, new Color(0.75f, 0.42f, 0.05f, 0.95f), 46);
            UIFactory.MenuButton(main, "SETTINGS", a, new Vector2(270f, -520f), new Vector2(460f, 90f), () => ShowSettings(false));
            UIFactory.MenuButton(main, "HOW TO DRIVE", a, new Vector2(270f, -630f), new Vector2(460f, 90f), () => ShowGuide());
            UIFactory.MenuButton(main, "QUIT", a, new Vector2(270f, -740f), new Vector2(460f, 90f), () => { if (onQuit != null) onQuit(); });
            statsText = UIFactory.Label(main, "", 26, new Vector2(0f, 0f), new Vector2(410f, 60f), new Vector2(760f, 40f), TextAnchor.MiddleLeft, new Color(1, 1, 1, 0.75f));
        }

        // ---------------- showroom (tractor selection) ----------------
        int showIdx;
        Text srBrand, srModel, srTag, srDesc, srStars, srBadge;
        Image[] srBars; Text[] srBarVals;
        static readonly string[] BarNames = { "POWER", "TORQUE", "TOP SPEED", "WEIGHT", "GEARS", "LIFT / TOW" };

        void BuildShowroom(Transform root)
        {
            showroom = UIFactory.Stretch(root, "Showroom");
            var left = UIFactory.Rect(showroom, "Card", new Vector2(0f, 0.5f), new Vector2(40f, 0f), new Vector2(760f, 960f), new Vector2(0f, 0.5f));
            UIFactory.Img(left, UIFactory.PanelColor, ProcTex.RoundRect());
            UIFactory.Label(left, "CHOOSE YOUR TRACTOR", 26, new Vector2(0.5f, 1f), new Vector2(0, -36f), new Vector2(700f, 34f), TextAnchor.MiddleLeft, new Color(1, 1, 1, 0.6f));
            srBadge = UIFactory.Label(left, "★ BEST TRACTOR IN THE GAME ★", 26, new Vector2(0.5f, 1f), new Vector2(0, -74f), new Vector2(700f, 34f), TextAnchor.MiddleLeft, new Color(1f, 0.8f, 0.2f), FontStyle.Bold);
            srBrand = UIFactory.Label(left, "", 68, new Vector2(0.5f, 1f), new Vector2(0, -140f), new Vector2(700f, 84f), TextAnchor.MiddleLeft, UIFactory.Accent, FontStyle.Bold);
            srModel = UIFactory.Label(left, "", 38, new Vector2(0.5f, 1f), new Vector2(0, -205f), new Vector2(700f, 48f), TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
            srTag = UIFactory.Label(left, "", 26, new Vector2(0.5f, 1f), new Vector2(0, -248f), new Vector2(700f, 36f), TextAnchor.MiddleLeft, new Color(1, 1, 1, 0.75f), FontStyle.Normal);
            srStars = UIFactory.Label(left, "", 34, new Vector2(0.5f, 1f), new Vector2(0, -292f), new Vector2(700f, 40f), TextAnchor.MiddleLeft, new Color(1f, 0.8f, 0.2f));
            srBars = new Image[BarNames.Length]; srBarVals = new Text[BarNames.Length];
            for (int i = 0; i < BarNames.Length; i++)
            {
                float y = -360f - i * 62f;
                UIFactory.Label(left, BarNames[i], 22, new Vector2(0f, 1f), new Vector2(120f, y), new Vector2(200f, 30f), TextAnchor.MiddleLeft, new Color(1, 1, 1, 0.75f));
                var bg = UIFactory.Rect(left, "BarBg", new Vector2(0f, 1f), new Vector2(235f, y), new Vector2(300f, 18f), new Vector2(0f, 0.5f));
                UIFactory.Img(bg, new Color(1, 1, 1, 0.12f));
                var fill = UIFactory.Rect(bg, "Fill", new Vector2(0f, 0.5f), Vector2.zero, new Vector2(300f, 18f), new Vector2(0f, 0.5f));
                srBars[i] = UIFactory.Img(fill, UIFactory.Accent, ProcTex.RoundRect());
                srBars[i].type = Image.Type.Filled; srBars[i].fillMethod = Image.FillMethod.Horizontal;
                srBarVals[i] = UIFactory.Label(left, "", 22, new Vector2(0f, 1f), new Vector2(640f, y), new Vector2(200f, 30f), TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
            }
            srDesc = UIFactory.Label(left, "", 22, new Vector2(0.5f, 0f), new Vector2(0, 170f), new Vector2(690f, 90f), TextAnchor.UpperLeft, new Color(1, 1, 1, 0.85f));
            UIFactory.MenuButton(left, "◄", new Vector2(0f, 0f), new Vector2(90f, 70f), new Vector2(120f, 84f), () => Cycle(-1), null, 44);
            UIFactory.MenuButton(left, "►", new Vector2(0f, 0f), new Vector2(230f, 70f), new Vector2(120f, 84f), () => Cycle(1), null, 44);
            UIFactory.MenuButton(left, "SELECT", new Vector2(1f, 0f), new Vector2(-190f, 70f), new Vector2(300f, 84f), () => { if (onShowroomConfirm != null) onShowroomConfirm(); }, new Color(0.75f, 0.42f, 0.05f, 0.95f), 40);
            UIFactory.MenuButton(showroom, "BACK", new Vector2(1f, 1f), new Vector2(-130f, -70f), new Vector2(200f, 80f), () => { if (onShowroomBack != null) onShowroomBack(); }, null, 32);
        }

        void Cycle(int d)
        {
            var ids = TractorCatalog.Ids;
            showIdx = (showIdx + d + ids.Length) % ids.Length;
            if (onShowroomPick != null) onShowroomPick(ids[showIdx]);
            FillShowroom(TractorCatalog.Get(ids[showIdx]));
        }

        void FillShowroom(TractorSpec sp)
        {
            srBrand.text = sp.brand;
            srBrand.color = new Color(sp.accentRgb[0], sp.accentRgb[1], sp.accentRgb[2]);
            if (sp.accentRgb[0] > 0.85f && sp.accentRgb[1] > 0.85f && sp.accentRgb[2] > 0.85f) srBrand.color = new Color(sp.bodyRgb[0] * 0.5f + 0.5f, sp.bodyRgb[1] * 0.5f + 0.5f, sp.bodyRgb[2] * 0.5f + 0.5f);
            srModel.text = sp.model + (sp.fourWheelDrive ? "  ·  4WD" : "  ·  2WD") + (sp.style == BodyStyle.Cabin ? "  ·  Cab" : "");
            srTag.text = "\u201C" + sp.tagline + "\u201D";
            srStars.text = new string('★', sp.stars) + new string('☆', 5 - sp.stars);
            srBadge.enabled = sp.flagship;
            srDesc.text = sp.description;
            float[] v = { sp.ratedPowerHp / 150f, sp.PeakTorqueNm / 650f, sp.TopSpeedKmh / 120f, sp.massKg / 4800f, sp.forwardSpeedsKmh.Length / 16f, sp.maxTowKg / 24000f };
            string[] t = { sp.ratedPowerHp + " HP", Mathf.RoundToInt(sp.PeakTorqueNm) + " Nm", sp.TopSpeedKmh + " km/h", sp.massKg + " kg",
                           sp.forwardSpeedsKmh.Length + "F + " + sp.reverseSpeedsKmh.Length + "R", Mathf.RoundToInt(sp.maxLiftKg) + " / " + Mathf.RoundToInt(sp.maxTowKg / 1000f) + " t" };
            for (int i = 0; i < srBars.Length; i++) { srBars[i].fillAmount = Mathf.Clamp01(v[i]); srBarVals[i].text = t[i]; srBars[i].color = sp.flagship ? new Color(1f, 0.75f, 0.2f) : UIFactory.Accent; }
        }

        public void ShowShowroom(string currentId)
        {
            HideAll();
            showroom.gameObject.SetActive(true);
            showIdx = System.Array.IndexOf(TractorCatalog.Ids, currentId);
            if (showIdx < 0) showIdx = 0;
            FillShowroom(TractorCatalog.Get(TractorCatalog.Ids[showIdx]));
        }

        // ---------------- map select ----------------
        void BuildMapSelect(Transform root)
        {
            mapSelect = UIFactory.Stretch(root, "MapSelect");
            UIFactory.Img(mapSelect, new Color(0, 0, 0, 0.45f), null, true);
            UIFactory.Label(mapSelect, "WHERE TO?", 56, new Vector2(0.5f, 1f), new Vector2(0, -110f), new Vector2(900f, 70f), TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            string[] titles = { "VILLAGE FARM", "HIMALAYAN CARGO ROUTE" };
            string[] desc = {
                "Proving ground, village and farm.\nHitch rotavator, cultivator, plough, seed drill or sprayer from the implement yard and work the field (paid per m²).\nCargo job: sugarcane to the mill.",
                "3 km ghat road with hairpins, cliff drops, mud, rocks and a river ford.\nHitch a trolley at the depot, load 4.5 t of apples and deliver them up the mountain to Pahadgaon.\nHeavy, steep and dangerous – low gears!" };
            Color[] cols = { new Color(0.25f, 0.45f, 0.18f, 0.95f), new Color(0.25f, 0.33f, 0.5f, 0.95f) };
            for (int i = 0; i < 2; i++)
            {
                int map = i;
                var card = UIFactory.Rect(mapSelect, "Map" + i, new Vector2(0.5f, 0.5f), new Vector2(i == 0 ? -400f : 400f, -20f), new Vector2(720f, 600f));
                UIFactory.Img(card, cols[i], ProcTex.RoundRect(), true);
                UIFactory.Label(card, titles[i], 42, new Vector2(0.5f, 1f), new Vector2(0, -60f), new Vector2(660f, 60f), TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
                UIFactory.Label(card, desc[i], 26, new Vector2(0.5f, 0.5f), new Vector2(0, 10f), new Vector2(640f, 320f), TextAnchor.UpperCenter, new Color(1, 1, 1, 0.92f));
                UIFactory.MenuButton(card, "DRIVE", new Vector2(0.5f, 0f), new Vector2(0, 70f), new Vector2(320f, 90f), () => { if (onMapChosen != null) onMapChosen(map); }, new Color(0.75f, 0.42f, 0.05f, 0.95f), 40);
            }
            UIFactory.MenuButton(mapSelect, "BACK", new Vector2(1f, 1f), new Vector2(-130f, -70f), new Vector2(200f, 80f), () => { if (onMapBack != null) onMapBack(); }, null, 32);
        }

        public void ShowMapSelect(int current) { HideAll(); mapSelect.gameObject.SetActive(true); }

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
            UIFactory.MenuButton(p, "RESTART MAP", a, new Vector2(0, -270f), bs, () => { if (onRestart != null) onRestart(); });
            UIFactory.MenuButton(p, "RECOVER TRACTOR (₹500)", a, new Vector2(0, -370f), bs, () => { if (onRecover != null) onRecover(); }, null, 30);
            UIFactory.MenuButton(p, "REFUEL & REPAIR (₹)", a, new Vector2(0, -470f), bs, () => { if (onService != null) onService(); }, null, 32);
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
                "IMPLEMENTS & TROLLEYS\n" +
                "Reverse until your hitch is right at the implement – the ATTACH button lights green. LOWER drops it into the soil, PTO spins rotavators/sprayers. Trolleys: LOAD in the yellow bay, TIP to unload.\n\n" +
                "Keyboard (editor): G attach · V lift · P PTO · B tip · N load · W/S accel/brake · A/D steer · Shift clutch · E/Q gear · R range · T F/R · I ignition · Space P-brake · F diff · L lights · H horn · C camera",
                22, new Vector2(0.5f, 1f), new Vector2(0, -470f), new Vector2(1400f, 720f), TextAnchor.UpperLeft, new Color(1, 1, 1, 0.92f));
            UIFactory.MenuButton(p, "GOT IT", new Vector2(0.5f, 0f), new Vector2(0, 70f), new Vector2(420f, 84f), () => guide.gameObject.SetActive(false), new Color(0.75f, 0.42f, 0.05f, 0.95f));
        }

        // ---------------- API ----------------
        public void HideAll()
        {
            main.gameObject.SetActive(false);
            pause.gameObject.SetActive(false);
            settingsPanel.gameObject.SetActive(false);
            guide.gameObject.SetActive(false);
            if (showroom != null) showroom.gameObject.SetActive(false);
            if (mapSelect != null) mapSelect.gameObject.SetActive(false);
        }

        public void ShowMain()
        {
            HideAll();
            main.gameObject.SetActive(true);
            var d = SaveSystem.Data;
            statsText.text = "₹ " + d.money.ToString("N0") + "   ·   " + d.odometerKm.ToString("0.0") + " km driven   ·   " + d.deliveries + " deliveries";
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
