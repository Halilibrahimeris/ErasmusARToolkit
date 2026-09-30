using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using static Erasmus.ARToolkit.Tests.RoomSurfaceMapTests;

namespace Erasmus.ARToolkit.Tests
{
    public class WallModelTests
    {
        /// <summary>The outline of a vertical plane: from a to b on the floor, between two heights.</summary>
        static List<Vector3> Plane(Vector2 a, Vector2 b, float bottom, float top)
        {
            return new List<Vector3>
            {
                new Vector3(a.x, bottom, a.y), new Vector3(b.x, bottom, b.y),
                new Vector3(b.x, top, b.y), new Vector3(a.x, top, a.y),
            };
        }

        static Vector3 NormalOf(Vector2 a, Vector2 b)
        {
            var along = (b - a).normalized;
            return new Vector3(along.y, 0f, -along.x);
        }

        static void AddPlane(WallModel walls, Vector2 a, Vector2 b, float bottom = 0.3f, float top = 2.1f)
        {
            walls.AddPlane(Plane(a, b, bottom, top), NormalOf(a, b));
        }

        static void AssertEnds(WallSegment wall, Vector2 a, Vector2 b, float tolerance = 1e-3f)
        {
            var sameWay = Vector2.Distance(wall.A, a) + Vector2.Distance(wall.B, b);
            var otherWay = Vector2.Distance(wall.A, b) + Vector2.Distance(wall.B, a);
            Assert.LessOrEqual(Mathf.Min(sameWay, otherWay), tolerance,
                $"The wall runs from {wall.A} to {wall.B}, not from {a} to {b}.");
        }

        [Test]
        public void VerticalPlane_BecomesAWall_ThatStandsOnTheFloor()
        {
            var walls = new WallModel();
            AddPlane(walls, new Vector2(-1f, 3f), new Vector2(2f, 3f), 0.4f, 2.2f);

            Assert.IsTrue(walls.Rebuild(0.02f, float.NaN));

            Assert.AreEqual(1, walls.Segments.Count);
            var wall = walls.Segments[0];
            AssertEnds(wall, new Vector2(-1f, 3f), new Vector2(2f, 3f));
            Assert.AreEqual(0.02f, wall.Bottom, 1e-5f, "A wall goes down to the floor, where furniture hid it.");
            Assert.AreEqual(2.2f, wall.Top, 1e-5f);
            Assert.AreEqual(3f, wall.Length, 1e-3f);
            Assert.IsTrue(wall.FromPlane);
            Assert.IsFalse(walls.Rebuild(0.02f, float.NaN), "Nothing changed.");
        }

        [Test]
        public void LowOrShortPlanes_AreNoWalls()
        {
            var walls = new WallModel();
            AddPlane(walls, new Vector2(0f, 0f), new Vector2(2f, 0f), 0.1f, 0.8f);      // the front of a cabinet
            AddPlane(walls, new Vector2(0f, 1f), new Vector2(0.4f, 1f), 0.1f, 2.1f);    // a pillar or a door frame
            AddPlane(walls, new Vector2(0f, 2f), new Vector2(2f, 2f), 1.2f, 1.9f);      // a wall above a row of desks

            walls.Rebuild(0f, float.NaN);

            Assert.AreEqual(1, walls.Segments.Count);
            Assert.AreEqual(2f, walls.Segments[0].A.y, 1e-3f);
        }

        [Test]
        public void PiecesOnOneLine_AreOneWall()
        {
            var walls = new WallModel();
            AddPlane(walls, new Vector2(0f, 0f), new Vector2(1.5f, 0.02f));
            AddPlane(walls, new Vector2(1.8f, 0.05f), new Vector2(3f, 0.04f), 0.2f, 2.4f);

            walls.Rebuild(0f, float.NaN);

            Assert.AreEqual(1, walls.Segments.Count);
            var wall = walls.Segments[0];
            Assert.AreEqual(3f, wall.Length, 0.02f);
            Assert.AreEqual(2.4f, wall.Top, 1e-5f);
            Assert.LessOrEqual(wall.DistanceTo(new Vector2(1.6f, 0.03f)), 0.03f);
        }

