using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// Converts a pose from OpenCV's camera convention (x right, y down, z forward; right-handed)
    /// to a Unity camera-local pose (x right, y up, z forward; left-handed).
    /// The object frame is the image: x = image right, y = image down, z = into the image.
    /// The resulting Unity pose follows AR Foundation's ARTrackedImage convention:
    /// Y = image normal toward the viewer, Z = toward the image top.
    /// </summary>
    public static class PoseConversion
    {
        /// <param name="rotation">3x3 rotation (object to camera), row-major, e.g. from Rodrigues.</param>
        /// <param name="translation">Object origin in camera space, meters.</param>
        public static Pose OpenCvToUnityCamera(double[,] rotation, double[] translation)
        {
            var imageRight = FlipY(Column(rotation, 0));
            var imageTop = -FlipY(Column(rotation, 1));
            var imageNormal = -FlipY(Column(rotation, 2));

            var unityRotation = Quaternion.LookRotation(imageTop, imageNormal);
            var position = FlipY(new Vector3((float)translation[0], (float)translation[1], (float)translation[2]));

            // LookRotation derives "right" from top and normal; it matches imageRight for a proper rotation.
            Debug.Assert(Vector3.Dot(unityRotation * Vector3.right, imageRight) > 0.9f);
            return new Pose(position, unityRotation);
        }

        static Vector3 Column(double[,] matrix, int column)
        {
            return new Vector3((float)matrix[0, column], (float)matrix[1, column], (float)matrix[2, column]);
        }

        static Vector3 FlipY(Vector3 value)
        {
            return new Vector3(value.x, -value.y, value.z);
        }
    }

    /// <summary>Color32 pixels (Unity order, bottom row first) to 8-bit grayscale for image analysis.</summary>
    public static class GrayscaleConverter
    {
        /// <param name="step">Keep every n-th pixel in both directions (1 = full size).</param>
        /// <param name="flipVertical">True to output the top row first (OpenCV order).</param>
        public static byte[] FromColor32(Color32[] pixels, int width, int height, int step, bool flipVertical)
        {
            step = Mathf.Max(1, step);
            var outWidth = width / step;
            var outHeight = height / step;
            var gray = new byte[outWidth * outHeight];
            for (var y = 0; y < outHeight; y++)
            {
                var sourceRow = flipVertical ? height - 1 - y * step : y * step;
                var sourceOffset = sourceRow * width;
                var targetOffset = y * outWidth;
                for (var x = 0; x < outWidth; x++)
                {
                    var p = pixels[sourceOffset + x * step];
                    gray[targetOffset + x] = (byte)((77 * p.r + 150 * p.g + 29 * p.b) >> 8);
                }
            }

            return gray;
        }
    }
}
