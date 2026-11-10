using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;
using UnityEngine.Rendering;

namespace TractorSim
{
    /// <summary>
    /// The 3D tractor + trolley model, loaded at runtime from Resources/Art/TractorModel.bytes (made offline
    /// from the user's FBX by Tools/fbx2trm.py: parts already split – body, four wheels, trolley chassis,
    /// tipping bed, trolley wheels – in Unity coordinates). Textures: Resources/Art/TractorTex (UDIM tiles
    /// 1001..1005 = materials 0..4). No editor import step is involved, so what was verified offline is what
    /// the game shows.
    /// </summary>
    public static class TractorModel
    {
        public const int Body = 0, FL = 1, FR = 2, RL = 3, RR = 4, TrolleyChassis = 5, TrolleyBed = 6, TrolleyWheels = 7;

        class Part { public string name; public int group, tile; public Vector3 pivot; public Mesh mesh; }

        static bool tried;
        static readonly List<Part> parts = new List<Part>();
        static TractorRigInfo info;
        static Material[] mats;
        static readonly Dictionary<int, Material[]> tinted = new Dictionary<int, Material[]>();
        public static string Error { get; private set; }

        public static TractorRigInfo Info { get { Load(); return info; } }
        public static bool Available { get { Load(); return info != null; } }

        static void Load()
        {
            if (tried) return;
            tried = true;
            try
            {
                var ta = Resources.Load<TextAsset>("Art/TractorModel");
                if (ta == null) { Error = "model file missing"; return; }
                byte[] raw;
                using (var gz = new GZipStream(new MemoryStream(ta.bytes), CompressionMode.Decompress))
                using (var ms = new MemoryStream()) { gz.CopyTo(ms); raw = ms.ToArray(); }
                Parse(raw);
                if (!Validate()) { parts.Clear(); info = null; Debug.LogWarning("[TractorSim] Tractor model rejected: " + Error); return; }
                Debug.Log("[TractorSim] Tractor model loaded: " + parts.Count + " parts");
            }
            catch (System.Exception e)
            {
                Error = e.Message; parts.Clear(); info = null;
                Debug.LogWarning("[TractorSim] Tractor model failed to load: " + e);
            }
        }

        static void Parse(byte[] d)
        {
            var r = new BinaryReader(new MemoryStream(d));
            if (new string(r.ReadChars(4)) != "TRM1") throw new IOException("bad header");
            var kv = new Dictionary<string, float>();
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++) { string k = Str(r); kv[k] = r.ReadSingle(); }
            int np = r.ReadInt32();
            for (int p = 0; p < np; p++)
            {
                var part = new Part();
                part.name = Str(r);
                part.group = r.ReadByte(); part.tile = r.ReadByte();
                part.pivot = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                int nv = r.ReadInt32(), ni = r.ReadInt32();
                var pos = new Vector3[nv]; var nrm = new Vector3[nv]; var uv = new Vector2[nv]; var idx = new int[ni];
                for (int i = 0; i < nv; i++) pos[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                for (int i = 0; i < nv; i++) { sbyte x = r.ReadSByte(), y = r.ReadSByte(), z = r.ReadSByte(); nrm[i] = new Vector3(x, y, z) / 127f; }
                for (int i = 0; i < nv; i++) uv[i] = new Vector2(Half(r.ReadUInt16()), Half(r.ReadUInt16()));
                for (int i = 0; i < ni; i++) idx[i] = (int)r.ReadUInt32();
                var mesh = new Mesh { name = part.name + "_" + (1001 + part.tile) };
                mesh.indexFormat = nv > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                mesh.vertices = pos; mesh.normals = nrm; mesh.uv = uv; mesh.triangles = idx;
                mesh.RecalculateBounds();
                mesh.RecalculateTangents();
                mesh.UploadMeshData(true);
                part.mesh = mesh;
                parts.Add(part);
            }
            info = new TractorRigInfo();
            info.rearRadius = Get(kv, "rearRadius", info.rearRadius); info.frontRadius = Get(kv, "frontRadius", info.frontRadius);
            info.wheelBase = Get(kv, "wheelBase", info.wheelBase);
            info.rearTrack = Get(kv, "rearTrack", info.rearTrack); info.frontTrack = Get(kv, "frontTrack", info.frontTrack);
            info.rearWidth = Get(kv, "rearWidth", info.rearWidth); info.frontWidth = Get(kv, "frontWidth", info.frontWidth);
            info.length = Get(kv, "length", info.length); info.width = Get(kv, "width", info.width); info.height = Get(kv, "height", info.height);
            info.rearZ = Get(kv, "rearZ", info.rearZ); info.hitchZ = Get(kv, "hitchZ", info.hitchZ);
            info.exhaustTop = new Vector3(Get(kv, "exhaustX", 0.33f), Get(kv, "exhaustY", 2.05f), Get(kv, "exhaustZ", 0.72f));
            info.hoodSide = new Vector3(Get(kv, "hoodX", 0.27f), Get(kv, "hoodY", 1.16f), Get(kv, "hoodZ", 0.26f));
            info.seat = new Vector3(0f, Get(kv, "seatY", 1.49f), Get(kv, "seatZ", -0.52f));
            info.trolleyAxleZ = Get(kv, "trolleyAxleZ", info.trolleyAxleZ); info.trolleyWheelRadius = Get(kv, "trolleyWheelRadius", info.trolleyWheelRadius);
            info.trolleyTrack = Get(kv, "trolleyTrack", info.trolleyTrack);
            info.trolleyBedFrontZ = Get(kv, "trolleyBedFrontZ", info.trolleyBedFrontZ); info.trolleyBedLength = Get(kv, "trolleyBedLength", info.trolleyBedLength);
            info.trolleyBedWidth = Get(kv, "trolleyBedWidth", info.trolleyBedWidth); info.trolleyFloorY = Get(kv, "trolleyFloorY", info.trolleyFloorY);
            info.trolleySideHeight = Get(kv, "trolleySideHeight", info.trolleySideHeight);
            info.hasTrolley = HasGroup(TrolleyBed) && HasGroup(TrolleyWheels);
            info.textured = Resources.Load<Texture2D>("Art/TractorTex/Albedo_1001") != null;
        }

