using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Erasmus.ARToolkit.Editor
{
    /// <summary>Builds a portal in the open scene: frame, the world behind the door (or the team's prefab).</summary>
    internal static class PortalBuilder
    {
        public const string RootName = "Portal";
        public const string WorldName = "Inside World";

        public static PortalController Create(float doorWidth, float doorHeight, Vector3 worldSize, Color frameColor, Color sky,
            GameObject worldPrefab, PlacementMode mode, Func<string, Color, Material> materials = null)
        {
            materials = materials ?? ModuleMaterials.Lit;
            doorWidth = Mathf.Max(0.5f, doorWidth);
            doorHeight = Mathf.Max(1f, doorHeight);
            worldSize = new Vector3(Mathf.Max(doorWidth + 1f, worldSize.x), Mathf.Max(doorHeight + 0.5f, worldSize.y), Mathf.Max(1f, worldSize.z));

            var root = new GameObject(GameObjectUtility.GetUniqueNameForSibling(null, RootName));
            var placement = root.AddComponent<ModulePlacement>();
            placement.Configure(mode, null, true);
            var portal = root.AddComponent<PortalController>();

            var frame = new GameObject("Frame").transform;
            frame.SetParent(root.transform, false);
            var frameMaterial = materials("Portal Frame " + ColorUtility.ToHtmlStringRGB(frameColor), frameColor);
            const float post = 0.12f;
            ModuleShapes.Part(frame, PrimitiveType.Cube, new Vector3(-(doorWidth + post) * 0.5f, doorHeight * 0.5f, 0f),
                new Vector3(post, doorHeight, post), frameMaterial).name = "Left post";
            ModuleShapes.Part(frame, PrimitiveType.Cube, new Vector3((doorWidth + post) * 0.5f, doorHeight * 0.5f, 0f),
                new Vector3(post, doorHeight, post), frameMaterial).name = "Right post";
            ModuleShapes.Part(frame, PrimitiveType.Cube, new Vector3(0f, doorHeight + post * 0.5f, 0f),
                new Vector3(doorWidth + post * 2f, post, post), frameMaterial).name = "Lintel";

            GameObject world;
            if (worldPrefab != null)
            {
                world = (GameObject)PrefabUtility.InstantiatePrefab(worldPrefab);
                world.name = WorldName;
                world.transform.SetParent(root.transform, false);
            }
            else
            {
                world = new GameObject(WorldName);
                world.transform.SetParent(root.transform, false);
                BuildExampleWorld(world.transform, worldSize, materials);
            }

            portal.Configure(doorWidth, doorHeight, worldSize, sky, world);
            return portal;
        }

        /// <summary>A meadow with trees, rocks and a sign with an info card, all behind the door.</summary>
        static void BuildExampleWorld(Transform world, Vector3 size, Func<string, Color, Material> materials)
        {
            var grass = materials("Portal Grass", new Color(0.35f, 0.62f, 0.3f));
            var trunk = materials("Portal Trunk", new Color(0.42f, 0.28f, 0.16f));
            var leaves = materials("Portal Leaves", new Color(0.18f, 0.48f, 0.24f));
            var stone = materials("Portal Stone", new Color(0.55f, 0.55f, 0.58f));
            ModuleShapes.Part(world, PrimitiveType.Cube, new Vector3(0f, -0.01f, -size.z * 0.5f), new Vector3(size.x, 0.02f, size.z), grass).name = "Ground";

            var spots = new[] { new Vector2(-0.3f, 0.35f), new Vector2(0.3f, 0.55f), new Vector2(-0.25f, 0.8f), new Vector2(0.2f, 0.85f) };
            for (var i = 0; i < spots.Length; i++)
            {
                var tree = new GameObject("Tree " + (i + 1)).transform;
                tree.SetParent(world, false);
                tree.localPosition = new Vector3(spots[i].x * size.x, 0f, -spots[i].y * size.z);
                ModuleShapes.Part(tree, PrimitiveType.Cylinder, new Vector3(0f, 0.6f, 0f), new Vector3(0.2f, 0.6f, 0.2f), trunk).name = "Trunk";
                ModuleShapes.Part(tree, PrimitiveType.Sphere, new Vector3(0f, 1.6f, 0f), new Vector3(1.1f, 1.1f, 1.1f), leaves).name = "Crown";
            }

            ModuleShapes.Part(world, PrimitiveType.Sphere, new Vector3(0.35f * size.x, 0.2f, -0.25f * size.z), new Vector3(0.7f, 0.4f, 0.5f), stone).name = "Rock";

            var sign = new GameObject("Sign").transform;
            sign.SetParent(world, false);
            sign.localPosition = new Vector3(0.8f, 0f, -1.5f);
            ModuleShapes.Part(sign, PrimitiveType.Cube, new Vector3(0f, 0.6f, 0f), new Vector3(0.08f, 1.2f, 0.08f), trunk).name = "Post";
            ModuleShapes.Part(sign, PrimitiveType.Cube, new Vector3(0f, 1.2f, 0f), new Vector3(0.7f, 0.4f, 0.05f), trunk).name = "Board";
            sign.gameObject.AddComponent<InfoCard>().SetInfo("The other world", "An example. Replace it with the world of your project.");
        }
    }

    /// <summary>Projects tab: portal (a door in the room to another world).</summary>
    internal sealed class PortalSection : SceneProjectSection<PortalController>
    {
        readonly FloatField widthField;
        readonly FloatField heightField;
        readonly Vector3Field worldField;
        readonly ColorField frameField;
        readonly ColorField skyField;
        readonly ObjectField prefabField;
        readonly EnumField placementField;

        public PortalSection() : base("Portal",
            "A door in the room. Through it another world is seen; walk through to be inside it, and turn around\n" +
            "to see the room through the door. Walking around the portal shows only its frame.",
            "Add Portal")
        {
            widthField = AddField(new FloatField("Door width (m)") { value = 1.2f });
            heightField = AddField(new FloatField("Door height (m)") { value = 2.2f });
            worldField = AddField(new Vector3Field("World (W x H x D, m)") { value = new Vector3(6f, 3f, 6f) },
                "The world lies behind the door, inside this size.");
            frameField = AddField(new ColorField("Frame color") { value = new Color(0.55f, 0.35f, 0.9f), showAlpha = false });
            skyField = AddField(new ColorField("Sky color") { value = new Color(0.45f, 0.7f, 0.95f), showAlpha = false });
            prefabField = AddField(new ObjectField("World prefab (optional)") { objectType = typeof(GameObject), allowSceneObjects = false },
                "Your world, modelled behind the door (-Z). Empty: an example meadow.");
            placementField = AddField(new EnumField("Placement", PlacementMode.TapToPlace));
        }

        protected override void OnAdd()
        {
            var portal = PortalBuilder.Create(widthField.value, heightField.value, worldField.value, frameField.value, skyField.value,
                prefabField.value as GameObject, (PlacementMode)placementField.value);
            RegisterCreated(portal.gameObject, "Add Portal");
            ShowMessage("Added. Build your world under Inside World (behind the door). Press Play and tap the floor.");
        }

        protected override string Describe(PortalController item)
        {
            return $"{item.name} (door {item.DoorWidth:0.##} x {item.DoorHeight:0.##} m)";
        }
    }
}
