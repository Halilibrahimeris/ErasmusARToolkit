using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Erasmus.ARToolkit.Tests
{
    /// <summary>The meshes of the depth correction and of the occluders, and the patches under placed objects.</summary>
    public class SteadyDepthTests
    {
        const float Cell = RoomSurfaceMap.CellSize;
        const float Seen = 0.8f;

        static SurfaceGrid Grid(int width, int depth, float priorWeight = SurfaceEvidence.PriorWeight)
        {
            var grid = new SurfaceGrid();
            grid.Reset(-4, 6, width, depth, priorWeight);
            return grid;
        }

        static void Fill(SurfaceGrid grid, RectInt cells, float logOdds, bool prior = false)
        {
            for (var z = cells.yMin; z < cells.yMax; z++)
            {
                for (var x = cells.xMin; x < cells.xMax; x++)
                {
                    grid.SetCell(x, z, logOdds, prior);
                }
            }
        }

        /// <summary>The point lies inside a triangle of the mesh, seen from above.</summary>
        static bool Covers(List<Vector3> vertices, List<int> triangles, float x, float z)
        {
            for (var i = 0; i < triangles.Count; i += 3)
            {
                var a = vertices[triangles[i]];
                var b = vertices[triangles[i + 1]];
                var c = vertices[triangles[i + 2]];
                if (Mathf.Abs(a.y) > 1e-4f || Mathf.Abs(b.y) > 1e-4f || Mathf.Abs(c.y) > 1e-4f)
                {
                    continue;
                }

                var d1 = (x - b.x) * (a.z - b.z) - (a.x - b.x) * (z - b.z);
                var d2 = (x - c.x) * (b.z - c.z) - (b.x - c.x) * (z - c.z);
                var d3 = (x - a.x) * (c.z - a.z) - (c.x - a.x) * (z - a.z);
                var negative = d1 < 0f || d2 < 0f || d3 < 0f;
                var positive = d1 > 0f || d2 > 0f || d3 > 0f;
                if (!(negative && positive))
                {
                    return true;
                }
            }

            return false;
        }

        static Vector2 CenterOf(SurfaceGrid grid, int x, int z)
        {
            var center = grid.CellCenter(x, z, 0f);
            return new Vector2(center.x, center.z);
        }

        [Test]
        public void BuildSteady_CoversEveryShownCell_AndTheCellsAround()
        {
            var grid = Grid(40, 30);
            Fill(grid, new RectInt(5, 5, 20, 12), Seen);
            Fill(grid, new RectInt(10, 8, 3, 3), 0f);
            new SurfaceRegionFilter().Apply(grid);
            var data = new SurfaceMeshData();

            new SurfaceMeshBuilder().BuildSteady(grid, data);

            for (var z = 0; z < grid.Depth; z++)
            {
                for (var x = 0; x < grid.Width; x++)
                {
                    var center = CenterOf(grid, x, z);
                    var covered = Covers(data.SteadyVertices, data.SteadyTriangles, center.x, center.y);
                    var near = x >= 4 && x < 26 && z >= 4 && z < 18;
                    Assert.AreEqual(near, covered, $"Cell {x}, {z}");
                }
            }

            var expected = 22 * 14 * Cell * Cell;
            Assert.AreEqual(expected, SurfaceMeshData.AreaOf(data.SteadyVertices, data.SteadyTriangles), 1e-3f);
        }

        [Test]
        public void BuildSteady_CoversCellsDepthConfirmed_ThatAreNotDrawn()
        {
            // A piece too small to draw: an object may stand on it all the same.
            var grid = Grid(30, 30);
            Fill(grid, new RectInt(10, 10, 3, 3), Seen);
            new SurfaceRegionFilter().Apply(grid);
            Assert.IsFalse(grid.IsShown(grid.IndexOf(11, 11)));
            var data = new SurfaceMeshData();

            new SurfaceMeshBuilder().BuildSteady(grid, data);

            var center = CenterOf(grid, 11, 11);
            Assert.IsTrue(Covers(data.SteadyVertices, data.SteadyTriangles, center.x, center.y));
            var edge = CenterOf(grid, 9, 9);
            Assert.IsTrue(Covers(data.SteadyVertices, data.SteadyTriangles, edge.x, edge.y));
            var outside = CenterOf(grid, 8, 8);
            Assert.IsFalse(Covers(data.SteadyVertices, data.SteadyTriangles, outside.x, outside.y));
        }

        [Test]
        public void BuildSteady_KeepsItsQuadsShort()
        {
            var grid = Grid(200, 6);
            Fill(grid, new RectInt(1, 1, 190, 3), Seen);
            new SurfaceRegionFilter().Apply(grid);
            var data = new SurfaceMeshData();

            new SurfaceMeshBuilder().BuildSteady(grid, data);

            for (var i = 0; i < data.SteadyTriangles.Count; i += 3)
            {
                var a = data.SteadyVertices[data.SteadyTriangles[i]];
                var b = data.SteadyVertices[data.SteadyTriangles[i + 1]];
                var c = data.SteadyVertices[data.SteadyTriangles[i + 2]];
                var longest = Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(b.x - c.x), Mathf.Abs(c.x - a.x));
                Assert.LessOrEqual(longest, 1f + 1e-4f, "How far the mesh is pulled is decided at its corners.");
            }
        }

        [Test]
        public void BuildSteady_EmptyGrid_MakesNothing()
        {
            var grid = Grid(10, 10);
            var data = new SurfaceMeshData();

            new SurfaceMeshBuilder().BuildSteady(grid, data);

            Assert.AreEqual(0, data.SteadyVertices.Count);
            Assert.AreEqual(0, data.SteadyTriangles.Count);
        }

        [Test]
        public void BuildOccluder_UsesConfirmedCellsOnly()
        {
            // The plane of ARCore reaches past the desk; depth confirmed the desk itself.
            var grid = Grid(60, 30);
            Fill(grid, new RectInt(2, 2, 50, 14), 0.1f, prior: true);
            Fill(grid, new RectInt(2, 2, 32, 14), Seen, prior: true);
            new SurfaceRegionFilter().Apply(grid);
            Assert.IsTrue(grid.IsShown(grid.IndexOf(45, 8)), "The grid still shows what ARCore claims.");
            var data = new SurfaceMeshData();

            new SurfaceMeshBuilder().BuildOccluder(grid, data);

            var onDesk = CenterOf(grid, 20, 8);
            var pastDesk = CenterOf(grid, 45, 8);
            Assert.IsTrue(Covers(data.OccluderVertices, data.OccluderTriangles, onDesk.x, onDesk.y));
            Assert.IsFalse(Covers(data.OccluderVertices, data.OccluderTriangles, pastDesk.x, pastDesk.y));
        }

        [Test]
        public void BuildOccluder_HasWallsOfOneMeter_AroundItsCells()
        {
            var grid = Grid(20, 20);
            Fill(grid, new RectInt(4, 4, 10, 6), Seen);
            new SurfaceRegionFilter().Apply(grid);
            var data = new SurfaceMeshData();

            new SurfaceMeshBuilder().BuildOccluder(grid, data);

            var lowest = 0f;
            var highest = float.MinValue;
            var walls = 0;
            foreach (var vertex in data.OccluderVertices)
            {
                lowest = Mathf.Min(lowest, vertex.y);
                highest = Mathf.Max(highest, vertex.y);
                if (vertex.y < -0.5f)
                {
                    walls++;
                }
            }

            Assert.AreEqual(-1f, lowest, 1e-5f, "The view scales the walls to reach the floor.");
            Assert.AreEqual(0f, highest, 1e-5f);

            // One wall quad per cell side on the outside: 2 x (10 + 6) sides, two bottom corners each.
            Assert.AreEqual(2 * (10 + 6) * 2, walls);
        }

        [Test]
        public void BuildFloorWindow_IsAFlatSquareAroundItsCenter()
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var center = SurfaceMeshBuilder.FloorWindowCenter(new Vector3(3.4f, 1.4f, -7.6f));

            SurfaceMeshBuilder.BuildFloorWindow(center, vertices, triangles);

            Assert.AreEqual(new Vector2(3f, -8f), center);
            var size = SurfaceMeshBuilder.FloorWindowSize;
            Assert.AreEqual(size * size, SurfaceMeshData.AreaOf(vertices, triangles), 1e-2f);
            foreach (var vertex in vertices)
            {
                Assert.AreEqual(0f, vertex.y);
                Assert.LessOrEqual(Mathf.Abs(vertex.x - center.x), size * 0.5f + 1e-4f);
                Assert.LessOrEqual(Mathf.Abs(vertex.z - center.y), size * 0.5f + 1e-4f);
            }

            Assert.IsTrue(Covers(vertices, triangles, 3.4f, -7.6f));
            Assert.IsTrue(Covers(vertices, triangles, 3.4f + SurfaceMeshBuilder.FloorWindowReach, -7.6f));
        }

        [Test]
        public void Rebuild_GivesTheSinkMeshesAtHeightZero_AndTheHeightsApart()
        {
            var map = new RoomSurfaceMap { PriorsNeedConfirmation = false };
            map.AddPrior(0f, RoomSurfaceMapTests.Polygon(new Rect(-2f, -2f, 4f, 4f)));
            map.AddPrior(0.75f, RoomSurfaceMapTests.Polygon(new Rect(0f, 0f, 1.6f, 0.7f)));
            RoomSurfaceMapTests.Integrate(map,
                RoomSurfaceMapTests.PointsOn(new Vector3(0.8f, 1.5f, -0.6f), 0.75f, new Rect(0f, 0f, 1.6f, 0.7f)), 3);
            var sink = new RecordingSink();
            var builder = new RoomModelBuilder();

            builder.Rebuild(map, 0.5f, true, sink);

            Assert.AreEqual(2, sink.Shown.Count);
            Assert.AreEqual(2, sink.Placed.Count);
            Assert.AreEqual(2, builder.Redrawn);
            var desk = map.Levels[1];
            Assert.AreEqual(desk.DrawnHeight, sink.Placed[desk.Id].Height, 1e-5f);
            Assert.AreEqual(0.75f, sink.Placed[desk.Id].Height, 0.035f);
            Assert.AreEqual(0f, sink.Placed[desk.Id].FloorHeight.Value, 0.01f);
            Assert.IsFalse(sink.Placed[desk.Id].IsFloor);
            Assert.AreEqual(0f, sink.HighestFillVertex, 1e-5f, "Meshes lie at height 0 of their surface.");
            Assert.Greater(sink.OccluderVertices[desk.Id], 0);
            Assert.AreEqual(0, sink.OccluderVertices[map.Levels[0].Id], "A floor hides nothing under it.");
            Assert.Greater(sink.SteadyVertices[map.Levels[0].Id], 0);

            // Nothing changed: no new meshes, but every surface is placed again.
            sink.Shown.Clear();
            sink.Placed.Clear();
            builder.Rebuild(map, 0.5f, false, sink);

            Assert.AreEqual(0, sink.Shown.Count);
            Assert.AreEqual(0, builder.Redrawn);
            Assert.AreEqual(2, sink.Placed.Count);
        }

        [Test]
        public void Rebuild_HeightThatChanges_NeedsNoNewMesh()
        {
            var map = new RoomSurfaceMap { PriorsNeedConfirmation = false };
            map.AddPrior(0.78f, RoomSurfaceMapTests.Polygon(new Rect(0f, 0f, 1.6f, 0.7f)));
            var sink = new RecordingSink();
            var builder = new RoomModelBuilder();
            builder.Rebuild(map, 0.5f, true, sink);
            var level = map.Levels[0];
            Assert.AreEqual(0.78f, sink.Placed[level.Id].Height, 1e-4f);

            level.SetHeight(0.74f);
            sink.Shown.Clear();
            for (var i = 0; i < 12; i++)
            {
                builder.Rebuild(map, 0.5f, false, sink);
            }

            Assert.AreEqual(0.74f, sink.Placed[level.Id].Height, 0.006f);
            Assert.AreEqual(0, sink.Shown.Count, "The surface only moved.");
        }

        sealed class RecordingSink : IRoomModelSink
        {
            public readonly List<int> Shown = new List<int>();
            public readonly Dictionary<int, LevelPlacement> Placed = new Dictionary<int, LevelPlacement>();
            public readonly Dictionary<int, int> OccluderVertices = new Dictionary<int, int>();
            public readonly Dictionary<int, int> SteadyVertices = new Dictionary<int, int>();
            public float HighestFillVertex = float.MinValue;

            public bool WantsOccluders => true;

            public void Show(int levelId, SurfaceMeshData data)
            {
                Shown.Add(levelId);
                OccluderVertices[levelId] = data.OccluderVertices.Count;
                SteadyVertices[levelId] = data.SteadyVertices.Count;
                foreach (var vertex in data.FillVertices)
                {
                    HighestFillVertex = Mathf.Max(HighestFillVertex, Mathf.Abs(vertex.y));
                }
            }

            public void Place(int levelId, in LevelPlacement placement)
            {
                Placed[levelId] = placement;
            }

            public void ShowObstacles(ObstacleMeshData data)
            {
            }

            public void ShowWalls(ObstacleMeshData data, float floorHeight)
            {
            }

            public void Keep(ICollection<int> levelIds)
            {
            }
        }
    }

    public class ContactDepthViewTests
    {
        readonly List<GameObject> created = new List<GameObject>();
        Material material;
        ContactDepthView view;

        [SetUp]
        public void SetUp()
        {
            material = new Material(Shader.Find("Erasmus AR Toolkit/Surface Depth"));
            view = new ContactDepthView(material);
        }

        [TearDown]
        public void TearDown()
        {
            view.Dispose();
            foreach (var item in created)
            {
                Object.DestroyImmediate(item);
            }

            created.Clear();
            Object.DestroyImmediate(material);
        }

        ToolkitObject NewObject(Vector3 position, float size = 0.2f, float raised = 0f)
        {
            var anchor = new GameObject("Anchor");
            anchor.transform.position = position;
            created.Add(anchor);
            var instance = GameObject.CreatePrimitive(PrimitiveType.Cube);
            instance.transform.SetParent(anchor.transform, false);
            instance.transform.localScale = Vector3.one * size;
            instance.transform.localPosition = new Vector3(0f, size * 0.5f + raised, 0f);
            var toolkitObject = instance.AddComponent<ToolkitObject>();
            toolkitObject.Initialize("Test Object", new InfoLabelData(), null, null, null, true);
            return toolkitObject;
        }

        static List<MeshRenderer> DrawnRenderers()
        {
            var drawn = new List<MeshRenderer>();
            var root = GameObject.Find("Contact Depth");
            if (root == null)
            {
                return drawn;
            }

            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>())
            {
                if (renderer.enabled)
                {
                    drawn.Add(renderer);
                }
            }

            return drawn;
        }

        [Test]
        public void FootprintOf_IsTheObjectAndAMargin_AtLeastAsLargeAsAHand()
        {
            var large = ContactDepthView.FootprintOf(new Bounds(new Vector3(1f, 0.5f, 2f), new Vector3(0.6f, 1f, 0.4f)));
            Assert.AreEqual(0.6f + 2f * ContactDepthView.Margin, large.width, 1e-5f);
            Assert.AreEqual(0.4f + 2f * ContactDepthView.Margin, large.height, 1e-5f);
            Assert.AreEqual(new Vector2(1f, 2f), large.center);

            var tiny = ContactDepthView.FootprintOf(new Bounds(Vector3.zero, Vector3.one * 0.0f));
            Assert.AreEqual(ContactDepthView.MinSize, tiny.width, 1e-5f);
            Assert.AreEqual(ContactDepthView.MinSize, tiny.height, 1e-5f);
        }

        [Test]
        public void Refresh_DrawsAPatchUnderEveryObject_AtTheHeightItWasPlacedAt()
        {
            var objects = new List<ToolkitObject>
            {
                NewObject(new Vector3(0f, 0.02f, 1f)),
                NewObject(new Vector3(1f, 0.02f, 1f), raised: 0.3f),
                NewObject(new Vector3(0f, 0.75f, 2f)),
            };

            view.Refresh(objects, null);

            Assert.AreEqual(3, view.PatchCount);
            Assert.AreEqual(2, view.GroupCount, "Two objects stand at the same height.");
            var drawn = DrawnRenderers();
            Assert.AreEqual(6, drawn.Count, "Mark, unmark and write per height.");
            foreach (var renderer in drawn)
            {
                StringAssert.StartsWith("Depth", renderer.name);
                var queue = renderer.sharedMaterial.renderQueue;
                Assert.GreaterOrEqual(queue, RenderOrder.ContactDepth);
                Assert.Less(queue, RenderOrder.SurfaceDepth);
                var height = renderer.transform.position.y;
                Assert.IsTrue(Mathf.Abs(height - 0.02f) < 1e-4f || Mathf.Abs(height - 0.75f) < 1e-4f, "Height " + height);
            }
        }

        [Test]
        public void Refresh_SkipsObjectsTheScanCovers()
        {
            var covered = NewObject(new Vector3(0f, 0f, 1f));
            var other = NewObject(new Vector3(1f, 0f, 1f));

            view.Refresh(new List<ToolkitObject> { covered, other }, target => target == covered);

            Assert.AreEqual(1, view.PatchCount);
        }

        [Test]
        public void Refresh_MoreHeightsThanSlots_LeavesTheRestAlone()
        {
            var objects = new List<ToolkitObject>();
            for (var i = 0; i < RenderOrder.ContactDepthSlots + 2; i++)
            {
                objects.Add(NewObject(new Vector3(i, 0.1f * i, 0f)));
            }

            Assert.DoesNotThrow(() => view.Refresh(objects, null));

            Assert.AreEqual(RenderOrder.ContactDepthSlots, view.GroupCount);
            Assert.AreEqual(RenderOrder.ContactDepthSlots, view.PatchCount);
        }

        [Test]
        public void Refresh_WithoutObjects_DrawsNothing()
        {
            view.Refresh(new List<ToolkitObject> { NewObject(Vector3.zero) }, null);
            Assert.AreEqual(1, view.PatchCount);

            view.Refresh(new List<ToolkitObject>(), null);

            Assert.AreEqual(0, view.PatchCount);
            Assert.AreEqual(0, DrawnRenderers().Count);
        }

        [Test]
        public void WithoutAMaterial_DoesNothing()
        {
            var none = new ContactDepthView(null);

            none.Refresh(new List<ToolkitObject> { NewObject(Vector3.zero) }, null);

            Assert.IsFalse(none.IsAvailable);
            Assert.AreEqual(0, none.PatchCount);
            none.Dispose();
        }
    }
}
