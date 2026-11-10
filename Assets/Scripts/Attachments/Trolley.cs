using UnityEngine;

namespace TractorSim
{
    public enum CargoType { None, Wheat, Rice, Sugarcane, Hay, Sand, Bricks, Soil, Fertiliser, Apples, Cement }

    /// <summary>
    /// Indian tractor trolley (hydraulic tipper). Its own rigidbody on sphere-cast wheels with
    /// tyre side-grip and brakes; joined to the tractor drawbar with an articulated joint.
    /// Load mass raises the centre of mass, so a full trolley sways, pushes downhill and can tip over.
    /// </summary>
    public class Trolley : Implement
    {
        public float emptyKg = 1300f;
        public float capacityKg = 5000f;
        public float CargoKg { get; private set; }
        public CargoType Cargo { get; private set; }
        public float TipAngle { get; private set; }      // 0..45 deg
        public bool Tipping { get; set; }
        public float Spilled { get; private set; }       // kg lost on the way
        public float BrakeInput;                           // set by the tractor (0..1)
        public bool ParkBrake = true;

        class TWheel { public Vector3 anchor; public float radius, width, travel, k, c, comp, prevComp, spin; public bool grounded; public Transform vis; public Vector3 contact; public float Fz; }
        TWheel[] wheels;
        Transform bed, cargoVis, jack;
        Renderer cargoRenderer;
        float loadTimer, tipDump;
        int layerMask;

        public override float TotalMass { get { return emptyKg + CargoKg; } }
        public float Fill { get { return capacityKg > 0 ? CargoKg / capacityKg : 0f; } }
        public bool IsTipped { get { return Vector3.Dot(transform.up, Vector3.up) < 0.4f; } }

