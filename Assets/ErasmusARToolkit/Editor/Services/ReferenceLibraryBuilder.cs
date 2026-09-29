using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.ARSubsystems;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Erasmus.ARToolkit.Editor
{
    /// <summary>Generates the XRReferenceImageLibrary from the image targets in the config.</summary>
    public static class ReferenceLibraryBuilder
    {
        public static XRReferenceImageLibrary Rebuild(ARToolkitConfig config)
        {
            if (config == null)
            {
                return null;
            }

            var library = GetOrCreateLibrary(config);
            for (var i = library.count - 1; i >= 0; i--)
            {
                library.RemoveAt(i);
            }

            var added = 0;
            foreach (var target in config.ImageTargets)
            {
                if (target == null || target.Texture == null || target.WidthMeters <= 0f)
                {
                    continue;
                }

                library.Add();
                var index = library.count - 1;
                library.SetName(index, target.ResolvedName);
                library.SetTexture(index, target.Texture, true);
                library.SetSpecifySize(index, true);
                library.SetSize(index, target.PhysicalSize);
                added++;
            }

            EditorUtility.SetDirty(library);
            if (config.ImageLibrary != library)
            {
                config.SetImageLibrary(library);
                EditorUtility.SetDirty(config);
            }

            AssetDatabase.SaveAssets();
            AssignToOpenScenes(config, library);
            Debug.Log($"[Erasmus AR Toolkit] Image library rebuilt with {added} image(s).", library);
            return library;
        }

        /// <summary>
        /// Every config owns exactly one library at a fixed path. A config that points at another
        /// config's library (for example the demo's) gets its own, so libraries never overwrite each other.
        /// </summary>
        static XRReferenceImageLibrary GetOrCreateLibrary(ARToolkitConfig config)
        {
            var path = LibraryPathFor(AssetDatabase.GetAssetPath(config), config.name);
            var current = config.ImageLibrary;
            if (current != null && AssetDatabase.GetAssetPath(current) == path)
            {
                return current;
            }

            var library = AssetDatabase.LoadAssetAtPath<XRReferenceImageLibrary>(path);
            if (library == null)
            {
                ToolkitAssets.EnsureFolder(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'));
                library = ScriptableObject.CreateInstance<XRReferenceImageLibrary>();
                AssetDatabase.CreateAsset(library, path);
            }

            if (current != null)
            {
                Debug.Log($"[Erasmus AR Toolkit] '{config.name}' used another config's image library " +
                          $"({AssetDatabase.GetAssetPath(current)}). It now uses its own: {path}", config);
            }

            return library;
        }

        /// <summary>The library lives in a Generated folder next to its config. Pure, for tests.</summary>
        public static string LibraryPathFor(string configAssetPath, string configName)
        {
            if (string.IsNullOrEmpty(configAssetPath))
            {
                return ToolkitPaths.ImageLibraryAssetPath;
            }

            var folder = System.IO.Path.GetDirectoryName(configAssetPath).Replace('\\', '/');
            return $"{folder}/Generated/{configName}_ImageLibrary.asset";
        }

        /// <summary>Points the image managers of open scenes that use this config at the library.</summary>
        static void AssignToOpenScenes(ARToolkitConfig config, XRReferenceImageLibrary library)
        {
            foreach (var manager in SceneQueries.FindAll<ARToolkitManager>())
            {
                if (manager.Config != config)
                {
                    continue;
                }

                foreach (var imageManager in manager.GetComponentsInChildren<ARTrackedImageManager>(true))
                {
                    var serialized = new SerializedObject(imageManager);
                    var property = serialized.FindProperty("m_SerializedLibrary");
                    if (property != null && property.objectReferenceValue != library)
                    {
                        property.objectReferenceValue = library;
                        serialized.ApplyModifiedProperties();
                    }
                }
            }
        }
    }

    /// <summary>Rebuilds the image library before every build so it never gets out of date.</summary>
    sealed class ReferenceLibraryPreBuild : IPreprocessBuildWithReport
    {
        public int callbackOrder => -100;

        public void OnPreprocessBuild(BuildReport report)
        {
            var config = ToolkitAssets.FindConfig();
            if (config != null && config.ImageTargets.Count > 0)
            {
                ReferenceLibraryBuilder.Rebuild(config);
            }
        }
    }
}
