using System.Collections.Generic;
using UnityEngine;

namespace TractorSim
{
    /// <summary>
    /// Hill traffic on the ghat road: cars, HRTC-style buses, goods trucks and auto-rickshaws driving on
    /// the LEFT (Indian rules) in both directions. Each vehicle follows its lane, slows for hairpins,
    /// keeps a gap to the vehicle in front – including the player's tractor – and overtakes a slow
    /// tractor on a straight when the oncoming lane is clear. Kinematic bodies: hitting one hurts.
    /// </summary>
    public class TrafficManager : MonoBehaviour
    {
        public const int DefaultDensity = 1;
        public static TrafficManager I { get; private set; }
        public int Density { get; private set; }

        enum Kind { Car, Bus, Truck, Auto }

        class Vehicle
        {
            public Transform t; public Rigidbody rb; public Kind kind;
            public float s, v, vmax, len, lane = -1f, laneTarget = -1f;
            public int dir;                // +1 = uphill (increasing s), -1 = downhill
            public float blocked, overtakeUntil;
        }

        List<Vector3> road; List<float> half;
        float[] cumS, curveV;
        float total;
        readonly List<Vehicle> cars = new List<Vehicle>();
        Transform tractor;
        int tractorIdx;

        public void Init(List<Vector3> roadPts, List<float> halfWidths, int density)
        {
            I = this;
            road = roadPts; half = halfWidths;
            int n = road.Count;
            cumS = new float[n]; curveV = new float[n];
            for (int i = 1; i < n; i++) cumS[i] = cumS[i - 1] + Vector3.Distance(road[i], road[i - 1]);
            total = cumS[n - 1];
            for (int i = 0; i < n; i++)
            {
                int a = Mathf.Max(0, i - 5), b = Mathf.Min(n - 1, i + 5);
                Vector3 d1 = road[i] - road[a]; d1.y = 0; Vector3 d2 = road[b] - road[i]; d2.y = 0;
                float k = (d1.sqrMagnitude < 0.01f || d2.sqrMagnitude < 0.01f) ? 0f : Vector3.Angle(d1, d2) * Mathf.Deg2Rad / Mathf.Max(1f, cumS[b] - cumS[a]);
                curveV[i] = k < 1e-4f ? 30f : Mathf.Clamp(Mathf.Sqrt(2.4f / k), 4.5f, 30f);    // lateral 2.4 m/s²
            }
            SetDensity(density);
        }

        public void SetDensity(int density)
        {
            Density = Mathf.Clamp(density, 0, 2);
            foreach (var c in cars) if (c.t != null) Destroy(c.t.gameObject);
            cars.Clear();
            if (road == null) return;
            int count = Density == 0 ? 0 : Density == 1 ? 8 : 22;
            var rnd = new System.Random(77 + count);
            for (int i = 0; i < count; i++)
            {
                var c = new Vehicle();
                c.dir = i % 2 == 0 ? 1 : -1;
                int r = rnd.Next(10);
                c.kind = r < 5 ? Kind.Car : r < 7 ? Kind.Truck : r < 9 ? Kind.Bus : Kind.Auto;
                c.len = c.kind == Kind.Bus ? 10.5f : c.kind == Kind.Truck ? 8f : c.kind == Kind.Auto ? 2.7f : 4.1f;
                c.vmax = (c.kind == Kind.Car ? 46f : c.kind == Kind.Bus ? 36f : c.kind == Kind.Truck ? 26f : 32f) / 3.6f * (0.9f + (float)rnd.NextDouble() * 0.2f);
                // spread along the road, away from the depot and summit yards
                c.s = Mathf.Lerp(120f, total - 120f, (i + (float)rnd.NextDouble() * 0.6f) / count);
                c.v = c.vmax * 0.5f;
                c.t = BuildVehicle(c.kind, rnd).transform;
                c.t.SetParent(transform, false);
                c.rb = c.t.GetComponent<Rigidbody>();
                cars.Add(c);
                Place(c, true);
            }
        }

