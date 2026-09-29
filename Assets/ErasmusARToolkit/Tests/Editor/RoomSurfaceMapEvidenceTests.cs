using NUnit.Framework;
using UnityEngine;
using static Erasmus.ARToolkit.Tests.RoomSurfaceMapTests;

namespace Erasmus.ARToolkit.Tests
{
    /// <summary>
    /// How the room scan weighs what it sees: confirmed surfaces against frames that look through them,
    /// points below the floor, frames that count less, and points ARCore is not sure of.
    /// </summary>
    public class RoomSurfaceMapEvidenceTests
    {
        const float SparseStep = 0.1f;

        static readonly Rect WholeFloor = new Rect(-2f, -2f, 4f, 4f);
        static readonly Rect SeenFloor = new Rect(-1f, -0.5f, 2f, 2f);
        static readonly Vector3 Camera = new Vector3(0f, 1.4f, -1f);

        /// <summary>A frame of tracking points: few, and without normals.</summary>
        static ScanFrame SparsePointsOn(Vector3 origin, float height, Rect area)
        {
            var frame = new ScanFrame();
            frame.Reset(origin, false);
            for (var z = area.yMin + SparseStep * 0.5f; z < area.yMax; z += SparseStep)
            {
                for (var x = area.xMin + SparseStep * 0.5f; x < area.xMax; x += SparseStep)
                {
                    frame.Add(new Vector3(x, height, z), false);
                }
            }

            return frame;
        }

        static ScanFrame WithConfidence(ScanFrame source, byte confidence)
        {
            var frame = new ScanFrame();
            frame.Reset(source.Origin, source.HasNormals);
            for (var i = 0; i < source.Count; i++)
            {
                frame.Add(source.PointAt(i), source.IsUpFacing(i), confidence);
            }

            return frame;
        }

        static RoomSurfaceMap MapWithATrustedFloor()
        {
            var map = new RoomSurfaceMap();
            map.AddPrior(0f, Polygon(WholeFloor));
            Integrate(map, PointsOn(Camera, 0f, SeenFloor), FloorModel.TrustFrames + 2);
            Assert.AreEqual(FloorState.Trusted, map.FloorState);
            return map;
        }

        [Test]
        public void Integrate_ConfirmedPrior_IsHiddenAfterThreeSeeThroughFrames()
        {
            var map = new RoomSurfaceMap();
            map.EnsureLevel(0f);
            map.AddPrior(0.75f, Polygon(new Rect(0f, 0f, 1f, 1f)));
            var table = map.Levels[map.EnsureLevel(0.75f)];
            Integrate(map, PointsOn(new Vector3(0.5f, 1.5f, -0.5f), 0.75f, new Rect(0f, 0f, 1f, 1f)), 2);
            Assert.AreEqual(CellState.Occupied, table.GetState(Cell(0.5f, 0.5f)));

            var floorBelow = PointsOn(new Vector3(0.5f, 1.5f, 0.5f), 0f, new Rect(0.2f, 0.2f, 0.6f, 0.6f));
            Integrate(map, floorBelow, 2);
            Assert.AreEqual(CellState.Occupied, table.GetState(Cell(0.5f, 0.5f)),
                "Two frames of doubt do not remove what depth confirmed.");

            map.Integrate(floorBelow);
            Assert.AreEqual(CellState.Carved, table.GetState(Cell(0.5f, 0.5f)));
        }

        [Test]
        public void Integrate_ConfirmedSeatSeenThroughAfterChairMoved_IsCarved()
        {
            var map = new RoomSurfaceMap();
            map.EnsureLevel(0f);
            var camera = new Vector3(0f, 1.4f, -0.8f);
            Integrate(map, PointsOn(camera, 0.45f, new Rect(-0.2f, -0.2f, 0.4f, 0.4f)), 8);
            Assert.AreEqual(2, map.Levels.Count);
            var seat = map.Levels[1];
            Assert.AreEqual(CellState.Occupied, seat.GetState(Cell(0.01f, 0.01f)));

            // The chair is pushed away: the camera sees the floor where the seat was.
            Integrate(map, PointsOn(camera, 0f, new Rect(-0.4f, 0f, 0.8f, 0.8f)), 10);

            Assert.AreEqual(CellState.Carved, seat.GetState(Cell(0.01f, 0.01f)));
        }

