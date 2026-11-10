using UnityEngine;

namespace TractorSim
{
    /// <summary>
    /// Measurements taken from the imported FBX tractor (Assets/Art/Tractor/tractor.fbx) by the editor
    /// importer, so physics (wheel sizes, wheelbase, track, hitch, trolley axle) matches the model exactly.
    /// All values in metres, tractor origin = ground level, midway between the axles, +Z forward.
    /// </summary>
    public class TractorRigInfo : ScriptableObject
    {
        public bool textured;
        public float rearRadius = 0.67f, frontRadius = 0.44f;
        public float rearTrack = 1.86f, frontTrack = 1.4f;
        public float rearWidth = 0.42f, frontWidth = 0.2f;
        public float wheelBase = 2.04f;
        public float length = 3.2f, width = 2.26f, height = 2.38f;
        public Vector3 exhaustTop = new Vector3(-0.33f, 2.02f, 0.72f);
        public Vector3 hoodSide = new Vector3(0.28f, 1.1f, 0.6f);
        public float hoodLength = 1.0f;
        public Vector3 seat = new Vector3(0f, 1.24f, -0.75f);
        public float rearZ = -1.6f;
        public float hitchZ = -1.86f;         // drawbar hitch eye (where the model's trolley drawbar ends)           // rearmost point of the tractor body (hitch goes just behind)

        // trolley (origin at the hitch eye)
        public bool hasTrolley;
        public float trolleyAxleZ = -2.48f, trolleyWheelRadius = 0.44f, trolleyTrack = 1.45f;
        public float trolleyBedFrontZ = -0.43f, trolleyBedLength = 2.97f, trolleyBedWidth = 1.8f;
        public float trolleyFloorY = 0.88f, trolleySideHeight = 0.65f;

        static TractorRigInfo cached; static bool loaded;
        public static TractorRigInfo Load()
        {
            if (!loaded) { cached = Resources.Load<TractorRigInfo>("Art/TractorRigInfo"); loaded = true; }
            return cached;
        }

        public static GameObject TractorPrefab() { return Resources.Load<GameObject>("Art/TractorRig"); }
        public static GameObject TrolleyPrefab() { return Resources.Load<GameObject>("Art/TrolleyRig"); }

        /// <summary>Make a brand's physics match the model (scaled to the brand's size).</summary>
        public void ApplyTo(TractorSpec s)
        {
            float k = s.bodyScale;
            s.rearWheelRadius = rearRadius * k;
            s.frontWheelRadius = frontRadius * k;
            s.wheelBase = wheelBase * k;
            s.trackRear = rearTrack * k;
            s.trackFront = frontTrack * k;
            s.rearWheelWidth = rearWidth * k;
            s.frontWheelWidth = frontWidth * k;
        }
    }
}
