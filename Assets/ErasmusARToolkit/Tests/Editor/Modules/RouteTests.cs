using System.Linq;
using Erasmus.ARToolkit.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Erasmus.ARToolkit.Tests
{
    public class RouteProgressTests
    {
        [Test]
        public void Mark_CountsEachStopOnce_InAnyOrder()
        {
            var progress = new RouteProgress(new[] { 1, 2, 3 });
            Assert.IsTrue(progress.Mark(3));
            Assert.IsTrue(progress.Mark(1));
            Assert.IsFalse(progress.Mark(1), "A stop found again does not count twice.");
            Assert.AreEqual(2, progress.FoundCount);
            Assert.AreEqual(2, progress.FirstMissing);
            Assert.IsFalse(progress.IsComplete);
            Assert.IsTrue(progress.Mark(2));
            Assert.IsTrue(progress.IsComplete);
        }

        [Test]
        public void Mark_UnknownStop_IsIgnored()
        {
            var progress = new RouteProgress(new[] { 1, 2, 0, -1 });
            Assert.AreEqual(2, progress.Total, "0 and negative numbers are no stops.");
            Assert.IsFalse(progress.Mark(7));
            Assert.AreEqual(0, progress.FoundCount);
        }

        [Test]
        public void EmptyRoute_IsNeverComplete()
        {
            Assert.IsFalse(new RouteProgress(new int[0]).IsComplete);
        }

        [Test]
        public void Line_ShowsProgress_AndTheWayOn()
        {
            var progress = new RouteProgress(new[] { 1, 2, 3 });
            progress.Mark(1);
            Assert.AreEqual("Route · 1 / 3 · Next: Library (up)", RouteText.Line("Route", progress,
                RouteText.Next(new RouteStop(1, "Library", RouteArrow.Up))));
            progress.Mark(2);
            progress.Mark(3);
            Assert.AreEqual("Route · 3 / 3 · complete", RouteText.Line("Route", progress, "Next: x"));
        }

        [Test]
        public void Next_UsesTheHintAndTheArrow()
        {
            Assert.AreEqual("Next: left", RouteText.Next(new RouteStop(1, "", RouteArrow.Left)));
            Assert.AreEqual("Next: Gym", RouteText.Next(new RouteStop(1, "Gym", RouteArrow.None)));
            Assert.AreEqual(string.Empty, RouteText.Next(new RouteStop(1, " ", RouteArrow.None)));
        }
    }

    public class RouteArrowMathTests
    {
        [TestCase(RouteArrow.Left, -1f, 0f, 0f)]
        [TestCase(RouteArrow.Right, 1f, 0f, 0f)]
        [TestCase(RouteArrow.Up, 0f, 0f, 1f)]
        [TestCase(RouteArrow.Down, 0f, 0f, -1f)]
        [TestCase(RouteArrow.Straight, 0f, -1f, 0f)]
        [TestCase(RouteArrow.Back, 0f, 1f, 0f)]
        public void Rotation_TurnsTheArrowTheRightWay(RouteArrow arrow, float x, float y, float z)
        {
            var pointing = RouteArrowMath.Rotation(arrow) * Vector3.forward;
            Assert.Less(Vector3.Distance(new Vector3(x, y, z), pointing), 0.0001f);
        }

        [Test]
        public void ArrowMesh_IsOneUnitLong_AndPointsAlongZ()
        {
            var mesh = RouteArrowMesh.Create();
            try
            {
                Assert.AreEqual(1f, mesh.bounds.size.z, 0.0001f);
                Assert.AreEqual(0.5f, mesh.bounds.max.z, 0.0001f);
                Assert.Greater(mesh.triangles.Length, 0);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }
    }

    public class RouteBuilderTests
    {
        ARToolkitConfig config;
        Texture2D image;

        [SetUp]
        public void SetUp()
        {
            config = ScriptableObject.CreateInstance<ARToolkitConfig>();
            image = new Texture2D(512, 512) { name = "Sign" };
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(config);
            Object.DestroyImmediate(image);
        }

        [Test]
        public void AddStop_NumbersTheStops()
        {
            RouteBuilder.AddStop(config, image, 0.21f, "Entrance", "", "Library", RouteArrow.Up, null);
            var second = RouteBuilder.AddStop(config, image, 0.21f, "Library", "", "", RouteArrow.None, null);
            Assert.AreEqual(2, second.RouteStop.Order);
            Assert.AreEqual(new[] { "Entrance", "Library" }, RouteBuilder.Stops(config).Select(s => s.ResolvedName).ToArray());
        }

        [Test]
        public void Move_SwapsTheNumbers_Remove_Renumbers()
        {
            var first = RouteBuilder.AddStop(config, image, 0.21f, "A", "", "to B", RouteArrow.Left, null);
            RouteBuilder.AddStop(config, image, 0.21f, "B", "", "to C", RouteArrow.Left, null);
            RouteBuilder.AddStop(config, image, 0.21f, "C", "", "", RouteArrow.None, null);

            RouteBuilder.Move(config, first, 1);
            Assert.AreEqual(new[] { "B", "A", "C" }, RouteBuilder.Stops(config).Select(s => s.ResolvedName).ToArray());

            RouteBuilder.Remove(config, RouteBuilder.Stops(config)[0]);
            var stops = RouteBuilder.Stops(config);
            Assert.AreEqual(new[] { "A", "C" }, stops.Select(s => s.ResolvedName).ToArray());
            Assert.AreEqual(new[] { 1, 2 }, stops.Select(s => s.RouteStop.Order).ToArray());
        }

        [Test]
        public void Validate_SameStopNumberTwice_IsAnError_MissingDirectionsAWarning()
        {
            var a = new ImageTargetItem("A", image, 0.2f, null, new InfoLabelData("A", ""), null, new RouteStop(1, "", RouteArrow.None));
            var b = new ImageTargetItem("B", image, 0.2f, null, new InfoLabelData("B", ""), null, new RouteStop(1, "", RouteArrow.None));
            var c = new ImageTargetItem("C", image, 0.2f, null, new InfoLabelData("C", ""), null, new RouteStop(2, "", RouteArrow.None));
            var issues = ConfigValidator.Validate(new PlaceableItem[0], new[] { a, b, c }, 10, Vector2.one);
            Assert.IsTrue(issues.Any(i => i.Severity == IssueSeverity.Error && i.Message.Contains("number 1")));
            Assert.IsTrue(issues.Any(i => i.Severity == IssueSeverity.Warning && i.Message.Contains("how to get")));
        }

        [Test]
        public void EnsureTracker_MakesOneTracker()
        {
            var tracker = RouteBuilder.EnsureTracker("Test Route", out var created);
            try
            {
                Assert.IsTrue(created);
                Assert.AreEqual("Test Route", tracker.RouteName);
                Assert.AreSame(tracker, RouteBuilder.EnsureTracker("Other", out var again));
                Assert.IsFalse(again);
            }
            finally
            {
                Object.DestroyImmediate(tracker.gameObject);
            }
        }
    }
}