        [Test]
        public void Integrate_FloorSeenPastConfirmedDeskEdge_CarvesTheOverhang()
        {
            var map = new RoomSurfaceMap();
            map.EnsureLevel(0f);
            var hull = new Rect(0f, 0f, 2.5f, 0.7f);
            map.AddPrior(0.75f, Polygon(hull));
            var desk = map.Levels[map.EnsureLevel(0.75f)];

            // Two frames taken while the phone moved: the whole hull looked like desk.
            Integrate(map, PointsOn(new Vector3(1.2f, 1.6f, -0.6f), 0.75f, hull), 2);
            Assert.AreEqual(CellState.Occupied, desk.GetState(Cell(2.15f, 0.35f)));

            Integrate(map, PointsOn(new Vector3(0.8f, 1.5f, -0.5f), 0.75f, new Rect(0f, 0f, 1.6f, 0.7f)), 3);
            Integrate(map, PointsOn(new Vector3(2.15f, 1.5f, 0.35f), 0f, new Rect(1.8f, 0f, 0.7f, 0.7f)), 6);

            Assert.AreEqual(CellState.Occupied, desk.GetState(Cell(0.8f, 0.35f)));
            Assert.AreEqual(CellState.Carved, desk.GetState(Cell(2.15f, 0.35f)));
        }

        [Test]
        public void Integrate_LevelsOutOfView_KeepTheirHeights()
        {
            var map = new RoomSurfaceMap();
            var floorFrame = PointsOn(new Vector3(0.5f, 1.5f, 2.5f), 0f, new Rect(0f, 2f, 1f, 1f));
            var camera = new Vector3(0.5f, 1.5f, -0.5f);
            map.EnsureLevel(0f);
            Integrate(map, floorFrame, 3);
            var step = map.Levels[map.EnsureLevel(0.10f)];
            Integrate(map, PointsOn(camera, 0.11f, new Rect(0f, 0f, 1f, 0.7f)), 3);
            var desk = map.Levels[map.EnsureLevel(0.75f)];
            Integrate(map, PointsOn(new Vector3(2.5f, 1.5f, -0.5f), 0.76f, new Rect(2f, 0f, 1f, 0.7f)), 3);
            Assert.AreEqual(0.11f, step.Height, 0.005f);
            Assert.AreEqual(0.76f, desk.Height, 0.005f);

            Integrate(map, floorFrame, 30);
            Assert.AreEqual(0.11f, step.Height, 0.005f, "The floor in view must not take the step along.");
            Assert.AreEqual(0.76f, desk.Height, 0.005f);

            Integrate(map, floorFrame, 200);
            Assert.AreEqual(3, map.Levels.Count);
            Assert.AreEqual(0.11f, step.Height, 0.005f);
            Assert.AreEqual(0.76f, desk.Height, 0.005f);
            Assert.AreEqual(0f, map.Levels[0].Height, 0.005f);
        }

        [Test]
        public void Integrate_FrameOfHalfWeight_CountsHalf()
        {
            var map = new RoomSurfaceMap();
            map.EnsureLevel(0f);
            var frame = PointsOn(Camera, 0f, new Rect(0f, 0f, 1f, 1f));
            frame.Weight = 0.5f;

            map.Integrate(frame);

            Assert.AreEqual(SurfaceEvidence.HitWeight * 0.5f, map.Levels[0].GetLogOdds(Cell(0.5f, 0.5f)), 1e-5f);
        }

        [Test]
        public void Integrate_FrameThatMayNotCarve_ConfirmsButRemovesNothing()
        {
            var map = new RoomSurfaceMap { PriorsNeedConfirmation = false };
            map.EnsureLevel(0f);
            map.AddPrior(0.75f, Polygon(new Rect(0f, 0f, 1f, 1f)));
            var table = map.Levels[map.EnsureLevel(0.75f)];
            var floorBelow = PointsOn(new Vector3(0.5f, 1.5f, 0.5f), 0f, new Rect(0.2f, 0.2f, 0.6f, 0.6f));
            floorBelow.AllowCarving = false;

            Integrate(map, floorBelow, 6);

            Assert.AreEqual(CellState.Occupied, table.GetState(Cell(0.5f, 0.5f)));
            Assert.AreEqual(CellState.Occupied, map.Levels[0].GetState(Cell(0.5f, 0.5f)));
        }

