using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Mottainai.EditorTools
{
    /// <summary>
    /// Android builds. Menu: Mottainai > Build.
    /// Batchmode: Unity.exe -batchmode -quit -projectPath . -executeMethod Mottainai.EditorTools.BuildScript.BuildAndroidDev
    /// Output goes to Builds/Android/ (git-ignored).
    /// </summary>
    public static class BuildScript
    {
        const string OutputDir = "Builds/Android";

        [MenuItem("Mottainai/Build/Android APK (Development)")]
        public static void BuildAndroidDev() => Build(BuildOptions.Development, appBundle: false, "Mottainai-dev.apk");

        [MenuItem("Mottainai/Build/Build and Run on Phone (Development)")]
        public static void BuildAndRunDev() =>
            Build(BuildOptions.Development | BuildOptions.AutoRunPlayer, appBundle: false, "Mottainai-dev.apk");

        [MenuItem("Mottainai/Build/Android App Bundle (Release, Play Store)")]
        public static void BuildAndroidRelease() => Build(BuildOptions.None, appBundle: true, "Mottainai.aab");

        static void Build(BuildOptions options, bool appBundle, string fileName)
        {
            string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
                throw new InvalidOperationException("No scenes enabled in Build Profiles / Build Settings.");

            Directory.CreateDirectory(OutputDir);
            EditorUserBuildSettings.buildAppBundle = appBundle;

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(OutputDir, fileName),
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = options,
            });

            var summary = report.summary;
            Debug.Log($"[BuildScript] {summary.result}: {summary.outputPath} " +
                      $"({summary.totalSize / (1024f * 1024f):F1} MB, {summary.totalTime:mm\\:ss})");

            if (Application.isBatchMode && summary.result != BuildResult.Succeeded)
                EditorApplication.Exit(1);
        }
    }
}
