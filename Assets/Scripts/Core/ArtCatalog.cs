using UnityEngine;

namespace TractorSim
{
    /// <summary>
    /// Slots for real, artist-made 3D assets. Fill this in the Unity Editor (Assets/Resources/ArtCatalog.asset)
    /// by dragging in models and textures from Asset Store / Sketchfab packs. Anything left empty falls back
    /// to the built-in procedural art, so the game always runs.
    /// </summary>
    [CreateAssetMenu(fileName = "ArtCatalog", menuName = "Tractor Sim/Art Catalog")]
    public class ArtCatalog : ScriptableObject
    {
        [System.Serializable]
        public class TractorArt
        {
            [Tooltip("golu35, anshu50, haldhar47, bharatveer60 or mohit150")]
            public string tractorId;
            public GameObject model;
            [Tooltip("Position of the model relative to the tractor origin (ground level, midway between axles, +Z forward)")]
            public Vector3 offset;
            public Vector3 euler;
            public float scale = 1f;
            [Tooltip("Names of the wheel objects inside the model (leave empty to keep the built-in wheels)")]
            public string wheelFL, wheelFR, wheelRL, wheelRR;
            [Tooltip("Hide the built-in brand stripes if your model has its own decals")]
            public bool hideBrandDecals;
        }

        [System.Serializable]
        public class ImplementArt
        {
            [Tooltip("trolley_single, trolley_tandem, rotavator, cultivator, plough, harrow, seeddrill, sprayer, leveller")]
            public string implementId;
            public GameObject model;
            public Vector3 offset;
            public Vector3 euler;
            public float scale = 1f;
        }

        [System.Serializable]
        public class GroundTexture
        {
            public Texture2D albedo;
            public Texture2D normal;
            [Tooltip("Metres covered by one repeat of the texture")]
            public float tileSize = 4f;
        }

        [Header("Vehicles")]
        public TractorArt[] tractors = new TractorArt[0];
        public ImplementArt[] implements = new ImplementArt[0];

        [Header("Ground (used by the mountain terrain and the farm)")]
        public GroundTexture grass = new GroundTexture();
        public GroundTexture dirt = new GroundTexture();
        public GroundTexture rock = new GroundTexture();
        public GroundTexture mud = new GroundTexture();
        public GroundTexture gravelRoad = new GroundTexture();
        public GroundTexture field = new GroundTexture();

        [Header("Vegetation & props")]
        [Tooltip("Grass blade texture with transparency (terrain detail grass)")]
        public Texture2D grassBlades;
        public GameObject[] trees = new GameObject[0];
        public GameObject[] rocks = new GameObject[0];
        public GameObject[] bushes = new GameObject[0];

        [Header("Sky")]
        public Material skybox;

        static ArtCatalog cached; static bool loaded;
        public static ArtCatalog Load()
        {
            if (!loaded) { cached = Resources.Load<ArtCatalog>("ArtCatalog"); loaded = true; }
            return cached;
        }

        public TractorArt FindTractor(string id)
        {
            if (tractors == null) return null;
            foreach (var t in tractors) if (t != null && t.tractorId == id) return t;
            return null;
        }

        public ImplementArt FindImplement(string id)
        {
            if (implements == null) return null;
            foreach (var t in implements) if (t != null && t.implementId == id) return t;
            return null;
        }

        public static bool Has(GroundTexture g) { return g != null && g.albedo != null; }
    }
}
