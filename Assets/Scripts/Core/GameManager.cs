using UnityEngine;

namespace TractorSim
{
    public enum GameState { Menu, Playing, Paused }

    /// <summary>
    /// Builds the whole game at runtime (world, tractor, camera, audio, UI) and runs the state machine:
    /// Menu → Playing ⇄ Paused → Menu. Applies settings and autosaves progress.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager I { get; private set; }
        public GameState State { get; private set; }

        TractorController tractor;
        CameraManager cams;
        HUDController hud;
        MenuController menus;
        Light sun;
        EngineSoundSynth engineAudio;
        float autosaveTimer;
        int nativeW, nativeH;
        float sessionStartOdo;

        void Awake()
        {
            I = this;
            SaveSystem.Load();
            nativeW = Display.main != null ? Display.main.systemWidth : Screen.width;
            nativeH = Display.main != null ? Display.main.systemHeight : Screen.height;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Screen.orientation = ScreenOrientation.LandscapeLeft;
            Screen.autorotateToLandscapeLeft = true; Screen.autorotateToLandscapeRight = true;
            Screen.autorotateToPortrait = false; Screen.autorotateToPortraitUpsideDown = false;
            Screen.orientation = ScreenOrientation.AutoRotation;

            // physics: 100 Hz for stable heavy-vehicle simulation
            Time.fixedDeltaTime = 0.01f;
            Time.maximumDeltaTime = 0.1f;
            Physics.defaultSolverIterations = 8;
            Physics.defaultSolverVelocityIterations = 2;
            Physics.gravity = new Vector3(0, -9.81f, 0);

            BuildWorld();
        }

        void BuildWorld()
        {
            // ---- input ----
            new GameObject("Input").AddComponent<MobileInputManager>();
            UIFactory.EnsureEventSystem();

            // ---- lighting & sky ----
            var sunGo = new GameObject("Sun");
            sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            RenderSettings.skybox = MatLib.Sky();
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;

            // ---- world ----
            TestGroundBuilder.Build();

            // ---- tractor ----
            var spec = TractorSpec.Default();
            tractor = TractorBuilder.Build(spec, TestGroundBuilder.SpawnPoint, TestGroundBuilder.SpawnRotation, new Color(0.66f, 0.07f, 0.05f));
            tractor.RestoreProgress(SaveSystem.Data);
            tractor.InputEnabled = false;
            sessionStartOdo = 0f;

            // ---- audio ----
            var am = new GameObject("Audio").AddComponent<AudioManager>();
            engineAudio = tractor.gameObject.AddComponent<EngineSoundSynth>();
            engineAudio.tractor = tractor;
            tractor.OnShift += () => am.Clunk();
            tractor.OnGrind += () => am.Grind();
            tractor.OnIndicatorTick += () => { if (tractor.Indicator != 0) am.Tick(); };
            tractor.OnImpact += () => am.Impact();

            // ---- camera ----
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.farClipPlane = 900f;
            camGo.AddComponent<AudioListener>();
            cams = camGo.AddComponent<CameraManager>();
            cams.Init(cam);
            cams.SetTarget(tractor);
            camGo.transform.position = tractor.transform.position + new Vector3(6, 3, -6);

            // ---- UI ----
            hud = new GameObject("HUD").AddComponent<HUDController>();
            hud.Build(tractor, SaveSystem.Settings);
            hud.onPause = Pause;
            hud.onCamera = () => cams.Next();
            hud.onRecover = () => tractor.Recover();

            menus = new GameObject("Menus").AddComponent<MenuController>();
            menus.Build(spec);
            menus.onPlay = StartDrive;
            menus.onResume = Resume;
            menus.onRestart = Restart;
            menus.onMainMenu = ToMainMenu;
            menus.onRecover = () => { tractor.Recover(); Resume(); };
            menus.onService = () => { tractor.Refuel(); tractor.Repair(); hud.Toast("Tank filled & tractor repaired", false); Resume(); };
            menus.onQuit = () => { SaveProgress(); Application.Quit(); };
            menus.onSettingsChanged = () => ApplySettings();

            ApplySettings();
            ToMainMenu();
        }

