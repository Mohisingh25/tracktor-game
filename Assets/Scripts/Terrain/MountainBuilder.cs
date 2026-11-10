using System.Collections.Generic;
using UnityEngine;

namespace TractorSim
{
    /// <summary>
    /// "Himalayan Ghat": a 1.2 km x 1.2 km Unity Terrain with a forested ridge climbed by a ~4 km
    /// asphalt ghat road of 12 stacked legs joined by 11 hairpin bends, one above the other like a real
    /// hill-station road. Legs climb at 7.5 % (one 11 % pitch), hairpins at 3 % and are widened for
    /// trolleys. Crash barriers on the hairpins, guard stones along the drops, two landslide stretches
    /// and two muddy patches, a dhaba lay-by halfway, a depot in the valley, a village at the summit,
    /// clouds drifting in the valleys and traffic on both lanes (TrafficManager).
    /// Registers its yards, routes and spawn points in <see cref="WorldAnchors"/> for the missions.
    /// </summary>
    public static class MountainBuilder
    {
        const float Size = 1200f, Height = 620f;
        const int Res = 513;
        public const float RoadHalf = 3.5f;
        const float Cx = 600f, Z0 = 190f, HairR = 14f;
        const float Grade = 0.075f, HairGrade = 0.03f, FaceSlope = 0.68f;
        const int Legs = 12;

        public static Vector3 Spawn; public static Quaternion SpawnRot;
        public static List<Vector3> Road { get { return road; } }

        static Transform root;
        static float[,] natural;
        static List<Vector3> road;          // centreline every ~1 m (x, height, z)
        static List<float> halfW;           // half width per sample (wider in hairpins)
        static List<int> hairOf;            // hairpin index per sample, -1 on legs
        static List<Vector2> hairCenters = new List<Vector2>();
        static List<int> legStart = new List<int>();
        static int dhabaIdx;
        static Vector3 dhabaPos, depotCenter, summitCenter;
        static float depotYaw, summitYaw;
        static readonly int[] slideA = new int[2], slideB = new int[2];   // landslide index ranges
        static readonly int[] mudA = new int[2], mudB = new int[2];

        public static Transform Build()
        {
            root = new GameObject("MountainWorld").transform;
            SurfaceZone.ClearCache();
            WorldAnchors.Clear(root);
            var rnd = new System.Random(2026);

            BuildRoadPath();
            PlanPlaces();
            BuildNatural();
            BuildTerrain();
            BuildRoadMesh();
            BuildBarriers();
            BuildForest(rnd);
            BuildRoadside(rnd);
            BuildLandslides(rnd);
            BuildDepot(rnd);
            BuildDhaba();
            BuildSummit(rnd);
            BuildClouds();
            RegisterRoutes();
            var traffic = new GameObject("Traffic").AddComponent<TrafficManager>();
            traffic.transform.SetParent(root, false);
            traffic.Init(road, halfW, TrafficManager.DefaultDensity);
            return root;
        }

        // ================================================================== road plan
        static float HairSpan(int j) { return Mathf.Lerp(360f, 150f, j / (float)(Legs - 2)); }
        static int Side(int j) { return j % 2 == 0 ? 1 : -1; }

        static void BuildRoadPath()
        {
            var pts = new List<Vector2>();
            var grade = new List<float>();
            var hair = new List<int>();
            legStart.Clear(); hairCenters.Clear();
            float z = Z0;
            for (int leg = 0; leg < Legs; leg++)
            {
                float xs = leg == 0 ? Cx - HairSpan(0) * 0.5f - 160f : Cx + Side(leg - 1) * HairSpan(leg - 1) * 0.5f;
                float xe = leg < Legs - 1 ? Cx + Side(leg) * HairSpan(leg) * 0.5f : Cx - Side(leg - 1) * 40f;
                float flatUntil = leg == 0 ? Cx - HairSpan(0) * 0.5f + 10f : xs;    // the approach runs flat along the valley
                float len = Mathf.Abs(xe - xs);
                float climbLen = leg == 0 ? Mathf.Abs(xe - flatUntil) : len;
                float dz = climbLen * Grade / FaceSlope;
                int n = Mathf.CeilToInt(len);
                legStart.Add(pts.Count);
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)n;
                    float x = Mathf.Lerp(xs, xe, t);
                    float climbT = leg == 0 ? Mathf.Clamp01((x - flatUntil) / Mathf.Max(1f, xe - flatUntil)) : t;
                    float zz = z + dz * climbT + 3f * Mathf.Sin(Mathf.PI * t) + 1.4f * Mathf.Sin(t * Mathf.PI * 5f + leg) * Mathf.Sin(Mathf.PI * t);
                    pts.Add(new Vector2(x, zz));
                    float g = (leg == 0 && x < flatUntil) ? 0.012f : Grade * (1f + 0.18f * Mathf.Sin(t * 9f + leg * 1.7f));
                    if (leg == 8 && t > 0.42f && t < 0.62f) g = 0.11f;                  // the brutal pitch
                    grade.Add(g); hair.Add(-1);
                }
                z += dz;
                if (leg == Legs - 1) { pts.Add(new Vector2(xe, z)); grade.Add(0.02f); hair.Add(-1); break; }
                // hairpin: semicircle bulging outwards
                var c = new Vector2(xe, z + HairR);
                hairCenters.Add(c);
                int hn = Mathf.CeilToInt(Mathf.PI * HairR);
                for (int i = 0; i < hn; i++)
                {
                    float phi = -Mathf.PI * 0.5f + Mathf.PI * i / hn;
                    pts.Add(new Vector2(c.x + Side(leg) * HairR * Mathf.Cos(phi), c.y + HairR * Mathf.Sin(phi)));
                    grade.Add(HairGrade); hair.Add(leg);
                }
                z += 2f * HairR;
            }
            // light smoothing of the plan (removes the small kinks where legs meet hairpins)
            var sm = new List<Vector2>(pts);
            for (int pass = 0; pass < 2; pass++)
                for (int i = 3; i < pts.Count - 3; i++)
                {
                    Vector2 s = Vector2.zero;
                    for (int k = -3; k <= 3; k++) s += pts[i + k];
                    sm[i] = s / 7f;
                }
            pts = sm;
            // heights: integrate the grade along the road
            int count = pts.Count;
            var h = new float[count];
            h[0] = 18f;
            for (int i = 1; i < count; i++) h[i] = h[i - 1] + grade[i] * Vector2.Distance(pts[i], pts[i - 1]);
            var h2 = new float[count];
            for (int pass = 0; pass < 3; pass++)
            {
                for (int i = 0; i < count; i++)
                {
                    float s = 0f; int c = 0;
                    for (int k = -8; k <= 8; k++) { int j = Mathf.Clamp(i + k, 0, count - 1); s += h[j]; c++; }
                    h2[i] = s / c;
                }
                System.Array.Copy(h2, h, count);
            }
            road = new List<Vector3>(count); halfW = new List<float>(count); hairOf = hair;
            for (int i = 0; i < count; i++) road.Add(new Vector3(pts[i].x, h[i], pts[i].y));
            for (int i = 0; i < count; i++) halfW.Add(RoadHalf + (hair[i] >= 0 ? 2.6f : Mathf.Clamp(Curvature(i) * 120f, 0f, 1.5f)));
            // blend the widening in and out
            var hw = new List<float>(halfW);
            for (int i = 0; i < count; i++)
            {
                float m = 0f;
                for (int k = -12; k <= 12; k++) { int j = Mathf.Clamp(i + k, 0, count - 1); m = Mathf.Max(m, halfW[j] - Mathf.Abs(k) * 0.12f); }
                hw[i] = Mathf.Max(halfW[i], m);
            }
            halfW = hw;

