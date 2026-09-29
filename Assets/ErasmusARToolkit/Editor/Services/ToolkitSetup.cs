using UnityEditor;
using UnityEngine;

namespace Erasmus.ARToolkit.Editor
{
    /// <summary>All one-time project setup steps in order. Used by the Setup tab and the menu.</summary>
    public static class ToolkitSetup
    {
        public static void RunAll()
        {
            ToolkitAssets.GetOrCreateConfig();
            ToolkitAssets.GetOrCreatePlanePrefab(ToolkitAssets.GetOrCreateTheme());
            ToolkitAssets.GetOrCreatePanelSettings();
            ProjectConfigurator.ImportTmpEssentials();
            UrpRendererConfigurator.AddMissingFeatures();
            UrpRendererConfigurator.MakeStencilSafe();
            SceneUpgrader.UpgradeOpenScenes();

            var fixedCount = 0;
            if (ProjectConfigurator.ConfigureAndroid())
            {
                fixedCount = Mathf.Max(0, ValidationFixer.FixAll(BuildTargetGroup.Android));

                // After the validation fixes: they create the ARCore settings this changes.
                ProjectConfigurator.SetDepthOptional();
            }

            Debug.Log($"[Erasmus AR Toolkit] Setup finished ({fixedCount} validation fix(es) applied). " +
                      "Check the Setup tab for anything still marked.");
        }
    }
}
