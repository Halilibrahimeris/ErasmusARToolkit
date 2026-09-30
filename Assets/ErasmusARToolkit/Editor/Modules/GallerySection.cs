using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Video;

namespace Erasmus.ARToolkit.Editor
{
    /// <summary>
    /// Makes exhibit prefabs for wall galleries: a picture or a video in an optional frame. The pivot lies
    /// on the wall, in the middle of the picture; the picture faces +Z (into the room).
    /// </summary>
    internal static class ExhibitPrefabBuilder
    {
        public const float FrameWidth = 0.04f;
        public const float FrameDepth = 0.03f;
        public const float BackDepth = 0.01f;
        public const string PictureName = "Picture";

        public static GameObject Build(string name, Texture2D image, VideoClip video, float width, bool frame,
            Color frameColor, string folder = ToolkitPaths.GalleryFolder)
        {
            var size = SizeOf(image, video, width);
            var safeName = SafeName(name);
            ToolkitAssets.EnsureFolder(folder + "/Materials");
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture");
            var picture = new Material(shader) { name = safeName, mainTexture = image, color = Color.white };
            AssetDatabase.CreateAsset(picture, AssetDatabase.GenerateUniqueAssetPath($"{folder}/Materials/{safeName}.mat"));
            var frameMaterial = ModuleMaterials.Lit("Frame " + ColorUtility.ToHtmlStringRGB(frameColor), frameColor);

            var root = new GameObject(safeName);
            try
            {
                BuildContent(root, size, picture, frame ? frameMaterial : null, frameMaterial, video);
                return PrefabUtility.SaveAsPrefabAsset(root, AssetDatabase.GenerateUniqueAssetPath($"{folder}/{safeName}.prefab"));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>Width and height of the picture in meters (the shape of the image or video).</summary>
        public static Vector2 SizeOf(Texture2D image, VideoClip video, float width)
        {
            width = Mathf.Max(0.05f, width);
            if (video != null)
            {
                return PosterLayout.FitWidth(width, (int)video.width, (int)video.height);
            }

            return image != null && image.width > 0
                ? new Vector2(width, width * image.height / image.width)
                : new Vector2(width, width * 0.75f);
        }

        /// <param name="frame">Frame material, or null for no frame.</param>
        /// <param name="back">Material of the thin board behind the picture.</param>
        internal static void BuildContent(GameObject root, Vector2 size, Material picture, Material frame, Material back,
            VideoClip video)
        {
            // A thin board behind the picture: it gives the exhibit a collider to tap and some depth.
            ModuleShapes.Part(root.transform, PrimitiveType.Cube, new Vector3(0f, 0f, BackDepth * 0.5f),
                new Vector3(size.x, size.y, BackDepth), back).name = "Back";

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = PictureName;
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.transform.SetParent(root.transform, false);
            quad.transform.localPosition = new Vector3(0f, 0f, BackDepth + 0.001f);

            // A quad is seen from its -Z side: turned around, it faces the room.
            quad.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            quad.transform.localScale = new Vector3(size.x, size.y, 1f);
            if (picture != null)
            {
                quad.GetComponent<MeshRenderer>().sharedMaterial = picture;
            }

            if (video != null)
            {
                quad.AddComponent<VideoSurface>().Configure(video, null, true, 1f);
            }

            if (frame == null)
            {
                return;
            }

            var w = FrameWidth;
            var z = FrameDepth * 0.5f;
            var outerWidth = size.x + 2f * w;
            ModuleShapes.Part(root.transform, PrimitiveType.Cube, new Vector3(0f, size.y * 0.5f + w * 0.5f, z),
                new Vector3(outerWidth, w, FrameDepth), frame).name = "Frame Top";
            ModuleShapes.Part(root.transform, PrimitiveType.Cube, new Vector3(0f, -size.y * 0.5f - w * 0.5f, z),
                new Vector3(outerWidth, w, FrameDepth), frame).name = "Frame Bottom";
            ModuleShapes.Part(root.transform, PrimitiveType.Cube, new Vector3(size.x * 0.5f + w * 0.5f, 0f, z),
                new Vector3(w, size.y, FrameDepth), frame).name = "Frame Right";
            ModuleShapes.Part(root.transform, PrimitiveType.Cube, new Vector3(-size.x * 0.5f - w * 0.5f, 0f, z),
                new Vector3(w, size.y, FrameDepth), frame).name = "Frame Left";
        }

        static string SafeName(string name)
        {
            var clean = new string((name ?? "Exhibit").Where(c => char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_').ToArray()).Trim();
            return clean.Length > 0 ? clean : "Exhibit";
        }
    }

    /// <summary>Projects tab: wall gallery (pictures, videos and objects that hang on the walls of the room).</summary>
    internal sealed class GallerySection : ProjectSection
    {
        readonly ObjectField imageField;
        readonly ObjectField videoField;
        readonly FloatField widthField;
        readonly Toggle frameField;
        readonly ColorField frameColorField;
        readonly TextField titleField;
        readonly TextField bodyField;
        readonly Button addButton;

        public GallerySection() : base("Wall Gallery",
            "Pictures and videos that users hang on the walls of the room: pick one in the object bar, tap a wall.\n" +
            "Any placeable object can hang on walls: set its Place On to Wall in the Content tab.")
        {
            imageField = AddField(new ObjectField("Picture") { objectType = typeof(Texture2D), allowSceneObjects = false },
                "A painting, a photo, a drawing. Empty (and no video): the toolkit makes an example picture.");
            videoField = AddField(new ObjectField("Or video") { objectType = typeof(VideoClip), allowSceneObjects = false },
                "Plays on the wall. H.264 MP4 works on every phone.");
            widthField = AddField(new FloatField("Width (m)") { value = 0.6f });
            frameField = AddField(new Toggle("Frame") { value = true });
            frameColorField = AddField(new ColorField("Frame color") { value = new Color(0.35f, 0.22f, 0.12f), showAlpha = false });
            titleField = AddField(new TextField("Title") { value = "Exhibit" });
            bodyField = AddField(new TextField("Text") { multiline = true, value = "Artist, year and what it shows." });
            addButton = AddButton("Add Exhibit", AddExhibit, true);
        }

        void AddExhibit()
        {
            var config = Config;
            if (config == null)
            {
                return;
            }

            var image = imageField.value as Texture2D;
            var video = videoField.value as VideoClip;
            if (image == null && video == null)
            {
                image = MarkerFiles.Create(ToolkitPaths.GalleryFolder + "/Images", "Painting");
            }

            var baseName = string.IsNullOrWhiteSpace(titleField.value)
                ? (video != null ? video.name : image.name)
                : titleField.value;
            var name = ConfigEdits.UniquePlaceableName(config, baseName);
            var prefab = ExhibitPrefabBuilder.Build(name, image, video, widthField.value, frameField.value, frameColorField.value);
            ConfigEdits.AddPlaceable(config, new PlaceableItem(name, prefab, 1f, new InfoLabelData(titleField.value, bodyField.value),
                null, PlaceOn.Wall), "Add Exhibit");
            var warning = video != null && !MediaFileRules.IsPhoneFriendlyVideo(AssetDatabase.GetAssetPath(video))
                ? " The video is not an MP4 file: it may not play on Android."
                : string.Empty;
            ShowMessage($"Added '{name}' ({AssetDatabase.GetAssetPath(prefab)}). Pick it in the object bar and tap a wall." + warning,
                warning.Length > 0 ? HelpBoxMessageType.Warning : HelpBoxMessageType.Info);
        }

        protected override void OnRefresh()
        {
            var config = Config;
            addButton.SetEnabled(config != null);
            var exhibits = config != null
                ? config.Placeables.Where(p => p != null && p.PlaceOn == PlaceOn.Wall).ToList()
                : new List<PlaceableItem>();
            ShowRows("On walls (placeable objects)", exhibits, item => item.ResolvedName,
                item => SelectAndPing(item.Prefab),
                item => ConfigEdits.RemovePlaceable(config, item, "Remove Exhibit"),
                config != null ? "None yet." : "Create the AR scene first.");
        }
    }
}
