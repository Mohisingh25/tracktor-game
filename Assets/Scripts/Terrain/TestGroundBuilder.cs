using UnityEngine;

namespace TractorSim
{
    /// <summary>
    /// Stage-1 proving ground in a North-Indian village setting: asphalt road, dirt tracks,
    /// mud pit, wet grass, sand dunes, rock garden, two graded hills, a river ford,
    /// a ploughed field, plus village props (houses, hand pump, water tank, poles, tea stall, cattle).
    /// </summary>
    public static class TestGroundBuilder
    {
        public static Vector3 SpawnPoint = new Vector3(0f, 0.4f, -25f);
        public static Quaternion SpawnRotation = Quaternion.identity;
        public static CargoMission Mission;
        public static FieldArea Farm;

        static Transform root;
        static Material grassM, dirtM, mudM, sandM, rockM, fieldM, roadM, brickM, plasterM, mudWallM, thatchM, woodM, leafM, leafM2, trunkM, waterM, concreteM, signM, steelM, wetM;

        public static Transform Build()
        {
            root = new GameObject("TestGround").transform;
            SurfaceZone.ClearCache();
            Materials();

            // ---- base ground (grass) ----
            Slab("Ground", new Vector3(0, -0.5f, 60f), new Vector3(640f, 1f, 640f), grassM, SurfaceKind.Grass);
            // invisible boundary
            for (int i = 0; i < 4; i++)
            {
                var w = new GameObject("Boundary").transform; w.SetParent(root, false);
                bool x = i < 2;
                float s = (i % 2 == 0) ? 1f : -1f;
                w.localPosition = x ? new Vector3(300f * s, 5f, 60f) : new Vector3(0f, 5f, 60f + 300f * s);
                var bc = w.gameObject.AddComponent<BoxCollider>(); bc.size = x ? new Vector3(1f, 10f, 640f) : new Vector3(640f, 10f, 1f);
            }

            // ---- asphalt road (spine) ----
            Slab("Road", new Vector3(0, -0.04f, 85f), new Vector3(7f, 0.1f, 290f), roadM, SurfaceKind.Asphalt);
            // dirt tracks branching to the zones
            Slab("Track_W1", new Vector3(-22f, -0.035f, 30f), new Vector3(37f, 0.1f, 6f), dirtM, SurfaceKind.Dirt);
            Slab("Track_E1", new Vector3(22f, -0.035f, 30f), new Vector3(37f, 0.1f, 6f), dirtM, SurfaceKind.Dirt);
            Slab("Track_W2", new Vector3(-22f, -0.035f, 100f), new Vector3(37f, 0.1f, 6f), dirtM, SurfaceKind.Dirt);
            Slab("Track_E2", new Vector3(22f, -0.035f, 100f), new Vector3(37f, 0.1f, 6f), dirtM, SurfaceKind.Dirt);
            Slab("Track_W3", new Vector3(-22f, -0.035f, 165f), new Vector3(37f, 0.1f, 6f), dirtM, SurfaceKind.Dirt);
            Slab("Track_E3", new Vector3(22f, -0.035f, 165f), new Vector3(37f, 0.1f, 6f), dirtM, SurfaceKind.Dirt);
            Slab("Track_Loop", new Vector3(-75f, -0.035f, 100f), new Vector3(6f, 0.1f, 160f), dirtM, SurfaceKind.Dirt);
            Slab("Track_LoopS", new Vector3(-40.75f, -0.035f, 16f), new Vector3(74.5f, 0.1f, 6f), dirtM, SurfaceKind.Dirt);

            // ---- WEST: mud pit, wet grass, sand dunes ----
            Slab("MudPit", new Vector3(-55f, -0.04f, 40f), new Vector3(30f, 0.1f, 36f), mudM, SurfaceKind.Mud);
            for (int i = 0; i < 6; i++)
                Water(new Vector3(-62f + (i % 3) * 7f, 0.025f, 30f + (i / 3) * 16f), new Vector2(4f + i % 2 * 2f, 5f));
            Sign(new Vector3(-38f, 0, 22f), "MUD PIT\nlow gear + diff lock");

            Slab("WetGrass", new Vector3(-50f, -0.04f, 95f), new Vector3(26f, 0.1f, 30f), wetM, SurfaceKind.WetGrass);
            Sign(new Vector3(-36f, 0, 92f), "WET GRASS");

            var dunes = MeshUtil.HeightField("SandDunes", root, new Vector3(-50f, 0.02f, 178f), 36f, 40f, 44,
                (x, z) => Mathf.Max(0f, Mathf.Sin(x * 0.25f) * Mathf.Cos(z * 0.18f) * 1.1f + Mathf.PerlinNoise(x * 0.08f + 10f, z * 0.08f) * 1.2f - 0.4f) * EdgeFade(x, z, 18f, 20f, 4f),
                sandM, new Vector2(10f, 12f));
            dunes.AddComponent<SurfaceZone>().kind = SurfaceKind.Sand;
            Sign(new Vector3(-28f, 0, 159f), "SAND DUNES");

            // ---- EAST: hills & rock garden ----
            // Hill A: 10° up, plateau, 18° down
            Hill("HillA", new Vector3(40.5f, 0f, 30f), 90f, 9f, new[] { 10f, 0f, -18f }, new[] { 28f, 14f, 0f });
            Sign(new Vector3(36f, 0, 22f), "HILL A\n10° up / 18° down");
            // Hill B: 18° up, plateau, 25° down
            Hill("HillB", new Vector3(40.5f, 0f, 100f), 90f, 9f, new[] { 18f, 0f, -25f }, new[] { 22f, 12f, 0f });
            Sign(new Vector3(36f, 0, 92f), "HILL B\n18° up / 25° down");

            Slab("RockBed", new Vector3(58f, -0.03f, 165f), new Vector3(40f, 0.1f, 40f), rockM, SurfaceKind.Rock);
            var rocks = new GameObject("Rocks").transform; rocks.SetParent(root, false);
            var rnd = new System.Random(42);
            for (int i = 0; i < 110; i++)
            {
                float rx = 40f + (float)rnd.NextDouble() * 36f, rz = 147f + (float)rnd.NextDouble() * 36f;
                float s = 0.25f + (float)rnd.NextDouble() * (i % 9 == 0 ? 1.1f : 0.5f);
                var rk = new GameObject("Rock"); rk.transform.SetParent(rocks, false);
                rk.transform.localPosition = new Vector3(rx, -s * 0.35f, rz);
                rk.transform.localRotation = Quaternion.Euler((float)rnd.NextDouble() * 360f, (float)rnd.NextDouble() * 360f, (float)rnd.NextDouble() * 360f);
                rk.transform.localScale = new Vector3(s * 1.3f, s * 0.8f, s);
                rk.AddComponent<MeshFilter>().sharedMesh = MeshUtil.PrimMesh(PrimitiveType.Sphere);
                rk.AddComponent<MeshRenderer>().sharedMaterial = rockM;
                var sc = rk.AddComponent<SphereCollider>(); sc.radius = 0.5f;
                rk.AddComponent<SurfaceZone>().kind = SurfaceKind.Rock;
            }
            Sign(new Vector3(36f, 0, 157f), "ROCK GARDEN");

            // ---- river ford on the loop track ----
            Ford(new Vector3(-75f, 0f, 135f));
            Sign(new Vector3(-68f, 0, 116f), "RIVER FORD\nkeep momentum");

            // ---- ploughed field at the top of the road ----
            var fieldGo = Slab("Field", new Vector3(0f, -0.03f, 255f), new Vector3(70f, 0.1f, 40f), fieldM, SurfaceKind.Field);
            Farm = fieldGo.AddComponent<FieldArea>();
            Farm.Init();
            Crops(new Vector3(-55f, 0f, 255f), 30f, 36f, true);   // sugarcane
            Crops(new Vector3(55f, 0f, 255f), 30f, 36f, false);   // wheat
            Sign(new Vector3(8f, 0, 230f), "FARM FIELD");

            Village();
            Trees();
            PolesAlongRoad();
            ImplementYard();
            SugarMillRun();
            return root;
        }