        public static Trolley Build(string id, string name, Vector3 pos, Quaternion rot, bool tandem)
        {
            var go = new GameObject("Trolley_" + id);
            go.transform.SetPositionAndRotation(pos, rot);
            var rb = go.AddComponent<Rigidbody>();
            var t = go.AddComponent<Trolley>();
            t.id = id; t.displayName = name; t.hitchType = HitchType.Drawbar;
            t.emptyKg = tandem ? 2300f : 1300f; t.capacityKg = tandem ? 10000f : 5000f;
            rb.mass = t.emptyKg;
            rb.linearDamping = 0.02f; rb.angularDamping = 0.2f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.solverIterations = 14;
            t.massKg = t.emptyKg;

            float L = tandem ? 4.6f : 3.6f, W = 2.0f, H = 0.75f;
            float bedY = 1.05f;
            float axleZ = tandem ? -L * 0.55f : -L * 0.58f;   // bed starts 1.5 m behind the hitch eye
            float bedFront = -1.4f;
            // the user's FBX tractor comes with a single-axle trolley: use its exact dimensions
            var rig = !tandem ? TractorRigInfo.Load() : null;
            GameObject rigPrefab = rig != null && rig.hasTrolley ? TractorModel.BuildTrolley() : null;
            if (rigPrefab != null)
            {
                L = rig.trolleyBedLength; W = rig.trolleyBedWidth; H = rig.trolleySideHeight;
                bedY = rig.trolleyFloorY; bedFront = rig.trolleyBedFrontZ; axleZ = rig.trolleyAxleZ;
            }
            var red = MatLib.Opaque(new Color(0.62f, 0.12f, 0.08f), 0.4f, 0.2f);
            var blue = MatLib.Opaque(new Color(0.15f, 0.3f, 0.55f), 0.4f, 0.2f);
            var dark = MatLib.Opaque(new Color(0.13f, 0.13f, 0.14f), 0.35f, 0.5f);
            var rubber = MatLib.Opaque(new Color(0.07f, 0.07f, 0.07f), 0.15f, 0f);
            var rim = MatLib.Opaque(new Color(0.75f, 0.73f, 0.68f), 0.5f, 0.6f);

            var vis = new GameObject("Visual").transform; vis.SetParent(go.transform, false);
            // chassis & drawbar (A-frame to the hitch eye at z = 0)
            var chassis = new GameObject("Chassis").transform; chassis.SetParent(vis, false);
            for (int s = -1; s <= 1; s += 2)
            {
                MeshUtil.Box(chassis, new Vector3(s * 0.55f, bedY - 0.25f, bedFront - L * 0.5f), new Vector3(0.12f, 0.16f, L), dark, "Rail");
                MeshUtil.Box(chassis, new Vector3(s * 0.28f, bedY - 0.4f, -0.72f), new Vector3(0.1f, 0.1f, 1.55f), dark, "Drawbar", new Vector3(-14f, s * -11f, 0));
            }
            MeshUtil.CylY(chassis, new Vector3(0, 0.5f, 0f), 0.12f, 0.08f, dark, "HitchEye");
            MeshUtil.Box(chassis, new Vector3(0, bedY - 0.35f, bedFront - L + 0.3f), new Vector3(0.14f, 0.3f, 0.14f), dark, "TipRam");
            MeshUtil.CombineByMaterial(chassis.gameObject);

            // tipping bed: pivots at its rear lower edge
            t.bed = new GameObject("Bed").transform; t.bed.SetParent(vis, false);
            t.bed.localPosition = new Vector3(0, bedY - 0.15f, bedFront - L);
            var bedVis = new GameObject("BedVisual").transform; bedVis.SetParent(t.bed, false);
            bedVis.localPosition = new Vector3(0, 0.15f, L);       // undo the pivot offset
            MeshUtil.Box(bedVis, new Vector3(0, 0f, -L * 0.5f), new Vector3(W, 0.08f, L), red, "Floor");
            for (int s = -1; s <= 1; s += 2)
            {
                MeshUtil.Box(bedVis, new Vector3(s * W * 0.5f, H * 0.5f, -L * 0.5f), new Vector3(0.05f, H, L), red, "Side");
                MeshUtil.Box(bedVis, new Vector3(s * W * 0.5f + s * 0.03f, H * 0.5f, -L * 0.5f), new Vector3(0.02f, H * 0.2f, L), blue, "Stripe");
                for (int k = 0; k < 4; k++) MeshUtil.Box(bedVis, new Vector3(s * (W * 0.5f + 0.02f), H * 0.5f, -L * (0.12f + k * 0.25f)), new Vector3(0.04f, H, 0.06f), dark, "Stake");
            }
            MeshUtil.Box(bedVis, new Vector3(0, H * 0.5f, 0f), new Vector3(W, H, 0.05f), red, "FrontWall");
            MeshUtil.Box(bedVis, new Vector3(0, H * 0.5f, -L), new Vector3(W, H, 0.05f), red, "Tailgate");
            MeshUtil.Box(bedVis, new Vector3(0, H * 0.8f, -L - 0.03f), new Vector3(1.0f, 0.18f, 0.02f), MatLib.Opaque(new Color(0.95f, 0.85f, 0.15f), 0.4f, 0f), "RearPlate");
            // reflectors / tail lamps
            for (int s = -1; s <= 1; s += 2) MeshUtil.Box(bedVis, new Vector3(s * 0.8f, 0.15f, -L - 0.03f), new Vector3(0.14f, 0.08f, 0.02f), MatLib.Emissive(new Color(0.8f, 0.05f, 0.05f), new Color(0.6f, 0f, 0f)), "TailLamp");
            MeshUtil.CombineByMaterial(bedVis.gameObject);
            // cargo heap (scaled with fill)
            t.cargoVis = MeshUtil.Box(bedVis, new Vector3(0, 0.05f, -L * 0.5f), new Vector3(W - 0.1f, 0.01f, L - 0.1f), MatLib.Opaque(Color.white, 0.1f, 0f), "Cargo").transform;
            t.cargoRenderer = t.cargoVis.GetComponent<Renderer>();
            t.cargoVis.gameObject.SetActive(false);

            // colliders (bed + chassis) and parking jack under the drawbar
            AddCol(go.transform, new Vector3(0, bedY + H * 0.45f, bedFront - L * 0.5f), new Vector3(W, H + 0.1f, L));
            AddCol(go.transform, new Vector3(0, 0.6f, -0.75f), new Vector3(0.5f, 0.2f, 1.2f));
            t.jack = new GameObject("Jack").transform; t.jack.SetParent(go.transform, false);
            t.jack.localPosition = new Vector3(0, 0.3f, -0.55f);
            t.jack.gameObject.layer = TractorBuilder.TractorLayer;
            t.jack.gameObject.AddComponent<BoxCollider>().size = new Vector3(0.2f, 0.6f, 0.2f);
            MeshUtil.Box(t.jack, Vector3.zero, new Vector3(0.08f, 0.6f, 0.08f), dark, "JackLeg");

            // wheels
            float r = tandem ? 0.48f : 0.55f, wW = 0.3f;
            float tr = 0.98f;
            if (rigPrefab != null) { r = rig.trolleyWheelRadius; tr = rig.trolleyTrack * 0.5f; }
            var axles = tandem ? new[] { axleZ + 0.6f, axleZ - 0.6f } : new[] { axleZ };
            t.wheels = new TWheel[axles.Length * 2];
            int idx = 0;
            foreach (var az in axles)
            {
                MeshUtil.CylX(vis, new Vector3(0, r, az), 0.12f, tr * 2f, dark, "Axle");
                for (int s = -1; s <= 1; s += 2)
                {
                    var w = new TWheel { radius = r, width = wW, travel = 0.08f, k = tandem ? 260000f : 220000f, c = tandem ? 16000f : 12000f };
                    w.anchor = new Vector3(s * tr, r + w.travel, az);
                    var wv = new GameObject("Wheel").transform; wv.SetParent(go.transform, false);
                    wv.localPosition = new Vector3(s * tr, r, az);
                    MeshUtil.CylX(wv, Vector3.zero, r * 2f, wW, MatLib.Opaque(new Color(0.08f, 0.08f, 0.08f), 0.12f, 0f, ProcTex.Tread(), new Vector2(1f, 5f)), "Tyre");
                    MeshUtil.CylX(wv, new Vector3(s * 0.01f, 0, 0), r * 1.1f, wW * 1.05f, rim, "Rim");
                    MeshUtil.CylX(wv, new Vector3(s * wW * 0.5f, 0, 0), r * 0.4f, 0.04f, rubber, "Hub");
                    MeshUtil.CombineByMaterial(wv.gameObject);
                    w.vis = wv;
                    t.wheels[idx++] = w;
                }
            }
            rb.centerOfMass = new Vector3(0, 0.95f, axleZ + 0.15f);

            t.hitchPoint = new GameObject("Hitch").transform; t.hitchPoint.SetParent(go.transform, false); t.hitchPoint.localPosition = new Vector3(0, 0.5f, 0f);
            t.workPoint = t.hitchPoint;
            t.bedLength = L; t.axleZ = axleZ; t.bedHeight = H; t.bedWidth = W;
            if (rigPrefab != null) ApplyRigTrolley(t, vis, rigPrefab);
            ApplyArtTrolley(t, vis);
            return t;
        }

