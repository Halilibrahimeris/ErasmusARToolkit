using System;
using System.Collections.Generic;
using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>Pinhole camera parameters in pixels (square pixels).</summary>
    public readonly struct CameraIntrinsics
    {
        public CameraIntrinsics(float focal, Vector2 principal, int width, int height)
        {
            Focal = focal;
            Principal = principal;
            Width = width;
            Height = height;
        }

        public float Focal { get; }
        public Vector2 Principal { get; }
        public int Width { get; }
        public int Height { get; }

        /// <summary>
        /// Intrinsics that match how an image is displayed: an image <paramref name="height"/> pixels tall,
        /// shown as a quad <paramref name="quadHeight"/> tall at <paramref name="distance"/> in front of the
        /// view camera. Content posed with these lines up exactly with the displayed image.
        /// </summary>
        public static CameraIntrinsics FromDisplayedImage(int width, int height, float distance, float quadHeight)
        {
            var focal = distance * height / Mathf.Max(0.0001f, quadHeight);
            return new CameraIntrinsics(focal, new Vector2(width * 0.5f, height * 0.5f), width, height);
        }

        public CameraIntrinsics Scaled(float factor)
        {
            return new CameraIntrinsics(Focal * factor, Principal * factor,
                Mathf.RoundToInt(Width * factor), Mathf.RoundToInt(Height * factor));
        }
    }

    /// <summary>A reference image prepared for detection (grayscale, top row first).</summary>
    public sealed class DetectorTarget
    {
        public DetectorTarget(string name, byte[] gray, int width, int height, Vector2 physicalSize)
        {
            Name = name;
            Gray = gray;
            Width = width;
            Height = height;
            PhysicalSize = physicalSize;
        }

        public string Name { get; }
        public byte[] Gray { get; }
        public int Width { get; }
        public int Height { get; }

        /// <summary>Printed size in meters (width, height).</summary>
        public Vector2 PhysicalSize { get; }
    }

    /// <summary>A reference image found in a camera frame.</summary>
    public readonly struct ImageDetection
    {
        public ImageDetection(string name, Pose cameraPose, Vector2[] corners, int inliers)
        {
            Name = name;
            CameraPose = cameraPose;
            Corners = corners;
            Inliers = inliers;
        }

        public string Name { get; }

        /// <summary>Pose in the view camera's local space. Y is the image normal, Z points to the image top.</summary>
        public Pose CameraPose { get; }

        /// <summary>Image corners in frame pixels: top-left, top-right, bottom-right, bottom-left.</summary>
        public Vector2[] Corners { get; }

        public int Inliers { get; }
    }

    /// <summary>Finds reference images in camera frames. Implemented by the optional OpenCV module.</summary>
    public interface IImageTargetDetector : IDisposable
    {
        /// <summary>Returns the names of targets that had enough detail to be used.</summary>
        IReadOnlyList<string> SetTargets(IReadOnlyList<DetectorTarget> targets);

        /// <param name="gray">Frame pixels, 8-bit grayscale, top row first.</param>
        IReadOnlyList<ImageDetection> Detect(byte[] gray, int width, int height, CameraIntrinsics intrinsics);
    }

    /// <summary>
    /// Where the optional webcam image recognition registers itself. Empty on platforms where the
    /// OpenCV module is not included (Android); image targets are then simulated on the floor.
    /// </summary>
    public static class ImageTargetDetectors
    {
        public static Func<IImageTargetDetector> Factory { get; set; }

        public static bool IsAvailable => Factory != null;

        /// <summary>Creates a detector, or returns null if none is available or its native library fails to load.</summary>
        public static IImageTargetDetector TryCreate()
        {
            if (Factory == null)
            {
                return null;
            }

            try
            {
                return Factory();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Erasmus AR Toolkit] Webcam image recognition is not available: " + exception.Message);
                return null;
            }
        }
    }
}
