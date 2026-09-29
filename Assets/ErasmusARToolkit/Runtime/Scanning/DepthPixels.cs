using Unity.Collections;
using UnityEngine.XR.ARSubsystems;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// Reads meters out of a depth CPU image (ARCore: 16-bit millimeters; others: 32-bit float meters),
    /// optionally skipping pixels whose confidence image value is too low. Only valid while the images
    /// are not disposed.
    /// </summary>
    internal readonly struct DepthPixels
    {
        const float MillimetersToMeters = 0.001f;

        readonly NativeArray<byte> depth;
        readonly int depthRowStride;
        readonly int depthPixelStride;
        readonly bool isFloat;
        readonly NativeArray<byte> confidence;
        readonly int confidenceRowStride;
        readonly int confidencePixelStride;
        readonly bool hasConfidence;
        readonly byte minConfidence;

        DepthPixels(XRCpuImage depthImage, XRCpuImage confidenceImage, bool useConfidence, byte minimumConfidence)
        {
            var depthPlane = depthImage.GetPlane(0);
            depth = depthPlane.data;
            depthRowStride = depthPlane.rowStride;
            depthPixelStride = depthPlane.pixelStride;
            isFloat = depthImage.format == XRCpuImage.Format.DepthFloat32;
            Width = depthImage.width;
            Height = depthImage.height;

            hasConfidence = useConfidence;
            minConfidence = minimumConfidence;
            if (useConfidence)
            {
                var confidencePlane = confidenceImage.GetPlane(0);
                confidence = confidencePlane.data;
                confidenceRowStride = confidencePlane.rowStride;
                confidencePixelStride = confidencePlane.pixelStride;
            }
            else
            {
                confidence = default;
                confidenceRowStride = 0;
                confidencePixelStride = 0;
            }
        }

        public int Width { get; }
        public int Height { get; }

        /// <summary>False for image formats this reader does not understand.</summary>
        public static bool TryCreate(XRCpuImage depthImage, XRCpuImage confidenceImage, bool hasConfidenceImage,
            byte minimumConfidence, out DepthPixels pixels)
        {
            pixels = default;
            if (!depthImage.valid || depthImage.planeCount < 1
                || (depthImage.format != XRCpuImage.Format.DepthUint16 && depthImage.format != XRCpuImage.Format.DepthFloat32))
            {
                return false;
            }

            var useConfidence = hasConfidenceImage && confidenceImage.valid && confidenceImage.planeCount > 0
                                && confidenceImage.format == XRCpuImage.Format.OneComponent8
                                && confidenceImage.width == depthImage.width
                                && confidenceImage.height == depthImage.height;
            pixels = new DepthPixels(depthImage, confidenceImage, useConfidence, minimumConfidence);
            return true;
        }

        /// <summary>Depth in meters, or 0 when there is none (no measurement or too little confidence).</summary>
        public float MetersAt(int x, int y)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height)
            {
                return 0f;
            }

            if (hasConfidence && confidence[y * confidenceRowStride + x * confidencePixelStride] < minConfidence)
            {
                return 0f;
            }

            var offset = y * depthRowStride + x * depthPixelStride;
            if (isFloat)
            {
                return depth.GetSubArray(offset, 4).Reinterpret<float>(1)[0];
            }

            return (depth[offset] | (depth[offset + 1] << 8)) * MillimetersToMeters;
        }

        /// <summary>First depth found in a small square around the pixel (raw depth has holes).</summary>
        public float MetersNear(int x, int y, int radius)
        {
            var value = MetersAt(x, y);
            for (var r = 1; r <= radius && value <= 0f; r++)
            {
                for (var dy = -r; dy <= r && value <= 0f; dy++)
                {
                    for (var dx = -r; dx <= r && value <= 0f; dx++)
                    {
                        value = MetersAt(x + dx, y + dy);
                    }
                }
            }

            return value;
        }
    }
}
