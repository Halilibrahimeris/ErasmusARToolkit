using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Erasmus.ARToolkit.Editor
{
    /// <summary>Adds the vision simulator to the open scene.</summary>
    internal static class VisionBuilder
    {
        public const string ObjectName = "Vision Simulator";

        public static VisionSimulator Create(IEnumerable<VisionFilterKind> offered, bool eyeHeights, float wheelchairEye,
            float childEye)
        {
            var go = new GameObject(GameObjectUtility.GetUniqueNameForSibling(null, ObjectName));
            var simulator = go.AddComponent<VisionSimulator>();
            var chosen = new HashSet<VisionFilterKind>(offered);
            simulator.SetFilters(VisionFilter.All.Select(kind => new VisionFilterEntry(kind, chosen.Contains(kind))));
            var serialized = new SerializedObject(simulator);
            serialized.FindProperty("offerEyeHeights").boolValue = eyeHeights;
            serialized.FindProperty("wheelchairEyeHeight").floatValue = Mathf.Max(0.3f, wheelchairEye);
            serialized.FindProperty("childEyeHeight").floatValue = Mathf.Max(0.3f, childEye);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return simulator;
        }
    }

    /// <summary>Projects tab: "See Through Other Eyes" (colour blindness and other ways of seeing).</summary>
    internal sealed class VisionSection : SceneProjectSection<VisionSimulator>
    {
        readonly Dictionary<VisionFilterKind, Toggle> filterToggles = new Dictionary<VisionFilterKind, Toggle>();
        readonly Toggle eyeHeightsField;
        readonly FloatField wheelchairField;
        readonly FloatField childField;

        public VisionSection() : base("See Through Other Eyes",
            "A Vision button shows the camera view as others see it: colour blindness, low vision, tunnel vision, cataract.\n" +
            "With a Design in the scene, an Eye button shows it from a wheelchair or a child's eye height.",
            "Add Vision Simulator")
        {
            foreach (var kind in VisionFilter.All)
            {
                filterToggles[kind] = AddField(new Toggle(VisionFilter.Name(kind)) { value = true },
                    VisionFilter.Description(kind));
            }

            eyeHeightsField = AddField(new Toggle("Eye heights (Design)") { value = true });
            wheelchairField = AddField(new FloatField("Wheelchair eye (m)") { value = 1.2f });
            childField = AddField(new FloatField("Child eye (m)") { value = 1.0f });
        }

        protected override void OnAdd()
        {
            var offered = filterToggles.Where(pair => pair.Value.value).Select(pair => pair.Key).ToList();
            if (offered.Count == 0)
            {
                ShowMessage("Tick at least one way of seeing.", HelpBoxMessageType.Warning);
                return;
            }

            var simulator = VisionBuilder.Create(offered, eyeHeightsField.value, wheelchairField.value, childField.value);
            RegisterCreated(simulator.gameObject, "Add Vision Simulator");
            ShowMessage("Added. Press Play and tap Vision on the HUD. Edit the texts in the Inspector.");
        }

        protected override string Describe(VisionSimulator item)
        {
            return $"{item.name} ({item.OfferedFilters.Count} ways of seeing)";
        }

        protected override void OnRefreshMore()
        {
            if (Existing().Count > 0 && !UrpRendererConfigurator.IsPhoneRenderingTuned)
            {
                ShowMessage("Tune phone rendering in the Setup tab: the vision filter needs a full-size camera copy.",
                    HelpBoxMessageType.Warning);
            }
        }
    }
}
