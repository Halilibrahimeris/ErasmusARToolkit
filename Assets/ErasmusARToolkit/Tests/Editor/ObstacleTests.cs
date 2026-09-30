using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using static Erasmus.ARToolkit.Tests.RoomSurfaceMapTests;

namespace Erasmus.ARToolkit.Tests
{
    public class GridRayTests
    {
        const float Cell = RoomSurfaceMap.CellSize;

        static List<Vector2Int> Walk(Vector2 origin, Vector2 end)
        {
            var cells = new List<Vector2Int>();
            var ray = new GridRay();
            ray.Start(origin, end);
            var last = 0f;
            while (ray.Next(out var cell, out var enter, out var exit))
            {
                Assert.GreaterOrEqual(enter, last - 1e-6f, "The walk goes forward.");
                Assert.GreaterOrEqual(exit, enter);
                Assert.LessOrEqual(exit, 1f);
                last = exit;
                cells.Add(cell);
                Assert.Less(cells.Count, 1000, "The walk has to end.");
            }

            return cells;
        }

        [Test]
        public void AlongAnAxis_VisitsEveryCellOnce()
        {
            var cells = Walk(new Vector2(0.02f, 0.02f), new Vector2(0.52f, 0.02f));

            Assert.AreEqual(11, cells.Count);
            for (var i = 0; i < cells.Count; i++)
            {
                Assert.AreEqual(new Vector2Int(i, 0), cells[i]);
            }
        }

        [Test]
        public void Diagonal_NeverSkipsACell()
        {
            var origin = new Vector2(-0.31f, 0.07f);
            var end = new Vector2(0.83f, -0.64f);

            var cells = Walk(origin, end);

            Assert.AreEqual(RoomSurfaceMap.CellOf(origin.x, origin.y), cells[0]);
            Assert.AreEqual(RoomSurfaceMap.CellOf(end.x, end.y), cells[cells.Count - 1]);
            for (var i = 1; i < cells.Count; i++)
            {
                var step = cells[i] - cells[i - 1];
                Assert.AreEqual(1, Mathf.Abs(step.x) + Mathf.Abs(step.y), $"Step {i} goes to a cell next door.");
            }

            // Every point of the line lies in a cell of the walk.
            for (var t = 0f; t <= 1f; t += 0.01f)
            {
                var point = Vector2.Lerp(origin, end, t);
                CollectionAssert.Contains(cells, RoomSurfaceMap.CellOf(point.x, point.y));
            }
        }

        [Test]
        public void WithinOneCell_VisitsThatCell()
        {
            var cells = Walk(new Vector2(0.01f, 0.01f), new Vector2(0.04f, 0.03f));

            Assert.AreEqual(1, cells.Count);
            Assert.AreEqual(Vector2Int.zero, cells[0]);
        }

        [Test]
        public void EnterAndExit_SayWhereTheLineCrossesTheCell()
        {
            var ray = new GridRay();
            ray.Start(new Vector2(0.025f, 0.025f), new Vector2(0.225f, 0.025f));

            Assert.IsTrue(ray.Next(out _, out var enter, out var exit));
            Assert.AreEqual(0f, enter);
            Assert.AreEqual(0.125f, exit, 1e-4f);
            Assert.IsTrue(ray.Next(out var second, out enter, out exit));
            Assert.AreEqual(new Vector2Int(1, 0), second);
            Assert.AreEqual(0.125f, enter, 1e-4f);
            Assert.AreEqual(0.375f, exit, 1e-4f);
        }
    }

    public class MinAreaRectangleTests
    {
        static List<Vector2> Rectangle(Vector2 center, Vector2 size, float yaw)
        {
            MinAreaRectangle.AxesOf(yaw, out var axisX, out var axisZ);
            var points = new List<Vector2>();
            for (var x = -1; x <= 1; x++)
            {
                for (var z = -1; z <= 1; z++)
                {
                    points.Add(center + axisX * (x * size.x * 0.5f) + axisZ * (z * size.y * 0.5f));
                }
            }

            return points;
        }

        [Test]
        public void AxesOf_TurnLikeAQuaternion()
        {
            MinAreaRectangle.AxesOf(30f, out var axisX, out var axisZ);
            var right = Quaternion.Euler(0f, 30f, 0f) * Vector3.right;
            var forward = Quaternion.Euler(0f, 30f, 0f) * Vector3.forward;

            Assert.AreEqual(right.x, axisX.x, 1e-5f);
            Assert.AreEqual(right.z, axisX.y, 1e-5f);
            Assert.AreEqual(forward.x, axisZ.x, 1e-5f);
            Assert.AreEqual(forward.z, axisZ.y, 1e-5f);
        }

        [Test]
        public void Fit_RectangleAlongTheAxes()
        {
            var points = Rectangle(new Vector2(1f, 2f), new Vector2(1.6f, 0.7f), 0f);

            Assert.IsTrue(MinAreaRectangle.Fit(points, 0f, out var center, out var size, out var yaw));

            Assert.AreEqual(0f, yaw, 1e-3f);
            Assert.AreEqual(1f, center.x, 1e-4f);
            Assert.AreEqual(2f, center.y, 1e-4f);
            Assert.AreEqual(1.6f, size.x, 1e-4f);
            Assert.AreEqual(0.7f, size.y, 1e-4f);
        }

