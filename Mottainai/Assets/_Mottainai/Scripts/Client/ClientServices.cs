using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Mottainai.Client
{
    /// Procedural SFX (fallback when no CC0 audio in Resources/Audio).
    public class Sfx : MonoBehaviour
    {
        public enum Id { Splash, Catch, Valuable, Toxic, Miss, Hit, Shield, Beep, Go, Sync, Throw, Combo }
        AudioClip[] clips;
        AudioSource src;
        const int Rate = 22050;

        void Awake()
        {
            src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            var names = Enum.GetNames(typeof(Id));
            clips = new AudioClip[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                var c = Resources.Load<AudioClip>("Audio/" + names[i].ToLowerInvariant());
                clips[i] = c != null ? c : Make((Id)i);
            }
        }

        public void Play(Id id, float vol = 1f) { if (clips != null) src.PlayOneShot(clips[(int)id], vol); }

        static AudioClip Make(Id id)
        {
            float len = id switch { Id.Splash => 0.3f, Id.Hit => 0.6f, Id.Shield => 0.6f, Id.Go => 0.4f, Id.Sync => 0.35f, Id.Valuable => 0.5f, Id.Combo => 0.4f, _ => 0.18f };
            int n = (int)(Rate * len);
            var data = new float[n];
            var rng = new System.Random((int)id + 7);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate, k = (float)i / n;
                float env = Mathf.Exp(-k * 5f) * Mathf.Clamp01(t * 200f);
                float noise = (float)rng.NextDouble() * 2f - 1f;
                float v;
                switch (id)
                {
                    case Id.Splash: lp += (noise - lp) * 0.25f; v = lp * env * 1.6f; break;
                    case Id.Catch: v = Mathf.Sin(2 * Mathf.PI * t * (660 + 600 * k)) * env * 0.5f; break;
                    case Id.Valuable: v = (Mathf.Sin(2 * Mathf.PI * t * 988) + Mathf.Sin(2 * Mathf.PI * t * 1319 * (k > 0.3f ? 1.5f : 1f))) * env * 0.3f; break;
                    case Id.Toxic: v = Mathf.Sign(Mathf.Sin(2 * Mathf.PI * t * (220 - 90 * k))) * env * 0.25f; break;
                    case Id.Miss: v = Mathf.Sin(2 * Mathf.PI * t * (420 - 200 * k)) * env * 0.35f; break;
                    case Id.Hit: lp += (noise - lp) * 0.08f; v = (lp * 1.5f + Mathf.Sin(2 * Mathf.PI * t * (90 - 40 * k))) * Mathf.Exp(-k * 4f) * 0.7f; break;
                    case Id.Shield: v = Mathf.Sin(2 * Mathf.PI * t * (520 + 520 * Mathf.Floor(k * 4f) / 4f)) * Mathf.Exp(-k * 2f) * 0.35f; break;
                    case Id.Beep: v = Mathf.Sin(2 * Mathf.PI * t * 880) * env * 0.4f; break;
                    case Id.Go: v = Mathf.Sin(2 * Mathf.PI * t * 1320) * Mathf.Exp(-k * 3f) * 0.45f; break;
                    case Id.Sync: v = (Mathf.Sin(2 * Mathf.PI * t * 523) + Mathf.Sin(2 * Mathf.PI * t * 659) + Mathf.Sin(2 * Mathf.PI * t * 784)) * env * 0.22f; break;
                    case Id.Throw: lp += (noise - lp) * 0.5f; v = lp * Mathf.Sin(Mathf.PI * k) * 0.4f; break;
                    case Id.Combo: v = Mathf.Sin(2 * Mathf.PI * t * (880 * (1 + Mathf.Floor(k * 3f) * 0.25f))) * Mathf.Exp(-k * 2f) * 0.35f; break;
                    default: v = 0; break;
                }
                data[i] = Mathf.Clamp(v, -1f, 1f);
            }
            var clip = AudioClip.Create(id.ToString(), n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }

    /// Drops URP render scale to 0.8 if FPS < 45 for 3 s.
    public class PerfGovernor : MonoBehaviour
    {
        float low, fps = 60f;
        bool dropped;
        float originalScale = 1f;
        int originalMsaa = 2;
        UniversalRenderPipelineAsset asset;

        void Start()
        {
            asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (asset != null) originalScale = asset.renderScale;
            if (asset != null) { originalMsaa = asset.msaaSampleCount; if (SystemInfo.systemMemorySize >= 6000 && !Application.isEditor) asset.msaaSampleCount = 4; }
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f) return;
            fps = Mathf.Lerp(fps, 1f / dt, 0.05f);
            if (dropped || asset == null) return;
            low = fps < 45f ? low + dt : 0f;
            if (low > 3f)
            {
                dropped = true;
                asset.renderScale = 0.8f;
                asset.msaaSampleCount = 2;
                Debug.Log("[Perf] FPS < 45 for 3 s -> render scale 0.8");
            }
        }

        void OnDestroy() { if (asset == null) return; asset.renderScale = originalScale; asset.msaaSampleCount = originalMsaa; }
        void OnApplicationQuit() => OnDestroy();
        public bool Dropped => dropped;
    }
}
