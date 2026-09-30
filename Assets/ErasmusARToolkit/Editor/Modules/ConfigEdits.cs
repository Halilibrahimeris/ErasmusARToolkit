using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Erasmus.ARToolkit.Editor
{
    /// <summary>
    /// Adds and removes config entries for the Projects tab (posters, exhibits, route stops), with undo.
    /// A config that is an asset is saved and its image library rebuilt.
    /// </summary>
    internal static class ConfigEdits
    {
        public static void AddImageTarget(ARToolkitConfig config, ImageTargetItem item, string undoName)
        {
            Undo.RecordObject(config, undoName);
            var targets = config.ImageTargets.ToList();
            targets.Add(item);
            config.SetContent(config.Placeables, targets);
            Save(config, true);
        }

        public static void RemoveImageTarget(ARToolkitConfig config, ImageTargetItem item, string undoName)
        {
            Undo.RecordObject(config, undoName);
            config.SetContent(config.Placeables, config.ImageTargets.Where(t => !ReferenceEquals(t, item)));
            Save(config, true);
        }

        /// <summary>Replaces the image targets (a new order of route stops).</summary>
        public static void SetImageTargets(ARToolkitConfig config, IEnumerable<ImageTargetItem> targets, string undoName)
        {
            Undo.RecordObject(config, undoName);
            config.SetContent(config.Placeables, targets);
            Save(config, false);
        }

        public static void AddPlaceable(ARToolkitConfig config, PlaceableItem item, string undoName)
        {
            Undo.RecordObject(config, undoName);
            var placeables = config.Placeables.ToList();
            placeables.Add(item);
            config.SetContent(placeables, config.ImageTargets);
            Save(config, false);
        }

        public static void RemovePlaceable(ARToolkitConfig config, PlaceableItem item, string undoName)
        {
            Undo.RecordObject(config, undoName);
            config.SetContent(config.Placeables.Where(p => !ReferenceEquals(p, item)), config.ImageTargets);
            Save(config, false);
        }

        /// <summary>A name no image target of the config has yet ("Poster", "Poster 2" ...).</summary>
        public static string UniqueTargetName(ARToolkitConfig config, string baseName)
        {
            var taken = new HashSet<string>(config.ImageTargets.Where(t => t != null).Select(t => t.ResolvedName));
            return Unique(taken, baseName);
        }

        public static string UniquePlaceableName(ARToolkitConfig config, string baseName)
        {
            var taken = new HashSet<string>(config.Placeables.Where(p => p != null).Select(p => p.ResolvedName));
            return Unique(taken, baseName);
        }

        internal static string Unique(ICollection<string> taken, string baseName)
        {
            baseName = string.IsNullOrWhiteSpace(baseName) ? "Item" : baseName.Trim();
            if (!taken.Contains(baseName))
            {
                return baseName;
            }

            for (var i = 2; ; i++)
            {
                var candidate = baseName + " " + i;
                if (!taken.Contains(candidate))
                {
                    return candidate;
                }
            }
        }

        public static void Save(ARToolkitConfig config, bool rebuildLibrary)
        {
            EditorUtility.SetDirty(config);
            if (!EditorUtility.IsPersistent(config))
            {
                return;
            }

            AssetDatabase.SaveAssets();
            if (rebuildLibrary)
            {
                ReferenceLibraryBuilder.Rebuild(config);
            }
        }
    }

    /// <summary>Printable markers made by the toolkit (for posters without an image, route stops).</summary>
    internal static class MarkerFiles
    {
        const int Size = 1024;

        /// <summary>Writes a new marker PNG to <paramref name="folder"/> and imports it for image tracking.</summary>
        public static Texture2D Create(string folder, string baseName)
        {
            ToolkitAssets.EnsureFolder(folder);
            var path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + SafeName(baseName) + ".png");
            var seed = path.GetHashCode() ^ Environment.TickCount;
            var texture = MarkerGenerator.Create(Size, seed);
            try
            {
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }

            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.mipmapEnabled = false;
            importer.maxTextureSize = Size;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static string SafeName(string name)
        {
            var clean = new string((name ?? "Marker").Where(c => char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_').ToArray()).Trim();
            return clean.Length > 0 ? clean : "Marker";
        }
    }
}
