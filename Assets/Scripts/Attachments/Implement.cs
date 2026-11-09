using System.Collections.Generic;
using UnityEngine;

namespace TractorSim
{
    public enum HitchType { Drawbar, ThreePoint }
    public enum WorkKind { None, Cultivate, Plough, Rotavate, Harrow, Seed, Spray, Level }

    /// <summary>
    /// Anything that hooks onto the tractor. Lives in the world as its own rigidbody when parked;
    /// trolleys stay jointed rigidbodies when hitched, 3-point implements are carried by the linkage.
    /// </summary>
    public class Implement : MonoBehaviour
    {
        public static readonly List<Implement> All = new List<Implement>();

        public string id;
        public string displayName;
        public HitchType hitchType;
        public float massKg = 300f;
        public float workWidth = 2f;
        public WorkKind work = WorkKind.None;
        public float draftNPerMetre = 2500f;     // soil resistance per metre of working width
        public float ptoPowerKw;                 // power taken from the engine when PTO is on
        public float minHp;                      // smallest tractor that can sensibly run it
        public Transform hitchPoint;             // where it connects (front centre)
        public Transform workPoint;              // where it touches the soil (rear edge centre)
        public Transform rotor;                  // spinning parts (rotavator rotor, discs)
        public float rotorSpeed;                 // deg/s at full PTO / ground speed
        public bool rotorFromGround;             // discs roll with ground speed instead of PTO
        public ParticleSystem workFx;            // soil spray / liquid spray

        public Rigidbody Body { get; private set; }
        public bool Attached { get; set; }
        public bool Lowered { get; set; }
        public bool PtoOn { get; set; }
        public float LiftAmount { get; set; }    // 0 = on the ground, 1 = fully raised

        protected virtual void Awake()
        {
            Body = GetComponent<Rigidbody>();
            All.Add(this);
        }

        protected virtual void OnDestroy() { All.Remove(this); }

        public virtual float TotalMass { get { return massKg; } }

        /// <summary>Called every physics step while attached; speed in m/s along the tractor.</summary>
        public virtual void WorkTick(float dt, float groundSpeed, bool inSoil)
        {
            float spin = 0f;
            if (rotor != null)
            {
                if (rotorFromGround) spin = inSoil ? groundSpeed * rotorSpeed : 0f;
                else spin = PtoOn ? rotorSpeed : 0f;
                rotor.Rotate(Vector3.right, spin * dt, Space.Self);
            }
            if (workFx != null)
            {
                var em = workFx.emission;
                bool on = work == WorkKind.Spray ? (PtoOn && Mathf.Abs(groundSpeed) > 0.2f) : (inSoil && Mathf.Abs(groundSpeed) > 0.3f);
                em.rateOverTime = on ? (work == WorkKind.Spray ? 160f : 25f + Mathf.Abs(groundSpeed) * 20f) : 0f;
            }
        }
    }

    /// <summary>Builds every implement procedurally (or from the ArtCatalog when a real model is assigned).</summary>
    public static class ImplementFactory
    {
        public static readonly string[] Ids = { "trolley_single", "trolley_tandem", "rotavator", "cultivator", "plough", "harrow", "seeddrill", "sprayer", "leveller" };

        public static Implement Create(string id, Vector3 pos, Quaternion rot)
        {
            switch (id)
            {
                case "trolley_single": return Trolley.Build(id, "Single-axle trolley (5 t)", pos, rot, false);
                case "trolley_tandem": return Trolley.Build(id, "Double-axle trolley (10 t)", pos, rot, true);
                case "rotavator": return Rotavator(pos, rot);
                case "cultivator": return Cultivator(pos, rot);
                case "plough": return Plough(pos, rot);
                case "harrow": return Harrow(pos, rot);
                case "seeddrill": return SeedDrill(pos, rot);
                case "sprayer": return Sprayer(pos, rot);
                case "leveller": return Leveller(pos, rot);
            }
            return null;
        }

        static readonly Color Orange = new Color(0.95f, 0.55f, 0.08f);
        static readonly Color Yellow = new Color(0.95f, 0.78f, 0.1f);
        static readonly Color BlueI = new Color(0.12f, 0.32f, 0.7f);
        static readonly Color RedI = new Color(0.75f, 0.1f, 0.08f);
        static readonly Color GreenI = new Color(0.18f, 0.5f, 0.2f);

