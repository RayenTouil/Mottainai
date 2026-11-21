using System;
using System.IO;
using Unity.CodeEditor;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Mottainai.EditorTools
{
    /// <summary>
    /// One-click project configuration. Menu: Mottainai > Setup.
    /// Batchmode: Unity.exe -batchmode -quit -projectPath . -executeMethod Mottainai.EditorTools.ProjectSetup.ConfigureAll
    /// </summary>
    public static class ProjectSetup
    {
        public const string CompanyName = "Greenhack";
        public const string ProductName = "Mottainai";
        public const string AndroidPackageId = "com.greenhack.mottainai";

        [MenuItem("Mottainai/Setup/Configure Everything")]
        public static void ConfigureAll()
        {
            ConfigureAndroid();
            UseVSCode();
            AssetDatabase.SaveAssets();
            Debug.Log("[ProjectSetup] Done.");
        }

        [MenuItem("Mottainai/Setup/Configure Android Player Settings")]
        public static void ConfigureAndroid()
        {
            var android = NamedBuildTarget.Android;

            PlayerSettings.companyName = CompanyName;
            PlayerSettings.productName = ProductName;
            PlayerSettings.SetApplicationIdentifier(android, AndroidPackageId);
            if (string.IsNullOrEmpty(PlayerSettings.bundleVersion) || PlayerSettings.bundleVersion == "1.0")
                PlayerSettings.bundleVersion = "0.1.0";

            // Google Play requires 64-bit (ARM64), which requires IL2CPP.
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;

            EditorSettings.serializationMode = SerializationMode.ForceText;

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

            Debug.Log($"[ProjectSetup] Android configured: {AndroidPackageId}, IL2CPP, ARM64.");
        }

        [MenuItem("Mottainai/Setup/Use VS Code as Script Editor")]
        public static void UseVSCode()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string[] candidates =
            {
                Path.Combine(localAppData, "Programs", "Microsoft VS Code", "Code.exe"),
                @"C:\Program Files\Microsoft VS Code\Code.exe",
            };

            foreach (var path in candidates)
            {
                if (!File.Exists(path)) continue;
                CodeEditor.SetExternalScriptEditor(path);
                CodeEditor.CurrentEditor.SyncAll();
                Debug.Log($"[ProjectSetup] External script editor set to {path}");
                return;
            }

            Debug.LogWarning("[ProjectSetup] VS Code not found; script editor unchanged.");
        }
    }
}
