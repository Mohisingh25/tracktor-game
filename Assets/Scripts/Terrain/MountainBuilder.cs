using System.Collections.Generic;
using UnityEngine;

namespace TractorSim
{
    /// <summary>
    /// "Himalayan Cargo Route": a 1.2 km x 1.2 km Unity Terrain with ridged mountains and a
    /// ~3 km ghat road of switchbacks and hairpins, cut into the slope with embankments and
    /// cliff drops. Gravel road with mud, rocky and river-ford sections; deodar forest, boulders,
    /// guard stones, milestones, warning signs; a depot at the bottom and a hill village at the top.
    /// </summary>
    public static class MountainBuilder
    {
        const float Size = 1200f, Height = 480f;
        const int Res = 513;
        const float RoadHalf = 3.4f;

        public static Vector3 Spawn; public static Quaternion SpawnRot;
        public static CargoMission Mission;

        static Transform root;
        static float[,] natural;     // metres
        static List<Vector3> road;   // centreline samples (x, roadHeight, z) every ~1 m
        static List<float> roadS;

        public static Transform Build()
        {
            root = new GameObject("MountainWorld").transform;
            SurfaceZone.ClearCache();
            var rnd = new System.Random(2026);

            BuildNatural();
            BuildRoadPath();
            var terrain = BuildTerrain();
            BuildRoadMesh();
            BuildForest(rnd);
            BuildRoadside(rnd);
            BuildDepotAndVillage(rnd);
            BuildMission();
            return root;
        }

        // ------------------------------------------------------------------ heights
        static float Ridged(float x, float z, float f)
        {
            float n = Mathf.PerlinNoise(x * f + 31.7f, z * f + 11.3f);
            n = 1f - Mathf.Abs(n * 2f - 1f);
            return n * n;
        }

        static void BuildNatural()
        {
            natural = new float[Res, Res];
            for (int iz = 0; iz < Res; iz++)
                for (int ix = 0; ix < Res; ix++)
                {
                    float x = ix / (float)(Res - 1) * Size, z = iz / (float)(Res - 1) * Size;
                    float north = Mathf.Clamp01(z / Size);
                    float ramp = 18f + 235f * Mathf.Pow(north, 1.15f);
                    float ridges = Ridged(x, z, 0.0042f) * 70f + Ridged(x, z, 0.011f) * 22f;
                    float detail = (Mathf.PerlinNoise(x * 0.03f + 5f, z * 0.03f + 9f) - 0.5f) * 8f;
                    // peaks around the edges, a valley along the west
                    float edge = Mathf.Max(0f, Mathf.Abs(x - Size * 0.5f) / (Size * 0.5f) - 0.55f) * 160f;
                    natural[iz, ix] = ramp + ridges * (0.4f + north) + detail + edge;
                }
        }

        static float NaturalAt(float x, float z)
        {
            float fx = Mathf.Clamp(x / Size * (Res - 1), 0, Res - 1.001f), fz = Mathf.Clamp(z / Size * (Res - 1), 0, Res - 1.001f);
            int x0 = (int)fx, z0 = (int)fz; float tx = fx - x0, tz = fz - z0;
            float a = Mathf.Lerp(natural[z0, x0], natural[z0, x0 + 1], tx);
            float b = Mathf.Lerp(natural[z0 + 1, x0], natural[z0 + 1, x0 + 1], tx);
            return Mathf.Lerp(a, b, tz);
        }

        // ------------------------------------------------------------------ road
        static readonly Vector2[] ControlPoints =
        {
            new Vector2(170, 120), new Vector2(260, 140), new Vector2(470, 180), new Vector2(530, 250),   // valley run & first hairpin
            new Vector2(470, 300), new Vector2(260, 330), new Vector2(205, 400),                        // back west, hairpin 2
            new Vector2(260, 450), new Vector2(560, 470), new Vector2(690, 540), new Vector2(660, 610),  // long climb east
            new Vector2(420, 645), new Vector2(370, 715),                                               // hairpin 4
            new Vector2(450, 760), new Vector2(760, 780), new Vector2(830, 860), new Vector2(760, 930),  // cliff section
            new Vector2(640, 960), new Vector2(660, 1030), new Vector2(820, 1060), new Vector2(980, 1050)   // final pitch to the village
        };

