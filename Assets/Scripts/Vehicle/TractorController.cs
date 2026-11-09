using UnityEngine;

namespace TractorSim
{
    /// <summary>
    /// Glue between driver input and the tractor's systems: engine, transmission, steering,
    /// brakes, physics, lights, damage. Exposes everything the HUD/dashboard needs.
    /// </summary>
    public class TractorController : MonoBehaviour
    {
        public TractorSpec Spec { get; private set; }
        public Drivetrain Drivetrain { get; private set; }
        public EngineSystem Engine { get { return Drivetrain.Engine; } }
        public TransmissionSystem Gearbox { get { return Drivetrain.Gearbox; } }
        public SteeringSystem Steering { get; private set; }
        public BrakeSystem Brakes { get; private set; }
        public VehiclePhysics Physics { get; private set; }

        public bool InputEnabled = true;
        public bool Headlights { get; private set; }
        public int Indicator { get; private set; }      // -1 left, 0 off, 1 right, 2 hazard
        public bool BlinkOn { get { return Indicator != 0 && Mathf.Repeat(Time.time, 0.8f) < 0.4f; } }
        public bool Horn { get; private set; }
        public float Damage { get; private set; }       // 0..100 %
        public bool Overturned { get; private set; }
        public bool Stuck { get; private set; }

        /// <summary>Raised for HUD toasts: (message, isWarning).</summary>
        public System.Action<string, bool> OnMessage;
        public System.Action OnGrind, OnShift, OnStarterBegin, OnStall, OnIndicatorTick, OnImpact;

        Vector3 spawnPos; Quaternion spawnRot;
        float overturnTimer, stuckTimer;
        bool lastBlink;

        public void Init(TractorSpec spec, VehiclePhysics physics)
        {
            Spec = spec;
            Drivetrain = new Drivetrain(spec);
            Steering = new SteeringSystem(spec);
            Brakes = new BrakeSystem(spec);
            Physics = physics;
            spawnPos = transform.position; spawnRot = transform.rotation;
            Brakes.Handbrake = true;   // parked when you climb on
        }

        public void ApplySettings(GameSettings s)
        {
            Drivetrain.SetMode(s.automatic ? TransmissionMode.Automatic : TransmissionMode.Manual);
            Steering.Sensitivity = s.steerSensitivity;
        }

        public void RestoreProgress(SaveData d)
        {
            if (d.fuelL >= 0f) Engine.FuelL = Mathf.Min(d.fuelL, Spec.fuelTankL);
            Damage = d.damage;
        }

        public void SetSpawn(Vector3 pos, Quaternion rot) { spawnPos = pos; spawnRot = rot; }

        /// <summary>Put the tractor back on its spawn point, engine off, parked.</summary>
        public void ResetTractor(bool keepEngine = false)
        {
            var rb = Physics.Body;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.position = spawnPos; rb.rotation = spawnRot;
            transform.SetPositionAndRotation(spawnPos, spawnRot);
            Physics.ResetWheels();
            Steering.Reset();
            if (!keepEngine) Engine.Stop();
            Drivetrain.Gearbox.ForceNeutral();
            Drivetrain.Auto.Reset();
            Brakes.Handbrake = true;
            Overturned = false; Stuck = false; overturnTimer = stuckTimer = 0f;
        }

        /// <summary>Recovery: lift the tractor back onto its wheels where it is.</summary>
        public void Recover()
        {
            var rb = Physics.Body;
            Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            Vector3 pos = transform.position + Vector3.up * 1.5f;
            RaycastHit hit;
            if (UnityEngine.Physics.Raycast(transform.position + Vector3.up * 20f, Vector3.down, out hit, 60f, UnityEngine.Physics.DefaultRaycastLayers & ~(1 << 2), QueryTriggerInteraction.Ignore))
                pos = hit.point + Vector3.up * 0.6f;
            rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
            var rot = Quaternion.LookRotation(fwd.normalized, Vector3.up);
            rb.position = pos; rb.rotation = rot;
            transform.SetPositionAndRotation(pos, rot);
            Physics.ResetWheels();
            Overturned = false; Stuck = false; overturnTimer = stuckTimer = 0f;
        }

        public void SetHeadlights(bool on) { Headlights = on; }

        public void Refuel() { Engine.FuelL = Spec.fuelTankL; }
        public void Repair() { Damage = 0f; Engine.DamageDerate = 1f; }

        void Msg(string m, bool warn) { if (OnMessage != null) OnMessage(m, warn); }