        static float Get(Dictionary<string, float> kv, string k, float def) { float v; return kv.TryGetValue(k, out v) && !float.IsNaN(v) ? v : def; }
        static string Str(BinaryReader r) { int l = r.ReadByte(); return System.Text.Encoding.UTF8.GetString(r.ReadBytes(l)); }
        static bool HasGroup(int g) { foreach (var p in parts) if (p.group == g) return true; return false; }

        static float Half(ushort h)
        {
            int s = (h >> 15) & 1, e = (h >> 10) & 0x1f, m = h & 0x3ff;
            float v;
            if (e == 0) v = m / 1024f * (float)System.Math.Pow(2.0, -14.0);
            else if (e == 31) v = m == 0 ? float.PositiveInfinity : float.NaN;
            else v = (1f + m / 1024f) * (float)System.Math.Pow(2.0, e - 15);
            return s == 1 ? -v : v;
        }

        /// <summary>Reject anything that would make an invisible or broken tractor.</summary>
        static bool Validate()
        {
            if (!HasGroup(Body)) { Error = "no body"; return false; }
            for (int g = FL; g <= RR; g++) if (!HasGroup(g)) { Error = "wheel group " + g + " missing"; return false; }
            if (info.rearRadius < 0.3f || info.rearRadius > 1.2f || info.frontRadius < 0.2f || info.frontRadius > info.rearRadius + 0.05f) { Error = "wheel radii " + info.rearRadius + "/" + info.frontRadius; return false; }
            if (info.wheelBase < 1.4f || info.wheelBase > 3.5f) { Error = "wheelbase " + info.wheelBase; return false; }
            if (info.rearTrack < 1.0f || info.rearTrack > 2.6f || info.frontTrack < 0.9f || info.frontTrack > 2.6f) { Error = "track " + info.rearTrack + "/" + info.frontTrack; return false; }
            if (info.height < 1.4f || info.height > 3.8f) { Error = "height " + info.height; return false; }
            Bounds b = new Bounds(); bool first = true;
            foreach (var p in parts)
            {
                if (p.group > RR) continue;
                var mb = p.mesh.bounds; mb.center += p.pivot;
                if (first) { b = mb; first = false; } else b.Encapsulate(mb);
            }
            if (b.size.y < 1.4f || b.size.z < 2.2f || b.size.z > 6f || Mathf.Abs(b.center.x) > 0.5f || b.min.y < -0.3f || b.min.y > 0.3f) { Error = "body bounds " + b; return false; }
            return true;
        }

        // ------------------------------------------------------------------ materials
        static Material[] Materials()
        {
            if (mats != null) return mats;
            mats = new Material[5];
            for (int t = 0; t < 5; t++)
            {
                int udim = 1001 + t;
                // prefer the material prepared at build time (shader variants guaranteed), else build one now
                var m = Resources.Load<Material>("Art/TractorMat_" + udim);
                if (m == null) m = MakeMaterial(udim);
                mats[t] = m;
            }
            return mats;
        }

