using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// A copy of one depth image with everything that belongs to it: how sure ARCore is of every pixel,
    /// and where the camera was and how it looked when the image was taken. The image itself has to be
    /// given back to ARCore at once; the copy is read later, when the scan takes the frame.
    /// Holds native memory: <see cref="Dispose"/> it.
    /// </summary>
    internal sealed class DepthSnapshot : IDisposable
    {
        const float MetersToMillimeters = 1000f;
        const float MillimetersToMeters = 0.001f;

        NativeArray<ushort> depth;
        NativeArray<byte> confidence;

        public int Width { get; private set; }

        public int Height { get; private set; }

        /// <summary>The copy is complete and was not read yet.</summary>
        public bool Ready { get; set; }

        public Vector3 Position { get; set; }

        public Quaternion Rotation { get; set; }

        public Matrix4x4 Projection { get; set; }

        /// <summary>The display matrix of the camera frame (screen to camera image).</summary>
        public Matrix4x4 Display { get; set; }

        /// <summary>Time of the camera frame the image arrived with, in seconds.</summary>
        public double FrameTime { get; set; }

        /// <summary>Time of the depth image, in seconds.</summary>
        public double DepthTime { get; set; }

        /// <summary>Millimeters of every pixel, row by row (0 = no measurement).</summary>
        public NativeArray<ushort> Depth => depth;

        public NativeArray<byte> Confidence => confidence;

        public void Dispose()
        {
            Ready = false;
            Width = 0;
            Height = 0;
            if (depth.IsCreated)
            {
                depth.Dispose();
            }

            if (confidence.IsCreated)
            {
                confidence.Dispose();
            }
        }

        /// <summary>
        /// Copies a depth image (16-bit millimeters or 32-bit float meters) and its confidence image.
        /// False for formats it does not understand, or when the two images do not fit together.
        /// </summary>
        public bool CopyFrom(XRCpuImage depthImage, XRCpuImage confidenceImage)
        {
            Ready = false;
            if (!depthImage.valid || depthImage.planeCount < 1 || !confidenceImage.valid
                || confidenceImage.planeCount < 1
                || confidenceImage.format != XRCpuImage.Format.OneComponent8
                || confidenceImage.width != depthImage.width || confidenceImage.height != depthImage.height)
            {
                return false;
            }

            var isFloat = depthImage.format == XRCpuImage.Format.DepthFloat32;
            if (!isFloat && depthImage.format != XRCpuImage.Format.DepthUint16)
            {
                return false;
            }

            Resize(depthImage.width, depthImage.height);
            var depthPlane = depthImage.GetPlane(0);
            var confidencePlane = confidenceImage.GetPlane(0);
            var depthData = depthPlane.data;
            var confidenceData = confidencePlane.data;
            for (var y = 0; y < Height; y++)
            {
                var depthRow = y * depthPlane.rowStride;
                var confidenceRow = y * confidencePlane.rowStride;
                var target = y * Width;
                for (var x = 0; x < Width; x++)
                {
                    var offset = depthRow + x * depthPlane.pixelStride;
                    depth[target + x] = isFloat
                        ? ToMillimeters(depthData.GetSubArray(offset, 4).Reinterpret<float>(1)[0])
                        : (ushort)(depthData[offset] | (depthData[offset + 1] << 8));
                    confidence[target + x] = confidenceData[confidenceRow + x * confidencePlane.pixelStride];
                }
            }

            return true;
        }

        /// <summary>For tests: a snapshot from plain arrays (millimeters and confidence, row by row).</summary>
        internal void CopyFrom(ushort[] millimeters, byte[] confidences, int width, int height)
        {
            Resize(width, height);
            depth.CopyFrom(millimeters);
            confidence.CopyFrom(confidences);
        }

        public byte ConfidenceAt(int x, int y)
        {
            return x < 0 || y < 0 || x >= Width || y >= Height ? (byte)0 : confidence[y * Width + x];
        }

        /// <summary>Depth in meters, or 0 when there is none (no measurement or too little confidence).</summary>
        public float MetersAt(int x, int y, byte minConfidence)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height)
            {
                return 0f;
            }

            var index = y * Width + x;
            return confidence[index] < minConfidence ? 0f : depth[index] * MillimetersToMeters;
        }

        /// <summary>First depth found in a small square around the pixel (raw depth has holes).</summary>
        public float MetersNear(int x, int y, int radius, byte minConfidence)
        {
            var value = MetersAt(x, y, minConfidence);
            for (var r = 1; r <= radius && value <= 0f; r++)
            {
                for (var dy = -r; dy <= r && value <= 0f; dy++)
                {
                    for (var dx = -r; dx <= r && value <= 0f; dx++)
                    {
                        value = MetersAt(x + dx, y + dy, minConfidence);
                    }
                }
            }

            return value;
        }

        /// <summary>Share (0..1) of the pixels that have a measurement with at least this confidence.</summary>
        public float ConfidentShare(byte minConfidence)
        {
            var total = Width * Height;
            if (total == 0)
            {
                return 0f;
            }

            var confident = 0;
            for (var i = 0; i < total; i++)
            {
                if (depth[i] > 0 && confidence[i] >= minConfidence)
                {
                    confident++;
                }
            }

            return (float)confident / total;
        }

        void Resize(int width, int height)
        {
            if (depth.IsCreated && Width == width && Height == height)
            {
                return;
            }

            Dispose();
            Width = width;
            Height = height;
            depth = new NativeArray<ushort>(width * height, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            confidence = new NativeArray<byte>(width * height, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }

        static ushort ToMillimeters(float meters)
        {
            return meters > 0f ? (ushort)Mathf.Min(meters * MetersToMillimeters, ushort.MaxValue) : (ushort)0;
        }
    }
}