        float bedLength, axleZ, bedHeight, bedWidth = 2f;

        static void AddCol(Transform parent, Vector3 c, Vector3 s)
        {
            var g = new GameObject("Col"); g.transform.SetParent(parent, false);
            g.layer = TractorBuilder.TractorLayer;   // vehicles' wheel casts ignore each other's bodies
            var b = g.AddComponent<BoxCollider>(); b.center = c; b.size = s;
        }

        /// <summary>Swap the procedural trolley visuals for the FBX trolley (bed tips, wheels spin, cargo heap stays).</summary>
        static void ApplyRigTrolley(Trolley t, Transform vis, GameObject prefab)
        {
            foreach (var r in vis.GetComponentsInChildren<Renderer>(true)) if (r.transform != t.cargoVis) r.enabled = false;
            foreach (var w in t.wheels) if (w.vis != null) foreach (var r in w.vis.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            var m = prefab;                            // built by TractorModel for this trolley
            m.transform.SetParent(t.transform, false);
            m.name = "RigModel";
            m.transform.localPosition = new Vector3(0f, 0.5f, 0f);      // prefab origin = hitch eye, 0.5 m above ground
            m.transform.localRotation = Quaternion.identity;
            m.transform.localScale = Vector3.one;
            foreach (var c in m.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
            var rigBed = m.transform.Find("Bed");
            if (rigBed != null) rigBed.SetParent(t.bed, true);           // tips with the hydraulic bed
            var rigWheels = m.transform.Find("Wheels");
            if (rigWheels != null && t.wheels.Length > 0 && t.wheels[0].vis != null) rigWheels.SetParent(t.wheels[0].vis, true);  // spins about the axle line
        }

        static void ApplyArtTrolley(Trolley t, Transform vis)
        {
            var cat = ArtCatalog.Load();
            var art = cat != null ? cat.FindImplement(t.id) : null;
            if (art == null || art.model == null) return;
            var m = Object.Instantiate(art.model, t.transform);
            m.transform.localPosition = art.offset; m.transform.localEulerAngles = art.euler; m.transform.localScale = Vector3.one * (art.scale <= 0 ? 1 : art.scale);
            foreach (var c in m.GetComponentsInChildren<Collider>()) Object.Destroy(c);
            var chassis = vis.Find("Chassis"); if (chassis != null) chassis.gameObject.SetActive(false);
            var bedVis = t.bed.Find("BedVisual");
            if (bedVis != null) foreach (var r in bedVis.GetComponentsInChildren<Renderer>()) if (r.transform != t.cargoVis) r.enabled = false;
        }

        protected override void Awake()
        {
            base.Awake();
            layerMask = Physics.DefaultRaycastLayers & ~(1 << 2);
        }

        // ------------------------------------------------------------ cargo
        public static Color CargoColor(CargoType c)
        {
            switch (c)
            {
                case CargoType.Wheat: return new Color(0.82f, 0.66f, 0.32f);
                case CargoType.Rice: return new Color(0.78f, 0.7f, 0.45f);
                case CargoType.Sugarcane: return new Color(0.42f, 0.55f, 0.22f);
                case CargoType.Hay: return new Color(0.85f, 0.75f, 0.42f);
                case CargoType.Sand: return new Color(0.86f, 0.76f, 0.55f);
                case CargoType.Bricks: return new Color(0.66f, 0.3f, 0.2f);
                case CargoType.Soil: return new Color(0.4f, 0.29f, 0.19f);
                case CargoType.Fertiliser: return new Color(0.92f, 0.92f, 0.9f);
                case CargoType.Apples: return new Color(0.7f, 0.12f, 0.1f);
                case CargoType.Cement: return new Color(0.6f, 0.6f, 0.6f);
            }
            return Color.white;
        }

        /// <summary>Density decides how high the heap gets (sand is heavy and low, hay is light and tall).</summary>
        static float Density(CargoType c)
        {
            switch (c)
            {
                case CargoType.Sand: case CargoType.Soil: return 1600f;
                case CargoType.Bricks: return 1400f;
                case CargoType.Cement: case CargoType.Fertiliser: return 1100f;
                case CargoType.Wheat: case CargoType.Rice: return 780f;
                case CargoType.Apples: return 600f;
                case CargoType.Sugarcane: return 450f;
                case CargoType.Hay: return 180f;
            }
            return 800f;
        }

        public void AddCargo(CargoType type, float kg)
        {
            if (Cargo != type && CargoKg > 1f) return;
            Cargo = type;
            CargoKg = Mathf.Clamp(CargoKg + kg, 0f, capacityKg);
            ApplyMass();
        }

        public float RemoveCargo(float kg)
        {
            float r = Mathf.Min(kg, CargoKg);
            CargoKg -= r;
            if (CargoKg <= 0.5f) { CargoKg = 0f; Cargo = CargoType.None; }
            ApplyMass();
            return r;
        }

        public void ResetSpill() { Spilled = 0f; }

        void ApplyMass()
        {
            var rb = Body != null ? Body : GetComponent<Rigidbody>();
            rb.mass = emptyKg + CargoKg;
            // centre of mass rises with the heap → top-heavy when loaded
            float heapH = HeapHeight();
            rb.centerOfMass = new Vector3(0, Mathf.Lerp(0.95f, 1.1f + heapH * 0.5f, Fill), axleZ + 0.15f);
            if (cargoVis != null)
            {
                bool any = CargoKg > 1f;
                cargoVis.gameObject.SetActive(any);
                if (any)
                {
                    cargoVis.localScale = new Vector3(bedWidth - 0.1f, Mathf.Max(0.02f, heapH), bedLength - 0.1f);
                    cargoVis.localPosition = new Vector3(0, 0.04f + heapH * 0.5f, -bedLength * 0.5f);
                    cargoRenderer.sharedMaterial = MatLib.Opaque(CargoColor(Cargo), Cargo == CargoType.Apples ? 0.5f : 0.08f, 0f,
                        ProcTex.Ground("cargo" + Cargo, CargoColor(Cargo) * 0.8f, CargoColor(Cargo) * 1.15f, Cargo == CargoType.Apples ? 22f : 12f, 0.4f, (int)Cargo + 50), new Vector2(3f, 6f));
                }
            }
        }

        float HeapHeight()
        {
            float vol = CargoKg / Density(Cargo);              // m^3
            float area = (bedWidth - 0.1f) * (bedLength - 0.1f);
            return Mathf.Min(vol / area, bedHeight + 0.9f);      // heaped above the sides when light & bulky
        }

        // ------------------------------------------------------------ physics
        void FixedUpdate()
        {
            if (Body == null || wheels == null) return;
            float dt = Time.fixedDeltaTime;
            var t = transform;
            Vector3 up = t.up;
            float brake = Mathf.Max(BrakeInput, (!Attached && ParkBrake) ? 1f : 0f);
            if (jack != null) jack.gameObject.SetActive(!Attached);

            foreach (var w in wheels)
            {
                Vector3 a = t.TransformPoint(w.anchor);
                RaycastHit hit;
                float castR = w.radius * 0.9f, offset = w.radius - castR, lift = 0.3f;
                w.prevComp = w.comp;
                if (Physics.SphereCast(a + up * lift, castR, -up, out hit, w.travel + lift + offset + 0.05f, layerMask, QueryTriggerInteraction.Ignore)
                    && hit.rigidbody != Body)
                {
                    float ext = hit.distance - lift - offset;
                    w.grounded = true;
                    w.comp = w.travel - ext;
                    w.contact = hit.point;
                    float comp = Mathf.Max(0f, w.comp);
                    float f = Mathf.Max(0f, w.k * comp + w.c * (w.comp - w.prevComp) / dt);
                    if (comp > w.travel) f += w.k * 6f * (comp - w.travel);
                    w.Fz = f;
                    Vector3 center = a - up * Mathf.Clamp(ext, -0.2f, w.travel);
                    Body.AddForceAtPosition(up * f, center, ForceMode.Force);

                    // tyre forces: free rolling, braked, with side grip
                    Vector3 n = hit.normal;
                    Vector3 fwd = Vector3.ProjectOnPlane(t.forward, n).normalized;
                    Vector3 side = Vector3.Cross(n, fwd);
                    Vector3 v = Body.GetPointVelocity(hit.point);
                    float vl = Vector3.Dot(v, fwd), vs = Vector3.Dot(v, side);
                    var surf = SurfaceProps.Get(SurfaceZone.Of(hit.collider, hit.point));
                    float mu = surf.grip;
                    float fy = TireModel.LatForce(vs, vl, f, mu);
                    if (Mathf.Abs(vs) < 0.25f && Mathf.Abs(vl) < 1f) fy = Mathf.Clamp(fy - vs * Body.mass / wheels.Length / dt * 0.25f, -mu * f, mu * f);
                    float roll = -surf.rolling * f * Mathf.Clamp(vl / 0.4f, -1f, 1f);
                    float fb = -brake * mu * f * 0.9f * Mathf.Clamp(vl / 0.3f, -1f, 1f);
                    float fx = roll + fb;
                    float mag = Mathf.Sqrt(fx * fx + fy * fy), max = mu * f;
                    if (mag > max && mag > 0f) { fx *= max / mag; fy *= max / mag; }
                    Body.AddForceAtPosition(fwd * fx + side * fy, Vector3.Lerp(hit.point, center, 0.4f), ForceMode.Force);
                    w.spin += (brake > 0.95f ? 0f : vl / w.radius) * Mathf.Rad2Deg * dt;
                    if (w.vis != null) w.vis.localPosition = t.InverseTransformPoint(center);
                }
                else
                {
                    w.grounded = false; w.comp = 0f; w.Fz = 0f;
                    if (w.vis != null) w.vis.localPosition = w.anchor - Vector3.up * w.travel;
                }
                if (w.vis != null) w.vis.localRotation = Quaternion.Euler(w.spin, 0f, 0f);
            }

            UpdateTipping(dt);
            UpdateSpill(dt);
        }

        void UpdateTipping(float dt)
        {
            float target = Tipping ? 45f : 0f;
            TipAngle = Mathf.MoveTowards(TipAngle, target, dt * (Tipping ? 9f : 14f));
            if (bed != null) bed.localRotation = Quaternion.Euler(-TipAngle, 0f, 0f);
        }

        /// <summary>Hard bumps and steep tilt make loose cargo fall off.</summary>
        void UpdateSpill(float dt)
        {
            if (CargoKg <= 0f || Cargo == CargoType.Bricks && TipAngle < 5f) return;
            float tilt = Vector3.Angle(transform.up, Vector3.up);
            float loss = 0f;
            if (TipAngle < 2f && tilt > 22f) loss += (tilt - 22f) * 6f * dt;      // leaning hard: heap slides off
            if (IsTipped) loss += CargoKg * 0.5f * dt;
            if (loss > 0f) { Spilled += RemoveCargo(loss); }
        }

        /// <summary>Tipper unloading: returns kg dumped this frame.</summary>
        public float DumpTick(float dt)
        {
            if (TipAngle < 20f || CargoKg <= 0f) return 0f;
            float rate = capacityKg / 9f * Mathf.InverseLerp(20f, 45f, TipAngle);
            return RemoveCargo(rate * dt);
        }

        public override void WorkTick(float dt, float groundSpeed, bool inSoil) { }
    }
}
