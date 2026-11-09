namespace TractorSim
{
    /// <summary>Input the axle solver needs about one driven wheel's contact patch.</summary>
    public struct WheelContact
    {
        public bool grounded;
        public float Fz;          // normal load, N
        public float vLong;       // contact patch speed along wheel heading, m/s
        public float mu;          // surface friction (already multiplied by tyre/traction factor)
        public float longCapacity;// 0..1 share of friction left for longitudinal after lateral demand
        public float brakeTorque; // Nm available at this wheel
    }

    /// <summary>Simple, stable tyre model (tanh slip curve with low-speed reference velocity).</summary>
    public static class TireModel
    {
        public const float LongStiffness = 7f;
        public const float LatStiffness = 9f;
        public const float LowSpeedRef = 2.0f; // m/s – avoids singular slip at standstill

        public static float LongForce(float omega, float radius, WheelContact c)
        {
            if (!c.grounded || c.Fz <= 0f) return 0f;
            float sv = omega * radius - c.vLong;
            float slip = sv / Num.Max(Num.Abs(c.vLong), LowSpeedRef);
            float maxF = c.mu * c.Fz * c.longCapacity;
            return maxF * Tanh(slip * LongStiffness);
        }

        public static float LatForce(float vLat, float vLong, float Fz, float mu)
        {
            if (Fz <= 0f) return 0f;
            float slip = vLat / Num.Max(Num.Abs(vLong), LowSpeedRef);
            return -mu * Fz * Tanh(slip * LatStiffness);
        }

        public static float Tanh(float x)
        {
            if (x > 8f) return 1f;
            if (x < -8f) return -1f;
            float e = (float)System.Math.Exp(2.0 * x);
            return (e - 1f) / (e + 1f);
        }
    }
}
