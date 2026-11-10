using UnityEngine;

namespace TractorSim
{
    /// <summary>
    /// Builds a complete, drivable tractor at runtime: rigidbody, colliders, wheel physics,
    /// a detailed procedural model (original design, no brand marks), lights, effects and anchors.
    /// Body origin = ground level, midway between the axles, +Z forward.
    /// </summary>
    public static class TractorBuilder
    {
        public const int TractorLayer = 2; // "Ignore Raycast" so wheel casts don't hit our own colliders

        public static Color C(float[] rgb) { return new Color(rgb[0], rgb[1], rgb[2]); }

        public static TractorController Build(TractorSpec spec, Vector3 pos, Quaternion rot)
        {
            var root = new GameObject("Tractor_" + spec.id);
            root.transform.SetPositionAndRotation(pos, rot);
            root.layer = TractorLayer;

            // the user's FBX tractor (if imported) sets wheel sizes, wheelbase and track for every brand
            var cat = ArtCatalog.Load();
            bool hasArt = cat != null && cat.FindTractor(spec.id) != null && cat.FindTractor(spec.id).model != null;
            var rig = hasArt ? null : TractorRigInfo.Load();
            GameObject rigPrefab = rig != null ? TractorRigInfo.TractorPrefab() : null;
            if (rigPrefab != null) rig.ApplyTo(spec);

            // ---------------- physics body ----------------
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = spec.massKg;
            rb.linearDamping = 0.02f;
            rb.angularDamping = 0.25f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            rb.maxAngularVelocity = 12f;

            float zr = -spec.wheelBase * 0.5f, zf = spec.wheelBase * 0.5f;
            float xr = spec.trackRear * 0.5f, xf = spec.trackFront * 0.5f;

            // body colliders follow the same scaling as the visual model (designed around a 1.96 m wheelbase)
            float sc = spec.bodyScale, sz = spec.wheelBase / 1.96f;
            System.Func<Vector3, Vector3> S = v => new Vector3(v.x * sc, v.y * sc, v.z * sz);
            AddBox(root, S(new Vector3(0, 0.95f, 0.9f)), S(new Vector3(0.66f, 0.75f, 1.9f)));
            AddBox(root, S(new Vector3(0, 0.72f, 1.97f)), S(new Vector3(0.62f, 0.35f, 0.25f)));
            AddBox(root, S(new Vector3(0, 0.95f, -0.55f)), S(new Vector3(0.7f, 0.9f, 1.0f)));
            AddBox(root, new Vector3(xr, 1.15f * sc, zr), new Vector3(0.46f * sc, 0.55f * sc, 1.1f * sc));
            AddBox(root, new Vector3(-xr, 1.15f * sc, zr), new Vector3(0.46f * sc, 0.55f * sc, 1.1f * sc));
            AddBox(root, new Vector3(0, 0.5f * sc, zr - 0.5f * sc), new Vector3(0.3f, 0.15f, 0.3f) * sc);
            if (spec.style == BodyStyle.Cabin) AddBox(root, S(new Vector3(0, 1.95f, -0.55f)), S(new Vector3(1.3f, 1.25f, 1.45f)));
            AddWheelCollider(root, new Vector3(xr, spec.rearWheelRadius, zr), 1.2f, spec.rearWheelWidth * 0.95f);
            AddWheelCollider(root, new Vector3(-xr, spec.rearWheelRadius, zr), 1.2f, spec.rearWheelWidth * 0.95f);
            AddWheelCollider(root, new Vector3(xf, spec.frontWheelRadius, zf), 0.66f, spec.frontWheelWidth);
            AddWheelCollider(root, new Vector3(-xf, spec.frontWheelRadius, zf), 0.66f, spec.frontWheelWidth);
            rb.centerOfMass = new Vector3(0f, spec.centerOfMassHeight, spec.centerOfMassZ);

            // ---------------- wheels ----------------
            float rearTravel = spec.rearTravel, frontTravel = spec.frontTravel;
            float rearK = spec.rearSpring, frontK = spec.frontSpring;
            float g = 9.81f;
            float rearShare = Mathf.Clamp01((zf - spec.centerOfMassZ) / spec.wheelBase);
            float rearLoad = spec.massKg * g * rearShare * 0.5f, frontLoad = spec.massKg * g * (1f - rearShare) * 0.5f;
            float rearAnchorY = spec.rearWheelRadius + rearTravel - rearLoad / rearK;
            float frontAnchorY = spec.frontWheelRadius + frontTravel - frontLoad / frontK;
            var wheels = new WheelState[4];
            wheels[0] = MakeWheel("FL", true, true, spec.fourWheelDrive, new Vector3(-xf, frontAnchorY, zf), spec.frontWheelRadius, spec.frontWheelWidth, frontTravel, frontK, spec.frontDamper);
            wheels[1] = MakeWheel("FR", true, false, spec.fourWheelDrive, new Vector3(xf, frontAnchorY, zf), spec.frontWheelRadius, spec.frontWheelWidth, frontTravel, frontK, spec.frontDamper);
            wheels[2] = MakeWheel("RL", false, true, true, new Vector3(-xr, rearAnchorY, zr), spec.rearWheelRadius, spec.rearWheelWidth, rearTravel, rearK, spec.rearDamper);
            wheels[3] = MakeWheel("RR", false, false, true, new Vector3(xr, rearAnchorY, zr), spec.rearWheelRadius, spec.rearWheelWidth, rearTravel, rearK, spec.rearDamper);

            var physics = root.AddComponent<VehiclePhysics>();
            var controller = root.AddComponent<TractorController>();
            controller.Init(spec, physics);
            physics.Init(spec, controller.Drivetrain, wheels);

            // ---------------- visuals ----------------
            var vis = root.AddComponent<TractorVisuals>();
            BuildModel(root.transform, spec, vis, wheels);
            ApplyArt(root.transform, spec, vis);
            if (rigPrefab != null) ApplyRig(root.transform, spec, vis, rig, rigPrefab);
            vis.Init(controller, physics);

            // ---------------- hitch points & attachment manager ----------------
            var drawbar = Anchor(root.transform, "HitchDrawbar", rigPrefab != null && rig.hasTrolley
                ? new Vector3(0, 0.5f, rig.hitchZ * sc)
                : new Vector3(0, 0.5f * sc, zr - 0.72f * sc));
            var threePoint = Anchor(root.transform, "Hitch3P", new Vector3(0, 0.55f * sc, zr - 0.55f * sc));
            var am = root.AddComponent<AttachmentManager>();
            am.Init(controller, drawbar, threePoint);
            return controller;
        }

