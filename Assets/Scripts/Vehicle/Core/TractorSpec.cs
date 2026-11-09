// Pure C# data: no UnityEngine dependency so the drivetrain can be unit-simulated headless.
namespace TractorSim
{
    public enum BodyStyle { Open, Canopy, Cabin }

    /// <summary>All the numbers that make one tractor model feel different from another.</summary>
    [System.Serializable]
    public class TractorSpec
    {
        public string id = "haldhar47";
        public string brand = "HALDHAR";
        public string model = "47 DI";
        public string tagline = "Reliable all-rounder";
        public string displayName { get { return brand + " " + model; } }
        public string description = "47 HP 3-cylinder diesel, 8F + 2R sliding mesh, rear-wheel drive.";
        public int stars = 3;                    // 1..5 overall rating shown in the showroom
        public bool flagship;                    // "BEST TRACTOR" badge

        // --- Look ---
        public float[] bodyRgb = { 0.66f, 0.07f, 0.05f };
        public float[] accentRgb = { 0.9f, 0.88f, 0.82f };
        public float[] rimRgb = { 0.72f, 0.7f, 0.66f };
        public BodyStyle style = BodyStyle.Open;
        public float bodyScale = 1f;             // overall size of the procedural body

        // --- Mass & geometry ---
        public float massKg = 1960f;
        public float wheelBase = 1.96f;
        public float trackFront = 1.38f;
        public float trackRear = 1.52f;
        public float rearWheelRadius = 0.68f;
        public float frontWheelRadius = 0.39f;
        public float rearWheelWidth = 0.38f;
        public float frontWheelWidth = 0.16f;
        public float centerOfMassHeight = 0.78f;
        public float centerOfMassZ = -0.25f;
        public float frontSpring = 80000f, frontDamper = 3900f, frontTravel = 0.14f;
        public float rearSpring = 180000f, rearDamper = 6200f, rearTravel = 0.08f;
        public float dragArea = 2.4f;            // Cd x frontal area, m^2
        public float rearWheelInertia = 32f;

        // --- Engine ---
        public int cylinders = 3;
        public float ratedPowerHp = 47f;
        public float idleRpm = 850f;
        public float ratedRpm = 2100f;
        public float highIdleRpm = 2260f;
        public float stallRpm = 380f;
        public float maxRpm = 2500f;
        public float engineInertia = 1.35f;
        public float governorDroopRpm = 110f;
        public float[] torqueRpm = { 0f, 500f, 800f, 1000f, 1200f, 1400f, 1600f, 1800f, 2000f, 2100f, 2300f, 2500f };
        public float[] torqueNm  = { 0f, 120f, 158f, 176f, 188f, 194f, 190f, 181f, 168f, 160f, 120f, 0f };
        public float starterTorqueNm = 70f;

        // --- Fuel & thermal ---
        public float fuelTankL = 48f;
        public float bsfcKgPerKWh = 0.238f;
        public float idleFuelLph = 0.75f;

        // --- Transmission: speeds (km/h) at rated rpm, grouped by range (gearsPerRange each) ---
        public int gearsPerRange = 4;
        public float[] forwardSpeedsKmh = { 2.9f, 4.2f, 5.7f, 8.4f, 10.8f, 15.4f, 20.6f, 30.8f };
        public float[] reverseSpeedsKmh = { 4.0f, 14.6f };
        public string[] rangeNames = { "L", "H" };
        public float drivelineEfficiency = 0.86f;
        public float clutchMaxTorqueNm = 290f;
        public bool rangeShiftOnTheMove;        // synchronised range (flagship) vs. stop-to-shift sliding mesh

        // --- Drive, brakes & steering ---
        public bool fourWheelDrive;
        public bool frontBrakes;
        public float brakeTorquePerRearWheel = 4600f;
        public float brakeTorquePerFrontWheel = 0f;
        public float handbrakeTorque = 6000f;
        public float maxSteerDeg = 36f;
        public float steerRateDegPerSec = 75f;
        public float tractionFactor = 1.0f;

        // --- Implements ---
        public float maxLiftKg = 1200f;          // 3-point hitch lift capacity
        public float maxTowKg = 8000f;           // recommended trolley gross weight

