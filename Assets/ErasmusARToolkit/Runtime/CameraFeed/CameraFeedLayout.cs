using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>How the camera image quad is scaled, rotated and textured.</summary>
    public readonly struct CameraFeedPlacement
    {
        public CameraFeedPlacement(Vector3 scale, float rotationDegrees, bool flipVertical)
        {
            Scale = scale;
            RotationDegrees = rotationDegrees;
            FlipVertical = flipVertical;
        }

        /// <summary>Always positive, so the quad keeps facing the camera.</summary>
        public Vector3 Scale { get; }

        /// <summary>Rotation around the view axis (Z).</summary>
        public float RotationDegrees { get; }

        /// <summary>Flip the texture upside down (done with UVs, not a negative scale).</summary>
        public bool FlipVertical { get; }
    }

    /// <summary>
    /// Fits a camera image behind the scene so it covers the whole view without stretching,
    /// taking the sensor rotation of phones (90/270 degrees) and vertical mirroring into account.
    /// </summary>
    public static class CameraFeedLayout
    {
        public static CameraFeedPlacement Compute(float viewHeight, float viewAspect, int textureWidth,
            int textureHeight, int rotationAngle, bool verticallyMirrored)
        {
            if (textureWidth <= 0 || textureHeight <= 0 || viewHeight <= 0f || viewAspect <= 0f)
            {
                return new CameraFeedPlacement(Vector3.zero, 0f, false);
            }

            var rotated = Mathf.Abs(rotationAngle) % 180 == 90;
            var imageAspect = rotated
                ? (float)textureHeight / textureWidth
                : (float)textureWidth / textureHeight;
            var cover = CoverSize(viewHeight, viewAspect, imageAspect);

            // After a 90 degree turn the quad's X axis runs along the screen's Y axis.
            var scale = rotated ? new Vector3(cover.y, cover.x, 1f) : new Vector3(cover.x, cover.y, 1f);
            return new CameraFeedPlacement(scale, -rotationAngle, verticallyMirrored);
        }

        /// <summary>Size (in screen axes) that covers a view without stretching the image.</summary>
        public static Vector2 CoverSize(float viewHeight, float viewAspect, float imageAspect)
        {
            var viewWidth = viewHeight * viewAspect;
            return imageAspect >= viewAspect
                ? new Vector2(viewHeight * imageAspect, viewHeight)
                : new Vector2(viewWidth, viewWidth / imageAspect);
        }
    }
}