        /// <summary>Concrete yard with every implement parked, hitch towards the lane so you can reverse onto it.</summary>
        static void ImplementYard()
        {
            Slab("ImplementYard", new Vector3(27f, -0.03f, 17f), new Vector3(34f, 0.1f, 16f), MatLib.Opaque(new Color(0.6f, 0.6f, 0.58f), 0.1f, 0f), SurfaceKind.Asphalt);
            string[] mounted = { "rotavator", "cultivator", "plough", "harrow", "seeddrill", "leveller" };
            for (int i = 0; i < mounted.Length; i++)
                ImplementFactory.Create(mounted[i], new Vector3(14f + i * 4.2f, 0.15f, 21.5f), Quaternion.Euler(0, 180f, 0)).transform.SetParent(root, true);
            ImplementFactory.Create("sprayer", new Vector3(14f + 6 * 4.2f + 3f, 0.15f, 21.5f), Quaternion.Euler(0, 180f, 0)).transform.SetParent(root, true);
            Sign(new Vector3(12f, 0, 9f), "IMPLEMENT YARD\nreverse up · ATTACH");
            // trolleys parked across the road
            ImplementFactory.Create("trolley_single", new Vector3(-14f, 0.7f, 10f), Quaternion.identity).transform.SetParent(root, true);
            ImplementFactory.Create("trolley_tandem", new Vector3(-21f, 0.7f, 10f), Quaternion.identity).transform.SetParent(root, true);
            ImplementFactory.Create("trolley_single", new Vector3(-28f, 0.7f, 10f), Quaternion.identity).transform.SetParent(root, true);
        }

