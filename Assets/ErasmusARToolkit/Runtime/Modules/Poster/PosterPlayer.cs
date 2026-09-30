using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// Brings a living poster to life: its video lies on the printed image, at the image's size, and its
    /// sound plays. Added to image target content that has <see cref="PosterMedia"/>.
    /// </summary>
    public static class PosterPlayer
    {
        public const string VideoObjectName = "Poster Video";

        /// <summary>Just above the image, so the video is not hidden by the paper.</summary>
        public const float HeightAboveImage = 0.002f;

        /// <param name="content">The content object that follows the image (it is switched off when the image is lost).</param>
        /// <param name="imageSize">Size of the image in meters.</param>
        public static VideoSurface Attach(GameObject content, PosterMedia media, Vector2 imageSize, ToolkitTheme theme)
        {
            if (content == null || media == null || !media.HasAny)
            {
                return null;
            }

            GameObject target;
            if (media.HasVideo)
            {
                target = GameObject.CreatePrimitive(PrimitiveType.Quad);
                target.name = VideoObjectName;
                ObjectUtility.SafeDestroy(target.GetComponent<Collider>());
                var scale = content.transform.localScale;
                target.transform.SetParent(content.transform, false);
                target.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                target.transform.localPosition = new Vector3(0f, HeightAboveImage / SafeScale(scale.y), 0f);
                target.transform.localScale = PosterLayout.QuadScale(imageSize, scale);
                var renderer = target.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = CreateMaterial(theme);
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            else
            {
                target = content;
            }

            var surface = target.AddComponent<VideoSurface>();
            surface.Configure(media.Video, media.Audio, media.Loop, media.Volume);
            surface.Play();
            return surface;
        }

        static Material CreateMaterial(ToolkitTheme theme)
        {
            // The theme's image material uses an unlit shader that is part of every build.
            var source = theme != null ? theme.ImageTargetMaterial : null;
            var material = source != null ? new Material(source) : new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.name = "Poster Video";
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
            material.color = Color.white;
            return material;
        }

        static float SafeScale(float value) => Mathf.Abs(value) < 0.0001f ? 1f : value;
    }

    /// <summary>Sizes of video pictures. Pure math, so it can be tested.</summary>
    public static class PosterLayout
    {
        /// <summary>
        /// Local scale of a quad that lies flat on an image of <paramref name="imageSize"/> meters, under a
        /// parent with <paramref name="parentScale"/> (the quad's X is the image width, its Y the image height).
        /// </summary>
        public static Vector3 QuadScale(Vector2 imageSize, Vector3 parentScale)
        {
            return new Vector3(Divide(imageSize.x, parentScale.x), Divide(imageSize.y, parentScale.z), 1f);
        }

        /// <summary>Width and height (meters) of a picture <paramref name="width"/> wide with the pixel size of a video.</summary>
        public static Vector2 FitWidth(float width, int pixelWidth, int pixelHeight)
        {
            if (pixelWidth <= 0 || pixelHeight <= 0)
            {
                return new Vector2(width, width * 9f / 16f);
            }

            return new Vector2(width, width * pixelHeight / pixelWidth);
        }

        static float Divide(float value, float divisor)
        {
            return Mathf.Abs(divisor) < 0.0001f ? value : value / Mathf.Abs(divisor);
        }
    }
}
