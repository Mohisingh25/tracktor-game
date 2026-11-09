using UnityEngine;

namespace TractorSim
{
    /// <summary>
    /// Real-time procedural 3-cylinder diesel: firing pulses at rpm/40 Hz with load-dependent
    /// knock, exhaust body, injector clatter, turbo-less intake hiss and a starter motor whine.
    /// Runs on the audio thread via OnAudioFilterRead, driven by values set from the main thread.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class EngineSoundSynth : MonoBehaviour
    {
        public TractorController tractor;
        public float masterVolume = 0.9f;

        // shared with the audio thread
        volatile float rpm, load, running, cranking, horn;
        int sampleRate;
        double phase, hornPhase1, hornPhase2, starterPhase;
        float lp1, lp2, noiseLp, hornLp, cycleGain = 1f, cycleNoise;
        uint rng = 22222;
        float smoothedRpm, smoothedLoad, smoothedRun;

        void Start()
        {
            sampleRate = AudioSettings.outputSampleRate;
            var src = GetComponent<AudioSource>();
            src.playOnAwake = true;
            src.loop = true;
            src.spatialBlend = 0f;   // engine heard "from the seat"; volume tied to camera distance below
            src.Play();
        }

        void Update()
        {
            if (tractor == null) return;
            var e = tractor.Engine;
            rpm = e.Rpm;
            load = Mathf.Clamp01(e.Rack);
            running = e.Running ? 1f : 0f;
            cranking = e.Cranking ? 1f : 0f;
            horn = tractor.Horn ? 1f : 0f;
            var cam = Camera.main;
            float dist = cam != null ? Vector3.Distance(cam.transform.position, tractor.transform.position) : 5f;
            GetComponent<AudioSource>().volume = masterVolume * Mathf.Clamp(6f / Mathf.Max(3f, dist), 0.15f, 1f);
        }

        float Rand()
        {
            rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
            return (rng & 0xFFFFFF) / (float)0xFFFFFF * 2f - 1f;
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            if (sampleRate == 0) sampleRate = 48000;
            float sr = sampleRate;
            float targetRpm = rpm, targetLoad = load, targetRun = Mathf.Max(running, cranking * 0.7f);
            for (int i = 0; i < data.Length; i += channels)
            {
                smoothedRpm += (targetRpm - smoothedRpm) * 0.0008f;
                smoothedLoad += (targetLoad - smoothedLoad) * 0.0006f;
                smoothedRun += (targetRun - smoothedRun) * 0.0004f;

                float fire = smoothedRpm / 40f;               // 3-cyl 4-stroke firing frequency
                double prev = phase;
                phase += fire / sr;
                if (phase >= 1.0)
                {
                    phase -= 1.0;
                    cycleGain = 0.85f + 0.3f * (Rand() * 0.5f + 0.5f);   // cycle-to-cycle variation
                    cycleNoise = 1f;
                }
                float p = (float)phase;
                // combustion pulse: sharp attack, exponential decay
                float pulse = Mathf.Exp(-p * (7f - smoothedLoad * 3f)) * cycleGain;
                // body: low harmonics of the firing frequency
                float body = Mathf.Sin(p * 6.2831853f) * 0.55f + Mathf.Sin(p * 12.566371f + 0.6f) * 0.25f + Mathf.Sin(p * 3.14159f) * 0.3f;
                float n = Rand();
                noiseLp += (n - noiseLp) * 0.25f;
                cycleNoise *= 0.9992f;
                // diesel clatter: filtered noise bursts at injection, louder under load and at idle
                float clatter = noiseLp * pulse * (0.35f + smoothedLoad * 0.5f);
                float raw = (pulse * 0.9f + body * 0.5f) * (0.55f + smoothedLoad * 0.7f) + clatter * 0.8f + n * 0.03f * smoothedLoad;
                // exhaust muffler: two-pole low-pass, opening with rpm
                float cutoff = 0.06f + smoothedRpm / 2500f * 0.12f + smoothedLoad * 0.05f;
                lp1 += (raw - lp1) * cutoff;
                lp2 += (lp1 - lp2) * cutoff;
                float engine = lp2 * 1.6f * smoothedRun;

                // starter motor whine
                float starter = 0f;
                if (cranking > 0.5f)
                {
                    starterPhase += 210.0 / sr;
                    if (starterPhase > 1.0) starterPhase -= 1.0;
                    starter = ((float)starterPhase * 2f - 1f) * 0.12f;
                }

                // twin-tone horn
                float hornS = 0f;
                if (horn > 0.5f)
                {
                    hornPhase1 += 415.0 / sr; if (hornPhase1 > 1.0) hornPhase1 -= 1.0;
                    hornPhase2 += 523.0 / sr; if (hornPhase2 > 1.0) hornPhase2 -= 1.0;
                    float sq = (hornPhase1 < 0.5 ? 1f : -1f) + (hornPhase2 < 0.5 ? 1f : -1f);
                    hornLp += (sq - hornLp) * 0.18f;
                    hornS = hornLp * 0.16f;
                }
                else hornLp *= 0.99f;

                float s = Mathf.Clamp(engine + starter + hornS, -1f, 1f);
                for (int c = 0; c < channels; c++) data[i + c] = s;
            }
        }
    }
}