        static WheelState MakeWheel(string name, bool front, bool left, bool driven, Vector3 anchor, float r, float w, float travel, float k, float c)
        {
            return new WheelState { name = name, front = front, left = left, driven = driven, anchorLocal = anchor, radius = r, width = w, travel = travel, spring = k, damper = c };
        }

        static void AddBox(GameObject root, Vector3 c, Vector3 s)
        {
            var go = new GameObject("Col");
            go.layer = TractorLayer;
            go.transform.SetParent(root.transform, false);
            var b = go.AddComponent<BoxCollider>();
            b.center = c; b.size = s;
        }

        static void AddWheelCollider(GameObject root, Vector3 c, float diameter, float width)
        {
            // slightly smaller than the tyre: acts as a crash/rollover shell, not as the suspension
            var go = new GameObject("WheelShell");
            go.layer = TractorLayer;
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = c;
            go.transform.localEulerAngles = new Vector3(0, 0, 90);
            go.transform.localScale = new Vector3(diameter * 0.88f, width * 0.5f, diameter * 0.88f);
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = MeshUtil.PrimMesh(PrimitiveType.Cylinder);
            mc.convex = true;
        }

        // =====================================================================
        static void BuildModel(Transform root, TractorSpec realSpec, TractorVisuals vis, WheelState[] wheels)
        {
            // The body is modelled once at "unit" size (the 47 HP reference tractor) and then scaled
            // to each brand's proportions; wheels are always built at their true sizes.
            var spec = TractorCatalog.Get("haldhar47");
            Color bodyColor = C(realSpec.bodyRgb);
            Color accent = C(realSpec.accentRgb);
            var red = MatLib.Opaque(bodyColor, 0.55f, 0.15f);
            var redDark = MatLib.Opaque(bodyColor * 0.7f, 0.4f, 0.1f);
            var dark = MatLib.Opaque(new Color(0.14f, 0.14f, 0.15f), 0.35f, 0.5f);
            var steel = MatLib.Opaque(new Color(0.5f, 0.5f, 0.52f), 0.55f, 0.8f);
            var chrome = MatLib.Opaque(new Color(0.85f, 0.85f, 0.85f), 0.9f, 1f);
            var black = MatLib.Opaque(new Color(0.05f, 0.05f, 0.05f), 0.15f, 0f);
            var seatMat = MatLib.Opaque(new Color(0.09f, 0.08f, 0.07f), 0.3f, 0f);
            var cream = MatLib.Opaque(new Color(0.88f, 0.85f, 0.78f), 0.5f, 0.1f);
            var rimMat = MatLib.Opaque(C(realSpec.rimRgb), 0.5f, 0.6f);
            var accentMat = MatLib.Opaque(accent, 0.6f, 0.1f);

            var visual = new GameObject("Visual").transform;
            visual.SetParent(root, false);
            var bodyRoot = new GameObject("Body").transform;      // scaled to the brand's proportions
            bodyRoot.SetParent(visual, false);
            bodyRoot.localScale = new Vector3(realSpec.bodyScale, realSpec.bodyScale, realSpec.wheelBase / 1.96f);
            vis.bodyRoot = bodyRoot;
            var stat = new GameObject("Static").transform;
            stat.SetParent(bodyRoot, false);

            float zr = -spec.wheelBase * 0.5f, zf = spec.wheelBase * 0.5f;
            float xr = spec.trackRear * 0.5f, xf = spec.trackFront * 0.5f;

            // --- engine & bonnet ---
            MeshUtil.Box(stat, new Vector3(0, 1.0f, 0.95f), new Vector3(0.62f, 0.5f, 1.62f), red, "Bonnet");
            MeshUtil.CylZ(stat, new Vector3(0, 1.25f, 0.95f), 0.62f, 1.62f, red).transform.localScale = new Vector3(0.62f, 0.81f, 0.18f);
            MeshUtil.Box(stat, new Vector3(0, 1.0f, 1.77f), new Vector3(0.58f, 0.52f, 0.06f), dark, "Grille");
            for (int i = 0; i < 6; i++) MeshUtil.Box(stat, new Vector3(0, 0.8f + i * 0.08f, 1.80f), new Vector3(0.52f, 0.02f, 0.02f), steel);
            MeshUtil.Box(stat, new Vector3(0, 0.62f, 0.75f), new Vector3(0.48f, 0.3f, 1.9f), dark, "EngineBlock");
            MeshUtil.Box(stat, new Vector3(0.25f, 0.72f, 0.95f), new Vector3(0.06f, 0.18f, 0.9f), steel, "InjectionPump");
            for (int i = 0; i < 3; i++) MeshUtil.CylX(stat, new Vector3(0.27f, 0.76f, 0.7f + i * 0.25f), 0.05f, 0.05f, chrome);
            MeshUtil.CylY(stat, new Vector3(-0.25f, 0.72f, 0.6f), 0.11f, 0.2f, MatLib.Opaque(new Color(0.15f, 0.3f, 0.6f), 0.4f, 0.2f), "OilFilter");
            // front weight frame & bumper
            MeshUtil.Box(stat, new Vector3(0, 0.72f, 1.97f), new Vector3(0.62f, 0.32f, 0.22f), dark, "FrontWeights");
            for (int i = 0; i < 4; i++) MeshUtil.Box(stat, new Vector3(0, 0.6f + i * 0.08f, 2.09f), new Vector3(0.6f, 0.06f, 0.05f), MatLib.Opaque(new Color(0.2f, 0.2f, 0.21f), 0.3f, 0.3f));
            // front axle (pivoting beam) & steering arms
            MeshUtil.Box(stat, new Vector3(0, spec.frontWheelRadius + 0.04f, zf), new Vector3(spec.trackFront - 0.2f, 0.12f, 0.12f), dark, "FrontAxle");
            MeshUtil.Box(stat, new Vector3(0, spec.frontWheelRadius + 0.14f, zf), new Vector3(0.18f, 0.12f, 0.3f), dark);
            MeshUtil.CylX(stat, new Vector3(0, spec.frontWheelRadius - 0.02f, zf - 0.2f), 0.04f, spec.trackFront - 0.35f, steel, "TieRod");
            // exhaust & air cleaner
            MeshUtil.CylY(stat, new Vector3(0.21f, 1.75f, 1.32f), 0.075f, 1.0f, black, "ExhaustPipe");
            MeshUtil.CylY(stat, new Vector3(0.21f, 1.62f, 1.32f), 0.15f, 0.35f, MatLib.Opaque(new Color(0.18f, 0.17f, 0.16f), 0.25f, 0.4f), "Muffler");
            MeshUtil.CylY(stat, new Vector3(0.21f, 1.62f, 1.32f), 0.16f, 0.29f, chrome, "MufflerGuard").transform.localScale = new Vector3(0.165f, 0.13f, 0.165f);
            MeshUtil.CylY(stat, new Vector3(-0.21f, 1.42f, 1.28f), 0.15f, 0.36f, black, "AirCleaner");
            MeshUtil.CylY(stat, new Vector3(-0.21f, 1.62f, 1.28f), 0.18f, 0.05f, black);
            // headlamps (housing) – lenses are separate emissive objects
            MeshUtil.CylZ(stat, new Vector3(0.28f, 1.17f, 1.66f), 0.17f, 0.16f, chrome, "HeadlampL");
            MeshUtil.CylZ(stat, new Vector3(-0.28f, 1.17f, 1.66f), 0.17f, 0.16f, chrome, "HeadlampR");

            // --- transmission, fuel tank, dashboard ---
            MeshUtil.Box(stat, new Vector3(0, 0.78f, -0.3f), new Vector3(0.62f, 0.62f, 0.9f), redDark, "GearboxHousing");
            MeshUtil.CylX(stat, new Vector3(0, spec.rearWheelRadius, zr), 0.48f, spec.trackRear - 0.45f, redDark, "RearAxleHousing");
            MeshUtil.CylX(stat, new Vector3(0, spec.rearWheelRadius, zr), 0.22f, spec.trackRear - 0.05f, dark, "Halfshafts");
            MeshUtil.Box(stat, new Vector3(0, 1.31f, 0.2f), new Vector3(0.6f, 0.22f, 0.5f), red, "FuelTank");
            MeshUtil.CylY(stat, new Vector3(0.18f, 1.44f, 0.32f), 0.09f, 0.05f, chrome, "FuelCap");
            MeshUtil.Box(stat, new Vector3(0, 1.36f, -0.08f), new Vector3(0.62f, 0.34f, 0.12f), dark, "Dashboard", new Vector3(-18f, 0, 0));
            // gauges on the dash
            MeshUtil.CylZ(stat, new Vector3(-0.13f, 1.42f, -0.15f), 0.13f, 0.02f, cream, "TachoFace").transform.localEulerAngles = new Vector3(-18f + 90f, 0, 0);
            MeshUtil.CylZ(stat, new Vector3(0.13f, 1.42f, -0.15f), 0.1f, 0.02f, cream, "TempFace").transform.localEulerAngles = new Vector3(-18f + 90f, 0, 0);
            // foot platform & steps
            MeshUtil.Box(stat, new Vector3(0, 0.82f, -0.62f), new Vector3(1.0f, 0.04f, 0.65f), MatLib.Opaque(new Color(0.25f, 0.25f, 0.26f), 0.2f, 0.6f), "Platform");
            MeshUtil.Box(stat, new Vector3(-0.52f, 0.5f, -0.25f), new Vector3(0.25f, 0.03f, 0.2f), steel, "Step");

            // --- rear fenders / mudguards (curved from segments) ---
            for (int s = -1; s <= 1; s += 2)
            {
                float x = s * xr;
                float R = spec.rearWheelRadius + 0.1f;
                for (int i = 0; i <= 8; i++)
                {
                    float a = Mathf.Lerp(-70f, 70f, i / 8f) * Mathf.Deg2Rad;
                    var p = new Vector3(x, spec.rearWheelRadius + Mathf.Cos(a) * R, zr + Mathf.Sin(a) * R);
                    MeshUtil.Box(stat, p, new Vector3(spec.rearWheelWidth + 0.12f, 0.035f, R * 0.29f), red, "Fender", new Vector3(a * Mathf.Rad2Deg, 0, 0));
                }
                MeshUtil.Box(stat, new Vector3(s * (xr - spec.rearWheelWidth * 0.5f - 0.07f), 1.05f, zr), new Vector3(0.03f, 0.75f, 1.3f), red, "FenderInner");
                // tool box on the fender front
                MeshUtil.Box(stat, new Vector3(x, 1.28f, zr + 0.62f), new Vector3(0.3f, 0.12f, 0.16f), dark, "ToolBox");
            }
            // --- seat ---
            MeshUtil.Box(stat, new Vector3(0, 1.08f, -0.86f), new Vector3(0.12f, 0.24f, 0.12f), dark, "SeatPost");
            MeshUtil.Box(stat, new Vector3(0, 1.22f, -0.82f), new Vector3(0.5f, 0.1f, 0.44f), seatMat, "SeatCushion");
            MeshUtil.Box(stat, new Vector3(0, 1.46f, -1.04f), new Vector3(0.5f, 0.42f, 0.08f), seatMat, "SeatBack", new Vector3(-10f, 0, 0));
            // --- rear linkage & hitch ---
            MeshUtil.Box(stat, new Vector3(0, 0.48f, zr - 0.48f), new Vector3(0.12f, 0.08f, 0.6f), steel, "Drawbar");
            MeshUtil.CylY(stat, new Vector3(0, 0.5f, zr - 0.72f), 0.05f, 0.18f, chrome, "HitchPin");
            MeshUtil.Box(stat, new Vector3(0.3f, 0.48f, zr - 0.35f), new Vector3(0.06f, 0.06f, 0.7f), dark, "LowerLinkR", new Vector3(8f, -8f, 0));
            MeshUtil.Box(stat, new Vector3(-0.3f, 0.48f, zr - 0.35f), new Vector3(0.06f, 0.06f, 0.7f), dark, "LowerLinkL", new Vector3(8f, 8f, 0));
            MeshUtil.Box(stat, new Vector3(0, 1.0f, zr - 0.35f), new Vector3(0.06f, 0.06f, 0.5f), dark, "TopLink", new Vector3(-12f, 0, 0));
            MeshUtil.CylZ(stat, new Vector3(0, 0.62f, zr - 0.32f), 0.07f, 0.2f, steel, "PTO");
            // number plate holder & reflectors
            MeshUtil.Box(stat, new Vector3(0, 0.95f, zr - 0.31f), new Vector3(0.36f, 0.14f, 0.02f), MatLib.Opaque(new Color(0.95f, 0.85f, 0.15f), 0.4f, 0f), "Plate");
            // mirrors on stalks from the fenders
            for (int s = -1; s <= 1; s += 2)
            {
                MeshUtil.Box(stat, new Vector3(s * (xr + 0.05f), 1.62f, zr + 0.55f), new Vector3(0.02f, 0.5f, 0.02f), black, "MirrorStalk", new Vector3(0, 0, -s * 12f));
                MeshUtil.Box(stat, new Vector3(s * (xr + 0.12f), 1.88f, zr + 0.55f), new Vector3(0.16f, 0.12f, 0.02f), black, "Mirror");
                MeshUtil.Box(stat, new Vector3(s * (xr + 0.12f), 1.88f, zr + 0.538f), new Vector3(0.14f, 0.1f, 0.005f), chrome, "MirrorGlass");
            }
            MeshUtil.CombineByMaterial(stat.gameObject);

            // --- brand highlight: accent stripe panel + company name on both bonnet sides and the grille ---
            var deco = new GameObject("Brand").transform; deco.SetParent(bodyRoot, false);
            for (int sgn = -1; sgn <= 1; sgn += 2)
            {
                MeshUtil.Box(deco, new Vector3(sgn * 0.313f, 1.08f, 1.0f), new Vector3(0.01f, 0.2f, 1.25f), accentMat, "BrandStripe");
                AddDecal(deco, realSpec.brand, new Vector3(sgn * 0.32f, 1.08f, 1.0f), new Vector3(0, -90 * sgn, 0), 0.0105f / realSpec.bodyScale * 1.6f, Color.Lerp(bodyColor, Color.black, 0.55f), realSpec.brand.Length > 12 ? 0.85f : 1f);
                AddDecal(deco, realSpec.model, new Vector3(sgn * 0.32f, 0.9f, 1.25f), new Vector3(0, -90 * sgn, 0), 0.008f, Color.white, 1f);
            }
            MeshUtil.Box(deco, new Vector3(0, 1.31f, 1.805f), new Vector3(0.52f, 0.09f, 0.01f), accentMat, "GrillePlate");
            AddDecal(deco, realSpec.brand, new Vector3(0, 1.31f, 1.815f), new Vector3(0, 180, 0), 0.0045f, Color.Lerp(bodyColor, Color.black, 0.55f), 1f);
            if (realSpec.style == BodyStyle.Canopy) BuildCanopy(bodyRoot, spec, accentMat, black, realSpec);
            if (realSpec.style == BodyStyle.Cabin) BuildCabin(bodyRoot, spec, red, accentMat, black, realSpec);
            vis.driver = BuildDriver(bodyRoot);

            // ---------------- moving parts ----------------
            // steering column + wheel (pivot rotates around its local up axis = column axis)
            var column = new GameObject("SteeringColumn").transform;
            column.SetParent(bodyRoot, false);
            column.localPosition = new Vector3(0, 1.48f, -0.2f);
            column.localEulerAngles = new Vector3(-35f, 0, 0);
            MeshUtil.CylY(column, new Vector3(0, -0.15f, 0), 0.05f, 0.4f, dark, "Column");
            var wheelPivot = new GameObject("SteeringWheel").transform;
            wheelPivot.SetParent(column, false);
            wheelPivot.localPosition = new Vector3(0, 0.07f, 0);
            for (int i = 0; i < 20; i++)
            {
                float a = i / 20f * Mathf.PI * 2f;
                var seg = MeshUtil.Box(wheelPivot, new Vector3(Mathf.Cos(a) * 0.2f, 0, Mathf.Sin(a) * 0.2f), new Vector3(0.03f, 0.03f, 0.068f), black, "Rim");
                seg.transform.localEulerAngles = new Vector3(0, -a * Mathf.Rad2Deg, 0);
            }
            for (int i = 0; i < 3; i++)
            {
                float a = (90f + i * 120f) * Mathf.Deg2Rad;
                var sp = MeshUtil.Box(wheelPivot, new Vector3(Mathf.Cos(a) * 0.1f, -0.01f, Mathf.Sin(a) * 0.1f), new Vector3(0.2f, 0.015f, 0.03f), dark, "Spoke");
                sp.transform.localEulerAngles = new Vector3(0, -a * Mathf.Rad2Deg, 0);
            }
            MeshUtil.CylY(wheelPivot, new Vector3(0, 0.01f, 0), 0.08f, 0.04f, red, "Hub");
            vis.steeringWheel = wheelPivot;

            // pedals (rotate around X at their pivot)
            vis.clutchPedal = Pedal(bodyRoot, new Vector3(-0.28f, 0.95f, -0.12f), dark);
            vis.brakePedal = Pedal(bodyRoot, new Vector3(0.24f, 0.95f, -0.12f), dark);
            vis.accelPedal = Pedal(bodyRoot, new Vector3(0.36f, 0.92f, -0.2f), steel);
            // gear levers
            vis.gearLever = Lever(bodyRoot, new Vector3(0.14f, 0.9f, -0.42f), 0.55f, black, MatLib.Opaque(new Color(0.85f, 0.1f, 0.05f), 0.6f, 0f));
            vis.rangeLever = Lever(bodyRoot, new Vector3(-0.14f, 0.9f, -0.45f), 0.45f, black, MatLib.Opaque(new Color(0.95f, 0.75f, 0.1f), 0.6f, 0f));
            vis.handThrottleLever = Lever(bodyRoot, new Vector3(0.24f, 1.38f, -0.18f), 0.16f, black, black);
            vis.handThrottleLever.localEulerAngles = new Vector3(0, 0, -70f);

            // ---------------- lights ----------------
            vis.headLens = new[] {
                Lens(bodyRoot, new Vector3(0.28f, 1.17f, 1.745f), 0.15f, Color.white),
                Lens(bodyRoot, new Vector3(-0.28f, 1.17f, 1.745f), 0.15f, Color.white) };
            vis.tailLens = new[] {
                LensBox(bodyRoot, new Vector3(xr + 0.12f, 1.3f, zr - 0.62f), Color.red),
                LensBox(bodyRoot, new Vector3(-xr - 0.12f, 1.3f, zr - 0.62f), Color.red) };
            vis.indicatorLensL = new[] {
                LensBox(bodyRoot, new Vector3(-0.33f, 1.32f, 1.6f), new Color(1f, 0.55f, 0f)),
                LensBox(bodyRoot, new Vector3(-xr - 0.12f, 1.42f, zr - 0.62f), new Color(1f, 0.55f, 0f)) };
            vis.indicatorLensR = new[] {
                LensBox(bodyRoot, new Vector3(0.33f, 1.32f, 1.6f), new Color(1f, 0.55f, 0f)),
                LensBox(bodyRoot, new Vector3(xr + 0.12f, 1.42f, zr - 0.62f), new Color(1f, 0.55f, 0f)) };
            vis.headlights = new[] {
                Spot(bodyRoot, new Vector3(0.28f, 1.17f, 1.8f)),
                Spot(bodyRoot, new Vector3(-0.28f, 1.17f, 1.8f)) };

            // ---------------- wheels ----------------
            vis.wheelRoots = new Transform[4];
            vis.wheelSpin = new Transform[4];
            for (int i = 0; i < 4; i++)
            {
                var w = wheels[i];
                var wr = new GameObject("Wheel_" + w.name).transform;
                wr.SetParent(root, false);
                wr.localPosition = w.anchorLocal - Vector3.up * w.travel * 0.5f;
                var spin = new GameObject("Spin").transform;
                spin.SetParent(wr, false);
                BuildWheel(spin, w, w.front ? (realSpec.fourWheelDrive ? 20 : 18) : 22, rimMat, black, chrome, w.left, realSpec.fourWheelDrive && w.front);
                MeshUtil.CombineByMaterial(spin.gameObject);
                vis.wheelRoots[i] = wr;
                vis.wheelSpin[i] = spin;
            }

            // ---------------- effects ----------------
            vis.exhaust = Effects.Smoke(bodyRoot, new Vector3(0.21f, 2.27f, 1.32f));
            vis.rearDust = new[] { Effects.Dust(root), Effects.Dust(root) };

            // ---------------- camera anchors ----------------
            vis.cockpitEye = Anchor(bodyRoot, "CockpitEye", new Vector3(0, 2.02f, -0.8f));
            float rs = realSpec.bodyScale;
            vis.hitchCam = Anchor(root, "HitchCam", new Vector3(0, 1.7f * rs, -realSpec.wheelBase * 0.5f - 0.2f * rs));
            vis.frontCam = Anchor(root, "FrontCam", new Vector3(0, 1.2f * rs, realSpec.wheelBase * 0.5f + 2.6f * rs));
        }