        [Test]
        public void PiecesApart_StayApart()
        {
            var walls = new WallModel();
            AddPlane(walls, new Vector2(0f, 0f), new Vector2(1.5f, 0f));
            AddPlane(walls, new Vector2(2.3f, 0f), new Vector2(4f, 0f));        // a door of 80 cm between them
            AddPlane(walls, new Vector2(0f, 0.4f), new Vector2(1.5f, 0.4f));    // another wall, 40 cm behind
            AddPlane(walls, new Vector2(0f, 3f), new Vector2(1f, 3.4f));        // turned by 22 degrees

            walls.Rebuild(0f, float.NaN);

            Assert.AreEqual(4, walls.Segments.Count);
        }

        [Test]
        public void Walls_AreLaidAlongTheRoom()
        {
            var walls = new WallModel();
            MinAreaRectangle.AxesOf(30f, out var axisX, out _);
            var a = new Vector2(1f, 1f);
            var b = a + (Vector2)(Quaternion.Euler(0f, 0f, -4f) * axisX) * 3f;
            AddPlane(walls, a, b);

            walls.Rebuild(0f, 30f);

            var wall = walls.Segments[0];
            Assert.AreEqual(0f, RoomAxes.Difference(RoomAxes.YawOf(wall.B - wall.A), 30f), 0.01f);
            Assert.AreEqual(3f, wall.Length, 1e-3f);
            Assert.LessOrEqual(Vector2.Distance((wall.A + wall.B) * 0.5f, (a + b) * 0.5f), 1e-3f, "Turned about its middle.");
        }

        [Test]
        public void SlantedWall_StaysAsItIs()
        {
            var walls = new WallModel();
            AddPlane(walls, new Vector2(0f, 0f), new Vector2(2f, 2f));

            walls.Rebuild(0f, 0f);

            AssertEnds(walls.Segments[0], new Vector2(0f, 0f), new Vector2(2f, 2f));
        }

        [Test]
        public void WallsThatNearlyMeet_MeetInACorner()
        {
            var walls = new WallModel();
            AddPlane(walls, new Vector2(0f, 0f), new Vector2(2.8f, 0f));
            AddPlane(walls, new Vector2(3f, 0.3f), new Vector2(3f, 2f));

            walls.Rebuild(0f, 0f);

            Assert.AreEqual(2, walls.Segments.Count);
            AssertEnds(walls.Segments[0], new Vector2(0f, 0f), new Vector2(3f, 0f));
            AssertEnds(walls.Segments[1], new Vector2(3f, 0f), new Vector2(3f, 2f));
        }

        [Test]
        public void WallsFarFromEachOther_GetNoCorner()
        {
            var walls = new WallModel();
            AddPlane(walls, new Vector2(0f, 0f), new Vector2(2f, 0f));
            AddPlane(walls, new Vector2(3f, 1f), new Vector2(3f, 3f));

            walls.Rebuild(0f, 0f);

            AssertEnds(walls.Segments[0], new Vector2(0f, 0f), new Vector2(2f, 0f));
            AssertEnds(walls.Segments[1], new Vector2(3f, 1f), new Vector2(3f, 3f));
        }

        [Test]
        public void TallThinObstacle_IsAWall_OtherObstaclesAreNot()
        {
            var walls = new WallModel();
            var volumes = new List<ObstacleVolume>
            {
                new ObstacleVolume(1, new Pose(new Vector3(1f, 0f, 2f), Quaternion.Euler(0f, 90f, 0f)),
                    new Vector3(3f, 1.9f, 0.15f), ObstacleKind.Wall, 0.8f, 5f),
                new ObstacleVolume(2, new Pose(new Vector3(0f, 0f, 0f), Quaternion.identity),
                    new Vector3(1.6f, 0.75f, 0.7f), ObstacleKind.Table, 1f, 5f),
            };

            walls.SetObstacleWalls(volumes);
            walls.Rebuild(0f, float.NaN);

            Assert.AreEqual(1, walls.Segments.Count);
            var wall = walls.Segments[0];
            Assert.IsFalse(wall.FromPlane);
            AssertEnds(wall, new Vector2(1f, 0.5f), new Vector2(1f, 3.5f));
            Assert.AreEqual(1.9f, wall.Top, 1e-4f);
            Assert.AreEqual(0.8f, wall.Confidence, 1e-5f);
        }

