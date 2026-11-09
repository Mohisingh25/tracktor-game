using System.Collections.Generic;
using UnityEngine;

namespace TractorSim
{
    public enum SurfaceKind { Asphalt, Dirt, Grass, WetGrass, Mud, Sand, Rock, Water, Field }

    /// <summary>Physical properties of a ground surface.</summary>
    public struct SurfaceProps
    {
        public string name;
        public float grip;            // friction coefficient for a lugged tractor tyre
        public float rolling;         // rolling resistance coefficient
        public float sink;            // visual/physical sinkage depth (m)
        public float bumpiness;       // small random suspension disturbance
        public Color dustColor;
        public bool muddy;            // throws mud instead of dust
        public bool dusty;

        public static SurfaceProps Get(SurfaceKind k)
        {
            switch (k)
            {
                case SurfaceKind.Asphalt:  return new SurfaceProps { name = "Asphalt",   grip = 1.00f, rolling = 0.016f, sink = 0f,    bumpiness = 0f,    dustColor = new Color(0.6f, 0.6f, 0.6f, 0.0f) };
                case SurfaceKind.Dirt:     return new SurfaceProps { name = "Dirt road", grip = 0.80f, rolling = 0.040f, sink = 0.01f, bumpiness = 0.004f, dustColor = new Color(0.62f, 0.52f, 0.38f, 0.55f), dusty = true };
                case SurfaceKind.Grass:    return new SurfaceProps { name = "Grass",     grip = 0.72f, rolling = 0.055f, sink = 0.02f, bumpiness = 0.003f, dustColor = new Color(0.4f, 0.45f, 0.3f, 0.25f) };
                case SurfaceKind.WetGrass: return new SurfaceProps { name = "Wet grass", grip = 0.48f, rolling = 0.065f, sink = 0.03f, bumpiness = 0.003f, dustColor = new Color(0.3f, 0.32f, 0.2f, 0.45f), muddy = true };
                case SurfaceKind.Mud:      return new SurfaceProps { name = "Deep mud",  grip = 0.36f, rolling = 0.160f, sink = 0.12f, bumpiness = 0.004f, dustColor = new Color(0.25f, 0.18f, 0.1f, 0.9f), muddy = true };
                case SurfaceKind.Sand:     return new SurfaceProps { name = "Sand",      grip = 0.50f, rolling = 0.130f, sink = 0.07f, bumpiness = 0.002f, dustColor = new Color(0.86f, 0.76f, 0.55f, 0.6f), dusty = true };
                case SurfaceKind.Rock:     return new SurfaceProps { name = "Rocky",     grip = 0.85f, rolling = 0.035f, sink = 0f,    bumpiness = 0.012f, dustColor = new Color(0.55f, 0.52f, 0.48f, 0.35f), dusty = true };
                case SurfaceKind.Water:    return new SurfaceProps { name = "Water",     grip = 0.55f, rolling = 0.110f, sink = 0.05f, bumpiness = 0.002f, dustColor = new Color(0.7f, 0.8f, 0.85f, 0.6f), muddy = true };
                case SurfaceKind.Field:    return new SurfaceProps { name = "Ploughed field", grip = 0.62f, rolling = 0.100f, sink = 0.06f, bumpiness = 0.006f, dustColor = new Color(0.45f, 0.33f, 0.22f, 0.6f), dusty = true };
            }
            return Get(SurfaceKind.Dirt);
        }
    }

    /// <summary>Tag a collider with its ground surface.</summary>
    public class SurfaceZone : MonoBehaviour
    {
        public SurfaceKind kind = SurfaceKind.Dirt;

        static readonly Dictionary<Collider, SurfaceKind> lookup = new Dictionary<Collider, SurfaceKind>();

        public static SurfaceKind Of(Collider c)
        {
            if (c == null) return SurfaceKind.Dirt;
            SurfaceKind k;
            if (lookup.TryGetValue(c, out k)) return k;
            var z = c.GetComponent<SurfaceZone>();
            k = z != null ? z.kind : SurfaceKind.Dirt;
            lookup[c] = k;
            return k;
        }

        /// <summary>Surface at a contact point: tagged colliders, or the dominant painted layer of a Unity Terrain.</summary>
        public static SurfaceKind Of(Collider c, Vector3 point)
        {
            var tc = c as TerrainCollider;
            if (tc != null)
            {
                var ts = tc.GetComponent<TerrainSurface>();
                if (ts != null) return ts.KindAt(point);
            }
            return Of(c);
        }

        public static void ClearCache() { lookup.Clear(); }
    }
}

namespace TractorSim
{
    /// <summary>Maps the terrain's splat (texture) layers to physical surfaces.</summary>
    public class TerrainSurface : MonoBehaviour
    {
        public SurfaceKind[] layerKinds;
        Terrain terrain;
        float[,,] alpha; int aw, ah;

        public void Init(Terrain t, SurfaceKind[] kinds)
        {
            terrain = t; layerKinds = kinds;
            var td = t.terrainData;
            aw = td.alphamapWidth; ah = td.alphamapHeight;
            alpha = td.GetAlphamaps(0, 0, aw, ah);
        }

        public SurfaceKind KindAt(Vector3 world)
        {
            if (terrain == null || alpha == null) return SurfaceKind.Dirt;
            var td = terrain.terrainData;
            Vector3 local = world - terrain.transform.position;
            int x = Mathf.Clamp((int)(local.x / td.size.x * (aw - 1)), 0, aw - 1);
            int z = Mathf.Clamp((int)(local.z / td.size.z * (ah - 1)), 0, ah - 1);
            int best = 0; float bv = -1f;
            int layers = alpha.GetLength(2);
            for (int l = 0; l < layers; l++) { float v = alpha[z, x, l]; if (v > bv) { bv = v; best = l; } }
            return best < layerKinds.Length ? layerKinds[best] : SurfaceKind.Dirt;
        }
    }
}
