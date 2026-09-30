using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Erasmus.ARToolkit.Editor
{
    /// <summary>Projects tab: design walkthrough (a space designed in Unity, walked through with the phone).</summary>
    internal sealed class DesignSection : SceneProjectSection<DesignSpace>
    {
        /// <summary>More triangles than this make a phone slow.</summary>
        public const int HeavyTriangleCount = 500000;

        readonly Vector3Field sizeField;
        readonly Toggle shellField;
        readonly FloatField startHeightField;
        readonly ColorField skyField;

        public DesignSection() : base("Design Walkthrough",
            "Design a space in Unity (classroom, garden, museum) and walk through it with the phone.\n" +
            "The Start Camera comes to where the app starts. AR shows the real room, Virtual the design alone.",
            "Add Design")
        {
            sizeField = AddField(new Vector3Field("Size (W x H x L, m)") { value = new Vector3(8f, 3f, 6f) },
                "Width, height and length of the space in meters.");
            shellField = AddField(new Toggle("Room shell (Virtual view)") { value = true },
                "Floor, walls and ceiling, shown in the Virtual view only.");
            startHeightField = AddField(new FloatField("Start height (m)") { value = 1.5f },
                "Eye height of the Start Camera above the floor.");
            skyField = AddField(new ColorField("Sky color") { value = new Color(0.62f, 0.78f, 0.93f), showAlpha = false },
                "Shown instead of the camera picture in the Virtual view.");
            AddButton("Add Info Card To Selection", () =>
            {
                var added = InfoCardTools.AddToSelection("Title", "What this object is.");
                ShowMessage(added == 0
                        ? "Select objects of the design in the Hierarchy first."
                        : $"Added {added} card(s). Edit their text in the Inspector (Info Card).",
                    added == 0 ? HelpBoxMessageType.Warning : HelpBoxMessageType.Info);
            });
            AddButton("Look Through Start Camera", LookThroughStartCamera);
        }

        protected override void OnAdd()
        {
            var existing = Existing().FirstOrDefault();
            if (existing != null)
            {
                SelectAndPing(existing.gameObject);
                return;
            }

            var design = DesignBuilder.Create(sizeField.value, shellField.value, startHeightField.value, skyField.value);
            RegisterCreated(design.gameObject, "Add Design");
            ShowMessage("Design added. Build your space inside it; the yellow camera is where the phone starts.");
        }

        protected override string Describe(DesignSpace item)
        {
            var size = item.Size;
            return $"{item.name} ({size.x:0.#} x {size.y:0.#} x {size.z:0.#} m)";
        }

        protected override void OnRefreshMore()
        {
            var designs = Existing();
            MainButton.text = designs.Count > 0 ? "Select Design" : "Add Design";
            if (designs.Count == 0)
            {
                return;
            }

            var triangles = DesignBuilder.CountTriangles(designs[0].gameObject);
            if (triangles > HeavyTriangleCount)
            {
                ShowMessage($"The design has {triangles:N0} triangles. Phones slow down above {HeavyTriangleCount:N0}: " +
                            "use simpler models.", HelpBoxMessageType.Warning);
            }
        }

        static void LookThroughStartCamera()
        {
            var design = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponentInParent<DesignSpace>()
                : null;
            design = design != null ? design : Existing().FirstOrDefault();
            var start = design != null ? design.StartPoint : null;
            var view = SceneView.lastActiveSceneView;
            if (start == null || view == null)
            {
                return;
            }

            view.AlignViewToObject(start.transform);
            view.Repaint();
        }
    }

    /// <summary>Draws the size of a design as a 1 m grid in the Scene view.</summary>
    [CustomEditor(typeof(DesignSpace))]
    internal sealed class DesignSpaceEditor : UnityEditor.Editor
    {
        static readonly Color GridColor = new Color(1f, 0.8f, 0f, 0.35f);
        static readonly Color EdgeColor = new Color(1f, 0.8f, 0f, 0.9f);

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();
            var design = (DesignSpace)target;
            if (design.StartPoint != null && GUILayout.Button("Look Through Start Camera") && SceneView.lastActiveSceneView != null)
            {
                SceneView.lastActiveSceneView.AlignViewToObject(design.StartPoint.transform);
            }

            EditorGUILayout.HelpBox($"{DesignBuilder.CountTriangles(design.gameObject):N0} triangles.", MessageType.None);
        }

        void OnSceneGUI()
        {
            var design = (DesignSpace)target;
            var size = design.Size;
            var root = design.transform;
            using (new Handles.DrawingScope(Matrix4x4.TRS(root.position, root.rotation, Vector3.one)))
            {
                var halfX = size.x * 0.5f;
                var halfZ = size.z * 0.5f;
                Handles.color = GridColor;
                for (var x = Mathf.Ceil(-halfX); x <= halfX; x += 1f)
                {
                    Handles.DrawLine(new Vector3(x, 0f, -halfZ), new Vector3(x, 0f, halfZ));
                }

                for (var z = Mathf.Ceil(-halfZ); z <= halfZ; z += 1f)
                {
                    Handles.DrawLine(new Vector3(-halfX, 0f, z), new Vector3(halfX, 0f, z));
                }

                Handles.color = EdgeColor;
                Handles.DrawWireCube(new Vector3(0f, size.y * 0.5f, 0f), size);
                Handles.Label(new Vector3(-halfX, size.y, -halfZ), $"{size.x:0.0} x {size.y:0.0} x {size.z:0.0} m");
            }
        }
    }
}
