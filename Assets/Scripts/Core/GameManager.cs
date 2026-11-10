using UnityEngine;

namespace TractorSim
{
    public enum GameState { Menu, Showroom, MapSelect, Playing, Paused }

    /// <summary>
    /// Builds the game at runtime and runs it: main menu → tractor showroom → map select →
    /// driving (missions, field work, free roam) ⇄ pause. Applies settings, autosaves progress.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager I { get; private set; }
        public GameState State { get; private set; }
        public static readonly string[] MapNames = { "Village Farm", "Himalayan Ghat" };

        TractorController tractor;
        CameraManager cams;
        HUDController hud;
        MenuController menus;
        Light sun;
        Transform world;
        int builtMap = -1;
        MissionRunner mission;
        int missionIndex = -1;
        MissionDef CurrentDef { get { return missionIndex >= 0 ? MissionCatalog.All[missionIndex] : null; } }
        float autosaveTimer;
        int nativeW, nativeH;
        float sessionStartOdo;
        GameObject cameraRig;

        void Awake()
        {
            I = this;
            SaveSystem.Load();
            nativeW = Display.main != null ? Display.main.systemWidth : Screen.width;
            nativeH = Display.main != null ? Display.main.systemHeight : Screen.height;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Screen.autorotateToLandscapeLeft = true; Screen.autorotateToLandscapeRight = true;
            Screen.autorotateToPortrait = false; Screen.autorotateToPortraitUpsideDown = false;
            Screen.orientation = ScreenOrientation.AutoRotation;

            Time.fixedDeltaTime = 0.01f;
            Time.maximumDeltaTime = 0.1f;
            Physics.defaultSolverIterations = 8;
            Physics.defaultSolverVelocityIterations = 2;
            Physics.gravity = new Vector3(0, -9.81f, 0);

            BuildPersistent();
            BuildMap(SaveSystem.Data.mapId);
            SpawnTractor(SaveSystem.Data.tractorId);
            ApplySettings();
            ToMainMenu();
        }

        // ------------------------------------------------------------------ setup
        void BuildPersistent()
        {
            new GameObject("Input").AddComponent<MobileInputManager>();
            UIFactory.EnsureEventSystem();

            var sunGo = new GameObject("Sun");
            sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            var cat = ArtCatalog.Load();
            RenderSettings.skybox = cat != null && cat.skybox != null ? cat.skybox : MatLib.Sky();
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;

            new GameObject("Audio").AddComponent<AudioManager>();

            // camera: the rig prefab carries post-processing (tone mapping, bloom, grading) when available
            var rigPrefab = Resources.Load<GameObject>("CameraRig");
            cameraRig = rigPrefab != null ? Instantiate(rigPrefab) : new GameObject("Main Camera");
            cameraRig.name = "Main Camera";
            cameraRig.tag = "MainCamera";
            var cam = cameraRig.GetComponent<Camera>();
            if (cam == null) cam = cameraRig.AddComponent<Camera>();
            cam.farClipPlane = 1400f;
            cam.allowHDR = true;
            if (cameraRig.GetComponent<AudioListener>() == null) cameraRig.AddComponent<AudioListener>();
            cams = cameraRig.AddComponent<CameraManager>();
            cams.Init(cam);

            hud = new GameObject("HUD").AddComponent<HUDController>();
            hud.Build(SaveSystem.Settings);
            hud.onPause = Pause;
            hud.onCamera = () => cams.Next();
            hud.onRecover = Recover;
            Career.OnEarn = m => hud.Toast(m, false);

            menus = new GameObject("Menus").AddComponent<MenuController>();
            menus.Build();
            menus.onPlay = OpenShowroom;
            menus.onShowroomPick = PreviewTractor;
            menus.onShowroomConfirm = () => { SaveSystem.Data.tractorId = tractor.Spec.id; SaveSystem.SaveProgress(); OpenMapSelect(); };
            menus.onShowroomBack = ToMainMenu;
            menus.onMapChosen = StartFreeRoam;
            menus.onMissionChosen = StartMission;
            menus.onMapBack = OpenShowroom;
            menus.onResume = Resume;
            menus.onRestart = Restart;
            menus.onMainMenu = ToMainMenu;
            menus.onRecover = () => { Recover(); Resume(); };
            menus.onService = Service;
            menus.onQuit = () => { SaveProgress(); Application.Quit(); };
            menus.onSettingsChanged = () => ApplySettings();
        }

