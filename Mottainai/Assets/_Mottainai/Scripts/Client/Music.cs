using UnityEngine;

namespace Mottainai.Client
{
    /// Procedural soundtrack: a cheerful 8-bar calypso loop + river ambience, synthesized at startup.
    /// CC0 clips in Resources/Audio/music and Resources/Audio/river override the synthesized ones.
    public class MusicPlayer : MonoBehaviour
    {
        public enum Mode { Menu, Game, Results }
        AudioSource music, river;
        float musicTarget = 0.3f, riverTarget = 0.18f;
        const int Rate = 22050;

        void Awake()
        {
            music = gameObject.AddComponent<AudioSource>();
            river = gameObject.AddComponent<AudioSource>();
            foreach (var s in new[] { music, river }) { s.loop = true; s.playOnAwake = false; s.spatialBlend = 0f; s.volume = 0f; }
            var mc = Resources.Load<AudioClip>("Audio/music");
            var rc = Resources.Load<AudioClip>("Audio/river");
            music.clip = mc != null ? mc : BuildSong();
            river.clip = rc != null ? rc : BuildRiver();
            music.Play();
            river.Play();
        }

        public void SetMode(Mode m)
        {
            musicTarget = m == Mode.Game ? 0.2f : m == Mode.Results ? 0.34f : 0.3f;
            riverTarget = m == Mode.Game ? 0.22f : 0.14f;
        }

        void Update()
        {
            float k = Mathf.Min(1f, Time.unscaledDeltaTime * 1.5f);
            music.volume = Mathf.Lerp(music.volume, musicTarget * VoiceDuck.Gain, k);
            river.volume = Mathf.Lerp(river.volume, riverTarget, k);
        }

        // ---------------- synthesis ----------------
        static float Midi(int m) => 440f * Mathf.Pow(2f, (m - 69) / 12f);

        static AudioClip BuildSong()
        {
            const float bpm = 112f;
            float beat = 60f / bpm;
            int bars = 8;
            int n = Mathf.CeilToInt(bars * 4 * beat * Rate);
            var buf = new float[n];
            var rng = new System.Random(7);
            int[] roots = { 48, 43, 45, 41, 48, 43, 41, 43 };          // C G Am F C G F G
            bool[] minor = { false, false, true, false, false, false, false, false };
            int[][] melody =
            {
                new[] { 0, -1, 2, 1, -1, 2, 3, -1 }, new[] { 3, 2, -1, 1, 2, -1, 0, -1 },
                new[] { 1, -1, 2, 3, 2, -1, 1, 0 }, new[] { 2, -1, 1, -1, 0, 1, 2, -1 }
            };
            for (int bar = 0; bar < bars; bar++)
            {
                int root = roots[bar];
                int third = minor[bar] ? 3 : 4;
                int[] tones = { 0, third, 7, 12 };
                float barStart = bar * 4 * beat;
                // bass: beats 1 and 3 root, "and of 2" fifth, "and of 4" octave pickup
                Pluck(buf, barStart, Midi(root - 12), 0.45f, 0.42f, 3f, Wave.Triangle);
                Pluck(buf, barStart + 1.5f * beat, Midi(root - 5), 0.3f, 0.3f, 4f, Wave.Triangle);
                Pluck(buf, barStart + 2f * beat, Midi(root - 12), 0.45f, 0.4f, 3f, Wave.Triangle);
                Pluck(buf, barStart + 3.5f * beat, Midi(root), 0.25f, 0.25f, 5f, Wave.Triangle);
                // ukulele-ish offbeat chord stabs
                for (int b = 0; b < 4; b++)
                    foreach (int iv in new[] { 0, third, 7 })
                        Pluck(buf, barStart + (b + 0.5f) * beat + iv * 0.004f, Midi(root + 12 + iv), 0.22f, 0.07f, 9f, Wave.Uke);
                // marimba melody (eighth notes)
                var pat = melody[bar % melody.Length];
                for (int e = 0; e < 8; e++)
                {
                    if (pat[e] < 0) continue;
                    Pluck(buf, barStart + e * 0.5f * beat, Midi(root + 24 + tones[pat[e]]), 0.35f, 0.11f, 6f, Wave.Marimba);
                }
                // drums
                for (int b = 0; b < 4; b++)
                {
                    float tb = barStart + b * beat;
                    if (b == 0 || b == 2) Kick(buf, tb, 0.55f);
                    else Clap(buf, tb, 0.22f, rng);
                    Shaker(buf, tb, 0.05f, rng);
                    Shaker(buf, tb + 0.5f * beat, 0.08f, rng);
                }
            }
            Normalize(buf, 0.8f);
            var clip = AudioClip.Create("MottainaiSong", n, 1, Rate, false);
            clip.SetData(buf, 0);
            return clip;
        }

