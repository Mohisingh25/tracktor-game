using UnityEngine;

namespace TractorSim
{
    /// <summary>Animates everything you can see move: wheels, steering wheel, pedals, levers, lights, smoke, spray.</summary>
    public class TractorVisuals : MonoBehaviour
    {
        public Transform steeringWheel, clutchPedal, brakePedal, accelPedal, gearLever, rangeLever, handThrottleLever;
        public Transform[] wheelRoots, wheelSpin;
        public Renderer[] headLens, tailLens, indicatorLensL, indicatorLensR;
        public Light[] headlights;
        public ParticleSystem exhaust;
        public ParticleSystem[] rearDust;
        public Transform cockpitEye, hitchCam, frontCam;

        TractorController ctrl;
        VehiclePhysics phys;
        Transform visualRoot;
        Quaternion steerBase;
        float smokeBurst;
        bool lastHead; int lastInd = -99; bool lastBrake; bool lastBlink;
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        public void Init(TractorController c, VehiclePhysics p)
        {
            ctrl = c; phys = p;
            visualRoot = transform.Find("Visual");
            if (steeringWheel != null) steerBase = steeringWheel.localRotation;
            c.OnStarterBegin += () => smokeBurst = 1.2f;
            c.OnGrind += () => { };
            UpdateLights(true);
        }

        void LateUpdate()
        {
            if (ctrl == null) return;
            var inp = MobileInputManager.I;
            var dt = Time.deltaTime;
            var eng = ctrl.Engine;

            // ---- wheels ----
            for (int i = 0; i < 4; i++)
            {
                var w = phys.Wheels[i];
                Vector3 c = transform.TransformPoint(w.centerLocal) - transform.up * w.sinkVisual;
                wheelRoots[i].position = c;
                wheelRoots[i].rotation = transform.rotation * Quaternion.Euler(0f, w.steerDeg, 0f);
                wheelSpin[i].localRotation = Quaternion.Euler(w.spinDeg, 0f, 0f);
            }

            // ---- controls in the cab ----
            if (steeringWheel != null) steeringWheel.localRotation = steerBase * Quaternion.Euler(0f, -ctrl.Steering.WheelRotationDeg, 0f);
            float clutch = ctrl.Drivetrain.Mode == TransmissionMode.Manual ? ctrl.Drivetrain.ClutchPedal : 0f;
            if (clutchPedal != null) clutchPedal.localRotation = Quaternion.Euler(clutch * 25f, 0f, 0f);
            if (brakePedal != null) brakePedal.localRotation = Quaternion.Euler(ctrl.Brakes.Pedal * 22f, 0f, 0f);
            if (accelPedal != null) accelPedal.localRotation = Quaternion.Euler((inp != null ? inp.Throttle : 0f) * 18f, 0f, 0f);
            if (gearLever != null)
            {
                int g = ctrl.Gearbox.Gear;
                // H-pattern: 1/2 left plane, 3/4 right plane, R far left
                float sideAng = g == 0 ? 0f : (g < 0 ? -22f : (g <= 2 ? -10f : 10f));
                float fwdAng = g == 0 ? 0f : (g < 0 ? 15f : ((g % 2 == 1) ? 15f : -15f));
                gearLever.localRotation = Quaternion.Slerp(gearLever.localRotation, Quaternion.Euler(fwdAng, 0f, sideAng), dt * 14f);
            }
            if (rangeLever != null) rangeLever.localRotation = Quaternion.Slerp(rangeLever.localRotation, Quaternion.Euler(ctrl.Gearbox.Range == 0 ? 15f : -15f, 0f, 0f), dt * 10f);
            if (handThrottleLever != null && inp != null) handThrottleLever.localRotation = Quaternion.Euler(0f, 0f, -70f + inp.HandThrottle * 60f);

            // ---- diesel shake ----
            if (visualRoot != null)
            {
                float amp = eng.Running ? Mathf.Lerp(0.004f, 0.0015f, eng.Rpm / ctrl.Spec.ratedRpm) * (0.6f + eng.Rack) : (eng.Cranking ? 0.006f : 0f);
                float f = eng.Rpm / 60f * 1.5f;
                visualRoot.localPosition = new Vector3(Mathf.Sin(Time.time * f * 6.283f) * amp * 0.5f, Mathf.Sin(Time.time * f * 6.283f * 1.3f) * amp, 0f);
            }

            UpdateLights(false);
            UpdateSmoke(dt);
            UpdateSpray();
        }