        /// <summary>Cargo job: cut sugarcane from the plot to the sugar mill by the village.</summary>
        static void SugarMillRun()
        {
            var m = new GameObject("SugarcaneMission").transform; m.SetParent(root, false);
            var vis = new GameObject("Visual").transform; vis.SetParent(m, false);
            var cane = MatLib.Opaque(new Color(0.45f, 0.58f, 0.24f), 0.1f, 0f);
            // loading bay beside the cane plot with cut cane bundles
            var load = Zone.Create(m, "CaneLoadingBay", new Vector3(-31f, 0f, 262f), new Vector3(5f, 4f, 12f), 0f);
            Slab("CaneBay", new Vector3(-31f, -0.03f, 262f), new Vector3(6f, 0.1f, 13f), MatLib.Opaque(new Color(0.55f, 0.45f, 0.32f), 0.05f, 0f), SurfaceKind.Dirt);
            for (int i = 0; i < 18; i++)
                MeshUtil.Part(vis, PrimitiveType.Cube, new Vector3(-36.5f - (i % 3) * 0.6f, 0.2f + (i / 9) * 0.35f, 256f + (i / 3 % 3) * 4f), new Vector3(0.5f, 0.32f, 3.2f), Vector3.zero, cane, "CaneBundle");
            Sign(new Vector3(-26f, 0, 252f), "CANE LOADING\nstop trolley · LOAD");
            // sugar mill: big shed + chimney
            var shed = MatLib.Opaque(new Color(0.75f, 0.72f, 0.65f), 0.1f, 0f);
            var tin = MatLib.Opaque(new Color(0.55f, 0.58f, 0.6f), 0.6f, 0.7f);
            WorldProps.Shed(vis, m, new Vector3(-42f, 0f, -48f), 0f, 20f, 12f, 8f, shed, tin);
            MeshUtil.Part(vis, PrimitiveType.Cylinder, new Vector3(-52f, 12f, -52f), new Vector3(2f, 12f, 2f), Vector3.zero, brickM, "Chimney");
            WorldProps.Capsule(m, new Vector3(-52f, 12f, -52f), 1f, 24f);
            var drop = Zone.Create(m, "MillYard", new Vector3(-42f, 0f, -36f), new Vector3(9f, 4f, 12f), 0f);
            Slab("MillYardPad", new Vector3(-42f, -0.03f, -36f), new Vector3(10f, 0.1f, 13f), MatLib.Opaque(new Color(0.6f, 0.6f, 0.58f), 0.1f, 0f), SurfaceKind.Asphalt);
            Sign(new Vector3(-36f, 0, -28f), "SUGAR MILL\nTIP here");
            MeshUtil.CombineByMaterial(vis.gameObject);

            var mission = m.gameObject.AddComponent<CargoMission>();
            mission.title = "Sugarcane to the mill";
            mission.cargo = CargoType.Sugarcane;
            mission.targetKg = 3000f; mission.basePay = 3500; mission.parTimeSec = 360f;
            mission.loadZone = load; mission.deliveryZone = drop;
            Vector3[] cps = { new Vector3(0, 0, 200f), new Vector3(0, 0, 110f), new Vector3(0, 0, 40f), new Vector3(-38f, 0, 16f), new Vector3(-40f, 0, -12f) };
            float[] yaws = { 180f, 180f, 180f, 270f, 180f };
            for (int i = 0; i < cps.Length; i++)
            {
                var z = Zone.Create(m, "CP" + (i + 1), cps[i] + Vector3.down * 0.5f, new Vector3(i < 3 ? 12f : 10f, 6f, 4f), yaws[i]);
                mission.checkpoints.Add(z);
                mission.checkpointMarkers.Add(WorldProps.Gate(m, cps[i], yaws[i], i < 3 ? 10f : 9f, "CHECKPOINT " + (i + 1)));
            }
            Mission = mission;
        }

        static float EdgeFade(float x, float z, float hx, float hz, float band)
        {
            float fx = Mathf.Clamp01((hx - Mathf.Abs(x)) / band);
            float fz = Mathf.Clamp01((hz - Mathf.Abs(z)) / band);
            return fx * fz;
        }

        static void Materials()
        {
            grassM = MatLib.Opaque(new Color(0.55f, 0.62f, 0.38f), 0.05f, 0f, ProcTex.Ground("grass", new Color(0.33f, 0.45f, 0.18f), new Color(0.55f, 0.58f, 0.3f), 6f, 0.25f, 1), new Vector2(160f, 160f));
            dirtM = MatLib.Opaque(Color.white, 0.05f, 0f, ProcTex.Ground("dirt", new Color(0.55f, 0.43f, 0.3f), new Color(0.68f, 0.57f, 0.42f), 5f, 0.3f, 2), new Vector2(6f, 6f));
            mudM = MatLib.Opaque(Color.white, 0.55f, 0f, ProcTex.Ground("mud", new Color(0.2f, 0.14f, 0.08f), new Color(0.34f, 0.25f, 0.15f), 4f, 0.2f, 3), new Vector2(6f, 7f));
            sandM = MatLib.Opaque(Color.white, 0.05f, 0f, ProcTex.Ground("sand", new Color(0.82f, 0.7f, 0.48f), new Color(0.93f, 0.83f, 0.62f), 7f, 0.15f, 4), Vector2.one);
            rockM = MatLib.Opaque(Color.white, 0.15f, 0f, ProcTex.Ground("rock", new Color(0.42f, 0.4f, 0.37f), new Color(0.6f, 0.58f, 0.54f), 9f, 0.4f, 5), new Vector2(2f, 2f));
            // harvested stubble: straw-coloured rows over dry soil (implements paint over this)
            fieldM = MatLib.Opaque(Color.white, 0.05f, 0f, ProcTex.Ground("stubble", new Color(0.5f, 0.41f, 0.27f), new Color(0.7f, 0.61f, 0.4f), 8f, 0.45f, 6, true), new Vector2(6f, 12f));
            wetM = MatLib.Opaque(Color.white, 0.6f, 0f, ProcTex.Ground("wetgrass", new Color(0.22f, 0.32f, 0.12f), new Color(0.38f, 0.42f, 0.2f), 6f, 0.3f, 7), new Vector2(8f, 10f));
            roadM = MatLib.Opaque(Color.white, 0.2f, 0f, ProcTex.Road(), new Vector2(1f, 40f));
            brickM = MatLib.Opaque(Color.white, 0.1f, 0f, ProcTex.Brick(), new Vector2(2f, 1f));
            plasterM = MatLib.Opaque(new Color(0.9f, 0.86f, 0.74f), 0.1f, 0f);
            mudWallM = MatLib.Opaque(new Color(0.62f, 0.48f, 0.33f), 0.05f, 0f);
            thatchM = MatLib.Opaque(new Color(0.66f, 0.56f, 0.32f), 0.05f, 0f);
            woodM = MatLib.Opaque(new Color(0.36f, 0.24f, 0.14f), 0.15f, 0f);
            leafM = MatLib.Opaque(new Color(0.2f, 0.36f, 0.14f), 0.1f, 0f);
            leafM2 = MatLib.Opaque(new Color(0.28f, 0.42f, 0.16f), 0.1f, 0f);
            trunkM = MatLib.Opaque(new Color(0.3f, 0.23f, 0.17f), 0.05f, 0f);
            waterM = MatLib.Transparent(new Color(0.32f, 0.38f, 0.3f, 0.75f), 0.95f);
            concreteM = MatLib.Opaque(new Color(0.66f, 0.66f, 0.63f), 0.1f, 0f);
            signM = MatLib.Opaque(new Color(0.1f, 0.35f, 0.18f), 0.3f, 0f);
            steelM = MatLib.Opaque(new Color(0.45f, 0.46f, 0.48f), 0.5f, 0.7f);
        }