        [Test]
        public void Fit_TurnedRectangle_IsFoundWithItsTurn()
        {
            var points = Rectangle(new Vector2(-0.5f, 0.3f), new Vector2(1.6f, 0.7f), 35f);

            Assert.IsTrue(MinAreaRectangle.Fit(points, 0f, out var center, out var size, out var yaw));

            Assert.AreEqual(35f, yaw, 0.1f);
            Assert.AreEqual(-0.5f, center.x, 1e-3f);
            Assert.AreEqual(0.3f, center.y, 1e-3f);
            Assert.AreEqual(1.6f, size.x, 1e-3f);
            Assert.AreEqual(0.7f, size.y, 1e-3f);
        }

        [Test]
        public void Fit_TurnedALittle_FollowsThePreferredDirection()
        {
            // Depth noise turned the desk by two degrees: the box follows the walls all the same.
            var points = Rectangle(Vector2.zero, new Vector2(1.6f, 0.7f), 2f);

            Assert.IsTrue(MinAreaRectangle.Fit(points, 0f, out _, out var size, out var yaw));

            Assert.AreEqual(0f, yaw, 1e-3f);
            Assert.AreEqual(1.6f * 0.7f, size.x * size.y, 1.6f * 0.7f * 0.15f);
        }

        [Test]
        public void Fit_NamesItsDirectionNearThePreferredOne()
        {
            // The same rectangle can be called "turned by 100 degrees" or, with its sides swapped, "by 10".
            var points = Rectangle(Vector2.zero, new Vector2(0.7f, 1.6f), 100f);

            Assert.IsTrue(MinAreaRectangle.Fit(points, 0f, out _, out var size, out var yaw));

            Assert.LessOrEqual(Mathf.Abs(Mathf.DeltaAngle(0f, yaw)), 45f);
            Assert.AreEqual(10f, yaw, 0.2f);
            Assert.AreEqual(1.6f, size.x, 1e-3f);
            Assert.AreEqual(0.7f, size.y, 1e-3f);
        }

        [Test]
        public void Fit_TooFewPoints_OrPointsOnOneLine_IsRefused()
        {
            Assert.IsFalse(MinAreaRectangle.Fit(new List<Vector2> { Vector2.zero, Vector2.one }, 0f, out _, out _, out _));
            Assert.IsFalse(MinAreaRectangle.Fit(null, 0f, out _, out _, out _));
            var line = new List<Vector2> { Vector2.zero, Vector2.one, Vector2.one * 2f, Vector2.one * 3f };
            Assert.IsFalse(MinAreaRectangle.Fit(line, 0f, out _, out _, out _));
        }

        [Test]
        public void ConvexHull_DropsThePointsInside()
        {
            var points = Rectangle(Vector2.zero, Vector2.one, 0f);
            var hull = new List<Vector2>();

            MinAreaRectangle.ConvexHull(points, hull);

            Assert.AreEqual(4, hull.Count);
            CollectionAssert.DoesNotContain(hull, Vector2.zero);
        }
    }

    public class ObstacleVolumeTests
    {
        static readonly ObstacleVolume Box = new ObstacleVolume(1,
            new Pose(new Vector3(2f, 0f, 1f), Quaternion.Euler(0f, 90f, 0f)), new Vector3(1.6f, 0.75f, 0.7f),
            ObstacleKind.Table, 1f, 10f);

        [Test]
        public void ContainsXZ_FollowsTheTurnOfTheBox()
        {
            // Turned by 90 degrees, the long side lies along z.
            Assert.IsTrue(Box.ContainsXZ(new Vector3(2f, 5f, 1.7f)));
            Assert.IsFalse(Box.ContainsXZ(new Vector3(2.7f, 0f, 1f)));
            Assert.IsTrue(Box.ContainsXZ(new Vector3(2.37f, 0f, 1f), 0.03f));
            Assert.AreEqual(0.75f, Box.Top, 1e-5f);
        }

        [Test]
        public void Raycast_TellsWhereTheRayEnters()
        {
            Assert.IsTrue(Box.Raycast(new Ray(new Vector3(2f, 2f, 1f), Vector3.down), out var fromAbove));
            Assert.AreEqual(1.25f, fromAbove, 1e-4f);

            Assert.IsTrue(Box.Raycast(new Ray(new Vector3(0f, 0.4f, 1f), Vector3.right), out var fromTheSide));
            Assert.AreEqual(2f - 0.35f, fromTheSide, 1e-4f);

            Assert.IsTrue(Box.Raycast(new Ray(new Vector3(2f, 0.3f, 1f), Vector3.right), out var fromInside));
            Assert.AreEqual(0f, fromInside);
        }

        [Test]
        public void Raycast_MissesBesideAndBehind()
        {
            Assert.IsFalse(Box.Raycast(new Ray(new Vector3(4f, 2f, 1f), Vector3.down), out _));
            Assert.IsFalse(Box.Raycast(new Ray(new Vector3(2f, 2f, 1f), Vector3.up), out _));
            Assert.IsFalse(Box.Raycast(new Ray(new Vector3(0f, 1f, 1f), Vector3.right), out _), "Over the top.");
        }
    }

