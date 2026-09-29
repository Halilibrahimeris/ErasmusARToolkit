using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Erasmus.ARToolkit.Editor
{
    /// <summary>Exports the toolkit as a .unitypackage for other projects (ExportTab.uxml).</summary>
    public sealed class ExportTab : IToolkitTab
    {
        const string FolderKey = "ErasmusARToolkit.ExportFolder";
        const string VersionKey = "ErasmusARToolkit.ExportVersion";

        readonly TextField version;
        readonly HelpBox versionError;
        readonly Toggle includeSamples;
        readonly TextField outputFolder;
        readonly Label fileName;
        readonly Label assetCount;
        readonly Button exportButton;
        readonly HelpBox result;
        readonly Button revealButton;
        string lastExport;

        public ExportTab()
        {
            Root = EditorUi.Clone("ExportTab");
            EditorUi.Q<HelpBox>(Root, "export-info").text =
                "Exports everything under " + ToolkitPaths.ToolkitRoot + " except the Tests folder. " +
                "Your own content in " + ToolkitPaths.ContentRoot + " is not included.\n\n" +
                "In another project: Assets > Import Package > Custom Package, then follow the setup window " +
                "that opens to install AR Foundation and ARCore.";
            version = EditorUi.Q<TextField>(Root, "version");
            versionError = EditorUi.Q<HelpBox>(Root, "version-error");
            includeSamples = EditorUi.Q<Toggle>(Root, "include-samples");
            outputFolder = EditorUi.Q<TextField>(Root, "output-folder");
            fileName = EditorUi.Q<Label>(Root, "file-name");
            assetCount = EditorUi.Q<Label>(Root, "asset-count");
            exportButton = EditorUi.Q<Button>(Root, "export");
            result = EditorUi.Q<HelpBox>(Root, "result");
            revealButton = EditorUi.Q<Button>(Root, "reveal");

            version.RegisterValueChangedCallback(evt =>
            {
                EditorPrefs.SetString(VersionKey, evt.newValue.Trim());
                UpdateLabels();
            });
            includeSamples.RegisterValueChangedCallback(_ => UpdateLabels());
            outputFolder.RegisterValueChangedCallback(evt => EditorPrefs.SetString(FolderKey, evt.newValue));
            EditorUi.OnClick(Root, "browse", BrowseFolder);
            exportButton.clicked += Export;
            revealButton.clicked += () => EditorUtility.RevealInFinder(lastExport);
            EditorUi.SetShown(result, false);
            EditorUi.SetShown(revealButton, false);
        }

        public string Title => "Export";
        public VisualElement Root { get; }

        public void Refresh()
        {
            version.SetValueWithoutNotify(EditorPrefs.GetString(VersionKey, ToolkitPaths.Version));
            outputFolder.SetValueWithoutNotify(EditorPrefs.GetString(FolderKey,
                Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Exports")));
            UpdateLabels();
        }

        void UpdateLabels()
        {
            var valid = PackageExportRules.IsValidVersion(version.value);
            EditorUi.SetShown(versionError, !valid);
            exportButton.SetEnabled(valid);
            fileName.text = "File: " + PackageExportRules.CreateFileName(version.value);
            assetCount.text = "Assets: " + PackageExporter.CollectPaths(includeSamples.value).Length;
        }

        void BrowseFolder()
        {
            var picked = EditorUtility.OpenFolderPanel("Output Folder", outputFolder.value, string.Empty);
            if (!string.IsNullOrEmpty(picked))
            {
                outputFolder.value = picked;
            }
        }

        void Export()
        {
            lastExport = PackageExporter.Export(version.value, outputFolder.value, includeSamples.value);
            var ok = !string.IsNullOrEmpty(lastExport);
            EditorUi.SetShown(result, true);
            EditorUi.SetShown(revealButton, ok);
            result.text = ok ? "Exported: " + lastExport : "Export failed. See the Console for details.";
            result.messageType = ok ? HelpBoxMessageType.Info : HelpBoxMessageType.Error;
        }
    }
}