        void BuildMap(int map)
        {
            if (builtMap == map && world != null) return;
            if (tractor != null) { var am = tractor.GetComponent<AttachmentManager>(); if (am != null) am.ReleaseForRebuild(); }
            if (world != null) Destroy(world.gameObject);
            foreach (var imp in Implement.All.ToArray()) if (imp != null && imp.transform.root != (tractor != null ? tractor.transform : null)) Destroy(imp.gameObject);
            Implement.All.Clear();
            world = map == 1 ? MountainBuilder.Build() : TestGroundBuilder.Build();
            builtMap = map;
            mission = null;
            ApplyTimeOfDay(SaveSystem.Settings.timeOfDay);
        }

        Vector3 SpawnPos { get { SpawnPoint sp; if (CurrentDef != null && WorldAnchors.TryGetSpawn(CurrentDef.spawn, out sp)) return sp.pos; return builtMap == 1 ? MountainBuilder.Spawn : TestGroundBuilder.SpawnPoint; } }
        Quaternion SpawnRot { get { SpawnPoint sp; if (CurrentDef != null && WorldAnchors.TryGetSpawn(CurrentDef.spawn, out sp)) return sp.rot; return builtMap == 1 ? MountainBuilder.SpawnRot : TestGroundBuilder.SpawnRotation; } }

        void SpawnTractor(string id)
        {
            if (tractor != null)
            {
                var am = tractor.GetComponent<AttachmentManager>(); if (am != null) am.ReleaseForRebuild();
                SaveProgress();
                Destroy(tractor.gameObject);
            }
            var spec = TractorCatalog.Get(id);
            tractor = TractorBuilder.Build(spec, SpawnPos + Vector3.up * (spec.bodyScale - 1f) * 0.6f, SpawnRot);
            tractor.RestoreProgress(SaveSystem.Data);
            tractor.InputEnabled = false;
            sessionStartOdo = 0f;
            var am2 = tractor.GetComponent<AttachmentManager>();
            am2.OnMessage += (m, w) => hud.Toast(m, w);
            var synth = tractor.gameObject.AddComponent<EngineSoundSynth>();
            synth.tractor = tractor;
            synth.masterVolume = 0.9f;
            var a = AudioManager.I;
            tractor.OnShift += () => a.Clunk();
            tractor.OnGrind += () => a.Grind();
            tractor.OnIndicatorTick += () => { if (tractor.Indicator != 0) a.Tick(); };
            tractor.OnImpact += () => a.Impact();
            am2.OnCouple += () => a.Clunk();
            cams.SetTarget(tractor);
            hud.Bind(tractor);
            tractor.ApplySettings(SaveSystem.Settings);
            tractor.SetHeadlights(SaveSystem.Settings.timeOfDay == 2);
        }

        // ------------------------------------------------------------------ flow
        void OpenShowroom()
        {
            State = GameState.Showroom;
            menus.ShowShowroom(tractor.Spec.id);
            cams.SetMode(CamMode.Menu);
        }

        void PreviewTractor(string id)
        {
            if (tractor != null && tractor.Spec.id == id) return;
            SpawnTractor(id);
            tractor.InputEnabled = false;
            hud.SetVisible(false);
            cams.SetMode(CamMode.Menu);
        }

        void OpenMapSelect()
        {
            State = GameState.MapSelect;
            menus.ShowMissionSelect();
        }

        public void StartFreeRoam(int map) { missionIndex = -1; StartDrive(map); }

        public void StartMission(int index)
        {
            if (index < 0 || index >= MissionCatalog.Count || !MissionCatalog.Unlocked(index)) return;
            missionIndex = index;
            StartDrive(MissionCatalog.All[index].map);
        }

        public void StartDrive(int map)
        {
            SaveSystem.Data.mapId = map;
            menus.HideAll();
            ClearMission();
            // a mission always starts on a freshly built map: trolleys parked, field unworked, yards empty
            if (builtMap != map || missionIndex >= 0)
            {
                builtMap = -1;
                BuildMap(map);
                SpawnTractor(tractor.Spec.id);
            }
            else tractor.ResetTractor();
            hud.SetVisible(true);
            hud.ResetControls();
            tractor.InputEnabled = true;
            cams.SetMode(CamMode.Chase);
            State = GameState.Playing;
            Time.timeScale = 1f;
            AudioListener.pause = false;
            SaveSystem.Data.sessions++;
            SaveSystem.SaveProgress();
            ApplyMissionEnvironment();
            BeginMission();
            if (SaveSystem.Settings.showHints && SaveSystem.Data.sessions <= 1) menus.ShowGuide();
            hud.Toast(tractor.Spec.displayName + " · " + (CurrentDef != null ? CurrentDef.title : MapNames[map] + " – free roam"), false);
        }

