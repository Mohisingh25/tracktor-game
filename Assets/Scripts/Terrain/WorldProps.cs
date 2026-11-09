using UnityEngine;

namespace TractorSim
{
    /// <summary>Reusable procedural props (conifers, signs, milestones, gates, sheds) and art-pack fallbacks.</summary>
    public static class WorldProps
    {
        static Mesh cone;

        public static Mesh Cone()
        {
            if (cone != null) return cone;
            const int n = 14;
            var v = new Vector3[n * 2 + 2]; var tris = new int[n * 6];
            v[0] = new Vector3(0, 1, 0); v[1] = Vector3.zero;
            for (int i = 0; i < n; i++)
            {
                float a = i / (float)n * Mathf.PI * 2f;
                v[2 + i] = new Vector3(Mathf.Cos(a) * 0.5f, 0, Mathf.Sin(a) * 0.5f);
                v[2 + n + i] = v[2 + i];
            }
            for (int i = 0; i < n; i++)
            {
                int a = 2 + i, b = 2 + (i + 1) % n;
                tris[i * 6] = 0; tris[i * 6 + 1] = b; tris[i * 6 + 2] = a;
                tris[i * 6 + 3] = 1; tris[i * 6 + 4] = 2 + n + i; tris[i * 6 + 5] = 2 + n + (i + 1) % n;
            }
            cone = new Mesh(); cone.vertices = v; cone.triangles = tris; cone.RecalculateNormals(); cone.RecalculateBounds();
            return cone;
        }

        public static GameObject ConePart(Transform parent, Vector3 pos, float diameter, float height, Material m)
        {
            var go = new GameObject("Cone");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = new Vector3(diameter, height, diameter);
            go.AddComponent<MeshFilter>().sharedMesh = Cone();
            go.AddComponent<MeshRenderer>().sharedMaterial = m;
            return go;
        }

        /// <summary>Himalayan deodar / pine: trunk plus stacked cones. Visual only (collider added separately).</summary>
        public static void Conifer(Transform vis, Vector3 p, float h, System.Random rnd, Material trunk, Material needles, Material needles2)
        {
            MeshUtil.Part(vis, PrimitiveType.Cylinder, p + new Vector3(0, h * 0.25f, 0), new Vector3(0.3f + h * 0.015f, h * 0.25f, 0.3f + h * 0.015f), Vector3.zero, trunk, "Trunk");
            int tiers = 4;
            for (int i = 0; i < tiers; i++)
            {
                float t = i / (float)tiers;
                float d = Mathf.Lerp(h * 0.42f, h * 0.12f, t);
                ConePart(vis, p + new Vector3(0, h * (0.18f + t * 0.62f), 0), d, h * 0.34f, rnd.NextDouble() > 0.5 ? needles : needles2);
            }
        }

        /// <summary>Uses an art-pack tree prefab when available, otherwise a procedural conifer.</summary>
        public static bool ArtTree(Transform parent, Vector3 p, float h, System.Random rnd)
        {
            var cat = ArtCatalog.Load();
            if (cat == null || cat.trees == null || cat.trees.Length == 0) return false;
            var prefab = cat.trees[rnd.Next(cat.trees.Length)];
            if (prefab == null) return false;
            var t = Object.Instantiate(prefab, parent);
            t.transform.position = p;
            t.transform.rotation = Quaternion.Euler(0, (float)rnd.NextDouble() * 360f, 0);
            t.transform.localScale *= h / 12f;
            return true;
        }

        public static void Collider(Transform parent, Vector3 center, Vector3 size, float yaw = 0f)
        {
            var go = new GameObject("Col"); go.transform.SetParent(parent, false);
            go.transform.position = center; go.transform.rotation = Quaternion.Euler(0, yaw, 0);
            go.AddComponent<BoxCollider>().size = size;
        }

        public static void Capsule(Transform parent, Vector3 center, float radius, float height)
        {
            var go = new GameObject("Col"); go.transform.SetParent(parent, false);
            go.transform.position = center;
            var c = go.AddComponent<CapsuleCollider>(); c.radius = radius; c.height = height;
        }

        public static void Text(Transform parent, string text, Vector3 localPos, float yaw, float size, Color c)
        {
            var go = new GameObject("Text"); go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localEulerAngles = new Vector3(0, yaw, 0);
            go.transform.localScale = Vector3.one * size;
            var tm = go.AddComponent<TextMesh>();
            tm.text = text; tm.font = UIFactory.DefaultFont; tm.fontSize = 48; tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center; tm.color = c; tm.fontStyle = FontStyle.Bold;
            go.GetComponent<MeshRenderer>().sharedMaterial = UIFactory.DefaultFont.material;
        }