            // special places along the road
            dhabaIdx = Mid(5, 0.5f);
            Range(slideA, Mid(3, 0.45f), 26); Range(slideB, Mid(9, 0.3f), 20);
            Range(mudA, Mid(6, 0.7f), 22); Range(mudB, Mid(10, 0.45f), 16);
        }

        static int Mid(int leg, float t)
        {
            int a = legStart[leg], b = leg + 1 < legStart.Count ? legStart[leg + 1] : road.Count;
            int lastLeg = a; while (lastLeg < b && hairOf[lastLeg] < 0) lastLeg++;
            return Mathf.Clamp(a + Mathf.RoundToInt((lastLeg - a) * t), 0, road.Count - 1);
        }
        static void Range(int[] r, int c, int half) { r[0] = c - half; r[1] = c + half; }
        static bool In(int[] r, float i) { return i >= r[0] && i <= r[1]; }

        public static float HalfAt(int i) { return halfW[Mathf.Clamp(i, 0, halfW.Count - 1)]; }

        static float Curvature(int i)
        {
            int a = Mathf.Max(0, i - 6), b = Mathf.Min(road.Count - 1, i + 6);
            Vector3 d1 = road[i] - road[a]; d1.y = 0; Vector3 d2 = road[b] - road[i]; d2.y = 0;
            if (d1.sqrMagnitude < 0.01f || d2.sqrMagnitude < 0.01f) return 0f;
            return Vector3.Angle(d1, d2) * Mathf.Deg2Rad / Mathf.Max(1f, (b - a));
        }

        static Vector3 RoadDir(int i)
        {
            Vector3 f = road[Mathf.Min(road.Count - 1, i + 2)] - road[Mathf.Max(0, i - 2)]; f.y = 0; return f.normalized;
        }

        static float YawAt(int i) { var f = RoadDir(i); return Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg; }

        static float DistToRoad(Vector3 p, int step = 3)
        {
            float best = 1e9f;
            for (int i = 0; i < road.Count; i += step)
            {
                float dx = road[i].x - p.x, dz = road[i].z - p.z;
                float d = dx * dx + dz * dz;
                if (d < best) best = d;
            }
            return Mathf.Sqrt(best);
        }

        // ================================================================== natural ground
        static float[] faceH, faceHalf;     // per 10 m z-bin: mean road height, half extent of the road in x
        const float Bin = 10f;

        static float Ridged(float x, float z, float f)
        {
            float n = Mathf.PerlinNoise(x * f + 31.7f, z * f + 11.3f);
            n = 1f - Mathf.Abs(n * 2f - 1f);
            return n * n;
        }

        static void BuildNatural()
        {
            int bins = Mathf.CeilToInt(Size / Bin);
            faceH = new float[bins]; faceHalf = new float[bins];
            var cnt = new int[bins];
            foreach (var p in road)
            {
                int b = Mathf.Clamp((int)(p.z / Bin), 0, bins - 1);
                faceH[b] += p.y; cnt[b]++;
                faceHalf[b] = Mathf.Max(faceHalf[b], Mathf.Abs(p.x - Cx));
            }
            int first = -1, last = -1;
            for (int b = 0; b < bins; b++) if (cnt[b] > 0) { faceH[b] /= cnt[b]; if (first < 0) first = b; last = b; }
            for (int b = 0; b < bins; b++)
            {
                if (cnt[b] > 0) continue;
                if (b < first) { faceH[b] = faceH[first]; faceHalf[b] = faceHalf[first]; }
                else if (b > last) { faceH[b] = faceH[last] + (b - last) * Bin * 0.42f; faceHalf[b] = faceHalf[last] + (b - last) * 3f; }
                else
                {   // gap inside: interpolate
                    int a = b - 1, c = b + 1; while (c < bins && cnt[c] == 0) c++;
                    float t = (b - a) / (float)(c - a);
                    faceH[b] = Mathf.Lerp(faceH[a], faceH[Mathf.Min(c, bins - 1)], t);
                    faceHalf[b] = Mathf.Lerp(faceHalf[a], faceHalf[Mathf.Min(c, bins - 1)], t);
                }
            }
            // smooth the face profile and width heavily (no terraces outside the road area)
            var tmp = new float[bins];
            for (int pass = 0; pass < 4; pass++)
            {
                for (int b = 0; b < bins; b++) { float s = 0; int c = 0; for (int k = -6; k <= 6; k++) { int j = Mathf.Clamp(b + k, 0, bins - 1); s += faceH[j]; c++; } tmp[b] = s / c; }
                System.Array.Copy(tmp, faceH, bins);
                for (int b = 0; b < bins; b++) { float s = 0; int c = 0; for (int k = -4; k <= 4; k++) { int j = Mathf.Clamp(b + k, 0, bins - 1); s += faceHalf[j]; c++; } tmp[b] = s / c; }
                System.Array.Copy(tmp, faceHalf, bins);
            }
            float zTop = road[road.Count - 1].z;
            natural = new float[Res, Res];
            for (int iz = 0; iz < Res; iz++)
                for (int ix = 0; ix < Res; ix++)
                {
                    float x = ix / (float)(Res - 1) * Size, z = iz / (float)(Res - 1) * Size;
                    float fb = Mathf.Clamp(z / Bin - 0.5f, 0f, bins - 1.001f);
                    int b = (int)fb; float bt = fb - b;
                    float faceAt = Mathf.Lerp(faceH[b], faceH[b + 1], bt), halfAt = Mathf.Lerp(faceHalf[b], faceHalf[b + 1], bt);
                    float ridge = faceAt + (Mathf.PerlinNoise(x * 0.02f + 3f, z * 0.02f + 7f) - 0.5f) * 6f;
                    if (z > zTop + 30f) ridge += Ridged(x, z, 0.006f) * 90f * Mathf.Clamp01((z - zTop - 30f) / 300f);   // peaks behind the summit
                    float e = Mathf.Abs(x - Cx) - (halfAt + 30f);
                    float valley = 8f + Mathf.PerlinNoise(x * 0.012f + 1f, z * 0.012f + 2f) * 14f;
                    float sideRange = Mathf.Max(0f, Mathf.Abs(x - Cx) - 390f) * 0.95f + Ridged(x, z, 0.0045f) * 80f * Mathf.Clamp01((Mathf.Abs(x - Cx) - 360f) / 140f);
                    float h = e <= 0f ? ridge : Mathf.Lerp(ridge, valley, Mathf.SmoothStep(0f, 1f, e / 170f)) + sideRange * Mathf.Clamp01(e / 220f);
                    if (z < 120f) h = Mathf.Lerp(valley + sideRange * 0.5f, h, Mathf.SmoothStep(0f, 1f, z / 120f));      // valley floor at the south edge
                    natural[iz, ix] = Mathf.Max(2f, h + Ridged(x, z, 0.03f) * 4f);
                }
        }

        // ================================================================== terrain
        static Terrain terrainRef;
        public static Terrain ActiveTerrain { get { return terrainRef; } }
        static float[,] roadDist;

        static void BuildTerrain()
        {
            float cell = Size / (Res - 1);
            var sumW = new float[Res, Res]; var sumWH = new float[Res, Res];
            roadDist = new float[Res, Res]; var nearH = new float[Res, Res]; var nearHalf = new float[Res, Res];
            for (int iz = 0; iz < Res; iz++) for (int ix = 0; ix < Res; ix++) roadDist[iz, ix] = 1e9f;
            const float Reach = 64f;
            int rad = Mathf.CeilToInt(Reach / cell);
            for (int i = 0; i < road.Count; i += 2)
            {
                var p = road[i];
                int cx = Mathf.RoundToInt(p.x / cell), cz = Mathf.RoundToInt(p.z / cell);
                for (int dz = -rad; dz <= rad; dz++)
                    for (int dx = -rad; dx <= rad; dx++)
                    {
                        int x = cx + dx, z = cz + dz;
                        if (x < 0 || z < 0 || x >= Res || z >= Res) continue;
                        float wx = x * cell - p.x, wz = z * cell - p.z;
                        float d2 = wx * wx + wz * wz;
                        if (d2 > Reach * Reach) continue;
                        float w = 1f / (d2 + 9f);
                        sumW[z, x] += w; sumWH[z, x] += w * p.y;
                        float d = Mathf.Sqrt(d2);
                        if (d < roadDist[z, x]) { roadDist[z, x] = d; nearH[z, x] = p.y; nearHalf[z, x] = halfW[i]; }
                    }
            }
            var final = new float[Res, Res];
            for (int iz = 0; iz < Res; iz++)
                for (int ix = 0; ix < Res; ix++)
                {
                    float wx = ix * cell, wz = iz * cell;
                    float h = natural[iz, ix];
                    float d = roadDist[iz, ix];
                    if (sumW[iz, ix] > 0f)
                    {
                        float idw = sumWH[iz, ix] / sumW[iz, ix];
                        h = Mathf.Lerp(idw, h, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(26f, 62f, d)));
                    }
                    float band = nearHalf[iz, ix] + 1.2f;
                    if (d < band + 7f)
                        h = Mathf.Lerp(nearH[iz, ix] - 0.3f, h, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(band, band + 7f, d)));
                    h = Flatten(h, wx, wz, depotCenter, 40f, 16f);
                    h = Flatten(h, wx, wz, summitCenter, 42f, 18f);
                    h = Flatten(h, wx, wz, dhabaPos, 9f, 7f);
                    final[iz, ix] = h;
                }

            var td = new TerrainData();
            td.heightmapResolution = Res;
            td.size = new Vector3(Size, Height, Size);
            var norm = new float[Res, Res];
            for (int iz = 0; iz < Res; iz++) for (int ix = 0; ix < Res; ix++) norm[iz, ix] = Mathf.Clamp01(final[iz, ix] / Height);
            td.SetHeights(0, 0, norm);

            var cat = ArtCatalog.Load();
            td.terrainLayers = new[]
            {
                Layer(cat != null ? cat.grass : null, ProcTex.Ground("mgrass", new Color(0.22f, 0.36f, 0.14f), new Color(0.4f, 0.49f, 0.24f), 7f, 0.3f, 21), 5f),
                Layer(cat != null ? cat.dirt : null, ProcTex.Ground("mdirt", new Color(0.45f, 0.36f, 0.26f), new Color(0.6f, 0.5f, 0.38f), 6f, 0.35f, 22), 4f),
                Layer(cat != null ? cat.rock : null, ProcTex.Ground("mrock", new Color(0.4f, 0.39f, 0.37f), new Color(0.62f, 0.6f, 0.57f), 10f, 0.45f, 23), 8f),
                Layer(cat != null ? cat.mud : null, ProcTex.Ground("mmud", new Color(0.2f, 0.15f, 0.09f), new Color(0.33f, 0.25f, 0.15f), 5f, 0.2f, 24), 4f),
                Layer(null, ProcTex.Ground("msnow", new Color(0.86f, 0.88f, 0.92f), new Color(0.98f, 0.98f, 1f), 6f, 0.2f, 29), 6f),
            };
            int A = 512;
            td.alphamapResolution = A;
            var alpha = new float[A, A, 5];
            for (int az = 0; az < A; az++)
                for (int ax = 0; ax < A; ax++)
                {
                    float u = ax / (float)(A - 1), v = az / (float)(A - 1);
                    float steep = td.GetSteepness(u, v);
                    float hgt = td.GetInterpolatedHeight(u, v);
                    int hx = Mathf.RoundToInt(u * (Res - 1)), hz = Mathf.RoundToInt(v * (Res - 1));
                    float d = roadDist[hz, hx];
                    float noise = Mathf.PerlinNoise(u * 40f, v * 40f);
                    float rock = Mathf.InverseLerp(32f, 44f, steep) * 1.6f + Mathf.InverseLerp(330f, 400f, hgt) * 0.8f;
                    float dirt = d < RoadHalf + 4.5f ? 0.9f : (d < RoadHalf + 9f ? 0.45f : 0.08f + noise * 0.3f);
                    float snow = Mathf.InverseLerp(390f, 440f, hgt) * 3f * Mathf.InverseLerp(48f, 30f, steep);
                    float mud = d < 14f ? Mathf.Max(0f, noise - 0.72f) * 2f : 0f;
                    float grass = Mathf.Max(0f, 1f - snow);
                    float sum = grass + dirt + rock + mud + snow;
                    alpha[az, ax, 0] = grass / sum; alpha[az, ax, 1] = dirt / sum; alpha[az, ax, 2] = rock / sum; alpha[az, ax, 3] = mud / sum; alpha[az, ax, 4] = snow / sum;
                }
            td.SetAlphamaps(0, 0, alpha);

            var bladeTex = cat != null && cat.grassBlades != null ? cat.grassBlades : ProcTex.GrassBlades();
            var dp = new DetailPrototype();
            dp.prototypeTexture = bladeTex;
            dp.renderMode = DetailRenderMode.GrassBillboard;
            dp.usePrototypeMesh = false;
            dp.minWidth = 0.5f; dp.maxWidth = 0.9f; dp.minHeight = 0.35f; dp.maxHeight = 0.8f;
            dp.healthyColor = new Color(0.52f, 0.68f, 0.32f); dp.dryColor = new Color(0.74f, 0.68f, 0.42f);
            dp.noiseSpread = 0.4f;
            td.detailPrototypes = new[] { dp };
            int D = 512;
            td.SetDetailResolution(D, 16);
            var det = new int[D, D];
            for (int dz = 0; dz < D; dz++)
                for (int dx = 0; dx < D; dx++)
                {
                    int hx = Mathf.RoundToInt(dx / (float)(D - 1) * (Res - 1)), hz = Mathf.RoundToInt(dz / (float)(D - 1) * (Res - 1));
                    float d = roadDist[hz, hx];
                    if (d < RoadHalf + 2.8f || d > 80f) continue;
                    float steep = td.GetSteepness(dx / (float)(D - 1), dz / (float)(D - 1));
                    if (steep > 34f) continue;
                    det[dz, dx] = Mathf.PerlinNoise(dx * 0.08f, dz * 0.08f) > 0.35f ? 6 : 2;
                }
            td.SetDetailLayer(0, 0, 0, det);
            td.wavingGrassStrength = 0.25f; td.wavingGrassAmount = 0.3f; td.wavingGrassSpeed = 0.4f;
            td.wavingGrassTint = new Color(0.7f, 0.75f, 0.55f);

            var go = Terrain.CreateTerrainGameObject(td);
            go.name = "MountainTerrain";
            go.transform.SetParent(root, false);
            var terrain = go.GetComponent<Terrain>();
            var tm = Resources.Load<Material>(MatLib.ResPath + "Terrain");
            if (tm != null) terrain.materialTemplate = tm;
            terrain.heightmapPixelError = 4f;
            terrain.basemapDistance = 350f;
            terrain.detailObjectDistance = 70f;
            terrain.detailObjectDensity = 1f;
            terrain.drawInstanced = true;
            var ts = go.AddComponent<TerrainSurface>();
            ts.Init(terrain, new[] { SurfaceKind.Grass, SurfaceKind.Dirt, SurfaceKind.Rock, SurfaceKind.Mud, SurfaceKind.Rock });
            terrainRef = terrain;
        }

        static float Flatten(float h, float x, float z, Vector3 c, float r, float blend)
        {
            float d = Vector2.Distance(new Vector2(x, z), new Vector2(c.x, c.z));
            if (d > r + blend) return h;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(r, r + blend, d));
            return Mathf.Lerp(c.y - 0.25f, h, t);
        }

        static TerrainLayer Layer(ArtCatalog.GroundTexture art, Texture2D fallback, float tile)
        {
            var l = new TerrainLayer();
            if (ArtCatalog.Has(art))
            {
                l.diffuseTexture = art.albedo;
                if (art.normal != null) l.normalMapTexture = art.normal;
                l.tileSize = Vector2.one * Mathf.Max(0.5f, art.tileSize);
            }
            else { l.diffuseTexture = fallback; l.tileSize = Vector2.one * tile; }
            l.smoothness = 0.05f; l.metallic = 0f;
            return l;
        }

        static float TerrainH(Vector3 p) { return terrainRef != null ? terrainRef.SampleHeight(p) + terrainRef.transform.position.y : p.y; }

        // ================================================================== road surface
        static void BuildRoadMesh()
        {
            var cat = ArtCatalog.Load();
            var asphalt = MatLib.Opaque(Color.white, 0.22f, 0f, ProcTex.Road(), Vector2.one);
            var mud = MatLib.Opaque(Color.white, 0.55f, 0f, ArtCatalog.Has(cat != null ? cat.mud : null) ? cat.mud.albedo : ProcTex.Ground("mud", new Color(0.2f, 0.14f, 0.08f), new Color(0.34f, 0.25f, 0.15f), 4f, 0.2f, 3), Vector2.one);
            var debris = MatLib.Opaque(Color.white, 0.12f, 0f, ProcTex.Ground("rockroad", new Color(0.38f, 0.35f, 0.31f), new Color(0.58f, 0.54f, 0.48f), 14f, 0.5f, 25), Vector2.one);
            var roadRoot = new GameObject("GhatRoad").transform; roadRoot.SetParent(root, false);
            const int Chunk = 40;
            for (int start = 0; start < road.Count - 1; start += Chunk)
            {
                int end = Mathf.Min(road.Count - 1, start + Chunk);
                // split chunks at special stretches so their surface is exact
                int s = start;
                while (s < end)
                {
                    SurfaceKind kind = KindAt(s);
                    int e = s + 1;
                    while (e < end && KindAt(e) == kind) e++;
                    Material m = kind == SurfaceKind.Mud ? mud : kind == SurfaceKind.Rock ? debris : asphalt;
                    RoadChunk(roadRoot, s, e, m, kind);
                    s = e;
                }
            }
        }

        static SurfaceKind KindAt(int i)
        {
            if (In(slideA, i) || In(slideB, i)) return SurfaceKind.Rock;
            if (In(mudA, i) || In(mudB, i)) return SurfaceKind.Mud;
            return SurfaceKind.Asphalt;
        }

        static void RoadChunk(Transform parent, int a, int b, Material m, SurfaceKind kind)
        {
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            for (int i = a; i <= b; i++)
            {
                Vector3 p = road[i];
                Vector3 fwd = (road[Mathf.Min(road.Count - 1, i + 1)] - road[Mathf.Max(0, i - 1)]); fwd.y = 0; fwd.Normalize();
                Vector3 right = new Vector3(fwd.z, 0, -fwd.x);
                float half = halfW[i];
                float v = i / 7.5f;
                // bank the hairpins slightly inwards, crown on the legs
                float bank = 0f;
                if (hairOf[i] >= 0)
                {
                    var c = hairCenters[hairOf[i]];
                    Vector3 toC = new Vector3(c.x - p.x, 0, c.y - p.z).normalized;
                    bank = Vector3.Dot(toC, right) > 0f ? -0.25f : 0.25f;      // inner edge lower
                }
                Vector3 l = p - right * half, r = p + right * half;
                verts.Add(l + Vector3.down * 0.45f); uvs.Add(new Vector2(0.07f, v));
                verts.Add(l + Vector3.up * (0.04f + bank)); uvs.Add(new Vector2(0f, v));
                verts.Add(p + Vector3.up * 0.09f); uvs.Add(new Vector2(0.5f, v));
                verts.Add(r + Vector3.up * (0.04f - bank)); uvs.Add(new Vector2(1f, v));
                verts.Add(r + Vector3.down * 0.45f); uvs.Add(new Vector2(0.93f, v));
            }
            int rows = b - a;
            for (int i = 0; i < rows; i++)
                for (int k = 0; k < 4; k++)
                {
                    int v0 = i * 5 + k, v1 = v0 + 1, v2 = v0 + 5, v3 = v0 + 6;
                    tris.Add(v0); tris.Add(v2); tris.Add(v1);
                    tris.Add(v1); tris.Add(v2); tris.Add(v3);
                }
            var mesh = new Mesh(); mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var go = new GameObject("Road_" + a + "_" + kind);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = m;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            go.AddComponent<SurfaceZone>().kind = kind;
        }

        // ================================================================== crash barriers (W-beam) on the hairpins
        static void BuildBarriers()
        {
            var steel = MatLib.Opaque(new Color(0.72f, 0.74f, 0.76f), 0.7f, 0.85f);
            var post = MatLib.Opaque(new Color(0.3f, 0.3f, 0.32f), 0.4f, 0.6f);
            var stripe = MatLib.Opaque(new Color(0.95f, 0.8f, 0.1f), 0.4f, 0f);
            var bar = new GameObject("CrashBarriers").transform; bar.SetParent(root, false);
            var vis = new GameObject("Visual").transform; vis.SetParent(bar, false);
            for (int h = 0; h < hairCenters.Count; h++)
            {
                var c = hairCenters[h];
                int first = -1, last = -1;
                for (int i = 0; i < road.Count; i++) if (hairOf[i] == h) { if (first < 0) first = i; last = i; }
                if (first < 0) continue;
                first = Mathf.Max(0, first - 18); last = Mathf.Min(road.Count - 1, last + 18);
                Vector3 prev = Vector3.zero; bool has = false;
                for (int i = first; i <= last; i += 3)
                {
                    Vector3 p = road[i];
                    Vector3 outDir = new Vector3(p.x - c.x, 0, p.z - c.y).normalized;
                    Vector3 q = p + outDir * (halfW[i] + 0.7f);
                    q.y = p.y;
                    MeshUtil.Part(vis, PrimitiveType.Cube, q + Vector3.up * 0.4f, new Vector3(0.12f, 0.8f, 0.12f), Vector3.zero, post, "Post");
                    if (has)
                    {
                        Vector3 mid = (prev + q) * 0.5f; Vector3 d = q - prev;
                        float len = d.magnitude;
                        if (len > 0.2f)
                        {
                            float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, pitch = -Mathf.Asin(Mathf.Clamp(d.y / len, -1f, 1f)) * Mathf.Rad2Deg;
                            MeshUtil.Part(vis, PrimitiveType.Cube, mid + Vector3.up * 0.62f, new Vector3(0.06f, 0.32f, len + 0.05f), new Vector3(pitch, yaw, 0), steel, "Rail");
                            if ((i / 3) % 2 == 0) MeshUtil.Part(vis, PrimitiveType.Cube, mid + Vector3.up * 0.62f - outDir * 0.035f, new Vector3(0.01f, 0.12f, len * 0.4f), new Vector3(pitch, yaw, 0), stripe, "Reflector");
                            WorldProps.Collider(bar, mid + Vector3.up * 0.55f, new Vector3(0.3f, 1.1f, len + 0.05f), yaw);
                        }
                    }
                    prev = q; has = true;
                }
                // chevron board at the apex
                int apex = (first + last) / 2;
                Vector3 ap = road[apex] + new Vector3(road[apex].x - c.x, 0, road[apex].z - c.y).normalized * (halfW[apex] + 1.6f);
                ap.y = road[apex].y;
                float face = Mathf.Atan2(c.x - ap.x, c.y - ap.z) * Mathf.Rad2Deg;
                WorldProps.Sign(bar, ap, face, ">>>>", new Color(0.95f, 0.8f, 0.1f), new Color(0.1f, 0.1f, 0.1f));
            }
            MeshUtil.CombineByMaterial(vis.gameObject);
        }

        // ================================================================== vegetation
        static void BuildForest(System.Random rnd)
        {
            var trunk = MatLib.Opaque(new Color(0.28f, 0.2f, 0.14f), 0.05f, 0f);
            var needles = MatLib.Opaque(new Color(0.11f, 0.24f, 0.13f), 0.08f, 0f);
            var needles2 = MatLib.Opaque(new Color(0.16f, 0.3f, 0.15f), 0.08f, 0f);
            var forest = new GameObject("Forest").transform; forest.SetParent(root, false);
            var cols = new GameObject("TreeColliders").transform; cols.SetParent(forest, false);
            const float ChunkSize = 150f;
            int chunks = Mathf.CeilToInt(Size / ChunkSize);
            var chunkRoots = new Transform[chunks, chunks];
            int placed = 0, guard = 0;
            int target = QualityTrees();
            var catA = ArtCatalog.Load();
            bool art = catA != null && catA.trees != null && catA.trees.Length > 0;
            float cell = Size / (Res - 1);
            while (placed < target && guard++ < target * 14)
            {
                Vector3 p;
                if (rnd.NextDouble() < 0.7)
                {   // dense near the road (what the player sees)
                    int i = rnd.Next(road.Count);
                    float ang = (float)rnd.NextDouble() * Mathf.PI * 2f;
                    float r = 7f + Mathf.Pow((float)rnd.NextDouble(), 1.4f) * 120f;
                    p = new Vector3(road[i].x + Mathf.Cos(ang) * r, 0, road[i].z + Mathf.Sin(ang) * r);
                }
                else p = new Vector3((float)rnd.NextDouble() * Size, 0, (float)rnd.NextDouble() * Size);
                if (p.x < 5 || p.z < 5 || p.x > Size - 5 || p.z > Size - 5) continue;
                int hx = Mathf.Clamp(Mathf.RoundToInt(p.x / cell), 0, Res - 1), hz = Mathf.Clamp(Mathf.RoundToInt(p.z / cell), 0, Res - 1);
                float dRoad = roadDist[hz, hx];
                if (dRoad < halfW[0] + 4.5f) continue;
                if (Flat(p, depotCenter) < 52f || Flat(p, summitCenter) < 55f || Flat(p, dhabaPos) < 14f) continue;
                float u = p.x / Size, v = p.z / Size;
                if (terrainRef.terrainData.GetSteepness(u, v) > 38f) continue;
                p.y = TerrainH(p);
                if (p.y > 400f) continue;                           // tree line
                float h = 10f + (float)rnd.NextDouble() * 13f;
                int cx = Mathf.Clamp((int)(p.x / ChunkSize), 0, chunks - 1), cz = Mathf.Clamp((int)(p.z / ChunkSize), 0, chunks - 1);
                if (chunkRoots[cx, cz] == null) { chunkRoots[cx, cz] = new GameObject("Chunk_" + cx + "_" + cz).transform; chunkRoots[cx, cz].SetParent(forest, false); }
                if (!(art && WorldProps.ArtTree(chunkRoots[cx, cz], p, h, rnd)))
                    WorldProps.Conifer(chunkRoots[cx, cz], p, h, rnd, trunk, needles, needles2);
                if (dRoad < 40f) WorldProps.Capsule(cols, p + Vector3.up * 2f, 0.35f, 4f);
                placed++;
            }
            foreach (var c in chunkRoots)
            {
                if (c == null) continue;
                if (!art) MeshUtil.CombineByMaterial(c.gameObject);
                var lod = c.gameObject.AddComponent<LODGroup>();
                lod.SetLODs(new[] { new LOD(0.01f, c.GetComponentsInChildren<Renderer>()) });
                lod.RecalculateBounds();
            }
            // boulders on the slopes
            var rockM = MatLib.Opaque(Color.white, 0.12f, 0f, ProcTex.Ground("boulder", new Color(0.38f, 0.37f, 0.35f), new Color(0.6f, 0.58f, 0.55f), 9f, 0.45f, 26), Vector2.one * 2f);
            var rocks = new GameObject("Boulders").transform; rocks.SetParent(root, false);
            for (int k = 0; k < 300; k++)
            {
                int i = rnd.Next(road.Count);
                float ang = (float)rnd.NextDouble() * Mathf.PI * 2f;
                float r = 7f + (float)rnd.NextDouble() * 60f;
                var p = new Vector3(road[i].x + Mathf.Cos(ang) * r, 0, road[i].z + Mathf.Sin(ang) * r);
                if (p.x < 2 || p.z < 2 || p.x > Size - 2 || p.z > Size - 2) continue;
                if (DistToRoad(p) < halfW[i] + 2.5f) continue;
                p.y = TerrainH(p);
                float s = 0.6f + Mathf.Pow((float)rnd.NextDouble(), 2f) * 3f;
                Boulder(rocks, p, s, rockM, rnd, r < 25f);
            }
        }

        static float Flat(Vector3 a, Vector3 b) { return new Vector2(a.x - b.x, a.z - b.z).magnitude; }

        static void Boulder(Transform parent, Vector3 p, float s, Material m, System.Random rnd, bool collide)
        {
            var b = new GameObject("Boulder"); b.transform.SetParent(parent, false);
            b.transform.position = p + Vector3.up * s * 0.15f;
            b.transform.rotation = Quaternion.Euler((float)rnd.NextDouble() * 360f, (float)rnd.NextDouble() * 360f, 0);
            b.transform.localScale = new Vector3(s * 1.3f, s * 0.75f, s);
            b.AddComponent<MeshFilter>().sharedMesh = MeshUtil.PrimMesh(PrimitiveType.Sphere);
            b.AddComponent<MeshRenderer>().sharedMaterial = m;
            if (collide) { b.AddComponent<SphereCollider>().radius = 0.5f; b.AddComponent<SurfaceZone>().kind = SurfaceKind.Rock; }
        }

        static int QualityTrees()
        {
            switch (SaveSystem.Settings != null ? SaveSystem.Settings.graphics : 1)
            {
                case 0: return 700;
                case 1: return 1500;
                case 2: return 2400;
                default: return 3400;
            }
        }

        // ================================================================== roadside furniture
        static void BuildRoadside(System.Random rnd)
        {
            var side = new GameObject("Roadside").transform; side.SetParent(root, false);
            var vis = new GameObject("Visual").transform; vis.SetParent(side, false);
            var white = MatLib.Opaque(new Color(0.94f, 0.94f, 0.92f), 0.2f, 0f);
            var black = MatLib.Opaque(new Color(0.08f, 0.08f, 0.08f), 0.2f, 0f);
            for (int i = 4; i < road.Count - 4; i += 6)
            {
                if (hairOf[i] >= 0) continue;                     // hairpins have crash barriers
                Vector3 p = road[i], f = RoadDir(i), r = new Vector3(f.z, 0, -f.x);
                float half = halfW[i];
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 probe = p + r * s * (half + 5f);
                    if (p.y - TerrainH(probe) < 1.5f) continue;   // only on the valley side
                    Vector3 edge = p + r * s * (half + 0.6f); edge.y = p.y;
                    MeshUtil.Part(vis, PrimitiveType.Cube, edge + Vector3.up * 0.3f, new Vector3(0.35f, 0.6f, 0.35f), Vector3.zero, white, "GuardStone");
                    MeshUtil.Part(vis, PrimitiveType.Cube, edge + Vector3.up * 0.42f, new Vector3(0.36f, 0.12f, 0.36f), Vector3.zero, black, "Band");
                    WorldProps.Collider(side, edge + Vector3.up * 0.3f, new Vector3(0.35f, 0.6f, 0.35f));
                }
            }
            MeshUtil.CombineByMaterial(vis.gameObject);

            for (int i = 300; i < road.Count - 80; i += 500)
            {
                Vector3 f = RoadDir(i), r = new Vector3(f.z, 0, -f.x);
                Vector3 pos = road[i] + r * (halfW[i] + 1.4f); pos.y = TerrainH(pos);
                WorldProps.Milestone(side, pos, Mathf.Atan2(-f.x, -f.z) * Mathf.Rad2Deg, "PAHADGAON", ((road.Count - i) / 1000f).ToString("0.0") + " km");
            }
            // hairpin numbers ("Hairpin 3/11") just before each bend, both directions
            for (int h = 0; h < hairCenters.Count; h++)
            {
                int first = -1, last = -1;
                for (int i = 0; i < road.Count; i++) if (hairOf[i] == h) { if (first < 0) first = i; last = i; }
                if (first < 0) continue;
                int up = Mathf.Max(0, first - 55), dn = Mathf.Min(road.Count - 1, last + 55);
                SignAt(side, up, -1, "HAIRPIN " + (h + 1) + "/" + hairCenters.Count + "\nLow gear", new Color(0.95f, 0.78f, 0.1f), new Color(0.1f, 0.1f, 0.1f), false);
                SignAt(side, dn, 1, "HAIRPIN " + (h + 1) + "/" + hairCenters.Count + "\nGo slow", new Color(0.95f, 0.78f, 0.1f), new Color(0.1f, 0.1f, 0.1f), true);
            }
            SignAt(side, 120, -1, "GHAT ROAD 4 km\n11 hairpins\nuse low gear", new Color(0.1f, 0.35f, 0.18f), Color.white, false);
            SignAt(side, slideA[0] - 60, -1, "LANDSLIDE\nAREA", new Color(0.85f, 0.2f, 0.1f), Color.white, false);
            SignAt(side, slideB[0] - 60, -1, "FALLING\nROCKS", new Color(0.85f, 0.2f, 0.1f), Color.white, false);
            SignAt(side, mudA[0] - 50, -1, "SLIPPERY\nROAD", new Color(0.95f, 0.78f, 0.1f), new Color(0.1f, 0.1f, 0.1f), false);
            SignAt(side, road.Count - 260, -1, "STEEP\nDESCENT\nbrake on engine", new Color(0.85f, 0.2f, 0.1f), Color.white, true);
        }

        /// <summary>Sign on the left (Indian traffic) of a driver going up (descending = false) or down.</summary>
        static void SignAt(Transform parent, int i, int unused, string text, Color board, Color ink, bool descending)
        {
            i = Mathf.Clamp(i, 2, road.Count - 3);
            Vector3 f = RoadDir(i); if (descending) f = -f;
            Vector3 right = new Vector3(f.z, 0, -f.x);
            Vector3 pos = road[i] - right * (halfW[i] + 1.8f); pos.y = Mathf.Max(TerrainH(pos), road[i].y - 0.4f);
            WorldProps.Sign(parent, pos, Mathf.Atan2(-f.x, -f.z) * Mathf.Rad2Deg, text, board, ink);
        }

        // ================================================================== landslides
        static void BuildLandslides(System.Random rnd)
        {
            var rockM = MatLib.Opaque(Color.white, 0.12f, 0f, ProcTex.Ground("slide", new Color(0.4f, 0.36f, 0.31f), new Color(0.6f, 0.55f, 0.48f), 9f, 0.45f, 31), Vector2.one * 2f);
            var t = new GameObject("Landslides").transform; t.SetParent(root, false);
            foreach (var r in new[] { slideA, slideB })
            {
                for (int k = 0; k < 26; k++)
                {
                    int i = rnd.Next(r[0], r[1]);
                    Vector3 f = RoadDir(i), right = new Vector3(f.z, 0, -f.x);
                    // debris lies mostly on the uphill half of the road, leaving one lane open
                    float up = TerrainH(road[i] + right * (halfW[i] + 6f)) > TerrainH(road[i] - right * (halfW[i] + 6f)) ? 1f : -1f;
                    float off = up * (0.6f + (float)rnd.NextDouble() * (halfW[i] - 0.6f));
                    Vector3 p = road[i] + right * off;
                    float s = k % 6 == 0 ? 0.9f + (float)rnd.NextDouble() * 0.6f : 0.25f + (float)rnd.NextDouble() * 0.4f;
                    Boulder(t, p, s, rockM, rnd, true);
                }
            }
        }

        // ================================================================== places
        static Zone YardZone(Transform parent, string id, Vector3 c, float yaw, float w, float l, Color col)
        {
            var z = Zone.Create(parent, id, c, new Vector3(w, 4f, l), yaw);
            var vis = new GameObject("Mark").transform; vis.SetParent(parent, false);
            MarkBox(vis, c, yaw, w, l, col);
            WorldAnchors.Zones[id] = z;
            return z;
        }

        static void BuildDepot(System.Random rnd)
        {
            var places = new GameObject("Depot").transform; places.SetParent(root, false);
            var vis = new GameObject("Visual").transform; vis.SetParent(places, false);
            var concrete = MatLib.Opaque(new Color(0.62f, 0.62f, 0.6f), 0.1f, 0f);
            var tin = MatLib.Opaque(new Color(0.55f, 0.58f, 0.6f), 0.6f, 0.7f);
            var crateM = MatLib.Opaque(new Color(0.6f, 0.45f, 0.25f), 0.1f, 0f);
            var sackM = MatLib.Opaque(new Color(0.85f, 0.82f, 0.74f), 0.1f, 0f);
            Vector3 d0 = road[0];
            Vector3 dir = RoadDir(4);
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            depotYaw = yaw;
            var q = Quaternion.Euler(0, yaw, 0);
            Vector3 c = depotCenter; c.y = d0.y - 0.25f;
            var pad = MeshUtil.Part(places, PrimitiveType.Cube, c + Vector3.down * 0.3f, new Vector3(64f, 0.62f, 64f), new Vector3(0, yaw, 0), concrete, "DepotYard");
            pad.AddComponent<BoxCollider>(); pad.AddComponent<SurfaceZone>().kind = SurfaceKind.Asphalt;
            WorldProps.Shed(vis, places, c + q * new Vector3(-18f, 0.01f, 14f), yaw, 18f, 10f, 6f, MatLib.Opaque(new Color(0.85f, 0.82f, 0.72f), 0.1f, 0f), tin);
            Vector3 bay = c + q * new Vector3(14f, 0f, 10f);
            YardZone(places, "depot", bay, yaw, 6f, 11f, new Color(1f, 0.85f, 0.1f));
            for (int i = 0; i < 24; i++)
            {
                var off = new Vector3(5.6f + (i % 3) * 0.7f, 0.25f + (i / 12) * 0.5f, -4f + (i / 3 % 4) * 1.6f);
                MeshUtil.Part(vis, PrimitiveType.Cube, bay + q * off, new Vector3(0.6f, 0.45f, 0.45f), new Vector3(0, yaw, 0), i % 2 == 0 ? crateM : sackM, "Goods");
            }
            WorldProps.Collider(places, bay + q * new Vector3(6.3f, 0.5f, 0f), new Vector3(2.2f, 1f, 6.5f), yaw);
            WorldProps.Sign(places, bay + q * new Vector3(-4f, 0, -6.5f), yaw + 180f, "GHAT DEPOT\nLOAD / UNLOAD", new Color(0.95f, 0.78f, 0.1f), new Color(0.1f, 0.1f, 0.1f));
            MeshUtil.Part(vis, PrimitiveType.Cube, c + q * new Vector3(-20f, 1f, -16f), new Vector3(0.8f, 2f, 0.6f), new Vector3(0, yaw, 0), MatLib.Opaque(new Color(0.85f, 0.15f, 0.1f), 0.5f, 0.1f), "FuelPump");
            WorldProps.Collider(places, c + q * new Vector3(-20f, 1f, -16f), new Vector3(0.8f, 2f, 0.6f), yaw);
            MeshUtil.CombineByMaterial(vis.gameObject);

            Spawn = c + q * new Vector3(0f, 0.6f, -10f);
            SpawnRot = q;
            WorldAnchors.Spawns["depot"] = new SpawnPoint(Spawn, SpawnRot);
            ImplementFactory.Create("trolley_single", c + q * new Vector3(-7f, 0.7f, 6f), q).transform.SetParent(root, true);
            ImplementFactory.Create("trolley_tandem", c + q * new Vector3(4f, 0.7f, 6f), q).transform.SetParent(root, true);
        }

        static void BuildDhaba()
        {
            var places = new GameObject("Dhaba").transform; places.SetParent(root, false);
            var vis = new GameObject("Visual").transform; vis.SetParent(places, false);
            float yaw = YawAt(dhabaIdx);
            var q = Quaternion.Euler(0, yaw, 0);
            Vector3 c = dhabaPos;
            var gravel = MatLib.Opaque(Color.white, 0.08f, 0f, ProcTex.Gravel(), Vector2.one * 4f);
            // retaining wall + gravel pad hanging over the valley side
            var pad = MeshUtil.Part(places, PrimitiveType.Cube, c + Vector3.down * 2.75f, new Vector3(14f, 5.5f, 30f), new Vector3(0, yaw, 0), gravel, "DhabaPad");
            pad.AddComponent<BoxCollider>(); pad.AddComponent<SurfaceZone>().kind = SurfaceKind.Dirt;
            var wall = MatLib.Opaque(new Color(0.88f, 0.7f, 0.42f), 0.1f, 0f);
            var roof = MatLib.Opaque(new Color(0.15f, 0.45f, 0.6f), 0.3f, 0.2f);
            Vector3 right = q * Vector3.right;
            float away = Vector3.Dot(c - road[dhabaIdx], right) > 0f ? 1f : -1f;
            Vector3 hut = c + right * away * 4f + q * new Vector3(0, 0, 8f);
            MeshUtil.Part(vis, PrimitiveType.Cube, hut + Vector3.up * 1.4f, new Vector3(5f, 2.8f, 8f), new Vector3(0, yaw, 0), wall, "DhabaHut");
            MeshUtil.Part(vis, PrimitiveType.Cube, hut + Vector3.up * 3f, new Vector3(6.5f, 0.15f, 9.5f), new Vector3(0, yaw, 4f * away), roof, "DhabaRoof");
            WorldProps.Collider(places, hut + Vector3.up * 1.4f, new Vector3(5f, 2.8f, 8f), yaw);
            for (int i = 0; i < 3; i++)   // charpais
                MeshUtil.Part(vis, PrimitiveType.Cube, c + right * away * 0.5f + q * new Vector3(0, 0.45f, -2f - i * 2.6f), new Vector3(1f, 0.12f, 2f), new Vector3(0, yaw + 90f, 0), MatLib.Opaque(new Color(0.55f, 0.38f, 0.2f), 0.1f, 0f), "Charpai");
            WorldProps.Sign(places, c + q * new Vector3(0, 0, 14f) - right * away * 1f, yaw + 180f, "SHER-E-PAHAD\nDHABA\nchai · paratha", new Color(0.8f, 0.15f, 0.1f), Color.white);
            Vector3 yard = c - right * away * 1.5f + q * new Vector3(0, 0, -6f);
            YardZone(places, "dhaba", yard, yaw, 6f, 11f, new Color(1f, 0.85f, 0.1f));
            MeshUtil.CombineByMaterial(vis.gameObject);
            WorldAnchors.Spawns["dhaba"] = new SpawnPoint(road[dhabaIdx] + Vector3.up * 0.6f, q);
        }

        static void BuildSummit(System.Random rnd)
        {
            var places = new GameObject("Pahadgaon").transform; places.SetParent(root, false);
            var vis = new GameObject("Visual").transform; vis.SetParent(places, false);
            var stone = MatLib.Opaque(new Color(0.55f, 0.52f, 0.48f), 0.1f, 0f, ProcTex.Ground("stonewall", new Color(0.45f, 0.43f, 0.4f), new Color(0.65f, 0.62f, 0.58f), 18f, 0.5f, 27), new Vector2(2, 1));
            var slate = MatLib.Opaque(new Color(0.3f, 0.32f, 0.35f), 0.3f, 0.1f);
            var wood = MatLib.Opaque(new Color(0.42f, 0.28f, 0.16f), 0.1f, 0f);
            var appleM = MatLib.Opaque(new Color(0.7f, 0.15f, 0.1f), 0.3f, 0f);
            Vector3 top = summitCenter;
            float tyaw = summitYaw;
            var tq = Quaternion.Euler(0, tyaw, 0);
            var vpad = MeshUtil.Part(places, PrimitiveType.Cube, new Vector3(top.x, top.y - 0.55f, top.z), new Vector3(56f, 0.62f, 56f), new Vector3(0, tyaw, 0),
                MatLib.Opaque(Color.white, 0.05f, 0f, ProcTex.Ground("villageground", new Color(0.48f, 0.4f, 0.3f), new Color(0.6f, 0.52f, 0.4f), 6f, 0.3f, 28), Vector2.one * 8f), "VillageGround");
            vpad.AddComponent<BoxCollider>(); vpad.AddComponent<SurfaceZone>().kind = SurfaceKind.Dirt;
            Vector3 yard = top + tq * new Vector3(8f, -0.25f, 6f);
            YardZone(places, "summit", yard, tyaw, 9f, 12f, new Color(0.2f, 0.85f, 0.3f));
            WorldProps.Sign(places, yard + tq * new Vector3(-6f, 0, -7f), tyaw + 180f, "PAHADGAON\nMANDI\nload / unload", new Color(0.1f, 0.35f, 0.18f), Color.white);
            for (int i = 0; i < 30; i++)   // apple crates waiting for the descent
                MeshUtil.Part(vis, PrimitiveType.Cube, yard + tq * new Vector3(6f + (i % 3) * 0.7f, 0.25f + (i / 15) * 0.5f, -5f + (i / 3 % 5) * 1.5f), new Vector3(0.6f, 0.45f, 0.45f), new Vector3(0, tyaw, 0), appleM, "AppleCrate");
            WorldProps.Collider(places, yard + tq * new Vector3(6.7f, 0.5f, -2f), new Vector3(2.2f, 1f, 7.5f), tyaw);
            for (int i = 0; i < 8; i++)
            {
                float a = (i / 8f) * Mathf.PI * 1.5f + 2.2f;
                Vector3 hp = top + new Vector3(Mathf.Cos(a) * 36f, 0f, Mathf.Sin(a) * 36f);
                hp.y = TerrainH(hp);
                float hy = (float)rnd.NextDouble() * 360f;
                var hq = Quaternion.Euler(0, hy, 0);
                MeshUtil.Part(vis, PrimitiveType.Cube, hp + Vector3.up * 1.6f, new Vector3(6f, 3.2f, 5f), new Vector3(0, hy, 0), stone, "StoneHouse");
                MeshUtil.Part(vis, PrimitiveType.Cube, hp + hq * new Vector3(-1.5f, 3.75f, 0), new Vector3(3.6f, 0.15f, 5.6f), new Vector3(0, hy, 28f), slate, "Roof");
                MeshUtil.Part(vis, PrimitiveType.Cube, hp + hq * new Vector3(1.5f, 3.75f, 0), new Vector3(3.6f, 0.15f, 5.6f), new Vector3(0, hy, -28f), slate, "Roof");
                MeshUtil.Part(vis, PrimitiveType.Cube, hp + hq * new Vector3(0, 1f, -2.52f), new Vector3(1f, 2f, 0.05f), new Vector3(0, hy, 0), wood, "Door");
                WorldProps.Collider(places, hp + Vector3.up * 1.6f, new Vector3(6f, 3.2f, 5f), hy);
            }
            Vector3 fp = top + new Vector3(-14f, 0, 16f); fp.y = TerrainH(fp);
            MeshUtil.Part(vis, PrimitiveType.Cylinder, fp + Vector3.up * 4f, new Vector3(0.15f, 4f, 0.15f), Vector3.zero, wood, "FlagPole");
            Color[] flags = { Color.blue, Color.white, Color.red, Color.green, Color.yellow };
            for (int i = 0; i < 10; i++)
                MeshUtil.Part(vis, PrimitiveType.Cube, fp + new Vector3(0.6f + i * 0.9f, 7.5f - i * 0.55f, 0), new Vector3(0.6f, 0.45f, 0.02f), Vector3.zero, MatLib.Opaque(flags[i % 5], 0.2f, 0f), "PrayerFlag");
            MeshUtil.CombineByMaterial(vis.gameObject);

            // the descent missions start here: tractor and trolleys at the top
            var back = Quaternion.Euler(0, tyaw + 180f, 0);
            WorldAnchors.Spawns["summit"] = new SpawnPoint(top + tq * new Vector3(-8f, 0.6f, 12f), back);
            ImplementFactory.Create("trolley_single", top + tq * new Vector3(-14f, 0.7f, 0f), back).transform.SetParent(root, true);
            ImplementFactory.Create("trolley_tandem", top + tq * new Vector3(-4f, 0.7f, 0f), back).transform.SetParent(root, true);
        }

        static void MarkBox(Transform vis, Vector3 center, float yaw, float w, float l, Color c)
        {
            var m = MatLib.Emissive(c, c * 0.35f);
            var q = Quaternion.Euler(0, yaw, 0);
            for (int s = -1; s <= 1; s += 2)
            {
                MeshUtil.Part(vis, PrimitiveType.Cube, center + q * new Vector3(s * w * 0.5f, 0.06f, 0), new Vector3(0.2f, 0.05f, l), new Vector3(0, yaw, 0), m, "Line");
                MeshUtil.Part(vis, PrimitiveType.Cube, center + q * new Vector3(0, 0.06f, s * l * 0.5f), new Vector3(w, 0.05f, 0.2f), new Vector3(0, yaw, 0), m, "Line");
            }
        }

        // ================================================================== clouds in the valleys
        static void BuildClouds()
        {
            var clouds = new GameObject("ValleyClouds").transform; clouds.SetParent(root, false);
            var mat = MatLib.Particle(ProcTex.SoftCircle());
            Vector3[] centres = { new Vector3(Cx - 330f, 70f, 520f), new Vector3(Cx + 330f, 80f, 520f), new Vector3(Cx, 45f, 80f) };
            Vector3[] sizes = { new Vector3(160f, 30f, 700f), new Vector3(160f, 30f, 700f), new Vector3(900f, 20f, 120f) };
            for (int i = 0; i < centres.Length; i++)
            {
                var go = new GameObject("Cloudbank"); go.transform.SetParent(clouds, false);
                go.transform.position = centres[i];
                var ps = go.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main;
                main.loop = true; main.prewarm = true;
                main.startLifetime = new ParticleSystem.MinMaxCurve(50f, 80f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.2f);
                main.startSize = new ParticleSystem.MinMaxCurve(45f, 95f);
                main.startColor = new Color(1f, 1f, 1f, 0.32f);
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.maxParticles = 70;
                var em = ps.emission; em.rateOverTime = 1.1f;
                var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = sizes[i];
                var col = ps.colorOverLifetime; col.enabled = true;
                var grad = new Gradient();
                grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                             new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
                col.color = new ParticleSystem.MinMaxGradient(grad);
                var r = go.GetComponent<ParticleSystemRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                ps.Play();
            }
        }

        // ================================================================== mission routes
        static void RegisterRoutes()
        {
            var up = new List<Waypoint>();
            var down = new List<Waypoint>();
            var dhaba = new List<Waypoint>();
            for (int i = 240; i < road.Count - 60; i += 360)
            {
                up.Add(new Waypoint(road[i], YawAt(i), halfW[i] * 2f + 3f));
                if (i < dhabaIdx - 60) dhaba.Add(new Waypoint(road[i], YawAt(i), halfW[i] * 2f + 3f));
            }
            for (int i = road.Count - 240; i > 60; i -= 360)
                down.Add(new Waypoint(road[i], YawAt(i) + 180f, halfW[i] * 2f + 3f));
            WorldAnchors.Routes["ghat_up"] = up;
            WorldAnchors.Routes["ghat_down"] = down;
            WorldAnchors.Routes["ghat_dhaba"] = dhaba;
        }

        public static float RouteLengthKm { get { return road != null ? road.Count / 1000f : 0f; } }

        /// <summary>Places that must be known before the terrain is carved.</summary>
        static void PlanPlaces()
        {
            Vector3 dir = RoadDir(4);
            depotCenter = road[0] - dir * 34f; depotCenter.y = road[0].y;
            Vector3 end = road[road.Count - 1], tdir = RoadDir(road.Count - 3);
            summitCenter = end + tdir * 30f; summitCenter.y = end.y;
            summitYaw = Mathf.Atan2(tdir.x, tdir.z) * Mathf.Rad2Deg;
            // dhaba on the valley (lower) side of leg 5
            Vector3 f = RoadDir(dhabaIdx), right = new Vector3(f.z, 0, -f.x);
            float s = right.z < 0f ? 1f : -1f;        // legs stack northwards, so south is downhill
            dhabaPos = road[dhabaIdx] + right * s * (halfW[dhabaIdx] + 7f);
            dhabaPos.y = road[dhabaIdx].y;
        }
    }
}