        [Test]
        public void WallThatIsMissedForAMoment_Stays()
        {
            var walls = new WallModel();
            var wall = new List<ObstacleVolume>
            {
                new ObstacleVolume(1, new Pose(new Vector3(1f, 0f, 2f), Quaternion.identity),
                    new Vector3(3f, 1.9f, 0.15f), ObstacleKind.Wall, 0.8f, 5f),
            };
            var nothing = new List<ObstacleVolume>();
            walls.SetObstacleWalls(wall);
            walls.Rebuild(0f, float.NaN);

            for (var i = 0; i < WallModel.KeepRedraws; i++)
            {
                walls.SetObstacleWalls(nothing);
                Assert.IsFalse(walls.Rebuild(0f, float.NaN), "Redraw " + i);
                Assert.AreEqual(1, walls.Segments.Count);
            }

            walls.SetObstacleWalls(wall);
            walls.Rebuild(0f, float.NaN);
            Assert.AreEqual(1, walls.Segments.Count, "Found again: still one wall.");

            for (var i = 0; i <= WallModel.KeepRedraws; i++)
            {
                walls.SetObstacleWalls(nothing);
                walls.Rebuild(0f, float.NaN);
            }

            Assert.AreEqual(0, walls.Segments.Count, "Gone for good.");
        }

        [Test]
        public void PlaneAndObstacle_OfTheSameWall_AreOneWall()
        {
            var walls = new WallModel();
            AddPlane(walls, new Vector2(0f, 3f), new Vector2(2f, 3f));
            walls.SetObstacleWalls(new List<ObstacleVolume>
            {
                new ObstacleVolume(1, new Pose(new Vector3(2.8f, 0f, 3.05f), Quaternion.identity),
                    new Vector3(2f, 1.8f, 0.15f), ObstacleKind.Wall, 0.7f, 5f),
            });

            walls.Rebuild(0f, 0f);

            Assert.AreEqual(1, walls.Segments.Count);
            Assert.AreEqual(3.8f, walls.Segments[0].Length, 0.02f);
            Assert.IsTrue(walls.Segments[0].FromPlane);
        }

        [Test]
        public void NewReadingOfThePlanes_ReplacesTheOldOne()
        {
            var walls = new WallModel();
            walls.BeginPlanes();
            AddPlane(walls, new Vector2(0f, 0f), new Vector2(2f, 0f));
            AddPlane(walls, new Vector2(0f, 3f), new Vector2(2f, 3f));
            walls.EndPlanes();
            walls.Rebuild(0f, float.NaN);
            Assert.AreEqual(2, walls.Segments.Count);

            walls.BeginPlanes();
            AddPlane(walls, new Vector2(0f, 3f), new Vector2(2.5f, 3f));
            walls.Rebuild(0f, float.NaN);
            Assert.AreEqual(2, walls.Segments.Count, "While the planes are read, the walls stay as they are.");
            walls.EndPlanes();

            Assert.IsTrue(walls.Rebuild(0f, float.NaN));
            Assert.AreEqual(1, walls.Segments.Count);
            Assert.AreEqual(2.5f, walls.Segments[0].Length, 1e-3f);
        }

        [Test]
        public void TheRoom_LiesOnTheSideTheCameraWasOn()
        {
            var walls = new WallModel();
            var wall = new WallSegment(new Vector2(0f, 2f), new Vector2(3f, 2f), 0f, 2f, 1f, true);
            Assert.IsFalse(walls.TryGetInside(wall, out _), "No camera was near the wall.");

            for (var i = 0; i < 10; i++)
            {
                walls.NoteCamera(new Vector2(0.3f * i, 0.5f));
            }

            // The wall runs along +x, so its normal points to +z: the cameras are on the other side.
            Assert.IsTrue(walls.TryGetInside(wall, out var side));
            Assert.AreEqual(-1, side);

            walls.NoteCamera(new Vector2(1f, 3f));
            Assert.IsTrue(walls.TryGetInside(wall, out side), "One camera of eleven: a mistake of tracking.");
            Assert.AreEqual(-1, side);

            for (var i = 0; i < 4; i++)
            {
                walls.NoteCamera(new Vector2(0.4f * i, 3.5f));
            }

            Assert.IsFalse(walls.TryGetInside(wall, out _), "The user walked on both sides: two rooms.");
        }

