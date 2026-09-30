using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using static Erasmus.ARToolkit.Tests.RoomSurfaceMapTests;

namespace Erasmus.ARToolkit.Tests
{
    public class ScanPacingTests
    {
        /// <summary>Frames of the app at a steady pace, for a while.</summary>
        static float Run(ScanPacing pacing, float from, float seconds, float frameMs, float costMs)
        {
            var now = from;
            var step = frameMs / 1000f;
            while (now < from + seconds)
            {
                now += step;
                pacing.NoteFrame(step, costMs, now);
            }

            return now;
        }

        [Test]
        public void PhoneThatKeepsUp_ScansEightTimesASecond()
        {
            var pacing = new ScanPacing();

            Run(pacing, 0f, 3f, 17f, 2f);

            Assert.IsFalse(pacing.IsSlow);
            Assert.AreEqual(ScanPacing.NormalInterval, pacing.Interval);
            Assert.AreEqual(17f, pacing.FrameMs, 0.5f);
        }

        [Test]
        public void ScanThatCostsTooMuch_SlowsDown()
        {
            var pacing = new ScanPacing();

            Run(pacing, 0f, 1f, 20f, 8f);

            Assert.IsTrue(pacing.IsSlow);
            Assert.AreEqual(ScanPacing.SlowInterval, pacing.Interval);
        }

        [Test]
        public void AppThatDropsFrames_SlowsTheScanDown()
        {
            var pacing = new ScanPacing();

            Run(pacing, 0f, 2f, 50f, 2f);

            Assert.IsTrue(pacing.IsSlow);
        }

        [Test]
        public void OneLongFrame_ChangesNothing()
        {
            var pacing = new ScanPacing();
            var now = Run(pacing, 0f, 2f, 17f, 2f);

            pacing.NoteFrame(0.12f, 2f, now + 0.12f);

            Assert.IsFalse(pacing.IsSlow, "Loading something takes a frame of 120 ms now and then.");
        }

        [Test]
        public void SlowScan_SpeedsUp_WhenAllWasWellForAWhile()
        {
            var pacing = new ScanPacing();
            var now = Run(pacing, 0f, 1f, 20f, 8f);
            Assert.IsTrue(pacing.IsSlow);

            now = Run(pacing, now, ScanPacing.RecoverySeconds - 1f, 20f, 2f);
            Assert.IsTrue(pacing.IsSlow, "Not yet.");

            now = Run(pacing, now, 0.5f, 20f, 4f);
            Assert.IsTrue(pacing.IsSlow);
            now = Run(pacing, now, ScanPacing.RecoverySeconds - 1f, 20f, 2f);
            Assert.IsTrue(pacing.IsSlow, "The scan cost too much in between: the wait starts over.");

            Run(pacing, now, 1.5f, 20f, 2f);
            Assert.IsFalse(pacing.IsSlow);
        }

        [Test]
        public void WarmPhone_ScansSlowly_HotPhone_EndsTheScan()
        {
            var pacing = new ScanPacing();
            Run(pacing, 0f, 1f, 17f, 2f);

            pacing.NoteThermalStatus(1);
            Assert.IsFalse(pacing.IsWarm);
            Assert.AreEqual(ScanPacing.NormalInterval, pacing.Interval);

            pacing.NoteThermalStatus(ScanPacing.WarmStatus);
            Assert.IsTrue(pacing.IsWarm);
            Assert.IsFalse(pacing.IsHot);
            Assert.AreEqual(ScanPacing.SlowInterval, pacing.Interval);

            pacing.NoteThermalStatus(ScanPacing.HotStatus);
            Assert.IsTrue(pacing.IsHot);

            pacing.NoteThermalStatus(ThermalStatus.Unknown);
            Assert.IsFalse(pacing.IsWarm);
            Assert.IsFalse(pacing.IsHot);
            Assert.AreEqual(ScanPacing.NormalInterval, pacing.Interval);
        }

        [Test]
        public void ThermalStatus_IsNotKnown_OffThePhone()
        {
            Assert.AreEqual(ThermalStatus.Unknown, ThermalStatus.Read());
        }

        [Test]
        public void NothingNewForAWhile_LooksComplete_Once()
        {
            var pacing = new ScanPacing();
            Assert.IsFalse(pacing.LooksComplete(3f, 0f));
            Assert.IsFalse(pacing.LooksComplete(3.1f, 10f));
            Assert.IsFalse(pacing.LooksComplete(3.2f, ScanPacing.CompleteAfterSeconds - 1f));

            Assert.IsTrue(pacing.LooksComplete(3.2f, ScanPacing.CompleteAfterSeconds + 0.1f));
            Assert.IsFalse(pacing.LooksComplete(3.2f, ScanPacing.CompleteAfterSeconds + 5f), "Said once.");
        }