    public class ObstacleMapTests
    {
        static readonly Vector2Int Here = new Vector2Int(3, -7);

        static void Frame(ObstacleMap map, double time, System.Action<ObstacleMap> what)
        {
            map.BeginFrame(time, 1f);
            what(map);
            map.EndFrame();
        }

        /// <summary>Frames in which something is seen in the cell, eight times a second.</summary>
        static double See(ObstacleMap map, double from, int frames, float top = 0.45f)
        {
            for (var i = 0; i < frames; i++)
            {
                Frame(map, from + i * 0.125, m => m.AddHit(Here, top));
            }

            return from + frames * 0.125;
        }

        [Test]
        public void SeenForAMoment_IsNotBlocked()
        {
            var map = new ObstacleMap();

            See(map, 0.0, 8);

            Assert.Greater(map.LogOddsOf(Here), ObstacleMap.BlockedOn);
            Assert.IsFalse(map.IsBlocked(Here), "Someone who walks by is not furniture.");
            Assert.AreEqual(0, map.BlockedCount);
        }

        [Test]
        public void SeenAgainLater_IsBlocked()
        {
            var map = new ObstacleMap();
            var version = map.Version;

            See(map, 0.0, 14);

            Assert.IsTrue(map.IsBlocked(Here));
            Assert.AreEqual(1, map.BlockedCount);
            Assert.AreNotEqual(version, map.Version);
            Assert.AreEqual(0.45f, map.TopOf(Here), 1e-5f);
            Assert.IsFalse(map.IsBlocked(Here + Vector2Int.right));
            Assert.IsTrue(float.IsNaN(map.TopOf(Here + Vector2Int.right)));
        }

        [Test]
        public void FloorSeenWhereSomethingStood_FreesTheCell()
        {
            var map = new ObstacleMap();
            var time = See(map, 0.0, 14);
            Assert.IsTrue(map.IsBlocked(Here));

            // The chair was moved. One look at the floor is enough: newer evidence wins.
            Frame(map, time + 1.0, m => m.AddFloorSeen(Here));

            Assert.IsFalse(map.IsBlocked(Here));
            Assert.LessOrEqual(map.LogOddsOf(Here), 0f);
            Assert.AreEqual(0, map.BlockedCount);
            Assert.IsTrue(float.IsNaN(map.TopOf(Here)));
        }

        [Test]
        public void FloorSeenInTheSameMoment_DoesNotFreeTheCell()
        {
            // The leg of a chair: some pixels of the cell show the leg, others the floor beside it.
            var map = new ObstacleMap();
            for (var i = 0; i < 14; i++)
            {
                Frame(map, i * 0.125, m =>
                {
                    m.AddHit(Here, 0.4f);
                    m.AddFloorSeen(Here);
                });
            }

            Assert.IsTrue(map.IsBlocked(Here));
        }

        [Test]
        public void RaysThroughTheSpaceAboveTheCell_FreeItStepByStep()
        {
            var map = new ObstacleMap();
            var time = See(map, 0.0, 14);
            var before = map.LogOddsOf(Here);

            Frame(map, time, m => m.AddMiss(Here));

            Assert.AreEqual(before - ObstacleMap.MissWeight, map.LogOddsOf(Here), 1e-5f);
            Assert.IsTrue(map.IsBlocked(Here), "One ray is not enough.");

            for (var i = 1; i < 14; i++)
            {
                Frame(map, time + i * 0.125, m => m.AddMiss(Here));
            }

            Assert.IsFalse(map.IsBlocked(Here));
        }

        [Test]
        public void MoreHitsThanMisses_InOneFrame_IsAHit()
        {
            var map = new ObstacleMap();

            Frame(map, 0.0, m =>
            {
                m.AddHit(Here, 0.3f);
                m.AddHit(Here, 0.5f);
                m.AddMiss(Here);
            });

            Assert.AreEqual(ObstacleMap.HitWeight, map.LogOddsOf(Here), 1e-5f);
            Assert.AreEqual(0.5f, map.TopOf(Here), 1e-5f, "The highest point of the frame.");
        }

        [Test]
        public void Top_DoesNotJumpUp_ForOneFrame()
        {
            var map = new ObstacleMap();
            var time = See(map, 0.0, 6, 0.45f);

            // A flying pixel.
            Frame(map, time, m => m.AddHit(Here, 1.4f));
            Assert.AreEqual(0.45f, map.TopOf(Here), 1e-5f);

            // Something higher that stays.
            time = See(map, time + 0.125, 3, 0.9f);
            Assert.AreEqual(0.9f, map.TopOf(Here), 1e-5f);

            // Lower again: down step by step.
            See(map, time, 1, 0.45f);
            Assert.AreEqual(0.9f - ObstacleMap.TopSink, map.TopOf(Here), 1e-5f);
        }

        [Test]
        public void WeakCells_FadeWhenTheyAreNotSeenAgain()
        {
            var map = new ObstacleMap();
            See(map, 0.0, 1);
            Assert.AreEqual(ObstacleMap.HitWeight, map.LogOddsOf(Here), 1e-5f);

            map.DecayWeak();
            Assert.AreEqual(ObstacleMap.HitWeight, map.LogOddsOf(Here), 1e-5f, "Seen since the last redraw.");

            map.DecayWeak();
            Assert.AreEqual(ObstacleMap.HitWeight - ObstacleMap.WeakDecay, map.LogOddsOf(Here), 1e-5f);

            for (var i = 0; i < 5; i++)
            {
                map.DecayWeak();
            }

            Assert.AreEqual(0f, map.LogOddsOf(Here));
            Assert.IsTrue(map.IsEmpty, "Nothing is left to remember.");
        }

