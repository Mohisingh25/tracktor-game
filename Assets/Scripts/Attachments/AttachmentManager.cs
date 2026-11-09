using UnityEngine;

namespace TractorSim
{
    /// <summary>
    /// The tractor's rear hitch. Reverse up to a trolley's eye or an implement's linkage pins,
    /// press ATTACH, and it couples for real: trolleys through an articulated joint on the drawbar,
    /// 3-point implements onto the hydraulic linkage (raise / lower, PTO, soil work, draft load).
    /// </summary>
    public class AttachmentManager : MonoBehaviour
    {
        public Implement Current { get; private set; }
        public Trolley CurrentTrolley { get { return Current as Trolley; } }
        public Implement Candidate { get; private set; }
        public string CandidateWhy { get; private set; }      // why it can't attach yet ("closer", "align")
        public float ChaseExtraDistance { get; private set; }
        public float DraftForce { get; private set; }
        public System.Action<string, bool> OnMessage;
        public System.Action OnCouple;

        TractorController tractor;
        Transform drawbar, threePoint;
        ConfigurableJoint joint;
        float baseMass; Vector3 baseCom;
        float scanTimer;
        Vector3 mountLocal; Quaternion mountRot;

        public void Init(TractorController t, Transform drawbarPoint, Transform threePointPoint)
        {
            tractor = t; drawbar = drawbarPoint; threePoint = threePointPoint;
            var rb = t.Physics.Body;
            baseMass = rb.mass; baseCom = rb.centerOfMass;
        }

        void Msg(string m, bool warn) { if (OnMessage != null) OnMessage(m, warn); }

        // ---------------------------------------------------------------- input
        void Update()
        {
            var inp = MobileInputManager.I;
            if (inp == null || tractor == null || !tractor.InputEnabled) return;
            if (inp.AttachPressed()) { if (Current != null) Detach(); else TryAttach(); }
            if (inp.LiftPressed()) ToggleLift();
            if (inp.PtoPressed()) TogglePto();
            if (inp.TipPressed()) ToggleTip();

            scanTimer -= Time.deltaTime;
            if (scanTimer <= 0f) { scanTimer = 0.2f; Scan(); }
        }

        /// <summary>Look for the nearest unhitched implement whose hitch is close to ours and roughly lined up.</summary>
        void Scan()
        {
            Candidate = null; CandidateWhy = null;
            if (Current != null) return;
            float best = 4f;
            foreach (var imp in Implement.All)
            {
                if (imp == null || imp.Attached || imp.hitchPoint == null) continue;
                Transform myHitch = imp.hitchType == HitchType.Drawbar ? drawbar : threePoint;
                Vector3 d = imp.hitchPoint.position - myHitch.position;
                float flat = new Vector2(d.x, d.z).magnitude;
                if (flat > best) continue;
                best = flat;
                Candidate = imp;
                float yaw = Vector3.Angle(Vector3.ProjectOnPlane(imp.transform.forward, Vector3.up), Vector3.ProjectOnPlane(transform.forward, Vector3.up));
                float tol = imp.hitchType == HitchType.Drawbar ? 0.75f : 0.6f;
                if (flat > tol) CandidateWhy = "Reverse closer (" + flat.ToString("0.0") + " m)";
                else if (yaw > (imp.hitchType == HitchType.Drawbar ? 60f : 25f)) CandidateWhy = "Line up straight";
                else if (Mathf.Abs(tractor.Physics.SpeedKmh) > 2f) CandidateWhy = "Stop to hitch";
            }
        }

        public bool CanAttach { get { return Current == null && Candidate != null && CandidateWhy == null; } }

        public void TryAttach()
        {
            Scan();
            if (Candidate == null) { Msg("Nothing to hitch here – reverse up to a trolley or implement", true); return; }
            if (CandidateWhy != null) { Msg(Candidate.displayName + ": " + CandidateWhy, true); return; }
            var imp = Candidate;
            if (imp.hitchType == HitchType.Drawbar) AttachTrolley((Trolley)imp);
            else AttachMounted(imp);
            if (OnCouple != null) OnCouple();
        }

