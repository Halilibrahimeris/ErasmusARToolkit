using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Erasmus.ARToolkit.Editor
{
    /// <summary>
    /// Makes sure an Android build contains the room scan shaders. Unity only builds shaders that an
    /// asset of the build refers to: the theme refers to the room scan materials and shaders, and every
    /// toolkit scene refers to the theme. A build started from Unity's own Build window passes here too.
    /// </summary>
    sealed class ToolkitPreBuild : IPreprocessBuildWithReport
    {
        // Before the image library is rebuilt (ReferenceLibraryPreBuild, -100).
        public int callbackOrder => -200;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android)
            {
                return;
            }

            // A project that never ran the toolkit setup has no theme and no toolkit scene to protect.
            var theme = ToolkitAssets.FindTheme();
            if (theme != null && !ToolkitAssets.EnsureRoomScanAssets(theme))
            {
                throw new BuildFailedException(
                    "[Erasmus AR Toolkit] The room scan materials are missing, so the room scan would not work " +
                    "on the phone. Run Tools > Erasmus AR Toolkit > Run Setup and build again.");
            }
        }
    }
}
