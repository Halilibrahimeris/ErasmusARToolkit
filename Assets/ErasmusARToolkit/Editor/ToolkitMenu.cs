using UnityEditor;
using UnityEngine;

namespace Erasmus.ARToolkit.Editor
{
    /// <summary>Menu shortcuts for the most common toolkit actions.</summary>
    static class ToolkitMenu
    {
        [MenuItem(ToolkitPaths.MenuRoot + "Run Setup", priority = 20)]
        static void RunSetup()
        {
            ToolkitSetup.RunAll();
        }

        [MenuItem(ToolkitPaths.MenuRoot + "Create AR Scene", priority = 40)]
        static void CreateScene()
        {
            SceneBuilder.CreateNewScene(ToolkitAssets.GetOrCreateConfig());
        }

        [MenuItem(ToolkitPaths.MenuRoot + "Add Sample Content", priority = 60)]
        static void AddSamples()
        {
            var config = ToolkitAssets.GetOrCreateConfig();
            SampleContent.AddTo(config);
            Selection.activeObject = config;
        }

        [MenuItem(ToolkitPaths.MenuRoot + "Rebuild Image Library", priority = 61)]
        static void RebuildLibrary()
        {
            ReferenceLibraryBuilder.Rebuild(ToolkitAssets.GetOrCreateConfig());
        }

        [MenuItem(ToolkitPaths.MenuRoot + "Build/Android APK", priority = 80)]
        static void BuildAndroid()
        {
            BuildRunner.Build(BuildPlatform.Android, BuildRunner.DefaultOutputFolder, false);
        }

        [MenuItem(ToolkitPaths.MenuRoot + "Build/Windows EXE", priority = 81)]
        static void BuildWindows()
        {
            BuildRunner.Build(BuildPlatform.Windows, BuildRunner.DefaultOutputFolder, false);
        }

        [MenuItem(ToolkitPaths.MenuRoot + "Export .unitypackage", priority = 100)]
        static void ExportPackage()
        {
            var folder = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath) ?? ".", "Exports");
            var file = PackageExporter.Export(ToolkitPaths.Version, folder, true);
            if (file != null && !Application.isBatchMode)
            {
                EditorUtility.RevealInFinder(file);
            }
        }
    }
}
