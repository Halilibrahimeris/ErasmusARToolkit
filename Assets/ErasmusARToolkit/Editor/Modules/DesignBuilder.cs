using System;
using UnityEditor;
using UnityEngine;

namespace Erasmus.ARToolkit.Editor
{
    /// <summary>
    /// Builds a design walkthrough in the open scene: the Design root (design space, placement at app
    /// start), its Start Camera, the room shell under Virtual Only and two example objects with info cards.
    /// </summary>
    internal static class DesignBuilder
    {
        public const string RootName = "Design";
        public const string StartName = "Start Camera";
        public const string VirtualOnlyName = "Virtual Only";
        public const string ExamplesName = "Furniture (replace with your own)";

        /// <param name="size">Width (x), height (y) and length (z) in meters.</param>
        /// <param name="materials">Makes a material for a name and color; null uses the project's module materials.</param>
        public static DesignSpace Create(Vector3 size, bool shell, float startHeight, Color sky,
            Func<string, Color, Material> materials = null)
        {
            materials = materials ?? ModuleMaterials.Lit;
            size = new Vector3(Mathf.Max(1f, size.x), Mathf.Max(2f, size.y), Mathf.Max(1f, size.z));

            var root = new GameObject(GameObjectUtility.GetUniqueNameForSibling(null, RootName));
            var placement = root.AddComponent<ModulePlacement>();
            var design = root.AddComponent<DesignSpace>();

            var startObject = new GameObject(StartName);
            startObject.transform.SetParent(root.transform, false);
            startObject.transform.localPosition = new Vector3(0f, Mathf.Clamp(startHeight, 0.5f, size.y - 0.1f), 0f);
            var start = startObject.AddComponent<DesignStartPoint>();

            var virtualOnly = new GameObject(VirtualOnlyName);
            virtualOnly.transform.SetParent(root.transform, false);
            if (shell)
            {
                RoomShellBuilder.Build(virtualOnly.transform, size, materials);
            }

            AddExamples(root.transform, size, materials);
            placement.Configure(PlacementMode.AtAppStart, startObject.transform, true);
            design.Configure(size, sky, start, virtualOnly);
            return design;
        }

        static void AddExamples(Transform root, Vector3 size, Func<string, Color, Material> materials)
        {
            var group = new GameObject(ExamplesName).transform;
            group.SetParent(root, false);

            var wood = materials("DesignWood", new Color(0.55f, 0.38f, 0.22f));
            var table = new GameObject("Table").transform;
            table.SetParent(group, false);
            table.localPosition = new Vector3(Mathf.Min(1.5f, size.x * 0.25f), 0f, Mathf.Min(1.5f, size.z * 0.25f));
            ModuleShapes.Part(table, PrimitiveType.Cube, new Vector3(0f, 0.72f, 0f), new Vector3(1.2f, 0.05f, 0.7f), wood);
            foreach (var corner in new[] { new Vector2(-0.55f, -0.3f), new Vector2(0.55f, -0.3f), new Vector2(-0.55f, 0.3f), new Vector2(0.55f, 0.3f) })
            {
                ModuleShapes.Part(table, PrimitiveType.Cube, new Vector3(corner.x, 0.35f, corner.y),
                    new Vector3(0.05f, 0.7f, 0.05f), wood);
            }

            AddCard(table.gameObject, "Table", "An example. Replace it with your own furniture or models.");

            var plant = new GameObject("Plant").transform;
            plant.SetParent(group, false);
            plant.localPosition = new Vector3(-Mathf.Min(1.5f, size.x * 0.25f), 0f, Mathf.Min(1.5f, size.z * 0.25f));
            ModuleShapes.Part(plant, PrimitiveType.Cylinder, new Vector3(0f, 0.15f, 0f), new Vector3(0.3f, 0.15f, 0.3f),
                materials("DesignPot", new Color(0.72f, 0.35f, 0.2f)));
            ModuleShapes.Part(plant, PrimitiveType.Sphere, new Vector3(0f, 0.55f, 0f), new Vector3(0.5f, 0.5f, 0.5f),
                materials("DesignLeaves", new Color(0.2f, 0.55f, 0.25f)));
            AddCard(plant.gameObject, "Plant", "Tap any object that has an Info Card to read about it.");
        }