        static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        static void BuildRoadPath()
        {
            // dense spline
            var pts = new List<Vector2>();
            var cp = ControlPoints;
            for (int i = 0; i < cp.Length - 1; i++)
            {
                Vector2 p0 = cp[Mathf.Max(0, i - 1)], p1 = cp[i], p2 = cp[i + 1], p3 = cp[Mathf.Min(cp.Length - 1, i + 2)];
                int steps = Mathf.CeilToInt(Vector2.Distance(p1, p2) * 2f);
                for (int s = 0; s < steps; s++) pts.Add(CatmullRom(p0, p1, p2, p3, s / (float)steps));
            }
            pts.Add(cp[cp.Length - 1]);
            // resample every 1 m
            var res = new List<Vector2> { pts[0] };
            float acc = 0f;
            for (int i = 1; i < pts.Count; i++)
            {
                float d = Vector2.Distance(pts[i], pts[i - 1]);
                acc += d;
                if (acc >= 1f) { res.Add(pts[i]); acc = 0f; }
            }
            // height profile: follow the smoothed natural ground but never steeper than the limit
            int n = res.Count;
            var nat = new float[n];
            for (int i = 0; i < n; i++) nat[i] = NaturalAt(res[i].x, res[i].y);
            var sm = new float[n];
            for (int i = 0; i < n; i++)
            {
                float sum = 0f; int c = 0;
                for (int k = -40; k <= 40; k++) { int j = Mathf.Clamp(i + k, 0, n - 1); sum += nat[j]; c++; }
                sm[i] = sum / c;
            }
            var h = new float[n];
            h[0] = sm[0];
            for (int i = 1; i < n; i++)
            {
                float s = i;
                float gmax = (s > 2250f && s < 2420f) ? 0.21f : 0.15f;        // one brutal pitch near the top
                h[i] = Mathf.Clamp(sm[i], h[i - 1] - gmax, h[i - 1] + gmax);
            }
            // ford: a dip into the stream bed in the valley
            for (int i = 0; i < n; i++)
            {
                float d = Mathf.Abs(i - 330f);
                if (d < 22f) h[i] -= 0.75f * Mathf.Cos(d / 22f * Mathf.PI * 0.5f);
            }
            // light smoothing so grade changes are gentle
            var h2 = new float[n];
            for (int pass = 0; pass < 3; pass++)
            {
                for (int i = 0; i < n; i++)
                {
                    float sum = 0f; int c = 0;
                    for (int k = -6; k <= 6; k++) { int j = Mathf.Clamp(i + k, 0, n - 1); sum += h[j]; c++; }
                    h2[i] = sum / c;
                }
                System.Array.Copy(h2, h, n);
            }
            road = new List<Vector3>(n); roadS = new List<float>(n);
            for (int i = 0; i < n; i++) { road.Add(new Vector3(res[i].x, h[i], res[i].y)); roadS.Add(i); }
        }

        // ------------------------------------------------------------------ terrain
        static Terrain BuildTerrain()
        {
            // carve: road bed, shoulders, flat depot and village
            var final = (float[,])natural.Clone();
            var dist = new float[Res, Res];
            var rh = new float[Res, Res];
            for (int iz = 0; iz < Res; iz++) for (int ix = 0; ix < Res; ix++) dist[iz, ix] = 1e9f;
            float cell = Size / (Res - 1);
            const float Shoulder = 16f;
            int rad = Mathf.CeilToInt((RoadHalf + Shoulder) / cell) + 1;
            for (int i = 0; i < road.Count; i += 1)
            {
                var p = road[i];
                int cx = Mathf.RoundToInt(p.x / cell), cz = Mathf.RoundToInt(p.z / cell);
                for (int dz = -rad; dz <= rad; dz++)
                    for (int dx = -rad; dx <= rad; dx++)
                    {
                        int x = cx + dx, z = cz + dz;
                        if (x < 0 || z < 0 || x >= Res || z >= Res) continue;
                        float wx = x * cell, wz = z * cell;
                        float d = Mathf.Sqrt((wx - p.x) * (wx - p.x) + (wz - p.z) * (wz - p.z));
                        if (d < dist[z, x]) { dist[z, x] = d; rh[z, x] = p.y; }
                    }
            }
            Vector3 depot = road[0], top = road[road.Count - 1];
            for (int iz = 0; iz < Res; iz++)
                for (int ix = 0; ix < Res; ix++)
                {
                    float wx = ix * cell, wz = iz * cell;
                    float h = natural[iz, ix];
                    float d = dist[iz, ix];
                    if (d < RoadHalf + Shoulder)
                    {
                        float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(RoadHalf + 1.5f, RoadHalf + Shoulder, d));
                        h = Mathf.Lerp(rh[iz, ix] - 0.25f, h, t);
                    }
                    h = Flatten(h, wx, wz, depot, 55f, 18f);
                    h = Flatten(h, wx, wz, top + new Vector3(30f, 0, 10f), 60f, 18f);
                    final[iz, ix] = h;
                }
            // stream bed crossing the ford
            var ford = road[330];
            for (int iz = 0; iz < Res; iz++)
                for (int ix = 0; ix < Res; ix++)
                {
                    float wx = ix * cell, wz = iz * cell;
                    // stream runs roughly north-south through the ford
                    float dx = Mathf.Abs(wx - ford.x - (wz - ford.z) * 0.15f);
                    if (dx < 9f && Mathf.Abs(wz - ford.z) < 140f) final[iz, ix] = Mathf.Min(final[iz, ix], ford.y - 0.9f + dx * 0.05f);
                }