        // ---------------- state machine ----------------
        public void StartDrive()
        {
            menus.HideAll();
            hud.SetVisible(true);
            hud.ResetControls();
            tractor.InputEnabled = true;
            cams.SetMode(CamMode.Chase);
            State = GameState.Playing;
            Time.timeScale = 1f;
            AudioListener.pause = false;
            SaveSystem.Data.sessions++;
            SaveSystem.SaveProgress();
            if (SaveSystem.Settings.showHints && SaveSystem.Data.sessions <= 1) menus.ShowGuide();
            hud.Toast(SaveSystem.Settings.automatic ? "Press START, then ACCEL to drive" : "Press START · clutch in · GEAR ▲ · release P-BRAKE", false);
        }

        public void Pause()
        {
            if (State != GameState.Playing) return;
            State = GameState.Paused;
            Time.timeScale = 0f;
            AudioListener.pause = true;
            tractor.InputEnabled = false;
            hud.ResetControls();
            menus.ShowPause();
            SaveProgress();
        }

        public void Resume()
        {
            menus.HideAll();
            State = GameState.Playing;
            Time.timeScale = 1f;
            AudioListener.pause = false;
            tractor.InputEnabled = true;
        }

        public void Restart()
        {
            SaveProgress();
            tractor.ResetTractor();
            hud.ResetControls();
            cams.SetMode(CamMode.Chase);
            Resume();
        }

        public void ToMainMenu()
        {
            SaveProgress();
            State = GameState.Menu;
            Time.timeScale = 1f;
            AudioListener.pause = false;
            tractor.ResetTractor(false);
            tractor.InputEnabled = false;
            hud.ResetControls();
            hud.SetVisible(false);
            cams.SetMode(CamMode.Menu);
            menus.ShowMain();
        }

        void Update()
        {
            var inp = MobileInputManager.I;
            if (inp != null)
            {
                if (inp.PausePressed())
                {
                    if (State == GameState.Playing) Pause();
                    else if (State == GameState.Paused) Resume();
                }
                if (State == GameState.Playing && inp.CameraNext()) cams.Next();
            }

            if (State == GameState.Playing)
            {
                autosaveTimer += Time.unscaledDeltaTime;
                if (autosaveTimer > 15f) { autosaveTimer = 0f; SaveProgress(); }
            }
        }