        [Test]
        public void FloorBehindAWall_ThatDepthNeverSaw_IsBlocked()
        {
            var walls = new WallModel();
            AddPlane(walls, new Vector2(0f, 2f), new Vector2(3f, 2f));
            walls.Rebuild(0f, float.NaN);
            walls.NoteCamera(new Vector2(1f, 0f));
            walls.NoteCamera(new Vector2(2f, 0.5f));
            var grid = new SurfaceGrid();
            grid.Reset(-20, -20, 120, 140);
            var seen = grid.IndexOf(Cell(1f, 2.5f).x + 20, Cell(1f, 2.5f).y + 20);
            grid.SetCell(Cell(1f, 2.5f).x + 20, Cell(1f, 2.5f).y + 20, 0.8f, true);

            walls.MarkBlocked(grid);

            bool IsBlocked(float x, float z) => grid.IsBlocked(grid.IndexOf(Cell(x, z).x + 20, Cell(x, z).y + 20));
            Assert.IsTrue(IsBlocked(1.5f, 2.6f), "Behind the wall.");
            Assert.IsTrue(IsBlocked(0.1f, 4.5f));
            Assert.IsFalse(grid.IsBlocked(seen), "Depth saw floor there: through a door, or a window.");
            Assert.IsFalse(IsBlocked(1.5f, 1.5f), "In the room.");
            Assert.IsFalse(IsBlocked(3.5f, 2.6f), "Beside the wall: the scan does not know what is there.");
            Assert.IsFalse(IsBlocked(1.5f, 2f + WallModel.ShadowDepth + 0.3f));
        }

        [Test]
        public void Blocks_TheCellsTheWallStandsOn()
        {
            var walls = new WallModel();
            AddPlane(walls, new Vector2(0f, 1.02f), new Vector2(2f, 1.02f));
            walls.Rebuild(0f, float.NaN);

            Assert.IsTrue(walls.Blocks(Cell(1f, 1.02f)));
            Assert.IsTrue(walls.Blocks(Cell(0.02f, 1.04f)));
            Assert.IsFalse(walls.Blocks(Cell(1f, 1.2f)));
            Assert.IsFalse(walls.Blocks(Cell(2.3f, 1.02f)), "Past its end.");

            var grid = new SurfaceGrid();
            grid.Reset(-10, -10, 80, 60);
            walls.MarkBlocked(grid);
            var blocked = 0;
            for (var i = 0; i < grid.CellCount; i++)
            {
                Assert.AreEqual(grid.IsBlocked(i), grid.IsWall(i));
                blocked += grid.IsBlocked(i) ? 1 : 0;
            }

            Assert.GreaterOrEqual(blocked, 40, "A row of cells, 2 m long.");
            Assert.LessOrEqual(blocked, 3 * 42);
            Assert.IsTrue(grid.IsWall(grid.IndexOf(Cell(1f, 1.02f).x + 10, Cell(1f, 1.02f).y + 10)));
        }

        [Test]
        public void Raycast_GoesThroughTheWall_BetweenItsEndsAndHeights()
        {
            var wall = new WallSegment(new Vector2(0f, 2f), new Vector2(3f, 2f), 0f, 2.2f, 1f, true);

            Assert.IsTrue(wall.Raycast(new Ray(new Vector3(1f, 1.4f, 0f), Vector3.forward), out var distance));
            Assert.AreEqual(2f, distance, 1e-4f);
            Assert.IsTrue(wall.Raycast(new Ray(new Vector3(1f, 1.4f, 4f), Vector3.back), out distance), "From the other side.");
            Assert.AreEqual(2f, distance, 1e-4f);

            Assert.IsFalse(wall.Raycast(new Ray(new Vector3(4f, 1.4f, 0f), Vector3.forward), out _), "Beside it.");
            Assert.IsFalse(wall.Raycast(new Ray(new Vector3(1f, 3f, 0f), Vector3.forward), out _), "Above it.");
            Assert.IsFalse(wall.Raycast(new Ray(new Vector3(1f, 1.4f, 0f), Vector3.back), out _), "Away from it.");
            Assert.IsFalse(wall.Raycast(new Ray(new Vector3(1f, 1.4f, 0f), Vector3.right), out _), "Along it.");
            Assert.IsFalse(wall.Raycast(new Ray(new Vector3(1f, 1.4f, 1f), Vector3.down), out _), "Down to the floor in front of it.");
        }