        // ---------------- canopy (sun roof, very common on Indian tractors) ----------------
        static void BuildCanopy(Transform body, TractorSpec unit, Material roofMat, Material postMat, TractorSpec real)
        {
            float zr = -unit.wheelBase * 0.5f;
            var white = MatLib.Opaque(new Color(0.94f, 0.94f, 0.92f), 0.45f, 0.05f);
            Vector3[] posts = { new Vector3(-0.62f, 0, zr - 0.38f), new Vector3(0.62f, 0, zr - 0.38f), new Vector3(-0.55f, 0, zr + 0.62f), new Vector3(0.55f, 0, zr + 0.62f) };
            foreach (var p in posts)
                MeshUtil.Box(body, new Vector3(p.x, 1.92f, p.z), new Vector3(0.05f, 1.05f, 0.05f), postMat, "CanopyPost");
            MeshUtil.Box(body, new Vector3(0, 2.47f, zr + 0.12f), new Vector3(1.45f, 0.06f, 1.55f), white, "CanopyRoof");
            MeshUtil.Box(body, new Vector3(0, 2.43f, zr + 0.12f), new Vector3(1.5f, 0.1f, 0.06f), white, "CanopyRib");
            for (int s = -1; s <= 1; s += 2)
                MeshUtil.Box(body, new Vector3(s * 0.74f, 2.4f, zr + 0.12f), new Vector3(0.04f, 0.14f, 1.55f), white, "CanopyLip");
        }

