using UnityEngine;

namespace TractorSim
{
    /// <summary>Front-wheel steering with rate limit, speed sensitivity and Ackermann geometry.</summary>
    public class SteeringSystem
    {
        readonly TractorSpec spec;
        public float Angle { get; private set; }        // centre steer angle, deg (+ = right)
        public float LeftAngle { get; private set; }
        public float RightAngle { get; private set; }
        public float WheelRotationDeg { get { return -Angle / spec.maxSteerDeg * 540f; } } // steering wheel turns ±1.5 rev
        public float Sensitivity = 1f;

        public SteeringSystem(TractorSpec spec) { this.spec = spec; }

        public void Update(float input, float dt, float speedKmh)
        {
            input = Mathf.Clamp(input, -1f, 1f);
            float speedLimit = Mathf.Lerp(1f, 0.45f, Mathf.Abs(speedKmh) / 32f);
            float target = input * spec.maxSteerDeg * speedLimit;
            float rate = spec.steerRateDegPerSec * Mathf.Lerp(0.6f, 1.6f, Sensitivity * 0.5f);
            Angle = Mathf.MoveTowards(Angle, target, rate * dt);

            // Ackermann: inner wheel turns more than outer
            float a = Mathf.Abs(Angle) * Mathf.Deg2Rad;
            if (a < 0.001f) { LeftAngle = RightAngle = Angle; return; }
            float R = spec.wheelBase / Mathf.Tan(a);
            float inner = Mathf.Atan(spec.wheelBase / (R - spec.trackFront * 0.5f)) * Mathf.Rad2Deg;
            float outer = Mathf.Atan(spec.wheelBase / (R + spec.trackFront * 0.5f)) * Mathf.Rad2Deg;
            if (Angle > 0f) { RightAngle = inner; LeftAngle = outer; }
            else { LeftAngle = -inner; RightAngle = -outer; }
        }

        public void Reset() { Angle = LeftAngle = RightAngle = 0f; }
    }

    /// <summary>Rear service brakes (tractors brake the rear wheels only) and the parking brake.</summary>
    public class BrakeSystem
    {
        readonly TractorSpec spec;
        public bool Handbrake;
        public float Pedal { get; private set; }
        public float LeftTorque { get; private set; }
        public float RightTorque { get; private set; }
        public bool Active { get { return Pedal > 0.05f || Handbrake; } }

        public BrakeSystem(TractorSpec spec) { this.spec = spec; }

        public void Update(float pedal)
        {
            // hydraulic lag: brakes build up over ~0.15 s
            Pedal = Mathf.MoveTowards(Pedal, Mathf.Clamp01(pedal), Time.fixedDeltaTime * 7f);
            float t = Pedal * Pedal * spec.brakeTorquePerRearWheel;   // progressive pedal feel
            if (Handbrake) t = Mathf.Max(t, spec.handbrakeTorque);
            LeftTorque = t;
            RightTorque = t;
        }
    }
}
