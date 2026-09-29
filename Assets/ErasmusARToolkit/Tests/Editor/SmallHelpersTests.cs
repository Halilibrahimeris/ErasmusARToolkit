using System;
using NUnit.Framework;
using UnityEngine;

namespace Erasmus.ARToolkit.Tests
{
    public class ImageTargetLookupTests
    {
        [Test]
        public void FindsItemsByResolvedName_FirstDuplicateWins()
        {
            var first = new ImageTargetItem("Poster", null, 0.2f, null);
            var duplicate = new ImageTargetItem("Poster", null, 0.3f, null);
            var other = new ImageTargetItem(" Map ", null, 0.2f, null);
            var lookup = new ImageTargetLookup(new[] { first, duplicate, other, null });

            Assert.AreEqual(2, lookup.Count);
            Assert.IsTrue(lookup.TryGet("Poster", out var found));
            Assert.AreSame(first, found);
            Assert.IsTrue(lookup.TryGet("Map", out _));
            Assert.IsFalse(lookup.TryGet("Unknown", out _));
            Assert.IsFalse(lookup.TryGet(null, out _));
        }
    }

    public class TapClassifierTests
    {
        [Test]
        public void ShortAndStill_IsTap()
        {
            Assert.IsTrue(TapClassifier.IsTap(Vector2.zero, new Vector2(5f, 5f), 0.1f, 20f, 0.4f));
        }

        [Test]
        public void MovedTooFar_OrHeldTooLong_IsNotTap()
        {
            Assert.IsFalse(TapClassifier.IsTap(Vector2.zero, new Vector2(30f, 0f), 0.1f, 20f, 0.4f));
            Assert.IsFalse(TapClassifier.IsTap(Vector2.zero, Vector2.zero, 1f, 20f, 0.4f));
        }
    }

    public class PlaneRaycastTests
    {
        [Test]
        public void RayPointingDown_HitsGround()
        {
            var ray = new Ray(new Vector3(1f, 2f, 1f), Vector3.down);
            Assert.IsTrue(PlaneRaycast.TryIntersect(ray, 0f, 10f, out var point));
            Assert.AreEqual(new Vector3(1f, 0f, 1f), point);
        }

        [Test]
        public void RayPointingUp_OrOutsideExtent_Misses()
        {
            Assert.IsFalse(PlaneRaycast.TryIntersect(new Ray(Vector3.up, Vector3.up), 0f, 10f, out _));
            Assert.IsFalse(PlaneRaycast.TryIntersect(new Ray(new Vector3(20f, 1f, 0f), Vector3.down), 0f, 10f, out _));
        }
    }

    public class OrbitMathTests
    {
        [Test]
        public void ZeroYawAndPitch_PlacesCameraBehindPivot()
        {
            var position = OrbitMath.ComputePosition(Vector3.zero, 0f, 0f, 2f);
            Assert.AreEqual(0f, position.x, 0.0001f);
            Assert.AreEqual(0f, position.y, 0.0001f);
            Assert.AreEqual(-2f, position.z, 0.0001f);
        }

        [Test]
        public void NinetyPitch_PlacesCameraAbovePivot()
        {
            var position = OrbitMath.ComputePosition(new Vector3(1f, 0f, 0f), 0f, 90f, 3f);
            Assert.AreEqual(1f, position.x, 0.0001f);
            Assert.AreEqual(3f, position.y, 0.0001f);
            Assert.AreEqual(0f, position.z, 0.0001f);
        }
    }

    public class ScreenshotPathsTests
    {
        [Test]
        public void FileName_UsesSortableTimestamp()
        {
            var name = ScreenshotPaths.CreateFileName(new DateTime(2026, 9, 28, 10, 15, 30));
            Assert.AreEqual("ARToolkit_20260928_101530.png", name);
        }

        [Test]
        public void Directory_OnMobile_UsesPersistentPath()
        {
            Assert.AreEqual("/persistent/Screenshots",
                ScreenshotPaths.GetDirectory(true, "/app/Data", "/persistent").Replace('\\', '/'));
        }

        [Test]
        public void Directory_OnDesktop_UsesFolderNextToData()
        {
            var directory = ScreenshotPaths.GetDirectory(false, "/app/Game_Data", "/persistent").Replace('\\', '/');
            Assert.AreEqual("/app/Screenshots", directory);
        }
    }

    public class GridTextureFactoryTests
    {
        [Test]
        public void LinesAreDrawnOnEdges_FillInside()
        {
            var texture = GridTextureFactory.Create(16, 2, Color.white, Color.clear);
            try
            {
                Assert.AreEqual(TextureWrapMode.Repeat, texture.wrapMode);
                Assert.AreEqual(Color.white, texture.GetPixel(0, 8));
                Assert.AreEqual(Color.white, texture.GetPixel(8, 1));
                Assert.AreEqual(Color.clear, texture.GetPixel(8, 8));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }
    }
}
