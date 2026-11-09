using System.Collections.Generic;
using UnityEngine;

namespace TractorSim
{
    /// <summary>Procedurally generated textures & sprites (no imported art needed).</summary>
    public static class ProcTex
    {
        static readonly Dictionary<string, Texture2D> texCache = new Dictionary<string, Texture2D>();
        static readonly Dictionary<string, Sprite> spriteCache = new Dictionary<string, Sprite>();

        static Texture2D New(int w, int h, bool repeat)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true);
            t.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Bilinear;
            t.anisoLevel = 2;
            return t;
        }

        /// <summary>Tileable noisy ground texture.</summary>
        public static Texture2D Ground(string key, Color a, Color b, float scale, float speckle, int seed, bool stripes = false)
        {
            Texture2D t;
            if (texCache.TryGetValue(key, out t)) return t;
            const int S = 128;
            t = New(S, S, true);
            var px = new Color[S * S];
            var rnd = new System.Random(seed);
            float ox = (float)rnd.NextDouble() * 100f, oy = (float)rnd.NextDouble() * 100f;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = (float)x / S, v = (float)y / S;
                    // tileable noise via sampling on a torus-ish combination
                    float n = TileNoise(u, v, scale, ox, oy) * 0.65f + TileNoise(u, v, scale * 3f, oy, ox) * 0.35f;
                    if (stripes) n = n * 0.5f + 0.5f * (0.5f + 0.5f * Mathf.Sin(v * Mathf.PI * 2f * 8f));
                    var c = Color.Lerp(a, b, n);
                    float s = (float)rnd.NextDouble();
                    if (s < speckle) c *= 0.75f + 0.5f * (float)rnd.NextDouble();
                    c.a = 1f;
                    px[y * S + x] = c;
                }
            t.SetPixels(px);
            t.Apply(true);
            texCache[key] = t;
            return t;
        }

        static float TileNoise(float u, float v, float scale, float ox, float oy)
        {
            // blend 4 offset samples so the texture wraps seamlessly
            float a = Mathf.PerlinNoise(ox + u * scale, oy + v * scale);
            float b = Mathf.PerlinNoise(ox + (u - 1f) * scale, oy + v * scale);
            float c = Mathf.PerlinNoise(ox + u * scale, oy + (v - 1f) * scale);
            float d = Mathf.PerlinNoise(ox + (u - 1f) * scale, oy + (v - 1f) * scale);
            float ab = Mathf.Lerp(a, b, u);
            float cd = Mathf.Lerp(c, d, u);
            return Mathf.Lerp(ab, cd, v);
        }

        public static Texture2D Road()
        {
            Texture2D t;
            if (texCache.TryGetValue("road", out t)) return t;
            const int W = 64, H = 128;
            t = New(W, H, true);
            var rnd = new System.Random(7);
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float g = 0.27f + (float)rnd.NextDouble() * 0.06f;
                    var c = new Color(g, g, g * 1.02f, 1f);
                    bool centre = x >= 30 && x <= 33 && (y % 64) < 36;
                    bool edge = x <= 2 || x >= W - 3;
                    if (centre) c = new Color(0.85f, 0.85f, 0.8f);
                    if (edge) c = new Color(0.8f, 0.8f, 0.75f);
                    px[y * W + x] = c;
                }
            t.SetPixels(px); t.Apply(true);
            texCache["road"] = t;
            return t;
        }

        public static Texture2D Brick()
        {
            Texture2D t;
            if (texCache.TryGetValue("brick", out t)) return t;
            const int S = 64;
            t = New(S, S, true);
            var rnd = new System.Random(3);
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    int row = y / 8;
                    int xo = (x + (row % 2) * 8) % 16;
                    bool mortar = (y % 8) == 0 || xo == 0;
                    var c = mortar ? new Color(0.72f, 0.68f, 0.6f) : new Color(0.62f, 0.3f, 0.2f) * (0.85f + 0.3f * (float)rnd.NextDouble());
                    c.a = 1; px[y * S + x] = c;
                }
            t.SetPixels(px); t.Apply(true);
            texCache["brick"] = t;
            return t;
        }

        public static Texture2D Tread()
        {
            Texture2D t;
            if (texCache.TryGetValue("tread", out t)) return t;
            const int S = 64;
            t = New(S, S, true);
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    // chevron lugs
                    float u = (float)x / S, v = (float)y / S;
                    float chevron = Mathf.Repeat(v * 4f + Mathf.Abs(u - 0.5f) * 1.6f, 1f);
                    bool lug = chevron < 0.45f && Mathf.Abs(u - 0.5f) > 0.04f;
                    float g = lug ? 0.16f : 0.07f;
                    px[y * S + x] = new Color(g, g, g, 1f);
                }
            t.SetPixels(px); t.Apply(true);
            texCache["tread"] = t;
            return t;
        }

        public static Texture2D SoftCircle()
        {
            Texture2D t;
            if (texCache.TryGetValue("soft", out t)) return t;
            const int S = 64;
            t = New(S, S, false);
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = (x + 0.5f) / S * 2f - 1f, dy = (y + 0.5f) / S * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - d);
                    a = a * a * (3f - 2f * a);
                    px[y * S + x] = new Color(1f, 1f, 1f, a);
                }
            t.SetPixels(px); t.Apply(true);
            texCache["soft"] = t;
            return t;
        }

        // ---------------- UI sprites ----------------

        static Sprite MakeSprite(string key, int size, System.Func<float, float, Color> shader, Vector4 border = default(Vector4))
        {
            Sprite s;
            if (spriteCache.TryGetValue(key, out s)) return s;
            var t = New(size, size, false);
            t.filterMode = FilterMode.Bilinear;
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    px[y * size + x] = shader((x + 0.5f) / size * 2f - 1f, (y + 0.5f) / size * 2f - 1f);
            t.SetPixels(px); t.Apply(false);
            s = Sprite.Create(t, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
            spriteCache[key] = s;
            return s;
        }

        static float AA(float d, float px) { return Mathf.Clamp01(0.5f - d / px); }

        public static Sprite Circle()
        {
            return MakeSprite("circle", 128, (x, y) =>
            {
                float d = Mathf.Sqrt(x * x + y * y) - 1f;
                return new Color(1, 1, 1, AA(d, 2f / 128f * 2f));
            });
        }

        public static Sprite Ring(float thickness)
        {
            return MakeSprite("ring" + thickness, 256, (x, y) =>
            {
                float r = Mathf.Sqrt(x * x + y * y);
                float d = Mathf.Abs(r - (1f - thickness * 0.5f)) - thickness * 0.5f;
                return new Color(1, 1, 1, AA(d, 2f / 256f * 2f));
            });
        }

        /// <summary>Rounded rectangle, 9-sliced.</summary>
        public static Sprite RoundRect()
        {
            return MakeSprite("rrect", 64, (x, y) =>
            {
                float r = 0.45f;
                float qx = Mathf.Abs(x) - (1f - r), qy = Mathf.Abs(y) - (1f - r);
                float d = Mathf.Sqrt(Mathf.Max(qx, 0) * Mathf.Max(qx, 0) + Mathf.Max(qy, 0) * Mathf.Max(qy, 0)) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
                return new Color(1, 1, 1, AA(d, 2f / 64f * 2f));
            }, new Vector4(20, 20, 20, 20));
        }

        public static Sprite SteeringWheel()
        {
            return MakeSprite("steer", 256, (x, y) =>
            {
                float r = Mathf.Sqrt(x * x + y * y);
                float rim = Mathf.Abs(r - 0.86f) - 0.11f;
                float hub = r - 0.2f;
                // three spokes (top-left, top-right, bottom)
                float best = 9f;
                float[] ang = { 200f, 340f, 90f + 180f };
                foreach (float a in ang)
                {
                    float ra = a * Mathf.Deg2Rad;
                    float dx = Mathf.Cos(ra), dy = Mathf.Sin(ra);
                    float t = Mathf.Clamp(x * dx + y * dy, 0f, 0.8f);
                    float px = x - dx * t, py = y - dy * t;
                    best = Mathf.Min(best, Mathf.Sqrt(px * px + py * py) - 0.07f);
                }
                float d = Mathf.Min(Mathf.Min(rim, hub), best);
                var c = new Color(0.12f, 0.12f, 0.13f, AA(d, 0.02f));
                if (r < 0.2f) c = Color.Lerp(c, new Color(0.75f, 0.1f, 0.1f, c.a), 0.8f);
                // top marker
                if (y > 0.74f && y < 0.97f && Mathf.Abs(x) < 0.06f) c = new Color(0.95f, 0.7f, 0.1f, c.a);
                return c;
            });
        }

        public static Sprite Needle()
        {
            return MakeSprite("needle", 128, (x, y) =>
            {
                // vertical tapered needle from centre up
                if (y < -0.15f) return new Color(1, 1, 1, 0);
                float w = Mathf.Lerp(0.07f, 0.01f, Mathf.Clamp01((y + 0.15f) / 1.1f));
                float d = Mathf.Abs(x) - w;
                if (y > 0.95f) d = 1f;
                return new Color(1, 1, 1, AA(d, 0.03f));
            });
        }

        public static Sprite Arrow()
        {
            return MakeSprite("arrow", 128, (x, y) =>
            {
                // left-pointing triangle
                float d = Mathf.Max(Mathf.Abs(y) * 1.2f - (x + 0.6f) * 0.75f, x - 0.55f);
                return new Color(1, 1, 1, AA(d, 0.03f));
            });
        }

        /// <summary>Gauge face: dark disc with tick marks over a 240° sweep.</summary>
        public static Sprite GaugeFace(int majorTicks, int redFromTick)
        {
            return MakeSprite("gauge" + majorTicks + "_" + redFromTick, 256, (x, y) =>
            {
                float r = Mathf.Sqrt(x * x + y * y);
                if (r > 1f) return new Color(0, 0, 0, AA(r - 1f, 0.01f));
                var c = new Color(0.06f, 0.07f, 0.08f, 0.88f);
                if (r > 0.95f) c = new Color(0.55f, 0.55f, 0.55f, 1f);
                float ang = Mathf.Atan2(x, y) * Mathf.Rad2Deg; // 0 = up, + clockwise
                if (ang >= -120f && ang <= 120f)
                {
                    float t = (ang + 120f) / 240f;
                    float minor = t * majorTicks * 5f;
                    float major = t * majorTicks;
                    bool isMajor = Mathf.Abs(major - Mathf.Round(major)) * 240f / majorTicks < 1.3f;
                    bool isMinor = Mathf.Abs(minor - Mathf.Round(minor)) * 240f / (majorTicks * 5f) < 0.6f;
                    bool red = major >= redFromTick;
                    if (isMajor && r > 0.74f && r < 0.92f) c = red ? new Color(0.95f, 0.2f, 0.15f) : Color.white;
                    else if (isMinor && r > 0.84f && r < 0.92f) c = red ? new Color(0.8f, 0.2f, 0.15f) : new Color(0.75f, 0.75f, 0.75f);
                    if (red && r > 0.92f && r < 0.95f) c = new Color(0.8f, 0.15f, 0.1f);
                }
                return c;
            });
        }
    }
}
