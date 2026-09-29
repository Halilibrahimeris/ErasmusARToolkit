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

            var fixedCount = 0;
            if (ProjectConfigurator.ConfigureAndroid())
            {
                fixedCount = Mathf.Max(0, ValidationFixer.FixAll(BuildTargetGroup.Android));
            }

            Debug.Log($"[Erasmus AR Toolkit] Setup finished ({fixedCount} validation fix(es) applied). " +
                      "Check the Setup tab for anything still marked.");
        }
    }
}