        void AttachTrolley(Trolley t)
        {
            var rb = tractor.Physics.Body;
            // snap the hitch eye onto the pin, then lock it with an articulated joint
            Vector3 delta = drawbar.position - t.hitchPoint.position;
            t.Body.position += delta; t.transform.position += delta;
            joint = t.gameObject.AddComponent<ConfigurableJoint>();
            joint.connectedBody = rb;
            joint.autoConfigureConnectedAnchor = false;
            joint.anchor = t.hitchPoint.localPosition;
            joint.connectedAnchor = rb.transform.InverseTransformPoint(drawbar.position);
            joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Locked;
            joint.angularXMotion = ConfigurableJointMotion.Limited;   // pitch over crests
            joint.angularYMotion = ConfigurableJointMotion.Limited;   // steering articulation
            joint.angularZMotion = ConfigurableJointMotion.Limited;   // roll on uneven ground
            var lo = new SoftJointLimit(); lo.limit = -35f; joint.lowAngularXLimit = lo;
            var hi = new SoftJointLimit(); hi.limit = 35f; joint.highAngularXLimit = hi;
            var y = new SoftJointLimit(); y.limit = 80f; joint.angularYLimit = y;
            var z = new SoftJointLimit(); z.limit = 25f; joint.angularZLimit = z;
            joint.enablePreprocessing = false;
            joint.breakForce = Mathf.Infinity;
            rb.solverIterations = 14;
            t.Attached = true; t.ParkBrake = false;
            Current = t;
            ChaseExtraDistance = t.capacityKg > 6000f ? 5f : 3.5f;
            Msg("Trolley hitched – " + t.displayName, false);
            if (t.TotalMass > tractor.Spec.maxTowKg) Msg("Warning: " + Mathf.RoundToInt(t.TotalMass / 1000f) + " t is more than this tractor should pull", true);
        }

        void AttachMounted(Implement imp)
        {
            var rb = tractor.Physics.Body;
            if (tractor.Spec.ratedPowerHp < imp.minHp)
                Msg(imp.displayName + " needs " + imp.minHp + " HP – this tractor will struggle", true);
            // the linkage carries it: becomes part of the tractor (mass, centre of mass)
            imp.Body.isKinematic = true;
            imp.Body.detectCollisions = false;
            imp.Body.interpolation = RigidbodyInterpolation.None;   // follows the tractor's interpolated transform
            imp.transform.SetParent(threePoint, true);
            mountLocal = -imp.hitchPoint.localPosition;
            mountRot = Quaternion.identity;
            imp.transform.localRotation = mountRot;
            imp.transform.localPosition = new Vector3(mountLocal.x, -threePoint.localPosition.y + 0.44f, mountLocal.z);
            imp.Attached = true; imp.Lowered = false; imp.LiftAmount = 1f;
            Current = imp;
            float m = imp.TotalMass;
            Vector3 impCom = rb.transform.InverseTransformPoint(imp.transform.TransformPoint(new Vector3(0, 0.4f, -imp.workPoint.localPosition.magnitude * 0.5f)));
            rb.mass = baseMass + m;
            rb.centerOfMass = (baseCom * baseMass + impCom * m) / (baseMass + m);
            ChaseExtraDistance = imp.workWidth > 4f ? 2.5f : 1.2f;
            Msg(imp.displayName + " mounted. LIFT to lower it, PTO to drive it", false);
            if (m > tractor.Spec.maxLiftKg) Msg("Too heavy for this tractor's hydraulics – it won't lift fully", true);
        }

        public void Detach()
        {
            if (Current == null) return;
            var rb = tractor.Physics.Body;
            if (Mathf.Abs(tractor.Physics.SpeedKmh) > 2f) { Msg("Stop before unhitching", true); return; }
            if (joint != null) { Destroy(joint); joint = null; }
            var imp = Current;
            if (imp.hitchType == HitchType.ThreePoint)
            {
                imp.transform.SetParent(null, true);
                imp.Body.isKinematic = false;
                imp.Body.detectCollisions = true;
                imp.Body.interpolation = RigidbodyInterpolation.Interpolate;
                imp.Body.linearVelocity = rb.linearVelocity;
                imp.PtoOn = false;
                rb.mass = baseMass; rb.centerOfMass = baseCom;
            }
            else
            {
                var t = (Trolley)imp;
                t.ParkBrake = true; t.BrakeInput = 0f; t.Tipping = false;
            }
            imp.Attached = false; imp.Lowered = false;
            Current = null;
            ChaseExtraDistance = 0f;
            DraftForce = 0f;
            tractor.Engine.PtoLoadNm = 0f;
            rb.solverIterations = 8;
            Msg(imp.displayName + " unhitched", false);
            if (OnCouple != null) OnCouple();
        }

        public void ToggleLift()
        {
            if (Current == null || Current.hitchType != HitchType.ThreePoint) { Msg("Lift works with 3-point implements", true); return; }
            Current.Lowered = !Current.Lowered;
            Msg(Current.Lowered ? "Implement lowered – drive slowly to work the soil" : "Implement raised", false);
        }

        public void TogglePto()
        {
            if (Current == null || Current.hitchType != HitchType.ThreePoint) { Msg("Nothing on the PTO", true); return; }
            if (Current.ptoPowerKw <= 0f) { Msg(Current.displayName + " doesn't use the PTO", true); return; }
            if (!tractor.Engine.Running) { Msg("Start the engine first", true); return; }
            Current.PtoOn = !Current.PtoOn;
            Msg(Current.PtoOn ? "PTO engaged" : "PTO off", false);
        }