        [Test]
        public void Mesh_IsAPanelFromTheFloorUp_WithBars()
        {
            var data = new ObstacleMeshData();
            var walls = new List<WallSegment>
            {
                new WallSegment(new Vector2(0f, 2f), new Vector2(3f, 2f), 0.1f, 3.1f, 1f, true),
            };

            WallMeshBuilder.Build(walls, data);

            Assert.AreEqual(4, data.FillVertices.Count);
            Assert.AreEqual(4 * 4 * 4, data.OutlineVertices.Count, "Four bars.");
            var highest = float.MinValue;
            var lowest = float.MaxValue;
            foreach (var vertex in data.FillVertices)
            {
                highest = Mathf.Max(highest, vertex.y);
                lowest = Mathf.Min(lowest, vertex.y);
                Assert.AreEqual(2f, vertex.z, 1e-5f);
            }

            Assert.AreEqual(0.1f, lowest, 1e-5f);
            Assert.AreEqual(0.1f + WallModel.MaxDrawnHeight, highest, 1e-5f, "A wall is drawn as high as a person can reach.");
        }

        [Test]
        public void Clear_ForgetsTheWalls()
        {
            var walls = new WallModel();
            AddPlane(walls, new Vector2(0f, 0f), new Vector2(2f, 0f));
            walls.Rebuild(0f, float.NaN);

            walls.Clear();

            Assert.AreEqual(0, walls.Segments.Count);
            Assert.IsTrue(walls.Changed);
            Assert.IsFalse(walls.Rebuild(0f, float.NaN));
            Assert.IsFalse(walls.Blocks(Cell(1f, 0f)));
        }
    }

    public class PlacementOnPlanesTests
    {
        [Test]
        public void FirstHorizontal_SkipsTheWalls()
        {
            var isHorizontal = new System.Func<bool, bool>(value => value);

            Assert.AreEqual(0, ARPlacementSurface.FirstHorizontal(new List<bool> { true, false }, isHorizontal));
            Assert.AreEqual(2, ARPlacementSurface.FirstHorizontal(new List<bool> { false, false, true }, isHorizontal));
            Assert.AreEqual(-1, ARPlacementSurface.FirstHorizontal(new List<bool> { false }, isHorizontal));
            Assert.AreEqual(-1, ARPlacementSurface.FirstHorizontal(new List<bool>(), isHorizontal));
        }
    }

    public class RoomSurfaceMapWallTests
    {
        static readonly Rect WholeFloor = new Rect(-3f, -3f, 6f, 6f);
        static readonly Vector3 Camera = new Vector3(0f, 1.4f, -1f);

        static RoomSurfaceMap MapWithATrustedFloor()
        {
            var map = new RoomSurfaceMap();
            map.AddPrior(0f, Polygon(WholeFloor));
            Integrate(map, PointsOn(Camera, 0f, new Rect(-1f, -0.5f, 2f, 2f)), FloorModel.TrustFrames + 2);
            Assert.AreEqual(FloorState.Trusted, map.FloorState);
            return map;
        }

        static void AddWall(RoomSurfaceMap map, Vector2 a, Vector2 b)
        {
            var along = (b - a).normalized;
            map.Walls.AddPlane(new List<Vector3>
            {
                new Vector3(a.x, 0.2f, a.y), new Vector3(b.x, 0.2f, b.y),
                new Vector3(b.x, 2.2f, b.y), new Vector3(a.x, 2.2f, a.y),
            }, new Vector3(along.y, 0f, -along.x));
        }

