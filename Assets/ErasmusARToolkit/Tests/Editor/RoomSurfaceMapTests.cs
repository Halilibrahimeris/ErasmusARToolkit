using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Erasmus.ARToolkit.Tests
{
    public class RoomSurfaceMapTests
    {
        const float Step = 0.025f;

        /// <summary>A frame of upward-facing points on a flat area at <paramref name="height"/>.</summary>
        internal static ScanFrame PointsOn(Vector3 origin, float height, Rect area, bool upFacing = true)
        {
            var frame = new ScanFrame();
            frame.Reset(origin, true);
            for (var z = area.yMin + Step * 0.5f; z < area.yMax; z += Step)
            {
                for (var x = area.xMin + Step * 0.5f; x < area.xMax; x += Step)
                {
                    frame.Add(new Vector3(x, height, z), upFacing);
                }
            }

            return frame;
        }

        internal static List<Vector2> Polygon(Rect rect)
        {
            return new List<Vector2>
            {
                new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMax, rect.yMin),
                new Vector2(rect.xMax, rect.yMax), new Vector2(rect.xMin, rect.yMax),
            };
        }

        internal static Vector2Int Cell(float x, float z) => RoomSurfaceMap.CellOf(x, z);

        internal static void Integrate(RoomSurfaceMap map, ScanFrame frame, int times)
        {
            for (var i = 0; i < times; i++)
            {
                map.Integrate(frame);
            }
        }

        [Test]
        public void EnsureLevel_HeightsCloseTogether_ShareOneLevel()
        {
            var map = new RoomSurfaceMap();

            var floor = map.EnsureLevel(0f);
            Assert.AreEqual(floor, map.EnsureLevel(0.07f));
            Assert.AreNotEqual(floor, map.EnsureLevel(0.75f));
            Assert.AreEqual(2, map.Levels.Count);
        }

        [Test]
        public void AddPrior_WithoutDepth_MarksCellsInsideThePlane_AsSurface()
        {
            var map = new RoomSurfaceMap { PriorsNeedConfirmation = false };
            map.AddPrior(0f, Polygon(new Rect(0f, 0f, 1f, 1f)));

            var level = map.Levels[0];
            Assert.IsTrue(level.HasPrior(Cell(0.5f, 0.5f)));
            Assert.AreEqual(CellState.Occupied, level.GetState(Cell(0.5f, 0.5f)));
            Assert.AreEqual(CellState.Unknown, level.GetState(Cell(1.5f, 0.5f)));
        }

        [Test]
        public void AddPrior_WithDepth_NeedsOneFrameOfHits_ToBecomeSurface()
        {
            // A table plane of ARCore: a hull that may reach past the table.
            var map = new RoomSurfaceMap();
            map.AddPrior(0.75f, Polygon(new Rect(0f, 0f, 1f, 1f)));
            var level = map.Levels[0];

            Assert.IsTrue(level.HasPrior(Cell(0.5f, 0.5f)));
            Assert.AreEqual(CellState.Unknown, level.GetState(Cell(0.5f, 0.5f)));

            map.Integrate(PointsOn(new Vector3(0.5f, 1.5f, -0.5f), 0.75f, new Rect(0f, 0f, 0.6f, 1f)));

            Assert.AreEqual(CellState.Occupied, level.GetState(Cell(0.3f, 0.5f)), "Seen by depth.");
            Assert.AreEqual(CellState.Unknown, level.GetState(Cell(0.8f, 0.5f)), "Only ARCore says so.");
        }

        [Test]
        public void AddPrior_OverlappingPlanesOfOneFloor_EndUpOnOneLevel()
        {
            var map = new RoomSurfaceMap();
            map.AddPrior(0f, Polygon(new Rect(0f, 0f, 2f, 2f)));
            map.AddPrior(0.06f, Polygon(new Rect(1f, 1f, 2f, 2f)));

            Assert.AreEqual(1, map.Levels.Count);
            Assert.IsTrue(map.Levels[0].HasPrior(Cell(2.5f, 2.5f)));
        }

        [Test]
        public void Integrate_OneFrameIsNotEnough_RepeatedFramesMakeASurface()
        {
            var map = new RoomSurfaceMap();
            map.EnsureLevel(0f);
            var frame = PointsOn(new Vector3(0.5f, 1.4f, -1f), 0f, new Rect(0f, 0f, 1f, 1f));

            map.Integrate(frame);
            Assert.AreEqual(CellState.Unknown, map.Levels[0].GetState(Cell(0.5f, 0.5f)));

            map.Integrate(frame);
            Assert.AreEqual(CellState.Occupied, map.Levels[0].GetState(Cell(0.5f, 0.5f)));
        }

        [Test]
        public void Integrate_ManyPointsInOneCell_CountOncePerFrame()
        {
            var map = new RoomSurfaceMap();
            map.EnsureLevel(0f);
            var frame = new ScanFrame();
            frame.Reset(new Vector3(0f, 1.4f, -1f), true);
            for (var i = 0; i < 50; i++)
            {
                frame.Add(new Vector3(0.01f, 0f, 0.01f), true);
            }

            map.Integrate(frame);

            Assert.AreEqual(SurfaceEvidence.HitWeight, map.Levels[0].GetLogOdds(Cell(0.01f, 0.01f)), 1e-5f);
        }

        [Test]
        public void Integrate_PointsOnATableSide_DoNotMakeATableTop()
        {
            var map = new RoomSurfaceMap();
            map.EnsureLevel(0.75f);
            var frame = PointsOn(new Vector3(0.5f, 1.4f, -1f), 0.75f, new Rect(0f, 0f, 1f, 1f), upFacing: false);

            Integrate(map, frame, 5);

            Assert.AreNotEqual(CellState.Occupied, map.Levels[0].GetState(Cell(0.5f, 0.5f)));
        }

        [Test]
        public void Integrate_RaysEndingOnTheFloor_CarveTheTablePlaneThatReachesPastTheTable()
        {
            var map = new RoomSurfaceMap();
            map.EnsureLevel(0f);
            // ARCore's convex plane covers the table (x 0..1.6) and empty space beside it (x 1.6..2.5).
            map.AddPrior(0.75f, Polygon(new Rect(0f, 0f, 2.5f, 0.7f)));
            var table = map.EnsureLevel(0.75f);

            Integrate(map, PointsOn(new Vector3(0.8f, 1.5f, -0.5f), 0.75f, new Rect(0f, 0f, 1.6f, 0.7f)), 3);
            Integrate(map, PointsOn(new Vector3(2.15f, 1.5f, 0.35f), 0f, new Rect(1.8f, 0f, 0.7f, 0.7f)), 4);

            var level = map.Levels[table];
            Assert.AreEqual(CellState.Occupied, level.GetState(Cell(0.8f, 0.35f)));
            Assert.AreEqual(CellState.Carved, level.GetState(Cell(2.15f, 0.35f)));
        }

        [Test]
        public void Integrate_PriorSeenThroughOnce_StaysUntilSeenThroughAgain()
        {
            var map = new RoomSurfaceMap { PriorsNeedConfirmation = false };
            map.EnsureLevel(0f);
            map.AddPrior(0.75f, Polygon(new Rect(0f, 0f, 1f, 1f)));
            var table = map.EnsureLevel(0.75f);
            var floorBelow = PointsOn(new Vector3(0.5f, 1.5f, 0.5f), 0f, new Rect(0.2f, 0.2f, 0.6f, 0.6f));

            map.Integrate(floorBelow);
            Assert.AreEqual(CellState.Occupied, map.Levels[table].GetState(Cell(0.5f, 0.5f)));

            map.Integrate(floorBelow);
            Assert.AreNotEqual(CellState.Occupied, map.Levels[table].GetState(Cell(0.5f, 0.5f)));
        }

        [Test]
        public void Integrate_HeightFromArcoreThreeCentimetersOff_IsCorrectedByDepth()
        {
            var map = new RoomSurfaceMap();
            map.EnsureLevel(0.78f);

            Integrate(map, PointsOn(new Vector3(0.5f, 1.5f, -0.5f), 0.75f, new Rect(0f, 0f, 1f, 0.7f)), 2);

            Assert.AreEqual(0.75f, map.Levels[0].Height, 0.005f);
            Assert.IsTrue(map.Levels[0].IsConfirmed);
        }

        [Test]
        public void Integrate_DriftedDuplicateFloor_MovesOntoTheRealFloorAndMerges()
        {
            var map = new RoomSurfaceMap();
            map.AddPrior(0f, Polygon(new Rect(0f, 0f, 2f, 2f)));
            map.AddPrior(0.12f, Polygon(new Rect(1f, 1f, 2f, 2f)));
            Assert.AreEqual(2, map.Levels.Count);

            Integrate(map, PointsOn(new Vector3(1.5f, 1.4f, -0.5f), 0f, new Rect(0f, 0f, 3f, 3f)), 2);

            Assert.AreEqual(1, map.Levels.Count);
            Assert.AreEqual(0f, map.Levels[0].Height, 0.01f);
            Assert.IsTrue(map.Levels[0].HasPrior(Cell(2.5f, 2.5f)));
        }

        [Test]
        public void Integrate_SurfaceSeenOverSeveralFrames_AddsALevelArcoreMissed()
        {
            var map = new RoomSurfaceMap();
            map.EnsureLevel(0f);
            var chairSeat = PointsOn(new Vector3(0f, 1.4f, -0.8f), 0.45f, new Rect(-0.2f, -0.2f, 0.4f, 0.4f));

            map.Integrate(chairSeat);
            Assert.AreEqual(1, map.Levels.Count, "One frame must not create a surface.");

            Integrate(map, chairSeat, 5);
            Assert.AreEqual(2, map.Levels.Count);
            Assert.AreEqual(0.45f, map.Levels[1].Height, 0.02f);
        }

        [Test]
        public void Integrate_TheCeiling_IsNotAddedAsASurface()
        {
            var map = new RoomSurfaceMap();
            map.EnsureLevel(0f);
            var ceiling = PointsOn(new Vector3(0f, 1.4f, -0.8f), 2.7f, new Rect(-0.5f, 0f, 1f, 1f));

            Integrate(map, ceiling, 8);

            Assert.AreEqual(1, map.Levels.Count);
        }

        [Test]
        public void Raycast_HitsTheNearestShownSurface_AtItsHeight()
        {
            var map = new RoomSurfaceMap { PriorsNeedConfirmation = false };
            map.AddPrior(0f, Polygon(new Rect(-2f, -2f, 4f, 4f)));
            map.AddPrior(0.75f, Polygon(new Rect(-0.5f, -0.5f, 1f, 1f)));
            ShowAll(map);

            Assert.IsTrue(map.Raycast(new Ray(new Vector3(0f, 1.5f, 0f), Vector3.down), 10f, out var onTable));
            Assert.AreEqual(0.75f, onTable.position.y, 1e-4f);
            Assert.IsTrue(map.Raycast(new Ray(new Vector3(1.5f, 1.5f, 1.5f), Vector3.down), 10f, out var onFloor));
            Assert.AreEqual(0f, onFloor.position.y, 1e-4f);
            Assert.AreEqual(Vector3.up, onFloor.up);
            Assert.IsFalse(map.Raycast(new Ray(new Vector3(5f, 1.5f, 5f), Vector3.down), 10f, out _));
        }

        [Test]
        public void Clear_RemovesEverything()
        {
            var map = new RoomSurfaceMap();
            map.AddPrior(0f, Polygon(new Rect(0f, 0f, 1f, 1f)));
            map.Integrate(PointsOn(new Vector3(0.5f, 1.4f, -1f), 0f, new Rect(0f, 0f, 1f, 1f)));

            map.Clear();

            Assert.AreEqual(0, map.Levels.Count);
            Assert.AreEqual(0f, map.CheckedArea);
            Assert.AreEqual(0, map.FramesIntegrated);
        }

        [Test]
        public void AddPrior_ConcavePlane_FillsOnlyItsShape()
        {
            var map = new RoomSurfaceMap();
            var lShape = new List<Vector2>
            {
                new Vector2(0f, 0f), new Vector2(2f, 0f), new Vector2(2f, 1f),
                new Vector2(1f, 1f), new Vector2(1f, 2f), new Vector2(0f, 2f),
            };

            map.AddPrior(0f, lShape);

            var level = map.Levels[0];
            Assert.IsTrue(level.HasPrior(Cell(0.5f, 1.5f)));
            Assert.IsTrue(level.HasPrior(Cell(1.5f, 0.5f)));
            Assert.IsFalse(level.HasPrior(Cell(1.5f, 1.5f)));
            Assert.IsFalse(level.HasPrior(Cell(-0.1f, 0.5f)));
        }

        [Test]
        public void AddPrior_PlaneAcrossChunkAndSignBoundaries_FillsEveryCell()
        {
            var map = new RoomSurfaceMap();
            map.AddPrior(0f, Polygon(new Rect(-2.5f, -2.5f, 5f, 5f)));

            var level = map.Levels[0];
            var missing = 0;
            for (var z = -49; z < 50; z++)
            {
                for (var x = -49; x < 50; x++)
                {
                    if (!level.HasPrior(new Vector2Int(x, z)))
                    {
                        missing++;
                    }
                }
            }

            Assert.AreEqual(0, missing);
            Assert.IsFalse(level.HasPrior(new Vector2Int(-51, 0)));
            Assert.IsFalse(level.HasPrior(new Vector2Int(50, 0)));
        }

        /// <summary>Decides what is shown, as the room scan does before it draws, so raycasts see the cells.</summary>
        internal static void ShowAll(RoomSurfaceMap map)
        {
            new RoomModelBuilder().Rebuild(map, 10f, true, null);
        }

        /// <summary>
        /// As <see cref="ShowAll(RoomSurfaceMap)"/>, with the builder of the test: it remembers the boxes
        /// around obstacles from redraw to redraw, as the room scan does.
        /// </summary>
        internal static void ShowAll(RoomSurfaceMap map, RoomModelBuilder builder)
        {
            builder.Rebuild(map, 0.5f, true, null);
        }
    }
}
