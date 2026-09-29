using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// Finds the depth image pixel behind a point on the screen. ARCore's camera background shader maps
    /// screen UV to camera texture UV with the frame's display matrix as a row vector,
    /// <c>(u, v, 1, 0) * M</c>, and samples the depth texture with the same UV. How the CPU depth image rows
    /// relate to that UV (and whether a platform hands the matrix over transposed) is not documented, so
    /// the four variants are candidates and <see cref="DepthScanSource"/> checks them against ARCore's own
    /// depth hit test on the device.
    /// </summary>
    public readonly struct DepthImageMapping
    {
        public static readonly DepthImageMapping[] Candidates =
        {
            new DepthImageMapping(false, false),
            new DepthImageMapping(false, true),
            new DepthImageMapping(true, false),
            new DepthImageMapping(true, true),
        };

        public DepthImageMapping(bool transposed, bool flipRows)
        {
            Transposed = transposed;
            FlipRows = flipRows;
        }

        /// <summary>Use the display matrix transposed (column-vector convention).</summary>
        public bool Transposed { get; }

        /// <summary>Image row 0 is at texture v = 1 instead of v = 0.</summary>
        public bool FlipRows { get; }

        /// <summary>Texture UV for a viewport point (0..1, origin bottom left, like Camera.ViewportPointToRay).</summary>
        public Vector2 ToTextureUv(Vector2 viewport, Matrix4x4 display)
        {
            if (Transposed)
            {
                return new Vector2(
                    viewport.x * display.m00 + viewport.y * display.m01 + display.m02,
                    viewport.x * display.m10 + viewport.y * display.m11 + display.m12);
            }

            return new Vector2(
                viewport.x * display.m00 + viewport.y * display.m10 + display.m20,
                viewport.x * display.m01 + viewport.y * display.m11 + display.m21);
        }

        /// <summary>The depth image pixel for a viewport point. False when it falls outside the image.</summary>
        public bool TryGetPixel(Vector2 viewport, Matrix4x4 display, int width, int height, out int x, out int y)
        {
            var uv = ToTextureUv(viewport, display);
            x = Mathf.FloorToInt(uv.x * width);
            var row = Mathf.FloorToInt(uv.y * height);
            y = FlipRows ? height - 1 - row : row;
            return x >= 0 && x < width && row >= 0 && row < height;
        }

        public override string ToString()
        {
            return (Transposed ? "transposed" : "row-vector") + (FlipRows ? ", rows flipped" : string.Empty);
        }
    }

    public static class DepthGeometry
    {
        /// <summary>
        /// World point for a depth value. ARCore depth is measured along the camera's view axis (not along
        /// the ray), so the distance along the ray is depth / cos(angle between ray and view axis).
        /// </summary>
        public static Vector3 PointFromDepth(Vector3 cameraPosition, Vector3 cameraForward, Vector3 rayDirection,
            float depth)
        {
            var cos = Vector3.Dot(rayDirection, cameraForward);
            return cos <= 1e-4f ? cameraPosition : cameraPosition + rayDirection * (depth / cos);
        }

        /// <summary>
        /// Camera-space direction (Unity camera space, looking down -z) of the ray through a viewport point,
        /// from the inverse of Camera.projectionMatrix. Rotate it with Camera.cameraToWorldMatrix.
        /// </summary>
        public static Vector3 ViewDirection(Matrix4x4 inverseProjection, Vector2 viewport)
        {
            var x = viewport.x * 2f - 1f;
            var y = viewport.y * 2f - 1f;
            var near = inverseProjection.MultiplyPoint(new Vector3(x, y, -1f));
            var far = inverseProjection.MultiplyPoint(new Vector3(x, y, 1f));
            return (far - near).normalized;
        }

        /// <summary>
        /// True if the surface spanned by two tangent vectors faces up or down (a floor or table top).
        /// <paramref name="minCosine"/> is the cosine of the largest allowed tilt.
        /// </summary>
        public static bool IsHorizontal(Vector3 tangentA, Vector3 tangentB, float minCosine)
        {
            var normal = Vector3.Cross(tangentA, tangentB);
            var length = normal.magnitude;
            return length > 1e-8f && Mathf.Abs(normal.y) >= minCosine * length;
        }
    }
}
