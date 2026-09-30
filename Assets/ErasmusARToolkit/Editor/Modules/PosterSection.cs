using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Video;

namespace Erasmus.ARToolkit.Editor
{
    /// <summary>Adds living posters (image targets with video and sound) to a config.</summary>
    internal static class PosterBuilder
    {
        public const string Folder = ToolkitPaths.ModulesFolder + "/Posters";

        /// <param name="image">The printed image; null makes a new marker to print.</param>
        public static ImageTargetItem Add(ARToolkitConfig config, Texture2D image, float widthMeters, VideoClip video,
            AudioClip audio, bool loop, GameObject model, string title, string body)
        {
            if (image == null)
            {
                image = MarkerFiles.Create(Folder, "Poster");
            }

            var name = ConfigEdits.UniqueTargetName(config, image.name);
            var item = new ImageTargetItem(name, image, Mathf.Max(0.05f, widthMeters), model,
                new InfoLabelData(title, body), new PosterMedia(video, audio, loop));
            ConfigEdits.AddImageTarget(config, item, "Add Poster");
            return item;
        }

        /// <summary>What a poster shows, for the list in the Projects tab.</summary>
        public static string Describe(ImageTargetItem item)
        {
            var parts = new List<string>();
            if (item.Media.HasVideo)
            {
                parts.Add("video");
            }

            if (item.Media.HasAudio)
            {
                parts.Add("sound");
            }

            if (item.Prefab != null)
            {
                parts.Add("model");
            }

            if (item.Info.HasContent)
            {
                parts.Add("card");
            }

            return $"{item.ResolvedName} ({item.WidthMeters:0.##} m): {string.Join(", ", parts)}";
        }
    }

    /// <summary>Projects tab: living poster (a printed poster that plays a video, a sound or shows a model).</summary>
    internal sealed class PosterSection : ProjectSection
    {
        readonly ObjectField imageField;
        readonly FloatField widthField;
        readonly ObjectField videoField;
        readonly ObjectField audioField;
        readonly Toggle loopField;
        readonly ObjectField modelField;
        readonly TextField titleField;
        readonly TextField bodyField;
        readonly Button addButton;

        public PosterSection() : base("Living Poster",
            "A printed poster comes to life when the camera sees it: a video plays on it, a sound starts, a model appears.\n" +
            "Leave the image empty to get a new marker to print. Use H.264 MP4 video for Android.")
        {
            imageField = AddField(new ObjectField("Image (PNG/JPG)") { objectType = typeof(Texture2D), allowSceneObjects = false },
                "The printed poster. Empty: the toolkit makes a marker to print.");
            widthField = AddField(new FloatField("Printed width (m)") { value = 0.3f }, "A4 portrait is 0.21 m, A3 0.297 m.");
            videoField = AddField(new ObjectField("Video (optional)") { objectType = typeof(VideoClip), allowSceneObjects = false });
            audioField = AddField(new ObjectField("Sound (optional)") { objectType = typeof(AudioClip), allowSceneObjects = false });
            loopField = AddField(new Toggle("Loop") { value = true });
            modelField = AddField(new ObjectField("3D model (optional)") { objectType = typeof(GameObject), allowSceneObjects = false });
            titleField = AddField(new TextField("Card title") { value = "Poster" });
            bodyField = AddField(new TextField("Card text") { multiline = true, value = "What this poster tells." });
            addButton = AddButton("Add Poster", AddPoster, true);
        }

        void AddPoster()
        {
            var config = Config;
            if (config == null)
            {
                return;
            }

            var video = videoField.value as VideoClip;
            var item = PosterBuilder.Add(config, imageField.value as Texture2D, widthField.value, video,
                audioField.value as AudioClip, loopField.value, modelField.value as GameObject, titleField.value, bodyField.value);
            var warning = video != null && !MediaFileRules.IsPhoneFriendlyVideo(AssetDatabase.GetAssetPath(video))
                ? " The video is not an MP4 file: it may not play on Android."
                : string.Empty;
            ShowMessage($"Added poster '{item.ResolvedName}'. Print {AssetDatabase.GetAssetPath(item.Texture)} " +
                        $"{item.WidthMeters:0.##} m wide.{warning}",
                warning.Length > 0 ? HelpBoxMessageType.Warning : HelpBoxMessageType.Info);
        }

        protected override void OnRefresh()
        {
            var config = Config;
            addButton.SetEnabled(config != null);
            var posters = config != null
                ? config.ImageTargets.Where(t => t != null && t.Media.HasAny).ToList()
                : new List<ImageTargetItem>();
            ShowRows("Posters in the config", posters, PosterBuilder.Describe,
                _ => SelectAndPing(config),
                item => ConfigEdits.RemoveImageTarget(config, item, "Remove Poster"),
                config != null ? "None yet." : "Create the AR scene first.");
        }
    }
}