        [Test]
        public void BlockedCells_DoNotFade()
        {
            var map = new ObstacleMap();
            See(map, 0.0, 14);
            var before = map.LogOddsOf(Here);

            for (var i = 0; i < 20; i++)
            {
                map.DecayWeak();
            }

            Assert.AreEqual(before, map.LogOddsOf(Here));
            Assert.IsTrue(map.IsBlocked(Here));
        }

        [Test]
        public void FloorSeen_WhereNothingWasKnown_ChangesNothing()
        {
            var map = new ObstacleMap();

            Frame(map, 0.0, m => m.AddFloorSeen(Here));

            Assert.IsTrue(map.IsEmpty);
            Assert.IsFalse(map.MayHaveObstacles(Here));
        }

        [Test]
        public void MarkBlocked_KeepsTheFloorGridAwayFromTheObstacle()
        {
            var map = new ObstacleMap();
            See(map, 0.0, 14);
            var grid = new SurfaceGrid();
            grid.Reset(-10, -20, 40, 40);

            map.MarkBlocked(grid);

            var x = Here.x + 10;
            var z = Here.y + 20;
            Assert.IsTrue(grid.IsBlocked(grid.IndexOf(x, z)));
            Assert.IsTrue(grid.IsBlocked(grid.IndexOf(x + 1, z - 1)), "The cells around it, too.");
            Assert.IsFalse(grid.IsBlocked(grid.IndexOf(x + 2, z)));
        }

        [Test]
        public void Clear_ForgetsEverything()
        {
            var map = new ObstacleMap();
            See(map, 0.0, 14);

            map.Clear();

            Assert.IsTrue(map.IsEmpty);
            Assert.AreEqual(0, map.BlockedCount);
            Assert.IsFalse(map.IsBlocked(Here));
            Assert.IsFalse(map.TryGetBounds(out _, out _));
        }
    }

    public class ObstacleClustererTests
    {
        const float Cell = RoomSurfaceMap.CellSize;

        static ObstacleGrid Grid(int width = 80, int depth = 80)
        {
            var grid = new ObstacleGrid();
            grid.Reset(-10, -10, width, depth);
            return grid;
        }

        static void Block(ObstacleGrid grid, RectInt cells, float top, int level = ObstacleGrid.NoLevel)
        {
            for (var z = cells.yMin; z < cells.yMax; z++)
            {
                for (var x = cells.xMin; x < cells.xMax; x++)
                {
                    grid.Set(grid.IndexOf(x, z), top, 1f, level);
                }
            }
        }

        static List<ObstacleVolume> Cluster(ObstacleClusterer clusterer, ObstacleGrid grid, double time = 0.0,
            float floor = 0f, System.Func<int, float> levelHeight = null)
        {
            var volumes = new List<ObstacleVolume>();
            clusterer.Cluster(grid, levelHeight, floor, 0f, time, volumes);
            return volumes;
        }

        [Test]
        public void BlockOfCells_GetsOneBox_ThatStandsOnTheFloor()
        {
            var grid = Grid();
            Block(grid, new RectInt(20, 30, 8, 6), 0.5f);

            var volumes = Cluster(new ObstacleClusterer(), grid, floor: 0.02f);

            Assert.AreEqual(1, volumes.Count);
            var box = volumes[0];
            Assert.AreEqual(8 * Cell + 2f * ObstacleClusterer.Padding, box.Size.x, 1e-4f);
            Assert.AreEqual(6 * Cell + 2f * ObstacleClusterer.Padding, box.Size.z, 1e-4f);
            Assert.AreEqual(0.48f, box.Size.y, 1e-4f);
            Assert.AreEqual(0.02f, box.Pose.position.y, 1e-5f);
            Assert.AreEqual((-10 + 20 + 4) * Cell, box.Pose.position.x, 1e-4f);
            Assert.AreEqual((-10 + 30 + 3) * Cell, box.Pose.position.z, 1e-4f);
            Assert.AreEqual(ObstacleKind.Other, box.Kind);
            Assert.AreEqual(1f, box.Confidence, 1e-5f);
            Assert.AreEqual(0f, box.Age);
        }

        [Test]
        public void FewCells_AreNoise()
        {
            var grid = Grid();
            Block(grid, new RectInt(20, 30, 2, 3), 0.5f);

            Assert.AreEqual(0, Cluster(new ObstacleClusterer(), grid).Count);
        }

        [Test]
        public void CellsWithAGapOfOneCell_AreOneObstacle()
        {
            // The legs of a chair, as depth sees them: not every cell between them is blocked.
            var grid = Grid();
            Block(grid, new RectInt(20, 20, 3, 8), 0.45f);
            Block(grid, new RectInt(24, 20, 3, 8), 0.45f);

            var volumes = Cluster(new ObstacleClusterer(), grid);

            Assert.AreEqual(1, volumes.Count);
            Assert.AreEqual(7 * Cell + 2f * ObstacleClusterer.Padding, volumes[0].Size.x, 1e-4f);
        }

