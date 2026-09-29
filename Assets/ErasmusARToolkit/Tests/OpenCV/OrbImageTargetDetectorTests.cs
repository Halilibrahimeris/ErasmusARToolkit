using System.Linq;
using System.Runtime.InteropServices;
using Erasmus.ARToolkit.OpenCV;
using NUnit.Framework;
using OpenCvSharp;
using UnityEditor;
using UnityEngine;

namespace Erasmus.ARToolkit.Tests
{
    /// <summary>
    /// Real OpenCV tests: the sample marker is projected into a synthetic camera frame with a known
    /// pose, then the detector must find it and recover that pose.
    /// </summary>
    public class OrbImageTargetDetectorTests
    {
        const string MarkerPath = "Assets/ErasmusARToolkit/Samples/Images/SampleMarker.png";
        const int FrameWidth = 640;
        const int FrameHeight = 480;
        static readonly Vector2 MarkerSize = new Vector2(0.2f, 0.2f);
        static readonly CameraIntrinsics Intrinsics = new CameraIntrinsics(600f, new Vector2(320f, 240f), FrameWidth, FrameHeight);

        byte[] markerGray;
        int markerWidth;
        int markerHeight;

        [OneTimeSetUp]
        public void LoadMarker()
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(MarkerPath);
            Assert.IsNotNull(texture, "Sample marker missing; run Generate Demo Scene.");
            markerGray = TexturePixelReader.ReadGray(texture, 640, out markerWidth, out markerHeight);
        }

        [Test]
        public void NativeLibrary_Loads()
        {
            StringAssert.StartsWith("4.", Cv2.GetVersionString());
        }

        [Test]
        public void FindsTiltedMarker_AndRecoversItsPose()
        {
            var rotation = RotationX(25f);
            var translation = new[] { 0.03, -0.02, 0.45 };
            var frame = RenderMarker(rotation, translation);

            using (var detector = new OrbImageTargetDetector())
            {
                var usable = detector.SetTargets(new[] { Target() });
                CollectionAssert.Contains(usable, "SampleMarker");

                var detections = detector.Detect(frame, FrameWidth, FrameHeight, Intrinsics);
                Assert.AreEqual(1, detections.Count, "The marker should be found once.");

                var expected = PoseConversion.OpenCvToUnityCamera(rotation, translation);
                var actual = detections[0].CameraPose;
                Assert.Less(Vector3.Distance(expected.position, actual.position), 0.02f,
                    $"Position: expected {expected.position}, got {actual.position}");
                Assert.Less(Quaternion.Angle(expected.rotation, actual.rotation), 4f,
                    $"Rotation off by {Quaternion.Angle(expected.rotation, actual.rotation):0.0} degrees");
            }
        }

        [Test]
        public void EmptyFrame_FindsNothing()
        {
            var noise = new byte[FrameWidth * FrameHeight];
            var random = new System.Random(7);
            random.NextBytes(noise);

            using (var detector = new OrbImageTargetDetector())
            {
                detector.SetTargets(new[] { Target() });
                Assert.IsEmpty(detector.Detect(noise, FrameWidth, FrameHeight, Intrinsics));
            }
        }

        [Test]
        public void FlatImage_IsRejectedAsTarget()
        {
            var flat = Enumerable.Repeat((byte)128, 200 * 200).ToArray();
            using (var detector = new OrbImageTargetDetector())
            {
                var usable = detector.SetTargets(new[] { new DetectorTarget("Flat", flat, 200, 200, MarkerSize) });
                Assert.IsEmpty(usable);
            }
        }

        DetectorTarget Target()
        {
            return new DetectorTarget("SampleMarker", markerGray, markerWidth, markerHeight, MarkerSize);
        }

        /// <summary>Projects the marker with a pinhole camera and returns the grayscale frame (top row first).</summary>
        byte[] RenderMarker(double[,] rotation, double[] translation)
        {
            var half = MarkerSize * 0.5f;
            var objectCorners = new[]
            {
                new Vector3(-half.x, -half.y, 0f), new Vector3(half.x, -half.y, 0f),
                new Vector3(half.x, half.y, 0f), new Vector3(-half.x, half.y, 0f),
            };
            var projected = objectCorners.Select(corner => Project(corner, rotation, translation)).ToArray();
            var imageCorners = new[]
            {
                new Point2f(0, 0), new Point2f(markerWidth, 0),
                new Point2f(markerWidth, markerHeight), new Point2f(0, markerHeight),
            };

            using (var marker = Mat.FromPixelData(markerHeight, markerWidth, MatType.CV_8UC1, markerGray))
            using (var homography = Cv2.GetPerspectiveTransform(imageCorners, projected))
            using (var frame = new Mat())
            {
                Cv2.WarpPerspective(marker, frame, homography, new Size(FrameWidth, FrameHeight),
                    InterpolationFlags.Linear, BorderTypes.Constant, new Scalar(90));
                var bytes = new byte[FrameWidth * FrameHeight];
                Marshal.Copy(frame.Data, bytes, 0, bytes.Length);
                return bytes;
            }
        }

        static Point2f Project(Vector3 point, double[,] r, double[] t)
        {
            var x = r[0, 0] * point.x + r[0, 1] * point.y + r[0, 2] * point.z + t[0];
            var y = r[1, 0] * point.x + r[1, 1] * point.y + r[1, 2] * point.z + t[1];
            var z = r[2, 0] * point.x + r[2, 1] * point.y + r[2, 2] * point.z + t[2];
            return new Point2f(
                (float)(Intrinsics.Focal * x / z + Intrinsics.Principal.x),
                (float)(Intrinsics.Focal * y / z + Intrinsics.Principal.y));
        }

        static double[,] RotationX(float degrees)
        {
            var radians = degrees * Mathf.Deg2Rad;
            var cos = Mathf.Cos(radians);
            var sin = Mathf.Sin(radians);
            return new double[,] { { 1, 0, 0 }, { 0, cos, -sin }, { 0, sin, cos } };
        }
    }
}
