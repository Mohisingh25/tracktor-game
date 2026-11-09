// Pure C# data: no UnityEngine dependency so the drivetrain can be unit-simulated headless.
namespace TractorSim
{
    /// <summary>All the numbers that make one tractor model feel different from another.</summary>
    [System.Serializable]
    public class TractorSpec
    {
        public string id = "haldhar47";
        public string displayName = "Haldhar 47 DI";
        public string description = "47 HP 3-cylinder diesel, 8F + 2R sliding mesh, rear-wheel drive.";

        // --- Mass & geometry ---
        public float massKg = 1960f;
        public float wheelBase = 1.96f;
        public float trackFront = 1.38f;
        public float trackRear = 1.52f;
        public float rearWheelRadius = 0.68f;   // 14.9-28 tyre
        public float frontWheelRadius = 0.39f;  // 6.00-16 tyre
        public float rearWheelWidth = 0.38f;
        public float frontWheelWidth = 0.16f;
        public float centerOfMassHeight = 0.78f;
        public float centerOfMassZ = -0.25f;    // behind the mid-wheelbase, towards rear axle

        // --- Engine ---
        public int cylinders = 3;
        public float ratedPowerHp = 47f;
        public float idleRpm = 850f;
        public float ratedRpm = 2100f;
        public float highIdleRpm = 2260f;       // governor max with no load
        public float stallRpm = 380f;
        public float maxRpm = 2500f;
        public float engineInertia = 1.35f;     // kg m^2, heavy tractor flywheel
        public float governorDroopRpm = 110f;   // rpm below set point where rack is fully open
        // gross torque curve (Nm) – roughly 15% torque backup, peak around 1400 rpm
        public float[] torqueRpm = { 0f, 500f, 800f, 1000f, 1200f, 1400f, 1600f, 1800f, 2000f, 2100f, 2300f, 2500f };
        public float[] torqueNm  = { 0f, 120f, 158f, 176f, 188f, 194f, 190f, 181f, 168f, 160f, 120f, 0f };
        public float starterTorqueNm = 70f;

        // --- Fuel & thermal ---
        public float fuelTankL = 48f;
        public float bsfcKgPerKWh = 0.238f;
        public float idleFuelLph = 0.75f;

        // --- Transmission: speeds (km/h) at rated rpm, like a brochure ---
        public int gearsPerRange = 4;
        public float[] forwardSpeedsKmh = { 2.9f, 4.2f, 5.7f, 8.4f, 10.8f, 15.4f, 20.6f, 30.8f }; // L1..L4, H1..H4
        public float[] reverseSpeedsKmh = { 4.0f, 14.6f };                                         // RL, RH
        public float drivelineEfficiency = 0.86f;
        public float clutchMaxTorqueNm = 290f;

        // --- Brakes & steering ---
        public float brakeTorquePerRearWheel = 4600f;
        public float handbrakeTorque = 6000f;
        public float maxSteerDeg = 36f;
        public float steerRateDegPerSec = 75f;

        // --- Traction ---
        public float tractionFactor = 1.0f;

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

        public static TractorSpec Default() { return new TractorSpec(); }
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