        [Test]
        public void ThingsApart_GetBoxesOfTheirOwn()
        {
            var grid = Grid();
            Block(grid, new RectInt(10, 10, 6, 6), 0.45f);
            Block(grid, new RectInt(30, 10, 6, 6), 0.9f);

            var volumes = Cluster(new ObstacleClusterer(), grid);

            Assert.AreEqual(2, volumes.Count);
            Assert.AreNotEqual(volumes[0].Id, volumes[1].Id);
        }

        [Test]
        public void LShape_IsCutInTwo()
        {
            var grid = Grid();
            Block(grid, new RectInt(10, 10, 32, 8), 0.75f);
            Block(grid, new RectInt(10, 18, 8, 24), 0.75f);

            var volumes = Cluster(new ObstacleClusterer(), grid);

            Assert.GreaterOrEqual(volumes.Count, 2, "One box around an L would cover the free space inside it.");
            var boxes = 0f;
            foreach (var volume in volumes)
            {
                boxes += volume.Size.x * volume.Size.z;
            }

            var cells = (32 * 8 + 8 * 24) * Cell * Cell;
            Assert.Less(boxes, cells * 1.5f);

            // The corner inside the L stays free.
            var inside = new Vector3((-10 + 30) * Cell, 0f, (-10 + 30) * Cell);
            foreach (var volume in volumes)
            {
                Assert.IsFalse(volume.ContainsXZ(inside), "Box " + volume.Id);
            }
        }

        [Test]
        public void Top_IgnoresAFewHighCells()
        {
            var grid = Grid();
            Block(grid, new RectInt(20, 20, 10, 10), 0.5f);
            Block(grid, new RectInt(20, 20, 2, 2), 1.6f);

            var volumes = Cluster(new ObstacleClusterer(), grid);

            Assert.AreEqual(0.5f, volumes[0].Size.y, 1e-4f);
        }

        [Test]
        public void ScannedSurface_GivesTheHeight_AndMakesItATableOrASeat()
        {
            var grid = Grid();
            Block(grid, new RectInt(10, 10, 32, 14), 0.78f, level: 4);
            Block(grid, new RectInt(50, 10, 8, 8), 0.47f, level: 5);
            System.Func<int, float> heights = id => id == 4 ? 0.75f : 0.45f;

            var volumes = Cluster(new ObstacleClusterer(), grid, levelHeight: heights);

            Assert.AreEqual(2, volumes.Count);
            var desk = volumes[0].Size.x > 1f ? volumes[0] : volumes[1];
            var chair = volumes[0].Size.x > 1f ? volumes[1] : volumes[0];
            Assert.AreEqual(ObstacleKind.Table, desk.Kind);
            Assert.AreEqual(0.75f, desk.Size.y, 1e-4f);
            Assert.AreEqual(ObstacleKind.Seat, chair.Kind);
            Assert.AreEqual(0.45f, chair.Size.y, 1e-4f);
        }

        [Test]
        public void TallLongAndThin_IsAWall()
        {
            var grid = Grid();
            Block(grid, new RectInt(5, 40, 60, 3), 1.9f);

            var volumes = Cluster(new ObstacleClusterer(), grid);

            Assert.AreEqual(1, volumes.Count);
            Assert.AreEqual(ObstacleKind.Wall, volumes[0].Kind);
        }

        [Test]
        public void BoxFoundAgain_KeepsItsNumber_AndGrowsOlder()
        {
            var clusterer = new ObstacleClusterer();
            var grid = Grid();
            Block(grid, new RectInt(20, 30, 8, 6), 0.5f);
            var first = Cluster(clusterer, grid, 10.0)[0];

            var again = Cluster(clusterer, grid, 14.5)[0];

            Assert.AreEqual(first.Id, again.Id);
            Assert.AreEqual(4.5f, again.Age, 1e-4f);
            Assert.AreEqual(first.Pose, again.Pose);
        }

        [Test]
        public void BoxThatMovedALittle_StaysWhereItWas()
        {
            var clusterer = new ObstacleClusterer();
            var grid = Grid();
            Block(grid, new RectInt(20, 30, 10, 10), 0.5f);
            var first = Cluster(clusterer, grid, 0.0)[0];

            // One more column of cells: the center moves by 2.5 cm, the size by 5 cm.
            grid = Grid();
            Block(grid, new RectInt(20, 30, 11, 10), 0.5f);
            var again = Cluster(clusterer, grid, 1.0)[0];

            Assert.AreEqual(first.Id, again.Id);
            Assert.AreEqual(first.Pose, again.Pose, "Boxes must not tremble.");
            Assert.AreEqual(first.Size, again.Size);

            // Three more: it is another size now.
            grid = Grid();
            Block(grid, new RectInt(20, 30, 14, 10), 0.5f);
            var larger = Cluster(clusterer, grid, 2.0)[0];

            Assert.AreEqual(first.Id, larger.Id);
            Assert.AreEqual(14 * Cell + 2f * ObstacleClusterer.Padding, larger.Size.x, 1e-4f);
        }

