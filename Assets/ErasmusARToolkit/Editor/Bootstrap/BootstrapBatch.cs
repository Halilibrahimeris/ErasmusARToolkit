using System.Diagnostics;
using System.Linq;
using System.Threading;
using UnityEditor;
using UnityEditor.PackageManager;
using Debug = UnityEngine.Debug;

namespace Erasmus.ARToolkit.Editor
{
    /// <summary>
    /// Command-line entry point that works before AR Foundation is installed (for CI or scripted setup):
    /// Unity.exe -batchmode -projectPath &lt;path&gt; -executeMethod Erasmus.ARToolkit.Editor.BootstrapBatch.InstallDependencies
    /// </summary>
    public static class BootstrapBatch
    {
        const int TimeoutMilliseconds = 10 * 60 * 1000;

        public static void InstallDependencies()
        {
            var missing = DependencyInstaller.GetMissingPackages();
            if (missing.Count == 0)
            {
                Debug.Log("[Erasmus AR Toolkit] All required packages are already installed.");
                EditorApplication.Exit(0);
                return;
            }

            Debug.Log("[Erasmus AR Toolkit] Installing: " + string.Join(", ", missing));
            var request = Client.AddAndRemove(packagesToAdd: missing.ToArray());
            var timer = Stopwatch.StartNew();
            while (!request.IsCompleted && timer.ElapsedMilliseconds < TimeoutMilliseconds)
            {
                // The Package Manager runs out of process, so waiting here is safe in batch mode.
                Thread.Sleep(250);
            }

            var success = request.IsCompleted && request.Status == StatusCode.Success;
            if (!success)
            {
                Debug.LogError("[Erasmus AR Toolkit] Package installation failed: " +
                               (request.Error != null ? request.Error.message : "timed out"));
            }

            EditorApplication.Exit(success ? 0 : 1);
        }
    }
}
