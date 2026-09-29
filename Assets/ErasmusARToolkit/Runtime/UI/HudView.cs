using UnityEngine;
using UnityEngine.UIElements;

namespace Erasmus.ARToolkit
{
    /// <summary>Typed access to the named elements of ARToolkitHUD.uxml.</summary>
    internal sealed class HudView
    {
        public const string HiddenClass = "hidden";
        public const string VisibleClass = "visible";
        public const string SelectedClass = "selected";
        public const string PlaceableButtonClass = "placeable-button";

        HudView(VisualElement root)
        {
            Root = root;
            SafeArea = Require<VisualElement>(root, "safe-area");
            Status = Require<Label>(root, "status");
            PlanesButton = Require<Button>(root, "planes-button");
            CameraButton = Require<Button>(root, "camera-button");
            ClearButton = Require<Button>(root, "clear-button");
            PhotoButton = Require<Button>(root, "photo-button");
            HelpButton = Require<Button>(root, "help-button");
            Toast = Require<Label>(root, "toast");
            Placeables = Require<ScrollView>(root, "placeables");
            HelpPanel = Require<VisualElement>(root, "help-panel");
            HelpText = Require<Label>(root, "help-text");
            HelpClose = Require<Button>(root, "help-close");
        }

        public VisualElement Root { get; }
        public VisualElement SafeArea { get; }
        public Label Status { get; }
        public Button PlanesButton { get; }
        public Button CameraButton { get; }
        public Button ClearButton { get; }
        public Button PhotoButton { get; }
        public Button HelpButton { get; }
        public Label Toast { get; }
        public ScrollView Placeables { get; }
        public VisualElement HelpPanel { get; }
        public Label HelpText { get; }
        public Button HelpClose { get; }

        /// <summary>Returns null (and logs) if the UXML is missing an element.</summary>
        public static HudView TryBind(VisualElement root)
        {
            try
            {
                return root != null ? new HudView(root) : null;
            }
            catch (MissingElementException exception)
            {
                Debug.LogError("[Erasmus AR Toolkit] ARToolkitHUD.uxml is missing an element: " + exception.Message);
                return null;
            }
        }

        public static void SetShown(VisualElement element, bool shown)
        {
            element.EnableInClassList(HiddenClass, !shown);
        }

        static T Require<T>(VisualElement root, string name) where T : VisualElement
        {
            return root.Q<T>(name) ?? throw new MissingElementException($"{typeof(T).Name} '{name}'");
        }

        sealed class MissingElementException : System.Exception
        {
            public MissingElementException(string message) : base(message)
            {
            }
        }
    }
}