        public void ToggleTip()
        {
            var t = CurrentTrolley;
            if (t == null) { Msg("Hitch a tipping trolley first", true); return; }
            if (!tractor.Engine.Running) { Msg("Hydraulics need the engine running", true); return; }
            t.Tipping = !t.Tipping;
            Msg(t.Tipping ? "Tipping trolley…" : "Lowering trolley bed", false);
        }

        /// <summary>Destroys whatever is hitched (used when the tractor is rebuilt).</summary>
        public void ReleaseForRebuild()
        {
            if (Current == null) return;
            if (joint != null) Destroy(joint);
            if (Current.hitchType == HitchType.ThreePoint) { Current.transform.SetParent(null, true); Current.Body.isKinematic = false; Current.Body.detectCollisions = true; }
            Current.Attached = false;
            Current = null;
        }

        // ---------------------------------------------------------------- physics
        void FixedUpdate()
        {
            if (Current == null || tractor == null) return;
            float dt = Time.fixedDeltaTime;
            var rb = tractor.Physics.Body;
            float speed = tractor.Physics.SpeedMs;

            var t = CurrentTrolley;
            if (t != null)
            {
                // trailer brakes follow the tractor's pedal and parking brake
                t.BrakeInput = Mathf.Max(tractor.Brakes.Pedal, tractor.Brakes.Handbrake ? 1f : 0f);
                tractor.Engine.PtoLoadNm = t.Tipping ? 25f : 0f;
                if (t.IsTipped && !tippedWarned) { tippedWarned = true; Msg("Trolley overturned!", true); }
                if (!t.IsTipped) tippedWarned = false;
                return;
            }

            var imp = Current;
            // hydraulic lift: ~1.5 s full stroke; heavy implements lift slower / not fully
            bool canLift = imp.TotalMass <= tractor.Spec.maxLiftKg * 1.15f;
            float target = imp.Lowered ? 0f : (canLift ? 1f : 0.25f);
            imp.LiftAmount = Mathf.MoveTowards(imp.LiftAmount, target, dt / 1.5f);
            // lowered: implement sits 6 cm into the soil; raised: half a metre up
            imp.transform.localPosition = new Vector3(mountLocal.x, -threePoint.localPosition.y - 0.06f + imp.LiftAmount * 0.5f, mountLocal.z);

            // is it in the soil?
            bool inSoil = false; FieldArea field = null;
            Vector3 wp = imp.workPoint.position;
            RaycastHit hit;
            if (imp.LiftAmount < 0.15f && Physics.Raycast(wp + Vector3.up * 1f, Vector3.down, out hit, 1.4f, Physics.DefaultRaycastLayers & ~(1 << 2), QueryTriggerInteraction.Ignore))
            {
                var kind = SurfaceZone.Of(hit.collider, hit.point);
                inSoil = kind != SurfaceKind.Asphalt && kind != SurfaceKind.Rock && kind != SurfaceKind.Water;
                field = hit.collider.GetComponent<FieldArea>();
            }

            // draft: soil resistance pulling back on the tractor
            float draft = 0f;
            if (inSoil && imp.work != WorkKind.Spray)
            {
                float soil = field != null ? field.Hardness(wp) : 1.2f;
                draft = imp.draftNPerMetre * imp.workWidth * soil * (0.75f + 0.25f * Mathf.Clamp01(Mathf.Abs(speed) / 2.5f));
                draft *= Mathf.Clamp01(Mathf.Abs(speed) / 0.25f + 0.3f);
                rb.AddForceAtPosition(-transform.forward * Mathf.Sign(speed + 0.0001f) * draft, threePoint.position, ForceMode.Force);
            }
            DraftForce = draft;
            // PTO power comes off the engine
            tractor.Engine.PtoLoadNm = imp.PtoOn ? imp.ptoPowerKw * 1000f / Mathf.Max(60f, tractor.Engine.Omega) * (inSoil ? 1f : 0.35f) : 0f;
            if (imp.PtoOn && !tractor.Engine.Running) imp.PtoOn = false;

            imp.WorkTick(dt, speed, inSoil);
            bool working = inSoil && Mathf.Abs(speed) > 0.3f && (imp.work != WorkKind.Rotavate || imp.PtoOn);
            if (imp.work == WorkKind.Spray) working = imp.PtoOn && Mathf.Abs(speed) > 0.3f && imp.LiftAmount < 0.6f;
            if (working && field == null && imp.work == WorkKind.Spray)
            {
                RaycastHit h2;
                if (Physics.Raycast(wp + Vector3.up, Vector3.down, out h2, 3f, Physics.DefaultRaycastLayers & ~(1 << 2), QueryTriggerInteraction.Ignore)) field = h2.collider.GetComponent<FieldArea>();
            }
            if (working && field != null)
            {
                float earned = field.Work(wp, imp.transform.right, imp.workWidth, imp.work);
                if (earned > 0f) Career.AddEarnings(earned);
            }
        }
        bool tippedWarned;
    }
}