        // ------------------------------------------------------------------ simulation
        void FixedUpdate()
        {
            if (cars.Count == 0) return;
            float dt = Time.fixedDeltaTime;
            if (tractor == null) { var tc = FindTractor(); if (tc != null) tractor = tc; }
            float tS = -1e9f, tLat = 0f, tV = 0f;
            if (tractor != null)
            {
                tractorIdx = NearestIndex(tractor.position, tractorIdx);
                Vector3 c = road[tractorIdx];
                Vector3 f = Fwd(tractorIdx), right = new Vector3(f.z, 0, -f.x);
                Vector3 d = tractor.position - c; d.y = 0f;
                if (d.magnitude < half[tractorIdx] + 6f)
                {
                    tS = cumS[tractorIdx] + Vector3.Dot(d, f);
                    tLat = Vector3.Dot(d, right);
                    var trb = tractor.GetComponent<Rigidbody>();
                    tV = trb != null ? Vector3.Dot(trb.linearVelocity, f) : 0f;
                }
            }

            foreach (var c in cars)
            {
                float target = c.vmax;
                // corner speed: look ahead 30 m
                for (float a = 0f; a <= 30f; a += 6f)
                {
                    int i = IndexAt(c.s + c.dir * a);
                    float cv = curveV[i] * (c.kind == Kind.Bus || c.kind == Kind.Truck ? 0.8f : 1f);
                    float allow = Mathf.Sqrt(cv * cv + 2f * 2.5f * a);      // can brake at 2.5 m/s² to reach the corner speed
                    target = Mathf.Min(target, allow);
                }
                // leader: other traffic in the same lane & direction
                float gap = 1e9f, leadV = 0f;
                foreach (var o in cars)
                {
                    if (o == c) continue;
                    float ds = (o.s - c.s) * c.dir;
                    if (ds <= 0f || ds > 70f) continue;
                    bool sameLane = o.dir == c.dir ? Mathf.Abs(o.lane - c.lane) < 1f : Mathf.Abs(LatOf(o) - LatOf(c)) < 2.2f;
                    if (!sameLane) continue;
                    float g = ds - (o.len + c.len) * 0.5f;
                    if (g < gap) { gap = g; leadV = o.dir == c.dir ? o.v : 0f; }
                }
                // the player's tractor (and its trolley) in our lane
                bool tractorBlocks = false;
                if (tS > -1e8f)
                {
                    float ds = (tS - c.s) * c.dir;
                    float rig = HasTrolley() ? 7.5f : 0f;           // a trolley may sit between us and the tractor
                    if (ds > -4f - rig && ds < 70f && Mathf.Abs(tLat - LatOf(c)) < 2.6f)
                    {
                        float g = ds - c.len * 0.5f - 2.5f - rig;
                        if (g < gap) { gap = g; leadV = Mathf.Max(0f, tV * c.dir); tractorBlocks = true; }
                    }
                }
                if (gap < 1e8f)
                {
                    float safe = 6f + c.v * 1.4f;
                    float follow = leadV + (gap - safe) * 0.6f;
                    target = Mathf.Min(target, Mathf.Max(0f, follow));
                    if (gap < 3f) target = 0f;
                }
                // overtake a crawling tractor on a straight when the other lane is clear
                if (tractorBlocks && c.v < 3f && leadV < 4f) c.blocked += dt; else c.blocked = Mathf.Max(0f, c.blocked - dt);
                if (c.blocked > 3.5f && c.lane < 0f && curveV[IndexAt(c.s)] > 20f && OncomingClear(c, 90f))
                {
                    c.laneTarget = 1f; c.overtakeUntil = tS + c.dir * 25f; c.blocked = 0f;
                }
                if (c.laneTarget > 0f && ((tS - c.overtakeUntil) * c.dir < 0f || tS < -1e8f)) c.laneTarget = -1f;
                if (c.laneTarget > 0f && !OncomingClear(c, 40f)) c.laneTarget = -1f;
                c.lane = Mathf.MoveTowards(c.lane, c.laneTarget, dt * 0.7f);

                float acc = target > c.v ? (c.kind == Kind.Car ? 1.6f : 0.9f) : -5.5f;
                c.v = Mathf.MoveTowards(c.v, target, Mathf.Abs(acc) * dt);
                c.s += c.dir * c.v * dt;
                if (c.s > total - 30f || c.s < 30f) Respawn(c);
                Place(c, false);
            }
        }