        [Test]
        public void Raycast_ThroughAWall_IsRefused()
        {
            var map = MapWithATrustedFloor();
            var builder = new RoomModelBuilder();
            AddWall(map, new Vector2(-3f, 2f), new Vector2(3f, 2f));
            ShowAll(map, builder);
            ShowAll(map, builder);
            Assert.AreEqual(1, map.Walls.Segments.Count);

            // The plane of the floor goes on behind the wall, as ARCore has it. Nothing is drawn there,
            // and the user is told why.
            var beyond = new Ray(Camera, (new Vector3(0f, 0f, 2.6f) - Camera).normalized);
            Assert.IsFalse(map.Raycast(beyond, 10f, out var pose, out _, out var blocked, out var behindWall));
            Assert.IsTrue(blocked);
            Assert.IsTrue(behindWall);
            Assert.AreEqual(2.6f, pose.position.z, 0.01f);
            Assert.IsFalse(map.Raycast(beyond, 10f, out _));

            var inTheRoom = new Ray(Camera, (new Vector3(0f, 0f, 1f) - Camera).normalized);
            Assert.IsTrue(map.Raycast(inTheRoom, 10f, out _, out _, out blocked, out behindWall));
            Assert.IsFalse(blocked);
            Assert.IsFalse(behindWall);
        }

        [Test]
        public void Floor_EndsAtTheWall()
        {
            var map = MapWithATrustedFloor();
            var builder = new RoomModelBuilder();
            AddWall(map, new Vector2(-3.2f, 2f), new Vector2(3.2f, 2f));
            ShowAll(map, builder);
            ShowAll(map, builder);

            var floor = map.Floor.Level;
            Assert.IsTrue(floor.IsShown(Cell(0f, 1f)));
            Assert.IsFalse(floor.IsShown(Cell(0f, 2.01f)), "Where the wall stands.");
            Assert.IsFalse(floor.IsShown(Cell(0f, 2.5f)),
                "Behind the wall the camera never saw a floor: what ARCore claims there is not drawn.");
        }

        [Test]
        public void FloorBehindAWall_ThatTheCameraSaw_Stays()
        {
            // A wall with a door: the next room was scanned through it.
            var map = MapWithATrustedFloor();
            var builder = new RoomModelBuilder();
            Integrate(map, PointsOn(new Vector3(0f, 1.4f, 1.2f), 0f, new Rect(-1f, 2.2f, 2f, 0.7f)), 4);
            AddWall(map, new Vector2(-3.2f, 2f), new Vector2(3.2f, 2f));
            ShowAll(map, builder);
            ShowAll(map, builder);

            Assert.IsTrue(map.Floor.Level.IsShown(Cell(0f, 2.5f)));

            // It is floor, but it lies behind the wall: what is placed there would stand in the next room.
            var beyond = new Ray(Camera, (new Vector3(0f, 0f, 2.5f) - Camera).normalized);
            Assert.IsTrue(map.Raycast(beyond, 10f, out _, out _, out var blocked, out var behindWall));
            Assert.IsTrue(blocked);
            Assert.IsTrue(behindWall);
        }

        [Test]
        public void WithoutAFloor_ThereAreNoWalls()
        {
            var map = new RoomSurfaceMap();
            var builder = new RoomModelBuilder();
            AddWall(map, new Vector2(-3f, 2f), new Vector2(3f, 2f));

            ShowAll(map, builder);

            Assert.AreEqual(0, map.Walls.Segments.Count, "Walls stand on the floor.");
        }

        [Test]
        public void SwitchedOff_WallsAreNeitherFoundNorInTheWay()
        {
            var map = MapWithATrustedFloor();
            map.DetectWalls = false;
            var builder = new RoomModelBuilder();
            AddWall(map, new Vector2(-3f, 2f), new Vector2(3f, 2f));
            ShowAll(map, builder);
            ShowAll(map, builder);

            Assert.AreEqual(0, map.Walls.Segments.Count);
            var beyond = new Ray(Camera, (new Vector3(0f, 0f, 2.6f) - Camera).normalized);
            Assert.IsTrue(map.Raycast(beyond, 10f, out _, out _, out var blocked, out _));
            Assert.IsFalse(blocked);
        }

        [Test]
        public void Walls_GiveTheRoomItsDirection()
        {
            var map = new RoomSurfaceMap();
            MinAreaRectangle.AxesOf(40f, out var axisX, out _);
            AddWall(map, axisX * -1f, axisX * 1f);
            var axes = new RoomAxes();

            // The edges of a table: 5 m in all, along another direction.
            axes.AddLoop(Polygon(new Rect(0f, 0f, 1.6f, 0.9f)));
            map.Walls.AddEdgesTo(axes, RoomModelBuilder.WallWeight);
            axes.Update();

            Assert.AreEqual(40f, axes.Yaw, 0.1f, "2 m of wall count as much as 6 m of other edges.");
        }
    }
}