        static void AddCard(GameObject target, string title, string body)
        {
            target.AddComponent<InfoCard>().SetInfo(title, body);
        }

        /// <summary>Triangles of all meshes under the design (heavy models slow the phone down).</summary>
        public static int CountTriangles(GameObject root)
        {
            var total = 0;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh;
                if (mesh == null)
                {
                    continue;
                }

                for (var i = 0; i < mesh.subMeshCount; i++)
                {
                    total += (int)(mesh.GetIndexCount(i) / 3);
                }
            }

            foreach (var skinned in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skinned.sharedMesh != null)
                {
                    total += skinned.sharedMesh.triangles.Length / 3;
                }
            }

            return total;
        }
    }

    /// <summary>Floor, four walls and a ceiling around a design (Virtual view only).</summary>
    internal static class RoomShellBuilder
    {
        public const float WallThickness = 0.1f;
        const float SlabThickness = 0.02f;

        public static void Build(Transform parent, Vector3 size, Func<string, Color, Material> materials)
        {
            var shell = new GameObject("Room Shell").transform;
            shell.SetParent(parent, false);
            var floor = materials("DesignFloor", new Color(0.62f, 0.58f, 0.52f));
            var wall = materials("DesignWall", new Color(0.93f, 0.92f, 0.88f));
            var ceiling = materials("DesignCeiling", Color.white);
            var t = WallThickness;

            Slab(shell, "Floor", new Vector3(0f, -SlabThickness * 0.5f, 0f), new Vector3(size.x, SlabThickness, size.z), floor);
            Slab(shell, "Ceiling", new Vector3(0f, size.y + SlabThickness * 0.5f, 0f), new Vector3(size.x, SlabThickness, size.z), ceiling);
            Slab(shell, "Wall Front", new Vector3(0f, size.y * 0.5f, size.z * 0.5f + t * 0.5f), new Vector3(size.x + 2f * t, size.y, t), wall);
            Slab(shell, "Wall Back", new Vector3(0f, size.y * 0.5f, -size.z * 0.5f - t * 0.5f), new Vector3(size.x + 2f * t, size.y, t), wall);
            Slab(shell, "Wall Right", new Vector3(size.x * 0.5f + t * 0.5f, size.y * 0.5f, 0f), new Vector3(t, size.y, size.z), wall);
            Slab(shell, "Wall Left", new Vector3(-size.x * 0.5f - t * 0.5f, size.y * 0.5f, 0f), new Vector3(t, size.y, size.z), wall);
        }

        static void Slab(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
        {
            var part = ModuleShapes.Part(parent, PrimitiveType.Cube, position, scale, material);
            part.name = name;

            // The shell is scenery: taps go through it.
            UnityEngine.Object.DestroyImmediate(part.GetComponent<Collider>());
        }
    }

    /// <summary>Primitive parts for the examples the Add buttons build.</summary>
    internal static class ModuleShapes
    {
        public static GameObject Part(Transform parent, PrimitiveType type, Vector3 localPosition, Vector3 localScale,
            Material material)
        {
            var part = GameObject.CreatePrimitive(type);
            part.name = type.ToString();
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
            if (material != null)
            {
                part.GetComponent<MeshRenderer>().sharedMaterial = material;
            }

            return part;
        }
    }

    /// <summary>Materials of the examples, saved with the project's content (Assets/ARToolkitContent/Modules).</summary>
    internal static class ModuleMaterials
    {
        public const string Folder = ToolkitPaths.ModulesFolder + "/Materials";

        public static Material Lit(string name, Color color)
        {
            return ToolkitAssets.GetOrCreateLit(Folder, name, color);
        }

        public static Material Unlit(string name, Color color)
        {
            var path = Folder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                return material;
            }

            ToolkitAssets.EnsureFolder(Folder);
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            material = new Material(shader) { name = name, color = color };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
