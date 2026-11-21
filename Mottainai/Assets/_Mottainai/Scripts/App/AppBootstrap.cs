using Mottainai.Client;
using UnityEngine;

namespace Mottainai.App
{
    /// Single entry point living in the generated Bootstrap scene.
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