        /// <summary>Common skeleton: rigidbody, 3-point A-frame, hitch & work points.</summary>
        static Implement Base(string id, string name, Vector3 pos, Quaternion rot, float mass, float width, float depth, Color paint, out Transform vis)
        {
            var go = new GameObject("Implement_" + id);
            go.transform.SetPositionAndRotation(pos, rot);
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.linearDamping = 0.1f; rb.angularDamping = 0.5f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            var imp = go.AddComponent<Implement>();
            imp.id = id; imp.displayName = name; imp.hitchType = HitchType.ThreePoint; imp.massKg = mass; imp.workWidth = width;
            vis = new GameObject("Visual").transform; vis.SetParent(go.transform, false);
            var steel = MatLib.Opaque(paint, 0.45f, 0.35f);
            var dark = MatLib.Opaque(new Color(0.15f, 0.15f, 0.16f), 0.35f, 0.5f);
            // A-frame mast & lower link pins at the front (z = 0 is the hitch)
            MeshUtil.Box(vis, new Vector3(0, 0.55f, 0.08f), new Vector3(0.12f, 0.7f, 0.1f), steel, "Mast", new Vector3(-12f, 0, 0));
            MeshUtil.Box(vis, new Vector3(0, 0.42f, -0.05f), new Vector3(Mathf.Min(width, 1.0f), 0.1f, 0.1f), steel, "CrossBar");
            for (int s = -1; s <= 1; s += 2) MeshUtil.CylX(vis, new Vector3(s * Mathf.Min(width, 1.0f) * 0.5f, 0.42f, 0.02f), 0.05f, 0.12f, dark, "Pin");
            imp.hitchPoint = new GameObject("Hitch").transform; imp.hitchPoint.SetParent(go.transform, false); imp.hitchPoint.localPosition = new Vector3(0, 0.42f, 0.05f);
            imp.workPoint = new GameObject("Work").transform; imp.workPoint.SetParent(go.transform, false); imp.workPoint.localPosition = new Vector3(0, 0f, -depth);
            // a simple box collider plus a parking stand
            var col = new GameObject("Col"); col.transform.SetParent(go.transform, false);
            col.layer = TractorBuilder.TractorLayer;
            var bc = col.AddComponent<BoxCollider>(); bc.center = new Vector3(0, 0.45f, -depth * 0.5f); bc.size = new Vector3(width, 0.7f, depth);
            return imp;
        }

        static void ApplyArt(Implement imp, Transform vis)
        {
            var cat = ArtCatalog.Load();
            var art = cat != null ? cat.FindImplement(imp.id) : null;
            if (art == null || art.model == null) return;
            var m = Object.Instantiate(art.model, imp.transform);
            m.transform.localPosition = art.offset; m.transform.localEulerAngles = art.euler; m.transform.localScale = Vector3.one * (art.scale <= 0 ? 1 : art.scale);
            foreach (var c in m.GetComponentsInChildren<Collider>()) Object.Destroy(c);
            vis.gameObject.SetActive(false);
            imp.rotor = null;
        }