        [Test]
        public void SomethingNew_StartsTheWaitOver()
        {
            var pacing = new ScanPacing();
            pacing.LooksComplete(3f, 0f);
            Assert.IsFalse(pacing.LooksComplete(4f, 15f), "A square meter more.");
            Assert.IsFalse(pacing.LooksComplete(4f, 25f));

            Assert.IsTrue(pacing.LooksComplete(4.1f, 15f + ScanPacing.CompleteAfterSeconds + 0.1f));

            // The user scans on, and stops again.
            Assert.IsFalse(pacing.LooksComplete(6f, 50f));
            Assert.IsTrue(pacing.LooksComplete(6f, 50f + ScanPacing.CompleteAfterSeconds + 0.1f));
        }

        [Test]
        public void NothingScannedYet_IsNotComplete()
        {
            var pacing = new ScanPacing();
            pacing.LooksComplete(0f, 0f);

            Assert.IsFalse(pacing.LooksComplete(0.1f, 60f));
        }

        [Test]
        public void Reset_StartsOver()
        {
            var pacing = new ScanPacing();
            Run(pacing, 0f, 1f, 20f, 8f);
            pacing.NoteThermalStatus(ScanPacing.HotStatus);

            pacing.Reset();

            Assert.IsFalse(pacing.IsSlow);
            Assert.IsFalse(pacing.IsHot);
            Assert.AreEqual(ScanPacing.NormalInterval, pacing.Interval);
        }
    }

    public class DepthGridTests
    {
        // What ARCore gives on a phone that is held upright: the camera image lies on its side and is
        // wider than the screen shows. The screen shows the middle 60 % of the long side of the image.
        static readonly Matrix4x4 Upright = DisplayMatrix(0f, -1f, 0.6f, 0f, 1f, 0.2f);