        [Test]
        public void BoxThatIsGone_IsForgotten()
        {
            var clusterer = new ObstacleClusterer();
            var grid = Grid();
            Block(grid, new RectInt(20, 30, 8, 6), 0.5f);
            var first = Cluster(clusterer, grid, 0.0)[0];

            Assert.AreEqual(0, Cluster(clusterer, Grid(), 1.0).Count);
            var back = Cluster(clusterer, grid, 2.0)[0];

            Assert.AreNotEqual(first.Id, back.Id);
            Assert.AreEqual(0f, back.Age);
        }

        [Test]
        public void ThingsLowerThanFiveCentimeters_GetNoBox()
        {
            var grid = Grid();
            Block(grid, new RectInt(20, 30, 8, 6), 0.03f);

            Assert.AreEqual(0, Cluster(new ObstacleClusterer(), grid).Count);
        }

        [Test]
        public void Mesh_HasFiveFacesAndTwelveEdgesPerBox()
        {
            var volume = new ObstacleVolume(1, new Pose(new Vector3(1f, 0.1f, 2f), Quaternion.Euler(0f, 30f, 0f)),
                new Vector3(0.6f, 0.5f, 0.4f), ObstacleKind.Other, 1f, 0f);
            var data = new ObstacleMeshData();

            ObstacleMeshBuilder.Build(new List<ObstacleVolume> { volume, volume }, data);

            Assert.AreEqual(2 * 5 * 4, data.FillVertices.Count);
            Assert.AreEqual(2 * 5 * 6, data.FillTriangles.Count);
            Assert.AreEqual(2 * 12 * 4 * 4, data.OutlineVertices.Count);
            var lowest = float.MaxValue;
            var highest = float.MinValue;
            foreach (var vertex in data.FillVertices)
            {
                lowest = Mathf.Min(lowest, vertex.y);
                highest = Mathf.Max(highest, vertex.y);
                Assert.IsTrue(volume.ContainsXZ(vertex, 1e-4f));
            }

            Assert.AreEqual(0.1f, lowest, 1e-5f);
            Assert.AreEqual(0.6f, highest, 1e-5f);
        }

        [Test]
        public void Mesh_FacesPointOutward()
        {
            var volume = new ObstacleVolume(1, new Pose(new Vector3(1f, 0f, 2f), Quaternion.Euler(0f, 30f, 0f)),
                new Vector3(0.6f, 0.5f, 0.4f), ObstacleKind.Other, 1f, 0f);
            var data = new ObstacleMeshData();
            ObstacleMeshBuilder.Build(new List<ObstacleVolume> { volume }, data);
            var middle = volume.Pose.position + Vector3.up * 0.25f;

            for (var i = 0; i < data.FillTriangles.Count; i += 3)
            {
                var a = data.FillVertices[data.FillTriangles[i]];
                var b = data.FillVertices[data.FillTriangles[i + 1]];
                var c = data.FillVertices[data.FillTriangles[i + 2]];

                // Unity: the normal of a triangle that runs clockwise points toward the viewer.
                var normal = Vector3.Cross(b - a, c - a);
                Assert.Greater(Vector3.Dot(normal, (a + b + c) / 3f - middle), 0f, "Triangle " + i / 3);
            }
        }
    }

    public class RoomSurfaceMapObstacleTests
    {
        static readonly Rect WholeFloor = new Rect(-3f, -3f, 6f, 6f);
        static readonly Rect SeenFloor = new Rect(-1f, -0.5f, 2f, 2f);
        static readonly Vector3 Camera = new Vector3(0f, 1.4f, -1f);

        static RoomSurfaceMap MapWithATrustedFloor()
        {
            var map = new RoomSurfaceMap();
            map.AddPrior(0f, Polygon(WholeFloor));
            Integrate(map, PointsOn(Camera, 0f, SeenFloor), FloorModel.TrustFrames + 2);
            Assert.AreEqual(FloorState.Trusted, map.FloorState);
            return map;
        }

        /// <summary>Points on the top and the front of a box, as depth sees something that stands on the floor.</summary>
        /// <param name="flatTop">False for something without a top to put things on (a plant, a heap of bags).</param>
        static ScanFrame PointsOnBox(Vector3 origin, Bounds box, bool flatTop = true, float step = 0.025f)
        {
            var frame = new ScanFrame();
            frame.Reset(origin, true);
            frame.Rotation = Quaternion.LookRotation(box.center - origin);
            for (var x = box.min.x + step * 0.5f; x < box.max.x; x += step)
            {
                for (var z = box.min.z + step * 0.5f; z < box.max.z; z += step)
                {
                    frame.Add(new Vector3(x, box.max.y, z), flatTop);
                }

                for (var y = box.min.y + step * 0.5f; y < box.max.y; y += step)
                {
                    frame.Add(new Vector3(x, y, box.min.z), false);
                }
            }

            return frame;
        }

        static Bounds Box(Vector3 min, Vector3 max)
        {
            var bounds = new Bounds();
            bounds.SetMinMax(min, max);
            return bounds;
        }

