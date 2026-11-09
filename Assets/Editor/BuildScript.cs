#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace TractorSim.EditorTools
{
    /// <summary>
    /// One-click project preparation and Android build. Used by GitHub Actions (game-ci) via
    /// buildMethod: TractorSim.EditorTools.BuildScript.BuildAndroid, and from the "Tractor Sim" menu.
    /// Creates the bootstrap scene, shader-variant template materials and player settings,
    /// so the repository needs no hand-made scene or asset setup.
    /// </summary>
    public static class BuildScript
    {
        const string ScenePath = "Assets/Scenes/Main.unity";
        const string MatDir = "Assets/Resources/TractorSimMaterials";

        [MenuItem("Tractor Sim/Prepare Project (scene, materials, settings)")]
        public static void Prepare()
        {
            ConfigurePlayer();
            CreateMaterials();
            CreateScene();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[TractorSim] Project prepared.");
        }

        [MenuItem("Tractor Sim/Build Android APK")]
        public static void BuildAndroid()
        {
            Prepare();
            string path = GetArg("-customBuildPath");
            if (string.IsNullOrEmpty(path)) path = "build/Android/IndianTractorSim.apk";
            if (!path.EndsWith(".apk")) path = Path.Combine(path, "IndianTractorSim.apk");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            EditorUserBuildSettings.buildAppBundle = false;

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = path,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            Debug.Log("[TractorSim] Build result: " + report.summary.result + "  size: " + report.summary.totalSize + " bytes  -> " + path);
            if (Application.isBatchMode)
                EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        static string GetArg(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }

        static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "DesiSim";
            PlayerSettings.productName = "Indian Tractor Simulator";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.Android.bundleVersionCode = 1;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.desisim.indiantractorsim");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.Android.startInFullscreen = true;
            PlayerSettings.runInBackground = false;

            // Our touch controls and keyboard use the classic Input Manager (works on every device).
            // 0 = Input Manager (old), 1 = Input System (new), 2 = Both.
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (assets != null && assets.Length > 0)
            {
                var so = new SerializedObject(assets[0]);
                var prop = so.FindProperty("activeInputHandler");
                if (prop != null && prop.intValue == 1)
                {
                    prop.intValue = 2;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    Debug.Log("[TractorSim] Active input handling set to Both.");
                }
            }
        }

        static void CreateMaterials()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder("Assets/Resources", "TractorSimMaterials");

            var std = Shader.Find("Standard");
            var opaque = new Material(std) { name = "Opaque", enableInstancing = true };
            Save(opaque, "Opaque");

            var emissive = new Material(std) { name = "Emissive" };
            emissive.EnableKeyword("_EMISSION");
            emissive.SetColor("_EmissionColor", Color.white);
            emissive.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            Save(emissive, "Emissive");

            var trans = new Material(std) { name = "Transparent" };
            TractorSim.MatLib.ConfigureFade(trans);
            trans.color = new Color(1, 1, 1, 0.5f);
            Save(trans, "Transparent");

            var psh = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
            if (psh == null) psh = Shader.Find("Particles/Standard Unlit");
            if (psh != null) Save(new Material(psh) { name = "Particle" }, "Particle");

            var sky = Shader.Find("Skybox/Procedural");
            if (sky != null)
            {
                var m = new Material(sky) { name = "Sky" };
                m.SetFloat("_SunSize", 0.035f);
                m.SetFloat("_AtmosphereThickness", 1.15f);
                m.SetColor("_SkyTint", new Color(0.55f, 0.6f, 0.68f));
                m.SetColor("_GroundColor", new Color(0.42f, 0.4f, 0.36f));
                m.SetFloat("_Exposure", 1.25f);
                Save(m, "Sky");
            }
        }

        static void Save(Material m, string name)
        {
            string p = MatDir + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (existing != null) { EditorUtility.CopySerialized(m, existing); EditorUtility.SetDirty(existing); }
            else AssetDatabase.CreateAsset(m, p);
        }

        static void CreateScene()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // Fog & skybox set in the scene so the build keeps the fog shader variants (fog stripping = automatic).
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 60f;
            RenderSettings.fogEndDistance = 650f;
            var sky = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/Sky.mat");
            if (sky != null) RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            // The game builds itself at runtime (GameBootstrap); a marker object documents that.
            var marker = new GameObject("README – world is generated at runtime by TractorSim.GameBootstrap");
            marker.hideFlags = HideFlags.None;
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }
    }
}
#endif