        void SaveProgress()
        {
            if (tractor == null) return;
            var d = SaveSystem.Data;
            float odo = tractor.Physics.Odometer / 1000f;
            d.odometerKm += odo - sessionStartOdo;
            sessionStartOdo = odo;
            d.fuelL = tractor.Engine.FuelL;
            d.damage = tractor.Damage;
            SaveSystem.SaveProgress();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) { SaveProgress(); if (State == GameState.Playing) Pause(); }
        }

        void OnApplicationQuit() { SaveProgress(); }

        // ---------------- settings ----------------
        public void ApplySettings()
        {
            var s = SaveSystem.Settings;
            tractor.ApplySettings(s);
            hud.ApplySettings(s);
            if (AudioManager.I != null) AudioManager.I.SetVolume(s.volume);

            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = s.fps;

            switch (s.graphics)
            {
                case 0: QualitySettings.shadows = ShadowQuality.Disable; QualitySettings.shadowDistance = 0f; QualitySettings.antiAliasing = 0; QualitySettings.pixelLightCount = 1; QualitySettings.lodBias = 0.5f; break;
                case 1: QualitySettings.shadows = ShadowQuality.HardOnly; QualitySettings.shadowResolution = ShadowResolution.Low; QualitySettings.shadowDistance = 45f; QualitySettings.antiAliasing = 0; QualitySettings.pixelLightCount = 2; QualitySettings.lodBias = 0.8f; break;
                case 2: QualitySettings.shadows = ShadowQuality.All; QualitySettings.shadowResolution = ShadowResolution.Medium; QualitySettings.shadowDistance = 80f; QualitySettings.antiAliasing = 2; QualitySettings.pixelLightCount = 3; QualitySettings.lodBias = 1f; break;
                default: QualitySettings.shadows = ShadowQuality.All; QualitySettings.shadowResolution = ShadowResolution.High; QualitySettings.shadowDistance = 130f; QualitySettings.antiAliasing = 4; QualitySettings.pixelLightCount = 4; QualitySettings.lodBias = 1.5f; break;
            }
            QualitySettings.shadowCascades = s.graphics >= 3 ? 2 : 1;

            if (Application.isMobilePlatform && nativeW > 0)
            {
                int w = Mathf.RoundToInt(nativeW * s.renderScale), h = Mathf.RoundToInt(nativeH * s.renderScale);
                if (w != Screen.width || h != Screen.height) Screen.SetResolution(w, h, FullScreenMode.FullScreenWindow);
            }

            ApplyTimeOfDay(s.timeOfDay);
        }

        void ApplyTimeOfDay(int tod)
        {
            var sky = RenderSettings.skybox;
            switch (tod)
            {
                case 1: // evening – low warm sun, dusty haze
                    sun.transform.rotation = Quaternion.Euler(9f, -65f, 0f);
                    sun.color = new Color(1f, 0.62f, 0.36f); sun.intensity = 0.95f;
                    RenderSettings.ambientSkyColor = new Color(0.55f, 0.48f, 0.45f);
                    RenderSettings.ambientEquatorColor = new Color(0.52f, 0.4f, 0.3f);
                    RenderSettings.ambientGroundColor = new Color(0.18f, 0.14f, 0.1f);
                    RenderSettings.fogColor = new Color(0.78f, 0.6f, 0.46f);
                    RenderSettings.fogStartDistance = 40f; RenderSettings.fogEndDistance = 520f;
                    if (sky != null && sky.HasProperty("_AtmosphereThickness")) { sky.SetFloat("_AtmosphereThickness", 1.6f); sky.SetFloat("_Exposure", 1.1f); }
                    break;
                case 2: // night – moonlight, headlights matter
                    sun.transform.rotation = Quaternion.Euler(35f, 150f, 0f);
                    sun.color = new Color(0.55f, 0.65f, 1f); sun.intensity = 0.12f;
                    RenderSettings.ambientSkyColor = new Color(0.05f, 0.07f, 0.12f);
                    RenderSettings.ambientEquatorColor = new Color(0.04f, 0.05f, 0.08f);
                    RenderSettings.ambientGroundColor = new Color(0.02f, 0.02f, 0.03f);
                    RenderSettings.fogColor = new Color(0.03f, 0.04f, 0.07f);
                    RenderSettings.fogStartDistance = 15f; RenderSettings.fogEndDistance = 220f;
                    if (sky != null && sky.HasProperty("_AtmosphereThickness")) { sky.SetFloat("_AtmosphereThickness", 0.4f); sky.SetFloat("_Exposure", 0.12f); }
                    break;
                default: // hazy North-Indian day
                    sun.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
                    sun.color = new Color(1f, 0.95f, 0.86f); sun.intensity = 1.2f;
                    RenderSettings.ambientSkyColor = new Color(0.62f, 0.68f, 0.78f);
                    RenderSettings.ambientEquatorColor = new Color(0.58f, 0.56f, 0.5f);
                    RenderSettings.ambientGroundColor = new Color(0.25f, 0.22f, 0.17f);
                    RenderSettings.fogColor = new Color(0.78f, 0.79f, 0.76f);
                    RenderSettings.fogStartDistance = 60f; RenderSettings.fogEndDistance = 650f;
                    if (sky != null && sky.HasProperty("_AtmosphereThickness")) { sky.SetFloat("_AtmosphereThickness", 1.15f); sky.SetFloat("_Exposure", 1.25f); }
                    break;
            }
            tractor.SetHeadlights(tod == 2);
            DynamicGI.UpdateEnvironment();
        }
    }
}