        [Test]
        public void SomethingOnTheFloor_BlocksItsCells_AndGetsABox()
        {
            var map = MapWithATrustedFloor();
            var bag = Box(new Vector3(0.2f, 0f, 0.4f), new Vector3(0.6f, 0.25f, 0.7f));

            Integrate(map, PointsOnBox(Camera, bag), 16);
            ShowAll(map);

            Assert.IsTrue(map.Obstacles.IsBlocked(Cell(0.4f, 0.55f)));
            Assert.AreEqual(1, map.Volumes.Count);
            var box = map.Volumes[0];
            Assert.IsTrue(box.ContainsXZ(bag.center));
            Assert.AreEqual(0.25f, box.Size.y, 0.03f);
            Assert.AreEqual(0.4f, box.Size.x, 0.12f);
            Assert.AreEqual(0.3f, box.Size.z, 0.12f);
            Assert.AreEqual(0f, box.Pose.position.y, 0.01f);
            Assert.IsFalse(map.Levels[0].IsShown(Cell(0.4f, 0.55f)), "The floor grid is not drawn under it.");
            Assert.IsTrue(map.Levels[0].IsShown(Cell(-0.5f, 0.5f)));
        }

        [Test]
        public void BeforeTheFloorIsTrusted_NothingIsAnObstacle()
        {
            var map = new RoomSurfaceMap();
            map.AddPrior(0f, Polygon(WholeFloor));
            var bag = Box(new Vector3(0.2f, 0f, 0.4f), new Vector3(0.6f, 0.25f, 0.7f));

            Integrate(map, PointsOnBox(Camera, bag), 8);
            ShowAll(map);

            Assert.AreNotEqual(FloorState.Trusted, map.FloorState);
            Assert.IsTrue(map.Obstacles.IsEmpty);
            Assert.AreEqual(0, map.Volumes.Count);
        }

        [Test]
        public void SwitchedOff_NothingIsAnObstacle()
        {
            var map = MapWithATrustedFloor();
            map.DetectObstacles = false;

            Integrate(map, PointsOnBox(Camera, Box(new Vector3(0.2f, 0f, 0.4f), new Vector3(0.6f, 0.25f, 0.7f))), 16);
            ShowAll(map);

            Assert.IsTrue(map.Obstacles.IsEmpty);
            Assert.AreEqual(0, map.Volumes.Count);
        }

        [Test]
        public void TheUsersOwnFeet_AreNoObstacle()
        {
            var map = MapWithATrustedFloor();
            var eye = new Vector3(0f, 1.4f, 0f);

            // The user looks down: shoes and legs, right under the phone.
            var feet = PointsOnBox(eye, Box(new Vector3(-0.15f, 0f, -0.1f), new Vector3(0.15f, 0.5f, 0.2f)),
                flatTop: false);
            feet.Rotation = Quaternion.LookRotation(new Vector3(0f, -1f, 0.2f));
            Integrate(map, feet, 16);
            ShowAll(map);

            Assert.AreEqual(0, map.Obstacles.BlockedCount);
            Assert.AreEqual(0, map.Volumes.Count);
        }

        [Test]
        public void NoiseFarAway_MakesNoLowObstacle()
        {
            var map = MapWithATrustedFloor();

            // The floor 3.3 m away, measured 12 cm too high in places.
            var far = new ScanFrame();
            far.Reset(Camera, true);
            far.Rotation = Quaternion.LookRotation(new Vector3(0f, -0.4f, 1f));
            for (var x = -0.5f; x < 0.5f; x += 0.025f)
            {
                for (var z = 1.9f; z < 2.1f; z += 0.025f)
                {
                    far.Add(new Vector3(x, 0.12f, z), false);
                }
            }

            Integrate(map, far, 16);

            Assert.AreEqual(0, map.Obstacles.BlockedCount);
        }

        [Test]
        public void Raycast_BehindAnObstacle_IsRefused()
        {
            var map = MapWithATrustedFloor();
            var builder = new RoomModelBuilder();
            var cabinet = Box(new Vector3(-0.3f, 0f, 0.3f), new Vector3(0.3f, 0.9f, 0.6f));
            Integrate(map, PointsOnBox(Camera, cabinet), 16);
            ShowAll(map, builder);
            Integrate(map, PointsOnBox(Camera, cabinet), 30);
            ShowAll(map, builder);
            Assert.AreEqual(1, map.Volumes.Count);
            Assert.GreaterOrEqual(map.Volumes[0].Age, RoomSurfaceMap.RefusalAge);

            // From the camera through the cabinet to the floor behind it.
            var behind = new Vector3(0f, 0f, 1.2f);
            var through = new Ray(Camera, (behind - Camera).normalized);
            Assert.IsTrue(map.Raycast(through, 10f, out var pose, out _, out var blocked), "The floor is there...");
            Assert.IsTrue(blocked, "...but the cabinet stands in the way.");
            Assert.AreEqual(1.2f, pose.position.z, 0.02f);
            Assert.IsFalse(map.Raycast(through, 10f, out _), "Placement finds no spot there.");

            // Beside the cabinet the floor is free.
            var beside = new Ray(Camera, (new Vector3(-0.8f, 0f, 0.5f) - Camera).normalized);
            Assert.IsTrue(map.Raycast(beside, 10f, out _, out _, out blocked));
            Assert.IsFalse(blocked);
        }