        static Implement Rotavator(Vector3 pos, Quaternion rot)
        {
            Transform v;
            var imp = Base("rotavator", "Rotavator 6 ft", pos, rot, 420f, 1.85f, 0.95f, Orange, out v);
            imp.work = WorkKind.Rotavate; imp.draftNPerMetre = 900f; imp.ptoPowerKw = 22f; imp.minHp = 35f;
            var paint = MatLib.Opaque(Orange, 0.5f, 0.3f);
            var dark = MatLib.Opaque(new Color(0.12f, 0.12f, 0.12f), 0.4f, 0.6f);
            var blade = MatLib.Opaque(new Color(0.55f, 0.55f, 0.55f), 0.7f, 0.9f);
            // hood (curved from segments)
            for (int i = 0; i < 7; i++)
            {
                float a = Mathf.Lerp(-80f, 80f, i / 6f) * Mathf.Deg2Rad;
                MeshUtil.Box(v, new Vector3(0, 0.35f + Mathf.Cos(a) * 0.32f, -0.5f + Mathf.Sin(a) * 0.32f), new Vector3(1.85f, 0.03f, 0.1f), paint, "Hood", new Vector3(a * Mathf.Rad2Deg, 0, 0));
            }
            for (int s = -1; s <= 1; s += 2) MeshUtil.Box(v, new Vector3(s * 0.93f, 0.32f, -0.5f), new Vector3(0.03f, 0.62f, 0.7f), paint, "SidePlate");
            MeshUtil.Box(v, new Vector3(0, 0.74f, -0.45f), new Vector3(0.3f, 0.22f, 0.3f), paint, "Gearbox");
            MeshUtil.CylZ(v, new Vector3(0, 0.62f, -0.05f), 0.07f, 0.4f, dark, "PTOShaft");
            MeshUtil.Box(v, new Vector3(0, 0.05f, -0.92f), new Vector3(1.85f, 0.03f, 0.25f), paint, "LevellingBoard", new Vector3(-20f, 0, 0));
            // rotor with L-blades
            var rotor = new GameObject("Rotor").transform; rotor.SetParent(v, false); rotor.localPosition = new Vector3(0, 0.25f, -0.5f);
            MeshUtil.CylX(rotor, Vector3.zero, 0.09f, 1.8f, dark, "Shaft");
            for (int f = 0; f < 7; f++)
                for (int k = 0; k < 6; k++)
                {
                    float a = (k * 60f + f * 25f) * Mathf.Deg2Rad;
                    float x = -0.78f + f * 0.26f;
                    MeshUtil.Box(rotor, new Vector3(x, Mathf.Cos(a) * 0.17f, Mathf.Sin(a) * 0.17f), new Vector3(0.02f, 0.2f, 0.06f), blade, "Blade", new Vector3(a * Mathf.Rad2Deg, 0, k % 2 == 0 ? 20f : -20f));
                }
            MeshUtil.CombineByMaterial(rotor.gameObject);
            imp.rotor = rotor; imp.rotorSpeed = 1260f;   // ~210 rpm
            imp.workFx = SoilFx(imp.transform, new Vector3(0, 0.1f, -0.9f), 1.8f);
            Finish(imp, v);
            return imp;
        }

        static Implement Cultivator(Vector3 pos, Quaternion rot)
        {
            Transform v;
            var imp = Base("cultivator", "Cultivator 9-tine", pos, rot, 280f, 2.1f, 0.9f, BlueI, out v);
            imp.work = WorkKind.Cultivate; imp.draftNPerMetre = 2600f; imp.minHp = 30f;
            var paint = MatLib.Opaque(BlueI, 0.5f, 0.3f);
            var tine = MatLib.Opaque(new Color(0.35f, 0.35f, 0.35f), 0.6f, 0.8f);
            MeshUtil.Box(v, new Vector3(0, 0.45f, -0.3f), new Vector3(2.1f, 0.1f, 0.1f), paint, "FrontBeam");
            MeshUtil.Box(v, new Vector3(0, 0.45f, -0.75f), new Vector3(2.1f, 0.1f, 0.1f), paint, "RearBeam");
            for (int s = -1; s <= 1; s += 2) MeshUtil.Box(v, new Vector3(s * 0.6f, 0.45f, -0.5f), new Vector3(0.08f, 0.08f, 0.6f), paint, "Strut");
            for (int i = 0; i < 9; i++)
            {
                float x = -0.92f + i * 0.23f;
                float z = i % 2 == 0 ? -0.3f : -0.75f;
                MeshUtil.Box(v, new Vector3(x, 0.25f, z - 0.05f), new Vector3(0.04f, 0.42f, 0.04f), tine, "Tine", new Vector3(-15f, 0, 0));
                MeshUtil.Box(v, new Vector3(x, 0.03f, z + 0.03f), new Vector3(0.05f, 0.03f, 0.14f), tine, "Shovel", new Vector3(25f, 0, 0));
            }
            imp.workFx = SoilFx(imp.transform, new Vector3(0, 0.05f, -0.85f), 2f);
            Finish(imp, v);
            return imp;
        }

