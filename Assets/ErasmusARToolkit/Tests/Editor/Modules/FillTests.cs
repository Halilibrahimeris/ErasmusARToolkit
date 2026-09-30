using System.Collections.Generic;
using System.Linq;
using Erasmus.ARToolkit.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Erasmus.ARToolkit.Tests
{
    public class FillLevelTests
    {
        [Test]
        public void Advance_ReachesTheTopInTheDuration_AndStopsThere()
        {
            var level = 0f;
            for (var i = 0; i < 10; i++)
            {
                level = FillLevel.Advance(level, 2f, 10f, 1f);
            }

            Assert.AreEqual(2f, level, 0.0001f);
            Assert.AreEqual(2f, FillLevel.Advance(level, 2f, 10f, 1f), 0.0001f);
            Assert.AreEqual(0.2f, FillLevel.Advance(0f, 2f, 10f, 1f), 0.0001f);
        }

        [Test]
        public void Advance_WithoutDuration_DoesNotMove()
        {
            Assert.AreEqual(0.5f, FillLevel.Advance(0.5f, 1f, 0f, 1f), 0.0001f);
        }

        [Test]
        public void Slider_MapsToTheLevel_AndBack()
        {
            Assert.AreEqual(0.75f, FillLevel.FromFraction(0.5f, 1.5f), 0.0001f);
            Assert.AreEqual(0.5f, FillLevel.Fraction(0.75f, 1.5f), 0.0001f);
            Assert.AreEqual(1.5f, FillLevel.FromFraction(3f, 1.5f), 0.0001f);
            Assert.AreEqual(0f, FillLevel.Fraction(1f, 0f));
        }

        [Test]
        public void Text_ShowsTheLevelInCentimeters()
        {
            Assert.AreEqual("Water · 45 cm", FillText.Line("Water", 0.452f));
            Assert.AreEqual("Level · 0 cm", FillText.Line(" ", 0f));
        }
    }

    public class FillAreaTests
    {
        [Test]
        public void Rectangle_IsCenteredOnTheModule()
        {
            var area = FillArea.Rectangle(new Vector2(4f, 2f));
            Assert.AreEqual(8f, area.Area, 0.0001f);
            Assert.IsTrue(area.Contains(new Vector2(1.9f, 0.9f)));
            Assert.IsFalse(area.Contains(new Vector2(2.1f, 0f)));
            Assert.AreEqual(new Rect(-2f, -1f, 4f, 2f), area.Bounds);
            Assert.IsFalse(area.IsScanned);
        }

        [Test]
        public void FromWorld_IsSeenFromTheModule()
        {
            var root = new GameObject("Fill").transform;
            try
            {
                root.SetPositionAndRotation(new Vector3(10f, 0f, 0f), Quaternion.Euler(0f, 90f, 0f));
                var world = new List<List<Vector2>>
                {
                    new List<Vector2> { new Vector2(9f, -1f), new Vector2(11f, -1f), new Vector2(11f, 1f), new Vector2(9f, 1f) },
                };
                var area = FillArea.FromWorld(world, root);
                Assert.IsTrue(area.IsScanned);
                Assert.AreEqual(4f, area.Area, 0.001f);
                Assert.IsTrue(area.Contains(Vector2.zero));
                Assert.IsFalse(area.Contains(new Vector2(3f, 0f)));
            }
            finally
            {
                Object.DestroyImmediate(root.gameObject);
            }
        }

        [Test]
        public void Surface_CoversTheArea()
        {
            var mesh = FillSurface.Build(FillArea.Rectangle(new Vector2(3f, 2f)));
            try
            {
                Assert.Greater(mesh.triangles.Length, 0);
                Assert.AreEqual(3f, mesh.bounds.size.x, 0.001f);
                Assert.AreEqual(2f, mesh.bounds.size.z, 0.001f);
                Assert.AreEqual(0f, mesh.bounds.size.y, 0.001f);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }
    }

    public class ObjectPileTests
    {
        static readonly FillArea Area = FillArea.Rectangle(new Vector2(1f, 1f));

        [Test]
        public void Layout_FillsLayerAfterLayer()
        {
            var positions = new List<Vector3>();
            var oneLayer = ObjectPile.Layout(Area, 0.25f, 0.1f, 0.1f, null, 1, positions);
            Assert.AreEqual(16, oneLayer, "A 1 m square holds 4 x 4 objects 25 cm apart.");
            Assert.IsTrue(positions.All(p => Mathf.Approximately(p.y, 0.05f)));

            var twoAndAHalf = ObjectPile.Layout(Area, 0.25f, 0.1f, 0.25f, null, 1, positions);
            Assert.AreEqual(40, twoAndAHalf, "Two full layers and half of the third.");
            Assert.AreEqual(0, ObjectPile.Layout(Area, 0.25f, 0.1f, 0f, null, 1, positions));
        }

        [Test]
        public void Layout_KeepsOutOfWhatStandsInTheRoom()
        {
            var positions = new List<Vector3>();
            ObjectPile.Layout(Area, 0.25f, 0.1f, 0.1f, p => p.x > 0f, 1, positions);
            Assert.AreEqual(8, positions.Count);
            Assert.IsTrue(positions.All(p => p.x < 0.1f));
        }

        [Test]
        public void Layout_StopsAtTheLimit()
        {
            var positions = new List<Vector3>();
            var count = ObjectPile.Layout(FillArea.Rectangle(new Vector2(10f, 10f)), 0.1f, 0.05f, 2f, null, 1, positions);
            Assert.AreEqual(ObjectPile.MaxCount, count);
        }

        [Test]
        public void Layout_IsTheSameEveryTime()
        {
            var a = new List<Vector3>();
            var b = new List<Vector3>();
            ObjectPile.Layout(Area, 0.2f, 0.1f, 0.35f, null, 7, a);
            ObjectPile.Layout(Area, 0.2f, 0.1f, 0.35f, null, 7, b);
            CollectionAssert.AreEqual(a, b);
        }
    }

    public class FillBuilderTests
    {
        [Test]
        public void Create_MakesATapToPlaceFill()
        {
            var fill = FillBuilder.Create(FillKind.Smoke, 1.2f, 30f, Color.gray, null, new Vector2(3f, 3f), "", "Full!",
                PlacementMode.TapToPlace);
            try
            {
                Assert.AreEqual(FillKind.Smoke, fill.Kind);
                Assert.AreEqual(1.2f, fill.MaxLevel, 0.0001f);
                Assert.AreEqual(PlacementMode.TapToPlace, fill.GetComponent<ModulePlacement>().PlacementMode);
            }
            finally
            {
                Object.DestroyImmediate(fill.gameObject);
            }
        }
    }
}