        public static Material MakeMaterial(int udim)
        {
            var sh = Shader.Find("Standard");
            var tmpl = Resources.Load<Material>(MatLib.ResPath + "Opaque");
            var m = tmpl != null ? new Material(tmpl) : new Material(sh);
            m.name = "Tractor_" + udim;
            m.color = Color.white;
            var albedo = Resources.Load<Texture2D>("Art/TractorTex/Albedo_" + udim);
            if (albedo != null) m.mainTexture = albedo;
            else m.color = udim == 1005 ? new Color(0.12f, 0.12f, 0.12f) : udim == 1004 ? new Color(0.45f, 0.32f, 0.2f) : new Color(0.25f, 0.38f, 0.5f);
            var nrm = Resources.Load<Texture2D>("Art/TractorTex/Normal_" + udim);
            if (nrm != null) { m.SetTexture("_BumpMap", nrm); m.EnableKeyword("_NORMALMAP"); }
            var ms = Resources.Load<Texture2D>("Art/TractorTex/MetalSmooth_" + udim);
            if (ms != null) { m.SetTexture("_MetallicGlossMap", ms); m.EnableKeyword("_METALLICGLOSSMAP"); m.SetFloat("_GlossMapScale", 0.9f); }
            else { m.SetFloat("_Glossiness", 0.35f); m.SetFloat("_Metallic", 0.2f); }
            if (udim == 1003) MakeGlass(m);
            m.enableInstancing = true;
            return m;
        }

        /// <summary>Tile 1003 holds the cab glass and lights: alpha-blended (Standard "Transparent" mode).</summary>
        public static void MakeGlass(Material m)
        {
            m.SetFloat("_Mode", 3f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)BlendMode.One);
            m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.DisableKeyword("_ALPHATEST_ON"); m.DisableKeyword("_ALPHABLEND_ON"); m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = 3000;
        }

        /// <summary>Body materials tinted towards a brand colour (paint), wheels/glass untouched.</summary>
        static Material[] BrandMaterials(Color paint, float amount)
        {
            int key = ((int)(paint.r * 255) << 16) | ((int)(paint.g * 255) << 8) | (int)(paint.b * 255) | ((int)(amount * 100) << 24);
            Material[] set;
            if (tinted.TryGetValue(key, out set)) return set;
            var baseMats = Materials();
            set = new Material[5];
            for (int t = 0; t < 5; t++)
            {
                if (amount <= 0f || t == 2 || t == 4) { set[t] = baseMats[t]; continue; }   // glass and tyres keep their look
                var m = new Material(baseMats[t]);
                m.color = Color.Lerp(Color.white, paint * 1.7f, amount);
                set[t] = m;
            }
            tinted[key] = set;
            return set;
        }

        // ------------------------------------------------------------------ building
        static void AddPart(Transform parent, Part p, Material[] set)
        {
            var go = new GameObject(p.mesh.name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = p.mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = set[Mathf.Clamp(p.tile, 0, 4)];
            r.shadowCastingMode = p.tile == 2 ? ShadowCastingMode.Off : ShadowCastingMode.On;
        }

        /// <summary>Tractor model: "Body" plus "Wheel_FL/FR/RL/RR" pivots at the wheel centres (axle = local X).</summary>
        public static GameObject BuildTractor(Color paint, float tint)
        {
            if (!Available) return null;
            var set = BrandMaterials(paint, tint);
            var root = new GameObject("RigModel");
            var body = new GameObject("Body").transform; body.SetParent(root.transform, false);
            string[] names = { "Body", "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" };
            var pivots = new Transform[5]; pivots[0] = body;
            foreach (var p in parts)
            {
                if (p.group > RR) continue;
                if (pivots[p.group] == null)
                {
                    pivots[p.group] = new GameObject(names[p.group]).transform;
                    pivots[p.group].SetParent(root.transform, false);
                    pivots[p.group].localPosition = p.pivot;
                }
                AddPart(pivots[p.group], p, set);
            }
            return root;
        }

        /// <summary>Trolley model with origin at the hitch eye: "Chassis", "Bed" (pivot = tipping hinge), "Wheels" (pivot = axle).</summary>
        public static GameObject BuildTrolley()
        {
            if (!Available || !info.hasTrolley) return null;
            var set = Materials();
            var root = new GameObject("TrolleyModel");
            string[] names = { "Chassis", "Bed", "Wheels" };
            var pivots = new Transform[3];
            foreach (var p in parts)
            {
                if (p.group < TrolleyChassis) continue;
                int i = p.group - TrolleyChassis;
                if (pivots[i] == null)
                {
                    pivots[i] = new GameObject(names[i]).transform;
                    pivots[i].SetParent(root.transform, false);
                    pivots[i].localPosition = p.pivot;
                }
                AddPart(pivots[i], p, set);
            }
            return root;
        }
    }
}
