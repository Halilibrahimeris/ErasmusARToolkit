using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Erasmus.ARToolkit.Tests
{
    /// <summary>
    /// The room scan on simulated rooms with the mistakes phones make: depth that gets worse with the
    /// distance, a shiny floor, pixels ARCore is not sure of, a pose that lags, a desk that is seen before
    /// the floor, and a user who starts on a podium.
    /// </summary>
    public class RoomScanScenarioTests
    {
        const float DeskHeight = 0.75f;
        const int FramesPerPose = 3;

        static readonly Rect FloorPlane = Rect.MinMaxRect(-1.5f, -1.5f, 3.5f, 2.2f);
        static readonly Rect DeskPlane = Rect.MinMaxRect(0f, 0f, 2.5f, 0.7f);

        // Camera position and the point it looks at, like a student walking around the desk.
        static readonly Vector3[,] Walk =
        {
            { new Vector3(0.8f, 1.4f, -1.0f), new Vector3(0.8f, 0.5f, 0.35f) },
            { new Vector3(2.2f, 1.4f, -1.0f), new Vector3(2.0f, 0.3f, 0.35f) },
            { new Vector3(3.0f, 1.4f, 0.35f), new Vector3(1.8f, 0.3f, 0.35f) },
            { new Vector3(-1.0f, 1.4f, 0.35f), new Vector3(0.5f, 0.5f, 0.35f) },
            { new Vector3(0.8f, 1.4f, 1.8f), new Vector3(0.8f, 0.5f, 0.35f) },
            { new Vector3(2.1f, 1.4f, -0.3f), new Vector3(2.1f, 0f, 0.6f) },
            { new Vector3(1.8f, 1.5f, 0.35f), new Vector3(1.9f, 0f, 0.35f) },
            { new Vector3(2.4f, 1.5f, 0.35f), new Vector3(2.3f, 0f, 0.35f) },
            { new Vector3(1.3f, 1.5f, 0.35f), new Vector3(2.0f, 0f, 0.35f) },
        };

        /// <summary>A desk and a chair as solid boxes.</summary>
        static RoomSimulator Classroom(SimulationOptions options = null)
        {
            var room = new RoomSimulator(options);
            room.AddBox(new Vector3(0f, 0f, 0f), new Vector3(1.6f, DeskHeight, 0.7f));
            room.AddBox(new Vector3(1.9f, 0f, 0.1f), new Vector3(2.3f, 0.45f, 0.5f));
            return room;
        }

        /// <summary>A desk top and a chair seat without what carries them: the floor mirrors their undersides.</summary>
        static RoomSimulator ClassroomOfSlabs(SimulationOptions options)
        {
            var room = new RoomSimulator(options);
            room.AddBox(new Vector3(0f, 0.7f, 0f), new Vector3(1.6f, DeskHeight, 0.7f));
            room.AddBox(new Vector3(1.9f, 0.4f, 0.1f), new Vector3(2.3f, 0.45f, 0.5f));
            return room;
        }

        static void Scan(RoomSurfaceMap map, RoomSimulator room, Vector3[,] poses, int framesPerPose = FramesPerPose,
            float alternatingPoseLag = 0f)
        {
            var frame = new ScanFrame();
            var count = 0;
            for (var pose = 0; pose < poses.GetLength(0); pose++)
            {
                for (var repeat = 0; repeat < framesPerPose; repeat++)
                {
                    room.Options.PoseLagDegrees = count++ % 2 == 0 ? alternatingPoseLag : -alternatingPoseLag;
                    room.Capture(poses[pose, 0], poses[pose, 1], frame);
                    map.Integrate(frame);
                }
            }
        }

        static RoomSurfaceMap MapWithPlanes(float floorPlaneHeight = 0f, bool deskPlane = true)
        {
            var map = new RoomSurfaceMap();
            map.AddPrior(floorPlaneHeight, RoomSurfaceMapTests.Polygon(FloorPlane));
            if (deskPlane)
            {
                map.AddPrior(0.78f, RoomSurfaceMapTests.Polygon(DeskPlane));
            }

            return map;
        }

        static SurfaceLevel LevelNear(RoomSurfaceMap map, float height)
        {
            SurfaceLevel best = null;
            foreach (var level in map.Levels)
            {
                if (best == null || Mathf.Abs(level.Height - height) < Mathf.Abs(best.Height - height))
                {
                    best = level;
                }
            }

            return best;
        }

        static int CountCells(SurfaceLevel level, Rect area, System.Func<Vector2Int, bool> matches)
        {
            var count = 0;
            var min = RoomSurfaceMap.CellOf(area.xMin + 0.001f, area.yMin + 0.001f);
            var max = RoomSurfaceMap.CellOf(area.xMax - 0.001f, area.yMax - 0.001f);
            for (var z = min.y; z <= max.y; z++)
            {
                for (var x = min.x; x <= max.x; x++)
                {
                    if (matches(new Vector2Int(x, z)))
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        /// <summary>Pieces the shown cells of a level fall into (cells that touch at a side are one piece).</summary>
        static int CountPieces(SurfaceLevel level, Rect area)
        {
            var min = RoomSurfaceMap.CellOf(area.xMin - 0.5f, area.yMin - 0.5f);
            var max = RoomSurfaceMap.CellOf(area.xMax + 0.5f, area.yMax + 0.5f);
            var seen = new HashSet<Vector2Int>();
            var queue = new Queue<Vector2Int>();
            var pieces = 0;
            for (var z = min.y; z <= max.y; z++)
            {
                for (var x = min.x; x <= max.x; x++)
                {
                    var start = new Vector2Int(x, z);
                    if (!level.IsShown(start) || !seen.Add(start))
                    {
                        continue;
                    }

                    pieces++;
                    queue.Enqueue(start);
                    while (queue.Count > 0)
                    {
                        var cell = queue.Dequeue();
                        foreach (var step in new[] { Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down })
                        {
                            var next = cell + step;
                            if (level.IsShown(next) && seen.Add(next))
                            {
                                queue.Enqueue(next);
                            }
                        }
                    }
                }
            }

            return pieces;
        }

        /// <summary>
        /// Cells of the floor that are not drawn although nothing stands there: holes. Under the desk and
        /// the chair, and one step around them, the floor is not drawn on purpose.
        /// </summary>
        static int CountHoles(SurfaceLevel floor, Rect area)
        {
            const float around = 0.15f;
            var desk = Rect.MinMaxRect(0f - around, 0f - around, 1.6f + around, 0.7f + around);
            var chair = Rect.MinMaxRect(1.9f - around, 0.1f - around, 2.3f + around, 0.5f + around);
            return CountCells(floor, area, cell =>
            {
                var center = new Vector2((cell.x + 0.5f) * RoomSurfaceMap.CellSize, (cell.y + 0.5f) * RoomSurfaceMap.CellSize);
                return !floor.IsShown(cell) && !desk.Contains(center) && !chair.Contains(center);
            });
        }

        static void AssertNothingUsableBelow(RoomSurfaceMap map, float floorHeight)
        {
            foreach (var level in map.Levels)
            {
                if (level.Height < floorHeight - FloorModel.SuppressBelow)
                {
                    Assert.IsTrue(!level.IsUsable || level.ShownArea <= 0f,
                        $"A surface of {level.ShownArea:0.00} m² is drawn at {level.Height:0.00}, below the floor.");
                }
            }
        }

        [Test]
        public void RangeNoise_FloorHeightStaysWithinOneCentimeter()
        {
            var room = Classroom(new SimulationOptions { NoiseBase = 0.01f, NoiseK = 0.005f });
            var map = MapWithPlanes();

            Scan(map, room, Walk);
            RoomSurfaceMapTests.ShowAll(map);

            Assert.AreEqual(FloorState.Trusted, map.FloorState);
            Assert.AreEqual(0f, map.Floor.Height, 0.01f);
            Assert.AreEqual(0f, map.FloorHeight(0.5f).Value, 0.01f);
            Assert.AreEqual(DeskHeight, LevelNear(map, DeskHeight).Height, 0.015f);
            AssertNothingUsableBelow(map, 0f);
        }

        [Test]
        public void ScaleBiasAndPlaneBias_FloorErrorAgainstGroundTruthWithinTwoCentimeters()
        {
            // The phone measures every depth 3 % too far, and ARCore has the floor plane 2 cm too high.
            var room = Classroom(new SimulationOptions { ScaleBias = 1.03f });
            var map = MapWithPlanes(floorPlaneHeight: 0.02f);

            Scan(map, room, Walk);
            RoomSurfaceMapTests.ShowAll(map);

            Assert.AreEqual(FloorState.Trusted, map.FloorState);
            Assert.AreEqual(0f, map.Floor.Height, 0.021f);
            AssertNothingUsableBelow(map, 0f);
        }

        [Test]
        public void GlossyFloor_ReflectionsMakeNoHolesAndNoLevelBelow()
        {
            var room = ClassroomOfSlabs(new SimulationOptions { GlossyFloor = 0.35f });
            var map = MapWithPlanes();

            Scan(map, room, Walk, 4);
            RoomSurfaceMapTests.ShowAll(map);

            Assert.AreEqual(FloorState.Trusted, map.FloorState);
            var floor = map.Floor.Level;
            Assert.AreEqual(0f, floor.Height, 0.01f);
            AssertNothingUsableBelow(map, 0f);
            Assert.AreEqual(0, CountHoles(floor, FloorPlane), "Cells of the floor that are not drawn.");
            var carved = CountCells(floor, FloorPlane, cell => floor.GetState(cell) == CellState.Carved);
            Assert.AreEqual(0, carved, "Cells of the floor the scan believes to be holes.");

            Assert.IsTrue(map.Raycast(new Ray(new Vector3(2.6f, 1.4f, 0.3f), Vector3.down), 5f, out var pose));
            Assert.AreEqual(0f, pose.position.y, 0.01f);
        }

        [Test]
        public void GlossyFloor_PlaneOnAReflection_DoesNotBecomeTheFloor()
        {
            var room = ClassroomOfSlabs(new SimulationOptions { GlossyFloor = 0.35f });
            var map = MapWithPlanes();
            Scan(map, room, Walk, 2);
            Assert.AreEqual(FloorState.Trusted, map.FloorState);

            // ARCore detects a plane on what the floor mirrors.
            map.AddPrior(-0.4f, RoomSurfaceMapTests.Polygon(Rect.MinMaxRect(1.5f, -0.5f, 3f, 1f)));
            Scan(map, room, Walk, 2);
            RoomSurfaceMapTests.ShowAll(map);

            Assert.AreEqual(FloorState.Trusted, map.FloorState);
            Assert.AreEqual(0f, map.Floor.Height, 0.01f);
            Assert.AreEqual(0f, map.FloorHeight(0.5f).Value, 0.01f);
            AssertNothingUsableBelow(map, 0f);
        }

        [Test]
        public void LowConfidenceFloor_CoverageAtLeast90PercentOfTheHull_OneComponent()
        {
            var room = Classroom(new SimulationOptions { LowConfidenceShare = 0.4f });
            var map = MapWithPlanes();

            Scan(map, room, Walk);
            RoomSurfaceMapTests.ShowAll(map);

            var floor = map.Floor.Level;
            Assert.IsNotNull(floor);
            var hull = FloorPlane.width * FloorPlane.height;
            Assert.GreaterOrEqual(floor.ShownArea, 0.9f * hull);
            Assert.AreEqual(1, CountPieces(floor, FloorPlane));
            Assert.AreEqual(0f, floor.Height, 0.01f);
        }

        [Test]
        public void LowConfidenceFloor_WithoutAPlane_IsStillFound()
        {
            var room = Classroom(new SimulationOptions { LowConfidenceShare = 0.4f });
            var map = new RoomSurfaceMap();

            Scan(map, room, Walk, 4);
            RoomSurfaceMapTests.ShowAll(map);

            Assert.AreNotEqual(FloorState.Unknown, map.FloorState);
            Assert.AreEqual(0f, map.Floor.Height, 0.01f);
            Assert.Greater(map.Floor.Level.ShownArea, 2f);
        }

        [Test]
        public void PoseLag_DoesNotCarveTheFloor()
        {
            var room = Classroom();
            var map = MapWithPlanes();

            Scan(map, room, Walk, 4, alternatingPoseLag: 2f);
            RoomSurfaceMapTests.ShowAll(map);

            Assert.AreEqual(FloorState.Trusted, map.FloorState);
            var floor = map.Floor.Level;
            Assert.AreEqual(0f, floor.Height, 0.03f);
            var carved = CountCells(floor, FloorPlane, cell => floor.GetState(cell) == CellState.Carved);
            Assert.AreEqual(0, carved);
            Assert.AreEqual(0, CountHoles(floor, FloorPlane));
            AssertNothingUsableBelow(map, 0f);
        }

        [Test]
        public void DeskPlaneFirst_FloorEndsAtZero()
        {
            var room = Classroom();
            var map = new RoomSurfaceMap();
            map.AddPrior(0.78f, RoomSurfaceMapTests.Polygon(DeskPlane));
            var frame = new ScanFrame();

            // ARCore found the desk first, and the user looks at it for a while.
            for (var i = 0; i < 20; i++)
            {
                room.Capture(new Vector3(0.8f + 0.02f * i, 1.4f, -1.0f), new Vector3(0.8f, 0.6f, 0.35f), frame);
                map.Integrate(frame);
                var desk = LevelNear(map, DeskHeight);
                Assert.IsFalse(desk.IsFloor, $"Frame {i}: a desk 65 cm below the phone is not the floor.");
            }

            map.AddPrior(0f, RoomSurfaceMapTests.Polygon(FloorPlane));
            Scan(map, room, Walk);
            RoomSurfaceMapTests.ShowAll(map);

            Assert.AreEqual(FloorState.Trusted, map.FloorState);
            Assert.AreEqual(0f, map.Floor.Height, 0.01f);
            Assert.AreEqual(0f, map.FloorHeight(0.5f).Value, 0.01f);
            Assert.AreEqual(DeskHeight, LevelNear(map, DeskHeight).Height, 0.01f);
            Assert.Greater(LevelNear(map, DeskHeight).ShownArea, 0.8f);
        }

        [Test]
        public void DeskPlaneFirst_UnderAHighCamera_FloorEndsAtZero()
        {
            var room = Classroom();
            var map = new RoomSurfaceMap();
            map.AddPrior(0.78f, RoomSurfaceMapTests.Polygon(DeskPlane));
            var frame = new ScanFrame();

            // A tall user: the desk lies where a floor could be. Only the desk is in view.
            for (var i = 0; i < 20; i++)
            {
                room.Capture(new Vector3(0.6f + 0.02f * i, 1.7f, 0.2f), new Vector3(0.8f, DeskHeight, 0.35f), frame);
                map.Integrate(frame);
            }

            Assert.AreNotEqual(FloorState.Trusted, map.FloorState, "One desk is too small for a floor.");

            // No floor plane from ARCore: depth has to find the floor.
            var high = new Vector3(0f, 0.3f, 0f);
            for (var round = 0; round < 2; round++)
            {
                for (var pose = 0; pose < Walk.GetLength(0); pose++)
                {
                    for (var repeat = 0; repeat < FramesPerPose; repeat++)
                    {
                        room.Capture(Walk[pose, 0] + high, Walk[pose, 1], frame);
                        map.Integrate(frame);
                    }
                }
            }

            RoomSurfaceMapTests.ShowAll(map);

            Assert.AreEqual(FloorState.Trusted, map.FloorState);
            Assert.AreEqual(0f, map.Floor.Height, 0.01f);
            var desk = LevelNear(map, DeskHeight);
            Assert.IsFalse(desk.IsFloor);
            Assert.AreEqual(DeskHeight, desk.Height, 0.01f);
            Assert.Greater(desk.ShownArea, 0.8f);
        }

        [Test]
        public void StartOnAPodium_FloorMovesDown_PodiumStaysASurface()
        {
            const float podium = 0.3f;
            var room = new RoomSimulator();
            room.AddBox(new Vector3(-1f, 0f, -1f), new Vector3(1f, podium, 1f));
            var map = new RoomSurfaceMap();
            map.AddPrior(podium, RoomSurfaceMapTests.Polygon(Rect.MinMaxRect(-1f, -1f, 1f, 1f)));
            var frame = new ScanFrame();
            var eye = new Vector3(0f, podium + 1.4f, 0f);

            // The user stands on the podium and looks at it, near the feet: nothing else is in view.
            room.Options.HalfFieldOfViewDegrees = 15f;
            for (var i = 0; i < 16; i++)
            {
                var angle = i * 45f * Mathf.Deg2Rad;
                room.Capture(eye, new Vector3(0.3f * Mathf.Cos(angle), podium, 0.3f * Mathf.Sin(angle)), frame);
                map.Integrate(frame);
            }

            Assert.AreEqual(FloorState.Trusted, map.FloorState);
            Assert.AreEqual(podium, map.Floor.Height, 0.01f);

            // Then at the room around it.
            room.Options.HalfFieldOfViewDegrees = 25f;
            for (var i = 0; i < 48; i++)
            {
                var angle = i * 30f * Mathf.Deg2Rad;
                var outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                room.Capture(eye + outward * 0.6f, outward * 2.4f, frame);
                map.Integrate(frame);
            }

            RoomSurfaceMapTests.ShowAll(map);

            Assert.AreEqual(FloorState.Trusted, map.FloorState);
            Assert.AreEqual(0f, map.Floor.Height, 0.015f);
            var top = LevelNear(map, podium);
            Assert.AreEqual(podium, top.Height, 0.015f);
            Assert.IsFalse(top.IsFloor);
            Assert.IsTrue(top.IsUsable);
            Assert.IsTrue(top.IsShown(RoomSurfaceMap.CellOf(0.2f, 0.2f)), "The podium is a surface to place things on.");
            Assert.IsTrue(map.Raycast(new Ray(new Vector3(2f, 1.5f, 0f), Vector3.down), 5f, out var onFloor));
            Assert.AreEqual(0f, onFloor.position.y, 0.02f);
            Assert.IsTrue(map.Raycast(new Ray(new Vector3(0.2f, 1.5f, 0.2f), Vector3.down), 5f, out var onPodium));
            Assert.AreEqual(podium, onPodium.position.y, 0.02f);
        }

        [Test]
        public void Integrate_SameSceneAt3480And19200Points_GivesTheSameLevelsAndFloorState()
        {
            var coarse = ScanWith(58, 60);
            var fine = ScanWith(160, 120);

            Assert.AreEqual(coarse.FloorState, fine.FloorState);
            Assert.AreEqual(UsableLevels(coarse).Count, UsableLevels(fine).Count);
            var coarseLevels = UsableLevels(coarse);
            var fineLevels = UsableLevels(fine);
            for (var i = 0; i < coarseLevels.Count; i++)
            {
                Assert.AreEqual(coarseLevels[i].Height, fineLevels[i].Height, 0.01f);
                Assert.AreEqual(coarseLevels[i].ShownArea, fineLevels[i].ShownArea,
                    0.1f * Mathf.Max(coarseLevels[i].ShownArea, 0.5f), $"Area at {coarseLevels[i].Height:0.00}");
            }
        }

        /// <summary>A chair as depth sees it: four thin legs, a seat and a back.</summary>
        static int[] AddChair(RoomSimulator room, Vector3 corner)
        {
            const float leg = 0.03f;
            const float side = 0.4f;
            var parts = new List<int>();
            foreach (var x in new[] { 0f, side - leg })
            {
                foreach (var z in new[] { 0f, side - leg })
                {
                    parts.Add(room.AddBox(corner + new Vector3(x, 0f, z), corner + new Vector3(x + leg, 0.42f, z + leg)));
                }
            }

            parts.Add(room.AddBox(corner + new Vector3(0f, 0.42f, 0f), corner + new Vector3(side, 0.45f, side)));
            parts.Add(room.AddBox(corner + new Vector3(0f, 0.45f, side - leg), corner + new Vector3(side, 0.9f, side)));
            return parts.ToArray();
        }

        // Around a chair that stands at (1.9 .. 2.3, 0.1 .. 0.5).
        static readonly Vector3[,] AroundTheChair =
        {
            { new Vector3(2.1f, 1.4f, -1.2f), new Vector3(2.1f, 0.2f, 0.3f) },
            { new Vector3(3.4f, 1.4f, 0.3f), new Vector3(2.1f, 0.2f, 0.3f) },
            { new Vector3(2.1f, 1.4f, 1.8f), new Vector3(2.1f, 0.2f, 0.3f) },
            { new Vector3(0.8f, 1.4f, 0.3f), new Vector3(2.1f, 0.2f, 0.3f) },
            { new Vector3(3.0f, 1.5f, -0.9f), new Vector3(2.1f, 0.1f, 0.3f) },
            { new Vector3(1.2f, 1.5f, -0.9f), new Vector3(2.1f, 0.1f, 0.3f) },
        };

        static readonly Rect ChairFloor = Rect.MinMaxRect(1.9f, 0.1f, 2.3f, 0.5f);

        static RoomSurfaceMap MapWithAFloor()
        {
            var map = new RoomSurfaceMap();
            map.AddPrior(0f, RoomSurfaceMapTests.Polygon(FloorPlane));
            return map;
        }

        static ObstacleVolume? BoxAt(RoomSurfaceMap map, Vector3 point)
        {
            foreach (var volume in map.Volumes)
            {
                if (volume.ContainsXZ(point))
                {
                    return volume;
                }
            }

            return null;
        }

        [Test]
        public void ThinLegChair_BlocksTheFloorUnderIt()
        {
            var room = new RoomSimulator(new SimulationOptions { Columns = 80, Rows = 80 });
            AddChair(room, new Vector3(1.9f, 0f, 0.1f));
            var map = MapWithAFloor();

            Scan(map, room, AroundTheChair, 6);
            RoomSurfaceMapTests.ShowAll(map);

            Assert.AreEqual(FloorState.Trusted, map.FloorState);
            var floor = map.Floor.Level;
            var drawnUnderIt = CountCells(floor, ChairFloor, floor.IsShown);
            Assert.AreEqual(0, drawnUnderIt, "The floor grid is drawn under the chair.");
            Assert.IsTrue(floor.IsShown(RoomSurfaceMap.CellOf(2.1f, -0.4f)), "The floor in front of the chair is free.");
            Assert.IsTrue(floor.IsShown(RoomSurfaceMap.CellOf(1.4f, 0.3f)));

            var box = BoxAt(map, new Vector3(2.1f, 0f, 0.3f));
            Assert.IsTrue(box.HasValue, "The chair has a box around it.");
            Assert.GreaterOrEqual(box.Value.Size.y, 0.4f);
            Assert.LessOrEqual(box.Value.Size.x, 0.65f);
            Assert.LessOrEqual(box.Value.Size.z, 0.65f);
            Assert.AreEqual(0f, box.Value.Pose.position.y, 0.01f);
            Assert.AreEqual(1, map.Volumes.Count);
            AssertNothingUsableBelow(map, 0f);
        }

        [Test]
        public void MovedChair_FreesTheFloor()
        {
            var room = new RoomSimulator(new SimulationOptions { Columns = 80, Rows = 80 });
            var chair = AddChair(room, new Vector3(1.9f, 0f, 0.1f));
            var map = MapWithAFloor();
            Scan(map, room, AroundTheChair, 6);
            RoomSurfaceMapTests.ShowAll(map);
            Assert.AreEqual(1, map.Volumes.Count);

            // Someone takes the chair out of the room.
            for (var i = chair.Length - 1; i >= 0; i--)
            {
                room.RemoveBox(chair[i]);
            }

            Scan(map, room, AroundTheChair, 3);
            RoomSurfaceMapTests.ShowAll(map);

            var floor = map.Floor.Level;
            Assert.AreEqual(0, map.Obstacles.BlockedCount);
            Assert.AreEqual(0, map.Volumes.Count, "The box goes with the chair.");
            var missing = CountCells(floor, ChairFloor, cell => !floor.IsShown(cell));
            Assert.AreEqual(0, missing, "The floor where the chair stood is drawn again.");
            foreach (var level in map.Levels)
            {
                Assert.IsTrue(level.IsFloor || level.ShownArea <= 0f, $"The seat at {level.Height:0.00} is gone.");
            }
        }

        [Test]
        public void PersonWalkingThrough_LeavesNoObstacle()
        {
            var room = new RoomSimulator(new SimulationOptions { Columns = 80, Rows = 80 });
            var map = MapWithAFloor();
            Scan(map, room, AroundTheChair, 3);
            Assert.AreEqual(FloorState.Trusted, map.FloorState);

            // Someone walks through the view: at each spot for a quarter of a second.
            var frame = new ScanFrame();
            for (var step = 0; step < 8; step++)
            {
                var x = 1.2f + 0.25f * step;
                var person = room.AddBox(new Vector3(x, 0f, 0.1f), new Vector3(x + 0.4f, 1.7f, 0.4f));
                for (var repeat = 0; repeat < 2; repeat++)
                {
                    room.Capture(AroundTheChair[0, 0], AroundTheChair[0, 1], frame);
                    map.Integrate(frame);
                }

                room.RemoveBox(person);
            }

            Scan(map, room, AroundTheChair, 2);
            RoomSurfaceMapTests.ShowAll(map);

            Assert.AreEqual(0, map.Obstacles.BlockedCount);
            Assert.AreEqual(0, map.Volumes.Count);
            var floor = map.Floor.Level;
            var missing = CountCells(floor, Rect.MinMaxRect(1.2f, 0.1f, 3.3f, 0.4f), cell => !floor.IsShown(cell));
            Assert.AreEqual(0, missing);
        }

        [Test]
        public void BagOnTheFloor_IsAnOtherVolume()
        {
            var room = new RoomSimulator(new SimulationOptions { Columns = 80, Rows = 80 });
            room.AddBox(new Vector3(1.9f, 0f, 0.1f), new Vector3(2.3f, 0.2f, 0.4f));
            var map = MapWithAFloor();

            Scan(map, room, AroundTheChair, 6);
            RoomSurfaceMapTests.ShowAll(map);

            Assert.AreEqual(1, map.Volumes.Count);
            var bag = map.Volumes[0];
            Assert.AreEqual(ObstacleKind.Other, bag.Kind);
            Assert.AreEqual(0.2f, bag.Size.y, 0.03f);

            // A box is a little larger than what is in it: depth is noisy at the edges of things.
            Assert.AreEqual(0.4f, bag.Size.x, 0.16f);
            Assert.AreEqual(0.3f, bag.Size.z, 0.16f);
            Assert.GreaterOrEqual(bag.Size.x, 0.4f);
            Assert.GreaterOrEqual(bag.Size.z, 0.3f);
            Assert.IsTrue(bag.ContainsXZ(new Vector3(2.1f, 0f, 0.25f)));
        }

        [Test]
        public void ChairUnderDesk_FloorIsNotFilled()
        {
            var room = Classroom(new SimulationOptions { Columns = 80, Rows = 80 });
            var map = MapWithPlanes();

            Scan(map, room, Walk, 5);
            RoomSurfaceMapTests.ShowAll(map);

            Assert.AreEqual(FloorState.Trusted, map.FloorState);
            var floor = map.Floor.Level;

            // The chair stands beside the desk: the floor between and under them is not drawn.
            var underTheChair = CountCells(floor, Rect.MinMaxRect(1.95f, 0.15f, 2.25f, 0.45f), floor.IsShown);
            Assert.AreEqual(0, underTheChair);
            Assert.IsTrue(floor.IsShown(RoomSurfaceMap.CellOf(2.8f, -0.8f)));

            var desk = BoxAt(map, new Vector3(0.8f, 0f, 0.35f));
            Assert.IsTrue(desk.HasValue);
            Assert.AreEqual(ObstacleKind.Table, desk.Value.Kind);
            Assert.AreEqual(DeskHeight, desk.Value.Size.y, 0.03f);
            var chair = BoxAt(map, new Vector3(2.1f, 0f, 0.3f));
            Assert.IsTrue(chair.HasValue);
            Assert.AreNotEqual(desk.Value.Id, chair.Value.Id, "The desk and the chair beside it are two things.");
        }

        [Test]
        public void BackrestTopAtDeskHeight_DoesNotJoinDeskAndChair()
        {
            // A chair pushed up to the desk; the top of its back is as high as the desk and 3 cm wide.
            var room = new RoomSimulator(new SimulationOptions { Columns = 80, Rows = 80 });
            room.AddBox(new Vector3(0f, 0f, 0f), new Vector3(1.6f, DeskHeight, 0.7f));
            room.AddBox(new Vector3(0.6f, 0f, -0.5f), new Vector3(1.0f, 0.45f, -0.1f));
            room.AddBox(new Vector3(0.6f, 0.45f, -0.5f), new Vector3(1.0f, DeskHeight, -0.47f));
            var map = MapWithAFloor();
            var builder = new RoomModelBuilder();
            Vector3[,] poses =
            {
                { new Vector3(0.8f, 1.5f, -1.6f), new Vector3(0.8f, 0.5f, 0f) },
                { new Vector3(2.4f, 1.5f, -1.2f), new Vector3(0.8f, 0.5f, 0f) },
                { new Vector3(-0.8f, 1.5f, -1.2f), new Vector3(0.8f, 0.5f, 0f) },
                { new Vector3(0.8f, 1.6f, -0.9f), new Vector3(0.8f, 0.6f, 0.2f) },
                { new Vector3(1.6f, 1.6f, -1.4f), new Vector3(0.8f, 0.4f, -0.2f) },
                { new Vector3(0f, 1.6f, -1.4f), new Vector3(0.8f, 0.4f, -0.2f) },
            };

            Scan(map, room, poses, 5);
            RoomSurfaceMapTests.ShowAll(map, builder);

            var desk = LevelNear(map, DeskHeight);
            Assert.AreEqual(DeskHeight, desk.Height, 0.015f);
            Assert.AreEqual(1.6f * 0.7f, desk.ShownArea, 1.6f * 0.7f * 0.15f);
            var onTheBackrest = CountCells(desk, Rect.MinMaxRect(0.6f, -0.5f, 1.0f, -0.4f), desk.IsShown);
            Assert.AreEqual(0, onTheBackrest, "The top of the back of a chair is no surface to put things on.");
            var shape = builder.ShapeOf(desk.Id);
            Assert.IsNotNull(shape);
            Assert.AreEqual(1, shape.Loops.Count, "One desk.");
            Assert.AreEqual(4, shape.Loops[0].Count, "A desk has four corners.");
        }

        [Test]
        public void FragmentedFloorBetweenDesks_KeepsItsAisles()
        {
            // Two rows of desks with an aisle of 60 cm between them. ARCore has no plane for the floor:
            // depth finds it, in pieces between the furniture.
            var room = new RoomSimulator(new SimulationOptions { Columns = 80, Rows = 80 });
            room.AddBox(new Vector3(0f, 0f, 0f), new Vector3(1.6f, DeskHeight, 0.7f));
            room.AddBox(new Vector3(0f, 0f, 1.3f), new Vector3(1.6f, DeskHeight, 2.0f));
            var map = new RoomSurfaceMap();
            Vector3[,] poses =
            {
                { new Vector3(-1.2f, 1.5f, 1.0f), new Vector3(0.8f, 0f, 1.0f) },
                { new Vector3(2.8f, 1.5f, 1.0f), new Vector3(0.8f, 0f, 1.0f) },
                { new Vector3(-1.4f, 1.5f, 0.2f), new Vector3(0.2f, 0f, 1.0f) },
                { new Vector3(3.0f, 1.5f, 1.8f), new Vector3(1.4f, 0f, 1.0f) },
                { new Vector3(-1.5f, 1.4f, -0.8f), new Vector3(-0.8f, 0f, 1.0f) },
                { new Vector3(3.1f, 1.4f, -0.8f), new Vector3(2.4f, 0f, 1.0f) },
                { new Vector3(0.8f, 1.6f, -1.5f), new Vector3(0.8f, 0f, -0.6f) },
                { new Vector3(0.8f, 1.6f, 3.5f), new Vector3(0.8f, 0f, 2.6f) },
            };

            Scan(map, room, poses, 5);
            RoomSurfaceMapTests.ShowAll(map);

            Assert.AreNotEqual(FloorState.Unknown, map.FloorState);
            var floor = map.Floor.Level;
            Assert.AreEqual(0f, floor.Height, 0.015f);
            var aisle = Rect.MinMaxRect(0.2f, 0.85f, 1.4f, 1.15f);
            var drawn = CountCells(floor, aisle, floor.IsShown);
            var all = CountCells(floor, aisle, cell => true);
            Assert.GreaterOrEqual(drawn, all * 0.9f, "The aisle between the desks is floor to walk on.");
            Assert.AreEqual(0, CountCells(floor, Rect.MinMaxRect(0.2f, 0.15f, 1.4f, 0.55f), floor.IsShown),
                "No floor is drawn under the desks.");
        }

        [Test]
        public void TurnedRoom_IsDrawnAlongItsWalls()
        {
            // The scan was started while the user looked 20 degrees past the walls: the room stands
            // turned in the space of the scan.
            var turn = Matrix4x4.Rotate(Quaternion.Euler(0f, 20f, 0f));
            var room = Classroom(new SimulationOptions { Columns = 80, Rows = 80 });
            var map = new RoomSurfaceMap();
            var builder = new RoomModelBuilder();
            var floorCorners = new List<Vector2>();
            foreach (var corner in RoomSurfaceMapTests.Polygon(FloorPlane))
            {
                var turned = turn.MultiplyPoint3x4(new Vector3(corner.x, 0f, corner.y));
                floorCorners.Add(new Vector2(turned.x, turned.z));
            }

            map.AddPrior(0f, floorCorners);
            var frame = new ScanFrame();
            for (var pose = 0; pose < Walk.GetLength(0); pose++)
            {
                for (var repeat = 0; repeat < 4; repeat++)
                {
                    room.Capture(Walk[pose, 0], Walk[pose, 1], frame);
                    frame.Transform(turn);
                    map.Integrate(frame);
                    RoomSurfaceMapTests.ShowAll(map, builder);
                }
            }

            Assert.AreEqual(FloorState.Trusted, map.FloorState);
            Assert.AreEqual(20f, builder.RoomYaw, 1.5f, "The walls give the direction.");
            var desk = LevelNear(map, DeskHeight);
            var shape = builder.ShapeOf(desk.Id);
            Assert.AreEqual(1, shape.Loops.Count);
            Assert.AreEqual(4, shape.Loops[0].Count, "A desk has four corners.");
            for (var i = 0; i < 4; i++)
            {
                var edge = shape.Loops[0][(i + 1) % 4] - shape.Loops[0][i];
                Assert.AreEqual(0f, RoomAxes.Difference(RoomAxes.YawOf(edge), builder.RoomYaw), 1.1f);
            }

            Assert.AreEqual(1.6f * 0.7f, desk.ShownArea, 1.6f * 0.7f * 0.15f);
            var deskCenter = turn.MultiplyPoint3x4(new Vector3(0.8f, 0f, 0.35f));
            var box = BoxAt(map, deskCenter);
            Assert.IsTrue(box.HasValue);
            Assert.AreEqual(ObstacleKind.Table, box.Value.Kind);
            Assert.AreEqual(0f, RoomAxes.Difference(box.Value.Pose.rotation.eulerAngles.y, builder.RoomYaw), 0.5f,
                "The box around the desk stands along the walls, too.");
        }

        [Test]
        public void RoomWithWalls_FloorEndsAtTheWalls()
        {
            // Plain walls: ARCore detects no planes on them, but depth sees them. The plane of the floor
            // reaches through the walls, as the planes of ARCore do.
            var room = new RoomSimulator(new SimulationOptions { Columns = 80, Rows = 80 })
            {
                Walls = Rect.MinMaxRect(-1f, -2f, 3f, 1.6f),
            };
            var map = MapWithAFloor();
            var builder = new RoomModelBuilder();
            Vector3[,] poses =
            {
                { new Vector3(1f, 1.4f, -0.8f), new Vector3(1f, 0f, 0.4f) },
                { new Vector3(1.4f, 1.4f, -0.6f), new Vector3(0.4f, 0f, 0.6f) },
                { new Vector3(0.6f, 1.4f, -0.6f), new Vector3(1.6f, 0f, 0.6f) },
                { new Vector3(1f, 1.4f, -0.4f), new Vector3(1f, 0.9f, 1.6f) },
                { new Vector3(1.6f, 1.4f, -0.2f), new Vector3(0f, 0.9f, 1.6f) },
                { new Vector3(0.4f, 1.4f, -0.2f), new Vector3(2f, 0.9f, 1.6f) },
                { new Vector3(1.2f, 1.5f, 0f), new Vector3(1f, 0.6f, 1.6f) },
                { new Vector3(0.8f, 1.5f, 0f), new Vector3(1f, 0.6f, 1.6f) },
            };
            var frame = new ScanFrame();
            for (var round = 0; round < 2; round++)
            {
                for (var pose = 0; pose < poses.GetLength(0); pose++)
                {
                    for (var repeat = 0; repeat < 3; repeat++)
                    {
                        room.Capture(poses[pose, 0], poses[pose, 1], frame);
                        map.Integrate(frame);
                    }

                    RoomSurfaceMapTests.ShowAll(map, builder);
                }
            }

            Assert.AreEqual(FloorState.Trusted, map.FloorState);
            Assert.GreaterOrEqual(map.Walls.Segments.Count, 1, "The wall the camera looked at.");
            WallSegment? far = null;
            foreach (var wall in map.Walls.Segments)
            {
                if (wall.DistanceTo(new Vector2(1f, 1.6f)) < 0.1f)
                {
                    far = wall;
                }
            }

            Assert.IsTrue(far.HasValue, "A wall where the wall is.");
            Assert.GreaterOrEqual(far.Value.Length, 1.5f);
            Assert.IsFalse(far.Value.FromPlane);
            Assert.AreEqual(0f, RoomAxes.Difference(RoomAxes.YawOf(far.Value.B - far.Value.A), 0f), 2f);
            Assert.AreEqual(0f, far.Value.Bottom, 0.02f);

            var floor = map.Floor.Level;
            Assert.IsTrue(floor.IsShown(RoomSurfaceMap.CellOf(1f, 0.8f)));
            var behind = CountCells(floor, Rect.MinMaxRect(0.2f, 1.7f, 1.8f, 2.15f), floor.IsShown);
            Assert.AreEqual(0, behind, "The floor ends at the wall.");
            foreach (var volume in map.Volumes)
            {
                // Of the walls at the sides the camera saw the ends only: too short to be called walls.
                Assert.LessOrEqual(Mathf.Min(volume.Size.x, volume.Size.z), 0.25f,
                    $"Nothing but walls stands in this room, and {volume.Kind} {volume.Id} is {volume.Size} large.");
            }

            var through = new Ray(new Vector3(1f, 1.4f, 0f), (new Vector3(1f, 0f, 2f) - new Vector3(1f, 1.4f, 0f)).normalized);
            Assert.IsFalse(map.Raycast(through, 10f, out _));
        }

        [Test]
        public void MapMovedThreeCentimeters_TheFloorFollowsSooner()
        {
            var waited = FramesUntilTheFloorFollows(false);
            var hurried = FramesUntilTheFloorFollows(true);

            Assert.Less(waited, 60, "The floor follows what depth sees in any case.");
            Assert.Less(hurried, waited);
            Assert.LessOrEqual(hurried, 12, "A second and a half of scanning.");
        }

        /// <summary>
        /// The scan moved with its anchor, the room did not: the floor lies 3 cm lower in the scan than
        /// before. Frames until the scan has its floor there.
        /// </summary>
        static int FramesUntilTheFloorFollows(bool mapMoveIsNoted)
        {
            var room = new RoomSimulator(new SimulationOptions { Columns = 60, Rows = 60 });
            var map = new RoomSurfaceMap();
            Scan(map, room, Walk, 4);
            Assert.AreEqual(FloorState.Trusted, map.FloorState);
            Assert.AreEqual(0f, map.Floor.Level.Height, 0.01f);

            room.FloorHeight = -0.03f;
            if (mapMoveIsNoted)
            {
                map.NoteMapMoved();
            }

            var frame = new ScanFrame();
            for (var frames = 1; frames <= 60; frames++)
            {
                var pose = frames % Walk.GetLength(0);
                room.Capture(Walk[pose, 0], Walk[pose, 1], frame);
                map.Integrate(frame);
                if (map.Floor.Level.Height < -0.02f)
                {
                    Assert.AreEqual(FloorState.Trusted, map.FloorState);
                    return frames;
                }
            }

            return 60;
        }

        static RoomSurfaceMap ScanWith(int columns, int rows)
        {
            var room = Classroom(new SimulationOptions { Columns = columns, Rows = rows });
            var map = MapWithPlanes();
            Scan(map, room, Walk);
            RoomSurfaceMapTests.ShowAll(map);
            return map;
        }

        /// <summary>The levels with something drawn on them, lowest first.</summary>
        static List<SurfaceLevel> UsableLevels(RoomSurfaceMap map)
        {
            var levels = new List<SurfaceLevel>();
            foreach (var level in map.Levels)
            {
                if (level.IsUsable && level.ShownArea > 0f)
                {
                    levels.Add(level);
                }
            }

            levels.Sort((a, b) => a.Height.CompareTo(b.Height));
            return levels;
        }
    }
}
