using UnityEngine;
using UnityEngine.Rendering;

namespace Mottainai.Client
{
    /// Phase 1 look: warm key light from the upper right, sky/grass/ground gradient ambient, pale-blue distance fog,
    /// stylized sky dome (gradient, cartoon clouds, sun glow + rays top-right, distant mountains, open sea ahead).
    public static class SceneLook
    {
        // visual sun just above the top-right corner of the gameplay camera (rays fan into the screen)
        public static readonly Vector3 SunVisualDir = new Vector3(0.629f, 0.208f, 0.749f).normalized;
        // key light: upper right, slightly behind the camera so camera-facing sides are lit (toy look)
        public static readonly Vector3 ToLight = new Vector3(0.5f, 0.8f, -0.32f).normalized;

        public static readonly Color Horizon = new Color(0.80f, 0.92f, 0.99f);
        public static readonly Color Mid = new Color(0.42f, 0.74f, 0.98f);
        public static readonly Color Zenith = new Color(0.16f, 0.55f, 0.95f);
        static readonly int SunDirId = Shader.PropertyToID("_MottainaiSunDir");

        public static Light Apply(Transform root, Camera cam, out Transform sky, out Material skyMat)
        {
            var lightGo = new GameObject("Sun");
            lightGo.transform.SetParent(root, false);
            lightGo.transform.rotation = Quaternion.LookRotation(-ToLight);
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Directional;
            l.intensity = 1.2f;
            l.color = new Color(1f, 0.95f, 0.84f);
            l.shadows = LightShadows.None;

            // gradient ambient as SH: sky blue from above, grass green around, warm earth from below
            var sh = new SphericalHarmonicsL2();
            sh.AddAmbientLight(new Color(0.40f, 0.47f, 0.48f));
            sh.AddDirectionalLight(Vector3.up, new Color(0.45f, 0.62f, 0.85f), 0.75f);
            sh.AddDirectionalLight(Vector3.down, new Color(0.55f, 0.45f, 0.32f), 0.45f);
            sh.AddDirectionalLight(new Vector3(1f, 0f, 0f), new Color(0.38f, 0.55f, 0.32f), 0.12f);
            sh.AddDirectionalLight(new Vector3(-1f, 0f, 0f), new Color(0.38f, 0.55f, 0.32f), 0.12f);
            RenderSettings.ambientMode = AmbientMode.Custom;
            RenderSettings.ambientProbe = sh;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Horizon;
            RenderSettings.fogStartDistance = 50f;
            RenderSettings.fogEndDistance = 175f;

            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Horizon;
            cam.farClipPlane = 190f;

            NoiseTexture.Ensure();
            Shader.SetGlobalVector("_MottainaiSkyHorizon", Horizon);
            Shader.SetGlobalVector("_MottainaiSkyZenith", Mid);
            Shader.SetGlobalVector(SunDirId, SunVisualDir);

            sky = null; skyMat = null;
            var skyShader = Shader.Find("Mottainai/Sky");
            if (skyShader != null)
            {
                skyMat = new Material(skyShader);
                skyMat.SetVector("_SunDir", SunVisualDir);
                skyMat.SetColor("_Horizon", Horizon);
                skyMat.SetColor("_Mid", Mid);
                skyMat.SetColor("_Zenith", Zenith);
                skyMat.SetFloat("_Rays", Quality.High ? 1f : 0f);
                sky = VisualFactory.Part(root, VisualFactory.Sphere, skyMat, Vector3.zero, Vector3.one * 340f, default, "SkyDome").transform;
            }
            return l;
        }
    }
}
