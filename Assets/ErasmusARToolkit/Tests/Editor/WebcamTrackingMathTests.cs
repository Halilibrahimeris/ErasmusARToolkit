using NUnit.Framework;
using UnityEngine;

namespace Erasmus.ARToolkit.Tests
{
    public class PoseConversionTests
    {
        static readonly double[,] Identity = { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };

        [Test]
        public void FacingImage_IsInFrontOfCamera_WithNormalTowardCamera_AndTopUp()
        {
            var pose = PoseConversion.OpenCvToUnityCamera(Identity, new double[] { 0.1, 0.2, 2.0 });

            // OpenCV y points down, Unity y points up.
            Assert.AreEqual(new Vector3(0.1f, -0.2f, 2f), pose.position);
            AssertDirection(new Vector3(0f, 0f, -1f), pose.rotation * Vector3.up, "image normal faces the camera");
            AssertDirection(new Vector3(0f, 1f, 0f), pose.rotation * Vector3.forward, "image top points up");
            AssertDirection(new Vector3(1f, 0f, 0f), pose.rotation * Vector3.right, "image right points right");
        }

        [Test]
        public void ImageRotatedOnScreen_RotatesAroundViewAxis()
        {
            // Object rotated +90 degrees around the OpenCV camera z axis (x -> y(down)).
            var rotated = new double[,] { { 0, -1, 0 }, { 1, 0, 0 }, { 0, 0, 1 } };
            var pose = PoseConversion.OpenCvToUnityCamera(rotated, new double[] { 0, 0, 1 });
            AssertDirection(new Vector3(0f, -1f, 0f), pose.rotation * Vector3.right, "image right now points down");
            AssertDirection(new Vector3(0f, 0f, -1f), pose.rotation * Vector3.up, "normal still faces the camera");
        }

        static void AssertDirection(Vector3 expected, Vector3 actual, string message)
        {
            Assert.Less(Vector3.Distance(expected, actual), 0.001f, $"{message}: expected {expected}, got {actual}");
        }
    }

    public class GrayscaleConverterTests
    {
        [Test]
        public void ConvertsLuminance_AndFlipsRowsToTopFirst()
        {
            // 2x2 image, Unity order: row 0 is the bottom.
            var pixels = new[]
            {
                new Color32(0, 0, 0, 255), new Color32(255, 255, 255, 255),      // bottom row
                new Color32(255, 0, 0, 255), new Color32(0, 255, 0, 255),        // top row
            };

            var gray = GrayscaleConverter.FromColor32(pixels, 2, 2, 1, true);

            Assert.AreEqual(4, gray.Length);
            Assert.AreEqual(76, gray[0]);    // red (top-left after flip)
            Assert.AreEqual(149, gray[1]);   // green
            Assert.AreEqual(0, gray[2]);     // black
            Assert.AreEqual(255, gray[3]);   // white
        }

        [Test]
        public void Downscales_ByStep()
        {
            var pixels = new Color32[8 * 4];
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32(255, 255, 255, 255);
            }

            var gray = GrayscaleConverter.FromColor32(pixels, 8, 4, 2, false);
            Assert.AreEqual(4 * 2, gray.Length);
            Assert.AreEqual(255, gray[0]);
        }
    }

    public class CameraIntrinsicsTests
    {
        [Test]
        public void Scaled_ScalesFocalAndPrincipalPoint()
        {
            var intrinsics = new CameraIntrinsics(800f, new Vector2(640f, 360f), 1280, 720).Scaled(0.5f);
            Assert.AreEqual(400f, intrinsics.Focal, 0.001f);
            Assert.AreEqual(new Vector2(320f, 180f), intrinsics.Principal);
            Assert.AreEqual(640, intrinsics.Width);
            Assert.AreEqual(360, intrinsics.Height);
        }

        [Test]
        public void FromCameraImage_UsesQuadGeometry()
        {
            // A 720 px tall image shown 1 m tall at 2 m distance: focal = 2 * 720 / 1 = 1440 px.
            var intrinsics = CameraIntrinsics.FromDisplayedImage(1280, 720, 2f, 1f);
            Assert.AreEqual(1440f, intrinsics.Focal, 0.001f);
            Assert.AreEqual(new Vector2(640f, 360f), intrinsics.Principal);
        }
    }
}
