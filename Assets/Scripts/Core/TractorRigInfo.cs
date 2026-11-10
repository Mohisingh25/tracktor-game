using UnityEngine;

namespace TractorSim
{
    /// <summary>
    /// Measurements of the 3D tractor model (from Resources/Art/TractorModel.bytes), so physics – wheel sizes,
    /// wheelbase, track, hitch, trolley axle – matches the model exactly.
    /// Metres; tractor origin = ground level midway between the axles, +Z forward. Trolley values are
    /// relative to the trolley's hitch eye.
    /// </summary>
    public class TractorRigInfo
    {
        public bool textured = true;
        public float rearRadius = 0.67f, frontRadius = 0.44f;
        public float rearTrack = 1.84f, frontTrack = 1.46f;
        public float rearWidth = 0.42f, frontWidth = 0.2f;
        public float wheelBase = 2.04f;
        public float length = 3.3f, width = 2.0f, height = 2.39f;
        public Vector3 exhaustTop = new Vector3(0.33f, 2.05f, 0.72f);
        public Vector3 hoodSide = new Vector3(0.27f, 1.16f, 0.26f);
        public float hoodLength = 1.0f;
        public Vector3 seat = new Vector3(0f, 1.49f, -0.52f);
        public float rearZ = -1.97f;
        public float hitchZ = -1.86f;

        public bool hasTrolley = true;
        public float trolleyAxleZ = -2.52f, trolleyWheelRadius = 0.44f, trolleyTrack = 1.46f;
        public float trolleyBedFrontZ = -0.28f, trolleyBedLength = 3.16f, trolleyBedWidth = 1.8f;
        public float trolleyFloorY = 0.9f, trolleySideHeight = 0.66f;

        /// <summary>The model's measurements, or null when the model is not available (procedural tractors are used).</summary>
        public static TractorRigInfo Load() { return TractorModel.Info; }

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