        void SetEmission(Renderer[] rs, Color c)
        {
            if (rs == null) return;
            foreach (var r in rs) if (r != null) r.material.SetColor(EmissionId, c);
        }

        void UpdateLights(bool force)
        {
            bool head = ctrl.Headlights;
            bool brake = ctrl.Brakes.Pedal > 0.05f;
            bool blink = ctrl.BlinkOn;
            int ind = ctrl.Indicator;
            if (force || head != lastHead)
            {
                SetEmission(headLens, head ? new Color(3f, 2.8f, 2.4f) : Color.black);
                foreach (var l in headlights) if (l != null) l.enabled = head;
            }
            if (force || head != lastHead || brake != lastBrake)
            {
                Color tail = brake ? new Color(3f, 0.1f, 0.05f) : (head ? new Color(0.9f, 0.05f, 0.02f) : Color.black);
                SetEmission(tailLens, tail);
            }
            if (force || blink != lastBlink || ind != lastInd)
            {
                Color on = new Color(3f, 1.4f, 0f);
                bool l = blink && (ind == -1 || ind == 2);
                bool r = blink && (ind == 1 || ind == 2);
                SetEmission(indicatorLensL, l ? on : Color.black);
                SetEmission(indicatorLensR, r ? on : Color.black);
            }
            lastHead = head; lastBrake = brake; lastBlink = blink; lastInd = ind;
        }

        void UpdateSmoke(float dt)
        {
            if (exhaust == null) return;
            var eng = ctrl.Engine;
            var em = exhaust.emission;
            var main = exhaust.main;
            smokeBurst = Mathf.Max(0f, smokeBurst - dt);
            if (!eng.Running && !eng.Cranking && smokeBurst <= 0f) { em.rateOverTime = 0f; return; }
            float rpmF = eng.Rpm / ctrl.Spec.ratedRpm;
            // diesel puffs black when the governor opens the rack suddenly
            float load = eng.Rack;
            float rate = 6f + rpmF * 18f + load * 25f + smokeBurst * 60f;
            em.rateOverTime = rate;
            float dark = Mathf.Clamp01(load * 0.8f + smokeBurst * 0.6f + (eng.Cranking ? 0.5f : 0f));
            float grey = Mathf.Lerp(0.45f, 0.08f, dark);
            main.startColor = new Color(grey, grey, grey, Mathf.Lerp(0.18f, 0.6f, dark));
            main.startSpeed = new ParticleSystem.MinMaxCurve(1f + rpmF * 2f, 1.8f + rpmF * 3f);
        }

        void UpdateSpray()
        {
            if (rearDust == null) return;
            for (int k = 0; k < 2; k++)
            {
                var w = phys.Wheels[2 + k];
                var ps = rearDust[k];
                if (ps == null || !w.grounded) continue;
                var pr = w.props;
                float slip = Mathf.Abs(w.slipSpeed);
                float speed = Mathf.Abs(w.vLong);
                float intensity = 0f;
                if (pr.muddy) intensity = Mathf.Clamp01(slip * 0.35f + speed * 0.04f);
                else if (pr.dusty) intensity = Mathf.Clamp01(slip * 0.25f + Mathf.Max(0f, speed - 2f) * 0.06f);
                if (intensity < 0.05f || pr.dustColor.a <= 0.01f) continue;
                int n = Random.value < intensity * 0.9f ? 1 + (int)(intensity * 2f) : 0;
                for (int i = 0; i < n; i++)
                {
                    var ep = new ParticleSystem.EmitParams();
                    Vector3 back = -transform.forward * Mathf.Sign(w.omega + 0.0001f);
                    ep.position = w.contactPoint + transform.up * 0.1f + back * (w.radius * 0.6f) + transform.right * Random.Range(-w.width, w.width) * 0.5f;
                    ep.velocity = back * (pr.muddy ? 1.5f + slip * 0.8f : 0.5f + slip * 0.3f) + Vector3.up * (pr.muddy ? 1.5f + slip * 0.6f : 0.4f) + Random.insideUnitSphere * 0.6f;
                    var c = pr.dustColor;
                    c.a *= Mathf.Lerp(0.5f, 1f, intensity);
                    ep.startColor = c;
                    ep.startSize = pr.muddy ? Random.Range(0.12f, 0.28f) : Random.Range(0.45f, 0.9f);
                    ep.startLifetime = pr.muddy ? 0.9f : 1.8f;
                    ps.Emit(ep, 1);
                }
            }
        }
    }
}