            var td = new TerrainData();
            td.heightmapResolution = Res;
            td.size = new Vector3(Size, Height, Size);
            var norm = new float[Res, Res];
            for (int iz = 0; iz < Res; iz++) for (int ix = 0; ix < Res; ix++) norm[iz, ix] = Mathf.Clamp01(final[iz, ix] / Height);
            td.SetHeights(0, 0, norm);

            // ---- texture layers: grass, dirt, rock, mud, gravel ----
            var cat = ArtCatalog.Load();
            td.terrainLayers = new[]
            {
                Layer(cat != null ? cat.grass : null, ProcTex.Ground("mgrass", new Color(0.26f, 0.38f, 0.16f), new Color(0.45f, 0.5f, 0.26f), 7f, 0.3f, 21), 5f),
                Layer(cat != null ? cat.dirt : null, ProcTex.Ground("mdirt", new Color(0.45f, 0.36f, 0.26f), new Color(0.6f, 0.5f, 0.38f), 6f, 0.35f, 22), 4f),
                Layer(cat != null ? cat.rock : null, ProcTex.Ground("mrock", new Color(0.4f, 0.39f, 0.37f), new Color(0.62f, 0.6f, 0.57f), 10f, 0.45f, 23), 8f),
                Layer(cat != null ? cat.mud : null, ProcTex.Ground("mmud", new Color(0.2f, 0.15f, 0.09f), new Color(0.33f, 0.25f, 0.15f), 5f, 0.2f, 24), 4f),
            };
            int A = 512;
            td.alphamapResolution = A;
            var alpha = new float[A, A, 4];
            for (int az = 0; az < A; az++)
                for (int ax = 0; ax < A; ax++)
                {
                    float u = ax / (float)(A - 1), v = az / (float)(A - 1);
                    float steep = td.GetSteepness(u, v);
                    float hgt = td.GetInterpolatedHeight(u, v);
                    int hx = Mathf.RoundToInt(u * (Res - 1)), hz = Mathf.RoundToInt(v * (Res - 1));
                    float d = dist[hz, hx];
                    float rock = Mathf.InverseLerp(30f, 42f, steep) + Mathf.InverseLerp(250f, 300f, hgt) * 0.5f;
                    float dirt = d < RoadHalf + 4f ? 1f : (d < RoadHalf + 9f ? 0.5f : 0.1f + Mathf.PerlinNoise(u * 40f, v * 40f) * 0.35f);
                    float mud = 0f;
                    float wx = u * Size, wz = v * Size;
                    float fdx = Mathf.Abs(wx - ford.x - (wz - ford.z) * 0.15f);
                    if (fdx < 12f && Mathf.Abs(wz - ford.z) < 140f) mud = 1.5f;
                    float grass = 1f;
                    float sum = grass + dirt + rock + mud;
                    alpha[az, ax, 0] = grass / sum; alpha[az, ax, 1] = dirt / sum; alpha[az, ax, 2] = rock / sum; alpha[az, ax, 3] = mud / sum;
                }
            td.SetAlphamaps(0, 0, alpha);

            // ---- grass detail near the road ----
            var bladeTex = cat != null && cat.grassBlades != null ? cat.grassBlades : ProcTex.GrassBlades();
            var dp = new DetailPrototype();
            dp.prototypeTexture = bladeTex;
            dp.renderMode = DetailRenderMode.GrassBillboard;
            dp.usePrototypeMesh = false;
            dp.minWidth = 0.5f; dp.maxWidth = 0.9f; dp.minHeight = 0.35f; dp.maxHeight = 0.75f;
            dp.healthyColor = new Color(0.55f, 0.7f, 0.35f); dp.dryColor = new Color(0.75f, 0.68f, 0.42f);
            dp.noiseSpread = 0.4f;
            td.detailPrototypes = new[] { dp };
            int D = 512;
            td.SetDetailResolution(D, 16);
            var det = new int[D, D];
            for (int dz = 0; dz < D; dz++)
                for (int dx = 0; dx < D; dx++)
                {
                    int hx = Mathf.RoundToInt(dx / (float)(D - 1) * (Res - 1)), hz = Mathf.RoundToInt(dz / (float)(D - 1) * (Res - 1));
                    float d = dist[hz, hx];
                    if (d < RoadHalf + 2.5f || d > 90f) continue;
                    float steep = td.GetSteepness(dx / (float)(D - 1), dz / (float)(D - 1));
                    if (steep > 33f) continue;
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
            ts.Init(terrain, new[] { SurfaceKind.Grass, SurfaceKind.Dirt, SurfaceKind.Rock, SurfaceKind.Mud });
            terrainRef = terrain;
            return terrain;
        }