        enum Wave { Triangle, Uke, Marimba }

        static void Pluck(float[] buf, float start, float freq, float dur, float amp, float decay, Wave w)
        {
            int s0 = (int)(start * Rate), len = (int)(dur * Rate);
            float phaseStep = freq / Rate;
            for (int i = 0; i < len; i++)
            {
                int idx = s0 + i;
                if (idx >= buf.Length) idx -= buf.Length; // wrap: seamless loop
                if (idx < 0 || idx >= buf.Length) continue;
                float t = (float)i / Rate;
                float env = Mathf.Exp(-t * decay) * Mathf.Clamp01(t * 400f) * Mathf.Clamp01((len - i) / (0.01f * Rate));
                float ph = i * phaseStep;
                float v;
                switch (w)
                {
                    case Wave.Triangle: { float f = ph - Mathf.Floor(ph); v = 4f * Mathf.Abs(f - 0.5f) - 1f; break; }
                    case Wave.Uke: v = Mathf.Sin(2f * Mathf.PI * ph) * 0.7f + Mathf.Sin(4f * Mathf.PI * ph) * 0.25f * Mathf.Exp(-t * 20f) + Mathf.Sin(6f * Mathf.PI * ph) * 0.12f; break;
                    default: v = Mathf.Sin(2f * Mathf.PI * ph) + Mathf.Sin(8f * Mathf.PI * ph) * 0.3f * Mathf.Exp(-t * 30f); break;
                }
                buf[idx] += v * env * amp;
            }
        }

        static void Kick(float[] buf, float start, float amp)
        {
            int s0 = (int)(start * Rate), len = (int)(0.2f * Rate);
            float ph = 0f;
            for (int i = 0; i < len && s0 + i < buf.Length; i++)
            {
                float t = (float)i / Rate;
                ph += (45f + 75f * Mathf.Exp(-t * 30f)) / Rate;
                buf[s0 + i] += Mathf.Sin(2f * Mathf.PI * ph) * Mathf.Exp(-t * 14f) * amp;
            }
        }

        static void Clap(float[] buf, float start, float amp, System.Random rng)
        {
            int s0 = (int)(start * Rate), len = (int)(0.14f * Rate);
            float prev = 0f;
            for (int i = 0; i < len && s0 + i < buf.Length; i++)
            {
                float t = (float)i / Rate;
                float nz = (float)rng.NextDouble() * 2f - 1f;
                float hp = nz - prev; prev = nz; // crude high-pass
                float env = Mathf.Exp(-t * 28f) + (t > 0.012f && t < 0.02f ? 0.6f : 0f);
                buf[s0 + i] += hp * env * amp;
            }
        }

        static void Shaker(float[] buf, float start, float amp, System.Random rng)
        {
            int s0 = (int)(start * Rate), len = (int)(0.05f * Rate);
            float prev = 0f;
            for (int i = 0; i < len && s0 + i < buf.Length; i++)
            {
                float t = (float)i / Rate;
                float nz = (float)rng.NextDouble() * 2f - 1f;
                float hp = nz - prev; prev = nz;
                buf[s0 + i] += hp * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.05f)) * amp;
            }
        }

        static AudioClip BuildRiver()
        {
            const float secs = 6f, fade = 0.6f;
            int n = (int)(secs * Rate), f = (int)(fade * Rate);
            var raw = new float[n + f];
            var rng = new System.Random(3);
            float lp1 = 0f, lp2 = 0f;
            for (int i = 0; i < raw.Length; i++)
            {
                float nz = (float)rng.NextDouble() * 2f - 1f;
                lp1 += (nz - lp1) * 0.035f;            // deep rumble
                lp2 += (nz - lp2) * 0.25f;             // brighter babble
                float t = (float)i / Rate;
                float burble = (lp2 - lp1) * (0.55f + 0.45f * Mathf.Sin(t * 5.3f + Mathf.Sin(t * 1.7f) * 2f));
                raw[i] = lp1 * 2.2f + burble * 0.6f;
            }
            var buf = new float[n];
            for (int i = 0; i < n; i++) buf[i] = raw[i];
            for (int i = 0; i < f; i++) { float k = (float)i / f; buf[i] = raw[i] * k + raw[n + i] * (1f - k); } // seamless loop
            Normalize(buf, 0.6f);
            var clip = AudioClip.Create("MottainaiRiver", n, 1, Rate, false);
            clip.SetData(buf, 0);
            return clip;
        }

        static void Normalize(float[] buf, float peak)
        {
            float mx = 0.0001f;
            for (int i = 0; i < buf.Length; i++) mx = Mathf.Max(mx, Mathf.Abs(buf[i]));
            float k = peak / mx;
            for (int i = 0; i < buf.Length; i++) buf[i] *= k;
        }
    }
}
