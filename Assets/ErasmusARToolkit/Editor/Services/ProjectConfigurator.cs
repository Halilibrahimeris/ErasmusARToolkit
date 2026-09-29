using System;
using TMPro;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;

namespace Erasmus.ARToolkit.Editor
{
    /// <summary>Checks and applies the project settings needed for Android AR and PC builds.</summary>
    public static class ProjectConfigurator
    {
        const string ArCoreLoaderType = "UnityEngine.XR.ARCore.ARCoreLoader";
        const string XRSettingsFolder = "Assets/XR";
        const string XRSettingsPath = XRSettingsFolder + "/XRGeneralSettingsPerBuildTarget.asset";
        const int ArCoreMinSdk = 24;

        // The ARCore settings live in the ARCore package's editor assembly, which the toolkit does not
        // refer to (it has to compile before ARCore is installed), so they are reached by name.
        const string ArCoreSettingsKey = "UnityEditor.XR.ARCore.ARCoreSettings";
        const string ArCoreSettingsType = ArCoreSettingsKey + ", Unity.XR.ARCore.Editor";
        const string ArCoreSettingsPath = XRSettingsFolder + "/Settings/ARCoreSettings.asset";
        const string ArCoreDepthProperty = "m_Depth";
        const int Optional = 1;

        public static bool IsAndroidModuleInstalled =>
            BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android);

        public static bool IsWindowsModuleInstalled =>
            BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);

        public static bool IsUrpActive => GraphicsSettings.defaultRenderPipeline != null;

        public static bool IsArCoreEnabled =>
            XRPackageMetadataStore.IsLoaderAssigned(ArCoreLoaderType, BuildTargetGroup.Android);

        /// <summary>0 = old Input Manager, 1 = Input System, 2 = both.</summary>
        public static int ActiveInputHandler
        {
            get
            {
                var playerSettings = Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings");
                var property = new SerializedObject(playerSettings).FindProperty("activeInputHandler");
                return property != null ? property.intValue : -1;
            }
        }

        public static bool IsInputSystemActive => ActiveInputHandler == 1 || ActiveInputHandler == 2;

        /// <summary>TextMeshPro needs its settings and default font imported into the project once.</summary>
        public static bool AreTmpEssentialsImported => Resources.Load<TMP_Settings>("TMP Settings") != null;

        public static void ImportTmpEssentials()
        {
            if (!AreTmpEssentialsImported)
            {
                TMP_PackageResourceImporter.ImportResources(true, false, false);
            }
        }

        public static bool IsAndroidPlayerReady =>
            PlayerSettings.GetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android) == ScriptingImplementation.IL2CPP
            && (PlayerSettings.Android.targetArchitectures & AndroidArchitecture.ARM64) != 0
            && (int)PlayerSettings.Android.minSdkVersion >= ArCoreMinSdk;

        /// <summary>Enables ARCore for Android and applies the player settings ARCore needs.</summary>
        public static bool ConfigureAndroid()
        {
            try
            {
                var perTarget = GetOrCreateXRSettings();
                if (!perTarget.HasSettingsForBuildTarget(BuildTargetGroup.Android))
                {
                    perTarget.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.Android);
                }

                if (!perTarget.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android))
                {
                    perTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);
                }

                var manager = perTarget.ManagerSettingsForBuildTarget(BuildTargetGroup.Android);
                if (!IsArCoreEnabled && !XRPackageMetadataStore.AssignLoader(manager, ArCoreLoaderType, BuildTargetGroup.Android))
                {
                    Debug.LogError("[Erasmus AR Toolkit] Could not enable ARCore. Tick 'Google ARCore' under " +
                                   "Project Settings > XR Plug-in Management > Android.");
                    OpenXRSettings();
                    return false;
                }

                ApplyAndroidPlayerSettings();
                AssetDatabase.SaveAssets();
                Debug.Log("[Erasmus AR Toolkit] Android is configured for ARCore.");
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError("[Erasmus AR Toolkit] Android setup failed: " + exception);
                OpenXRSettings();
                return false;
            }
        }

        /// <summary>
        /// ARCore Depth is optional: phones without depth can install the app. They get no real-world
        /// occlusion and scan rooms with tracking points.
        /// </summary>
        public static bool IsDepthOptional
        {
            get
            {
                var depth = FindArCoreSettings(false)?.FindProperty(ArCoreDepthProperty);
                return depth != null && depth.intValue == Optional;
            }
        }

        /// <summary>Returns false when the ARCore settings cannot be found or made (ARCore not installed).</summary>
        public static bool SetDepthOptional()
        {
            var settings = FindArCoreSettings(true);
            var depth = settings?.FindProperty(ArCoreDepthProperty);
            if (depth == null)
            {
                return false;
            }

            if (depth.intValue != Optional)
            {
                depth.intValue = Optional;
                settings.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(settings.targetObject);
                AssetDatabase.SaveAssets();
                Debug.Log("[Erasmus AR Toolkit] ARCore Depth is now optional: phones without depth can install " +
                          "the app. They scan rooms with tracking points and have no real-world occlusion.");
            }

            return true;
        }

        static SerializedObject FindArCoreSettings(bool create)
        {
            if (EditorBuildSettings.TryGetConfigObject(ArCoreSettingsKey, out ScriptableObject settings)
                && settings != null)
            {
                return new SerializedObject(settings);
            }

            var type = create ? Type.GetType(ArCoreSettingsType) : null;
            if (type == null)
            {
                return null;
            }

            settings = AssetDatabase.LoadAssetAtPath<ScriptableObject>(ArCoreSettingsPath);
            if (settings == null)
            {
                ToolkitAssets.EnsureFolder(XRSettingsFolder + "/Settings");
                settings = ScriptableObject.CreateInstance(type);
                AssetDatabase.CreateAsset(settings, ArCoreSettingsPath);
            }

            EditorBuildSettings.AddConfigObject(ArCoreSettingsKey, settings, true);
            return new SerializedObject(settings);
        }

        public static void OpenXRSettings()
        {
            SettingsService.OpenProjectSettings("Project/XR Plug-in Management");
        }

        public static void OpenPlayerSettings()
        {
            SettingsService.OpenProjectSettings("Project/Player");
        }

        static void ApplyAndroidPlayerSettings()
        {
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            if ((int)PlayerSettings.Android.minSdkVersion < ArCoreMinSdk)
            {
                PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)ArCoreMinSdk;
            }
        }

        static XRGeneralSettingsPerBuildTarget GetOrCreateXRSettings()
        {
            if (EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey,
                    out XRGeneralSettingsPerBuildTarget existing) && existing != null)
            {
                return existing;
            }

            var guids = AssetDatabase.FindAssets("t:" + nameof(XRGeneralSettingsPerBuildTarget));
            var settings = guids.Length > 0
                ? AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(AssetDatabase.GUIDToAssetPath(guids[0]))
                : null;

            if (settings == null)
            {
                ToolkitAssets.EnsureFolder(XRSettingsFolder);
                settings = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(settings, XRSettingsPath);
            }

            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, settings, true);
            return settings;
        }
    }
}
