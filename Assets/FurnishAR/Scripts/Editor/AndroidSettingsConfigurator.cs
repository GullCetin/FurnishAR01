using UnityEngine;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.XR.Management;
using UnityEngine.XR.Management;
using System.IO;

namespace FurnishAR.Editor
{
    /// <summary>
    /// Configures everything required to get AR running on Android:
    /// - ARCore XR plug-in enabled via XR Plug-in Management (loader assigned + init on start)
    /// - OpenGLES3 as the primary graphics API (most reliable ARCore path; avoids the
    ///   Vulkan ARCommandBufferSupportRendererFeature requirement)
    /// - Minimum API level 26 for ARCore (Unity 6 floor)
    /// - 64-bit ARM64 architecture, IL2CPP with Minimal managed stripping
    /// - Adds FurnishARScene to build scenes
    /// Idempotent: safe to re-run anytime from the FurnishAR menu.
    /// </summary>
    public static class AndroidSettingsConfigurator
    {
        private const string ScenePath = "Assets/FurnishAR/Scenes/FurnishARScene.unity";
        private const string XRPerTargetPath = "Assets/XR/XRGeneralSettingsPerBuildTarget.asset";
        private const string ARCoreLoaderPath = "Assets/XR/Loaders/ARCoreLoader.asset";

        [MenuItem("FurnishAR/Configure Android & Build Settings")]
        public static void ConfigureSettings()
        {
            // 1. Player Settings
            PlayerSettings.productName = "Furnish AR";
            PlayerSettings.companyName = "FurnishAR";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.furnishar.app");

            // 2. Minimum API level: Unity 6 floor is 26 (Android 8.0); ARCore supports it
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;

            // 3. Architecture: ARM64 is required for ARCore
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

            // 3b. Managed code stripping: Minimal keeps serialized MonoBehaviour fields and
            //     reflection-referenced types intact on device. Higher levels (Medium/High)
            //     have been observed to silently strip ScriptableObject data and UI code
            //     paths, producing exactly the "works in Editor, empty on device" failure.
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.Minimal);
            Debug.Log($"[FurnishAR] ✅ Managed stripping level: {PlayerSettings.GetManagedStrippingLevel(NamedBuildTarget.Android)} (Minimal keeps runtime UI/catalog intact).");

            // 4. Graphics API: OpenGLES3 primary (ARCore-safe, no Vulkan renderer feature needed)
            ConfigureGraphicsAPIs();

            // 5. XR Plug-in Management: assign ARCoreLoader for the Android build target
            ConfigureXRManagement();

            // 6. Build Scenes list
            if (File.Exists(ScenePath))
            {
                var scenes = new EditorBuildSettingsScene[]
                {
                    new EditorBuildSettingsScene(ScenePath, true)
                };
                EditorBuildSettings.scenes = scenes;
                Debug.Log($"[FurnishAR] Added {ScenePath} to Build Settings (index 0).");
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[FurnishAR] ✅ Android ARCore settings configured successfully!");
        }

        private static void ConfigureGraphicsAPIs()
        {
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[]
            {
                UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3
            });
            Debug.Log("[FurnishAR] ✅ Android Graphics API: OpenGLES3 (primary).");
        }

        private static void ConfigureXRManagement()
        {
            var perTarget = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(XRPerTargetPath);
            if (perTarget == null)
            {
                Debug.LogError($"[FurnishAR] {XRPerTargetPath} not found. Enable XR Plug-in Management once in Project Settings to create the asset, then re-run this.");
                return;
            }

            // Create default XRGeneralSettings + XRManagerSettings for Android if missing.
            if (!perTarget.HasSettingsForBuildTarget(BuildTargetGroup.Android))
                perTarget.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.Android);

            if (!perTarget.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android))
                perTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);

            var xr = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
            if (xr == null || xr.Manager == null)
            {
                Debug.LogError("[FurnishAR] Failed to obtain XRGeneralSettings/XRManagerSettings for Android.");
                return;
            }

            // AR starts automatically with the app.
            xr.InitManagerOnStart = true;
            xr.Manager.automaticLoading = true;
            xr.Manager.automaticRunning = true;

            var arcoreLoader = AssetDatabase.LoadAssetAtPath<UnityEngine.XR.ARCore.ARCoreLoader>(ARCoreLoaderPath);
            if (arcoreLoader == null)
            {
                Debug.LogError($"[FurnishAR] ARCoreLoader asset missing at {ARCoreLoaderPath}.");
                return;
            }

            var loaders = new System.Collections.Generic.List<XRLoader> { arcoreLoader };
            if (!xr.Manager.TrySetLoaders(loaders))
            {
                Debug.LogError("[FurnishAR] Could not assign ARCoreLoader as an XR loader.");
                return;
            }

            EditorUtility.SetDirty(xr);
            EditorUtility.SetDirty(xr.Manager);
            EditorUtility.SetDirty(perTarget);
            Debug.Log("[FurnishAR] ✅ XR Plug-in Management: ARCore loader enabled for Android (init on start).");
        }
    }
}
