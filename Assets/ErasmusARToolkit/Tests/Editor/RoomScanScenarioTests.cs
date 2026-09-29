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
            var holes = CountCells(floor, FloorPlane, cell => !floor.IsShown(cell));
            Assert.AreEqual(0, holes, "Cells of the floor that are not drawn.");
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
            var holes = CountCells(floor, FloorPlane, cell => !floor.IsShown(cell));
            Assert.AreEqual(0, holes);
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
