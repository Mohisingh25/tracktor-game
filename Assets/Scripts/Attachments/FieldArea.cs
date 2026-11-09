using UnityEngine;

namespace TractorSim
{
    /// <summary>
    /// A farm field that remembers what has been done to every half-metre of soil:
    /// stubble → cultivated / ploughed → rotavated seedbed → sown → sprayed.
    /// Implements paint it as they pass; the colour change is visible and pays per square metre.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class FieldArea : MonoBehaviour
    {
        public const byte Stubble = 0, Cultivated = 1, Ploughed = 2, Rotavated = 3, Sown = 4, Sprayed = 5;
        public static readonly string[] StateNames = { "Stubble", "Cultivated", "Ploughed", "Rotavated", "Sown", "Sprayed" };
        const float CellSize = 0.5f;
        const float RatePerM2 = 0.6f;   // ₹ per square metre per operation

        int nx, nz;
        byte[] cells;
        Texture2D tex;
        Color32[] px;
        bool dirty; float applyTimer;
        Vector2 sizeXZ;
        public int[] Counts { get; private set; }
        public int CellCount { get { return nx * nz; } }

        public void Init()
        {
            Vector3 s = transform.lossyScale;
            sizeXZ = new Vector2(s.x, s.z);
            nx = Mathf.Max(4, Mathf.RoundToInt(s.x / CellSize));
            nz = Mathf.Max(4, Mathf.RoundToInt(s.z / CellSize));
            cells = new byte[nx * nz];
            Counts = new int[6]; Counts[0] = nx * nz;
            tex = new Texture2D(nx, nz, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp; tex.filterMode = FilterMode.Bilinear;
            px = new Color32[nx * nz];
            for (int i = 0; i < px.Length; i++) px[i] = Col(Stubble, i);
            tex.SetPixels32(px); tex.Apply(false);

            // overlay sits just above the field surface
            var ov = new GameObject("FieldOverlay");
            ov.transform.SetParent(transform, false);
            ov.transform.localPosition = new Vector3(0f, 0.5f + 0.01f / Mathf.Max(0.01f, s.y), 0f);
            ov.transform.localEulerAngles = new Vector3(90f, 0f, 0f);
            ov.AddComponent<MeshFilter>().sharedMesh = MeshUtil.PrimMesh(PrimitiveType.Quad);
            var m = MatLib.Transparent(Color.white, 0.05f);
            m = new Material(m);
            m.mainTexture = tex;
            var mr = ov.AddComponent<MeshRenderer>();
            mr.sharedMaterial = m;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        Color32 Col(byte state, int i)
        {
            // small per-cell variation so it doesn't look flat
            int h = (i * 73856093) ^ (i >> 3) * 19349663;
            float n = ((h & 0xFF) / 255f - 0.5f) * 0.08f;
            Color c;
            switch (state)
            {
                case Cultivated: c = new Color(0.4f + n, 0.29f + n, 0.18f + n, 0.92f); break;
                case Ploughed: c = new Color(0.29f + n, 0.2f + n, 0.12f + n, 0.95f); break;
                case Rotavated: c = new Color(0.36f + n, 0.26f + n, 0.16f + n, 0.95f); break;
                case Sown: c = new Color(0.33f + n, 0.25f + n * 2f, 0.14f + n, 0.95f); break;
                case Sprayed: c = new Color(0.27f + n, 0.36f + n, 0.15f + n, 0.95f); break;
                default: c = new Color(0, 0, 0, 0); break;   // show the stubble ground texture underneath
            }
            return c;
        }

        static bool Allowed(WorkKind k, byte from, out byte to)
        {
            to = from;
            switch (k)
            {
                case WorkKind.Cultivate: if (from == Stubble || from == Ploughed) { to = Cultivated; return true; } break;
                case WorkKind.Harrow: if (from == Stubble || from == Ploughed) { to = Cultivated; return true; } break;
                case WorkKind.Plough: if (from != Ploughed) { to = Ploughed; return true; } break;
                case WorkKind.Rotavate: if (from <= Ploughed) { to = Rotavated; return true; } break;
                case WorkKind.Level: if (from == Ploughed) { to = Cultivated; return true; } break;
                case WorkKind.Seed: if (from == Cultivated || from == Rotavated) { to = Sown; return true; } break;
                case WorkKind.Spray: if (from == Sown) { to = Sprayed; return true; } break;
            }
            return false;
        }

        bool ToCell(Vector3 world, out int ix, out int iz)
        {
            Vector3 l = transform.InverseTransformPoint(world);   // local in [-0.5, 0.5]
            ix = Mathf.FloorToInt((l.x + 0.5f) * nx);
            iz = Mathf.FloorToInt((l.z + 0.5f) * nz);
            return ix >= 0 && iz >= 0 && ix < nx && iz < nz;
        }

        /// <summary>Paint a strip of the given width centred on pos. Returns ₹ earned for newly worked soil.</summary>
        public float Work(Vector3 pos, Vector3 right, float width, WorkKind kind)
        {
            if (cells == null) return 0f;
            int changed = 0;
            int samples = Mathf.Max(2, Mathf.CeilToInt(width / (CellSize * 0.5f)));
            for (int i = 0; i <= samples; i++)
            {
                Vector3 p = pos + right * (-width * 0.5f + width * i / samples);
                int ix, iz;
                if (!ToCell(p, out ix, out iz)) continue;
                int idx = iz * nx + ix;
                byte to;
                if (!Allowed(kind, cells[idx], out to)) continue;
                Counts[cells[idx]]--; Counts[to]++;
                cells[idx] = to;
                px[idx] = Col(to, idx);
                changed++;
            }
            if (changed > 0) dirty = true;
            return changed * CellSize * CellSize * RatePerM2;
        }

        /// <summary>Soil resistance multiplier at a point (unworked stubble is hardest).</summary>
        public float Hardness(Vector3 pos)
        {
            int ix, iz;
            if (cells == null || !ToCell(pos, out ix, out iz)) return 1.2f;
            switch (cells[iz * nx + ix])
            {
                case Stubble: return 1.3f;
                case Ploughed: return 1.0f;
                case Cultivated: return 0.8f;
                case Rotavated: return 0.6f;
                default: return 0.7f;
            }
        }

        public float Percent(byte state) { return Counts == null ? 0f : 100f * Counts[state] / Mathf.Max(1, CellCount); }

        void Update()
        {
            applyTimer -= Time.deltaTime;
            if (dirty && applyTimer <= 0f)
            {
                tex.SetPixels32(px); tex.Apply(false);
                dirty = false; applyTimer = 0.25f;
            }
        }
    }

    /// <summary>Money earned from field work and deliveries; saved locally.</summary>
    public static class Career
    {
        static float pending;
        public static System.Action<string> OnEarn;
        public static long Money { get { return SaveSystem.Data != null ? SaveSystem.Data.money : 0; } }

        public static void AddEarnings(float rupees)
        {
            pending += rupees;
            if (pending >= 25f)
            {
                int add = Mathf.FloorToInt(pending);
                pending -= add;
                SaveSystem.Data.money += add;
                if (OnEarn != null) OnEarn("+₹" + add);
            }
        }

        public static void Pay(int rupees, string why)
        {
            SaveSystem.Data.money += rupees;
            SaveSystem.SaveProgress();
            if (OnEarn != null) OnEarn("+₹" + rupees + "  " + why);
        }

        public static bool Spend(int rupees)
        {
            if (SaveSystem.Data.money < rupees) return false;
            SaveSystem.Data.money -= rupees;
            SaveSystem.SaveProgress();
            return true;
        }
    }
}
