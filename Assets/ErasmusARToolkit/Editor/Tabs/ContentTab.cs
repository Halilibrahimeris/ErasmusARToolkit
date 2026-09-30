using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Erasmus.ARToolkit.Editor
{
    /// <summary>
    /// Edits placeables, image targets, info labels and behaviour settings (ContentTab.uxml).
    /// The fields are bound to the selected ARToolkitConfig through binding-path attributes.
    /// </summary>
    public sealed class ContentTab : IToolkitTab
    {
        const string DropHoverClass = "drop-area--hover";

        readonly ObjectField configField;
        readonly VisualElement noConfig;
        readonly VisualElement configEditor;
        readonly VisualElement issuesList;
        readonly VisualElement qualityResults;
        readonly Label dropArea;
        readonly Button selectLibraryButton;
        ARToolkitConfig config;
        SerializedObject serializedConfig;

        public ContentTab()
        {
            Root = EditorUi.Clone("ContentTab");
            configField = EditorUi.Q<ObjectField>(Root, "config-field");
            configField.objectType = typeof(ARToolkitConfig);
            noConfig = EditorUi.Q<VisualElement>(Root, "no-config");
            configEditor = EditorUi.Q<VisualElement>(Root, "config-editor");
            issuesList = EditorUi.Q<VisualElement>(Root, "issues");
            qualityResults = EditorUi.Q<VisualElement>(Root, "quality-results");
            dropArea = EditorUi.Q<Label>(Root, "drop-area");
            selectLibraryButton = EditorUi.Q<Button>(Root, "select-library");

            configField.RegisterValueChangedCallback(evt => SetConfig(evt.newValue as ARToolkitConfig));
            EditorUi.OnClick(Root, "create-config", () => configField.value = ToolkitAssets.GetOrCreateConfig());
            EditorUi.OnClick(Root, "rebuild-library", () =>
            {
                ReferenceLibraryBuilder.Rebuild(config);
                RefreshLibraryButton();
            });
            EditorUi.OnClick(Root, "check-quality", CheckImageQuality);
            selectLibraryButton.clicked += () => Selection.activeObject = config != null ? config.ImageLibrary : null;
            EditorUi.OnClick(Root, "add-samples", () =>
            {
                SampleContent.AddTo(config);
                SetConfig(config);
            });
            RegisterDropArea();
        }

        public string Title => "Content";
        public VisualElement Root { get; }

        public void Refresh()
        {
            var current = config != null ? config : ToolkitAssets.FindConfig();
            if (current != config || serializedConfig == null)
            {
                configField.SetValueWithoutNotify(current);
                SetConfig(current);
            }
            else
            {
                Validate();
            }
        }

        void SetConfig(ARToolkitConfig newConfig)
        {
            config = newConfig;
            configEditor.Unbind();
            serializedConfig = config != null ? new SerializedObject(config) : null;

            EditorUi.SetShown(noConfig, config == null);
            EditorUi.SetShown(configEditor, config != null);
            qualityResults.Clear();
            if (serializedConfig != null)
            {
                configEditor.Bind(serializedConfig);
                configEditor.TrackSerializedObjectValue(serializedConfig, _ => Validate());
            }

            Validate();
            RefreshLibraryButton();
        }

        void RefreshLibraryButton()
        {
            selectLibraryButton.SetEnabled(config != null && config.ImageLibrary != null);
        }

        void Validate()
        {
            issuesList.Clear();
            if (config == null)
            {
                return;
            }

            var issues = ConfigValidator.Validate(config);
            var imageWarnings = config.ImageTargets
                .Where(target => target != null && target.Texture != null)
                .SelectMany(ImageWarnings)
                .ToList();

            if (issues.Count == 0 && imageWarnings.Count == 0)
            {
                issuesList.Add(EditorUi.Message("Content looks good.", HelpBoxMessageType.Info));
                return;
            }

            foreach (var issue in issues)
            {
                issuesList.Add(EditorUi.Message(issue.Message,
                    issue.Severity == IssueSeverity.Error ? HelpBoxMessageType.Error : HelpBoxMessageType.Warning));
            }

            foreach (var warning in imageWarnings)
            {
                issuesList.Add(EditorUi.Message(warning, HelpBoxMessageType.Warning));
            }
        }

        static IEnumerable<string> ImageWarnings(ImageTargetItem target)
        {
            var path = AssetDatabase.GetAssetPath(target.Texture);
            if (!ImageFileRules.IsSupportedFormat(path))
            {
                yield return $"Image '{target.ResolvedName}' must be a PNG or JPG file for ARCore.";
            }

            if (!ImageFileRules.IsLargeEnough(target.Texture.width, target.Texture.height))
            {
                yield return $"Image '{target.ResolvedName}' is smaller than {ImageFileRules.MinimumPixels} px; it may not be detected.";
            }

            if (target.Media.HasVideo && !MediaFileRules.IsPhoneFriendlyVideo(AssetDatabase.GetAssetPath(target.Media.Video)))
            {
                yield return $"The video of '{target.ResolvedName}' is not an MP4 file; Android phones play H.264 MP4 video.";
            }
        }

        void CheckImageQuality()
        {
            qualityResults.Clear();
            if (config == null)
            {
                return;
            }

            var targets = config.ImageTargets.Where(t => t != null && t.Texture != null).ToList();
            if (targets.Count == 0)
            {
                qualityResults.Add(EditorUi.Message("No image targets to check.", HelpBoxMessageType.Info));
                return;
            }

            try
            {
                for (var i = 0; i < targets.Count; i++)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("Checking image quality", targets[i].ResolvedName,
                            i / (float)targets.Count))
                    {
                        qualityResults.Add(EditorUi.Message("Check cancelled.", HelpBoxMessageType.None));
                        break;
                    }

                    qualityResults.Add(QualityMessage(targets[i].ResolvedName, ImageQualityChecker.Evaluate(targets[i].Texture)));
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        static HelpBox QualityMessage(string name, int score)
        {
            if (score < 0)
            {
                return EditorUi.Message($"'{name}': could not be scored (needs a PNG/JPG and the ARCore plugin).",
                    HelpBoxMessageType.Warning);
            }

            return ImageQualityChecker.IsGoodScore(score)
                ? EditorUi.Message($"'{name}': quality {score}/100 - good.", HelpBoxMessageType.Info)
                : EditorUi.Message($"'{name}': quality {score}/100 - below {ImageQualityChecker.RecommendedScore}, " +
                                   "use a more detailed image.", HelpBoxMessageType.Warning);
        }

        void RegisterDropArea()
        {
            dropArea.RegisterCallback<DragEnterEvent>(_ => dropArea.AddToClassList(DropHoverClass));
            dropArea.RegisterCallback<DragLeaveEvent>(_ => dropArea.RemoveFromClassList(DropHoverClass));
            dropArea.RegisterCallback<DragUpdatedEvent>(_ =>
                DragAndDrop.visualMode = config != null ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected);
            dropArea.RegisterCallback<DragPerformEvent>(_ =>
            {
                dropArea.RemoveFromClassList(DropHoverClass);
                if (config == null)
                {
                    return;
                }

                DragAndDrop.AcceptDrag();
                AddDropped(DragAndDrop.objectReferences);
            });
        }

        void AddDropped(IEnumerable<Object> objects)
        {
            serializedConfig.Update();
            var placeables = serializedConfig.FindProperty("placeables");
            var targets = serializedConfig.FindProperty("imageTargets");
            foreach (var dropped in objects)
            {
                if (dropped is GameObject prefab && PrefabUtility.IsPartOfPrefabAsset(prefab))
                {
                    var element = Append(placeables);
                    element.FindPropertyRelative("displayName").stringValue = prefab.name;
                    element.FindPropertyRelative("prefab").objectReferenceValue = prefab;
                    element.FindPropertyRelative("defaultScale").floatValue = 1f;
                    ModuleFieldDefaults.ResetPlaceable(element);
                }
                else if (dropped is Texture2D texture)
                {
                    var element = Append(targets);
                    element.FindPropertyRelative("name").stringValue = texture.name;
                    element.FindPropertyRelative("texture").objectReferenceValue = texture;
                    element.FindPropertyRelative("widthMeters").floatValue = 0.2f;
                    element.FindPropertyRelative("prefab").objectReferenceValue = null;
                    ModuleFieldDefaults.ResetImageTarget(element);
                }
            }

            serializedConfig.ApplyModifiedProperties();
            Validate();
        }

        /// <summary>
        /// A new list element is a copy of the last one: the project module fields go back to their
        /// defaults, so a dropped image does not become a video poster or a route stop by accident.
        /// </summary>
        static class ModuleFieldDefaults
        {
            public static void ResetImageTarget(SerializedProperty element)
            {
                var media = element.FindPropertyRelative("media");
                if (media != null)
                {
                    media.FindPropertyRelative("video").objectReferenceValue = null;
                    media.FindPropertyRelative("audio").objectReferenceValue = null;
                    media.FindPropertyRelative("loop").boolValue = true;
                    media.FindPropertyRelative("volume").floatValue = 1f;
                }

                var stop = element.FindPropertyRelative("routeStop");
                if (stop != null)
                {
                    stop.FindPropertyRelative("order").intValue = 0;
                    stop.FindPropertyRelative("nextHint").stringValue = string.Empty;
                    stop.FindPropertyRelative("arrow").enumValueIndex = 0;
                }
            }

            public static void ResetPlaceable(SerializedProperty element)
            {
                var placeOn = element.FindPropertyRelative("placeOn");
                if (placeOn != null)
                {
                    placeOn.enumValueIndex = 0;
                }
            }
        }

        static SerializedProperty Append(SerializedProperty array)
        {
            array.arraySize++;
            return array.GetArrayElementAtIndex(array.arraySize - 1);
        }
    }
}