        void Update()
        {
            var inp = MobileInputManager.I;
            if (inp == null || !InputEnabled) return;

            if (inp.Ignition())
            {
                if (Engine.Running || Engine.Cranking) { Engine.Stop(); Msg("Engine off", false); }
                else
                {
                    Drivetrain.ClutchPedal = inp.Clutch;
                    if (Drivetrain.TryStartEngine()) { if (OnStarterBegin != null) OnStarterBegin(); }
                }
            }

            bool manual = Drivetrain.Mode == TransmissionMode.Manual;
            if (manual)
            {
                Drivetrain.ClutchPedal = inp.Clutch;
                if (inp.GearUp()) Shift(+1);
                if (inp.GearDown()) Shift(-1);
                if (inp.RangeToggle())
                {
                    int target = (Gearbox.Range + 1) % Gearbox.RangeCount;
                    if (Gearbox.TrySetRange(target, Drivetrain.ClutchFree, Physics.SpeedKmh)) { Msg("Range " + Gearbox.RangeName(target), false); if (OnShift != null) OnShift(); }
                    else Grind();
                }
                inp.DirectionToggle();
            }
            else
            {
                inp.GearUp(); inp.GearDown(); inp.RangeToggle();
                if (inp.DirectionToggle())
                {
                    Drivetrain.RequestedDirection = Drivetrain.RequestedDirection > 0 ? -1 : 1;
                    Msg(Drivetrain.RequestedDirection > 0 ? "Forward selected" : "Reverse selected", false);
                }
            }

            if (inp.LightsToggle()) Headlights = !Headlights;
            if (inp.IndicatorLeft()) Indicator = Indicator == -1 ? 0 : -1;
            if (inp.IndicatorRight()) Indicator = Indicator == 1 ? 0 : 1;
            if (inp.Hazard()) Indicator = Indicator == 2 ? 0 : 2;
            if (inp.HandbrakeToggle()) { Brakes.Handbrake = !Brakes.Handbrake; Msg(Brakes.Handbrake ? "Parking brake ON" : "Parking brake released", false); }
            if (inp.DiffToggle()) { Drivetrain.DiffLock = !Drivetrain.DiffLock; Msg(Drivetrain.DiffLock ? "Diff lock ENGAGED" : "Diff lock released", false); }
            if (inp.ResetPressed()) Recover();
            Horn = inp.horn;

            bool blink = BlinkOn;
            if (blink != lastBlink) { lastBlink = blink; if (OnIndicatorTick != null) OnIndicatorTick(); }

            // relay system events
            if (!string.IsNullOrEmpty(Engine.Event)) { bool warn = Engine.Event.Contains("stall") || Engine.Event.Contains("fuel") || Engine.Event.Contains("won't") || Engine.Event.Contains("Press"); Msg(Engine.Event, warn); Engine.Event = null; }
            if (!string.IsNullOrEmpty(Gearbox.Event)) { Msg(Gearbox.Event, true); Gearbox.Event = null; }
        }

        void Shift(int dir)
        {
            int target = Gearbox.Gear + dir;
            if (target < -1 || target > Spec.gearsPerRange) return;
            if (Gearbox.TrySetGear(target, Drivetrain.ClutchFree)) { if (OnShift != null) OnShift(); }
            else Grind();
        }

        void Grind() { if (OnGrind != null) OnGrind(); Damage = Mathf.Min(100f, Damage + 0.15f); }

        void FixedUpdate()
        {
            if (Physics == null) return;
            float dt = Time.fixedDeltaTime;
            var inp = MobileInputManager.I;
            float steer = 0f, throttle = 0f, brake = 0f;
            if (inp != null && InputEnabled)
            {
                steer = inp.Steer; throttle = inp.Throttle; brake = inp.Brake;
                Drivetrain.HandThrottle = inp.HandThrottle;
                if (Drivetrain.Mode == TransmissionMode.Manual) Drivetrain.ClutchPedal = inp.Clutch;
            }
            else { Drivetrain.HandThrottle = 0f; }

            // pressing the accelerator in automatic releases the parking brake like a hill-hold
            if (Drivetrain.Mode == TransmissionMode.Automatic && Brakes.Handbrake && throttle > 0.3f && Engine.Running)
            {
                Brakes.Handbrake = false; Msg("Parking brake released", false);
            }

            Drivetrain.ThrottleInput = throttle;
            Drivetrain.BrakeInput = brake;
            Steering.Update(steer, dt, Physics.SpeedKmh);
            Brakes.Update(brake);
            Engine.DamageDerate = Damage > 70f ? Mathf.Lerp(1f, 0.55f, (Damage - 70f) / 30f) : 1f;

            bool wasRunning = Engine.Running;
            Physics.Simulate(dt, Steering, Brakes);
            if (wasRunning && Engine.JustStalled && OnStall != null) OnStall();

            // auto-shift sound hook
            if (Drivetrain.Mode == TransmissionMode.Automatic && Drivetrain.Auto.Shifting && !autoShiftPlayed) { autoShiftPlayed = true; if (OnShift != null) OnShift(); }
            if (!Drivetrain.Auto.Shifting) autoShiftPlayed = false;

            // overturn detection
            if (Vector3.Dot(transform.up, Vector3.up) < 0.35f) overturnTimer += dt; else overturnTimer = 0f;
            if (!Overturned && overturnTimer > 1.5f) { Overturned = true; Msg("Tractor overturned! Use Recover", true); }

            // stuck detection: throttle on, wheels turning, going nowhere
            bool trying = (throttle > 0.5f || Drivetrain.HandThrottle > 0.5f) && Engine.Running && !Gearbox.InNeutral;
            float wheelSpeed = Mathf.Abs(Drivetrain.RearOmegaAvg) * Spec.rearWheelRadius;
            if (trying && Mathf.Abs(Physics.SpeedMs) < 0.25f && (wheelSpeed > 1.0f || Engine.Rack > 0.97f)) stuckTimer += dt; else stuckTimer = Mathf.Max(0f, stuckTimer - dt * 2f);
            bool nowStuck = stuckTimer > 6f;
            if (nowStuck && !Stuck) Msg("Stuck! Try low range, diff lock, rocking back and forth – or Recover", true);
            Stuck = nowStuck;
        }
        bool autoShiftPlayed;

        void OnCollisionEnter(Collision c)
        {
            float impulse = c.impulse.magnitude;
            float dv = impulse / Physics.Body.mass;
            if (dv > 2.5f)
            {
                Damage = Mathf.Min(100f, Damage + (dv - 2.5f) * 4f);
                if (OnImpact != null) OnImpact();
                if (dv > 5f) Msg("Heavy impact! Damage " + Mathf.RoundToInt(Damage) + "%", true);
            }
        }
    }
}