        bool OncomingClear(Vehicle c, float dist)
        {
            foreach (var o in cars)
            {
                if (o.dir == c.dir) continue;
                float ds = (o.s - c.s) * c.dir;
                if (ds > -5f && ds < dist) return false;
            }
            return true;
        }

        void Respawn(Vehicle c)
        {
            float start = c.dir > 0 ? 40f : total - 40f;
            foreach (var o in cars) if (o != c && Mathf.Abs(o.s - start) < 25f) { c.v = 0f; c.s = Mathf.Clamp(c.s, 30f, total - 30f); return; }
            if (tractor != null && Vector3.Distance(tractor.position, road[IndexAt(start)]) < 40f) { c.v = 0f; c.s = Mathf.Clamp(c.s, 30f, total - 30f); return; }
            c.s = start; c.v = c.vmax * 0.4f; c.lane = c.laneTarget = -1f;
            Place(c, true);
        }

        /// <summary>Lateral offset of a vehicle in the uphill frame (+ = right of an uphill driver).</summary>
        float LatOf(Vehicle c)
        {
            float off = Mathf.Clamp(half[IndexAt(c.s)] * 0.5f, 1.6f, 2.8f);
            return c.lane * off * c.dir;    // lane -1 = left of the driver (India drives on the left)
        }

        void Place(Vehicle c, bool teleport)
        {
            int i = IndexAt(c.s);
            int j = Mathf.Clamp(i + 1, 0, road.Count - 1);
            float seg = Mathf.Max(0.001f, cumS[j] - cumS[i]);
            float t = Mathf.Clamp01((c.s - cumS[i]) / seg);
            Vector3 p = Vector3.Lerp(road[i], road[j], t);
            Vector3 f = Fwd(i), right = new Vector3(f.z, 0, -f.x);
            p += right * LatOf(c);
            // pitch from the road grade over the vehicle length
            Vector3 ahead = road[IndexAt(c.s + c.dir * c.len * 0.5f)], behind = road[IndexAt(c.s - c.dir * c.len * 0.5f)];
            Vector3 travel = ahead - behind; if (travel.sqrMagnitude < 0.01f) travel = f * c.dir;
            var rot = Quaternion.LookRotation(travel.normalized, Vector3.up);
            p.y += 0.08f;
            if (teleport || c.rb == null) { c.t.SetPositionAndRotation(p, rot); }
            else { c.rb.MovePosition(p); c.rb.MoveRotation(rot); }
        }

        Vector3 Fwd(int i)
        {
            Vector3 f = road[Mathf.Min(road.Count - 1, i + 2)] - road[Mathf.Max(0, i - 2)]; f.y = 0; return f.normalized;
        }

        int IndexAt(float s)
        {
            s = Mathf.Clamp(s, 0f, total);
            int lo = 0, hi = cumS.Length - 1;
            while (hi - lo > 1) { int m = (lo + hi) >> 1; if (cumS[m] <= s) lo = m; else hi = m; }
            return lo;
        }

        int NearestIndex(Vector3 p, int hint)
        {
            int best = hint; float bd = float.MaxValue;
            // local search first, then a coarse global search if we lost track
            for (int k = -40; k <= 40; k++)
            {
                int i = Mathf.Clamp(hint + k, 0, road.Count - 1);
                float d = (road[i].x - p.x) * (road[i].x - p.x) + (road[i].z - p.z) * (road[i].z - p.z);
                if (d < bd) { bd = d; best = i; }
            }
            if (bd > 100f)
                for (int i = 0; i < road.Count; i += 4)
                {
                    float d = (road[i].x - p.x) * (road[i].x - p.x) + (road[i].z - p.z) * (road[i].z - p.z);
                    if (d < bd) { bd = d; best = i; }
                }
            return best;
        }

        static Transform FindTractor()
        {
            var t = Object.FindFirstObjectByType<TractorController>();
            return t != null ? t.transform : null;
        }