        public int RangeCount { get { return System.Math.Max(1, forwardSpeedsKmh.Length / gearsPerRange); } }
        public float TopSpeedKmh { get { return forwardSpeedsKmh[forwardSpeedsKmh.Length - 1]; } }
        public float PeakTorqueNm { get { float m = 0f; foreach (var t in torqueNm) if (t > m) m = t; return m; } }

        public float RatioForSpeed(float kmh)
        {
            float ratedOmega = ratedRpm * Num.RpmToRad;
            float v = kmh / 3.6f;
            return ratedOmega * rearWheelRadius / v;
        }

        public float GrossTorque(float rpm)
        {
            if (rpm <= torqueRpm[0]) return torqueNm[0];
            int n = torqueRpm.Length;
            if (rpm >= torqueRpm[n - 1]) return torqueNm[n - 1];
            for (int i = 1; i < n; i++)
            {
                if (rpm <= torqueRpm[i])
                {
                    float t = (rpm - torqueRpm[i - 1]) / (torqueRpm[i] - torqueRpm[i - 1]);
                    return torqueNm[i - 1] + (torqueNm[i] - torqueNm[i - 1]) * t;
                }
            }
            return 0f;
        }

        /// <summary>Builds a realistic diesel torque curve from peak torque and rated speed.</summary>
        public void SetTorqueCurve(float peakNm, float rated)
        {
            float[] f = { 0f, 0.24f, 0.38f, 0.48f, 0.57f, 0.67f, 0.76f, 0.86f, 0.95f, 1.0f, 1.1f, 1.19f };
            float[] s = { 0f, 0.62f, 0.81f, 0.9f, 0.97f, 1.0f, 0.98f, 0.93f, 0.86f, 0.82f, 0.62f, 0f };
            torqueRpm = new float[f.Length]; torqueNm = new float[f.Length];
            for (int i = 0; i < f.Length; i++) { torqueRpm[i] = f[i] * rated; torqueNm[i] = s[i] * peakNm; }
            ratedRpm = rated;
            highIdleRpm = rated * 1.075f;
            maxRpm = rated * 1.19f;
        }

        public static TractorSpec Default() { return TractorCatalog.Get("haldhar47"); }
    }

    /// <summary>The tractors you can choose in the showroom. All names are original game brands.</summary>
    public static class TractorCatalog
    {
        public static readonly string[] Ids = { "golu35", "anshu50", "haldhar47", "bharatveer60", "mohit150" };

        public static TractorSpec Get(string id)
        {
            switch (id)
            {
                case "golu35": return GoluChaudhary();
                case "anshu50": return AnshuSingh();
                case "bharatveer60": return BharatVeer();
                case "mohit150": return MohitSingh();
                default: return Haldhar();
            }
        }

        public static TractorSpec[] All()
        {
            var a = new TractorSpec[Ids.Length];
            for (int i = 0; i < Ids.Length; i++) a[i] = Get(Ids[i]);
            return a;
        }

        // Classic small Indian tractor: red bonnet, white canopy, light and nimble.
        static TractorSpec GoluChaudhary()
        {
            var s = new TractorSpec();
            s.id = "golu35"; s.brand = "GOLU CHAUDHARY"; s.model = "GC-35"; s.tagline = "Chhota packet, bada dhamaka";
            s.description = "35 HP 3-cylinder, 8F + 2R, 2WD. Light, nimble and thrifty on diesel.";
            s.stars = 2;
            s.bodyRgb = new[] { 0.78f, 0.08f, 0.06f }; s.accentRgb = new[] { 0.95f, 0.95f, 0.93f }; s.rimRgb = new[] { 0.85f, 0.83f, 0.78f };
            s.style = BodyStyle.Canopy; s.bodyScale = 0.94f;
            s.massKg = 1650f; s.wheelBase = 1.86f; s.trackRear = 1.42f; s.trackFront = 1.3f;
            s.rearWheelRadius = 0.63f; s.frontWheelRadius = 0.37f; s.rearWheelWidth = 0.32f; s.frontWheelWidth = 0.15f;
            s.ratedPowerHp = 35f; s.SetTorqueCurve(140f, 2100f); s.idleRpm = 820f; s.engineInertia = 1.1f;
            s.fuelTankL = 42f; s.clutchMaxTorqueNm = 210f;
            s.forwardSpeedsKmh = new[] { 2.6f, 3.8f, 5.2f, 7.6f, 9.8f, 14.0f, 19.0f, 28.0f };
            s.reverseSpeedsKmh = new[] { 3.6f, 13.2f };
            s.brakeTorquePerRearWheel = 3800f; s.handbrakeTorque = 5000f;
            s.maxLiftKg = 1000f; s.maxTowKg = 5000f;
            s.rearWheelInertia = 26f;
            return s;
        }

