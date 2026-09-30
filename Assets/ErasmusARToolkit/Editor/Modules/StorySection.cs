using System;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Erasmus.ARToolkit.Editor
{
    /// <summary>Builds stories, their characters and points in the open scene.</summary>
    internal static class StoryBuilder
    {
        public const string RootName = "Story";

        /// <summary>A story with two characters, two points and five steps that play on their own.</summary>
        public static StoryStage CreateStory(PlacementMode mode, Func<string, Color, Material> materials = null)
        {
            materials = materials ?? ModuleMaterials.Lit;
            var root = new GameObject(GameObjectUtility.GetUniqueNameForSibling(null, RootName));
            root.AddComponent<ModulePlacement>().Configure(mode, null, true);
            var stage = root.AddComponent<StoryStage>();

            var guide = AddCharacter(stage, "Guide", new Color(0.2f, 0.45f, 0.9f), null, 1.1f, materials);
            guide.transform.localPosition = new Vector3(-1f, 0f, 0f);
            var friend = AddCharacter(stage, "Friend", new Color(0.95f, 0.55f, 0.15f), null, 1.1f, materials);
            friend.transform.localPosition = new Vector3(1.5f, 0f, -1f);
            friend.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var door = AddPoint(stage, "Door");
            door.transform.localPosition = new Vector3(-1.5f, 0f, 2f);
            AddPoint(stage, "Table").transform.localPosition = new Vector3(1.5f, 0f, 1f);

            stage.SetSteps(new[]
            {
                new StoryStep(guide, StoryAction.Say, "Hello! Welcome to our story. Tap to go on.", StoryAdvance.Tap),
                new StoryStep(guide, StoryAction.WalkTo, advance: StoryAdvance.Auto, point: door),
                new StoryStep(friend, StoryAction.TurnToUser),
                new StoryStep(friend, StoryAction.Say, "Come closer, I have something to tell you.", StoryAdvance.UserNear, nearMeters: 2f),
                new StoryStep(friend, StoryAction.Say, "This is how a story works. Change the steps in the Inspector of the Story.", StoryAdvance.Tap),
            });
            return stage;
        }

        /// <summary>A character: the toolkit's simple one, or an instance of <paramref name="model"/>.</summary>
        public static StoryCharacter AddCharacter(StoryStage stage, string name, Color color, GameObject model, float walkSpeed,
            Func<string, Color, Material> materials = null)
        {
            materials = materials ?? ModuleMaterials.Lit;
            name = string.IsNullOrWhiteSpace(name) ? "Character" : name.Trim();
            GameObject go;
            if (model != null)
            {
                go = (GameObject)PrefabUtility.InstantiatePrefab(model);
                go.name = GameObjectUtility.GetUniqueNameForSibling(stage.transform, name);
                go.transform.SetParent(stage.transform, false);
            }
            else
            {
                go = new GameObject(GameObjectUtility.GetUniqueNameForSibling(stage.transform, name));
                go.transform.SetParent(stage.transform, false);
                SimpleCharacterBuilder.Build(go.transform, color, materials);
            }

            var character = go.GetComponent<StoryCharacter>() ?? go.AddComponent<StoryCharacter>();
            character.Configure(name, walkSpeed);
            if (go.GetComponent<InfoCard>() == null)
            {
                go.AddComponent<InfoCard>().SetInfo(name, "A character of the story.");
            }

            return character;
        }

        public static StoryPoint AddPoint(StoryStage stage, string name)
        {
            var go = new GameObject(GameObjectUtility.GetUniqueNameForSibling(stage.transform, string.IsNullOrWhiteSpace(name) ? "Point" : name.Trim()));
            go.transform.SetParent(stage.transform, false);
            return go.AddComponent<StoryPoint>();
        }

        /// <summary>The story the selection is in, or the only one in the scene.</summary>
        public static StoryStage FindStage(GameObject selected)
        {
            var fromSelection = selected != null ? selected.GetComponentInParent<StoryStage>() : null;
            if (fromSelection != null)
            {
                return fromSelection;
            }

            var stages = SceneQueries.FindAll<StoryStage>().Where(s => s.gameObject.scene.IsValid()).ToArray();
            return stages.Length == 1 ? stages[0] : null;
        }
    }

    /// <summary>The toolkit's simple character: a body, a head and eyes, about 1.7 m tall, facing +Z.</summary>
    internal static class SimpleCharacterBuilder
    {
        public static void Build(Transform root, Color color, Func<string, Color, Material> materials)
        {
            var body = ModuleShapes.Part(root, PrimitiveType.Capsule, new Vector3(0f, 0.75f, 0f), new Vector3(0.5f, 0.75f, 0.5f),
                materials("Character " + ColorUtility.ToHtmlStringRGB(color), color));
            body.name = "Body";
            UnityEngine.Object.DestroyImmediate(body.GetComponent<Collider>());
            var head = ModuleShapes.Part(body.transform, PrimitiveType.Sphere, new Vector3(0f, 1.2f, 0f), new Vector3(0.7f, 0.47f, 0.7f),
                materials("Skin", new Color(0.95f, 0.8f, 0.65f)));
            head.name = "Head";
            UnityEngine.Object.DestroyImmediate(head.GetComponent<Collider>());
            var eyes = materials("Eyes", new Color(0.1f, 0.1f, 0.12f));
            foreach (var side in new[] { -1f, 1f })
            {
                var eye = ModuleShapes.Part(head.transform, PrimitiveType.Sphere, new Vector3(0.15f * side, 0.1f, 0.42f), Vector3.one * 0.12f, eyes);
                eye.name = side < 0f ? "Left eye" : "Right eye";
                UnityEngine.Object.DestroyImmediate(eye.GetComponent<Collider>());
            }

            var collider = root.gameObject.AddComponent<CapsuleCollider>();
            collider.center = new Vector3(0f, 0.85f, 0f);
            collider.height = 1.7f;
            collider.radius = 0.3f;
        }
    }

    /// <summary>Projects tab: NPC story (characters that walk and talk in the room).</summary>
    internal sealed class StorySection : SceneProjectSection<StoryStage>
    {
        readonly EnumField placementField;
        readonly TextField nameField;
        readonly ColorField colorField;
        readonly ObjectField modelField;
        readonly FloatField speedField;
        readonly TextField pointField;

        public StorySection() : base("NPC Story",
            "Characters walk and talk in the room: steps like Say, Walk To, Turn To User, Wait. A step goes on\n" +
            "at once, on a tap or when the user comes close. After a room scan they walk around the furniture.",
            "Add Story")
        {
            placementField = AddField(new EnumField("Placement", PlacementMode.TapToPlace));
            nameField = AddField(new TextField("NPC name") { value = "Ayse" });
            colorField = AddField(new ColorField("NPC color") { value = new Color(0.55f, 0.3f, 0.75f), showAlpha = false });
            modelField = AddField(new ObjectField("NPC model (optional)") { objectType = typeof(GameObject), allowSceneObjects = false },
                "A character model. With an Animator, its Speed (float) and Talk (bool) parameters are set.");
            speedField = AddField(new FloatField("NPC walk speed (m/s)") { value = 1.1f });
            pointField = AddField(new TextField("Point name") { value = "Window" });
            AddButton("Add NPC To Story", () => AddToStory(stage =>
            {
                var character = StoryBuilder.AddCharacter(stage, nameField.value, colorField.value, modelField.value as GameObject, speedField.value);
                RegisterCreated(character.gameObject, "Add NPC");
                return $"Added {character.name}. Give it steps in the Inspector of {stage.name}.";
            }));
            AddButton("Add Point To Story", () => AddToStory(stage =>
            {
                var point = StoryBuilder.AddPoint(stage, pointField.value);
                RegisterCreated(point.gameObject, "Add Point");
                return $"Added the point {point.name}. Move it, then use it in a Walk To step.";
            }));
        }

        protected override void OnAdd()
        {
            var stage = StoryBuilder.CreateStory((PlacementMode)placementField.value);
            RegisterCreated(stage.gameObject, "Add Story");
            ShowMessage("Added a story with two characters and five steps. Press Play and tap the floor; tap to go on.");
        }

        void AddToStory(Func<StoryStage, string> add)
        {
            var stage = StoryBuilder.FindStage(Selection.activeGameObject);
            if (stage == null)
            {
                ShowMessage("Add a story first, or select the story to add to.", HelpBoxMessageType.Warning);
                return;
            }

            ShowMessage(add(stage));
        }

        protected override string Describe(StoryStage item)
        {
            var characters = item.GetComponentsInChildren<StoryCharacter>(true).Length;
            return $"{item.name} ({characters} characters, {item.Steps.Count} steps)";
        }
    }
}