        // ---------------- cab (flagship) ----------------
        static void BuildCabin(Transform body, TractorSpec unit, Material paint, Material accent, Material frame, TractorSpec real)
        {
            float zr = -unit.wheelBase * 0.5f;
            var glass = MatLib.Transparent(new Color(0.55f, 0.65f, 0.7f, 0.28f), 0.95f);
            float z0 = zr - 0.42f, z1 = zr + 0.98f, y0 = 1.3f, y1 = 2.58f, hw = 0.66f;
            float zc = (z0 + z1) * 0.5f, yc = (y0 + y1) * 0.5f;
            // pillars
            foreach (var x in new[] { -hw, hw })
                foreach (var z in new[] { z0, z1, zc + 0.15f })
                    MeshUtil.Box(body, new Vector3(x, yc, z), new Vector3(0.06f, y1 - y0, 0.06f), frame, "CabPillar");
            // glass panels
            MeshUtil.Box(body, new Vector3(0, yc, z1), new Vector3(hw * 2f, y1 - y0, 0.02f), glass, "Windscreen");
            MeshUtil.Box(body, new Vector3(0, yc, z0), new Vector3(hw * 2f, y1 - y0, 0.02f), glass, "RearGlass");
            MeshUtil.Box(body, new Vector3(-hw, yc, zc), new Vector3(0.02f, y1 - y0, z1 - z0), glass, "DoorL");
            MeshUtil.Box(body, new Vector3(hw, yc, zc), new Vector3(0.02f, y1 - y0, z1 - z0), glass, "DoorR");
            // roof, beacon, work lights
            MeshUtil.Box(body, new Vector3(0, y1 + 0.06f, zc), new Vector3(hw * 2f + 0.12f, 0.12f, z1 - z0 + 0.18f), paint, "CabRoof");
            MeshUtil.Box(body, new Vector3(0, y1 + 0.13f, zc), new Vector3(hw * 2f - 0.1f, 0.03f, z1 - z0 - 0.1f), accent, "CabRoofStripe");
            MeshUtil.CylY(body, new Vector3(-0.45f, y1 + 0.2f, z0 + 0.15f), 0.12f, 0.12f, MatLib.Emissive(new Color(1f, 0.6f, 0.05f), new Color(0.8f, 0.4f, 0f)), "Beacon");
            for (int s = -1; s <= 1; s += 2)
                MeshUtil.Box(body, new Vector3(s * 0.4f, y1 + 0.02f, z1 + 0.12f), new Vector3(0.16f, 0.08f, 0.06f), MatLib.Emissive(Color.white, new Color(0.5f, 0.5f, 0.45f)), "WorkLight");
            // wiper
            MeshUtil.Box(body, new Vector3(0.1f, y0 + 0.35f, z1 + 0.02f), new Vector3(0.02f, 0.6f, 0.02f), frame, "Wiper", new Vector3(0, 0, 35f));
        }