        [Test]
        public void Integrate_PointsArcoreIsNotSureOf_ConfirmNothing()
        {
            var map = new RoomSurfaceMap();
            map.EnsureLevel(0f);
            var unsure = WithConfidence(PointsOn(Camera, 0f, new Rect(0f, 0f, 1f, 1f)),
                (byte)(RoomSurfaceMap.MinConfidence - 1));

            Integrate(map, unsure, 6);

            Assert.AreEqual(0f, map.Levels[0].GetLogOdds(Cell(0.5f, 0.5f)));
            Assert.IsFalse(map.Levels[0].IsConfirmed);
        }

        [Test]
        public void Integrate_PointsOfMediumConfidence_ConfirmButDoNotCarve()
        {
            var map = new RoomSurfaceMap { PriorsNeedConfirmation = false };
            map.EnsureLevel(0f);
            map.AddPrior(0.75f, Polygon(new Rect(0f, 0f, 1f, 1f)));
            var table = map.Levels[map.EnsureLevel(0.75f)];
            var floorBelow = WithConfidence(
                PointsOn(new Vector3(0.5f, 1.5f, 0.5f), 0f, new Rect(0.2f, 0.2f, 0.6f, 0.6f)),
                (byte)(RoomSurfaceMap.CarveConfidence - 1));

            Integrate(map, floorBelow, 6);

            Assert.AreEqual(CellState.Occupied, table.GetState(Cell(0.5f, 0.5f)), "Removing needs more confidence.");
            Assert.AreEqual(CellState.Occupied, map.Levels[0].GetState(Cell(0.5f, 0.5f)));
        }

        [Test]
        public void Integrate_PointsSeenThroughATrustedFloor_MakeNoHoleAndNoLevel()
        {
            var map = MapWithATrustedFloor();

            // What a shiny floor mirrors: depth says the underside of a desk lies 40 cm below the floor.
            Integrate(map, PointsOn(Camera, -0.4f, SeenFloor), 12);

            Assert.AreEqual(FloorState.Trusted, map.FloorState);
            Assert.AreEqual(1, map.Levels.Count, "A reflection is not a surface.");
            Assert.Greater(map.Reflections, 0);
            Assert.AreEqual(0f, map.Levels[0].Height, 0.005f);
            for (var z = -0.4f; z < 0.9f; z += 0.1f)
            {
                for (var x = -0.7f; x < 0.7f; x += 0.1f)
                {
                    Assert.AreEqual(CellState.Occupied, map.Levels[0].GetState(Cell(x, z)), $"Floor at {x}, {z}");
                }
            }
        }

        [Test]
        public void Integrate_PointsFarBelowTheCamera_AreIgnored()
        {
            var map = new RoomSurfaceMap();
            map.AddPrior(0f, Polygon(WholeFloor));

            // The mirrored ceiling: 2.6 m "below" a floor the camera is 1.4 m above.
            Integrate(map, PointsOn(Camera, -2.6f, new Rect(-0.5f, 0f, 1f, 1f)), 10);

            Assert.AreEqual(1, map.Levels.Count);
            Assert.Greater(map.Implausible, 0);
            Assert.AreEqual(CellState.Occupied, map.Levels[0].GetState(Cell(0f, 0.2f)), "Nothing was carved.");
        }

        [Test]
        public void Integrate_FloorCellsCarvedBeforeTheFloorWasKnown_ComeBackWithTrust()
        {
            var map = new RoomSurfaceMap();
            map.AddPrior(0f, Polygon(WholeFloor));
            var patch = new Rect(-0.2f, 0.4f, 0.4f, 0.4f);
            var throughTheFloor = PointsOn(Camera, -0.4f, patch);

            // Rays from the camera to the mirrored patch cross the floor around (0, 0.24).
            var crossing = Cell(0.01f, 0.24f);
            Integrate(map, throughTheFloor, 6);
            Assert.AreEqual(CellState.Carved, map.Levels[0].GetState(crossing),
                "Until the floor is known, a ray that ends below it looks like a hole.");

            Integrate(map, PointsOn(Camera, 0f, new Rect(-1f, 1f, 2f, 1f)), FloorModel.TrustFrames);

            Assert.AreEqual(FloorState.Trusted, map.FloorState);
            Assert.AreEqual(CellState.Occupied, map.Levels[0].GetState(crossing));
            foreach (var level in map.Levels)
            {
                Assert.IsTrue(level.IsFloor || !level.IsUsable, $"The level at {level.Height} is below the floor.");
            }

            ShowAll(map);
            Assert.IsTrue(map.Raycast(new Ray(new Vector3(0.01f, 1f, 0.24f), Vector3.down), 5f, out var pose));
            Assert.AreEqual(0f, pose.position.y, 0.01f);
            Assert.AreEqual(0f, map.FloorHeight(0.5f).Value, 0.01f);
        }

