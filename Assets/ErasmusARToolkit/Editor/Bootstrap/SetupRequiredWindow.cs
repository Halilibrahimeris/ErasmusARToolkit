using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Erasmus.ARToolkit.Editor
{
    /// <summary>
    /// Small window shown while the AR packages are missing (SetupRequiredWindow.uxml). Once they
    /// are installed the full toolkit window (in the AR-dependent editor assembly) takes over the menu.
    /// </summary>
    public sealed class SetupRequiredWindow : EditorWindow
    {
        Label missingLabel;
        Button installButton;
        HelpBox doneBox;

        public static void Open()
        {
            var window = GetWindow<SetupRequiredWindow>(true, ToolkitPaths.DisplayName + " Setup", true);
            window.minSize = new Vector2(420, 240);
        }

#if !(ERASMUS_HAS_ARF && ERASMUS_HAS_INPUTSYSTEM)
        [MenuItem(ToolkitPaths.OpenMenuPath, priority = 0)]
        static void OpenFromMenu()
        {
            Open();
        }
#endif

        void CreateGUI()
        {
            var style = LoadByName<StyleSheet>("ARToolkitWindow");
            if (style != null)
            {
                rootVisualElement.styleSheets.Add(style);
            }

            var layout = LoadByName<VisualTreeAsset>("SetupRequiredWindow");
            if (layout == null)
            {
                rootVisualElement.Add(new HelpBox("SetupRequiredWindow.uxml is missing. Use Tools > Erasmus AR Toolkit > " +
                                                  "Install Dependencies.", HelpBoxMessageType.Error));
                return;
            }

            layout.CloneTree(rootVisualElement);
            missingLabel = rootVisualElement.Q<Label>("missing");
            installButton = rootVisualElement.Q<Button>("install");
            doneBox = rootVisualElement.Q<HelpBox>("done");
            installButton.clicked += DependencyInstaller.InstallMissingPackages;
            UpdateState();
        }

        void OnInspectorUpdate()
        {
            UpdateState();
        }

        void UpdateState()
        {
            if (installButton == null)
            {
                return;
            }

            var missing = DependencyInstaller.GetMissingPackages();
            missingLabel.text = missing.Count > 0 ? "Missing: " + string.Join(", ", missing) : string.Empty;
            installButton.style.display = missing.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            installButton.SetEnabled(!DependencyInstaller.IsInstalling);
            installButton.text = DependencyInstaller.IsInstalling ? "Installing..." : "Install Required Packages";
            doneBox.style.display = missing.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        static T LoadByName<T>(string fileName) where T : Object
        {
            foreach (var guid in AssetDatabase.FindAssets($"{fileName} t:{typeof(T).Name}"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) == fileName)
                {
                    return AssetDatabase.LoadAssetAtPath<T>(path);
                }
            }

            return null;
        }
    }
}