        // ---------------- seated driver ----------------
        static Transform BuildDriver(Transform body)
        {
            var d = new GameObject("Driver").transform; d.SetParent(body, false);
            var skin = MatLib.Opaque(new Color(0.55f, 0.37f, 0.25f), 0.25f, 0f);
            var shirt = MatLib.Opaque(new Color(0.15f, 0.55f, 0.2f), 0.2f, 0f);
            var pants = MatLib.Opaque(new Color(0.82f, 0.82f, 0.8f), 0.2f, 0f);
            var hair = MatLib.Opaque(new Color(0.08f, 0.06f, 0.05f), 0.3f, 0f);
            var shoes = MatLib.Opaque(new Color(0.12f, 0.09f, 0.07f), 0.3f, 0f);
            // torso & head
            MeshUtil.Part(d, PrimitiveType.Capsule, new Vector3(0, 1.58f, -0.86f), new Vector3(0.36f, 0.3f, 0.24f), new Vector3(-6, 0, 0), shirt, "Torso");
            MeshUtil.Part(d, PrimitiveType.Sphere, new Vector3(0, 2.0f, -0.83f), new Vector3(0.2f, 0.24f, 0.22f), Vector3.zero, skin, "Head");
            MeshUtil.Part(d, PrimitiveType.Sphere, new Vector3(0, 2.06f, -0.85f), new Vector3(0.21f, 0.16f, 0.22f), Vector3.zero, hair, "Hair");
            MeshUtil.Part(d, PrimitiveType.Cylinder, new Vector3(0, 1.86f, -0.85f), new Vector3(0.09f, 0.05f, 0.09f), Vector3.zero, skin, "Neck");
            // arms reaching to the steering wheel
            for (int s = -1; s <= 1; s += 2)
            {
                MeshUtil.Part(d, PrimitiveType.Capsule, new Vector3(s * 0.21f, 1.62f, -0.72f), new Vector3(0.09f, 0.16f, 0.09f), new Vector3(55f, 0, s * -10f), shirt, "UpperArm");
                MeshUtil.Part(d, PrimitiveType.Capsule, new Vector3(s * 0.17f, 1.58f, -0.47f), new Vector3(0.075f, 0.15f, 0.075f), new Vector3(100f, 0, s * -25f), skin, "Forearm");
                // thighs forward, shins down to the pedals
                MeshUtil.Part(d, PrimitiveType.Capsule, new Vector3(s * 0.11f, 1.3f, -0.62f), new Vector3(0.14f, 0.22f, 0.14f), new Vector3(90f, 0, 0), pants, "Thigh");
                MeshUtil.Part(d, PrimitiveType.Capsule, new Vector3(s * 0.12f, 1.05f, -0.4f), new Vector3(0.11f, 0.2f, 0.11f), new Vector3(-15f, 0, 0), pants, "Shin");
                MeshUtil.Part(d, PrimitiveType.Cube, new Vector3(s * 0.12f, 0.86f, -0.33f), new Vector3(0.1f, 0.07f, 0.24f), Vector3.zero, shoes, "Shoe");
            }
            MeshUtil.CombineByMaterial(d.gameObject);
            return d;
        }

