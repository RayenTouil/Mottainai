using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Mottainai.Client
{
    /// Runtime URP post-processing: bloom, color grading, vignette + gameplay pulses (hit / sync / shield).
    public class PostFx : MonoBehaviour
    {
        Volume volume;
        Bloom bloom;
        ColorAdjustments color;
        Vignette vignette;
        float hitPulse, syncPulse;
        public float ShieldLeft;
        public bool Quality = true;

        public static PostFx Create(Transform parent, Camera cam)
        {
            var go = new GameObject("PostFx");
            go.transform.SetParent(parent, false);
            var fx = go.AddComponent<PostFx>();
            fx.Build(cam);
            return fx;
        }

        void Build(Camera cam)
        {
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.None; // MSAA from the URP asset
            data.renderShadows = false;

            volume = gameObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            volume.sharedProfile = profile;

            bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(0.88f);
            bloom.intensity.Override(0.55f);
            bloom.scatter.Override(0.62f);
            bloom.highQualityFiltering.Override(false);
            bloom.downscale.Override(BloomDownscaleMode.Quarter);

            color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0.08f);
            color.contrast.Override(12f);
            color.saturation.Override(18f);
            color.colorFilter.Override(Color.white);

            vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.2f);
            vignette.smoothness.Override(0.45f);
            vignette.color.Override(new Color(0.02f, 0.08f, 0.12f));

            var tonemap = profile.Add<Tonemapping>(true);
            tonemap.mode.Override(TonemappingMode.Neutral);

            // low-memory devices: lighter bloom
            if (SystemInfo.systemMemorySize < 4000) { bloom.intensity.Override(0.35f); bloom.downscale.Override(BloomDownscaleMode.Quarter); }
        }

        public void Hit() => hitPulse = 1f;
        public void Sync() => syncPulse = 1f;

        void Update()
        {
            float dt = Time.deltaTime;
            hitPulse = Mathf.Max(0f, hitPulse - dt * 1.6f);
            syncPulse = Mathf.Max(0f, syncPulse - dt * 2.5f);
            float shield = ShieldLeft > 0.01f ? 1f : 0f;
            vignette.intensity.Override(0.2f + hitPulse * 0.35f + shield * 0.12f);
            vignette.color.Override(hitPulse > 0.01f ? Color.Lerp(new Color(0.02f, 0.08f, 0.12f), new Color(0.85f, 0.05f, 0.05f), hitPulse)
                : shield > 0f ? new Color(0.05f, 0.45f, 0.6f) : new Color(0.02f, 0.08f, 0.12f));
            bloom.intensity.Override(0.55f + syncPulse * 0.6f);
            color.postExposure.Override(0.08f + syncPulse * 0.25f);
            color.colorFilter.Override(Color.Lerp(Color.white, new Color(1f, 0.75f, 0.75f), hitPulse * 0.7f));
        }
    }
}
