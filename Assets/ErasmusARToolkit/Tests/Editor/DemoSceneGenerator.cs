using Erasmus.ARToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace Erasmus.ARToolkit.Tests
{
    /// <summary>
    /// Developer tool: (re)generates the demo config and demo scene that ship in the Samples folder.
    /// Not exported with the package.
    /// </summary>
    static class DemoSceneGenerator
    {
        const string DemoConfigPath = ToolkitPaths.SamplesFolder + "/ARToolkitDemoConfig.asset";
        const string DemoScenePath = ToolkitPaths.SamplesFolder + "/Scenes/ARToolkitDemo.unity";

        [MenuItem("Tools/Erasmus AR Toolkit/Developer/Generate Demo Scene", priority = 1010)]
        static void Generate()
        {
            var config = AssetDatabase.LoadAssetAtPath<ARToolkitConfig>(DemoConfigPath);
            if (config == null)
            {
                ToolkitAssets.EnsureFolder(ToolkitPaths.SamplesFolder);
                config = ScriptableObject.CreateInstance<ARToolkitConfig>();
                AssetDatabase.CreateAsset(config, DemoConfigPath);
            }

            SampleContent.AddTo(config);
            SceneBuilder.CreateNewScene(config, DemoScenePath, overwrite: true);
        }
    }
}