        void ClearMission()
        {
            if (mission != null) { mission.Abort(); Destroy(mission.gameObject); mission = null; }
            hud.SetMission(null);
        }

        void BeginMission()
        {
            ClearMission();
            var def = CurrentDef;
            if (def == null || def.map != builtMap) return;
            mission = MissionRunner.Create(def, missionIndex);
            mission.OnMessage = (m, w) => hud.Toast(m, w);
            mission.OnCheckpoint = () => { if (AudioManager.I != null) AudioManager.I.Click(); };
            mission.Begin(tractor);
            hud.SetMission(mission);
        }

        /// <summary>Night, fog and traffic for the current mission (free roam uses the settings).</summary>
        void ApplyMissionEnvironment()
        {
            var def = CurrentDef;
            ApplyTimeOfDay(def != null && def.night ? 2 : SaveSystem.Settings.timeOfDay);
            if (def != null && def.fog)
            {
                RenderSettings.fogColor = def.night ? new Color(0.05f, 0.06f, 0.08f) : new Color(0.72f, 0.74f, 0.76f);
                RenderSettings.fogStartDistance = 4f;
                RenderSettings.fogEndDistance = def.night ? 55f : 75f;
            }
            if (TrafficManager.I != null)
            {
                int density = def != null && def.traffic >= 0 ? def.traffic : TrafficManager.DefaultDensity;
                if (TrafficManager.I.Density != density) TrafficManager.I.SetDensity(density);
            }
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

        /// <summary>Restart the map: rebuilds trolleys, implements, the field and the mission.</summary>
        public void Restart()
        {
            SaveProgress();
            int map = builtMap;
            builtMap = -1;
            BuildMap(map);
            SpawnTractor(tractor.Spec.id);
            hud.ResetControls();
            cams.SetMode(CamMode.Chase);
            ApplyMissionEnvironment();
            BeginMission();
            Resume();
        }

        void Recover()
        {
            const int cost = 500;
            long m = SaveSystem.Data.money;
            int charged = (int)System.Math.Min(cost, m);
            Career.Spend(charged);
            tractor.Recover();
            var am = tractor.GetComponent<AttachmentManager>();
            if (am != null && am.CurrentTrolley != null && am.CurrentTrolley.IsTipped) am.Detach();
            hud.Toast("Recovery truck called: −₹" + charged, true);
        }

        void Service()
        {
            float litres = tractor.Spec.fuelTankL - tractor.Engine.FuelL;
            int cost = Mathf.RoundToInt(litres * 95f + tractor.Damage * 40f);
            if (!Career.Spend(Mathf.Min(cost, (int)SaveSystem.Data.money))) { }
            tractor.Refuel(); tractor.Repair();
            hud.Toast("Diesel filled & repaired: −₹" + cost, false);
            Resume();
        }

        public void ToMainMenu()
        {
            SaveProgress();
            ClearMission();
            missionIndex = -1;
            ApplyTimeOfDay(SaveSystem.Settings.timeOfDay);
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

        // ------------------------------------------------------------------ settings
        public void ApplySettings()
        {
            var s = SaveSystem.Settings;
            if (tractor != null) tractor.ApplySettings(s);
            hud.ApplySettings(s);
            if (AudioManager.I != null) AudioManager.I.SetVolume(s.volume);

            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = s.fps;

            switch (s.graphics)
            {
                case 0: QualitySettings.shadows = ShadowQuality.Disable; QualitySettings.shadowDistance = 0f; QualitySettings.antiAliasing = 0; QualitySettings.pixelLightCount = 1; QualitySettings.lodBias = 0.5f; break;
                case 1: QualitySettings.shadows = ShadowQuality.HardOnly; QualitySettings.shadowResolution = ShadowResolution.Low; QualitySettings.shadowDistance = 50f; QualitySettings.antiAliasing = 0; QualitySettings.pixelLightCount = 2; QualitySettings.lodBias = 0.8f; break;
                case 2: QualitySettings.shadows = ShadowQuality.All; QualitySettings.shadowResolution = ShadowResolution.Medium; QualitySettings.shadowDistance = 90f; QualitySettings.antiAliasing = 2; QualitySettings.pixelLightCount = 3; QualitySettings.lodBias = 1.2f; break;
                default: QualitySettings.shadows = ShadowQuality.All; QualitySettings.shadowResolution = ShadowResolution.High; QualitySettings.shadowDistance = 150f; QualitySettings.antiAliasing = 4; QualitySettings.pixelLightCount = 4; QualitySettings.lodBias = 2f; break;
            }
            QualitySettings.shadowCascades = s.graphics >= 2 ? 2 : 1;

            // post-processing (film tone mapping, bloom, colour grading) from Medium up
            foreach (var b in cameraRig.GetComponents<Behaviour>())
                if (b != null && b.GetType().Name == "PostProcessLayer") b.enabled = s.graphics >= 1;

            var terrain = MountainBuilder.ActiveTerrain;
            if (terrain != null)
            {
                terrain.detailObjectDistance = new[] { 0f, 45f, 75f, 110f }[s.graphics];
                terrain.heightmapPixelError = new[] { 9f, 6f, 4f, 2.5f }[s.graphics];
                terrain.basemapDistance = new[] { 150f, 250f, 400f, 700f }[s.graphics];
            }

            if (Application.isMobilePlatform && nativeW > 0)
            {
                int w = Mathf.RoundToInt(nativeW * s.renderScale), h = Mathf.RoundToInt(nativeH * s.renderScale);
                if (w != Screen.width || h != Screen.height) Screen.SetResolution(w, h, FullScreenMode.FullScreenWindow);
            }
            if (CurrentDef != null) ApplyMissionEnvironment(); else ApplyTimeOfDay(s.timeOfDay);
        }

        void ApplyTimeOfDay(int tod)
        {
            var sky = RenderSettings.skybox;
            bool mountain = builtMap == 1;
            switch (tod)
            {
                case 1:
                    sun.transform.rotation = Quaternion.Euler(9f, -65f, 0f);
                    sun.color = new Color(1f, 0.62f, 0.36f); sun.intensity = 0.95f;
                    RenderSettings.ambientSkyColor = new Color(0.55f, 0.48f, 0.45f);
                    RenderSettings.ambientEquatorColor = new Color(0.52f, 0.4f, 0.3f);
                    RenderSettings.ambientGroundColor = new Color(0.18f, 0.14f, 0.1f);
                    RenderSettings.fogColor = new Color(0.78f, 0.6f, 0.46f);
                    RenderSettings.fogStartDistance = mountain ? 60f : 40f; RenderSettings.fogEndDistance = mountain ? 900f : 520f;
                    if (sky != null && sky.HasProperty("_AtmosphereThickness")) { sky.SetFloat("_AtmosphereThickness", 1.6f); sky.SetFloat("_Exposure", 1.1f); }
                    break;
                case 2:
                    sun.transform.rotation = Quaternion.Euler(35f, 150f, 0f);
                    sun.color = new Color(0.55f, 0.65f, 1f); sun.intensity = 0.12f;
                    RenderSettings.ambientSkyColor = new Color(0.05f, 0.07f, 0.12f);
                    RenderSettings.ambientEquatorColor = new Color(0.04f, 0.05f, 0.08f);
                    RenderSettings.ambientGroundColor = new Color(0.02f, 0.02f, 0.03f);
                    RenderSettings.fogColor = new Color(0.03f, 0.04f, 0.07f);
                    RenderSettings.fogStartDistance = 15f; RenderSettings.fogEndDistance = 260f;
                    if (sky != null && sky.HasProperty("_AtmosphereThickness")) { sky.SetFloat("_AtmosphereThickness", 0.4f); sky.SetFloat("_Exposure", 0.12f); }
                    break;
                default:
                    sun.transform.rotation = Quaternion.Euler(mountain ? 40f : 48f, -35f, 0f);
                    sun.color = new Color(1f, 0.95f, 0.86f); sun.intensity = 1.2f;
                    RenderSettings.ambientSkyColor = new Color(0.62f, 0.68f, 0.78f);
                    RenderSettings.ambientEquatorColor = new Color(0.58f, 0.56f, 0.5f);
                    RenderSettings.ambientGroundColor = new Color(0.25f, 0.22f, 0.17f);
                    // mountain haze: blue-grey, starts further out so the valleys read with depth
                    RenderSettings.fogColor = mountain ? new Color(0.7f, 0.76f, 0.84f) : new Color(0.78f, 0.79f, 0.76f);
                    RenderSettings.fogStartDistance = mountain ? 90f : 60f; RenderSettings.fogEndDistance = mountain ? 1100f : 650f;
                    if (sky != null && sky.HasProperty("_AtmosphereThickness")) { sky.SetFloat("_AtmosphereThickness", 1.15f); sky.SetFloat("_Exposure", 1.25f); }
                    break;
            }
            if (tractor != null) tractor.SetHeadlights(tod == 2);
            DynamicGI.UpdateEnvironment();
        }
    }
}