        /// <summary>Row-vector convention: uv = (vx, vy, 1) * M.</summary>
        static Matrix4x4 DisplayMatrix(float xFromX, float xFromY, float yFromX, float yFromY, float offsetX, float offsetY)
        {
            var matrix = Matrix4x4.identity;
            matrix.m00 = xFromX;
            matrix.m10 = xFromY;
            matrix.m20 = offsetX;
            matrix.m01 = yFromX;
            matrix.m11 = yFromY;
            matrix.m21 = offsetY;
            return matrix;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TryGetViewport_TurnsToTextureUvRound(bool transposed)
        {
            var mapping = new DepthImageMapping(transposed, false);
            var display = transposed ? Upright.transpose : Upright;
            foreach (var viewport in new[] { new Vector2(0.2f, 0.7f), new Vector2(0f, 0f), new Vector2(1.3f, -0.2f) })
            {
                var uv = mapping.ToTextureUv(viewport, display);

                Assert.IsTrue(mapping.TryGetViewport(uv, display, out var back));

                Assert.AreEqual(viewport.x, back.x, 1e-5f);
                Assert.AreEqual(viewport.y, back.y, 1e-5f);
            }
        }

        [Test]
        public void TryGetViewport_MatrixThatCannotBeTurnedRound_IsRefused()
        {
            var flat = DisplayMatrix(1f, 0f, 2f, 0f, 0f, 0f);

            Assert.IsFalse(new DepthImageMapping(false, false).TryGetViewport(Vector2.one, flat, out _));
            Assert.IsFalse(new DepthImageMapping(false, false).TryGetImageBounds(flat, out var bounds));
            Assert.AreEqual(new Rect(0f, 0f, 1f, 1f), bounds);
        }

        [Test]
        public void ImageBounds_ReachBeyondTheScreen_WhereTheImageDoes()
        {
            var mapping = new DepthImageMapping(false, false);

            Assert.IsTrue(mapping.TryGetImageBounds(Upright, out var bounds));

            // Across the screen the image is wider than the screen; along it, it fits.
            Assert.AreEqual(-1f / 3f, bounds.xMin, 1e-4f);
            Assert.AreEqual(4f / 3f, bounds.xMax, 1e-4f);
            Assert.AreEqual(0f, bounds.yMin, 1e-4f);
            Assert.AreEqual(1f, bounds.yMax, 1e-4f);

            // Every corner of the bounds is a corner of the image.
            Assert.IsTrue(mapping.TryGetPixel(new Vector2(bounds.xMin + 0.01f, 0.5f), Upright, 160, 90, out _, out _));
            Assert.IsFalse(mapping.TryGetPixel(new Vector2(bounds.xMin - 0.01f, 0.5f), Upright, 160, 90, out _, out _));
        }

        [Test]
        public void IdentityMatrix_TheImageIsTheScreen()
        {
            Assert.IsTrue(new DepthImageMapping(false, false).TryGetImageBounds(Matrix4x4.identity, out var bounds));

            Assert.AreEqual(new Rect(0f, 0f, 1f, 1f), bounds);
        }

        [Test]
        public void Grid_OverTheScreen_IsAsBefore()
        {
            DepthScanSource.LayOutGrid(new Rect(0f, 0f, 1f, 1f), 20f / 9f, out var bounds, out var columns, out var rows);

            Assert.AreEqual(new Rect(0f, 0f, 1f, 1f), bounds);
            Assert.AreEqual(DepthScanSource.Columns, columns);
            Assert.AreEqual(DepthScanSource.MaxRows, rows);
        }

        [Test]
        public void Grid_GoesOnBesideTheScreen_WithinItsBudget()
        {
            DepthScanSource.LayOutGrid(Rect.MinMaxRect(-1f / 3f, 0f, 4f / 3f, 1f), 20f / 9f, out var bounds,
                out var columns, out var rows);

            Assert.AreEqual(-1f / 3f, bounds.xMin, 1e-4f);
            Assert.AreEqual(4f / 3f, bounds.xMax, 1e-4f);
            Assert.Greater(columns, DepthScanSource.Columns, "More of the room in one frame.");
            Assert.LessOrEqual(columns * rows, DepthScanSource.MaxSamples);
            Assert.Greater(columns * rows, DepthScanSource.MaxSamples * 0.9f);

            // The grid has the shape of the one over the screen: only the distance between the points grows.
            var across = bounds.width / columns * DepthScanSource.Columns;
            var along = bounds.height / rows * DepthScanSource.MaxRows;
            Assert.AreEqual(1f, across / along, 0.05f);
            Assert.Less(across, 1.4f, "The points lie a little further apart, not much.");
        }

        [Test]
        public void Grid_NeverReachesFarBeyondTheScreen_AndNeverShrinksIt()
        {
            DepthScanSource.LayOutGrid(Rect.MinMaxRect(-3f, 0.2f, 4f, 0.8f), 16f / 9f, out var bounds, out _, out _);

            Assert.AreEqual(-DepthScanSource.MaxBeyondScreen, bounds.xMin, 1e-5f);
            Assert.AreEqual(1f + DepthScanSource.MaxBeyondScreen, bounds.xMax, 1e-5f);
            Assert.AreEqual(0f, bounds.yMin, 1e-5f);
            Assert.AreEqual(1f, bounds.yMax, 1e-5f);
        }
    }

    public class RedrawBudgetTests
    {
        [Test]
        public void ManySurfacesChanged_AreDrawnAFewAtATime()
        {
            var map = new RoomSurfaceMap { PriorsNeedConfirmation = false };
            map.AddPrior(0f, Polygon(new Rect(-3f, -3f, 6f, 6f)));
            for (var i = 0; i < 6; i++)
            {
                map.AddPrior(0.3f + 0.15f * i, Polygon(new Rect(i, 4f, 0.8f, 0.8f)));
            }

            var builder = new RoomModelBuilder();
            builder.Rebuild(map, 0.5f, true, null);
            Assert.AreEqual(7, builder.Redrawn, "Everything, when everything is asked for.");
            Assert.AreEqual(0, builder.Waiting(map));

            foreach (var level in map.Levels)
            {
                level.MarkDirty();
            }

            var drawn = new List<int>();
            for (var redraw = 0; redraw < 8 && builder.Waiting(map) > 0; redraw++)
            {
                builder.Rebuild(map, 0.15f, false, null);
                Assert.LessOrEqual(builder.Redrawn, RoomModelBuilder.MaxRedraws, "Redraw " + redraw);
                drawn.Add(builder.Redrawn);
            }

            Assert.AreEqual(0, builder.Waiting(map), "Every surface gets its turn.");
            Assert.GreaterOrEqual(drawn.Count, 3);
            Assert.LessOrEqual(drawn.Count, 5);
        }
    }
}
