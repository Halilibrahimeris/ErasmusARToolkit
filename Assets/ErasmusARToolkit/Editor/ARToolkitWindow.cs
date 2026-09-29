using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Erasmus.ARToolkit.Editor
{
    /// <summary>
    /// Main toolkit window (Tools > Erasmus AR Toolkit > Open Toolkit), built with UI Toolkit.
    /// Layout: ARToolkitWindow.uxml, styles: ARToolkitWindow.uss, one UXML per tab.
    /// </summary>
    public sealed class ARToolkitWindow : EditorWindow
    {
        const string SelectedTabKey = "ErasmusARToolkit.SelectedTab";
        const string ActiveTabClass = "tab-button--active";

        readonly List<Button> tabButtons = new List<Button>();
        IToolkitTab[] tabs;
        int selectedTab;

        [MenuItem(ToolkitPaths.OpenMenuPath, priority = 0)]
        public static void Open()
        {
            var window = GetWindow<ARToolkitWindow>();
            window.titleContent = new GUIContent("AR Toolkit", EditorGUIUtility.IconContent("d_SceneViewCamera").image);
            window.minSize = new Vector2(460, 420);
            window.Show();
        }

        void CreateGUI()
        {
            var layout = EditorUi.Clone("ARToolkitWindow");
            EditorUi.AddStyleSheet(rootVisualElement);
            rootVisualElement.Add(layout);
            EditorUi.Q<Label>(layout, "version").text = "v" + ToolkitPaths.Version;

            tabs = new IToolkitTab[]
            {
                new SetupTab(),
                new SceneTab(),
                new ContentTab(),
                new BuildTab(),
                new ExportTab(),
            };

            var tabBar = EditorUi.Q<VisualElement>(layout, "tab-bar");
            var content = EditorUi.Q<ScrollView>(layout, "tab-content");
            for (var i = 0; i < tabs.Length; i++)
            {
                var index = i;
                var button = new Button(() => SelectTab(index)) { text = tabs[i].Title };
                button.AddToClassList("tab-button");
                tabBar.Add(button);
                tabButtons.Add(button);
                content.Add(tabs[i].Root);
            }

            SelectTab(Mathf.Clamp(EditorPrefs.GetInt(SelectedTabKey, 0), 0, tabs.Length - 1));
        }

        void OnFocus()
        {
            RefreshSelected();
        }

        void OnProjectChange()
        {
            RefreshSelected();
        }

        void OnHierarchyChange()
        {
            RefreshSelected();
        }

        void SelectTab(int index)
        {
            selectedTab = index;
            EditorPrefs.SetInt(SelectedTabKey, index);
            for (var i = 0; i < tabs.Length; i++)
            {
                EditorUi.SetShown(tabs[i].Root, i == index);
                tabButtons[i].EnableInClassList(ActiveTabClass, i == index);
            }

            RefreshSelected();
        }

        void RefreshSelected()
        {
            if (tabs != null && selectedTab >= 0 && selectedTab < tabs.Length)
            {
                tabs[selectedTab].Refresh();
            }
        }
    }
}
