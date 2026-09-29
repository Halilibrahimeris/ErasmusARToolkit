using System;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Erasmus.ARToolkit.Editor
{
    /// <summary>
    /// Command-line entry points (for CI or scripted builds), for example:
    /// Unity.exe -batchmode -projectPath &lt;path&gt; -executeMethod Erasmus.ARToolkit.Editor.BatchCommands.BuildAndroid
    /// Each method exits Unity with code 0 on success and 1 on failure.
    /// Run BootstrapBatch.InstallDependencies first in a fresh project.
    /// </summary>
    public static class BatchCommands
    {
        /// <summary>
        /// Runs all Setup tab steps. The TextMeshPro essentials are a package import that Unity
        /// processes on a later editor update, so the other steps run once it has finished.
        /// </summary>
        public static void Setup()
        {
            if (ProjectConfigurator.AreTmpEssentialsImported)
            {
                RunSetupSteps();
                return;
            }

            AssetDatabase.importPackageCompleted += OnTmpImportFinished;
            AssetDatabase.importPackageFailed += OnTmpImportFailed;
            ProjectConfigurator.ImportTmpEssentials();
        }

        static void OnTmpImportFinished(string packageName)
        {
            StopWaitingForTmp();
            RunSetupSteps();
        }

        static void OnTmpImportFailed(string packageName, string error)
        {
            StopWaitingForTmp();
            Debug.LogError("[Erasmus AR Toolkit] TextMeshPro essentials import failed: " + error);
            RunSetupSteps();
        }

        static void StopWaitingForTmp()
        {
            AssetDatabase.importPackageCompleted -= OnTmpImportFinished;
            AssetDatabase.importPackageFailed -= OnTmpImportFailed;
        }

        static void RunSetupSteps()
        {
            Run(() =>
            {
                ToolkitSetup.RunAll();
                return ProjectConfigurator.IsArCoreEnabled && ProjectConfigurator.AreTmpEssentialsImported;
            });
        }

        /// <summary>Adds the sample content to the project config and creates an AR scene with it.</summary>
        public static void CreateSampleScene()
        {
            Run(() =>
            {
                var config = ToolkitAssets.GetOrCreateConfig();
                SampleContent.AddTo(config);
                return SceneBuilder.CreateNewScene(config) != null;
            });
        }

        public static void BuildAndroid()
        {
            Run(() => Succeeded(BuildRunner.Build(BuildPlatform.Android, BuildRunner.DefaultOutputFolder, false)));
        }

        public static void BuildWindows()
        {
            Run(() => Succeeded(BuildRunner.Build(BuildPlatform.Windows, BuildRunner.DefaultOutputFolder, false)));
        }

        static bool Succeeded(BuildReport report)
        {
            return report != null && report.summary.result == BuildResult.Succeeded;
        }

        static void Run(Func<bool> command)
        {
            var success = false;
            try
            {
                success = command();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }

            Debug.Log("[Erasmus AR Toolkit] Batch command " + (success ? "succeeded." : "FAILED."));
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(success ? 0 : 1);
            }
        }
    }
}
