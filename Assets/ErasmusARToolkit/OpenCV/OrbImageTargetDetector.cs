using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;
using UnityEngine;

namespace Erasmus.ARToolkit.OpenCV
{
    /// <summary>
    /// Finds reference images in camera frames with OpenCV:
    /// ORB features → ratio-tested matches → RANSAC homography → planar pose (solvePnP, IPPE).
    /// Thread-safe for one Detect call at a time (the toolkit never runs two at once).
    /// </summary>
    public sealed class OrbImageTargetDetector : IImageTargetDetector
    {
        const int FrameFeatures = 1500;
        const int TargetFeatures = 1000;
        const int MinTargetKeyPoints = 30;
        const float RatioTest = 0.75f;
        const int MinGoodMatches = 15;
        const int MinInliers = 12;
        const double RansacThresholdPixels = 4.0;
        const double MinAreaFraction = 0.002;

        readonly ORB frameOrb = ORB.Create(FrameFeatures);
        readonly ORB targetOrb = ORB.Create(TargetFeatures);
        readonly BFMatcher matcher = new BFMatcher(NormTypes.Hamming);
        readonly List<PreparedTarget> targets = new List<PreparedTarget>();
        bool disposed;

        public IReadOnlyList<string> SetTargets(IReadOnlyList<DetectorTarget> newTargets)
        {
            ReleaseTargets();
            foreach (var target in newTargets)
            {
                var prepared = Prepare(target);
                if (prepared != null)
                {
                    targets.Add(prepared);
                }
                else
                {
                    Debug.LogWarning($"[Erasmus AR Toolkit] Image '{target.Name}' has too little detail for webcam recognition.");
                }
            }

            return targets.Select(t => t.Name).ToList();
        }

        public IReadOnlyList<ImageDetection> Detect(byte[] gray, int width, int height, CameraIntrinsics intrinsics)
        {
            var results = new List<ImageDetection>();
            if (disposed || targets.Count == 0 || gray == null || gray.Length < width * height)
            {
                return results;
            }

            using (var frame = Mat.FromPixelData(height, width, MatType.CV_8UC1, gray))
            using (var frameDescriptors = new Mat())
            {
                frameOrb.DetectAndCompute(frame, null, out var frameKeyPoints, frameDescriptors);
                if (frameDescriptors.Empty() || frameKeyPoints.Length < MinGoodMatches)
                {
                    return results;
                }

                foreach (var target in targets)
                {
                    if (TryLocate(target, frameKeyPoints, frameDescriptors, width, height, intrinsics, out var detection))
                    {
                        results.Add(detection);
                    }
                }
            }

            return results;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            ReleaseTargets();
            frameOrb.Dispose();
            targetOrb.Dispose();
            matcher.Dispose();
        }

        PreparedTarget Prepare(DetectorTarget target)
        {
            if (target.Gray == null || target.Width <= 0 || target.Height <= 0)
            {
                return null;
            }

            using (var image = Mat.FromPixelData(target.Height, target.Width, MatType.CV_8UC1, target.Gray))
            {
                var descriptors = new Mat();
                targetOrb.DetectAndCompute(image, null, out var keyPoints, descriptors);
                if (keyPoints.Length < MinTargetKeyPoints || descriptors.Empty())
                {
                    descriptors.Dispose();
                    return null;
                }

                return new PreparedTarget(target.Name, keyPoints, descriptors, target.Width, target.Height,
                    target.PhysicalSize);
            }
        }

