using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Erasmus.ARToolkit.Editor
{
    /// <summary>Adds a room fill (water, smoke or piled-up objects) to the open scene.</summary>
    internal static class FillBuilder
    {
        public const string ObjectName = "Room Fill";

        public static RoomFill Create(FillKind kind, float maxLevel, float durationSeconds, Color color, GameObject prefab,
            Vector2 areaSize, string label, string fullMessage, PlacementMode mode)
        {
            var go = new GameObject(GameObjectUtility.GetUniqueNameForSibling(null, ObjectName));
            var placement = go.AddComponent<ModulePlacement>();
            placement.Configure(mode, null, true);
            var fill = go.AddComponent<RoomFill>();
            fill.Configure(kind, maxLevel, durationSeconds, color, prefab, areaSize,
                string.IsNullOrWhiteSpace(label) ? DefaultLabel(kind) : label, fullMessage);
            return fill;
        }

        public static string DefaultLabel(FillKind kind)
        {
            return kind == FillKind.Smoke ? "Smoke" : kind == FillKind.Objects ? "Pile" : "Water";
        }

        public static Color DefaultColor(FillKind kind)
        {
            switch (kind)
            {
                case FillKind.Smoke: return new Color(0.55f, 0.55f, 0.55f, 0.35f);
                case FillKind.Objects: return new Color(0.3f, 0.75f, 0.9f, 1f);
                default: return new Color(0.2f, 0.55f, 0.85f, 0.55f);
            }
        }
    }

    /// <summary>Projects tab: room fill (water, smoke or objects rising in the room).</summary>
    internal sealed class FillSection : SceneProjectSection<RoomFill>
    {
        readonly EnumField kindField;
        readonly FloatField maxLevelField;
        readonly FloatField durationField;
        readonly ColorField colorField;
        readonly ObjectField prefabField;
        readonly Vector2Field areaField;
        readonly TextField labelField;
        readonly TextField messageField;
        readonly EnumField placementField;

        public RoomFill Last { get; private set; }

        public FillSection() : base("Room Fill",
            "Water, smoke or objects rise in the room up to a level: a flood, a fire, a year of plastic bottles.\n" +
            "After a room scan it covers the scanned floor and real furniture stays dry; otherwise a rectangle.",
            "Add Room Fill")
        {
            kindField = AddField(new EnumField("Fills with", FillKind.Water));
            maxLevelField = AddField(new FloatField("Highest level (m)") { value = 1f });
            durationField = AddField(new FloatField("Seconds to fill") { value = 20f }, "0: only the slider moves it.");
            colorField = AddField(new ColorField("Color") { value = FillBuilder.DefaultColor(FillKind.Water) });
            prefabField = AddField(new ObjectField("Object (Objects)") { objectType = typeof(GameObject), allowSceneObjects = false },
                "What piles up. Empty: small capsules.");
            areaField = AddField(new Vector2Field("Area without scan (m)") { value = new Vector2(4f, 4f) });
            labelField = AddField(new TextField("Level label") { value = string.Empty }, "Empty: Water, Smoke or Pile.");
            messageField = AddField(new TextField("Message when full") { multiline = true, value = string.Empty });
            placementField = AddField(new EnumField("Placement", PlacementMode.TapToPlace));
            kindField.RegisterValueChangedCallback(e => colorField.value = FillBuilder.DefaultColor((FillKind)e.newValue));
        }

        protected override void OnAdd()
        {
            Last = FillBuilder.Create((FillKind)kindField.value, Mathf.Max(0.05f, maxLevelField.value), Mathf.Max(0f, durationField.value),
                colorField.value, prefabField.value as GameObject, areaField.value, labelField.value, messageField.value,
                (PlacementMode)placementField.value);
            RegisterCreated(Last.gameObject, "Add Room Fill");
            ShowMessage("Added. Press Play and tap the floor, then Start.");
        }

        protected override string Describe(RoomFill item)
        {
            return $"{item.name} ({item.Kind}, up to {item.MaxLevel:0.##} m)";
        }
    }
}
