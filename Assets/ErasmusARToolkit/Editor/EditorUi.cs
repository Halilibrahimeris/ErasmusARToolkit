using System;
using UnityEngine.UIElements;

namespace Erasmus.ARToolkit.Editor
{
    /// <summary>One page of the toolkit window, built from its own UXML file.</summary>
    public interface IToolkitTab
    {
        string Title { get; }

        VisualElement Root { get; }

        /// <summary>Re-reads project state and updates the UI.</summary>
        void Refresh();
    }

    /// <summary>Small helpers for loading and querying the toolkit's editor UXML.</summary>
    public static class EditorUi
    {
        public const string StyleSheetName = "ARToolkitWindow";

        /// <summary>Instantiates a UXML file by name, or returns an error label if it is missing.</summary>
        public static VisualElement Clone(string uxmlName)
        {
            var asset = ToolkitAssets.FindAsset<VisualTreeAsset>(uxmlName);
            if (asset == null)
            {
                return new HelpBox($"{uxmlName}.uxml was not found. Re-import the toolkit package.", HelpBoxMessageType.Error);
            }

            var root = asset.Instantiate();
            root.style.flexGrow = 1;
            return root;
        }

        public static void AddStyleSheet(VisualElement root)
        {
            var sheet = ToolkitAssets.FindAsset<StyleSheet>(StyleSheetName);
            if (sheet != null)
            {
                root.styleSheets.Add(sheet);
            }
        }

        /// <summary>Finds a named element or a placeholder, so a broken UXML never throws.</summary>
        public static T Q<T>(VisualElement root, string name) where T : VisualElement, new()
        {
            var element = root.Q<T>(name);
            if (element != null)
            {
                return element;
            }

            UnityEngine.Debug.LogError($"[Erasmus AR Toolkit] UI element '{name}' ({typeof(T).Name}) is missing from the UXML.");
            return new T();
        }

        public static void SetShown(VisualElement element, bool shown)
        {
            element.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public static void OnClick(VisualElement root, string buttonName, Action action)
        {
            Q<Button>(root, buttonName).clicked += action;
        }

        public static HelpBox Message(string text, HelpBoxMessageType type)
        {
            return new HelpBox(text, type);
        }
    }
}