        /// <summary>Road sign on a post facing direction 'facing' (world yaw).</summary>
        public static void Sign(Transform parent, Vector3 pos, float facingYaw, string text, Color board, Color textCol)
        {
            var s = new GameObject("Sign").transform; s.SetParent(parent, false);
            s.position = pos; s.rotation = Quaternion.Euler(0, facingYaw, 0);
            var post = MatLib.Opaque(new Color(0.45f, 0.46f, 0.48f), 0.5f, 0.6f);
            MeshUtil.Part(s, PrimitiveType.Cylinder, new Vector3(0, 1.1f, 0), new Vector3(0.08f, 1.1f, 0.08f), Vector3.zero, post, "Post");
            MeshUtil.Part(s, PrimitiveType.Cube, new Vector3(0, 2.25f, 0), new Vector3(1.6f, 0.8f, 0.05f), Vector3.zero, MatLib.Opaque(board, 0.3f, 0f), "Board");
            Text(s, text, new Vector3(0, 2.25f, 0.04f), 180f, 0.03f, textCol);
            Collider(parent, pos + Vector3.up * 1f, new Vector3(0.2f, 2f, 0.2f));
        }

        /// <summary>Indian milestone: white stone with a yellow top.</summary>
        public static void Milestone(Transform parent, Vector3 pos, float yaw, string line1, string line2)
        {
            var s = new GameObject("Milestone").transform; s.SetParent(parent, false);
            s.position = pos; s.rotation = Quaternion.Euler(0, yaw, 0);
            MeshUtil.Part(s, PrimitiveType.Cube, new Vector3(0, 0.35f, 0), new Vector3(0.5f, 0.7f, 0.18f), Vector3.zero, MatLib.Opaque(new Color(0.93f, 0.93f, 0.9f), 0.2f, 0f), "Stone");
            MeshUtil.Part(s, PrimitiveType.Cylinder, new Vector3(0, 0.72f, 0), new Vector3(0.5f, 0.09f, 0.18f), new Vector3(90, 0, 0), MatLib.Opaque(new Color(0.95f, 0.78f, 0.1f), 0.3f, 0f), "Top");
            Text(s, line1, new Vector3(0, 0.5f, 0.1f), 180f, 0.008f, new Color(0.1f, 0.1f, 0.1f));
            Text(s, line2, new Vector3(0, 0.3f, 0.1f), 180f, 0.011f, new Color(0.1f, 0.1f, 0.1f));
            Collider(parent, pos + Vector3.up * 0.35f, new Vector3(0.5f, 0.7f, 0.2f), yaw);
        }

        /// <summary>Checkpoint arch across the road (visual only).</summary>
        public static Transform Gate(Transform parent, Vector3 pos, float yaw, float width, string label)
        {
            var g = new GameObject("CheckpointGate").transform; g.SetParent(parent, false);
            g.position = pos; g.rotation = Quaternion.Euler(0, yaw, 0);
            var orange = MatLib.Emissive(new Color(1f, 0.5f, 0.05f), new Color(0.6f, 0.25f, 0f));
            var white = MatLib.Opaque(Color.white, 0.4f, 0f);
            for (int s = -1; s <= 1; s += 2) MeshUtil.Part(g, PrimitiveType.Cylinder, new Vector3(s * width * 0.5f, 2.5f, 0), new Vector3(0.18f, 2.5f, 0.18f), Vector3.zero, white, "Pole");
            MeshUtil.Part(g, PrimitiveType.Cube, new Vector3(0, 4.7f, 0), new Vector3(width, 0.7f, 0.08f), Vector3.zero, orange, "Banner");
            Text(g, label, new Vector3(0, 4.7f, -0.06f), 0f, 0.05f, Color.white);
            Text(g, label, new Vector3(0, 4.7f, 0.06f), 180f, 0.05f, Color.white);
            return g;
        }

        /// <summary>Open-sided farm shed / godown.</summary>
        public static void Shed(Transform vis, Transform cols, Vector3 c, float yaw, float w, float d, float h, Material wall, Material roof)
        {
            var q = Quaternion.Euler(0, yaw, 0);
            MeshUtil.Part(vis, PrimitiveType.Cube, c + q * new Vector3(0, h * 0.5f, d * 0.5f), new Vector3(w, h, 0.25f), new Vector3(0, yaw, 0), wall, "BackWall");
            for (int s = -1; s <= 1; s += 2)
                MeshUtil.Part(vis, PrimitiveType.Cube, c + q * new Vector3(s * w * 0.5f, h * 0.5f, 0), new Vector3(0.25f, h, d), new Vector3(0, yaw, 0), wall, "SideWall");
            MeshUtil.Part(vis, PrimitiveType.Cube, c + q * new Vector3(0, h + 0.15f, 0), new Vector3(w + 0.8f, 0.12f, d + 1.2f), new Vector3(-6f, yaw, 0), roof, "Roof");
            Collider(cols, c + q * new Vector3(0, h * 0.5f, d * 0.5f), new Vector3(w, h, 0.3f), yaw);
            for (int s = -1; s <= 1; s += 2) Collider(cols, c + q * new Vector3(s * w * 0.5f, h * 0.5f, 0), new Vector3(0.3f, h, d), yaw);
        }
    }
}
