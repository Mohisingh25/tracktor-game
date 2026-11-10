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
        public System.Action<int> onMapChosen, onMissionChosen;
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
            UIFactory.Label(main, "5 tractors · 20 missions · Himalayan ghat road", 26, new Vector2(0f, 1f), new Vector2(410f, -285f), new Vector2(760f, 40f), TextAnchor.MiddleLeft, new Color(1, 1, 1, 0.7f));
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
            float[] v = { sp.ratedPowerHp / 210f, sp.PeakTorqueNm / 860f, sp.TopSpeedKmh / 140f, sp.massKg / 4800f, sp.forwardSpeedsKmh.Length / 16f, sp.maxTowKg / 24000f };
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

        // ---------------- mission select (career) ----------------
        Image[] mCards; Text[] mCardTitle, mCardInfo;
        Text mdTitle, mdSub, mdStory, mdRules, mdReward, mdStatus, mdProgress;
        Button mdStart; Text mdStartLabel;
        int selMission;

        void BuildMapSelect(Transform root)
        {
            mapSelect = UIFactory.Stretch(root, "MissionSelect");
            UIFactory.Img(mapSelect, new Color(0, 0, 0, 0.55f), null, true);
            UIFactory.Label(mapSelect, "MISSIONS", 52, new Vector2(0f, 1f), new Vector2(330f, -70f), new Vector2(600f, 64f), TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
            mdProgress = UIFactory.Label(mapSelect, "", 26, new Vector2(0f, 1f), new Vector2(760f, -74f), new Vector2(600f, 40f), TextAnchor.MiddleLeft, UIFactory.Accent, FontStyle.Bold);
            int n = MissionCatalog.Count;
            mCards = new Image[n]; mCardTitle = new Text[n]; mCardInfo = new Text[n];
            const float W = 236f, H = 168f, Gap = 12f;
            for (int i = 0; i < n; i++)
            {
                int idx = i;
                int col = i % 5, row = i / 5;
                var card = UIFactory.Rect(mapSelect, "Mission" + i, new Vector2(0f, 1f), new Vector2(40f + col * (W + Gap) + W * 0.5f, -140f - row * (H + Gap) - H * 0.5f), new Vector2(W, H));
                mCards[i] = UIFactory.Img(card, UIFactory.ButtonColor, ProcTex.RoundRect(), true);
                var b = card.gameObject.AddComponent<Button>();
                b.targetGraphic = mCards[i];
                b.onClick.AddListener(() => SelectMission(idx));
                var d = MissionCatalog.All[i];
                UIFactory.Label(card, (i + 1).ToString(), 40, new Vector2(0f, 1f), new Vector2(34f, -32f), new Vector2(60f, 50f), TextAnchor.MiddleLeft, new Color(1, 1, 1, 0.35f), FontStyle.Bold);
                UIFactory.Label(card, d.map == 1 ? "GHAT" : "GAON", 18, new Vector2(1f, 1f), new Vector2(-46f, -24f), new Vector2(80f, 26f), TextAnchor.MiddleRight, d.map == 1 ? new Color(0.55f, 0.75f, 1f) : new Color(0.6f, 0.9f, 0.45f), FontStyle.Bold);
                mCardTitle[i] = UIFactory.Label(card, d.title, 23, new Vector2(0.5f, 0.5f), new Vector2(0f, 6f), new Vector2(W - 20f, 56f), TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
                mCardInfo[i] = UIFactory.Label(card, "", 19, new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(W - 16f, 30f), TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.8f));
            }
            // details
            var det = UIFactory.Rect(mapSelect, "Details", new Vector2(1f, 0.5f), new Vector2(-330f, -20f), new Vector2(600f, 900f));
            UIFactory.Img(det, UIFactory.PanelColor, ProcTex.RoundRect(), true);
            mdTitle = UIFactory.Label(det, "", 42, new Vector2(0.5f, 1f), new Vector2(0f, -56f), new Vector2(560f, 56f), TextAnchor.MiddleLeft, UIFactory.Accent, FontStyle.Bold);
            mdSub = UIFactory.Label(det, "", 26, new Vector2(0.5f, 1f), new Vector2(0f, -104f), new Vector2(560f, 36f), TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
            mdStory = UIFactory.Label(det, "", 23, new Vector2(0.5f, 1f), new Vector2(0f, -250f), new Vector2(560f, 230f), TextAnchor.UpperLeft, new Color(1, 1, 1, 0.88f));
            mdRules = UIFactory.Label(det, "", 22, new Vector2(0.5f, 1f), new Vector2(0f, -440f), new Vector2(560f, 130f), TextAnchor.UpperLeft, new Color(1f, 0.85f, 0.5f));
            mdReward = UIFactory.Label(det, "", 34, new Vector2(0.5f, 1f), new Vector2(0f, -540f), new Vector2(560f, 46f), TextAnchor.MiddleLeft, new Color(0.45f, 0.95f, 0.45f), FontStyle.Bold);
            mdStatus = UIFactory.Label(det, "", 22, new Vector2(0.5f, 1f), new Vector2(0f, -590f), new Vector2(560f, 40f), TextAnchor.MiddleLeft, new Color(1, 1, 1, 0.7f));
            mdStart = UIFactory.MenuButton(det, "START", new Vector2(0.5f, 0f), new Vector2(0f, 220f), new Vector2(520f, 96f), () => { if (MissionCatalog.Unlocked(selMission) && onMissionChosen != null) onMissionChosen(selMission); }, new Color(0.75f, 0.42f, 0.05f, 0.95f), 42);
            mdStartLabel = mdStart.GetComponentInChildren<Text>();
            UIFactory.MenuButton(det, "FREE ROAM · GAON", new Vector2(0.5f, 0f), new Vector2(-135f, 90f), new Vector2(255f, 76f), () => { if (onMapChosen != null) onMapChosen(0); }, new Color(0.22f, 0.4f, 0.16f, 0.95f), 24);
            UIFactory.MenuButton(det, "FREE ROAM · GHAT", new Vector2(0.5f, 0f), new Vector2(135f, 90f), new Vector2(255f, 76f), () => { if (onMapChosen != null) onMapChosen(1); }, new Color(0.22f, 0.3f, 0.48f, 0.95f), 24);
            UIFactory.MenuButton(mapSelect, "BACK", new Vector2(0f, 0f), new Vector2(140f, 70f), new Vector2(220f, 80f), () => { if (onMapBack != null) onMapBack(); }, null, 32);
        }

        void SelectMission(int i)
        {
            selMission = i;
            var d = MissionCatalog.All[i];
            bool open = MissionCatalog.Unlocked(i);
            mdTitle.text = (i + 1) + ". " + d.title;
            mdSub.text = d.subtitle + "  ·  " + (d.map == 1 ? "Himalayan Ghat" : "Village Farm") + "  ·  " + new string('★', d.difficulty) + new string('☆', 5 - d.difficulty);
            mdStory.text = d.story;
            mdRules.text = d.RulesText();
            mdReward.text = "Reward up to ₹" + Mathf.RoundToInt(d.reward * 1.3f).ToString("N0");
            float best = MissionCatalog.Best(i);
            mdStatus.text = !open ? "LOCKED – complete more missions to unlock" : MissionCatalog.Done(i) ? "✔ Completed  ·  best " + MissionRunner.FormatTime(best) : "Not completed yet";
            mdStart.interactable = open;
            if (mdStartLabel != null) mdStartLabel.text = open ? "START" : "LOCKED";
            RefreshCards();
        }

        void RefreshCards()
        {
            for (int i = 0; i < mCards.Length; i++)
            {
                bool open = MissionCatalog.Unlocked(i), done = MissionCatalog.Done(i);
                Color c = done ? new Color(0.16f, 0.36f, 0.16f, 0.95f) : open ? new Color(0.16f, 0.17f, 0.2f, 0.95f) : new Color(0.08f, 0.08f, 0.09f, 0.85f);
                if (i == selMission) c = new Color(0.75f, 0.42f, 0.05f, 0.95f);
                mCards[i].color = c;
                mCardTitle[i].color = open ? Color.white : new Color(1, 1, 1, 0.35f);
                var d = MissionCatalog.All[i];
                mCardInfo[i].text = !open ? "LOCKED" : (done ? "✔ " : "") + "₹" + d.reward.ToString("N0") + "  ·  " + MissionRunner.FormatTime(d.timeLimitSec);
            }
            mdProgress.text = MissionCatalog.DoneCount() + " / " + MissionCatalog.Count + " completed  ·  ₹" + SaveSystem.Data.money.ToString("N0");
        }

        public void ShowMissionSelect()
        {
            HideAll();
            mapSelect.gameObject.SetActive(true);
            // select the first open, unfinished mission
            int pick = 0;
            for (int i = 0; i < MissionCatalog.Count; i++) if (MissionCatalog.Unlocked(i) && !MissionCatalog.Done(i)) { pick = i; break; }
            SelectMission(pick);
        }

        public void ShowMapSelect(int current) { ShowMissionSelect(); }

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