        bool HasTrolley()
        {
            if (tractor == null) return false;
            var am = tractor.GetComponent<AttachmentManager>();
            return am != null && am.CurrentTrolley != null;
        }

        // ------------------------------------------------------------------ vehicles
        static readonly Color[] CarColors = { new Color(0.85f, 0.85f, 0.85f), new Color(0.75f, 0.1f, 0.08f), new Color(0.1f, 0.18f, 0.4f), new Color(0.15f, 0.15f, 0.15f), new Color(0.6f, 0.6f, 0.62f) };
        static readonly Color[] TruckColors = { new Color(0.95f, 0.55f, 0.1f), new Color(0.9f, 0.8f, 0.15f), new Color(0.15f, 0.5f, 0.25f), new Color(0.75f, 0.15f, 0.1f) };

        static GameObject BuildVehicle(Kind k, System.Random rnd)
        {
            var go = new GameObject("Traffic_" + k);
            var vis = new GameObject("Visual").transform; vis.SetParent(go.transform, false);
            var glass = MatLib.Opaque(new Color(0.08f, 0.1f, 0.12f), 0.9f, 0.3f);
            var rubber = MatLib.Opaque(new Color(0.06f, 0.06f, 0.06f), 0.15f, 0f);
            var lamp = MatLib.Emissive(new Color(1f, 0.95f, 0.8f), new Color(1.6f, 1.5f, 1.2f));
            var tail = MatLib.Emissive(new Color(0.8f, 0.05f, 0.05f), new Color(0.9f, 0f, 0f));
            Vector3 size; float wr;
            switch (k)
            {
                case Kind.Bus:
                {
                    var body = MatLib.Opaque(new Color(0.9f, 0.88f, 0.84f), 0.5f, 0.1f);
                    var stripe = MatLib.Opaque(new Color(0.75f, 0.12f, 0.1f), 0.5f, 0.1f);
                    size = new Vector3(2.5f, 2.9f, 10.5f); wr = 0.5f;
                    MeshUtil.Part(vis, PrimitiveType.Cube, new Vector3(0, 1.85f, 0), new Vector3(2.5f, 2.6f, 10.5f), Vector3.zero, body, "Body");
                    MeshUtil.Part(vis, PrimitiveType.Cube, new Vector3(0, 1.25f, 0), new Vector3(2.52f, 0.35f, 10.52f), Vector3.zero, stripe, "Stripe");
                    MeshUtil.Part(vis, PrimitiveType.Cube, new Vector3(0, 2.45f, 0.3f), new Vector3(2.54f, 0.75f, 8.6f), Vector3.zero, glass, "Windows");
                    MeshUtil.Part(vis, PrimitiveType.Cube, new Vector3(0, 2.2f, 5.24f), new Vector3(2.3f, 1.2f, 0.05f), Vector3.zero, glass, "Windscreen");
                    break;
                }
                case Kind.Truck:
                {
                    var paint = MatLib.Opaque(TruckColors[rnd.Next(TruckColors.Length)], 0.45f, 0.1f);
                    var cargo = MatLib.Opaque(new Color(0.55f, 0.35f, 0.18f), 0.2f, 0f);
                    size = new Vector3(2.45f, 3.2f, 8f); wr = 0.52f;
                    MeshUtil.Part(vis, PrimitiveType.Cube, new Vector3(0, 1.75f, 3f), new Vector3(2.4f, 2.1f, 2f), Vector3.zero, paint, "Cab");
                    MeshUtil.Part(vis, PrimitiveType.Cube, new Vector3(0, 2.2f, 4.02f), new Vector3(2.1f, 0.9f, 0.05f), Vector3.zero, glass, "Windscreen");
                    MeshUtil.Part(vis, PrimitiveType.Cube, new Vector3(0, 1.0f, -1f), new Vector3(2.2f, 0.4f, 6f), Vector3.zero, MatLib.Opaque(new Color(0.15f, 0.15f, 0.15f), 0.3f, 0.5f), "Chassis");
                    MeshUtil.Part(vis, PrimitiveType.Cube, new Vector3(0, 2.0f, -1.1f), new Vector3(2.45f, 1.6f, 5.6f), Vector3.zero, paint, "Body");
                    MeshUtil.Part(vis, PrimitiveType.Cube, new Vector3(0, 3.0f, -1.1f), new Vector3(2.2f, 0.5f, 5.2f), Vector3.zero, cargo, "Tarpaulin");
                    break;
                }
                case Kind.Auto:
                {
                    var yellow = MatLib.Opaque(new Color(0.95f, 0.8f, 0.1f), 0.45f, 0.05f);
                    var green = MatLib.Opaque(new Color(0.15f, 0.45f, 0.2f), 0.45f, 0.05f);
                    size = new Vector3(1.35f, 1.75f, 2.7f); wr = 0.24f;
                    MeshUtil.Part(vis, PrimitiveType.Cube, new Vector3(0, 0.7f, 0), new Vector3(1.3f, 0.8f, 2.6f), Vector3.zero, green, "Body");
                    MeshUtil.Part(vis, PrimitiveType.Cube, new Vector3(0, 1.55f, -0.2f), new Vector3(1.35f, 0.1f, 2.1f), Vector3.zero, yellow, "Roof");
                    MeshUtil.Part(vis, PrimitiveType.Cube, new Vector3(0, 1.25f, 0.85f), new Vector3(1.1f, 0.55f, 0.05f), new Vector3(-15f, 0, 0), glass, "Windscreen");
                    break;
                }
                default:
                {
                    var paint = MatLib.Opaque(CarColors[rnd.Next(CarColors.Length)], 0.75f, 0.35f);
                    size = new Vector3(1.75f, 1.5f, 4.1f); wr = 0.31f;
                    MeshUtil.Part(vis, PrimitiveType.Cube, new Vector3(0, 0.62f, 0), new Vector3(1.72f, 0.62f, 4.05f), Vector3.zero, paint, "Body");
                    MeshUtil.Part(vis, PrimitiveType.Cube, new Vector3(0, 1.15f, -0.25f), new Vector3(1.55f, 0.5f, 2.1f), Vector3.zero, glass, "Cabin");
                    MeshUtil.Part(vis, PrimitiveType.Cube, new Vector3(0, 1.41f, -0.3f), new Vector3(1.5f, 0.04f, 1.7f), Vector3.zero, paint, "Roof");
                    break;
                }
            }
            float zf = size.z * 0.5f;
            for (int s = -1; s <= 1; s += 2)
            {
                MeshUtil.Part(vis, PrimitiveType.Cube, new Vector3(s * (size.x * 0.5f - 0.25f), k == Kind.Bus ? 0.9f : 0.65f, zf + 0.01f), new Vector3(0.28f, 0.14f, 0.04f), Vector3.zero, lamp, "Headlamp");
                MeshUtil.Part(vis, PrimitiveType.Cube, new Vector3(s * (size.x * 0.5f - 0.2f), k == Kind.Bus ? 0.9f : 0.7f, -zf - 0.01f), new Vector3(0.22f, 0.12f, 0.04f), Vector3.zero, tail, "Taillamp");
                // wheels
                float[] axles = k == Kind.Auto ? new[] { -0.8f } : new[] { zf * 0.62f, -zf * 0.62f };
                foreach (var az in axles)
                    MeshUtil.Part(vis, PrimitiveType.Cylinder, new Vector3(s * (size.x * 0.5f - 0.12f), wr, az), new Vector3(wr * 2f, 0.12f, wr * 2f), new Vector3(0, 0, 90f), rubber, "Wheel");
            }
            if (k == Kind.Auto) MeshUtil.Part(vis, PrimitiveType.Cylinder, new Vector3(0, wr, 1f), new Vector3(wr * 2f, 0.1f, wr * 2f), new Vector3(0, 0, 90f), rubber, "FrontWheel");
            MeshUtil.CombineByMaterial(vis.gameObject);
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            var bc = go.AddComponent<BoxCollider>();
            bc.size = size; bc.center = new Vector3(0, size.y * 0.5f + 0.15f, 0);
            go.AddComponent<SurfaceZone>().kind = SurfaceKind.Asphalt;
            return go;
        }

        void OnDestroy() { if (I == this) I = null; }
    }
}
