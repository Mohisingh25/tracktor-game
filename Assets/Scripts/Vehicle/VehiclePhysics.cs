using UnityEngine;

namespace TractorSim
{
    /// <summary>Runtime state of one wheel.</summary>
    public class WheelState
    {
        public string name;
        public bool front, left, driven;
        public Vector3 anchorLocal;     // wheel centre at full bump (max compression), body space
        public float radius, width, travel, spring, damper;

        public bool grounded;
        public float compression, prevCompression;
        public Vector3 contactPoint, contactNormal = Vector3.up, centerWorld, centerLocal;
        public Collider hitCollider;
        public SurfaceKind surface = SurfaceKind.Dirt;
        public SurfaceProps props = SurfaceProps.Get(SurfaceKind.Dirt);
        public float Fz, Fx, Fy, vLong, vLat;
        public float omega, spinDeg, steerDeg;
        public float slipSpeed;          // |ω r − v| for effects
        public float sinkVisual;
        public float bumpPhase;
    }

    /// <summary>
    /// Ray/sphere-cast wheel physics: spring-damper suspension, tanh tyre model with friction circle,
    /// surface-dependent grip, rolling resistance and sinkage. Rear wheel spin comes from the
    /// Drivetrain axle solver so engine inertia, clutch slip and differential are all coupled.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class VehiclePhysics : MonoBehaviour
    {
        public WheelState[] Wheels;     // 0 FL, 1 FR, 2 RL, 3 RR
        public Rigidbody Body { get; private set; }
        public float SpeedKmh { get; private set; }       // signed, + forward
        public float SpeedMs { get; private set; }
        public float Odometer { get; private set; }
        public int GroundedCount { get; private set; }
        public float SlopeDeg { get; private set; }
        public float ExtraRollingForce;                    // external drag (future implements/trolleys)

        Drivetrain drivetrain;
        TractorSpec spec;
        const float CastLift = 0.35f;
        int layerMask;

        public void Init(TractorSpec spec, Drivetrain dt, WheelState[] wheels)
        {
            this.spec = spec;
            drivetrain = dt;
            Wheels = wheels;
            Body = GetComponent<Rigidbody>();
            layerMask = Physics.DefaultRaycastLayers & ~(1 << 2);
        }

        public void ResetWheels()
        {
            foreach (var w in Wheels) { w.omega = 0f; w.compression = w.prevCompression = 0f; w.Fx = w.Fy = 0f; }
            drivetrain.OmegaLeft = drivetrain.OmegaRight = 0f;
        }

        public void Simulate(float dt, SteeringSystem steering, BrakeSystem brakes)
        {
            var t = transform;
            Vector3 up = t.up;
            SpeedMs = Vector3.Dot(Body.linearVelocity, t.forward);
            SpeedKmh = SpeedMs * 3.6f;
            Odometer += Mathf.Abs(SpeedMs) * dt;
            SlopeDeg = Vector3.Angle(t.forward, Vector3.ProjectOnPlane(t.forward, Vector3.up)) * Mathf.Sign(t.forward.y);

            Wheels[0].steerDeg = steering.LeftAngle;
            Wheels[1].steerDeg = steering.RightAngle;

            // ---------- 1. suspension & contact ----------
            GroundedCount = 0;
            for (int i = 0; i < Wheels.Length; i++)
            {
                var w = Wheels[i];
                Vector3 anchor = t.TransformPoint(w.anchorLocal);
                RaycastHit hit;
                float castR = w.radius * 0.94f;
                float offset = w.radius - castR;   // so the sphere's bottom matches the tyre's bottom
                bool got = Physics.SphereCast(anchor + up * CastLift, castR, -up, out hit, w.travel + CastLift + offset + 0.05f, layerMask, QueryTriggerInteraction.Ignore);
                w.prevCompression = w.compression;
                if (got)
                {
                    float ext = hit.distance - CastLift - offset;
                    w.grounded = true;
                    w.hitCollider = hit.collider;
                    var kind = SurfaceZone.Of(hit.collider);
                    if (kind != w.surface) { w.surface = kind; w.props = SurfaceProps.Get(kind); }
                    w.contactNormal = hit.normal;
                    // contact point under the wheel along -up (sphere hit point can be off to the side on edges)
                    w.centerWorld = anchor - up * Mathf.Clamp(ext, -0.2f, w.travel);
                    w.contactPoint = w.centerWorld - hit.normal * w.radius;
                    // surface bumps (rocky ground, ruts) as a moving disturbance
                    w.bumpPhase += Mathf.Abs(w.vLong) * dt * 3.1f;
                    float bump = w.props.bumpiness * (Mathf.PerlinNoise(w.bumpPhase, i * 7.3f) - 0.5f) * 2f;
                    float sink = w.props.sink * Mathf.Clamp01(0.4f + Mathf.Abs(w.slipSpeed) * 0.3f);
                    w.sinkVisual = Mathf.Lerp(w.sinkVisual, sink, dt * 2f);
                    w.compression = w.travel - ext + bump - w.sinkVisual;
                    GroundedCount++;
                }
                else
                {
                    w.grounded = false;
                    w.hitCollider = null;
                    w.compression = 0f;
                    w.centerWorld = anchor - up * w.travel;
                    w.contactPoint = w.centerWorld - up * w.radius;
                    w.contactNormal = up;
                }

                w.centerLocal = t.InverseTransformPoint(w.centerWorld);
                float comp = Mathf.Max(0f, w.compression);
                float force = 0f;
                if (w.grounded && comp > 0f)
                {
                    float vel = (w.compression - w.prevCompression) / dt;
                    force = w.spring * comp + w.damper * vel;
                    if (comp > w.travel) force += w.spring * 8f * (comp - w.travel); // bump stop
                    force = Mathf.Max(0f, force);
                    Body.AddForceAtPosition(up * force, w.centerWorld, ForceMode.Force);
                }
                w.Fz = force;
            }

            // ---------- 2. tyre kinematics & lateral forces ----------
            for (int i = 0; i < Wheels.Length; i++)
            {
                var w = Wheels[i];
                if (!w.grounded || w.Fz <= 0f) { w.vLong = w.vLat = 0f; w.Fy = 0f; continue; }
                Vector3 n = w.contactNormal;
                Vector3 heading = Quaternion.AngleAxis(w.steerDeg, up) * t.forward;
                Vector3 fwd = Vector3.ProjectOnPlane(heading, n).normalized;
                Vector3 side = Vector3.Cross(n, fwd);
                Vector3 v = Body.GetPointVelocity(w.contactPoint);
                w.vLong = Vector3.Dot(v, fwd);
                w.vLat = Vector3.Dot(v, side);
                float mu = w.props.grip * spec.tractionFactor * (w.front ? 0.95f : 1f);
                w.Fy = TireModel.LatForce(w.vLat, w.vLong, w.Fz, mu);
                // low-speed stiction so a parked tractor doesn't creep sideways down a slope
                if (Mathf.Abs(w.vLat) < 0.25f && Mathf.Abs(w.vLong) < 1f)
                {
                    float massShare = Body.mass * 0.25f;
                    float extra = -w.vLat * massShare / dt * 0.25f;
                    w.Fy = Mathf.Clamp(w.Fy + extra, -mu * w.Fz, mu * w.Fz);
                }
            }

            // ---------- 3. drivetrain & rear axle ----------
            var rl = Wheels[2]; var rr = Wheels[3];
            var cl = Contact(rl, brakes.LeftTorque);
            var cr = Contact(rr, brakes.RightTorque);
            drivetrain.Step(dt, cl, cr, SpeedKmh);
            rl.omega = drivetrain.OmegaLeft;
            rr.omega = drivetrain.OmegaRight;
            rl.Fx = rl.grounded ? drivetrain.AxleForce[0] : 0f;
            rr.Fx = rr.grounded ? drivetrain.AxleForce[1] : 0f;

            // ---------- 4. apply tyre forces ----------
            for (int i = 0; i < Wheels.Length; i++)
            {
                var w = Wheels[i];
                if (w.front)
                {
                    // free-rolling front wheels
                    w.omega = w.grounded ? w.vLong / w.radius : w.omega * (1f - dt * 0.5f);
                    w.Fx = 0f;
                }
                w.spinDeg += w.omega * Mathf.Rad2Deg * dt;
                if (w.spinDeg > 360f || w.spinDeg < -360f) w.spinDeg %= 360f;
                w.slipSpeed = w.omega * w.radius - w.vLong;
                if (!w.grounded || w.Fz <= 0f) continue;

                Vector3 n = w.contactNormal;
                Vector3 heading = Quaternion.AngleAxis(w.steerDeg, up) * t.forward;
                Vector3 fwd = Vector3.ProjectOnPlane(heading, n).normalized;
                Vector3 side = Vector3.Cross(n, fwd);

                float rolling = -w.props.rolling * w.Fz * Mathf.Clamp(w.vLong / 0.4f, -1f, 1f);
                float fx = w.Fx + rolling;
                float fy = w.Fy;
                // friction circle: longitudinal demand reduces available side grip
                float mu = w.props.grip * spec.tractionFactor;
                float max = mu * w.Fz * 1.05f;
                float mag = Mathf.Sqrt(fx * fx + fy * fy);
                if (mag > max && mag > 0f) { float s = max / mag; fy *= s; if (!w.driven) fx *= s; }
                w.Fy = fy;

                Vector3 at = Vector3.Lerp(w.contactPoint, w.centerWorld, 0.45f);
                Body.AddForceAtPosition(fwd * fx + side * fy, at, ForceMode.Force);
            }

            if (ExtraRollingForce > 0f && Mathf.Abs(SpeedMs) > 0.05f)
                Body.AddForce(-t.forward * Mathf.Sign(SpeedMs) * ExtraRollingForce, ForceMode.Force);

            // parking stiction: hold the tractor completely still with brakes on
            if (brakes.Active && GroundedCount >= 3 && Body.linearVelocity.sqrMagnitude < 0.04f && drivetrain.ThrottleInput < 0.1f)
            {
                Vector3 hv = Vector3.ProjectOnPlane(Body.linearVelocity, up);
                Body.AddForce(-hv * Body.mass / dt * 0.5f, ForceMode.Force);
            }
        }

        WheelContact Contact(WheelState w, float brakeTorque)
        {
            var c = new WheelContact();
            c.grounded = w.grounded && w.Fz > 0f;
            c.Fz = w.Fz;
            c.vLong = w.vLong;
            c.mu = w.props.grip * spec.tractionFactor;
            float maxF = c.mu * Mathf.Max(1f, w.Fz);
            float latUse = Mathf.Clamp01(Mathf.Abs(w.Fy) / maxF);
            c.longCapacity = Mathf.Max(0.25f, Mathf.Sqrt(1f - latUse * latUse));
            c.brakeTorque = brakeTorque;
            return c;
        }
    }
}