        static GameObject Slab(string name, Vector3 pos, Vector3 size, Material m, SurfaceKind kind)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            go.AddComponent<MeshFilter>().sharedMesh = MeshUtil.PrimMesh(PrimitiveType.Cube);
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = m;
            go.AddComponent<BoxCollider>();
            go.AddComponent<SurfaceZone>().kind = kind;
            return go;
        }

        static void Water(Vector3 pos, Vector2 size)
        {
            var w = MeshUtil.Part(root, PrimitiveType.Cube, pos, new Vector3(size.x, 0.02f, size.y), Vector3.zero, waterM, "Puddle");
            w.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>Hill made from a list of slope segments (deg, length). Last length 0 = descend to ground.</summary>
        static void Hill(string name, Vector3 start, float yaw, float width, float[] slopes, float[] lengths)
        {
            var pts = new System.Collections.Generic.List<Vector2>();
            float z = 0f, y = 0f;
            pts.Add(new Vector2(-1f, 0f));
            pts.Add(new Vector2(0f, 0f));
            for (int i = 0; i < slopes.Length; i++)
            {
                float tan = Mathf.Tan(slopes[i] * Mathf.Deg2Rad);
                float len = lengths[i];
                if (len <= 0f && tan < 0f) len = y / -tan;   // run down to the ground
                int steps = Mathf.Max(2, Mathf.CeilToInt(len / 2f));
                for (int s = 1; s <= steps; s++)
                {
                    float dz = len / steps;
                    // round the crest a little so the tractor doesn't "hit" a sharp edge
                    z += dz; y += dz * tan;
                    pts.Add(new Vector2(z, Mathf.Max(0f, y)));
                }
            }
            pts.Add(new Vector2(z + 1f, 0f));
            var go = MeshUtil.Profile(name, root, start, yaw, width, pts.ToArray(), dirtM, mudWallM, 0.3f);
            var q = Quaternion.Euler(0, yaw, 0);
            go.AddComponent<SurfaceZone>().kind = SurfaceKind.Dirt;
            // guard stones along the edge of the plateau
            for (int i = 2; i < pts.Count - 1; i += 3)
                for (int s = -1; s <= 1; s += 2)
                    MeshUtil.Part(root, PrimitiveType.Cube, start + q * new Vector3(s * (width * 0.5f - 0.2f), pts[i].y + 0.2f, pts[i].x), new Vector3(0.35f, 0.4f, 0.35f), new Vector3(0, yaw, 0), concreteM, "GuardStone");
        }

        static void Ford(Vector3 c)
        {
            // banks dip 1.1 m into a 10 m wide river bed
            var pts = new[] { new Vector2(-14f, 0f), new Vector2(-10f, -0.2f), new Vector2(-6f, -0.9f), new Vector2(-4f, -1.1f), new Vector2(4f, -1.1f), new Vector2(6f, -0.9f), new Vector2(10f, -0.2f), new Vector2(14f, 0f) };
            // Profile builds above y=0; offset the pieces so the bed sits below ground level
            var go = new GameObject("Ford"); go.transform.SetParent(root, false); go.transform.localPosition = c;
            var verts = new System.Collections.Generic.List<Vector3>(); var tris = new System.Collections.Generic.List<int>(); var uvs = new System.Collections.Generic.List<Vector2>();
            float hw = 14f;
            for (int i = 0; i < pts.Length; i++)
            {
                verts.Add(new Vector3(-hw, pts[i].y, pts[i].x)); uvs.Add(new Vector2(0, pts[i].x * 0.3f));
                verts.Add(new Vector3(hw, pts[i].y, pts[i].x)); uvs.Add(new Vector2(hw * 0.6f, pts[i].x * 0.3f));
            }
            for (int i = 0; i < pts.Length - 1; i++) { int a = i * 2; tris.Add(a); tris.Add(a + 2); tris.Add(a + 1); tris.Add(a + 1); tris.Add(a + 2); tris.Add(a + 3); }
            var mesh = new Mesh(); mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mudM;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            go.AddComponent<SurfaceZone>().kind = SurfaceKind.Mud;
            // cut the grass slab visually: a dark trench rim isn't needed – the bed is wider than the track
            // water surface (no collider) – wheels feel the wet mud bed below it
            var wtr = MeshUtil.Part(root, PrimitiveType.Cube, c + new Vector3(0, -0.45f, 0), new Vector3(hw * 2f, 0.02f, 11f), Vector3.zero, MatLib.Transparent(new Color(0.25f, 0.36f, 0.38f, 0.7f), 0.95f), "RiverWater");
            wtr.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // the grass ground slab would cover the trench – lower a hole by placing the ford above: replace ground locally
            HoleInGround(c, hw * 2f, 28f);
            for (int e = -1; e <= 1; e += 2)
            {
                var wallPos = c + new Vector3(e * (hw + 0.25f), -0.6f, 0f);
                MeshUtil.Part(root, PrimitiveType.Cube, wallPos, new Vector3(0.5f, 1.2f, 28f), Vector3.zero, mudWallM, "FordBank");
                var bc = new GameObject("FordBankCol"); bc.transform.SetParent(root, false); bc.transform.localPosition = wallPos;
                bc.AddComponent<BoxCollider>().size = new Vector3(0.5f, 1.2f, 28f);
            }
        }

        /// <summary>The base ground is one big box; to show the trench we hide it locally with a box-shaped mask
        /// by placing the ford and disabling ground collision there via a trigger volume.</summary>
        static void HoleInGround(Vector3 c, float sx, float sz)
        {
            // Simplest robust approach: split the ground. We rebuild the ground as 4 slabs around the hole.
            var ground = root.Find("Ground");
            if (ground == null) return;
            Vector3 gp = ground.localPosition; Vector3 gs = ground.localScale;
            float gx0 = gp.x - gs.x * 0.5f, gx1 = gp.x + gs.x * 0.5f, gz0 = gp.z - gs.z * 0.5f, gz1 = gp.z + gs.z * 0.5f;
            float hx0 = c.x - sx * 0.5f, hx1 = c.x + sx * 0.5f, hz0 = c.z - sz * 0.5f + 0.01f, hz1 = c.z + sz * 0.5f - 0.01f;
            Object.DestroyImmediate(ground.gameObject);
            Slab("Ground_S", new Vector3((gx0 + gx1) * 0.5f, gp.y, (gz0 + hz0) * 0.5f), new Vector3(gx1 - gx0, gs.y, hz0 - gz0), grassM, SurfaceKind.Grass);
            Slab("Ground_N", new Vector3((gx0 + gx1) * 0.5f, gp.y, (hz1 + gz1) * 0.5f), new Vector3(gx1 - gx0, gs.y, gz1 - hz1), grassM, SurfaceKind.Grass);
            Slab("Ground_W", new Vector3((gx0 + hx0) * 0.5f, gp.y, (hz0 + hz1) * 0.5f), new Vector3(hx0 - gx0, gs.y, hz1 - hz0), grassM, SurfaceKind.Grass);
            Slab("Ground_E", new Vector3((hx1 + gx1) * 0.5f, gp.y, (hz0 + hz1) * 0.5f), new Vector3(gx1 - hx1, gs.y, hz1 - hz0), grassM, SurfaceKind.Grass);
            // the loop track also crosses the hole: cut it too
            var track = root.Find("Track_Loop");
            if (track != null)
            {
                Vector3 tp = track.localPosition, ts = track.localScale;
                float t0 = tp.z - ts.z * 0.5f, t1 = tp.z + ts.z * 0.5f;
                Object.DestroyImmediate(track.gameObject);
                Slab("Track_LoopA", new Vector3(tp.x, tp.y, (t0 + hz0) * 0.5f), new Vector3(ts.x, ts.y, hz0 - t0), dirtM, SurfaceKind.Dirt);
                Slab("Track_LoopB", new Vector3(tp.x, tp.y, (hz1 + t1) * 0.5f), new Vector3(ts.x, ts.y, t1 - hz1), dirtM, SurfaceKind.Dirt);
            }
        }

        static void Sign(Vector3 pos, string text)
        {
            var s = new GameObject("Sign_" + text.Split('\n')[0]).transform;
            s.SetParent(root, false);
            s.localPosition = pos;
            // board faces the approaching driver (towards the road / spawn)
            Vector3 toRoad = Mathf.Abs(pos.x) > 1f ? new Vector3(-pos.x, 0f, 0f) : new Vector3(0f, 0f, -1f);
            s.localRotation = Quaternion.LookRotation(toRoad.normalized, Vector3.up);
            MeshUtil.Part(s, PrimitiveType.Cylinder, new Vector3(-1.1f, 1.1f, 0), new Vector3(0.1f, 1.1f, 0.1f), Vector3.zero, steelM);
            MeshUtil.Part(s, PrimitiveType.Cylinder, new Vector3(1.1f, 1.1f, 0), new Vector3(0.1f, 1.1f, 0.1f), Vector3.zero, steelM);
            MeshUtil.Part(s, PrimitiveType.Cube, new Vector3(0, 2.2f, 0), new Vector3(2.8f, 1.1f, 0.08f), Vector3.zero, signM);
            var post = new GameObject("PostCol"); post.transform.SetParent(s, false); post.transform.localPosition = new Vector3(0, 1.4f, 0);
            post.AddComponent<BoxCollider>().size = new Vector3(2.4f, 2.8f, 0.15f);
            var tgo = new GameObject("Text"); tgo.transform.SetParent(s, false);
            tgo.transform.localPosition = new Vector3(0, 2.2f, 0.05f);
            tgo.transform.localScale = Vector3.one * 0.045f;
            var tm = tgo.AddComponent<TextMesh>();
            tm.text = text; tm.font = UIFactory.DefaultFont; tm.fontSize = 40; tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center; tm.color = Color.white;
            tgo.GetComponent<MeshRenderer>().sharedMaterial = UIFactory.DefaultFont.material;
            // TextMesh faces -Z; the sign faces the road
            tgo.transform.localEulerAngles = new Vector3(0, 180f, 0);
        }

        static void Crops(Vector3 c, float sx, float sz, bool sugarcane)
        {
            var g = new GameObject(sugarcane ? "Sugarcane" : "Wheat").transform;
            g.SetParent(root, false);
            var rnd = new System.Random(sugarcane ? 11 : 12);
            var m1 = sugarcane ? MatLib.Opaque(new Color(0.3f, 0.5f, 0.18f), 0.1f, 0f) : MatLib.Opaque(new Color(0.78f, 0.66f, 0.3f), 0.1f, 0f);
            var m2 = sugarcane ? MatLib.Opaque(new Color(0.42f, 0.58f, 0.22f), 0.1f, 0f) : MatLib.Opaque(new Color(0.7f, 0.58f, 0.25f), 0.1f, 0f);
            float rowGap = sugarcane ? 1.1f : 0.5f;
            for (float x = -sx * 0.5f; x < sx * 0.5f; x += rowGap)
                for (float z = -sz * 0.5f; z < sz * 0.5f; z += sugarcane ? 0.7f : 0.6f)
                {
                    float h = sugarcane ? 2.2f + (float)rnd.NextDouble() * 0.8f : 0.8f + (float)rnd.NextDouble() * 0.2f;
                    var p = c + new Vector3(x + (float)rnd.NextDouble() * 0.2f, h * 0.5f, z);
                    MeshUtil.Part(g, PrimitiveType.Cube, p - c, new Vector3(sugarcane ? 0.12f : 0.35f, h, sugarcane ? 0.12f : 0.5f), new Vector3(0, (float)rnd.NextDouble() * 90f, (float)rnd.NextDouble() * 6f - 3f), rnd.NextDouble() > 0.5 ? m1 : m2, "Plant");
                }
            g.localPosition = c;
            MeshUtil.CombineByMaterial(g.gameObject);
            // soft "bund" around the crop so you don't plough through it
            var col = new GameObject("CropFence"); col.transform.SetParent(g, false);
            col.AddComponent<BoxCollider>().size = new Vector3(sx, 1.2f, sz);
            col.GetComponent<BoxCollider>().center = new Vector3(0, 0.6f, 0);
        }

        static void Village()
        {
            var v = new GameObject("Village").transform; v.SetParent(root, false);
            var vis = new GameObject("VillageVisual").transform; vis.SetParent(v, false);
            var rnd = new System.Random(9);
            // a row of houses along the east side of the road near the spawn
            for (int i = 0; i < 5; i++)
            {
                float z = -55f + i * 11f;
                bool pucca = i % 2 == 0;
                House(vis, v, new Vector3(16f + (float)rnd.NextDouble() * 2f, 0, z), pucca, rnd);
            }
            for (int i = 0; i < 4; i++)
                House(vis, v, new Vector3(-17f - (float)rnd.NextDouble() * 2f, 0, -52f + i * 12f), i % 2 == 1, rnd);

            // brick boundary wall
            Wall(vis, v, new Vector3(10f, 0, -10f), new Vector3(0.3f, 1.4f, 40f));
            // hand pump
            var hp = new Vector3(6f, 0, -32f);
            MeshUtil.Part(vis, PrimitiveType.Cube, hp + new Vector3(0, 0.05f, 0), new Vector3(1.6f, 0.1f, 1.6f), Vector3.zero, concreteM);
            MeshUtil.Part(vis, PrimitiveType.Cylinder, hp + new Vector3(0, 0.6f, 0), new Vector3(0.14f, 0.5f, 0.14f), Vector3.zero, MatLib.Opaque(new Color(0.15f, 0.25f, 0.5f), 0.4f, 0.4f));
            MeshUtil.Part(vis, PrimitiveType.Cube, hp + new Vector3(0, 1.05f, 0.25f), new Vector3(0.05f, 0.05f, 0.7f), new Vector3(-15f, 0, 0), steelM);
            MeshUtil.Part(vis, PrimitiveType.Cube, hp + new Vector3(0, 0.8f, -0.15f), new Vector3(0.06f, 0.06f, 0.25f), Vector3.zero, steelM);
            Collider(v, hp + new Vector3(0, 0.6f, 0), new Vector3(0.3f, 1.2f, 0.3f));

            // overhead water tank on legs
            var wt = new Vector3(28f, 0, -20f);
            for (int i = 0; i < 4; i++)
            {
                var leg = wt + new Vector3((i % 2 == 0 ? -1.6f : 1.6f), 4f, (i < 2 ? -1.6f : 1.6f));
                MeshUtil.Part(vis, PrimitiveType.Cube, leg, new Vector3(0.35f, 8f, 0.35f), Vector3.zero, concreteM);
                Collider(v, leg, new Vector3(0.35f, 8f, 0.35f));
            }
            MeshUtil.Part(vis, PrimitiveType.Cylinder, wt + new Vector3(0, 9.4f, 0), new Vector3(4.6f, 1.4f, 4.6f), Vector3.zero, plasterM);
            MeshUtil.Part(vis, PrimitiveType.Cylinder, wt + new Vector3(0, 10.9f, 0), new Vector3(4.8f, 0.1f, 4.8f), Vector3.zero, concreteM);

            // tea stall with awning and bench
            var ts = new Vector3(-9f, 0, -10f);
            MeshUtil.Part(vis, PrimitiveType.Cube, ts + new Vector3(0, 0.6f, 0), new Vector3(2.4f, 1.2f, 1.2f), Vector3.zero, woodM);
            MeshUtil.Part(vis, PrimitiveType.Cube, ts + new Vector3(0, 2.4f, -0.4f), new Vector3(3.2f, 0.06f, 2.2f), new Vector3(12f, 0, 0), MatLib.Opaque(new Color(0.15f, 0.45f, 0.6f), 0.3f, 0f));
            for (int i = 0; i < 4; i++) MeshUtil.Part(vis, PrimitiveType.Cylinder, ts + new Vector3(i % 2 == 0 ? -1.5f : 1.5f, 1.2f, i < 2 ? -1.4f : 0.6f), new Vector3(0.08f, 1.2f, 0.08f), Vector3.zero, woodM);
            MeshUtil.Part(vis, PrimitiveType.Cube, ts + new Vector3(0, 0.4f, -1.9f), new Vector3(2.4f, 0.08f, 0.4f), Vector3.zero, woodM);
            MeshUtil.Part(vis, PrimitiveType.Cylinder, ts + new Vector3(-0.6f, 1.35f, 0), new Vector3(0.3f, 0.15f, 0.3f), Vector3.zero, steelM);   // kettle
            Collider(v, ts + new Vector3(0, 1.2f, -0.4f), new Vector3(3.2f, 2.4f, 2.8f));

            // cattle resting under the trees (static for Stage 1)
            Cow(vis, v, new Vector3(-12f, 0, -30f), 30f, new Color(0.9f, 0.88f, 0.82f));
            Cow(vis, v, new Vector3(-13.5f, 0, -26f), 200f, new Color(0.18f, 0.16f, 0.15f));   // buffalo
            Cow(vis, v, new Vector3(30f, 0, 2f), 110f, new Color(0.6f, 0.42f, 0.28f));
            MeshUtil.CombineByMaterial(vis.gameObject);
        }

        static void House(Transform vis, Transform colRoot, Vector3 p, bool pucca, System.Random rnd)
        {
            float w = 6f + (float)rnd.NextDouble() * 2f, d = 6f, h = pucca ? 3.2f : 2.6f;
            Material wall = pucca ? (rnd.NextDouble() > 0.5 ? brickM : plasterM) : mudWallM;
            MeshUtil.Part(vis, PrimitiveType.Cube, p + new Vector3(0, h * 0.5f, 0), new Vector3(d, h, w), Vector3.zero, wall);
            Collider(colRoot, p + new Vector3(0, h * 0.5f, 0), new Vector3(d, h, w));
            if (pucca)
            {
                MeshUtil.Part(vis, PrimitiveType.Cube, p + new Vector3(0, h + 0.1f, 0), new Vector3(d + 0.3f, 0.2f, w + 0.3f), Vector3.zero, concreteM);
                MeshUtil.Part(vis, PrimitiveType.Cube, p + new Vector3(0, h + 0.55f, 0), new Vector3(d, 0.7f, w), Vector3.zero, wall);   // parapet
            }
            else
            {
                // sloped thatch roof
                MeshUtil.Part(vis, PrimitiveType.Cube, p + new Vector3(-d * 0.25f, h + 0.55f, 0), new Vector3(d * 0.62f, 0.15f, w + 0.6f), new Vector3(0, 0, 22f), thatchM);
                MeshUtil.Part(vis, PrimitiveType.Cube, p + new Vector3(d * 0.25f, h + 0.55f, 0), new Vector3(d * 0.62f, 0.15f, w + 0.6f), new Vector3(0, 0, -22f), thatchM);
            }
            // door & windows facing the road
            float face = p.x > 0 ? -1f : 1f;
            MeshUtil.Part(vis, PrimitiveType.Cube, p + new Vector3(face * (d * 0.5f + 0.02f), 1.0f, 0), new Vector3(0.05f, 2.0f, 1.0f), Vector3.zero, MatLib.Opaque(new Color(0.15f, 0.35f, 0.45f), 0.3f, 0f));
            MeshUtil.Part(vis, PrimitiveType.Cube, p + new Vector3(face * (d * 0.5f + 0.02f), 1.6f, w * 0.3f), new Vector3(0.05f, 0.8f, 0.9f), Vector3.zero, MatLib.Opaque(new Color(0.12f, 0.12f, 0.12f), 0.6f, 0f));
        }

        static void Wall(Transform vis, Transform colRoot, Vector3 p, Vector3 size)
        {
            MeshUtil.Part(vis, PrimitiveType.Cube, p + new Vector3(0, size.y * 0.5f, 0), size, Vector3.zero, brickM);
            Collider(colRoot, p + new Vector3(0, size.y * 0.5f, 0), size);
        }

        static void Cow(Transform vis, Transform colRoot, Vector3 p, float yaw, Color c)
        {
            var m = MatLib.Opaque(c, 0.15f, 0f);
            var q = Quaternion.Euler(0, yaw, 0);
            MeshUtil.Part(vis, PrimitiveType.Capsule, p + q * new Vector3(0, 1.0f, 0), new Vector3(0.75f, 0.85f, 0.75f), (q * Quaternion.Euler(90, 0, 0)).eulerAngles, m, "CowBody");
            MeshUtil.Part(vis, PrimitiveType.Cube, p + q * new Vector3(0, 1.35f, 0.95f), new Vector3(0.35f, 0.4f, 0.5f), q.eulerAngles + new Vector3(25, 0, 0), m, "CowHead");
            MeshUtil.Part(vis, PrimitiveType.Sphere, p + q * new Vector3(0, 1.45f, -0.1f), new Vector3(0.4f, 0.35f, 0.4f), Vector3.zero, m, "Hump");
            for (int i = 0; i < 4; i++)
                MeshUtil.Part(vis, PrimitiveType.Cylinder, p + q * new Vector3(i % 2 == 0 ? -0.22f : 0.22f, 0.35f, i < 2 ? 0.55f : -0.55f), new Vector3(0.14f, 0.35f, 0.14f), Vector3.zero, m, "Leg");
            Collider(colRoot, p + new Vector3(0, 0.9f, 0), new Vector3(1.0f, 1.6f, 1.0f), yaw, 2.2f);
        }

        static void Collider(Transform parent, Vector3 center, Vector3 size, float yaw = 0f, float lengthZ = -1f)
        {
            var go = new GameObject("Col"); go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.transform.localEulerAngles = new Vector3(0, yaw, 0);
            var bc = go.AddComponent<BoxCollider>();
            bc.size = lengthZ > 0f ? new Vector3(size.x, size.y, lengthZ) : size;
        }

        static void Trees()
        {
            var t = new GameObject("Trees").transform; t.SetParent(root, false);
            var vis = new GameObject("TreeVisual").transform; vis.SetParent(t, false);
            var rnd = new System.Random(77);
            int placed = 0, guard = 0;
            while (placed < 140 && guard++ < 2000)
            {
                float x = (float)rnd.NextDouble() * 560f - 280f, z = (float)rnd.NextDouble() * 560f - 220f;
                if (InPlayArea(x, z)) continue;
                Tree(vis, t, new Vector3(x, 0, z), rnd);
                placed++;
            }
            // shade trees near the village
            Tree(vis, t, new Vector3(-12f, 0, -34f), rnd); Tree(vis, t, new Vector3(-14f, 0, -22f), rnd); Tree(vis, t, new Vector3(9f, 0, -36f), rnd);
            Tree(vis, t, new Vector3(44f, 0, 4f), rnd); Tree(vis, t, new Vector3(-6f, 0, -60f), rnd);
            MeshUtil.CombineByMaterial(vis.gameObject);
        }

        static bool InPlayArea(float x, float z)
        {
            if (Mathf.Abs(x) < 9f && z > -70f && z < 280f) return true;          // road
            if (Mathf.Abs(x) < 85f && z > -70f && z < 285f) return true;         // zones + village
            return false;
        }

        static void Tree(Transform vis, Transform colRoot, Vector3 p, System.Random rnd)
        {
            float h = 3.5f + (float)rnd.NextDouble() * 3.5f;
            float girth = 0.25f + (float)rnd.NextDouble() * 0.25f;
            MeshUtil.Part(vis, PrimitiveType.Cylinder, p + new Vector3(0, h * 0.5f, 0), new Vector3(girth, h * 0.5f, girth), Vector3.zero, trunkM, "Trunk");
            int blobs = 3 + rnd.Next(3);
            for (int i = 0; i < blobs; i++)
            {
                float s = 2.2f + (float)rnd.NextDouble() * 2.2f;
                var o = new Vector3((float)rnd.NextDouble() * 2.4f - 1.2f, h + (float)rnd.NextDouble() * 1.5f - 0.3f, (float)rnd.NextDouble() * 2.4f - 1.2f);
                MeshUtil.Part(vis, PrimitiveType.Sphere, p + o, new Vector3(s, s * 0.75f, s), Vector3.zero, rnd.NextDouble() > 0.5 ? leafM : leafM2, "Leaves");
            }
            var col = new GameObject("TreeCol"); col.transform.SetParent(colRoot, false); col.transform.localPosition = p + new Vector3(0, h * 0.5f, 0);
            var cc = col.AddComponent<CapsuleCollider>(); cc.radius = girth * 0.5f; cc.height = h;
        }

        static void PolesAlongRoad()
        {
            var g = new GameObject("Poles").transform; g.SetParent(root, false);
            var vis = new GameObject("PoleVisual").transform; vis.SetParent(g, false);
            Vector3 prev = Vector3.zero; bool has = false;
            var wireMat = MatLib.Opaque(new Color(0.08f, 0.08f, 0.08f), 0.2f, 0f);
            for (float z = -60f; z <= 230f; z += 35f)
            {
                var p = new Vector3(5.5f, 0, z);
                MeshUtil.Part(vis, PrimitiveType.Cube, p + new Vector3(0, 4.5f, 0), new Vector3(0.22f, 9f, 0.22f), Vector3.zero, concreteM, "Pole");
                MeshUtil.Part(vis, PrimitiveType.Cube, p + new Vector3(0, 8.6f, 0), new Vector3(1.4f, 0.1f, 0.1f), Vector3.zero, steelM, "CrossArm");
                var col = new GameObject("PoleCol"); col.transform.SetParent(g, false); col.transform.localPosition = p + new Vector3(0, 4.5f, 0);
                col.AddComponent<BoxCollider>().size = new Vector3(0.25f, 9f, 0.25f);
                if (has)
                {
                    for (int k = -1; k <= 1; k += 2)
                    {
                        var a = prev + new Vector3(k * 0.6f, 8.65f, 0);
                        var b = p + new Vector3(k * 0.6f, 8.65f, 0);
                        // sagging wire made of a few short segments
                        const int segs = 6;
                        for (int s = 0; s < segs; s++)
                        {
                            float t0 = s / (float)segs, t1 = (s + 1) / (float)segs;
                            var p0 = Vector3.Lerp(a, b, t0) + Vector3.down * Mathf.Sin(t0 * Mathf.PI) * 0.6f;
                            var p1 = Vector3.Lerp(a, b, t1) + Vector3.down * Mathf.Sin(t1 * Mathf.PI) * 0.6f;
                            var mid = (p0 + p1) * 0.5f;
                            var dir = p1 - p0;
                            var wgo = MeshUtil.Part(vis, PrimitiveType.Cube, mid, new Vector3(0.025f, 0.025f, dir.magnitude), Quaternion.LookRotation(dir).eulerAngles, wireMat, "Wire");
                            wgo.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        }
                    }
                }
                prev = p; has = true;
            }
            MeshUtil.CombineByMaterial(vis.gameObject);
        }
    }
}
