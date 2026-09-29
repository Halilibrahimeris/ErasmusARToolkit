using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Erasmus.ARToolkit.Editor
{
    /// <summary>Exports the toolkit folder (without tests) as a .unitypackage.</summary>
    public static class PackageExporter
    {
        public static string[] CollectPaths(bool includeSamples)
        {
            var all = AssetDatabase.FindAssets(string.Empty, new[] { ToolkitPaths.ToolkitRoot })
                .Select(AssetDatabase.GUIDToAssetPath);
            return PackageExportRules.SelectPaths(all, includeSamples).OrderBy(path => path).ToArray();
        }

        /// <summary>Exports and returns the file path, or null on failure.</summary>
        public static string Export(string version, string outputFolder, bool includeSamples)
        {
            if (!PackageExportRules.IsValidVersion(version))
            {
                Debug.LogError($"[Erasmus AR Toolkit] '{version}' is not a valid version. Use the form 1.0.0.");
                return null;
            }

            var paths = CollectPaths(includeSamples);
            if (paths.Length == 0)
            {
                Debug.LogError("[Erasmus AR Toolkit] Nothing to export under " + ToolkitPaths.ToolkitRoot);
                return null;
            }

            try
            {
                AssetDatabase.SaveAssets();
                Directory.CreateDirectory(outputFolder);
                var file = Path.Combine(outputFolder, PackageExportRules.CreateFileName(version));
                AssetDatabase.ExportPackage(paths, file, ExportPackageOptions.Default);
                Debug.Log($"[Erasmus AR Toolkit] Exported {paths.Length} assets to {file}");
                return file;
            }
            catch (System.Exception exception)
            {
                Debug.LogError("[Erasmus AR Toolkit] Export failed: " + exception);
                return null;
            }
        }
    }
}
