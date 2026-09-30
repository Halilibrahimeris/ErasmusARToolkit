using System;
using System.Linq;
using Erasmus.ARToolkit.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Erasmus.ARToolkit.Tests
{
    /// <summary>
    /// Developer tool: (re)generates the demo config and demo scenes that ship in the Samples folder.
    /// Not exported with the package.
    /// </summary>
    static class DemoSceneGenerator
    {
        const string DemoConfigPath = ToolkitPaths.SamplesFolder + "/ARToolkitDemoConfig.asset";
        const string DemoScenePath = ToolkitPaths.SamplesFolder + "/Scenes/ARToolkitDemo.unity";
        const string ModuleScenesFolder = ToolkitPaths.SamplesFolder + "/Scenes/Modules";
        const string ModuleMaterialsFolder = ToolkitPaths.SamplesFolder + "/Materials/Modules";

        [MenuItem("Tools/Erasmus AR Toolkit/Developer/Generate Demo Scene", priority = 1010)]
        static void Generate()
        {
            SceneBuilder.CreateNewScene(DemoConfig(), DemoScenePath, overwrite: true);
        }

        /// <summary>One scene per scene module, built with the same builders as the Projects tab.</summary>
        [MenuItem("Tools/Erasmus AR Toolkit/Developer/Generate Module Demos", priority = 1011)]
        public static void GenerateModuleDemos()
        {
            var config = DemoConfig();
            var buildScenes = EditorBuildSettings.scenes;
            ToolkitAssets.EnsureFolder(ModuleScenesFolder);
            Material Lit(string name, Color color) => ToolkitAssets.GetOrCreateLit(ModuleMaterialsFolder, name, color);

            Build(config, "DesignDemo", () =>
            {
                DesignBuilder.Create(new Vector3(8f, 3f, 6f), true, 1.5f, new Color(0.55f, 0.75f, 0.95f), Lit);
                var all = Enum.GetValues(typeof(VisionFilterKind)).Cast<VisionFilterKind>();
                VisionBuilder.Create(all, true, 1.2f, 1.0f);
            });
            Build(config, "RoomFillDemo", () =>
            {
                FillBuilder.Create(FillKind.Water, 1f, 20f, FillBuilder.DefaultColor(FillKind.Water), null, new Vector2(4f, 4f),
                    FillBuilder.DefaultLabel(FillKind.Water), "The room is flooded.", PlacementMode.TapToPlace);
            });
            Build(config, "PortalDemo", () =>
            {
                PortalBuilder.Create(1.2f, 2.2f, new Vector3(6f, 3f, 6f), new Color(0.35f, 0.25f, 0.2f), new Color(0.45f, 0.7f, 0.95f),
                    null, PlacementMode.TapToPlace, Lit);
            });
            Build(config, "StoryDemo", () => StoryBuilder.CreateStory(PlacementMode.TapToPlace, Lit));

            // The module demos are examples to open, not scenes of the build.
            EditorBuildSettings.scenes = buildScenes;
        }

        static void Build(ARToolkitConfig config, string name, Action addModules)
        {
            var path = ModuleScenesFolder + "/" + name + ".unity";
            if (SceneBuilder.CreateNewScene(config, path, overwrite: true) == null)
            {
                return;
            }

            addModules();
            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, path);
        }

        static ARToolkitConfig DemoConfig()
        {
            var config = AssetDatabase.LoadAssetAtPath<ARToolkitConfig>(DemoConfigPath);
            if (config == null)
            {
                ToolkitAssets.EnsureFolder(ToolkitPaths.SamplesFolder);
                config = ScriptableObject.CreateInstance<ARToolkitConfig>();
                AssetDatabase.CreateAsset(config, DemoConfigPath);
            }

            SampleContent.AddTo(config);
            return config;
        }
    }
}
