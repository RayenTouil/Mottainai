using Mottainai.Client;
using UnityEngine;

namespace Mottainai.App
{
    /// Single entry point living in the generated Bootstrap scene. Decides server vs client from command line.
    public class AppBootstrap : MonoBehaviour
    {
        static bool started;

        void Awake()
        {
            if (started) { Destroy(gameObject); return; }
            started = true;
            DontDestroyOnLoad(gameObject);
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            Application.runInBackground = true;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            var args = LaunchArgs.Parse(System.Environment.GetCommandLineArgs());
#if UNITY_SERVER || MOTTAINAI_SERVER_BUILD
            args.server = true;
#endif
            if (Application.isBatchMode) args.headless = true;

            if (args.server)
            {
                if (!AppHooks.ServerAvailable) { Debug.LogError("Server code not included in this build."); return; }
                if (!args.headless)
                {
                    var cam = new GameObject("ServerCamera").AddComponent<Camera>();
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = new Color(0.04f, 0.1f, 0.12f);
                    cam.cullingMask = 0;
                    DontDestroyOnLoad(cam.gameObject);
                }
                AppHooks.StartServer(args);
                return;
            }

            Screen.orientation = ScreenOrientation.AutoRotation;
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = true;
            ClientApp.Create();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void EnsureBootstrap()
        {
            if (started || FindAnyObjectByType<AppBootstrap>() != null) return;
            new GameObject("Bootstrap").AddComponent<AppBootstrap>();
        }
    }
}