        // Mid-size workhorse: blue, canopy, 3 ranges.
        static TractorSpec AnshuSingh()
        {
            var s = new TractorSpec();
            s.id = "anshu50"; s.brand = "ANSHU SINGH"; s.model = "AS-50"; s.tagline = "Kheton ka raja";
            s.description = "50 HP 3-cylinder, 12F + 3R (3 ranges), 2WD, power steering. Pulls heavy trolleys all day.";
            s.stars = 3;
            s.bodyRgb = new[] { 0.08f, 0.25f, 0.62f }; s.accentRgb = new[] { 0.92f, 0.92f, 0.9f }; s.rimRgb = new[] { 0.85f, 0.75f, 0.2f };
            s.style = BodyStyle.Canopy; s.bodyScale = 1.03f;
            s.massKg = 2150f; s.wheelBase = 2.02f; s.trackRear = 1.56f; s.trackFront = 1.42f;
            s.rearWheelRadius = 0.7f; s.frontWheelRadius = 0.4f; s.rearWheelWidth = 0.4f;
            s.ratedPowerHp = 50f; s.SetTorqueCurve(210f, 2000f); s.engineInertia = 1.45f;
            s.fuelTankL = 60f; s.clutchMaxTorqueNm = 320f;
            s.gearsPerRange = 4; s.rangeNames = new[] { "L", "M", "H" };
            s.forwardSpeedsKmh = new[] { 2.3f, 3.1f, 4.2f, 5.6f, 6.8f, 8.9f, 11.6f, 15.0f, 17.5f, 22.0f, 27.5f, 34.0f };
            s.reverseSpeedsKmh = new[] { 3.4f, 9.0f, 16.0f };
            s.brakeTorquePerRearWheel = 5000f; s.steerRateDegPerSec = 95f;
            s.maxLiftKg = 1800f; s.maxTowKg = 10000f;
            s.rearWheelInertia = 34f;
            return s;
        }

        static TractorSpec Haldhar()
        {
            var s = new TractorSpec();   // the original Stage-1 tractor
            s.stars = 3; s.tagline = "Reliable all-rounder";
            return s;
        }

        // Heavy 4WD: green, 60 HP.
        static TractorSpec BharatVeer()
        {
            var s = new TractorSpec();
            s.id = "bharatveer60"; s.brand = "BHARAT VEER"; s.model = "60 4WD"; s.tagline = "Mitti ho ya pahaad";
            s.description = "60 HP 4-cylinder, 12F + 3R, four-wheel drive. Best grip in mud and on mountains.";
            s.stars = 4;
            s.bodyRgb = new[] { 0.12f, 0.42f, 0.18f }; s.accentRgb = new[] { 0.95f, 0.8f, 0.15f }; s.rimRgb = new[] { 0.95f, 0.8f, 0.15f };
            s.style = BodyStyle.Canopy; s.bodyScale = 1.08f;
            s.massKg = 2600f; s.wheelBase = 2.15f; s.trackRear = 1.62f; s.trackFront = 1.55f;
            s.rearWheelRadius = 0.74f; s.frontWheelRadius = 0.5f; s.rearWheelWidth = 0.42f; s.frontWheelWidth = 0.26f;
            s.cylinders = 4; s.ratedPowerHp = 60f; s.SetTorqueCurve(255f, 2100f); s.engineInertia = 1.6f;
            s.fuelTankL = 65f; s.clutchMaxTorqueNm = 390f;
            s.gearsPerRange = 4; s.rangeNames = new[] { "L", "M", "H" };
            s.forwardSpeedsKmh = new[] { 2.1f, 2.9f, 3.9f, 5.2f, 6.5f, 8.6f, 11.2f, 14.8f, 18.0f, 23.0f, 29.5f, 38.0f };
            s.reverseSpeedsKmh = new[] { 3.0f, 8.5f, 17.0f };
            s.fourWheelDrive = true; s.frontBrakes = false;
            s.brakeTorquePerRearWheel = 5800f; s.steerRateDegPerSec = 95f; s.maxSteerDeg = 34f;
            s.frontSpring = 110000f; s.frontDamper = 5200f;
            s.maxLiftKg = 2200f; s.maxTowKg = 14000f;
            s.rearWheelInertia = 38f;
            return s;
        }