        static Implement Plough(Vector3 pos, Quaternion rot)
        {
            Transform v;
            var imp = Base("plough", "MB Plough 2-bottom", pos, rot, 360f, 0.9f, 1.6f, RedI, out v);
            imp.work = WorkKind.Plough; imp.draftNPerMetre = 9000f; imp.minHp = 35f;
            var paint = MatLib.Opaque(RedI, 0.5f, 0.3f);
            var board = MatLib.Opaque(new Color(0.6f, 0.6f, 0.6f), 0.85f, 0.9f);
            MeshUtil.Box(v, new Vector3(0.15f, 0.5f, -0.75f), new Vector3(0.1f, 0.1f, 1.6f), paint, "Beam", new Vector3(0, -12f, 0));
            for (int b = 0; b < 2; b++)
            {
                float z = -0.55f - b * 0.75f; float x = 0.05f + b * 0.3f;
                MeshUtil.Box(v, new Vector3(x, 0.3f, z), new Vector3(0.06f, 0.45f, 0.06f), paint, "Leg");
                var mb = MeshUtil.Box(v, new Vector3(x + 0.12f, 0.1f, z - 0.1f), new Vector3(0.04f, 0.3f, 0.5f), board, "Mouldboard", new Vector3(10f, 35f, -25f));
                MeshUtil.Box(v, new Vector3(x, 0.0f, z + 0.15f), new Vector3(0.05f, 0.04f, 0.35f), board, "Share", new Vector3(0, 30f, 0));
            }
            MeshUtil.CylX(v, new Vector3(-0.3f, 0.2f, -1.5f), 0.4f, 0.1f, MatLib.Opaque(new Color(0.1f, 0.1f, 0.1f), 0.2f, 0f), "DepthWheel");
            imp.workFx = SoilFx(imp.transform, new Vector3(0.2f, 0.1f, -1.4f), 0.9f);
            Finish(imp, v);
            return imp;
        }

        static Implement Harrow(Vector3 pos, Quaternion rot)
        {
            Transform v;
            var imp = Base("harrow", "Disc Harrow 14-disc", pos, rot, 520f, 2.2f, 1.3f, GreenI, out v);
            imp.work = WorkKind.Harrow; imp.draftNPerMetre = 2200f; imp.minHp = 40f;
            var paint = MatLib.Opaque(GreenI, 0.5f, 0.3f);
            var disc = MatLib.Opaque(new Color(0.55f, 0.55f, 0.55f), 0.8f, 0.9f);
            MeshUtil.Box(v, new Vector3(0, 0.6f, -0.65f), new Vector3(2.2f, 0.1f, 1.0f), paint, "Frame");
            var rotor = new GameObject("Gangs").transform; rotor.SetParent(v, false); rotor.localPosition = new Vector3(0, 0.25f, -0.65f);
            for (int g = 0; g < 2; g++)
                for (int i = 0; i < 7; i++)
                {
                    float x = -0.95f + i * 0.31f;
                    var d = MeshUtil.CylX(rotor, new Vector3(x, 0, g == 0 ? 0.3f : -0.3f), 0.46f, 0.02f, disc, "Disc");
                    d.transform.localEulerAngles = new Vector3(0, g == 0 ? 15f : -15f, 90f);
                }
            MeshUtil.CombineByMaterial(rotor.gameObject);
            imp.rotor = rotor; imp.rotorSpeed = 250f; imp.rotorFromGround = true;
            imp.workFx = SoilFx(imp.transform, new Vector3(0, 0.05f, -1.2f), 2.1f);
            Finish(imp, v);
            return imp;
        }

        static Implement SeedDrill(Vector3 pos, Quaternion rot)
        {
            Transform v;
            var imp = Base("seeddrill", "Seed-cum-Fertiliser Drill 11-row", pos, rot, 380f, 2.0f, 1.0f, Yellow, out v);
            imp.work = WorkKind.Seed; imp.draftNPerMetre = 1500f; imp.minHp = 30f;
            var paint = MatLib.Opaque(Yellow, 0.5f, 0.3f);
            var dark = MatLib.Opaque(new Color(0.15f, 0.15f, 0.15f), 0.3f, 0.4f);
            MeshUtil.Box(v, new Vector3(0, 0.95f, -0.5f), new Vector3(2.0f, 0.45f, 0.55f), paint, "Hopper");
            MeshUtil.Box(v, new Vector3(0, 1.19f, -0.5f), new Vector3(2.05f, 0.04f, 0.6f), dark, "Lid");
            for (int i = 0; i < 11; i++)
            {
                float x = -0.9f + i * 0.18f;
                MeshUtil.Box(v, new Vector3(x, 0.45f, -0.6f), new Vector3(0.03f, 0.55f, 0.03f), dark, "SeedTube");
                MeshUtil.Box(v, new Vector3(x, 0.12f, -0.65f), new Vector3(0.04f, 0.2f, 0.08f), dark, "Tyne", new Vector3(-20f, 0, 0));
            }
            MeshUtil.CylX(v, new Vector3(-1.05f, 0.25f, -0.5f), 0.5f, 0.08f, dark, "DriveWheel");
            imp.workFx = SoilFx(imp.transform, new Vector3(0, 0.05f, -0.8f), 1.9f);
            Finish(imp, v);
            return imp;
        }