        bool TryLocate(PreparedTarget target, KeyPoint[] frameKeyPoints, Mat frameDescriptors, int width, int height,
            CameraIntrinsics intrinsics, out ImageDetection detection)
        {
            detection = default;
            var knn = matcher.KnnMatch(target.Descriptors, frameDescriptors, 2);
            var good = knn.Where(m => m.Length == 2 && m[0].Distance < RatioTest * m[1].Distance)
                .Select(m => m[0])
                .ToList();
            if (good.Count < MinGoodMatches)
            {
                return false;
            }

            var source = good.Select(m => new Point2d(target.KeyPoints[m.QueryIdx].Pt.X, target.KeyPoints[m.QueryIdx].Pt.Y));
            var destination = good.Select(m => new Point2d(frameKeyPoints[m.TrainIdx].Pt.X, frameKeyPoints[m.TrainIdx].Pt.Y));
            using (var mask = new Mat())
            using (var homography = Cv2.FindHomography(source, destination, HomographyMethods.Ransac,
                       RansacThresholdPixels, mask))
            {
                if (homography.Empty() || Cv2.CountNonZero(mask) < MinInliers)
                {
                    return false;
                }

                var imageCorners = new[]
                {
                    new Point2f(0, 0), new Point2f(target.Width, 0),
                    new Point2f(target.Width, target.Height), new Point2f(0, target.Height),
                };
                var frameCorners = Cv2.PerspectiveTransform(imageCorners, homography);
                if (!IsPlausibleQuad(frameCorners, width, height))
                {
                    return false;
                }

                var pose = EstimatePose(frameCorners, target.PhysicalSize, intrinsics);
                var corners = frameCorners.Select(p => new Vector2(p.X, p.Y)).ToArray();
                detection = new ImageDetection(target.Name, pose, corners, Cv2.CountNonZero(mask));
                return true;
            }
        }

        static bool IsPlausibleQuad(Point2f[] corners, int width, int height)
        {
            if (!Cv2.IsContourConvex(corners))
            {
                return false;
            }

            var area = Math.Abs(Cv2.ContourArea(corners));
            return area >= MinAreaFraction * width * height;
        }

        /// <summary>Pose of the image in the camera, from its four corners and printed size.</summary>
        static Pose EstimatePose(Point2f[] frameCorners, Vector2 physicalSize, CameraIntrinsics intrinsics)
        {
            var halfWidth = physicalSize.x * 0.5f;
            var halfHeight = physicalSize.y * 0.5f;
            var objectPoints = new[]
            {
                new Point3f(-halfWidth, -halfHeight, 0f), new Point3f(halfWidth, -halfHeight, 0f),
                new Point3f(halfWidth, halfHeight, 0f), new Point3f(-halfWidth, halfHeight, 0f),
            };
            var cameraMatrix = new double[,]
            {
                { intrinsics.Focal, 0, intrinsics.Principal.x },
                { 0, intrinsics.Focal, intrinsics.Principal.y },
                { 0, 0, 1 },
            };

            var rotationVector = new double[3];
            var translation = new double[3];
            Cv2.SolvePnP(objectPoints, frameCorners, cameraMatrix, new double[4], ref rotationVector, ref translation,
                false, SolvePnPMethod.IPPE);
            Cv2.Rodrigues(rotationVector, out var rotation, out _);
            return PoseConversion.OpenCvToUnityCamera(rotation, translation);
        }

        void ReleaseTargets()
        {
            foreach (var target in targets)
            {
                target.Descriptors.Dispose();
            }

            targets.Clear();
        }

        sealed class PreparedTarget
        {
            public PreparedTarget(string name, KeyPoint[] keyPoints, Mat descriptors, int width, int height,
                Vector2 physicalSize)
            {
                Name = name;
                KeyPoints = keyPoints;
                Descriptors = descriptors;
                Width = width;
                Height = height;
                PhysicalSize = physicalSize;
            }

            public string Name { get; }
            public KeyPoint[] KeyPoints { get; }
            public Mat Descriptors { get; }
            public int Width { get; }
            public int Height { get; }
            public Vector2 PhysicalSize { get; }
        }
    }

    /// <summary>Makes the OpenCV detector available to the toolkit when this module is included.</summary>
    static class OpenCvDetectorRegistration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register()
        {
            ImageTargetDetectors.Factory = () => new OrbImageTargetDetector();
        }
    }
}
