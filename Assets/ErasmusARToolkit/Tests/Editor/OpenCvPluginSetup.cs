using UnityEditor;
using UnityEngine;

namespace Erasmus.ARToolkit.Tests
{
    /// <summary>
    /// Developer tool: restricts the OpenCvSharp plugins to the Windows Editor and Windows x64 builds.
    /// The settings are stored in the .meta files, so they ship with the exported package.
    /// </summary>
    static class OpenCvPluginSetup
    {
        const string Folder = "Assets/ErasmusARToolkit/Plugins/OpenCvSharp/";
        const string ManagedPath = Folder + "OpenCvSharp.dll";
        const string NativePath = Folder + "x86_64/OpenCvSharpExtern.dll";

        [MenuItem("Tools/Erasmus AR Toolkit/Developer/Configure OpenCV Plugins", priority = 1020)]
        static void Configure()
        {
            ConfigureWindowsOnly(ManagedPath, false);
            ConfigureWindowsOnly(NativePath, true);
            Debug.Log("[Erasmus AR Toolkit] OpenCV plugins restricted to the Windows Editor and Windows x64 builds.");
        }

        static void ConfigureWindowsOnly(string path, bool isNative)
        {
            if (!(AssetImporter.GetAtPath(path) is PluginImporter importer))
            {
                Debug.LogError("[Erasmus AR Toolkit] Plugin not found: " + path);
                return;
            }

            importer.ClearSettings();
            importer.SetCompatibleWithAnyPlatform(false);
            importer.SetCompatibleWithEditor(true);
            importer.SetEditorData("OS", "Windows");
            importer.SetEditorData("CPU", "x86_64");
            importer.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows64, true);
            importer.SetPlatformData(BuildTarget.StandaloneWindows64, "CPU", "x86_64");
            importer.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows, false);
            importer.SetCompatibleWithPlatform(BuildTarget.Android, false);
            importer.SetCompatibleWithPlatform(BuildTarget.iOS, false);
            importer.SetCompatibleWithPlatform(BuildTarget.StandaloneOSX, false);
            importer.SetCompatibleWithPlatform(BuildTarget.StandaloneLinux64, false);
            importer.SetCompatibleWithPlatform(BuildTarget.WebGL, false);

            if (!isNative)
            {
                // Only the OpenCV assembly definition references it (Auto Reference off);
                // its System.Memory / Unsafe dependencies come from Unity's .NET Standard 2.1 profile.
                var serialized = new SerializedObject(importer);
                SetBool(serialized, "m_IsExplicitlyReferenced", true);
                SetBool(serialized, "m_ValidateReferences", false);
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            importer.SaveAndReimport();
        }

        static void SetBool(SerializedObject serialized, string name, bool value)
        {
            var property = serialized.FindProperty(name);
            if (property != null)
            {
                property.boolValue = value;
            }
            else
            {
                Debug.LogWarning("[Erasmus AR Toolkit] PluginImporter has no property " + name);
            }
        }
    }
}