        [Test]
        public void Integrate_SmallSurfaceSeenThroughTheFloor_IsHiddenAsAReflection()
        {
            var map = new RoomSurfaceMap();
            map.AddPrior(0f, Polygon(WholeFloor));
            var floorFrame = PointsOn(Camera, 0f, SeenFloor);
            var mirrored = PointsOn(Camera, -0.4f, new Rect(-0.2f, 0.4f, 0.4f, 0.4f));

            // The floor and its reflection take turns, as when a share of the pixels shows the reflection.
            for (var i = 0; i < 8; i++)
            {
                map.Integrate(floorFrame);
                map.Integrate(mirrored);
            }

            Assert.AreEqual(FloorState.Trusted, map.FloorState);
            Assert.AreEqual(0f, map.Floor.Height, 0.005f);
            ShowAll(map);
            foreach (var level in map.Levels)
            {
                if (!level.IsFloor)
                {
                    Assert.IsFalse(level.IsUsable, $"The level at {level.Height} is a reflection.");
                    Assert.AreEqual(0f, level.ShownArea);
                }
            }

            Assert.IsTrue(map.Raycast(new Ray(new Vector3(0f, 1f, 0.3f), Vector3.down), 5f, out var pose));
            Assert.AreEqual(0f, pose.position.y, 0.01f);
        }

        [Test]
        public void AddPrior_PlaneAFewCentimetersOffTheFloor_JoinsTheFloor()
        {
            var map = MapWithATrustedFloor();

            map.AddPrior(0.12f, Polygon(new Rect(2f, 2f, 1f, 1f)));
            map.AddPrior(-0.1f, Polygon(new Rect(-3f, -3f, 1f, 1f)));

            Assert.AreEqual(1, map.Levels.Count);
            Assert.IsTrue(map.Levels[0].HasPrior(Cell(2.5f, 2.5f)));
            Assert.IsTrue(map.Levels[0].HasPrior(Cell(-2.5f, -2.5f)));
            Assert.AreEqual(0f, map.Levels[0].Height, 0.005f);
        }

        [Test]
        public void PriorsReadAgain_WithTheSamePlanes_ChangeNothing()
        {
            var map = new RoomSurfaceMap();
            map.BeginPriors();
            map.AddPrior(0f, Polygon(WholeFloor));
            map.EndPriors();
            ShowAll(map);
            Assert.IsFalse(map.Levels[0].IsDirty);

            map.BeginPriors();
            map.AddPrior(0f, Polygon(WholeFloor));
            map.EndPriors();

            Assert.IsFalse(map.Levels[0].IsDirty, "The same planes must not redraw the floor.");
            Assert.AreEqual(16f, map.Levels[0].PlaneArea, 0.2f);

            map.BeginPriors();
            map.AddPrior(0f, Polygon(new Rect(-2f, -2f, 2f, 2f)));
            map.EndPriors();

            Assert.IsTrue(map.Levels[0].IsDirty);
            Assert.AreEqual(4f, map.Levels[0].PlaneArea, 0.1f);
            Assert.IsFalse(map.Levels[0].HasPrior(Cell(1f, 1f)));
        }

        [Test]
        public void SetFloor_MakesTheChosenLevelTheFloor()
        {
            var map = new RoomSurfaceMap { PriorsNeedConfirmation = false };
            map.AddPrior(0.3f, Polygon(new Rect(0f, 0f, 1f, 1f)));
            map.AddPrior(0.75f, Polygon(new Rect(2f, 0f, 1f, 1f)));
            ShowAll(map);
            Assert.IsTrue(map.Raycast(new Ray(new Vector3(0.5f, 1.5f, 0.5f), Vector3.down), 5f, out _, out var id));

            Assert.IsTrue(map.SetFloor(id));

            Assert.AreEqual(FloorState.Trusted, map.FloorState);
            Assert.AreEqual(0.3f, map.FloorHeight(0.5f).Value, 0.01f);
            Assert.IsFalse(map.SetFloor(12345));
        }

