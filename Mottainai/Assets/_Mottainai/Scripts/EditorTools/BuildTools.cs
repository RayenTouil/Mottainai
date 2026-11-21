using System;
using System.IO;
using System.Linq;
using System.Xml;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Mottainai.EditorTools
{
    /// Menu + batchmode entry points. Everything (scene, settings, assets) is generated from code.
    /// Batchmode: Unity -batchmode -quit -projectPath Mottainai -executeMethod Mottainai.EditorTools.BuildTools.BuildAndroid
    public static class BuildTools
    {
        public const string PackageId = "com.supcom.mottainai";
        const string Root = "Assets/_Mottainai";
        const string ScenePath = Root + "/Scenes/Bootstrap.unity";
        const string TuningPath = Root + "/Resources/Tuning/GameTuning.asset";
        const string MobileRp = "Assets/Settings/Mobile_RPAsset.asset";

        [MenuItem("Mottainai/Setup Project", priority = 0)]
        public static void SetupProject()
        {
            ImportTmpEssentials();
            ReimportUrtShaders();
            EnsureTuning();
            ConfigureUrp();
            ConfigurePlayer();
            CreateBootstrapScene();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[BuildTools] SetupProject done (no Netcode prefabs needed: world state is custom-serialized).");
        }

        static void ImportTmpEssentials()
        {
            if (AssetDatabase.FindAssets("t:TMP_Settings").Length > 0) return;
            var pkg = Directory.GetFiles(Path.GetFullPath("Library/PackageCache"), "TMP Essential Resources.unitypackage", SearchOption.AllDirectories).FirstOrDefault();
            string virt = "Packages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage";
            if (File.Exists(Path.GetFullPath(virt))) pkg = Path.GetFullPath(virt);
            if (pkg == null) { Debug.LogWarning("[BuildTools] TMP Essential Resources not found"); return; }
            int n = ExtractUnityPackage(pkg);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log($"[BuildTools] Imported TMP Essential Resources ({n} assets)");
        }

        /// Synchronous .unitypackage extraction (AssetDatabase.ImportPackage is async and lost in batchmode).
        static int ExtractUnityPackage(string file)
        {
            var entries = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, byte[]>>();
            using (var fs = File.OpenRead(file))
            using (var gz = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionMode.Decompress))
            {
                var header = new byte[512];
                while (true)
                {
                    if (ReadFull(gz, header, 512) < 512) break;
                    string name = System.Text.Encoding.ASCII.GetString(header, 0, 100).TrimEnd('\0', ' ');
                    if (string.IsNullOrEmpty(name)) break;
                    string prefix = System.Text.Encoding.ASCII.GetString(header, 345, 155).TrimEnd('\0', ' ');
                    if (!string.IsNullOrEmpty(prefix)) name = prefix + "/" + name;
                    string sizeStr = System.Text.Encoding.ASCII.GetString(header, 124, 12).Trim('\0', ' ');
                    long size = string.IsNullOrEmpty(sizeStr) ? 0 : Convert.ToInt64(sizeStr, 8);
                    var data = new byte[size];
                    ReadFull(gz, data, (int)size);
                    long pad = (512 - size % 512) % 512;
                    if (pad > 0) ReadFull(gz, new byte[pad], (int)pad);
                    if (name.StartsWith("./")) name = name.Substring(2);
                    int slash = name.IndexOf('/');
                    if (slash <= 0 || header[156] == (byte)'5') continue;
                    string guid = name.Substring(0, slash), part = name.Substring(slash + 1);
                    if (!entries.TryGetValue(guid, out var d)) entries[guid] = d = new System.Collections.Generic.Dictionary<string, byte[]>();
                    d[part] = data;
                }
            }
            int count = 0;
            foreach (var kv in entries)
            {
                if (!kv.Value.TryGetValue("pathname", out var pn)) continue;
                string path = System.Text.Encoding.UTF8.GetString(pn).Split('\n')[0].Trim();
                if (!path.StartsWith("Assets/")) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                if (kv.Value.TryGetValue("asset", out var bytes)) { File.WriteAllBytes(path, bytes); count++; }
                else Directory.CreateDirectory(path);
                if (kv.Value.TryGetValue("asset.meta", out var meta)) File.WriteAllBytes(path + ".meta", meta);
            }
            return count;
        }

        static int ReadFull(Stream s, byte[] buf, int len)
        {
            int read = 0;
            while (read < len) { int r = s.Read(buf, read, len - read); if (r <= 0) break; read += r; }
            return read;
        }

        /// Unity 6.3 bug: package .urtshader files can be imported before their importer is registered
        /// ("Host type is not matching any asset type"). A forced reimport fixes the cached artifact.
        [MenuItem("Mottainai/Fix URP 'Host type' errors")]
        public static void ReimportUrtShaders()
        {
            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("", new[] { "Packages/com.unity.render-pipelines.core", "Packages/com.unity.render-pipelines.universal" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".urtshader")) continue;
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                n++;
            }
            if (n > 0) Debug.Log($"[BuildTools] Reimported {n} .urtshader assets");
        }

        static void EnsureTuning()
        {
            if (AssetDatabase.LoadAssetAtPath<GameTuning>(TuningPath) != null) return;
            Directory.CreateDirectory(Path.GetDirectoryName(TuningPath));
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<GameTuning>(), TuningPath);
        }

        static void ConfigureUrp()
        {
            var rp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(MobileRp);
            if (rp == null) { Debug.LogWarning("[BuildTools] Mobile URP asset missing"); return; }
            var so = new SerializedObject(rp);
            void Set(string name, Action<SerializedProperty> f) { var p = so.FindProperty(name); if (p != null) f(p); }
            Set("m_MSAA", p => p.intValue = 2);
            Set("m_RenderScale", p => p.floatValue = 1f);
            Set("m_SupportsHDR", p => p.boolValue = false);
            Set("m_MainLightShadowsSupported", p => p.boolValue = false);
            Set("m_AdditionalLightShadowsSupported", p => p.boolValue = false);
            Set("m_ShadowDistance", p => p.floatValue = 0f);
            Set("m_UseSRPBatcher", p => p.boolValue = true);
            so.ApplyModifiedPropertiesWithoutUndo();
            GraphicsSettings.defaultRenderPipeline = rp;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = rp;
                QualitySettings.vSyncCount = 0;
                QualitySettings.shadows = UnityEngine.ShadowQuality.Disable;
            }
            QualitySettings.SetQualityLevel(current, false);
            EditorUtility.SetDirty(rp);
        }

        static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "Supcom";
            PlayerSettings.productName = "Mottainai";
            PlayerSettings.bundleVersion = "0.4.0";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, PackageId);
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, PackageId);
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, PackageId);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.forceInternetPermission = true;
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.Minimal);
            PlayerSettings.gpuSkinning = true;
            // standalone server window
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1100;
            PlayerSettings.defaultScreenHeight = 640;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.visibleInBackground = true;
            EditorSettings.serializationMode = SerializationMode.ForceText;
        }

        static void CreateBootstrapScene()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var type = Type.GetType("Mottainai.App.AppBootstrap, Mottainai.App");
            var go = new GameObject("Bootstrap");
            if (type != null) go.AddComponent(type);
            else Debug.LogWarning("[BuildTools] AppBootstrap type not found; runtime auto-bootstrap will be used");
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        // ---------------- builds ----------------
        static string Arg(string name, string def)
        {
            var a = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(a, name);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
        }

        static bool HasArg(string name) => Environment.GetCommandLineArgs().Contains(name);

        [MenuItem("Mottainai/Build/Android APK")]
        public static void BuildAndroid()
        {
            SetupProject();
            EditorUserBuildSettings.buildAppBundle = false;
            var opts = HasArg("-dev") ? BuildOptions.Development : BuildOptions.None;
            if (HasArg("-run")) opts |= BuildOptions.AutoRunPlayer;
            Build(BuildTarget.Android, BuildTargetGroup.Android, Arg("-out", "../Builds/Mottainai.apk"), opts, 0, null);
        }

        [MenuItem("Mottainai/Build/Android APK + Run on Phone")]
        public static void BuildAndroidRun()
        {
            SetupProject();
            EditorUserBuildSettings.buildAppBundle = false;
            Build(BuildTarget.Android, BuildTargetGroup.Android, "../Builds/Mottainai.apk", BuildOptions.AutoRunPlayer, 0, null);
        }

        static bool ServerModule(string engineFolder, string variationPrefix)
        {
            var dir = Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines", engineFolder, "Variations");
            return Directory.Exists(dir) && Directory.GetDirectories(dir).Any(d => Path.GetFileName(d).StartsWith(variationPrefix));
        }

        [MenuItem("Mottainai/Build/Server (Windows)")]
        public static void BuildServerWindows()
        {
            SetupProject();
            bool dedicated = ServerModule("WindowsStandaloneSupport", "win64_server");
            if (!dedicated) Debug.LogWarning("[BuildTools] Dedicated Server module not installed: building Windows player with MOTTAINAI_SERVER_BUILD (server mode forced, runs headless with -batchmode -nographics).");
            Build(BuildTarget.StandaloneWindows64, BuildTargetGroup.Standalone, Arg("-out", "Builds/Server/Windows/MottainaiServer.exe"), BuildOptions.None,
                dedicated ? (int)StandaloneBuildSubtarget.Server : (int)StandaloneBuildSubtarget.Player, dedicated ? null : new[] { "MOTTAINAI_SERVER_BUILD" });
        }

        [MenuItem("Mottainai/Build/Server (Linux)")]
        public static void BuildServerLinux()
        {
            SetupProject();
            bool linux = Directory.Exists(Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines", "LinuxStandaloneSupport"));
            if (!linux)
            {
                Debug.LogError("[BuildTools] Linux Build Support module not installed for this editor. Install 'Linux Dedicated Server Build Support' in Unity Hub.");
                if (Application.isBatchMode) EditorApplication.Exit(2);
                return;
            }
            bool dedicated = ServerModule("LinuxStandaloneSupport", "linux64_server");
            Build(BuildTarget.StandaloneLinux64, BuildTargetGroup.Standalone, Arg("-out", "Builds/Server/Linux/MottainaiServer.x86_64"), BuildOptions.None,
                dedicated ? (int)StandaloneBuildSubtarget.Server : (int)StandaloneBuildSubtarget.Player, dedicated ? null : new[] { "MOTTAINAI_SERVER_BUILD" });
        }

        public static void BuildServerForThisOS()
        {
            if (Application.platform == RuntimePlatform.LinuxEditor) BuildServerLinux();
            else BuildServerWindows();
        }

        static void Build(BuildTarget target, BuildTargetGroup group, string path, BuildOptions options, int subtarget, string[] defines)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var bpo = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = path,
                target = target,
                targetGroup = group,
                subtarget = subtarget,
                options = options,
                extraScriptingDefines = defines
            };
            var report = BuildPipeline.BuildPlayer(bpo);
            var s = report.summary;
            Debug.Log($"[BuildTools] {target} {s.result}: {Path.GetFullPath(path)} ({s.totalSize / 1048576f:F1} MB, {s.totalTime:mm\\:ss}, {s.totalErrors} errors)");
            if (Application.isBatchMode) EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
        }
    }

    /// Adds permissions needed by the 5G / slicing / haptics plugin to the generated Gradle manifest.
    public class AndroidManifestPatcher : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 10;
        static readonly string[] Permissions =
        {
            "android.permission.INTERNET", "android.permission.ACCESS_NETWORK_STATE", "android.permission.CHANGE_NETWORK_STATE",
            "android.permission.READ_PHONE_STATE", "android.permission.VIBRATE", "android.permission.ACCESS_WIFI_STATE"
        };

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            var manifest = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifest)) { Debug.LogWarning("[Manifest] not found: " + manifest); return; }
            var doc = new XmlDocument();
            doc.Load(manifest);
            const string ns = "http://schemas.android.com/apk/res/android";
            var root = doc.DocumentElement;
            foreach (var perm in Permissions)
            {
                bool exists = false;
                foreach (XmlNode n in root.SelectNodes("uses-permission"))
                    if (n.Attributes?["android:name"]?.Value == perm) exists = true;
                if (exists) continue;
                var el = doc.CreateElement("uses-permission");
                el.SetAttribute("name", ns, perm);
                root.PrependChild(el);
            }
            doc.Save(manifest);
        }
    }
}
