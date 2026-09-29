using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.UIElements;

namespace Erasmus.ARToolkit.Editor
{
    /// <summary>One-click builds for Android (APK) and Windows (EXE) (BuildTab.uxml).</summary>
    public sealed class BuildTab : IToolkitTab
    {
        const string OutputFolderKey = "ErasmusARToolkit.BuildFolder";
        const string PlatformKey = "ErasmusARToolkit.BuildPlatform";

        readonly RadioButtonGroup platformGroup;
        readonly TextField outputFolder;
        readonly Label outputFile;
        readonly HelpBox noScenes;
        readonly VisualElement sceneList;
        readonly HelpBox sceneHint;
        readonly HelpBox platformHint;
        readonly HelpBox problem;
        readonly Button buildButton;
        readonly Button buildRunButton;
        readonly HelpBox result;

        public BuildTab()
        {
            Root = EditorUi.Clone("BuildTab");
            platformGroup = EditorUi.Q<RadioButtonGroup>(Root, "platform");
            outputFolder = EditorUi.Q<TextField>(Root, "output-folder");
            outputFile = EditorUi.Q<Label>(Root, "output-file");
            noScenes = EditorUi.Q<HelpBox>(Root, "no-scenes");
            sceneList = EditorUi.Q<VisualElement>(Root, "scene-list");
            sceneHint = EditorUi.Q<HelpBox>(Root, "scene-hint");
            platformHint = EditorUi.Q<HelpBox>(Root, "platform-hint");
            problem = EditorUi.Q<HelpBox>(Root, "problem");
            buildButton = EditorUi.Q<Button>(Root, "build");
            buildRunButton = EditorUi.Q<Button>(Root, "build-run");
            result = EditorUi.Q<HelpBox>(Root, "result");

            platformGroup.RegisterValueChangedCallback(evt =>
            {
                EditorPrefs.SetInt(PlatformKey, evt.newValue);
                Refresh();
            });
            outputFolder.RegisterValueChangedCallback(evt =>
            {
                EditorPrefs.SetString(OutputFolderKey, evt.newValue);
                Refresh();
            });
            EditorUi.OnClick(Root, "browse", BrowseFolder);
            buildButton.clicked += () => RunBuild(false);
            buildRunButton.clicked += () => RunBuild(true);
            EditorUi.SetShown(result, false);
        }

        public string Title => "Build";
        public VisualElement Root { get; }

        BuildPlatform Platform => (BuildPlatform)Mathf.Clamp(platformGroup.value, 0, 1);

        public void Refresh()
        {
            platformGroup.SetValueWithoutNotify(EditorPrefs.GetInt(PlatformKey, (int)BuildPlatform.Android));
            outputFolder.SetValueWithoutNotify(EditorPrefs.GetString(OutputFolderKey, BuildRunner.DefaultOutputFolder));
            outputFile.text = "Output file: " +
                              BuildPaths.GetLocationPath(Platform, outputFolder.value, PlayerSettings.productName);

            var scenes = BuildRunner.GetScenes();
            EditorUi.SetShown(noScenes, scenes.Length == 0);
            sceneList.Clear();
            for (var i = 0; i < scenes.Length; i++)
            {
                sceneList.Add(new Label("• " + scenes[i] + (i == 0 ? "  (starts on the phone)" : string.Empty)));
            }

            var hint = BuildPaths.FirstSceneHint(scenes);
            sceneHint.text = hint ?? string.Empty;
            EditorUi.SetShown(sceneHint, hint != null);

            platformHint.text = Platform == BuildPlatform.Android
                ? "Connect an ARCore-supported phone with USB debugging on for 'Build And Run'. " +
                  "Switching platform the first time can take a few minutes."
                : "The Windows build runs in Desktop mode: mouse and keyboard on a virtual ground grid.";
            buildRunButton.text = Platform == BuildPlatform.Android ? "Build And Run On Phone" : "Build And Run";

            var blocking = BuildRunner.GetBlockingProblem(Platform);
            problem.text = blocking ?? string.Empty;
            EditorUi.SetShown(problem, blocking != null);
            buildButton.SetEnabled(blocking == null);
            buildRunButton.SetEnabled(blocking == null);
        }

        void BrowseFolder()
        {
            var picked = EditorUtility.OpenFolderPanel("Output Folder", outputFolder.value, string.Empty);
            if (!string.IsNullOrEmpty(picked))
            {
                outputFolder.value = picked;
            }
        }

        void RunBuild(bool run)
        {
            var platform = Platform;
            var folder = outputFolder.value;
            EditorApplication.delayCall += () =>
            {
                var report = BuildRunner.Build(platform, folder, run);
                ShowResult(report);
            };
        }

        void ShowResult(BuildReport report)
        {
            EditorUi.SetShown(result, true);
            if (report == null)
            {
                result.text = "Build cancelled.";
                result.messageType = HelpBoxMessageType.Warning;
                return;
            }

            var summary = report.summary;
            var text = $"{summary.result}: {summary.outputPath}\n{summary.totalErrors} error(s), " +
                       $"{summary.totalWarnings} warning(s), {summary.totalTime.TotalSeconds:0}s";
            if (summary.result == BuildResult.Failed)
            {
                var firstError = report.steps.SelectMany(step => step.messages)
                    .FirstOrDefault(message => message.type == LogType.Error).content;
                if (!string.IsNullOrEmpty(firstError))
                {
                    text += "\nFirst error: " + firstError;
                }
            }

            result.text = text;
            result.messageType = summary.result == BuildResult.Succeeded
                ? HelpBoxMessageType.Info
                : HelpBoxMessageType.Error;
        }
    }
}
