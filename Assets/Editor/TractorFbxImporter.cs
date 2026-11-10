#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace TractorSim.EditorTools
{
    /// <summary>
    /// Build-time preparation of the 3D tractor's materials. The model itself is NOT imported by Unity any more:
    /// it was converted offline from the FBX into Resources/Art/TractorModel.bytes (see Tools/fbx2trm.py) and is
    /// loaded at runtime by TractorModel. This class only bakes the five UDIM materials as assets so the
    /// Standard shader variants they need (normal map, metallic map, transparent glass) are kept in the build.
    /// </summary>
    public static class TractorFbxImporter
    {
        const string Dir = "Assets/Resources/Art";

        [MenuItem("Tractor Sim/Rebuild tractor materials")]
        public static void Process()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/Resources", "Art");
            int made = 0;
            for (int udim = 1001; udim <= 1005; udim++)
            {
                string path = Dir + "/TractorMat_" + udim + ".mat";
                AssetDatabase.DeleteAsset(path);
                var m = TractorSim.TractorModel.MakeMaterial(udim);
                AssetDatabase.CreateAsset(m, path);
                made++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[TractorSim] Tractor materials prepared: " + made);
        }
    }

    /// <summary>Import settings for the tractor textures: normal maps as normal maps, metallic/smoothness linear.</summary>
    public class TractorTexturePostprocessor : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.Contains("/TractorTex/")) return;
            var ti = (TextureImporter)assetImporter;
            ti.maxTextureSize = 2048;
            ti.mipmapEnabled = true;
            ti.anisoLevel = 4;
            if (assetPath.Contains("Normal_")) ti.textureType = TextureImporterType.NormalMap;
            else if (assetPath.Contains("MetalSmooth_")) { ti.textureType = TextureImporterType.Default; ti.sRGBTexture = false; }
            else { ti.textureType = TextureImporterType.Default; ti.sRGBTexture = true; ti.alphaIsTransparency = assetPath.EndsWith(".png"); }
        }
    }
}
#endif