        [Test]
        public void Raycast_OnTheFloorWhereSomethingStands_IsBlocked()
        {
            var map = MapWithATrustedFloor();
            var plant = Box(new Vector3(0.2f, 0f, 0.4f), new Vector3(0.6f, 0.6f, 0.7f));
            Integrate(map, PointsOnBox(Camera, plant, flatTop: false), 16);
            ShowAll(map);
            Assert.AreEqual(1, map.Levels.Count, "Nothing to put things on.");

            var down = new Ray(new Vector3(0.4f, 1.5f, 0.55f), Vector3.down);

            Assert.IsFalse(map.Raycast(down, 10f, out _, out _, out var blocked), "No floor is drawn there.");
            Assert.IsTrue(blocked, "The user is told that something stands there.");
        }

        [Test]
        public void Raycast_NewBox_DoesNotRefuseYet()
        {
            var map = MapWithATrustedFloor();
            var cabinet = Box(new Vector3(-0.3f, 0f, 0.3f), new Vector3(0.3f, 0.9f, 0.6f));
            Integrate(map, PointsOnBox(Camera, cabinet), 16);
            ShowAll(map);
            Assert.AreEqual(1, map.Volumes.Count);

            var through = new Ray(Camera, (new Vector3(0f, 0f, 1.2f) - Camera).normalized);
            Assert.IsTrue(map.Raycast(through, 10f, out _, out _, out var blocked));

            Assert.IsFalse(blocked, "A box that was found a moment ago may be gone in the next.");
        }

        [Test]
        public void Raycast_OnATableTop_IsNotRefused_OnASeatItIs()
        {
            var map = MapWithATrustedFloor();
            var builder = new RoomModelBuilder();
            map.AddPrior(0.75f, Polygon(new Rect(0.2f, 0.4f, 1.0f, 0.6f)));
            var desk = PointsOn(new Vector3(0.7f, 1.5f, -0.4f), 0.75f, new Rect(0.2f, 0.4f, 1.0f, 0.6f));
            var seat = PointsOn(new Vector3(-0.6f, 1.4f, -0.4f), 0.45f, new Rect(-0.8f, 0.4f, 0.4f, 0.4f));
            for (var i = 0; i < 16; i++)
            {
                map.Integrate(desk);
                map.Integrate(seat);
            }

            ShowAll(map, builder);
            for (var i = 0; i < 16; i++)
            {
                map.Integrate(desk);
                map.Integrate(seat);
            }

            ShowAll(map, builder);
            Assert.AreEqual(2, map.Volumes.Count);

            var ontoTheDesk = new Ray(new Vector3(0.7f, 1.5f, -0.4f), (new Vector3(0.7f, 0.75f, 0.7f)
                - new Vector3(0.7f, 1.5f, -0.4f)).normalized);
            Assert.IsTrue(map.Raycast(ontoTheDesk, 10f, out var onDesk, out _, out var blocked));
            Assert.IsFalse(blocked, "Things are put on tables.");
            Assert.AreEqual(0.75f, onDesk.position.y, 0.02f);

            var ontoTheSeat = new Ray(new Vector3(-0.6f, 1.4f, -0.4f), (new Vector3(-0.6f, 0.45f, 0.6f)
                - new Vector3(-0.6f, 1.4f, -0.4f)).normalized);
            Assert.IsTrue(map.Raycast(ontoTheSeat, 10f, out var onSeat, out _, out blocked));
            Assert.AreEqual(0.45f, onSeat.position.y, 0.03f);
            Assert.IsTrue(blocked, "A seat is for sitting.");

            var kinds = new List<ObstacleKind> { map.Volumes[0].Kind, map.Volumes[1].Kind };
            CollectionAssert.AreEquivalent(new[] { ObstacleKind.Table, ObstacleKind.Seat }, kinds);
        }

        [Test]
        public void Podium_IsATier_NotAnObstacle()
        {
            var map = MapWithATrustedFloor();
            var top = new Rect(-1f, 0.6f, 2f, 1f);
            map.AddPrior(0.3f, Polygon(top));

            Integrate(map, PointsOn(Camera, 0.3f, top), 20);
            ShowAll(map);
            Integrate(map, PointsOn(Camera, 0.3f, top), 4);
            ShowAll(map);

            var podium = map.Levels[1];
            Assert.AreEqual(0.3f, podium.Height, 0.01f);
            Assert.IsTrue(map.IsTier(podium));
            Assert.AreEqual(0, map.Volumes.Count, "A podium is a floor to walk on.");
            Assert.IsTrue(map.Raycast(new Ray(new Vector3(0f, 1.5f, 1f), Vector3.down), 10f, out var pose, out _,
                out var blocked));
            Assert.IsFalse(blocked);
            Assert.AreEqual(0.3f, pose.position.y, 0.02f);
        }

        [Test]
        public void AnotherFloor_ForgetsWhatStoodOnTheOldOne()
        {
            var map = MapWithATrustedFloor();
            Integrate(map, PointsOnBox(Camera, Box(new Vector3(0.2f, 0f, 0.4f), new Vector3(0.6f, 0.25f, 0.7f))), 16);
            Assert.Greater(map.Obstacles.BlockedCount, 0);

            map.Clear();

            Assert.IsTrue(map.Obstacles.IsEmpty);
            Assert.AreEqual(0, map.Volumes.Count);
        }
    }
}
