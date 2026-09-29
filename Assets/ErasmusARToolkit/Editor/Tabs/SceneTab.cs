using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Erasmus.ARToolkit.Editor
{
    /// <summary>Creates AR scenes and shows the toolkit state of the open scene (SceneTab.uxml).</summary>
    public sealed class SceneTab : IToolkitTab
    {
        const string DemoScenePath = ToolkitPaths.SamplesFolder + "/Scenes/ARToolkitDemo.unity";

        readonly ObjectField configField;
        readonly Button createConfigButton;
        readonly Button createSceneButton;
        readonly Button addToSceneButton;
        readonly Button openDemoButton;
        readonly HelpBox noToolkit;
        readonly VisualElement sceneStatus;
        readonly StatusRow managerRow;
        readonly StatusRow sceneConfigRow;
        readonly HelpBox configMismatch;
        readonly Button useSelectedConfig;
        readonly StatusRow inBuildRow;

        public SceneTab()
        {
            Root = EditorUi.Clone("SceneTab");
            configField = EditorUi.Q<ObjectField>(Root, "config-field");
            configField.objectType = typeof(ARToolkitConfig);
            createConfigButton = EditorUi.Q<Button>(Root, "create-config");
            createSceneButton = EditorUi.Q<Button>(Root, "create-scene");
            addToSceneButton = EditorUi.Q<Button>(Root, "add-to-scene");
            openDemoButton = EditorUi.Q<Button>(Root, "open-demo");
            noToolkit = EditorUi.Q<HelpBox>(Root, "no-toolkit");
            sceneStatus = EditorUi.Q<VisualElement>(Root, "scene-status");
            managerRow = EditorUi.Q<StatusRow>(Root, "row-manager");
            sceneConfigRow = EditorUi.Q<StatusRow>(Root, "row-scene-config");
            configMismatch = EditorUi.Q<HelpBox>(Root, "config-mismatch");
            useSelectedConfig = EditorUi.Q<Button>(Root, "use-selected-config");
            inBuildRow = EditorUi.Q<StatusRow>(Root, "row-in-build");

            configField.RegisterValueChangedCallback(_ => Refresh());
            createConfigButton.clicked += () =>
            {
                configField.value = ToolkitAssets.GetOrCreateConfig();
                Refresh();
            };
            createSceneButton.clicked += () =>
            {
                var config = SelectedConfig;
                EditorApplication.delayCall += () => SceneBuilder.CreateNewScene(config);
            };
            addToSceneButton.clicked += () =>
            {
                SceneBuilder.AddToCurrentScene(SelectedConfig);
                Refresh();
            };
            openDemoButton.clicked += OpenDemoScene;
            useSelectedConfig.clicked += AssignSelectedConfig;
            EditorUi.OnClick(Root, "select-manager", SelectManager);
        }

        public string Title => "Scene";
        public VisualElement Root { get; }

        ARToolkitConfig SelectedConfig => configField.value as ARToolkitConfig;

        public void Refresh()
        {
            if (configField.value == null)
            {
                configField.SetValueWithoutNotify(ToolkitAssets.FindConfig());
            }

            var hasConfig = SelectedConfig != null;
            EditorUi.SetShown(createConfigButton, !hasConfig);
            createSceneButton.SetEnabled(hasConfig);
            addToSceneButton.SetEnabled(hasConfig);
            EditorUi.SetShown(openDemoButton, AssetDatabase.LoadAssetAtPath<SceneAsset>(DemoScenePath) != null);
            RefreshOpenScene();
        }

        void RefreshOpenScene()
        {
            var manager = SceneQueries.FindFirst<ARToolkitManager>();
            EditorUi.SetShown(noToolkit, manager == null);
            EditorUi.SetShown(sceneStatus, manager != null);
            if (manager == null)
            {
                return;
            }

            managerRow.SetState(true, null, "AR Toolkit found: " + manager.name);
            sceneConfigRow.SetState(manager.Config != null, AssignSelectedConfig,
                manager.Config != null ? "Config assigned: " + manager.Config.name : "Config assigned");
            var mismatch = manager.Config != null && SelectedConfig != null && manager.Config != SelectedConfig;
            EditorUi.SetShown(configMismatch, mismatch);
            EditorUi.SetShown(useSelectedConfig, mismatch);

            var path = EditorSceneManager.GetActiveScene().path;
            var inBuild = !string.IsNullOrEmpty(path)
                          && System.Array.Exists(EditorBuildSettings.scenes, s => s.path == path && s.enabled);
            inBuildRow.SetState(inBuild, AddOpenSceneToBuild);
        }

        void AssignSelectedConfig()
        {
            var manager = SceneQueries.FindFirst<ARToolkitManager>();
            if (manager == null || SelectedConfig == null)
            {
                return;
            }

            Undo.RecordObject(manager, "Assign Toolkit Config");
            manager.SetConfig(SelectedConfig);
            EditorUtility.SetDirty(manager);
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
            Refresh();
        }

        void AddOpenSceneToBuild()
        {
            var path = EditorSceneManager.GetActiveScene().path;
            if (string.IsNullOrEmpty(path))
            {
                EditorUtility.DisplayDialog(ToolkitPaths.DisplayName, "Save the scene first.", "OK");
                return;
            }

            SceneBuilder.AddSceneToBuildSettings(path);
            Refresh();
        }

        static void OpenDemoScene()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                EditorSceneManager.OpenScene(DemoScenePath);
            }
        }

        static void SelectManager()
        {
            var manager = SceneQueries.FindFirst<ARToolkitManager>();
            if (manager != null)
            {
                Selection.activeGameObject = manager.gameObject;
                EditorGUIUtility.PingObject(manager.gameObject);
            }
        }
    }
}
