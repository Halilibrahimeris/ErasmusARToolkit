using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Erasmus.ARToolkit.Editor
{
    /// <summary>Builds the Android APK or the Windows player from the scenes in Build Settings.</summary>
    public static class BuildRunner
    {
        public static string DefaultOutputFolder =>
            Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Builds");

        public static string[] GetScenes()
        {
            return EditorBuildSettings.scenes.Where(scene => scene.enabled && File.Exists(scene.path))
                .Select(scene => scene.path).ToArray();
        }

        /// <summary>Returns a problem that blocks the build, or null when ready.</summary>
        public static string GetBlockingProblem(BuildPlatform platform)
        {
            if (platform == BuildPlatform.Android && !ProjectConfigurator.IsAndroidModuleInstalled)
            {
                return "Android Build Support is not installed. Add it to this Unity version in Unity Hub.";
            }

            if (platform == BuildPlatform.Windows && !ProjectConfigurator.IsWindowsModuleInstalled)
            {
                return "Windows Build Support is not installed. Add it to this Unity version in Unity Hub.";
            }

            if (platform == BuildPlatform.Android && !ProjectConfigurator.IsArCoreEnabled)
            {
                return "ARCore is not enabled for Android. Use 'Configure Android' in the Setup tab.";
            }

            if (GetScenes().Length == 0)
            {
                return "No scenes in Build Settings. Create an AR scene in the Scene tab first.";
            }

            var config = ToolkitAssets.FindConfig();
            if (config != null && ConfigValidator.HasErrors(ConfigValidator.Validate(config)))
            {
                return "The toolkit config has errors. Fix them in the Content tab.";
            }

            return null;
        }

        public static BuildReport Build(BuildPlatform platform, string outputFolder, bool run)
        {
            var problem = GetBlockingProblem(platform);
            if (problem != null)
            {
                EditorUtility.DisplayDialog(ToolkitPaths.DisplayName, problem, "OK");
                return null;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return null;
            }

            var location = BuildPaths.GetLocationPath(platform, outputFolder, PlayerSettings.productName);
            Directory.CreateDirectory(Path.GetDirectoryName(location) ?? outputFolder);

            var options = new BuildPlayerOptions
            {
                scenes = GetScenes(),
                locationPathName = location,
                target = platform == BuildPlatform.Android ? BuildTarget.Android : BuildTarget.StandaloneWindows64,
                targetGroup = platform == BuildPlatform.Android ? BuildTargetGroup.Android : BuildTargetGroup.Standalone,
                options = run ? BuildOptions.AutoRunPlayer : BuildOptions.None,
            };

            if (platform == BuildPlatform.Android)
            {
                EditorUserBuildSettings.buildAppBundle = false;
            }

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            if (summary.result == BuildResult.Succeeded)
            {
                var megabytes = SizeOnDisk(platform, location) / (1024f * 1024f);
                Debug.Log($"[Erasmus AR Toolkit] Build succeeded: {location} ({megabytes:0.0} MB)");
            }
            else
            {
                Debug.LogError($"[Erasmus AR Toolkit] Build {summary.result} with {summary.totalErrors} error(s). See the Console.");
            }

            return report;
        }

        /// <summary>
        /// summary.totalSize counts intermediate files, so measure the output instead: the APK file
        /// on Android, the whole player folder (exe + _Data) on Windows.
        /// </summary>
        static long SizeOnDisk(BuildPlatform platform, string location)
        {
            try
            {
                if (platform == BuildPlatform.Android)
                {
                    return File.Exists(location) ? new FileInfo(location).Length : 0L;
                }

                var folder = new DirectoryInfo(Path.GetDirectoryName(location) ?? ".");
                return folder.Exists ? folder.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : 0L;
            }
            catch (IOException)
            {
                return 0L;
            }
        }
    }
}
