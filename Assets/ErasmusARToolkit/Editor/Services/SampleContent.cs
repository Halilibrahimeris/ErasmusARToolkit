using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Erasmus.ARToolkit.Editor
{
    /// <summary>
    /// Sample prefabs and a sample image target, generated from code so they work in any
    /// project. Pivots are at the bottom so objects sit on surfaces.
    /// </summary>
    public static class SampleContent
    {
        const string PrefabsFolder = ToolkitPaths.SamplesFolder + "/Prefabs";
        const string MaterialsFolder = ToolkitPaths.SamplesFolder + "/Materials";
        const string ImagesFolder = ToolkitPaths.SamplesFolder + "/Images";
        const string MarkerPath = ImagesFolder + "/SampleMarker.png";
        const int MarkerSize = 1024;

        static readonly Color EuBlue = new Color(0f, 0.2f, 0.6f);
        static readonly Color EuYellow = new Color(1f, 0.8f, 0f);

        /// <summary>Appends the sample placeables and image target to the config (skips ones already there).</summary>
        public static void AddTo(ARToolkitConfig config)
        {
            var placeables = new List<PlaceableItem>(config.Placeables);
            var targets = new List<ImageTargetItem>(config.ImageTargets);

            foreach (var sample in CreatePlaceables())
            {
                if (placeables.All(p => p == null || p.Prefab != sample.Prefab))
                {
                    placeables.Add(sample);
                }
            }

            var marker = GetOrCreateMarker();
            if (targets.All(t => t == null || t.Texture != marker))
            {
                targets.Add(new ImageTargetItem("SampleMarker", marker, 0.2f, GetOrCreateGlobe(),
                    new InfoLabelData("Image Target", "This globe appears when the camera sees the printed sample marker.", true)));
            }

            Undo.RecordObject(config, "Add Sample Content");
            config.SetContent(placeables, targets);
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            ReferenceLibraryBuilder.Rebuild(config);
        }

        public static Texture2D GetOrCreateMarker()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(MarkerPath);
            if (existing != null)
            {
                return existing;
            }

            ToolkitAssets.EnsureFolder(ImagesFolder);
            var texture = MarkerGenerator.Create(MarkerSize, 20260928);
            try
            {
                File.WriteAllBytes(MarkerPath, texture.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }

            AssetDatabase.ImportAsset(MarkerPath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(MarkerPath);
            importer.textureType = TextureImporterType.Default;
            importer.mipmapEnabled = false;
            importer.maxTextureSize = MarkerSize;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(MarkerPath);
        }

        static IEnumerable<PlaceableItem> CreatePlaceables()
        {
            yield return new PlaceableItem("Box", GetOrCreateBox(), 1f,
                new InfoLabelData("Erasmus Box", "A simple 15 cm cube. Drag it, pinch to scale, twist to rotate."));
            yield return new PlaceableItem("Globe", GetOrCreateGlobe(), 1f,
                new InfoLabelData("Globe", "Erasmus+ connects students and teachers all over Europe."));
            yield return new PlaceableItem("EU Flag", GetOrCreateFlag(), 1f,
                new InfoLabelData("EU Flag", "Twelve golden stars on a blue background."));
        }

        static GameObject GetOrCreateBox()
        {
            return GetOrCreatePrefab("Box", root =>
            {
                AddPart(root, PrimitiveType.Cube, new Vector3(0f, 0.075f, 0f), Vector3.one * 0.15f, Lit("Blue", EuBlue));
            });
        }

        static GameObject GetOrCreateGlobe()
        {
            return GetOrCreatePrefab("Globe", root =>
            {
                AddPart(root, PrimitiveType.Cylinder, new Vector3(0f, 0.01f, 0f), new Vector3(0.12f, 0.01f, 0.12f), Lit("Yellow", EuYellow));
                AddPart(root, PrimitiveType.Sphere, new Vector3(0f, 0.12f, 0f), Vector3.one * 0.2f, Lit("Blue", EuBlue));
            });
        }

        static GameObject GetOrCreateFlag()
        {
            return GetOrCreatePrefab("EUFlag", root =>
            {
                var white = Lit("White", new Color(0.92f, 0.92f, 0.92f));
                AddPart(root, PrimitiveType.Cylinder, new Vector3(0f, 0.005f, 0f), new Vector3(0.08f, 0.005f, 0.08f), white);
                AddPart(root, PrimitiveType.Cylinder, new Vector3(0f, 0.2f, 0f), new Vector3(0.012f, 0.2f, 0.012f), white);
                var flag = AddPart(root, PrimitiveType.Cube, new Vector3(0.105f, 0.33f, 0f), new Vector3(0.2f, 0.13f, 0.006f), Lit("Blue", EuBlue));
                var yellow = Lit("Yellow", EuYellow);
                for (var i = 0; i < 12; i++)
                {
                    var angle = i * Mathf.PI * 2f / 12f;
                    var star = AddPart(root, PrimitiveType.Sphere,
                        flag.transform.localPosition + new Vector3(Mathf.Cos(angle) * 0.042f, Mathf.Sin(angle) * 0.042f, -0.004f),
                        Vector3.one * 0.012f, yellow);
                    Object.DestroyImmediate(star.GetComponent<Collider>());
                }
            });
        }

        static GameObject GetOrCreatePrefab(string name, System.Action<GameObject> build)
        {
            var path = PrefabsFolder + "/" + name + ".prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                return existing;
            }

            ToolkitAssets.EnsureFolder(PrefabsFolder);
            var root = new GameObject(name);
            try
            {
                build(root);
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static GameObject AddPart(GameObject root, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
        {
            var part = GameObject.CreatePrimitive(type);
            part.transform.SetParent(root.transform, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            part.GetComponent<MeshRenderer>().sharedMaterial = material;
            return part;
        }

        static Material Lit(string name, Color color)
        {
            return ToolkitAssets.GetOrCreateLit(MaterialsFolder, "Sample" + name, color);
        }
    }

    /// <summary>Draws a detailed, non-repeating pattern that AR image tracking can recognise well.</summary>
    public static class MarkerGenerator
    {
        public static Texture2D Create(int size, int seed)
        {
            var random = new System.Random(seed);
            var pixels = Enumerable.Repeat(Color.white, size * size).ToArray();
            var palette = new[]
            {
                new Color(0f, 0.2f, 0.6f), new Color(1f, 0.8f, 0f), Color.black,
                new Color(0.85f, 0.1f, 0.1f), new Color(0.1f, 0.6f, 0.3f), new Color(0.4f, 0.4f, 0.4f),
            };

            for (var i = 0; i < 260; i++)
            {
                var color = palette[random.Next(palette.Length)];
                var cx = random.Next(size);
                var cy = random.Next(size);
                var extent = random.Next(size / 60, size / 9);
                if (random.Next(2) == 0)
                {
                    FillRect(pixels, size, cx, cy, extent, random.Next(size / 60, size / 9), color);
                }
                else
                {
                    FillCircle(pixels, size, cx, cy, extent, color);
                }
            }

            DrawBorder(pixels, size, size / 32, Color.black);
            var texture = new Texture2D(size, size, TextureFormat.RGB24, false) { name = "SampleMarker" };
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        static void FillRect(Color[] pixels, int size, int cx, int cy, int halfWidth, int halfHeight, Color color)
        {
            for (var y = Mathf.Max(0, cy - halfHeight); y < Mathf.Min(size, cy + halfHeight); y++)
            {
                for (var x = Mathf.Max(0, cx - halfWidth); x < Mathf.Min(size, cx + halfWidth); x++)
                {
                    pixels[y * size + x] = color;
                }
            }
        }

        static void FillCircle(Color[] pixels, int size, int cx, int cy, int radius, Color color)
        {
            for (var y = Mathf.Max(0, cy - radius); y < Mathf.Min(size, cy + radius); y++)
            {
                for (var x = Mathf.Max(0, cx - radius); x < Mathf.Min(size, cx + radius); x++)
                {
                    var dx = x - cx;
                    var dy = y - cy;
                    if (dx * dx + dy * dy <= radius * radius)
                    {
                        pixels[y * size + x] = color;
                    }
                }
            }
        }

        static void DrawBorder(Color[] pixels, int size, int thickness, Color color)
        {
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    if (x < thickness || y < thickness || x >= size - thickness || y >= size - thickness)
                    {
                        pixels[y * size + x] = color;
                    }
                }
            }
        }
    }
}