        static Implement Sprayer(Vector3 pos, Quaternion rot)
        {
            Transform v;
            var imp = Base("sprayer", "Boom Sprayer 400 L", pos, rot, 520f, 8f, 0.9f, Yellow, out v);
            imp.work = WorkKind.Spray; imp.draftNPerMetre = 0f; imp.ptoPowerKw = 3f; imp.minHp = 30f;
            var tank = MatLib.Opaque(new Color(0.95f, 0.95f, 0.92f), 0.5f, 0f);
            var paint = MatLib.Opaque(Yellow, 0.5f, 0.3f);
            MeshUtil.CylY(v, new Vector3(0, 0.9f, -0.45f), 0.85f, 0.75f, tank, "Tank");
            MeshUtil.Box(v, new Vector3(0, 0.6f, -0.85f), new Vector3(8f, 0.06f, 0.06f), paint, "Boom");
            for (int i = 0; i < 17; i++) MeshUtil.Box(v, new Vector3(-4f + i * 0.5f, 0.52f, -0.85f), new Vector3(0.03f, 0.1f, 0.03f), paint, "Nozzle");
            var fx = SoilFx(imp.transform, new Vector3(0, 0.45f, -0.85f), 8f);
            var main = fx.main; main.startColor = new Color(0.8f, 0.9f, 1f, 0.35f); main.gravityModifier = 0.6f; main.startSize = 0.18f; main.startLifetime = 0.6f;
            imp.workFx = fx;
            imp.workWidth = 8f;
            // the boom is wide: keep the body collider narrow so it doesn't snag
            imp.GetComponentInChildren<BoxCollider>().size = new Vector3(1.2f, 0.9f, 0.9f);
            Finish(imp, v);
            return imp;
        }

        static Implement Leveller(Vector3 pos, Quaternion rot)
        {
            Transform v;
            var imp = Base("leveller", "Land Leveller 8 ft", pos, rot, 450f, 2.45f, 0.8f, Orange, out v);
            imp.work = WorkKind.Level; imp.draftNPerMetre = 3200f; imp.minHp = 40f;
            var paint = MatLib.Opaque(Orange, 0.5f, 0.3f);
            var edge = MatLib.Opaque(new Color(0.5f, 0.5f, 0.5f), 0.8f, 0.9f);
            MeshUtil.Box(v, new Vector3(0, 0.3f, -0.7f), new Vector3(2.45f, 0.5f, 0.05f), paint, "Blade", new Vector3(-10f, 0, 0));
            MeshUtil.Box(v, new Vector3(0, 0.04f, -0.66f), new Vector3(2.45f, 0.06f, 0.08f), edge, "CuttingEdge");
            for (int s = -1; s <= 1; s += 2) MeshUtil.Box(v, new Vector3(s * 0.5f, 0.5f, -0.35f), new Vector3(0.08f, 0.08f, 0.7f), paint, "Arm", new Vector3(20f, 0, 0));
            imp.workFx = SoilFx(imp.transform, new Vector3(0, 0.1f, -0.8f), 2.3f);
            Finish(imp, v);
            return imp;
        }

        static void Finish(Implement imp, Transform v)
        {
            MeshUtil.CombineByMaterial(v.gameObject);
            ApplyArt(imp, v);
        }

        public static ParticleSystem SoilFx(Transform parent, Vector3 local, float width)
        {
            var go = new GameObject("WorkFx");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.startLifetime = 0.9f; main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
            main.startColor = new Color(0.32f, 0.22f, 0.13f, 0.9f);
            main.gravityModifier = 1f; main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 400;
            var em = ps.emission; em.rateOverTime = 0f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(width, 0.05f, 0.1f);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = MatLib.Particle(ProcTex.SoftCircle());
            ps.Play();
            return ps;
        }
    }
}
