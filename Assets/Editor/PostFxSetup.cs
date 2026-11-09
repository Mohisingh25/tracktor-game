#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace TractorSim.EditorTools
{
    /// <summary>
    /// Creates Resources/CameraRig.prefab: the main camera with Post-Processing Stack v2
    /// (ACES film tone mapping, colour grading, bloom, vignette, FXAA). The game instantiates it at runtime
    /// and switches it off on the Low graphics setting.
    /// </summary>
    public static class PostFxSetup
    {
        const string ProfilePath = "Assets/Resources/PostFX_Profile.asset";
        const string RigPath = "Assets/Resources/CameraRig.prefab";
        const string ResourcesPath = "Packages/com.unity.postprocessing/PostProcessing/PostProcessResources.asset";

        public static void CreateCameraRig()
        {
            var profile = AssetDatabase.LoadAssetAtPath<PostProcessProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<PostProcessProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);

                var cg = profile.AddSettings<ColorGrading>();
                cg.gradingMode.Override(GradingMode.HighDefinitionRange);
                cg.tonemapper.Override(Tonemapper.ACES);
                cg.postExposure.Override(0.45f);
                cg.temperature.Override(5f);
                cg.saturation.Override(6f);
                cg.contrast.Override(10f);
                AssetDatabase.AddObjectToAsset(cg, profile);

                var bloom = profile.AddSettings<Bloom>();
                bloom.intensity.Override(0.9f);
                bloom.threshold.Override(1.1f);
                bloom.softKnee.Override(0.6f);
                bloom.fastMode.Override(true);
                AssetDatabase.AddObjectToAsset(bloom, profile);

                var vig = profile.AddSettings<Vignette>();
                vig.intensity.Override(0.22f);
                vig.smoothness.Override(0.4f);
                AssetDatabase.AddObjectToAsset(vig, profile);

                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
            }

            var go = new GameObject("CameraRig");
            var cam = go.AddComponent<Camera>();
            cam.allowHDR = true;
            cam.farClipPlane = 1400f;
            var layer = go.AddComponent<PostProcessLayer>();
            var res = AssetDatabase.LoadAssetAtPath<PostProcessResources>(ResourcesPath);
            if (res != null) layer.Init(res);
            layer.volumeTrigger = go.transform;
            layer.volumeLayer = ~0;
            layer.antialiasingMode = PostProcessLayer.Antialiasing.FastApproximateAntialiasing;
            layer.fastApproximateAntialiasing.fastMode = true;
            var vol = go.AddComponent<PostProcessVolume>();
            vol.isGlobal = true;
            vol.priority = 1f;
            vol.sharedProfile = profile;
            PrefabUtility.SaveAsPrefabAsset(go, RigPath);
            Object.DestroyImmediate(go);
        }
    }
}
#endif