        // ---------------- real 3D art from the ArtCatalog (optional) ----------------
        static void ApplyArt(Transform root, TractorSpec spec, TractorVisuals vis)
        {
            var cat = ArtCatalog.Load();
            var art = cat != null ? cat.FindTractor(spec.id) : null;
            if (art == null || art.model == null) return;
            var model = Object.Instantiate(art.model);
            model.name = "ArtModel";
            model.transform.SetParent(vis.bodyRoot.parent, false);   // under Visual, not scaled
            model.transform.localPosition = art.offset;
            model.transform.localEulerAngles = art.euler;
            model.transform.localScale = Vector3.one * (art.scale <= 0f ? 1f : art.scale);
            foreach (var c in model.GetComponentsInChildren<Collider>()) Object.Destroy(c);
            foreach (var r in model.GetComponentsInChildren<Rigidbody>()) Object.Destroy(r);
            // hide the procedural body (keep moving cab controls hidden too – the model has its own)
            var stat = vis.bodyRoot.Find("Static"); if (stat != null) stat.gameObject.SetActive(false);
            if (art.hideBrandDecals) { var b = vis.bodyRoot.Find("Brand"); if (b != null) b.gameObject.SetActive(false); }
            var wheelNames = new[] { art.wheelFL, art.wheelFR, art.wheelRL, art.wheelRR };
            bool any = false;
            vis.artWheels = new Transform[4];
            for (int i = 0; i < 4; i++)
            {
                if (string.IsNullOrEmpty(wheelNames[i])) continue;
                vis.artWheels[i] = FindDeep(model.transform, wheelNames[i]);
                if (vis.artWheels[i] != null) any = true;
            }
            if (any && vis.wheelRoots != null) foreach (var w in vis.wheelRoots) if (w != null) w.gameObject.SetActive(false);
        }

