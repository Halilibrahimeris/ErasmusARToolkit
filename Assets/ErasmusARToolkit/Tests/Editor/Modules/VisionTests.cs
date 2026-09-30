using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Erasmus.ARToolkit.Tests
{
    public class VisionFilterTests
    {
        [TestCase(VisionFilterKind.Protanopia)]
        [TestCase(VisionFilterKind.Deuteranopia)]
        [TestCase(VisionFilterKind.Tritanopia)]
        [TestCase(VisionFilterKind.Achromatopsia)]
        public void ColourMatrices_KeepWhiteWhite(VisionFilterKind kind)
        {
            var settings = VisionFilter.Settings(kind);
            foreach (var row in new[] { settings.Row0, settings.Row1, settings.Row2 })
            {
                Assert.AreEqual(1f, row.x + row.y + row.z, 0.001f, "Each row adds up to 1: white stays white.");
            }
        }

        [Test]
        public void Normal_ChangesNothing()
        {
            var settings = VisionFilter.Settings(VisionFilterKind.Normal);
            Assert.AreEqual(Vector3.right, settings.Row0);
            Assert.AreEqual(Vector3.up, settings.Row1);
            Assert.AreEqual(Vector3.forward, settings.Row2);
            Assert.AreEqual(0f, settings.Blur);
            Assert.AreEqual(0f, settings.Tunnel);
            Assert.AreEqual(0f, settings.Haze);
        }

        [Test]
        public void LowVisionBlurs_TunnelNarrows_CataractHazes()
        {
            Assert.Greater(VisionFilter.Settings(VisionFilterKind.LowVision).Blur, 0f);
            Assert.Greater(VisionFilter.Settings(VisionFilterKind.TunnelVision).Tunnel, 0f);
            Assert.Greater(VisionFilter.Settings(VisionFilterKind.Cataract).Haze, 0f);
        }

        [Test]
        public void Next_GoesThroughTheOfferedFilters_AndBackToNormal()
        {
            var offered = new[] { VisionFilterKind.Deuteranopia, VisionFilterKind.LowVision };
            var kind = VisionFilterKind.Normal;
            kind = VisionFilter.Next(offered, kind);
            Assert.AreEqual(VisionFilterKind.Deuteranopia, kind);
            kind = VisionFilter.Next(offered, kind);
            Assert.AreEqual(VisionFilterKind.LowVision, kind);
            kind = VisionFilter.Next(offered, kind);
            Assert.AreEqual(VisionFilterKind.Normal, kind);
            Assert.AreEqual(VisionFilterKind.Normal, VisionFilter.Next(new VisionFilterKind[0], VisionFilterKind.Normal));
            Assert.AreEqual(VisionFilterKind.Normal, VisionFilter.Next(offered, VisionFilterKind.Cataract),
                "A filter that is not offered any more goes back to Normal.");
        }

        [Test]
        public void Entries_OfferEveryFilter_WithTheToolkitText()
        {
            var entries = VisionFilterEntry.Defaults();
            Assert.AreEqual(VisionFilter.All.Count, entries.Count);
            Assert.IsTrue(entries.All(e => e.Offered && e.Description.Length > 0));
            Assert.AreEqual("Mine", new VisionFilterEntry(VisionFilterKind.Cataract, true, " Mine ").Description);
            Assert.IsFalse(new VisionFilterEntry(VisionFilterKind.Normal).Offered, "Normal is always there, not offered.");
        }

        [Test]
        public void EyeHeights_ALowerEyeLiftsTheWorld()
        {
            Assert.AreEqual(0.3f, EyeHeights.Offset(1.5f, 1.2f), 0.0001f);
            Assert.AreEqual(0f, EyeHeights.Offset(1.5f, 1.5f), 0.0001f);
        }

        [Test]
        public void Texts_NameTheFilter()
        {
            Assert.AreEqual("Vision: Green-blind", VisionText.Button(VisionFilterKind.Deuteranopia));
            Assert.AreEqual("Eye: Wheelchair", VisionText.EyeButton(EyeHeight.Wheelchair));
        }
    }
}
