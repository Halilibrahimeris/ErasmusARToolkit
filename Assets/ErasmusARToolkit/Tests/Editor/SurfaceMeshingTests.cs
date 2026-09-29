using NUnit.Framework;
using UnityEngine;

namespace Erasmus.ARToolkit.Tests
{
    /// <summary>Region filter (pieces and holes) and marching squares meshes of a scanned surface.</summary>
    public class SurfaceMeshingTests
    {
        const float Cell = RoomSurfaceMap.CellSize;
        const float Seen = 0.8f;       // log-odds of a surface seen several times
        const float Carved = -0.6f;    // log-odds of a spot seen through several times

        static SurfaceGrid Grid(int width, int depth)
        {
            var grid = new SurfaceGrid();
            grid.Reset(0, 0, width, depth);
            return grid;
        }

        static void Fill(SurfaceGrid grid, RectInt cells, float logOdds)
        {
            for (var z = cells.yMin; z < cells.yMax; z++)
            {
                for (var x = cells.xMin; x < cells.xMax; x++)
                {
                    grid.SetCell(x, z, logOdds, false);
                }
            }
        }

        static bool Shown(SurfaceGrid grid, int x, int z) => grid.IsShown(grid.IndexOf(x, z));

        [Test]
        public void Filter_ShowsOccupiedCells()
        {
            var grid = Grid(20, 20);
            Fill(grid, new RectInt(2, 2, 10, 10), Seen);

            new SurfaceRegionFilter().Apply(grid);

            Assert.IsTrue(Shown(grid, 5, 5));
            Assert.IsFalse(Shown(grid, 15, 15));
        }

        [Test]
        public void Filter_DropsSmallFragments()
        {
            var grid = Grid(40, 20);
            Fill(grid, new RectInt(2, 2, 10, 10), Seen);     // 0.25 m²: a surface
            Fill(grid, new RectInt(25, 5, 3, 3), Seen);      // 0.0225 m²: a fragment

            new SurfaceRegionFilter().Apply(grid);

            Assert.IsTrue(Shown(grid, 5, 5));
            Assert.IsFalse(Shown(grid, 26, 6));
        }

        [Test]
        public void Filter_FillsSmallUnseenHoles_ButNotCarvedOnes()
        {
            var grid = Grid(40, 20);
            Fill(grid, new RectInt(1, 1, 16, 16), Seen);
            Fill(grid, new RectInt(5, 5, 3, 3), 0f);          // never seen: under a monitor stand
            Fill(grid, new RectInt(21, 1, 16, 16), Seen);
            Fill(grid, new RectInt(25, 5, 3, 3), Carved);     // seen through: a real gap

            new SurfaceRegionFilter().Apply(grid);

            Assert.IsTrue(Shown(grid, 6, 6), "An unseen hole inside a surface should be filled.");
            Assert.IsFalse(Shown(grid, 26, 6), "A carved hole must stay open.");
        }

        [Test]
        public void Filter_DoesNotFillLargeHoles()
        {
            var grid = Grid(40, 40);
            Fill(grid, new RectInt(1, 1, 36, 36), Seen);
            Fill(grid, new RectInt(8, 8, 15, 15), 0f);        // 0.56 m²

            new SurfaceRegionFilter().Apply(grid);

            Assert.IsFalse(Shown(grid, 15, 15));
        }

        [Test]
        public void Build_SquareSurface_HasAboutItsArea_AtItsHeight()
        {
            var grid = Grid(30, 30);
            Fill(grid, new RectInt(5, 5, 20, 20), Seen);     // 1 m x 1 m
            new SurfaceRegionFilter().Apply(grid);
            var data = new SurfaceMeshData();

            new SurfaceMeshBuilder().Build(grid, 0.75f, null, data);

            Assert.AreEqual(1f, data.FillArea(), 0.1f);
            Assert.Greater(data.FillTriangles.Count, 0);
            foreach (var vertex in data.FillVertices)
            {
                Assert.AreEqual(0.75f, vertex.y, 1e-5f);
                Assert.That(vertex.x, Is.InRange(5 * Cell - Cell, 25 * Cell + Cell));
            }
        }