        static Terrain terrainRef;
        public static Terrain ActiveTerrain { get { return terrainRef; } }

        static float Flatten(float h, float x, float z, Vector3 c, float r, float blend)
        {
            float d = Vector2.Distance(new Vector2(x, z), new Vector2(c.x, c.z));
            if (d > r + blend) return h;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(r, r + blend, d));
            return Mathf.Lerp(c.y - 0.2f, h, t);
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

        // ------------------------------------------------------------------ road surface mesh
        static void BuildRoadMesh()
        {
            var cat = ArtCatalog.Load();
            var gravel = MatLib.Opaque(Color.white, 0.08f, 0f, ArtCatalog.Has(cat != null ? cat.gravelRoad : null) ? cat.gravelRoad.albedo : ProcTex.Gravel(), Vector2.one);
            var mud = MatLib.Opaque(Color.white, 0.55f, 0f, ArtCatalog.Has(cat != null ? cat.mud : null) ? cat.mud.albedo : ProcTex.Ground("mud", new Color(0.2f, 0.14f, 0.08f), new Color(0.34f, 0.25f, 0.15f), 4f, 0.2f, 3), Vector2.one);
            var rocky = MatLib.Opaque(Color.white, 0.12f, 0f, ProcTex.Ground("rockroad", new Color(0.38f, 0.36f, 0.33f), new Color(0.58f, 0.55f, 0.5f), 14f, 0.5f, 25), Vector2.one);
            var roadRoot = new GameObject("GhatRoad").transform; roadRoot.SetParent(root, false);
            const int Chunk = 40;
            for (int start = 0; start < road.Count - 1; start += Chunk)
            {
                int end = Mathf.Min(road.Count - 1, start + Chunk);
                float mid = (start + end) * 0.5f;
                SurfaceKind kind = SurfaceKind.Dirt; Material m = gravel;
                if ((mid > 760f && mid < 860f) || (mid > 1700f && mid < 1780f)) { kind = SurfaceKind.Mud; m = mud; }
                else if (mid > 1350f && mid < 1520f) { kind = SurfaceKind.Rock; m = rocky; }
                else if (mid > 310f && mid < 350f) { kind = SurfaceKind.Water; m = mud; }
                RoadChunk(roadRoot, start, end, m, kind);
            }
            // water over the ford
            var ford = road[330];
            Vector3 dir = (road[340] - road[320]).normalized;
            var wtr = MeshUtil.Part(roadRoot, PrimitiveType.Cube, ford + Vector3.up * 0.45f, new Vector3(18f, 0.02f, 280f), new Vector3(0, Mathf.Atan2(0.15f, 1f) * Mathf.Rad2Deg, 0),
                MatLib.Transparent(new Color(0.22f, 0.34f, 0.36f, 0.72f), 0.96f), "Stream");
            wtr.transform.position = new Vector3(ford.x, ford.y + 0.35f, ford.z);
            wtr.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        static void RoadChunk(Transform parent, int a, int b, Material m, SurfaceKind kind)
        {
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            for (int i = a; i <= b; i++)
            {
                Vector3 p = road[i];
                Vector3 fwd = (road[Mathf.Min(road.Count - 1, i + 1)] - road[Mathf.Max(0, i - 1)]); fwd.y = 0; fwd.Normalize();
                Vector3 right = new Vector3(fwd.z, 0, -fwd.x);
                // widen in tight bends so a trolley can follow round the hairpin
                float curv = Curvature(i);
                float half = RoadHalf + Mathf.Clamp(curv * 120f, 0f, 2.6f);
                // crown & camber
                Vector3 l = p - right * half, r = p + right * half;
                verts.Add(l + Vector3.down * 0.35f); uvs.Add(new Vector2(-0.15f, i * 0.25f));       // skirt
                verts.Add(l + Vector3.up * 0.04f); uvs.Add(new Vector2(0f, i * 0.25f));
                verts.Add(p + Vector3.up * 0.08f); uvs.Add(new Vector2(0.5f, i * 0.25f));
                verts.Add(r + Vector3.up * 0.04f); uvs.Add(new Vector2(1f, i * 0.25f));
                verts.Add(r + Vector3.down * 0.35f); uvs.Add(new Vector2(1.15f, i * 0.25f));
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
            var go = new GameObject("RoadChunk_" + a);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = m;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            go.AddComponent<SurfaceZone>().kind = kind;
        }

        static float Curvature(int i)
        {
            int a = Mathf.Max(0, i - 6), b = Mathf.Min(road.Count - 1, i + 6);
            Vector3 d1 = road[i] - road[a]; d1.y = 0; Vector3 d2 = road[b] - road[i]; d2.y = 0;
            if (d1.sqrMagnitude < 0.01f || d2.sqrMagnitude < 0.01f) return 0f;
            float ang = Vector3.Angle(d1, d2) * Mathf.Deg2Rad;
            return ang / Mathf.Max(1f, (b - a));
        }

        static Vector3 RoadDir(int i)
        {
            Vector3 f = road[Mathf.Min(road.Count - 1, i + 2)] - road[Mathf.Max(0, i - 2)]; f.y = 0; return f.normalized;
        }

        static float DistToRoad(Vector3 p, int step = 4)
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

        // ------------------------------------------------------------------ vegetation
        static void BuildForest(System.Random rnd)
        {
            var trunk = MatLib.Opaque(new Color(0.28f, 0.2f, 0.14f), 0.05f, 0f);
            var needles = MatLib.Opaque(new Color(0.12f, 0.26f, 0.14f), 0.08f, 0f);
            var needles2 = MatLib.Opaque(new Color(0.17f, 0.31f, 0.16f), 0.08f, 0f);
            var forest = new GameObject("Forest").transform; forest.SetParent(root, false);
            var cols = new GameObject("TreeColliders").transform; cols.SetParent(forest, false);
            // chunked so far-away chunks can be culled cheaply
            const float ChunkSize = 200f;
            int chunks = Mathf.CeilToInt(Size / ChunkSize);
            var chunkRoots = new Transform[chunks, chunks];
            int placed = 0, guard = 0;
            int target = QualityTrees();
            bool art = ArtCatalog.Load() != null && ArtCatalog.Load().trees != null && ArtCatalog.Load().trees.Length > 0;
            while (placed < target && guard++ < target * 12)
            {
                int i = rnd.Next(road.Count);
                Vector3 rp = road[i];
                float ang = (float)rnd.NextDouble() * Mathf.PI * 2f;
                float r = 9f + (float)Mathf.Pow((float)rnd.NextDouble(), 1.6f) * 180f;
                var p = new Vector3(rp.x + Mathf.Cos(ang) * r, 0, rp.z + Mathf.Sin(ang) * r);
                if (p.x < 5 || p.z < 5 || p.x > Size - 5 || p.z > Size - 5) continue;
                if (DistToRoad(p) < RoadHalf + 6f) continue;
                if (Vector3.Distance(new Vector3(p.x, 0, p.z), new Vector3(road[0].x, 0, road[0].z)) < 70f) continue;
                Vector3 last = road[road.Count - 1] + new Vector3(30f, 0, 10f);
                if (Vector3.Distance(new Vector3(p.x, 0, p.z), new Vector3(last.x, 0, last.z)) < 75f) continue;
                float u = p.x / Size, v = p.z / Size;
                if (terrainRef.terrainData.GetSteepness(u, v) > 34f) continue;
                p.y = TerrainH(p);
                float h = 9f + (float)rnd.NextDouble() * 12f;
                int cx = Mathf.Clamp((int)(p.x / ChunkSize), 0, chunks - 1), cz = Mathf.Clamp((int)(p.z / ChunkSize), 0, chunks - 1);
                if (chunkRoots[cx, cz] == null) { chunkRoots[cx, cz] = new GameObject("Chunk_" + cx + "_" + cz).transform; chunkRoots[cx, cz].SetParent(forest, false); }
                if (!(art && WorldProps.ArtTree(chunkRoots[cx, cz], p, h, rnd)))
                    WorldProps.Conifer(chunkRoots[cx, cz], p, h, rnd, trunk, needles, needles2);
                if (r < 80f) WorldProps.Capsule(cols, p + Vector3.up * 2f, 0.35f, 4f);
                placed++;
            }
            foreach (var c in chunkRoots)
            {
                if (c == null) continue;
                if (!art) MeshUtil.CombineByMaterial(c.gameObject);
                var lod = c.gameObject.AddComponent<LODGroup>();
                lod.SetLODs(new[] { new LOD(0.012f, c.GetComponentsInChildren<Renderer>()) });
                lod.RecalculateBounds();
            }
            // boulders on the slopes and beside the road
            var rockM = MatLib.Opaque(Color.white, 0.12f, 0f, ProcTex.Ground("boulder", new Color(0.38f, 0.37f, 0.35f), new Color(0.6f, 0.58f, 0.55f), 9f, 0.45f, 26), Vector2.one * 2f);
            var rocks = new GameObject("Boulders").transform; rocks.SetParent(root, false);
            for (int k = 0; k < 260; k++)
            {
                int i = rnd.Next(road.Count);
                Vector3 rp = road[i];
                float ang = (float)rnd.NextDouble() * Mathf.PI * 2f;
                float r = 7f + (float)rnd.NextDouble() * 60f;
                var p = new Vector3(rp.x + Mathf.Cos(ang) * r, 0, rp.z + Mathf.Sin(ang) * r);
                if (DistToRoad(p) < RoadHalf + 3f) continue;
                p.y = TerrainH(p);
                float s = 0.6f + (float)Mathf.Pow((float)rnd.NextDouble(), 2f) * 3f;
                var b = new GameObject("Boulder"); b.transform.SetParent(rocks, false);
                b.transform.position = p + Vector3.up * s * 0.15f;
                b.transform.rotation = Quaternion.Euler((float)rnd.NextDouble() * 360f, (float)rnd.NextDouble() * 360f, 0);
                b.transform.localScale = new Vector3(s * 1.3f, s * 0.75f, s);
                b.AddComponent<MeshFilter>().sharedMesh = MeshUtil.PrimMesh(PrimitiveType.Sphere);
                b.AddComponent<MeshRenderer>().sharedMaterial = rockM;
                if (r < 25f) { b.AddComponent<SphereCollider>().radius = 0.5f; b.AddComponent<SurfaceZone>().kind = SurfaceKind.Rock; }
            }
        }

        static int QualityTrees()
        {
            switch (SaveSystem.Settings != null ? SaveSystem.Settings.graphics : 1)
            {
                case 0: return 500;
                case 1: return 1100;
                case 2: return 1800;
                default: return 2600;
            }
        }

        // ------------------------------------------------------------------ roadside furniture
        static void BuildRoadside(System.Random rnd)
        {
            var side = new GameObject("Roadside").transform; side.SetParent(root, false);
            var vis = new GameObject("Visual").transform; vis.SetParent(side, false);
            var white = MatLib.Opaque(new Color(0.94f, 0.94f, 0.92f), 0.2f, 0f);
            var black = MatLib.Opaque(new Color(0.08f, 0.08f, 0.08f), 0.2f, 0f);
            for (int i = 4; i < road.Count - 4; i += 7)
            {
                Vector3 p = road[i], f = RoadDir(i), r = new Vector3(f.z, 0, -f.x);
                float half = RoadHalf + Mathf.Clamp(Curvature(i) * 120f, 0f, 2.6f);
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 edge = p + r * s * (half + 0.6f);
                    Vector3 probe = p + r * s * (half + 5f);
                    float drop = p.y - TerrainH(probe);
                    if (drop < 1.6f) continue;                        // only on the valley side: cliff guard stones
                    edge.y = p.y;
                    MeshUtil.Part(vis, PrimitiveType.Cube, edge + Vector3.up * 0.3f, new Vector3(0.35f, 0.6f, 0.35f), Vector3.zero, white, "GuardStone");
                    MeshUtil.Part(vis, PrimitiveType.Cube, edge + Vector3.up * 0.42f, new Vector3(0.36f, 0.12f, 0.36f), Vector3.zero, black, "Band");
                    WorldProps.Collider(side, edge + Vector3.up * 0.3f, new Vector3(0.35f, 0.6f, 0.35f));
                }
            }
            MeshUtil.CombineByMaterial(vis.gameObject);

            // milestones every 500 m, hairpin warnings where the road bends hard
            int totalKm = Mathf.CeilToInt(road.Count / 1000f);
            for (int i = 250; i < road.Count - 50; i += 500)
            {
                Vector3 f = RoadDir(i), r = new Vector3(f.z, 0, -f.x);
                Vector3 pos = road[i] + r * (RoadHalf + 1.4f); pos.y = TerrainH(pos);
                float remaining = (road.Count - i) / 1000f;
                WorldProps.Milestone(side, pos, Mathf.Atan2(-f.x, -f.z) * Mathf.Rad2Deg, "PAHADGAON", remaining.ToString("0.0") + " km");
            }
            int lastSign = -999;
            for (int i = 30; i < road.Count - 30; i++)
            {
                if (Curvature(i) < 0.045f || i - lastSign < 120) continue;
                lastSign = i;
                int at = Mathf.Max(0, i - 45);
                Vector3 f = RoadDir(at), r = new Vector3(f.z, 0, -f.x);
                Vector3 pos = road[at] + r * (RoadHalf + 2f); pos.y = TerrainH(pos);
                WorldProps.Sign(side, pos, Mathf.Atan2(-f.x, -f.z) * Mathf.Rad2Deg, "HAIRPIN\nBEND\nGo slow", new Color(0.95f, 0.78f, 0.1f), new Color(0.1f, 0.1f, 0.1f));
            }
            // ghat section entry sign
            {
                Vector3 f = RoadDir(40), r = new Vector3(f.z, 0, -f.x);
                Vector3 pos = road[40] + r * (RoadHalf + 2f); pos.y = TerrainH(pos);
                WorldProps.Sign(side, pos, Mathf.Atan2(-f.x, -f.z) * Mathf.Rad2Deg, "GHAT ROAD\nuse low gear", new Color(0.1f, 0.35f, 0.18f), Color.white);
            }
        }

        // ------------------------------------------------------------------ places
        static Vector3 depotPos, villagePos;
        static Zone loadZone, deliveryZone;

        static void BuildDepotAndVillage(System.Random rnd)
        {
            var places = new GameObject("Places").transform; places.SetParent(root, false);
            var vis = new GameObject("Visual").transform; vis.SetParent(places, false);
            var concrete = MatLib.Opaque(new Color(0.62f, 0.62f, 0.6f), 0.1f, 0f);
            var stone = MatLib.Opaque(new Color(0.55f, 0.52f, 0.48f), 0.1f, 0f, ProcTex.Ground("stonewall", new Color(0.45f, 0.43f, 0.4f), new Color(0.65f, 0.62f, 0.58f), 18f, 0.5f, 27), new Vector2(2, 1));
            var slate = MatLib.Opaque(new Color(0.3f, 0.32f, 0.35f), 0.3f, 0.1f);
            var tin = MatLib.Opaque(new Color(0.55f, 0.58f, 0.6f), 0.6f, 0.7f);
            var wood = MatLib.Opaque(new Color(0.42f, 0.28f, 0.16f), 0.1f, 0f);
            var crateM = MatLib.Opaque(new Color(0.6f, 0.45f, 0.25f), 0.1f, 0f);

            // ---------- depot (start) ----------
            Vector3 d0 = road[0];
            Vector3 dir = RoadDir(4);
            depotPos = d0;
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            var q = Quaternion.Euler(0, yaw, 0);
            Vector3 c = d0 - dir * 30f; c.y = d0.y - 0.2f;
            var pad = MeshUtil.Part(places, PrimitiveType.Cube, c + Vector3.down * 0.3f, new Vector3(60f, 0.62f, 60f), new Vector3(0, yaw, 0), concrete, "DepotYard");
            pad.AddComponent<BoxCollider>(); pad.AddComponent<SurfaceZone>().kind = SurfaceKind.Asphalt;
            WorldProps.Shed(vis, places, c + q * new Vector3(-16f, 0.01f, 12f), yaw, 18f, 10f, 6f, MatLib.Opaque(new Color(0.85f, 0.82f, 0.72f), 0.1f, 0f), tin);
            // loading bay with stacked apple crates & cement bags
            Vector3 bay = c + q * new Vector3(12f, 0f, 8f);
            loadZone = Zone.Create(places, "LoadingBay", bay, new Vector3(5f, 4f, 10f), yaw);
            MarkBox(vis, bay, yaw, 5f, 10f, new Color(1f, 0.85f, 0.1f));
            for (int i = 0; i < 24; i++)
            {
                var off = new Vector3(5.2f + (i % 3) * 0.7f, 0.25f + (i / 12) * 0.5f, -4f + (i / 3 % 4) * 1.6f);
                MeshUtil.Part(vis, PrimitiveType.Cube, bay + q * off, new Vector3(0.6f, 0.45f, 0.45f), new Vector3(0, yaw, 0), crateM, "Crate");
            }
            WorldProps.Collider(places, bay + q * new Vector3(6f, 0.5f, 0f), new Vector3(2.2f, 1f, 6.5f), yaw);
            WorldProps.Sign(places, bay + q * new Vector3(-3.5f, 0, -6f), yaw + 180f, "LOADING\nBAY", new Color(0.95f, 0.78f, 0.1f), new Color(0.1f, 0.1f, 0.1f));
            // fuel pump
            MeshUtil.Part(vis, PrimitiveType.Cube, c + q * new Vector3(-18f, 1f, -14f), new Vector3(0.8f, 2f, 0.6f), new Vector3(0, yaw, 0), MatLib.Opaque(new Color(0.85f, 0.15f, 0.1f), 0.5f, 0.1f), "FuelPump");
            WorldProps.Collider(places, c + q * new Vector3(-18f, 1f, -14f), new Vector3(0.8f, 2f, 0.6f), yaw);

            // tractor spawn: parked facing the road
            Spawn = c + q * new Vector3(0f, 0.6f, -8f);
            SpawnRot = q;
            // trolleys parked facing the road: drive past, then reverse back onto the hitch eye
            ImplementFactory.Create("trolley_single", c + q * new Vector3(-6f, 0.7f, 8f), q).transform.SetParent(root, true);
            ImplementFactory.Create("trolley_tandem", c + q * new Vector3(4f, 0.7f, 8f), q).transform.SetParent(root, true);

            // ---------- hill village (finish) ----------
            Vector3 top = road[road.Count - 1] + new Vector3(30f, 0, 10f);
            villagePos = top;
            Vector3 tdir = RoadDir(road.Count - 3);
            float tyaw = Mathf.Atan2(tdir.x, tdir.z) * Mathf.Rad2Deg;
            var tq = Quaternion.Euler(0, tyaw, 0);
            var vpad = MeshUtil.Part(places, PrimitiveType.Cube, new Vector3(top.x, top.y - 0.5f, top.z), new Vector3(46f, 0.62f, 46f), new Vector3(0, tyaw, 0), MatLib.Opaque(Color.white, 0.05f, 0f, ProcTex.Ground("villageground", new Color(0.48f, 0.4f, 0.3f), new Color(0.6f, 0.52f, 0.4f), 6f, 0.3f, 28), Vector2.one * 8f), "VillageGround");
            vpad.AddComponent<BoxCollider>(); vpad.AddComponent<SurfaceZone>().kind = SurfaceKind.Dirt;
            Vector3 yard = top + tq * new Vector3(0f, -0.2f, 4f);
            deliveryZone = Zone.Create(places, "DeliveryYard", yard, new Vector3(9f, 4f, 12f), tyaw);
            MarkBox(vis, yard, tyaw, 9f, 12f, new Color(0.2f, 0.85f, 0.3f));
            WorldProps.Sign(places, yard + tq * new Vector3(-6f, 0, -7f), tyaw + 180f, "PAHADGAON\nMANDI\nUnload here", new Color(0.1f, 0.35f, 0.18f), Color.white);
            for (int i = 0; i < 7; i++)
            {
                float a = (i / 7f) * Mathf.PI * 1.6f + 0.6f;
                Vector3 hp = top + new Vector3(Mathf.Cos(a) * 32f, 0f, Mathf.Sin(a) * 32f);
                hp.y = TerrainH(hp);
                float hy = (float)rnd.NextDouble() * 360f;
                var hq = Quaternion.Euler(0, hy, 0);
                MeshUtil.Part(vis, PrimitiveType.Cube, hp + Vector3.up * 1.6f, new Vector3(6f, 3.2f, 5f), new Vector3(0, hy, 0), stone, "StoneHouse");
                MeshUtil.Part(vis, PrimitiveType.Cube, hp + hq * new Vector3(-1.5f, 3.75f, 0), new Vector3(3.6f, 0.15f, 5.6f), new Vector3(0, hy, 28f), slate, "Roof");
                MeshUtil.Part(vis, PrimitiveType.Cube, hp + hq * new Vector3(1.5f, 3.75f, 0), new Vector3(3.6f, 0.15f, 5.6f), new Vector3(0, hy, -28f), slate, "Roof");
                MeshUtil.Part(vis, PrimitiveType.Cube, hp + hq * new Vector3(0, 1f, -2.52f), new Vector3(1f, 2f, 0.05f), new Vector3(0, hy, 0), wood, "Door");
                WorldProps.Collider(places, hp + Vector3.up * 1.6f, new Vector3(6f, 3.2f, 5f), hy);
            }
            // prayer-flag pole
            Vector3 fp = top + new Vector3(-12f, 0, 14f); fp.y = TerrainH(fp);
            MeshUtil.Part(vis, PrimitiveType.Cylinder, fp + Vector3.up * 4f, new Vector3(0.15f, 4f, 0.15f), Vector3.zero, wood, "FlagPole");
            Color[] flags = { Color.blue, Color.white, Color.red, Color.green, Color.yellow };
            for (int i = 0; i < 10; i++)
                MeshUtil.Part(vis, PrimitiveType.Cube, fp + new Vector3(0.6f + i * 0.9f, 7.5f - i * 0.55f, 0), new Vector3(0.6f, 0.45f, 0.02f), Vector3.zero, MatLib.Opaque(flags[i % 5], 0.2f, 0f), "PrayerFlag");
            MeshUtil.CombineByMaterial(vis.gameObject);
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

        // ------------------------------------------------------------------ mission
        static void BuildMission()
        {
            var mgo = new GameObject("CargoMission");
            mgo.transform.SetParent(root, false);
            var mission = mgo.AddComponent<CargoMission>();
            mission.title = "Apples to Pahadgaon";
            mission.cargo = CargoType.Apples;
            mission.targetKg = 4500f;
            mission.basePay = 9000;
            mission.parTimeSec = road.Count / 3.0f;      // ~11 km/h average
            mission.loadZone = loadZone;
            mission.deliveryZone = deliveryZone;
            int n = 0;
            for (int i = 380; i < road.Count - 80; i += 380)
            {
                Vector3 f = RoadDir(i);
                float yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
                float half = RoadHalf + Mathf.Clamp(Curvature(i) * 120f, 0f, 2.6f);
                var z = Zone.Create(mgo.transform, "Checkpoint" + (++n), road[i] - Vector3.up * 0.5f, new Vector3(half * 2f + 4f, 6f, 4f), yaw);
                mission.checkpoints.Add(z);
                mission.checkpointMarkers.Add(WorldProps.Gate(mgo.transform, road[i], yaw, half * 2f + 1.5f, "CHECKPOINT " + n));
            }
            Mission = mission;
        }

        public static float RouteLengthKm { get { return road != null ? road.Count / 1000f : 0f; } }
    }
}
