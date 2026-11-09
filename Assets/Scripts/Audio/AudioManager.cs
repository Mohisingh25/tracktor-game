using UnityEngine;

namespace TractorSim
{
    /// <summary>One-shot sounds (gear clunk, grind, indicator tick, impact) synthesised at startup, plus ambience.</summary>
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager I { get; private set; }
        AudioSource oneShots, ambience;
        AudioClip clunk, grind, tick, impact, click, birds;
        float volume = 0.9f;

        void Awake()
        {
            I = this;
            oneShots = gameObject.AddComponent<AudioSource>();
            oneShots.playOnAwake = false;
            ambience = gameObject.AddComponent<AudioSource>();
            ambience.loop = true;
            int sr = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 44100;
            clunk = Make("clunk", sr, 0.18f, (t, r) => Mathf.Sin(t * 2f * Mathf.PI * 90f) * Mathf.Exp(-t * 30f) + r * Mathf.Exp(-t * 60f) * 0.4f);
            grind = Make("grind", sr, 0.45f, (t, r) => (r * 0.6f + Mathf.Sign(Mathf.Sin(t * 2f * Mathf.PI * 1400f)) * 0.3f * (0.5f + 0.5f * Mathf.Sin(t * 2f * Mathf.PI * 37f))) * Mathf.Clamp01(1f - t / 0.45f));
            tick = Make("tick", sr, 0.03f, (t, r) => r * Mathf.Exp(-t * 300f) + Mathf.Sin(t * 2f * Mathf.PI * 2500f) * Mathf.Exp(-t * 200f) * 0.5f);
            impact = Make("impact", sr, 0.4f, (t, r) => (r * 0.8f + Mathf.Sin(t * 2f * Mathf.PI * 55f)) * Mathf.Exp(-t * 12f));
            click = Make("click", sr, 0.04f, (t, r) => Mathf.Sin(t * 2f * Mathf.PI * 1800f) * Mathf.Exp(-t * 150f));
            birds = MakeAmbience(sr);
            ambience.clip = birds;
            ambience.volume = 0.18f;
            ambience.Play();
        }

        public void SetVolume(float v) { volume = v; AudioListener.volume = v; }

        static AudioClip Make(string name, int sr, float seconds, System.Func<float, float, float> f)
        {
            int n = Mathf.CeilToInt(sr * seconds);
            var data = new float[n];
            var rnd = new System.Random(name.GetHashCode());
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr;
                data[i] = Mathf.Clamp(f(t, (float)rnd.NextDouble() * 2f - 1f) * 0.6f, -1f, 1f);
            }
            var c = AudioClip.Create(name, n, 1, sr, false);
            c.SetData(data, 0);
            return c;
        }

        /// <summary>Rural ambience: soft wind plus occasional chirps (6 s loop).</summary>
        static AudioClip MakeAmbience(int sr)
        {
            int n = sr * 6;
            var d = new float[n];
            var rnd = new System.Random(5);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float noise = (float)rnd.NextDouble() * 2f - 1f;
                lp += (noise - lp) * 0.02f;
                d[i] = lp * 0.5f;
            }
            // chirps
            for (int k = 0; k < 9; k++)
            {
                int start = rnd.Next(0, n - sr / 4);
                float f0 = 2800f + (float)rnd.NextDouble() * 1800f;
                int len = sr / 12;
                for (int j = 0; j < len; j++)
                {
                    float t = j / (float)sr;
                    float env = Mathf.Sin(Mathf.PI * j / len);
                    d[start + j] += Mathf.Sin(2f * Mathf.PI * (f0 + 900f * Mathf.Sin(t * 60f)) * t) * env * 0.12f;
                }
            }
            // crossfade the loop ends
            int fade = sr / 10;
            for (int i = 0; i < fade; i++) { float a = i / (float)fade; d[i] = d[i] * a + d[n - fade + i] * (1f - a); }
            var c = AudioClip.Create("ambience", n - fade, 1, sr, false);
            var trimmed = new float[n - fade];
            System.Array.Copy(d, trimmed, n - fade);
            c.SetData(trimmed, 0);
            return c;
        }

        public void Clunk() { oneShots.PlayOneShot(clunk, 0.8f); }
        public void Grind() { oneShots.PlayOneShot(grind, 0.9f); }
        public void Tick() { oneShots.PlayOneShot(tick, 0.5f); }
        public void Impact() { oneShots.PlayOneShot(impact, 1f); }
        public void Click() { oneShots.PlayOneShot(click, 0.5f); }
    }
}
