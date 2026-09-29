using NUnit.Framework;
using UnityEngine;

namespace Erasmus.ARToolkit.Tests
{
    public class CameraDeviceSelectorTests
    {
        static readonly CameraDeviceInfo[] Devices =
        {
            new CameraDeviceInfo("Front Camera", true),
            new CameraDeviceInfo("Back Camera", false),
            new CameraDeviceInfo("USB Webcam", false),
        };

        [Test]
        public void NoDevices_ReturnsMinusOne()
        {
            Assert.AreEqual(-1, CameraDeviceSelector.Pick(new CameraDeviceInfo[0], null, true));
            Assert.AreEqual(-1, CameraDeviceSelector.Pick(null, null, false));
        }

        [Test]
        public void PreferredName_Wins_CaseInsensitive()
        {
            Assert.AreEqual(2, CameraDeviceSelector.Pick(Devices, "usb webcam", true));
        }

        [Test]
        public void UnknownPreferredName_FallsBackToDefaultRule()
        {
            Assert.AreEqual(0, CameraDeviceSelector.Pick(Devices, "Missing", false));
        }

        [Test]
        public void Mobile_PrefersBackCamera_DesktopTakesFirst()
        {
            Assert.AreEqual(1, CameraDeviceSelector.Pick(Devices, null, true));
            Assert.AreEqual(0, CameraDeviceSelector.Pick(Devices, "", false));
        }

        [Test]
        public void Mobile_WithOnlyFrontCamera_UsesIt()
        {
            var frontOnly = new[] { new CameraDeviceInfo("Selfie", true) };
            Assert.AreEqual(0, CameraDeviceSelector.Pick(frontOnly, null, true));
        }

        [TestCase(0, 3, 1)]
        [TestCase(2, 3, -1)]
        [TestCase(-1, 3, 0)]
        [TestCase(0, 1, -1)]
        [TestCase(-1, 0, -1)]
        public void Next_CyclesThroughDevicesThenOff(int current, int count, int expected)
        {
            Assert.AreEqual(expected, CameraDeviceSelector.Next(current, count));
        }
    }

    public class CameraFeedLayoutTests
    {
        [Test]
        public void Landscape16x9_OnSquareView_CoversByHeight()
        {
            var layout = CameraFeedLayout.Compute(10f, 1f, 1600, 900, 0, false);
            Assert.AreEqual(0f, layout.RotationDegrees);
            Assert.AreEqual(10f * 16f / 9f, layout.Scale.x, 0.001f);
            Assert.AreEqual(10f, layout.Scale.y, 0.001f);
        }

        [Test]
        public void Rotated90_SwapsAspect_AndQuadAxes()
        {
            // A 1600x900 sensor rotated 90 degrees looks like a 900x1600 portrait image.
            var layout = CameraFeedLayout.Compute(16f, 9f / 16f, 1600, 900, 90, false);
            Assert.AreEqual(-90f, layout.RotationDegrees);
            // Screen is 9 x 16 units; the portrait image fills it exactly. Quad X becomes screen Y.
            Assert.AreEqual(16f, layout.Scale.x, 0.001f);
            Assert.AreEqual(9f, layout.Scale.y, 0.001f);
        }

        [Test]
        public void VerticallyMirrored_FlipsTheTexture_NotTheQuad()
        {
            // A negative scale would turn the quad's back to the camera and it would be culled.
            var layout = CameraFeedLayout.Compute(10f, 1f, 1000, 1000, 0, true);
            Assert.IsTrue(layout.FlipVertical);
            Assert.AreEqual(10f, layout.Scale.y, 0.001f);
            Assert.Greater(layout.Scale.x, 0f);
        }

        [Test]
        public void NotMirrored_DoesNotFlip()
        {
            Assert.IsFalse(CameraFeedLayout.Compute(10f, 1f, 1000, 1000, 0, false).FlipVertical);
        }

        [Test]
        public void InvalidTexture_ReturnsZeroScale()
        {
            Assert.AreEqual(Vector3.zero, CameraFeedLayout.Compute(10f, 1f, 0, 0, 0, false).Scale);
        }
    }
}