        // The flagship: 150 HP, 4WD, cab, 16 gears, 120 km/h.
        static TractorSpec MohitSingh()
        {
            var s = new TractorSpec();
            s.id = "mohit150"; s.brand = "MOHIT SINGH"; s.model = "MS-150 TURBO"; s.tagline = "Sabse taqatwar. Sabse tez.";
            s.description = "150 HP 6-cylinder turbo diesel, 16F + 4R, 4WD, front suspension, 4-wheel brakes, air-con cab. Up to 120 km/h.";
            s.stars = 5; s.flagship = true;
            s.bodyRgb = new[] { 0.06f, 0.06f, 0.07f }; s.accentRgb = new[] { 0.95f, 0.72f, 0.18f }; s.rimRgb = new[] { 0.95f, 0.72f, 0.18f };
            s.style = BodyStyle.Cabin; s.bodyScale = 1.25f;
            s.massKg = 4800f; s.wheelBase = 2.75f; s.trackRear = 1.9f; s.trackFront = 1.85f;
            s.rearWheelRadius = 0.86f; s.frontWheelRadius = 0.64f; s.rearWheelWidth = 0.55f; s.frontWheelWidth = 0.42f;
            s.centerOfMassHeight = 0.95f; s.centerOfMassZ = -0.1f;
            s.cylinders = 6; s.ratedPowerHp = 150f; s.SetTorqueCurve(640f, 2200f); s.idleRpm = 800f; s.engineInertia = 2.6f;
            s.starterTorqueNm = 160f; s.stallRpm = 360f;
            s.fuelTankL = 260f; s.bsfcKgPerKWh = 0.215f; s.idleFuelLph = 1.4f;
            s.clutchMaxTorqueNm = 950f; s.drivelineEfficiency = 0.88f;
            s.gearsPerRange = 8; s.rangeNames = new[] { "L", "H" }; s.rangeShiftOnTheMove = true;
            s.forwardSpeedsKmh = new[] { 2.5f, 3.5f, 4.8f, 6.5f, 8.8f, 12f, 16f, 21f, 26f, 32f, 40f, 50f, 62f, 77f, 96f, 120f };
            s.reverseSpeedsKmh = new[] { 4.5f, 18f };
            s.fourWheelDrive = true; s.frontBrakes = true;
            s.brakeTorquePerRearWheel = 14000f; s.brakeTorquePerFrontWheel = 8000f; s.handbrakeTorque = 14000f;
            s.frontSpring = 190000f; s.frontDamper = 14000f; s.frontTravel = 0.16f;
            s.rearSpring = 330000f; s.rearDamper = 16000f; s.rearTravel = 0.07f;
            s.dragArea = 2.6f; s.maxSteerDeg = 38f; s.steerRateDegPerSec = 110f; s.tractionFactor = 1.08f;
            s.maxLiftKg = 6500f; s.maxTowKg = 24000f;
            s.rearWheelInertia = 70f;
            return s;
        }
    }

    /// <summary>Tiny math helper so the core compiles without UnityEngine.</summary>
    public static class Num
    {
        public const float RpmToRad = 0.10471976f;   // 2π/60
        public const float RadToRpm = 9.5492966f;
        public static float Clamp(float v, float a, float b) { return v < a ? a : (v > b ? b : v); }
        public static float Clamp01(float v) { return v < 0f ? 0f : (v > 1f ? 1f : v); }
        public static float Lerp(float a, float b, float t) { return a + (b - a) * Clamp01(t); }
        public static float Abs(float v) { return v < 0f ? -v : v; }
        public static float Sign(float v) { return v < 0f ? -1f : 1f; }
        public static float Max(float a, float b) { return a > b ? a : b; }
        public static float Min(float a, float b) { return a < b ? a : b; }
        public static float MoveTowards(float c, float t, float d)
        {
            if (Abs(t - c) <= d) return t;
            return c + Sign(t - c) * d;
        }
        public static float SmoothStep01(float x) { x = Clamp01(x); return x * x * (3f - 2f * x); }
    }
}