        // ---------------- the imported FBX tractor ----------------
        static void ApplyRig(Transform root, TractorSpec spec, TractorVisuals vis, TractorRigInfo rig, GameObject prefab)
        {
            float k = spec.bodyScale;
            var model = Object.Instantiate(prefab);
            model.name = "RigModel";
            model.transform.SetParent(vis.bodyRoot.parent, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one * k;
            foreach (var c in model.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);

            // hide the procedural body but keep the driver, lights and particle effects working
            var keep = vis.driver;
            foreach (var r in vis.bodyRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;
                if (keep != null && r.transform.IsChildOf(keep)) continue;
                r.enabled = false;
            }
            if (vis.wheelRoots != null) foreach (var w in vis.wheelRoots) if (w != null) w.gameObject.SetActive(false);

            // untextured import: paint the body in the brand colour so each company still looks different
            if (!rig.textured)
            {
                var body = model.transform.Find("Body");
                if (body != null)
                {
                    var paint = MatLib.Opaque(C(spec.bodyRgb), 0.6f, 0.15f);
                    foreach (var r in body.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = paint;
                }
            }

            // wheels from the model, driven by the suspension
            vis.artWheels = new Transform[4];
            var names = new[] { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" };
            for (int i = 0; i < 4; i++) vis.artWheels[i] = model.transform.Find(names[i]);
            vis.artWheelsFollow = true;

            // driver on the model's seat, cockpit camera at his eyes, smoke from the model's exhaust
            Vector3 seat = rig.seat * k;
            if (vis.driver != null)
            {
                Vector3 procSeat = vis.bodyRoot.TransformPoint(new Vector3(0f, 1.22f, -0.82f));
                vis.driver.position += root.TransformPoint(seat) - procSeat;
            }
            if (vis.cockpitEye != null) vis.cockpitEye.position = root.TransformPoint(seat + new Vector3(0f, 0.8f, 0.02f) * k);
            if (vis.exhaust != null) vis.exhaust.transform.position = root.TransformPoint(rig.exhaustTop * k);

            // company name on both bonnet sides, highlighted
            var brand = new GameObject("RigBrand").transform; brand.SetParent(model.transform, false);
            Color ink = rig.textured ? new Color(1f, 0.85f, 0.1f) : Color.Lerp(C(spec.accentRgb), Color.white, 0.3f);
            for (int sgn = -1; sgn <= 1; sgn += 2)
            {
                AddDecal(brand, spec.brand, new Vector3(sgn * rig.hoodSide.x, rig.hoodSide.y, rig.hoodSide.z), new Vector3(0, -90 * sgn, 0), 0.0105f / k * 1.6f, ink, spec.brand.Length > 12 ? 0.85f : 1f);
                AddDecal(brand, spec.model, new Vector3(sgn * rig.hoodSide.x, rig.hoodSide.y - 0.17f, rig.hoodSide.z + 0.2f), new Vector3(0, -90 * sgn, 0), 0.008f, Color.white, 1f);
            }
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t) { var r = FindDeep(c, name); if (r != null) return r; }
            return null;
        }

        static Transform Anchor(Transform parent, string name, Vector3 p)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = p;
            return t;
        }

        static void BuildWheel(Transform spin, WheelState w, int lugs, Material rim, Material rubber, Material chrome, bool left, bool lugged = false)
        {
            float d = w.radius * 2f;
            float side = left ? -1f : 1f;
            // tyre carcass
            var tyre = MeshUtil.CylX(spin, Vector3.zero, d * 0.96f, w.width, MatLib.Opaque(new Color(0.08f, 0.08f, 0.08f), 0.12f, 0f, ProcTex.Tread(), new Vector2(1f, 6f)), "Tyre");
            // sidewall bulge
            MeshUtil.CylX(spin, Vector3.zero, d * 0.9f, w.width * 1.08f, rubber, "Sidewall");
            // rim & disc
            MeshUtil.CylX(spin, new Vector3(side * 0.005f, 0, 0), d * 0.62f, w.width * 1.1f, rim, "Rim");
            MeshUtil.CylX(spin, new Vector3(side * w.width * 0.2f, 0, 0), d * 0.22f, w.width * 0.9f, rim, "Hub");
            MeshUtil.CylX(spin, new Vector3(side * w.width * 0.5f, 0, 0), d * 0.12f, 0.05f, chrome, "HubCap");
            for (int i = 0; i < 6; i++)
            {
                float a = i / 6f * Mathf.PI * 2f;
                MeshUtil.CylX(spin, new Vector3(side * w.width * 0.56f, Mathf.Cos(a) * d * 0.09f, Mathf.Sin(a) * d * 0.09f), 0.025f, 0.03f, chrome, "Nut");
            }
            // traction lugs (chevrons on rear, ribs on front)
            for (int i = 0; i < lugs; i++)
            {
                float a = i / (float)lugs * 360f;
                float rad = a * Mathf.Deg2Rad;
                float rr = w.radius * 0.975f;
                var p = new Vector3(0, Mathf.Cos(rad) * rr, Mathf.Sin(rad) * rr);
                if (w.driven || lugged)
                {
                    for (int s = -1; s <= 1; s += 2)
                    {
                        var lug = MeshUtil.Box(spin, p + new Vector3(s * w.width * 0.25f, 0, 0), new Vector3(w.width * 0.5f, 0.05f, 0.06f), rubber, "Lug");
                        lug.transform.localRotation = Quaternion.Euler(a, 0, 0) * Quaternion.Euler(0, s * 25f, 0);
                    }
                }
                else
                {
                    var rib = MeshUtil.Box(spin, p, new Vector3(w.width * 0.25f, 0.02f, 0.05f), rubber, "Rib");
                    rib.transform.localRotation = Quaternion.Euler(a, 0, 0);
                }
            }
        }

        static Transform Pedal(Transform parent, Vector3 pivot, Material mat)
        {
            var p = new GameObject("Pedal").transform;
            p.SetParent(parent, false);
            p.localPosition = pivot;
            MeshUtil.Box(p, new Vector3(0, -0.07f, 0.1f), new Vector3(0.025f, 0.025f, 0.22f), mat, "Arm", new Vector3(30f, 0, 0));
            MeshUtil.Box(p, new Vector3(0, -0.14f, 0.2f), new Vector3(0.09f, 0.02f, 0.06f), mat, "Pad", new Vector3(60f, 0, 0));
            return p;
        }

        static Transform Lever(Transform parent, Vector3 pivot, float len, Material rod, Material knob)
        {
            var p = new GameObject("Lever").transform;
            p.SetParent(parent, false);
            p.localPosition = pivot;
            MeshUtil.CylY(p, new Vector3(0, len * 0.5f, 0), 0.025f, len, rod, "Rod");
            MeshUtil.Part(p, PrimitiveType.Sphere, new Vector3(0, len, 0), Vector3.one * 0.06f, Vector3.zero, knob, "Knob");
            return p;
        }

        static Renderer Lens(Transform parent, Vector3 pos, float d, Color c)
        {
            var go = MeshUtil.CylZ(parent, pos, d, 0.015f, MatLib.Emissive(c * 0.8f, Color.black), "Lens");
            return go.GetComponent<Renderer>();
        }

        static Renderer LensBox(Transform parent, Vector3 pos, Color c)
        {
            var go = MeshUtil.Box(parent, pos, new Vector3(0.09f, 0.06f, 0.04f), MatLib.Emissive(c * 0.6f, Color.black), "Lamp");
            return go.GetComponent<Renderer>();
        }

        static Light Spot(Transform parent, Vector3 pos)
        {
            var go = new GameObject("Headlight");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localEulerAngles = new Vector3(8f, 0, 0);
            var l = go.AddComponent<Light>();
            l.type = LightType.Spot;
            l.range = 40f;
            l.spotAngle = 65f;
            l.intensity = 2.6f;
            l.color = new Color(1f, 0.95f, 0.82f);
            l.shadows = LightShadows.None;
            l.enabled = false;
            return l;
        }

        static void AddDecal(Transform parent, string text, Vector3 pos, Vector3 euler, float size, Color color, float squeeze)
        {
            var go = new GameObject("Decal");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localEulerAngles = euler;
            go.transform.localScale = new Vector3(size * squeeze, size, size);
            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.font = UIFactory.DefaultFont;
            tm.fontSize = 64;
            tm.fontStyle = FontStyle.Bold;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.color = color;
            go.GetComponent<MeshRenderer>().sharedMaterial = UIFactory.DefaultFont.material;
        }
    }
}
