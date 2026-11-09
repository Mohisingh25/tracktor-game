using System.Collections.Generic;
using UnityEngine;

namespace TractorSim
{
    /// <summary>Helpers to assemble models from primitives and simple generated meshes.</summary>
    public static class MeshUtil
    {
        static readonly Dictionary<PrimitiveType, Mesh> primMeshes = new Dictionary<PrimitiveType, Mesh>();

        public static Mesh PrimMesh(PrimitiveType t)
        {
            Mesh m;
            if (primMeshes.TryGetValue(t, out m) && m != null) return m;
            var go = GameObject.CreatePrimitive(t);
            m = go.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(go);
            primMeshes[t] = m;
            return m;
        }

        /// <summary>Visual-only primitive (no collider).</summary>
        public static GameObject Part(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Vector3 euler, Material mat, string name = null)
        {
            var go = new GameObject(name ?? type.ToString());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localEulerAngles = euler;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = PrimMesh(type);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            return go;
        }

        public static GameObject Box(Transform parent, Vector3 pos, Vector3 size, Material mat, string name = null, Vector3? euler = null)
        {
            return Part(parent, PrimitiveType.Cube, pos, size, euler ?? Vector3.zero, mat, name);
        }

        /// <summary>Cylinder lying along local X (diameter d, length len).</summary>
        public static GameObject CylX(Transform parent, Vector3 pos, float d, float len, Material mat, string name = null)
        {
            return Part(parent, PrimitiveType.Cylinder, pos, new Vector3(d, len * 0.5f, d), new Vector3(0, 0, 90), mat, name);
        }

        public static GameObject CylY(Transform parent, Vector3 pos, float d, float len, Material mat, string name = null)
        {
            return Part(parent, PrimitiveType.Cylinder, pos, new Vector3(d, len * 0.5f, d), Vector3.zero, mat, name);
        }

        public static GameObject CylZ(Transform parent, Vector3 pos, float d, float len, Material mat, string name = null)
        {
            return Part(parent, PrimitiveType.Cylinder, pos, new Vector3(d, len * 0.5f, d), new Vector3(90, 0, 0), mat, name);
        }

        /// <summary>Merge all MeshRenderers under root that share a material into single meshes (fewer draw calls).</summary>
        public static void CombineByMaterial(GameObject root)
        {
            var groups = new Dictionary<Material, List<CombineInstance>>();
            var toDelete = new List<GameObject>();
            var rootInv = root.transform.worldToLocalMatrix;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.gameObject == root) continue;
                var mr = mf.GetComponent<MeshRenderer>();
                if (mr == null || mf.sharedMesh == null) continue;
                List<CombineInstance> list;
                if (!groups.TryGetValue(mr.sharedMaterial, out list)) { list = new List<CombineInstance>(); groups[mr.sharedMaterial] = list; }
                list.Add(new CombineInstance { mesh = mf.sharedMesh, transform = rootInv * mf.transform.localToWorldMatrix });
                toDelete.Add(mf.gameObject);
            }
            foreach (var kv in groups)
            {
                var go = new GameObject("Combined_" + kv.Key.name);
                go.transform.SetParent(root.transform, false);
                var mesh = new Mesh();
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.CombineMeshes(kv.Value.ToArray(), true, true);
                mesh.RecalculateBounds();
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = kv.Key;
            }
            foreach (var g in toDelete) Object.DestroyImmediate(g);
        }

        /// <summary>A height-field mesh (x,z in [-size/2,size/2]) from a height function, with collider.</summary>
        public static GameObject HeightField(string name, Transform parent, Vector3 pos, float sizeX, float sizeZ, int res, System.Func<float, float, float> height, Material mat, Vector2 uvScale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var verts = new Vector3[(res + 1) * (res + 1)];
            var uvs = new Vector2[verts.Length];
            for (int z = 0; z <= res; z++)
                for (int x = 0; x <= res; x++)
                {
                    float fx = (float)x / res - 0.5f, fz = (float)z / res - 0.5f;
                    float wx = fx * sizeX, wz = fz * sizeZ;
                    verts[z * (res + 1) + x] = new Vector3(wx, height(wx, wz), wz);
                    uvs[z * (res + 1) + x] = new Vector2((fx + 0.5f) * uvScale.x, (fz + 0.5f) * uvScale.y);
                }
            var tris = new int[res * res * 6];
            int t = 0;
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    int i = z * (res + 1) + x;
                    tris[t++] = i; tris[t++] = i + res + 1; tris[t++] = i + 1;
                    tris[t++] = i + 1; tris[t++] = i + res + 1; tris[t++] = i + res + 2;
                }
            var mesh = new Mesh();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = verts; mesh.uv = uvs; mesh.triangles = tris;
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            return go;
        }

        /// <summary>
        /// Ramp/hill prism: profile in the YZ plane given as (z, y) points along the length,
        /// extruded across width (X). Includes side walls and a collider.
        /// </summary>
        public static GameObject Profile(string name, Transform parent, Vector3 pos, float yawDeg, float width, Vector2[] profile, Material topMat, Material sideMat, float uvPerMeter = 0.25f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localEulerAngles = new Vector3(0, yawDeg, 0);
            var v = new List<Vector3>(); var uv = new List<Vector2>();
            var top = new List<int>(); var side = new List<int>();
            float hw = width * 0.5f;
            float dist = 0f;
            // top surface
            for (int i = 0; i < profile.Length; i++)
            {
                if (i > 0) dist += Vector2.Distance(profile[i], profile[i - 1]);
                v.Add(new Vector3(-hw, profile[i].y, profile[i].x)); uv.Add(new Vector2(0, dist * uvPerMeter));
                v.Add(new Vector3(hw, profile[i].y, profile[i].x)); uv.Add(new Vector2(width * uvPerMeter, dist * uvPerMeter));
            }
            for (int i = 0; i < profile.Length - 1; i++)
            {
                int a = i * 2;
                top.Add(a); top.Add(a + 2); top.Add(a + 1);
                top.Add(a + 1); top.Add(a + 2); top.Add(a + 3);
            }
            // side walls down to y = 0
            for (int s = 0; s < 2; s++)
            {
                float x = s == 0 ? -hw : hw;
                int baseIdx = v.Count;
                for (int i = 0; i < profile.Length; i++)
                {
                    v.Add(new Vector3(x, profile[i].y, profile[i].x)); uv.Add(new Vector2(profile[i].x * uvPerMeter, profile[i].y * uvPerMeter));
                    v.Add(new Vector3(x, -0.05f, profile[i].x)); uv.Add(new Vector2(profile[i].x * uvPerMeter, 0));
                }
                for (int i = 0; i < profile.Length - 1; i++)
                {
                    int a = baseIdx + i * 2;
                    if (s == 0) { side.Add(a); side.Add(a + 1); side.Add(a + 2); side.Add(a + 2); side.Add(a + 1); side.Add(a + 3); }
                    else { side.Add(a); side.Add(a + 2); side.Add(a + 1); side.Add(a + 2); side.Add(a + 3); side.Add(a + 1); }
                }
            }
            var mesh = new Mesh();
            mesh.SetVertices(v); mesh.SetUVs(0, uv);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(top, 0); mesh.SetTriangles(side, 1);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = new[] { topMat, sideMat };
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            return go;
        }
    }
}
