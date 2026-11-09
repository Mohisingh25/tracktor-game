using System.Collections.Generic;
using UnityEngine;

namespace TractorSim
{
    /// <summary>
    /// Runtime material factory. In builds, template materials are saved into Resources by the
    /// build script so the needed shader variants are included; in the editor we fall back to Shader.Find.
    /// </summary>
    public static class MatLib
    {
        public const string ResPath = "TractorSimMaterials/";
        static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();
        static Material opaqueT, emissiveT, transparentT, particleT, skyT;

        static Material Template(ref Material slot, string resName, string shaderName)
        {
            if (slot != null) return slot;
            slot = Resources.Load<Material>(ResPath + resName);
            if (slot == null)
            {
                var sh = Shader.Find(shaderName);
                if (sh == null) sh = Shader.Find("Standard");
                slot = new Material(sh);
            }
            return slot;
        }

        public static Material Opaque(Color c, float smooth = 0.15f, float metal = 0f, Texture tex = null, Vector2? tiling = null)
        {
            string key = "o" + c + smooth + metal + (tex != null ? tex.name + tex.GetHashCode() : "") + (tiling.HasValue ? tiling.Value.ToString() : "");
            Material m;
            if (cache.TryGetValue(key, out m)) return m;
            m = new Material(Template(ref opaqueT, "Opaque", "Standard"));
            m.color = c;
            m.SetFloat("_Glossiness", smooth);
            m.SetFloat("_Metallic", metal);
            if (tex != null)
            {
                m.mainTexture = tex;
                if (tiling.HasValue) m.mainTextureScale = tiling.Value;
            }
            m.enableInstancing = true;
            cache[key] = m;
            return m;
        }

        /// <summary>Unique (not cached) emissive material so lights can be switched individually.</summary>
        public static Material Emissive(Color baseColor, Color emission)
        {
            var m = new Material(Template(ref emissiveT, "Emissive", "Standard"));
            m.color = baseColor;
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission);
            m.SetFloat("_Glossiness", 0.8f);
            return m;
        }

        public static Material Transparent(Color c, float smooth = 0.9f)
        {
            string key = "t" + c + smooth;
            Material m;
            if (cache.TryGetValue(key, out m)) return m;
            m = new Material(Template(ref transparentT, "Transparent", "Standard"));
            ConfigureFade(m);
            m.color = c;
            m.SetFloat("_Glossiness", smooth);
            cache[key] = m;
            return m;
        }

        public static void ConfigureFade(Material m)
        {
            m.SetFloat("_Mode", 2f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.DisableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_ALPHABLEND_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = 3000;
        }

        public static Material Particle(Texture tex)
        {
            var m = new Material(Template(ref particleT, "Particle", "Legacy Shaders/Particles/Alpha Blended"));
            m.mainTexture = tex;
            return m;
        }

        public static Material Sky()
        {
            return Template(ref skyT, "Sky", "Skybox/Procedural");
        }
    }
}