        [Test]
        public void Build_LargeFloor_MergesInteriorSquaresIntoRows()
        {
            var grid = Grid(202, 202);
            Fill(grid, new RectInt(1, 1, 200, 200), Seen);     // 10 m x 10 m
            new SurfaceRegionFilter().Apply(grid);
            var data = new SurfaceMeshData();

            new SurfaceMeshBuilder().Build(grid, 0f, null, data);

            Assert.AreEqual(100f, data.FillArea(), 2f);
            Assert.Less(data.FillVertices.Count, 10000, "One quad per row, not one per square.");
        }

        [Test]
        public void Build_SmoothEdges_StraightenNoisyEdges()
        {
            var grid = Grid(30, 30);
            Fill(grid, new RectInt(5, 5, 20, 20), Seen);
            for (var x = 5; x < 25; x += 2)
            {
                grid.SetCell(x, 25, Seen, false);   // one-cell teeth along the top edge, like noisy evidence
            }

            new SurfaceRegionFilter().Apply(grid);
            var raw = new SurfaceMeshData();
            var smooth = new SurfaceMeshData();

            new SurfaceMeshBuilder { SmoothEdges = false }.Build(grid, 0f, null, raw);
            new SurfaceMeshBuilder().Build(grid, 0f, null, smooth);

            Assert.Less(smooth.OutlineLength, raw.OutlineLength * 0.9f);
            Assert.AreEqual(raw.FillArea(), smooth.FillArea(), 0.1f, "Smoothing must not change the size.");
        }

        [Test]
        public void Build_Outline_FollowsOnlyTheEdge()
        {
            var grid = Grid(30, 30);
            Fill(grid, new RectInt(5, 5, 20, 20), Seen);
            new SurfaceRegionFilter().Apply(grid);
            var data = new SurfaceMeshData();

            new SurfaceMeshBuilder().Build(grid, 0f, null, data);

            Assert.Greater(data.OutlineTriangles.Count, 0);
            var center = new Vector3(15 * Cell, 0f, 15 * Cell);
            foreach (var vertex in data.OutlineVertices)
            {
                var fromCenter = Mathf.Max(Mathf.Abs(vertex.x - center.x), Mathf.Abs(vertex.z - center.z));
                Assert.Greater(fromCenter, 0.4f, "Outline vertices must lie on the border of the 1 m square.");
            }
        }

        [Test]
        public void Build_Occluder_OnlyForRaisedSurfaces_WithWallsDownToTheFloor()
        {
            var grid = Grid(30, 30);
            Fill(grid, new RectInt(5, 5, 20, 20), Seen);
            new SurfaceRegionFilter().Apply(grid);
            var data = new SurfaceMeshData();
            var builder = new SurfaceMeshBuilder();

            builder.Build(grid, 0.05f, 0f, data);
            Assert.AreEqual(0, data.OccluderTriangles.Count, "A mat on the floor needs no occluder.");

            builder.Build(grid, 0.75f, 0f, data);
            Assert.Greater(data.OccluderTriangles.Count, data.FillTriangles.Count);
            var lowest = float.MaxValue;
            foreach (var vertex in data.OccluderVertices)
            {
                lowest = Mathf.Min(lowest, vertex.y);
            }

            Assert.AreEqual(0f, lowest, 1e-5f);
        }

        [Test]
        public void Build_EmptyGrid_MakesNothing()
        {
            var grid = Grid(10, 10);
            new SurfaceRegionFilter().Apply(grid);
            var data = new SurfaceMeshData();

            new SurfaceMeshBuilder().Build(grid, 0f, 0f, data);

            Assert.AreEqual(0, data.FillVertices.Count);
            Assert.AreEqual(0, data.OutlineVertices.Count);
            Assert.AreEqual(0, data.OccluderVertices.Count);
        }

        [Test]
        public void Build_DiagonalTouch_IsHandled()
        {
            // Two blocks that only touch at a corner (the ambiguous marching squares case).
            var grid = Grid(30, 30);
            Fill(grid, new RectInt(2, 2, 10, 10), Seen);
            Fill(grid, new RectInt(12, 12, 10, 10), Seen);
            new SurfaceRegionFilter().Apply(grid);
            var data = new SurfaceMeshData();

            new SurfaceMeshBuilder().Build(grid, 0f, null, data);

            Assert.AreEqual(0.5f, data.FillArea(), 0.1f);
            Assert.AreEqual(0, data.FillTriangles.Count % 3);
        }
    }
}
