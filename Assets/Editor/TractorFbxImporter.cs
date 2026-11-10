#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace TractorSim.EditorTools
{
    /// <summary>
    /// Turns the user's FBX (Assets/Art/Tractor/tractor.fbx – a tractor with a trolley behind it, wheels merged
    /// in pairs, UDIM textures) into game-ready prefabs:
    ///   Resources/Art/TractorRig.prefab  – body + four separate wheel pivots (spin & steer)
    ///   Resources/Art/TrolleyRig.prefab  – chassis + tipping bed + wheel set, origin at the hitch eye
    ///   Resources/Art/TractorRigInfo.asset – measurements so physics matches the model
    /// UDIM tiles (1001..1005) become separate materials. Textures are optional: put the Blender
    /// textures_2k files in Assets/Art/Tractor/Textures and they are used automatically.
    /// </summary>
    public static class TractorFbxImporter
    {
        const string FbxPath = "Assets/Art/Tractor/tractor.fbx";
        const string TexDir = "Assets/Art/Tractor/Textures";
        const string OutDir = "Assets/Resources/Art";
        const string GenDir = "Assets/Generated";

        enum Group { Body, FL, FR, RL, RR, TrolleyChassis, TrolleyBed, TrolleyWheels }

        class Bucket
        {
            public readonly List<Vector3> v = new List<Vector3>();
            public readonly List<Vector3> n = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<int> t = new List<int>();
            public readonly Dictionary<long, int> map = new Dictionary<long, int>();
        }

        struct WheelSet { public Vector3 center; public float radius; public bool found; }

        [MenuItem("Tractor Sim/Re-import FBX tractor")]
        public static void Process()
        {
            if (!File.Exists(FbxPath)) { Debug.Log("[TractorSim] No FBX tractor at " + FbxPath + " – using procedural tractors."); return; }
            var importer = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
            if (importer == null) return;
            bool reimport = false;
            if (!importer.isReadable) { importer.isReadable = true; reimport = true; }
            if (importer.importAnimation) { importer.importAnimation = false; reimport = true; }
            if (importer.importCameras || importer.importLights) { importer.importCameras = false; importer.importLights = false; reimport = true; }
            if (reimport) importer.SaveAndReimport();

            var src = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            if (src == null) return;
            var inst = Object.Instantiate(src);
            inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            inst.transform.localScale = Vector3.one;
            try { Build(inst); }
            finally { Object.DestroyImmediate(inst); }
        }

        static Bounds? NamedBounds(GameObject root, string name)
        {
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                if (r.name == name) return r.bounds;
            return null;
        }

        static void Build(GameObject inst)
        {
            // ---------- 1. find axles from the known part names (fallback: measured values) ----------
            var rearB = NamedBounds(inst, "Truck.062");
            var frontB = NamedBounds(inst, "Truck.008");
            var trolB = NamedBounds(inst, "Truck.005");
            var boxB = NamedBounds(inst, "back.001");
            var hoodB = NamedBounds(inst, "Cube");
            var pipeB = NamedBounds(inst, "Truck.014");
            WheelSet rear = Axle(rearB, new Vector3(0, 0.656f, -0.871f), 0.668f);
            WheelSet front = Axle(frontB, new Vector3(0, 0.439f, 1.166f), 0.443f);
            WheelSet trol = Axle(trolB, new Vector3(0, 0.439f, -4.233f), 0.443f);
            bool flip = front.center.z < rear.center.z;   // model faces -Z → turn it round
            Quaternion fix = flip ? Quaternion.Euler(0, 180f, 0) : Quaternion.identity;
            if (flip) { rear.center = fix * rear.center; front.center = fix * front.center; trol.center = fix * trol.center; }
            float trolleyCut = rear.center.z - rear.radius - 0.2f;   // everything behind this belongs to the trolley

            // ---------- 2. classify every triangle ----------
            var buckets = new Dictionary<long, Bucket>();
            var groupBounds = new Dictionary<Group, Bounds>();
            bool textured = HasTextures();
            var mfs = inst.GetComponentsInChildren<MeshFilter>(true);
            float boxFront = boxB.HasValue ? (fix * boxB.Value.center).z + boxB.Value.extents.z : trol.center.z + 2.0f;
            float floorY = boxB.HasValue ? boxB.Value.min.y : 0.88f;
            for (int m = 0; m < mfs.Length; m++)
            {
                var mesh = mfs[m].sharedMesh;
                if (mesh == null) continue;
                var M = mfs[m].transform.localToWorldMatrix;
                string nm = mfs[m].name;
                int wheelHint = nm == "Truck.062" ? 1 : nm == "Truck.008" ? 2 : nm == "Truck.005" ? 3 : 0;
                var verts = mesh.vertices; var norms = mesh.normals; var uvs = mesh.uv;
                Vector3[] wv = new Vector3[verts.Length]; Vector3[] wn = new Vector3[verts.Length];
                for (int i = 0; i < verts.Length; i++)
                {
                    wv[i] = fix * M.MultiplyPoint3x4(verts[i]);
                    wn[i] = norms.Length == verts.Length ? (fix * M.MultiplyVector(norms[i])).normalized : Vector3.up;
                }
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    var tris = mesh.GetTriangles(sub);
                    for (int i = 0; i + 2 < tris.Length; i += 3)
                    {
                        int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                        Vector3 cen = (wv[a] + wv[b] + wv[c]) / 3f;
                        Group g = Classify(cen, wheelHint, front, rear, trol, trolleyCut, boxFront, floorY);
                        float uMean = uvs.Length == verts.Length ? (uvs[a].x + uvs[b].x + uvs[c].x) / 3f : 0.5f;
                        int tile = Mathf.Clamp(Mathf.FloorToInt(uMean), 0, 9);
                        int part = 0;
                        if (!textured && IsWheel(g))
                        {
                            WheelSet ws = g == Group.TrolleyWheels ? trol : (g == Group.FL || g == Group.FR ? front : rear);
                            float rr = new Vector2(cen.y - ws.center.y, cen.z - ws.center.z).magnitude;
                            part = rr < ws.radius * 0.62f ? 1 : 0;   // rim vs tyre
                        }
                        long key = ((long)g << 16) | ((long)tile << 4) | (long)part;
                        Bucket bk;
                        if (!buckets.TryGetValue(key, out bk)) { bk = new Bucket(); buckets[key] = bk; }
                        int[] idx = { a, b, c };
                        for (int k = 0; k < 3; k++)
                        {
                            long vk = ((long)m << 32) | (long)idx[k];
                            int ni;
                            if (!bk.map.TryGetValue(vk, out ni))
                            {
                                ni = bk.v.Count; bk.map[vk] = ni;
                                bk.v.Add(wv[idx[k]]); bk.n.Add(wn[idx[k]]);
                                var uv = uvs.Length == verts.Length ? uvs[idx[k]] : Vector2.zero;
                                bk.uv.Add(new Vector2(uv.x - tile, uv.y));
                                Bounds gb;
                                if (groupBounds.TryGetValue(g, out gb)) { gb.Encapsulate(wv[idx[k]]); groupBounds[g] = gb; }
                                else groupBounds[g] = new Bounds(wv[idx[k]], Vector3.zero);
                            }
                            bk.t.Add(ni);
                        }
                    }
                }
            }

            // ---------- 3. measurements ----------
            float groundY = Mathf.Min(rear.center.y - rear.radius, front.center.y - front.radius);
            Vector3 origin = new Vector3(0f, groundY, (front.center.z + rear.center.z) * 0.5f);
            var info = ScriptableObject.CreateInstance<TractorRigInfo>();
            info.textured = textured;
            info.rearRadius = rear.radius; info.frontRadius = front.radius;
            info.wheelBase = front.center.z - rear.center.z;
            info.rearTrack = WheelTrack(groupBounds, Group.RL, Group.RR, 1.86f);
            info.frontTrack = WheelTrack(groupBounds, Group.FL, Group.FR, 1.4f);
            info.rearWidth = WheelWidth(groupBounds, Group.RL, 0.42f);
            info.frontWidth = WheelWidth(groupBounds, Group.FL, 0.2f);
            Bounds body; groupBounds.TryGetValue(Group.Body, out body);
            info.length = body.size.z; info.width = body.size.x; info.height = body.max.y - groundY;
            info.rearZ = body.min.z - origin.z;
            if (pipeB.HasValue) { var p = fix * pipeB.Value.center; info.exhaustTop = new Vector3(p.x, pipeB.Value.max.y - groundY + 0.02f, p.z - origin.z); }
            if (hoodB.HasValue) { var h = hoodB.Value; info.hoodSide = new Vector3(h.extents.x + 0.006f, h.center.y - groundY + 0.12f, (fix * h.center).z + h.extents.z * 0.45f - origin.z); info.hoodLength = h.size.z * 0.4f; }
            info.seat = new Vector3(0f, 1.24f, rear.center.z + 0.15f - origin.z);

            // ---------- 4. materials ----------
            if (!AssetDatabase.IsValidFolder(GenDir)) AssetDatabase.CreateFolder("Assets", "Generated");
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder(OutDir)) AssetDatabase.CreateFolder("Assets/Resources", "Art");
            var mats = new Dictionary<long, Material>();
            // ---------- 5. tractor prefab ----------
            string meshAsset = GenDir + "/TractorRigMeshes.asset";
            AssetDatabase.DeleteAsset(meshAsset);
            var holder = new Mesh { name = "TractorRigMeshes" };
            AssetDatabase.CreateAsset(holder, meshAsset);

            var root = new GameObject("TractorRig");
            var bodyGo = new GameObject("Body"); bodyGo.transform.SetParent(root.transform, false);
            var wheelGos = new Dictionary<Group, Transform>();
            foreach (var g in new[] { Group.FL, Group.FR, Group.RL, Group.RR })
            {
                Bounds gb;
                if (!groupBounds.TryGetValue(g, out gb)) continue;
                WheelSet ws = g == Group.FL || g == Group.FR ? front : rear;
                var wgo = new GameObject("Wheel_" + g);
                wgo.transform.SetParent(root.transform, false);
                wgo.transform.localPosition = new Vector3(gb.center.x, ws.center.y, ws.center.z) - origin;
                wheelGos[g] = wgo.transform;
            }
            var trolley = new GameObject("TrolleyRig");
            Vector3 eye = Vector3.zero; bool hasTrolley = groupBounds.ContainsKey(Group.TrolleyChassis) || groupBounds.ContainsKey(Group.TrolleyBed);
            if (hasTrolley)
            {
                Bounds tc; groupBounds.TryGetValue(Group.TrolleyChassis, out tc);
                eye = new Vector3(0f, groundY + 0.5f, tc.max.z);
            }
            var tChassis = new GameObject("Chassis"); tChassis.transform.SetParent(trolley.transform, false);
            var tBedPivot = new GameObject("Bed"); tBedPivot.transform.SetParent(trolley.transform, false);
            Bounds bedB; groupBounds.TryGetValue(Group.TrolleyBed, out bedB);
            Vector3 bedPivotWorld = new Vector3(0f, bedB.min.y, bedB.min.z);
            tBedPivot.transform.localPosition = bedPivotWorld - eye;
            var tWheels = new GameObject("Wheels"); tWheels.transform.SetParent(trolley.transform, false);
            tWheels.transform.localPosition = new Vector3(0f, trol.center.y, trol.center.z) - eye;

            int count = 0;
            foreach (var kv in buckets)
            {
                var g = (Group)(kv.Key >> 16);
                var bk = kv.Value;
                if (bk.t.Count == 0) continue;
                Transform parent; Vector3 pivotWorld;
                switch (g)
                {
                    case Group.Body: parent = bodyGo.transform; pivotWorld = origin; break;
                    case Group.TrolleyChassis: parent = tChassis.transform; pivotWorld = eye; break;
                    case Group.TrolleyBed: parent = tBedPivot.transform; pivotWorld = bedPivotWorld; break;
                    case Group.TrolleyWheels: parent = tWheels.transform; pivotWorld = new Vector3(0f, trol.center.y, trol.center.z); break;
                    default:
                        if (!wheelGos.TryGetValue(g, out parent)) continue;
                        pivotWorld = parent.localPosition + origin; break;
                }
                var mesh = new Mesh { name = g + "_" + kv.Key };
                mesh.indexFormat = bk.v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                var vv = new Vector3[bk.v.Count];
                for (int i = 0; i < vv.Length; i++) vv[i] = bk.v[i] - pivotWorld;
                mesh.vertices = vv; mesh.normals = bk.n.ToArray(); mesh.uv = bk.uv.ToArray();
                mesh.triangles = bk.t.ToArray();
                mesh.RecalculateBounds();
                if (textured) mesh.RecalculateTangents();
                AssetDatabase.AddObjectToAsset(mesh, holder);
                var part = new GameObject(mesh.name);
                part.transform.SetParent(parent, false);
                part.AddComponent<MeshFilter>().sharedMesh = mesh;
                part.AddComponent<MeshRenderer>().sharedMaterial = MatFor(mats, kv.Key, textured);
                count++;
            }

            info.hasTrolley = hasTrolley;
            if (hasTrolley)
            {
                info.hitchZ = eye.z - origin.z;
                info.trolleyAxleZ = trol.center.z - eye.z;
                info.trolleyWheelRadius = trol.radius;
                Bounds tw;
                info.trolleyTrack = groupBounds.TryGetValue(Group.TrolleyWheels, out tw) ? tw.size.x - 0.2f : 1.45f;
                info.trolleyBedFrontZ = bedB.max.z - eye.z;
                info.trolleyBedLength = bedB.size.z;
                info.trolleyBedWidth = bedB.size.x;
                info.trolleyFloorY = bedB.min.y - groundY;
                info.trolleySideHeight = bedB.size.y;
            }

            AssetDatabase.SaveAssets();
            Save(root, OutDir + "/TractorRig.prefab");
            if (hasTrolley) Save(trolley, OutDir + "/TrolleyRig.prefab"); else Object.DestroyImmediate(trolley);
            AssetDatabase.DeleteAsset(OutDir + "/TractorRigInfo.asset");
            AssetDatabase.CreateAsset(info, OutDir + "/TractorRigInfo.asset");
            AssetDatabase.SaveAssets();
            Debug.Log("[TractorSim] FBX tractor processed: " + count + " parts, textured=" + textured + ", wheelbase " + info.wheelBase.ToString("0.00") + " m, rear r " + info.rearRadius.ToString("0.00") + ", front r " + info.frontRadius.ToString("0.00"));
        }

        static Material MatFor(Dictionary<long, Material> mats, long key, bool textured)
        {
            Material mm;
            if (mats.TryGetValue(key, out mm)) return mm;
            var g = (Group)(key >> 16); int tile = (int)((key >> 4) & 0xFFF); int part = (int)(key & 0xF);
            mm = textured ? TileMaterial(tile) : FallbackMaterial(g, part);
            mats[key] = mm;
            return mm;
        }

        static void Save(GameObject go, string path)
        {
            PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
        }

        static WheelSet Axle(Bounds? b, Vector3 fallback, float fallbackR)
        {
            var w = new WheelSet();
            if (b.HasValue) { w.center = new Vector3(0f, b.Value.center.y, b.Value.center.z); w.radius = b.Value.extents.y; w.found = true; }
            else { w.center = fallback; w.radius = fallbackR; }
            return w;
        }

        static bool IsWheel(Group g) { return g == Group.FL || g == Group.FR || g == Group.RL || g == Group.RR || g == Group.TrolleyWheels; }

        static Group Classify(Vector3 c, int hint, WheelSet front, WheelSet rear, WheelSet trol, float trolleyCut, float boxFront, float floorY)
        {
            // known wheel meshes first; otherwise only hub-sized geometry near an axle counts as wheel (keeps fenders on the body)
            if (hint == 1) return c.x < 0f ? Group.RL : Group.RR;
            if (hint == 2) return c.x < 0f ? Group.FL : Group.FR;
            if (hint == 3) return Group.TrolleyWheels;
            if (c.z < trolleyCut)
            {
                if (Mathf.Abs(c.x) > 0.55f && YZ(c, trol) < trol.radius * 0.8f) return Group.TrolleyWheels;
                if (c.z < boxFront + 0.02f && c.y > floorY - 0.06f) return Group.TrolleyBed;
                return Group.TrolleyChassis;
            }
            if (Mathf.Abs(c.x) > 0.7f && YZ(c, rear) < rear.radius * 0.8f) return c.x < 0f ? Group.RL : Group.RR;
            if (Mathf.Abs(c.x) > 0.5f && YZ(c, front) < front.radius * 0.8f) return c.x < 0f ? Group.FL : Group.FR;
            return Group.Body;
        }

        static float YZ(Vector3 c, WheelSet w) { return new Vector2(c.y - w.center.y, c.z - w.center.z).magnitude; }

        static float WheelTrack(Dictionary<Group, Bounds> gb, Group l, Group r, float fallback)
        {
            Bounds a, b;
            if (gb.TryGetValue(l, out a) && gb.TryGetValue(r, out b)) return Mathf.Abs(b.center.x - a.center.x);
            return fallback;
        }

        static float WheelWidth(Dictionary<Group, Bounds> gb, Group g, float fallback)
        {
            Bounds a;
            return gb.TryGetValue(g, out a) ? Mathf.Max(0.1f, a.size.x) : fallback;
        }

        // ---------------- textures ----------------
        static string FindTex(string key, int udim)
        {
            if (!AssetDatabase.IsValidFolder(TexDir)) return null;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TexDir }))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                var f = Path.GetFileNameWithoutExtension(p).ToLowerInvariant();
                if (f.Contains(key) && f.Contains(udim.ToString())) return p;
            }
            return null;
        }

        static bool HasTextures() { return FindTex("base_color", 1001) != null; }

        static Texture2D Load(string path, bool normal, bool readable, bool srgb)
        {
            if (path == null) return null;
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti != null)
            {
                bool dirty = false;
                if (normal && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; dirty = true; }
                if (ti.isReadable != readable) { ti.isReadable = readable; dirty = true; }
                if (!normal && ti.sRGBTexture != srgb) { ti.sRGBTexture = srgb; dirty = true; }
                if (ti.maxTextureSize > 2048) { ti.maxTextureSize = 2048; dirty = true; }
                if (dirty) ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material TileMaterial(int tile)
        {
            int udim = 1001 + tile;
            string matPath = GenDir + "/Tractor_UDIM" + udim + ".mat";
            var m = new Material(Shader.Find("Standard")) { name = "Tractor_UDIM" + udim };
            var albedoPath = FindTex("base_color", udim);
            var opacityPath = FindTex("opacity", udim);
            var albedo = Load(albedoPath, false, opacityPath != null, true);
            bool cutout = false;
            if (albedo != null && opacityPath != null)
            {
                var op = Load(opacityPath, false, true, false);
                var combined = CombineAlpha(albedo, op, GenDir + "/Tractor_Albedo_" + udim + ".png", out cutout);
                if (combined != null) albedo = combined;
            }
            if (albedo != null) m.mainTexture = albedo;
            var normal = Load(FindTex("normal", udim), true, false, false);
            if (normal != null) { m.SetTexture("_BumpMap", normal); m.EnableKeyword("_NORMALMAP"); }
            var metal = FindTex("metallic", udim); var rough = FindTex("roughness", udim);
            if (metal != null || rough != null)
            {
                var ms = CombineMetalSmooth(Load(metal, false, true, false), Load(rough, false, true, false), GenDir + "/Tractor_MetalSmooth_" + udim + ".png");
                if (ms != null) { m.SetTexture("_MetallicGlossMap", ms); m.EnableKeyword("_METALLICGLOSSMAP"); m.SetFloat("_GlossMapScale", 1f); }
            }
            else { m.SetFloat("_Glossiness", 0.4f); }
            if (cutout)
            {
                m.SetFloat("_Mode", 1f); m.EnableKeyword("_ALPHATEST_ON"); m.SetFloat("_Cutoff", 0.5f);
                m.SetOverrideTag("RenderType", "TransparentCutout"); m.renderQueue = 2450;
            }
            AssetDatabase.DeleteAsset(matPath);
            AssetDatabase.CreateAsset(m, matPath);
            return m;
        }

        static Texture2D CombineAlpha(Texture2D albedo, Texture2D opacity, string outPath, out bool cutout)
        {
            cutout = false;
            if (albedo == null || opacity == null) return null;
            int w = albedo.width, h = albedo.height;
            var a = albedo.GetPixels();
            var o = opacity.width == w && opacity.height == h ? opacity.GetPixels() : null;
            if (o == null) return null;
            for (int i = 0; i < a.Length; i++) { a[i].a = o[i].r; if (o[i].r < 0.5f) cutout = true; }
            if (!cutout) return null;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true);
            t.SetPixels(a); t.Apply();
            File.WriteAllBytes(outPath, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(outPath);
            return Load(outPath, false, false, true);
        }

        static Texture2D CombineMetalSmooth(Texture2D metal, Texture2D rough, string outPath)
        {
            var refTex = metal != null ? metal : rough;
            if (refTex == null) return null;
            int w = refTex.width, h = refTex.height;
            Color[] mp = metal != null && metal.width == w ? metal.GetPixels() : null;
            Color[] rp = rough != null && rough.width == w ? rough.GetPixels() : null;
            var o = new Color[w * h];
            for (int i = 0; i < o.Length; i++)
            {
                float mv = mp != null ? mp[i].r : 0f;
                float sv = rp != null ? 1f - rp[i].r : 0.4f;
                o[i] = new Color(mv, mv, mv, sv);
            }
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true, true);
            t.SetPixels(o); t.Apply();
            File.WriteAllBytes(outPath, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(outPath);
            return Load(outPath, false, false, false);
        }

        /// <summary>Until the real textures are added: painted body, black tyres, silver/yellow rims.</summary>
        static Material FallbackMaterial(Group g, int part)
        {
            string name = "TractorFallback_" + g + "_" + part;
            string path = GenDir + "/" + name + ".mat";
            var m = new Material(Shader.Find("Standard")) { name = name };
            switch (g)
            {
                case Group.Body: m.color = new Color(0.68f, 0.07f, 0.05f); m.SetFloat("_Glossiness", 0.6f); m.SetFloat("_Metallic", 0.15f); break;
                case Group.TrolleyChassis: m.color = new Color(0.15f, 0.15f, 0.16f); m.SetFloat("_Glossiness", 0.35f); break;
                case Group.TrolleyBed: m.color = new Color(0.12f, 0.32f, 0.58f); m.SetFloat("_Glossiness", 0.45f); break;
                default:
                    if (part == 1) { m.color = new Color(0.9f, 0.72f, 0.12f); m.SetFloat("_Glossiness", 0.55f); m.SetFloat("_Metallic", 0.3f); }
                    else { m.color = new Color(0.06f, 0.06f, 0.06f); m.SetFloat("_Glossiness", 0.12f); }
                    break;
            }
            m.enableInstancing = true;
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }
    }
}
#endif