        [Test]
        public void Raycast_DoesNotHitWhatLiesBelowTheFloor()
        {
            var map = new RoomSurfaceMap { PriorsNeedConfirmation = false };
            map.AddPrior(0f, Polygon(new Rect(0f, 0f, 2f, 2f)));
            map.AddPrior(-0.5f, Polygon(new Rect(2f, 0f, 2f, 2f)));
            ShowAll(map);
            Assert.IsTrue(map.Raycast(new Ray(new Vector3(0.5f, 1.5f, 0.5f), Vector3.down), 5f, out _, out var id));
            map.SetFloor(id);
            ShowAll(map);

            Assert.IsFalse(map.Raycast(new Ray(new Vector3(3f, 1.5f, 1f), Vector3.down), 5f, out _));
        }

        [Test]
        public void Integrate_Sparse_HeightFromArcoreThreeCentimetersOff_IsCorrected()
        {
            var map = new RoomSurfaceMap { PriorsNeedConfirmation = false };
            map.AddPrior(0.78f, Polygon(new Rect(0f, 0f, 1.6f, 0.7f)));

            Integrate(map, SparsePointsOn(new Vector3(0.8f, 1.5f, -0.5f), 0.75f, new Rect(0f, 0f, 1.6f, 0.7f)), 3);

            Assert.AreEqual(1, map.Levels.Count);
            Assert.AreEqual(0.75f, map.Levels[0].Height, 0.01f);
        }

        [Test]
        public void Integrate_Sparse_DriftedDuplicateFloor_MovesOntoTheRealFloorAndMerges()
        {
            var map = new RoomSurfaceMap { PriorsNeedConfirmation = false };
            map.AddPrior(0f, Polygon(new Rect(0f, 0f, 2f, 2f)));
            map.AddPrior(0.12f, Polygon(new Rect(1f, 1f, 2f, 2f)));
            Assert.AreEqual(2, map.Levels.Count);

            Integrate(map, SparsePointsOn(new Vector3(1.5f, 1.4f, -0.5f), 0f, new Rect(0f, 0f, 3f, 3f)), 3);

            Assert.AreEqual(1, map.Levels.Count);
            Assert.AreEqual(0f, map.Levels[0].Height, 0.01f);
            Assert.IsTrue(map.Levels[0].HasPrior(Cell(2.5f, 2.5f)));
        }

        [Test]
        public void Integrate_Sparse_PlanesShowAtOnce()
        {
            var map = new RoomSurfaceMap { PriorsNeedConfirmation = false };
            map.AddPrior(0.75f, Polygon(new Rect(0f, 0f, 1f, 1f)));

            Assert.AreEqual(CellState.Occupied, map.Levels[0].GetState(Cell(0.5f, 0.5f)));

            map.PriorsNeedConfirmation = true;
            Assert.AreEqual(CellState.Unknown, map.Levels[0].GetState(Cell(0.5f, 0.5f)),
                "With depth, a raised plane has to be confirmed.");
        }

        [Test]
        public void TooManyLevels_TheWeakestGo()
        {
            var map = new RoomSurfaceMap();
            map.AddPrior(0f, Polygon(WholeFloor));
            for (var i = 1; i <= RoomSurfaceMap.MaxLevels + 3; i++)
            {
                map.EnsureLevel(0.1f * i);
            }

            map.Integrate(PointsOn(Camera, 0f, SeenFloor));

            Assert.AreEqual(RoomSurfaceMap.MaxLevels, map.Levels.Count);
            Assert.AreEqual(0f, map.Levels[0].Height, 0.005f, "The floor stays.");
        }

        [Test]
        public void LevelMerged_TellsWhichLevelWentIntoWhich()
        {
            var map = new RoomSurfaceMap();
            map.AddPrior(0f, Polygon(new Rect(0f, 0f, 2f, 2f)));
            map.AddPrior(0.12f, Polygon(new Rect(1f, 1f, 2f, 2f)));
            var floorId = map.Levels[0].Id;
            var duplicateId = map.Levels[1].Id;
            var from = -1;
            var into = -1;
            map.LevelMerged += (gone, kept) =>
            {
                from = gone;
                into = kept;
            };

            Integrate(map, PointsOn(new Vector3(1.5f, 1.4f, -0.5f), 0f, new Rect(0f, 0f, 3f, 3f)), 2);

            Assert.AreEqual(1, map.Levels.Count);
            Assert.AreEqual(map.Levels[0].Id, into);
            Assert.AreEqual(into == floorId ? duplicateId : floorId, from);
            Assert.IsNotNull(map.FindLevel(into));
            Assert.IsNull(map.FindLevel(from));
        }
    }
}
