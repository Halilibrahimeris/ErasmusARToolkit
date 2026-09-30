using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Erasmus.ARToolkit.Tests
{
    public class MaskMorphologyTests
    {
        const int Width = 20;
        const int Height = 16;

        static bool[] Mask(params RectInt[] blocks)
        {
            var mask = new bool[Width * Height];
            foreach (var block in blocks)
            {
                for (var z = block.yMin; z < block.yMax; z++)
                {
                    for (var x = block.xMin; x < block.xMax; x++)
                    {
                        mask[z * Width + x] = true;
                    }
                }
            }

            return mask;
        }

        static int Count(bool[] mask)
        {
            var count = 0;
            foreach (var cell in mask)
            {
                count += cell ? 1 : 0;
            }

            return count;
        }

        [Test]
        public void Close_FillsAGapOfOneCell_AndAHole()
        {
            var mask = Mask(new RectInt(2, 2, 4, 8), new RectInt(7, 2, 4, 8));
            mask[5 * Width + 3] = false;

            MaskMorphology.Close(mask, Width, Height, null, new bool[mask.Length]);

            Assert.IsTrue(mask[5 * Width + 6], "The gap between the blocks.");
            Assert.IsTrue(mask[5 * Width + 3], "The hole in a block.");
            Assert.AreEqual(9 * 8, Count(mask));
        }

        [Test]
        public void Close_LeavesLockedCellsOff_AndLosesNothing()
        {
            var mask = Mask(new RectInt(2, 2, 4, 8), new RectInt(7, 2, 4, 8));
            var before = Count(mask);
            var locked = new bool[mask.Length];
            for (var z = 0; z < Height; z++)
            {
                locked[z * Width + 6] = true;
            }

            MaskMorphology.Close(mask, Width, Height, locked, new bool[mask.Length]);

            Assert.IsFalse(mask[5 * Width + 6], "The camera looked through there.");
            Assert.AreEqual(before, Count(mask), "Every cell of the blocks is still there.");
        }

        [Test]
        public void Close_DoesNotJoinWhatLiesTwoCellsApart_AtACorner()
        {
            var mask = Mask(new RectInt(2, 2, 4, 4), new RectInt(9, 9, 4, 4));
            var before = Count(mask);

            MaskMorphology.Close(mask, Width, Height, null, new bool[mask.Length]);

            Assert.AreEqual(before, Count(mask));
        }

        [Test]
        public void Open_RemovesWhatIsThinnerThanThreeCells()
        {
            // A desk, and the top edge of the back of a chair beside it.
            var mask = Mask(new RectInt(2, 2, 10, 6), new RectInt(14, 3, 2, 8));

            MaskMorphology.Open(mask, Width, Height, null, new bool[mask.Length]);

            Assert.AreEqual(10 * 6, Count(mask));
            Assert.IsTrue(mask[2 * Width + 2], "The corner of the desk stays.");
            Assert.IsFalse(mask[5 * Width + 14]);
        }

        [Test]
        public void Open_KeepsABlockOfThreeCells()
        {
            var mask = Mask(new RectInt(5, 5, 3, 3));

            MaskMorphology.Open(mask, Width, Height, null, new bool[mask.Length]);

            Assert.AreEqual(9, Count(mask));
        }
    }

    public class ContourTracerTests
    {
        const float Cell = RoomSurfaceMap.CellSize;
        const int Width = 24;
        const int Height = 20;

        static List<List<Vector2>> Trace(bool[] mask, int originX = 0, int originZ = 0)
        {
            var loops = new List<List<Vector2>>();
            new ContourTracer().Trace(mask, Width, Height, originX, originZ, loops);
            return loops;
        }

        static bool[] Block(RectInt block)
        {
            var mask = new bool[Width * Height];
            Fill(mask, block, true);
            return mask;
        }

        static void Fill(bool[] mask, RectInt block, bool value)
        {
            for (var z = block.yMin; z < block.yMax; z++)
            {
                for (var x = block.xMin; x < block.xMax; x++)
                {
                    mask[z * Width + x] = value;
                }
            }
        }

        [Test]
        public void Block_HasOneOutline_AroundItsCells()
        {
            var loops = Trace(Block(new RectInt(3, 4, 10, 6)), -5, 7);

            Assert.AreEqual(1, loops.Count);
            var loop = loops[0];
            Assert.AreEqual(2 * (10 + 6), loop.Count, "One point per side of a cell.");

            // The corners are cut off by half a cell.
            var expected = (10 * 6 - 0.5f) * Cell * Cell;
            Assert.AreEqual(expected, PolygonMath.SignedArea(loop), 1e-5f, "Counter-clockwise, so the area is positive.");
            foreach (var point in loop)
            {
                Assert.GreaterOrEqual(point.x, (3 - 5) * Cell - 1e-5f);
                Assert.LessOrEqual(point.x, (13 - 5) * Cell + 1e-5f);
                Assert.GreaterOrEqual(point.y, (4 + 7) * Cell - 1e-5f);
                Assert.LessOrEqual(point.y, (10 + 7) * Cell + 1e-5f);
            }
        }

        [Test]
        public void Hole_HasAnOutlineOfItsOwn_ThatRunsTheOtherWay()
        {
            var mask = Block(new RectInt(2, 2, 14, 12));
            Fill(mask, new RectInt(6, 5, 4, 3), false);

            var loops = Trace(mask);

            Assert.AreEqual(2, loops.Count);
            var outer = PolygonMath.SignedArea(loops[0]) > 0f ? loops[0] : loops[1];
            var hole = PolygonMath.SignedArea(loops[0]) > 0f ? loops[1] : loops[0];
            Assert.Greater(PolygonMath.SignedArea(outer), 0f);
            Assert.AreEqual(-(4 * 3 - 0.5f) * Cell * Cell, PolygonMath.SignedArea(hole), 1e-5f);
            Assert.AreEqual((14 * 12 - 4 * 3) * Cell * Cell, PolygonMath.Area(loops), 1e-5f);
        }

        [Test]
        public void CellsThatTouchAtACorner_HaveOutlinesOfTheirOwn()
        {
            var mask = Block(new RectInt(3, 3, 4, 4));
            Fill(mask, new RectInt(7, 7, 4, 4), true);

            var loops = Trace(mask);

            Assert.AreEqual(2, loops.Count);
            Assert.Greater(PolygonMath.SignedArea(loops[0]), 0f);
            Assert.Greater(PolygonMath.SignedArea(loops[1]), 0f);
            Assert.IsTrue(PolygonMath.IsSimple(loops[0]));
            Assert.IsTrue(PolygonMath.IsSimple(loops[1]));
        }

        [Test]
        public void SlantedEdge_IsARowOfPointsOnALine()
        {
            // A staircase that goes up one cell for every cell to the right.
            var mask = new bool[Width * Height];
            for (var x = 2; x < 16; x++)
            {
                for (var z = 2; z <= x; z++)
                {
                    mask[z * Width + x] = true;
                }
            }

            var loop = Trace(mask)[0];

            var onTheSlope = 0;
            foreach (var point in loop)
            {
                // The line through the middles of the steps.
                if (Mathf.Abs(point.y - point.x - 0.5f * Cell) < 1e-5f)
                {
                    onTheSlope++;
                }
            }

            Assert.GreaterOrEqual(onTheSlope, 2 * 13);
        }

        [Test]
        public void EmptyMask_HasNoOutline()
        {
            Assert.AreEqual(0, Trace(new bool[Width * Height]).Count);
        }
    }

    public class PolygonSimplifierTests
    {
        static List<Vector2> Simplify(List<Vector2> loop, float tolerance)
        {
            var output = new List<Vector2>();
            new PolygonSimplifier().Simplify(loop, tolerance, output);
            return output;
        }

        /// <summary>A rectangle with a point every few centimeters, each a little off its side.</summary>
        static List<Vector2> NoisyRectangle(float width, float depth, float noise, float step = 0.05f)
        {
            var random = new System.Random(7);
            var loop = new List<Vector2>();
            var corners = new[] { new Vector2(0f, 0f), new Vector2(width, 0f), new Vector2(width, depth), new Vector2(0f, depth) };
            for (var side = 0; side < 4; side++)
            {
                var from = corners[side];
                var to = corners[(side + 1) % 4];
                var along = (to - from).normalized;
                var normal = new Vector2(-along.y, along.x);
                var length = Vector2.Distance(from, to);
                for (var t = 0f; t < length - 1e-4f; t += step)
                {
                    var off = t < 1e-4f ? 0f : ((float)random.NextDouble() * 2f - 1f) * noise;
                    loop.Add(from + along * t + normal * off);
                }
            }

            return loop;
        }

        [Test]
        public void StraightSides_KeepTheirCornersOnly()
        {
            var simplified = Simplify(NoisyRectangle(1.6f, 0.7f, 0f), 0.03f);

            Assert.AreEqual(4, simplified.Count);
            Assert.AreEqual(1.6f * 0.7f, PolygonMath.Area(simplified), 1e-4f);
        }

        [Test]
        public void NoiseBelowTheTolerance_IsLeftOut()
        {
            var noisy = NoisyRectangle(1.6f, 0.7f, 0.02f);

            var simplified = Simplify(noisy, 0.03f);

            Assert.AreEqual(4, simplified.Count);
            foreach (var point in noisy)
            {
                Assert.LessOrEqual(PolygonMath.DistanceToOutline(point, simplified), 0.03f + 1e-4f);
            }
        }

        [Test]
        public void WhatIsFartherThanTheTolerance_Stays()
        {
            var loop = NoisyRectangle(1.6f, 0.7f, 0f);
            loop[10] += new Vector2(0f, 0.2f);

            var simplified = Simplify(loop, 0.03f);

            CollectionAssert.Contains(simplified, loop[10]);
            Assert.Greater(simplified.Count, 4);
        }

        [Test]
        public void ASliver_IsNoOutline()
        {
            var sliver = new List<Vector2> { new Vector2(0f, 0f), new Vector2(1f, 0.005f), new Vector2(2f, 0f), new Vector2(1f, -0.005f) };

            Assert.AreEqual(0, Simplify(sliver, 0.03f).Count);
            Assert.AreEqual(0, Simplify(new List<Vector2> { Vector2.zero, Vector2.one }, 0.03f).Count);
        }
    }

    public class RoomAxesTests
    {
        static void AddRectangle(RoomAxes axes, Vector2 center, Vector2 size, float yaw, float weight = 1f)
        {
            MinAreaRectangle.AxesOf(yaw, out var axisX, out var axisZ);
            var halfX = axisX * (size.x * 0.5f);
            var halfZ = axisZ * (size.y * 0.5f);
            axes.AddLoop(new List<Vector2>
            {
                center - halfX - halfZ, center + halfX - halfZ, center + halfX + halfZ, center - halfX + halfZ,
            }, weight);
        }

        [Test]
        public void EdgesOfARoom_GiveItsDirection()
        {
            var axes = new RoomAxes();
            AddRectangle(axes, Vector2.zero, new Vector2(4f, 3f), 27f);

            Assert.IsTrue(axes.Update());

            Assert.IsTrue(axes.HasYaw);
            Assert.AreEqual(27f, axes.Yaw, 0.05f);
        }

        [Test]
        public void RoomAlongTheAxes_GivesZero()
        {
            var axes = new RoomAxes();
            AddRectangle(axes, Vector2.zero, new Vector2(4f, 3f), 0f);

            Assert.IsTrue(axes.Update());

            Assert.AreEqual(0f, axes.Yaw, 1e-3f);
        }

        [Test]
        public void DirectionsTurnedBy90Degrees_AreTheSame()
        {
            var axes = new RoomAxes();
            AddRectangle(axes, Vector2.zero, new Vector2(4f, 3f), 117f);

            axes.Update();

            Assert.AreEqual(27f, axes.Yaw, 0.05f);
            Assert.AreEqual(0f, RoomAxes.Difference(117f, 27f), 1e-4f);
            Assert.AreEqual(4f, RoomAxes.Difference(1f, 87f), 1e-4f);
            Assert.AreEqual(-4f, RoomAxes.Difference(87f, 1f), 1e-4f);
            Assert.AreEqual(80f, RoomAxes.Wrap(-10f), 1e-4f);
        }

        [Test]
        public void MostOfTheEdges_Decide()
        {
            var axes = new RoomAxes();
            AddRectangle(axes, Vector2.zero, new Vector2(5f, 4f), 12f);
            AddRectangle(axes, new Vector2(1f, 1f), new Vector2(1.6f, 0.7f), 12.5f);
            AddRectangle(axes, new Vector2(2f, 1f), new Vector2(0.6f, 0.6f), 50f);

            axes.Update();

            Assert.AreEqual(12f, axes.Yaw, 0.3f);
        }

        [Test]
        public void ShortEdges_AndTooFewOfThem_SayNothing()
        {
            var axes = new RoomAxes();
            AddRectangle(axes, Vector2.zero, new Vector2(0.2f, 0.25f), 30f);
            Assert.IsFalse(axes.Update(), "Edges shorter than 30 cm.");

            AddRectangle(axes, Vector2.zero, new Vector2(0.4f, 0.4f), 30f);
            Assert.IsFalse(axes.Update(), "1.6 m of edges in all.");
            Assert.IsFalse(axes.HasYaw);
            Assert.AreEqual(0f, axes.Yaw);
        }

        [Test]
        public void Direction_FollowsSlowly_AndStaysWhenFrozen()
        {
            var axes = new RoomAxes();
            AddRectangle(axes, Vector2.zero, new Vector2(4f, 3f), 20f);
            axes.Update();
            Assert.AreEqual(20f, axes.Yaw, 0.05f, "The first estimate is taken as it is.");

            axes.Begin();
            AddRectangle(axes, Vector2.zero, new Vector2(4f, 3f), 25f);
            axes.Update();
            Assert.AreEqual(20f + RoomAxes.Follow * 5f, axes.Yaw, 0.05f);

            axes.Settle();
            Assert.AreEqual(25f, axes.Yaw, 0.05f);

            axes.Freeze();
            axes.Begin();
            AddRectangle(axes, Vector2.zero, new Vector2(4f, 3f), 40f);
            axes.Update();
            axes.Settle();
            Assert.AreEqual(25f, axes.Yaw, 0.05f);

            axes.Reset();
            Assert.IsFalse(axes.HasYaw);
            Assert.IsFalse(axes.IsFrozen);
        }

        [Test]
        public void Walls_CountMore()
        {
            var axes = new RoomAxes();
            AddRectangle(axes, Vector2.zero, new Vector2(2f, 2f), 10f);
            AddRectangle(axes, Vector2.zero, new Vector2(1.5f, 1.5f), 40f, 3f);

            axes.Update();

            Assert.AreEqual(40f, axes.Yaw, 0.05f);
        }
    }

    public class PolygonSnapperTests
    {
        static List<Vector2> Rectangle(Vector2 center, Vector2 size, float yaw)
        {
            MinAreaRectangle.AxesOf(yaw, out var axisX, out var axisZ);
            var halfX = axisX * (size.x * 0.5f);
            var halfZ = axisZ * (size.y * 0.5f);
            return new List<Vector2>
            {
                center - halfX - halfZ, center + halfX - halfZ, center + halfX + halfZ, center - halfX + halfZ,
            };
        }

        static void AssertRightAngles(List<Vector2> loop, float yaw)
        {
            for (var i = 0; i < loop.Count; i++)
            {
                var edge = loop[(i + 1) % loop.Count] - loop[i];
                Assert.AreEqual(0f, RoomAxes.Difference(RoomAxes.YawOf(edge), yaw), 0.01f, "Edge " + i);
            }
        }

        [Test]
        public void SidesThatAreALittleOff_AreLaidAlongTheRoom()
        {
            var scanned = Rectangle(new Vector2(1f, 2f), new Vector2(1.6f, 0.7f), 33f);
            scanned[1] += new Vector2(0.02f, -0.015f);
            scanned[3] += new Vector2(-0.01f, 0.02f);
            var output = new List<Vector2>();

            Assert.IsTrue(new PolygonSnapper().Snap(scanned, 30f, output));

            Assert.AreEqual(4, output.Count);
            AssertRightAngles(output, 30f);
            Assert.AreEqual(1.6f * 0.7f, PolygonMath.Area(output), 1.6f * 0.7f * 0.1f);
            Assert.Greater(PolygonMath.SignedArea(output), 0f, "It runs the way it ran.");
        }

        [Test]
        public void SideInPieces_BecomesOneSide()
        {
            // The long side of a desk, as the scan found it: three pieces, a centimeter or two apart.
            var scanned = new List<Vector2>
            {
                new Vector2(0f, 0f), new Vector2(0.5f, 0.01f), new Vector2(0.55f, -0.01f), new Vector2(1.1f, 0f),
                new Vector2(1.15f, 0.02f), new Vector2(1.6f, 0.01f), new Vector2(1.6f, 0.7f), new Vector2(0f, 0.7f),
            };
            var output = new List<Vector2>();

            Assert.IsTrue(new PolygonSnapper().Snap(scanned, 0f, output));

            Assert.AreEqual(4, output.Count);
            AssertRightAngles(output, 0f);
            Assert.AreEqual(1.6f * 0.7f, PolygonMath.Area(output), 0.03f);
        }

        [Test]
        public void SlantedEdge_StaysAsItIs()
        {
            // A desk with one corner cut off at 45 degrees.
            var scanned = new List<Vector2>
            {
                new Vector2(0f, 0f), new Vector2(1.2f, 0f), new Vector2(1.6f, 0.4f), new Vector2(1.6f, 0.9f),
                new Vector2(0f, 0.9f),
            };
            var output = new List<Vector2>();

            Assert.IsTrue(new PolygonSnapper().Snap(scanned, 0f, output));

            Assert.AreEqual(5, output.Count);
            Assert.AreEqual(PolygonMath.Area(scanned), PolygonMath.Area(output), 1e-3f);
            var slanted = 0;
            for (var i = 0; i < output.Count; i++)
            {
                var edge = output[(i + 1) % output.Count] - output[i];
                if (Mathf.Abs(RoomAxes.Difference(RoomAxes.YawOf(edge), 0f)) > 40f)
                {
                    slanted++;
                }
            }

            Assert.AreEqual(1, slanted);
        }

        [Test]
        public void NothingAlongTheRoom_IsLeftAlone()
        {
            var output = new List<Vector2>();

            Assert.IsFalse(new PolygonSnapper().Snap(Rectangle(Vector2.zero, new Vector2(1f, 1f), 45f), 0f, output));
            Assert.AreEqual(0, output.Count);
        }

        [Test]
        public void ResultFarFromTheScan_IsRefused()
        {
            // A long thin triangle: laid along the room, its area would change by much.
            var scanned = new List<Vector2> { new Vector2(0f, 0f), new Vector2(2f, 0.3f), new Vector2(0f, 0.25f) };
            var output = new List<Vector2>();

            Assert.IsFalse(new PolygonSnapper().Snap(scanned, 0f, output));
            Assert.AreEqual(0, output.Count);
        }

        [Test]
        public void StepInASide_IsKept()
        {
            // An L: two desks, one 20 cm deeper than the other.
            var scanned = new List<Vector2>
            {
                new Vector2(0f, 0f), new Vector2(3f, 0.01f), new Vector2(3f, 0.9f), new Vector2(1.5f, 0.91f),
                new Vector2(1.51f, 0.7f), new Vector2(0f, 0.7f),
            };
            var output = new List<Vector2>();

            Assert.IsTrue(new PolygonSnapper().Snap(scanned, 0f, output));

            Assert.AreEqual(6, output.Count);
            AssertRightAngles(output, 0f);
            Assert.AreEqual(PolygonMath.Area(scanned), PolygonMath.Area(output), 0.05f);
        }
    }

    public class PolygonFillTests
    {
        static float AreaOf(List<Vector3> vertices, List<int> triangles) => SurfaceMeshData.AreaOf(vertices, triangles);

        static List<Vector2> Rectangle(Vector2 center, Vector2 size, float yaw, bool clockwise = false)
        {
            MinAreaRectangle.AxesOf(yaw, out var axisX, out var axisZ);
            var halfX = axisX * (size.x * 0.5f);
            var halfZ = axisZ * (size.y * 0.5f);
            var loop = new List<Vector2>
            {
                center - halfX - halfZ, center + halfX - halfZ, center + halfX + halfZ, center - halfX + halfZ,
            };
            if (clockwise)
            {
                loop.Reverse();
            }

            return loop;
        }

        [Test]
        public void RectangleAlongTheRoom_IsOneQuad()
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var loops = new List<List<Vector2>> { Rectangle(new Vector2(1f, 2f), new Vector2(1.6f, 0.7f), 30f) };

            new PolygonFill().Trapezoids(loops, 30f, vertices, triangles);

            Assert.AreEqual(4, vertices.Count);
            Assert.AreEqual(6, triangles.Count);
            Assert.AreEqual(1.6f * 0.7f, AreaOf(vertices, triangles), 1e-4f);
            foreach (var vertex in vertices)
            {
                Assert.AreEqual(0f, vertex.y);
            }
        }

        [Test]
        public void AnyShape_IsFilledWithItsArea()
        {
            var shape = new List<Vector2>
            {
                new Vector2(0f, 0f), new Vector2(2f, 0.2f), new Vector2(2.5f, 1.4f), new Vector2(1.2f, 0.8f),
                new Vector2(0.4f, 1.9f), new Vector2(-0.6f, 0.9f),
            };
            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            new PolygonFill().Trapezoids(new List<List<Vector2>> { shape }, 17f, vertices, triangles);

            Assert.AreEqual(PolygonMath.Area(shape), AreaOf(vertices, triangles), 1e-3f);
            foreach (var vertex in vertices)
            {
                Assert.LessOrEqual(PolygonMath.DistanceToOutline(new Vector2(vertex.x, vertex.z), shape), 1e-3f,
                    "Every corner of the fill lies on the outline.");
            }
        }

        [Test]
        public void Holes_StayOpen()
        {
            var loops = new List<List<Vector2>>
            {
                Rectangle(Vector2.zero, new Vector2(3f, 2f), 0f),
                Rectangle(new Vector2(0.5f, 0.2f), new Vector2(1f, 0.6f), 0f, clockwise: true),
            };
            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            new PolygonFill().Trapezoids(loops, 0f, vertices, triangles);

            Assert.AreEqual(3f * 2f - 1f * 0.6f, AreaOf(vertices, triangles), 1e-4f);
            Assert.IsTrue(PolygonMath.Contains(loops, new Vector2(-1f, 0f)));
            Assert.IsFalse(PolygonMath.Contains(loops, new Vector2(0.5f, 0.2f)), "Inside the hole.");
        }

        [Test]
        public void NoOutline_NoFill()
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            new PolygonFill().Trapezoids(new List<List<Vector2>>(), 0f, vertices, triangles);

            Assert.AreEqual(0, vertices.Count);
        }
    }

    public class PolygonRasterTests
    {
        struct Cells : IRowSink
        {
            public HashSet<Vector2Int> Inside;

            public void AddRun(int row, int first, int last)
            {
                for (var x = first; x <= last; x++)
                {
                    Inside.Add(new Vector2Int(x, row));
                }
            }
        }

        static HashSet<Vector2Int> Fill(List<List<Vector2>> loops)
        {
            var sink = new Cells { Inside = new HashSet<Vector2Int>() };
            PolygonRaster.FillRows(loops, RoomSurfaceMap.CellSize, ref sink, new List<float>());
            return sink.Inside;
        }

        [Test]
        public void CellsWhoseCentersLieInside_AreInside()
        {
            var square = new List<Vector2> { new Vector2(0f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0.32f), new Vector2(0f, 0.32f) };

            var cells = Fill(new List<List<Vector2>> { square });

            Assert.AreEqual(10 * 6, cells.Count);
            CollectionAssert.Contains(cells, new Vector2Int(0, 0));
            CollectionAssert.Contains(cells, new Vector2Int(9, 5));
            CollectionAssert.DoesNotContain(cells, new Vector2Int(10, 0));
            CollectionAssert.DoesNotContain(cells, new Vector2Int(0, 6));
        }

        [Test]
        public void ALoopInsideALoop_IsAHole()
        {
            var outer = new List<Vector2> { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) };
            var hole = new List<Vector2> { new Vector2(-0.2f, -0.2f), new Vector2(-0.2f, 0.2f), new Vector2(0.2f, 0.2f), new Vector2(0.2f, -0.2f) };

            var cells = Fill(new List<List<Vector2>> { outer, hole });

            Assert.AreEqual(40 * 40 - 8 * 8, cells.Count);
            CollectionAssert.DoesNotContain(cells, new Vector2Int(0, 0));
            CollectionAssert.Contains(cells, new Vector2Int(-20, -20));
        }

        [Test]
        public void OneLoop_FillsLikeThePlanesOfTheMap()
        {
            var shape = new List<Vector2>
            {
                new Vector2(0f, 0f), new Vector2(2f, 0f), new Vector2(2f, 1f),
                new Vector2(1f, 1f), new Vector2(1f, 2f), new Vector2(0f, 2f),
            };
            var map = new RoomSurfaceMap();
            map.AddPrior(0f, shape);
            var sink = new Cells { Inside = new HashSet<Vector2Int>() };

            PolygonRaster.FillRows(shape, RoomSurfaceMap.CellSize, ref sink, new List<float>());

            Assert.AreEqual(3 * 20 * 20, sink.Inside.Count);
            foreach (var cell in sink.Inside)
            {
                Assert.IsTrue(map.Levels[0].HasPrior(cell));
            }

            Assert.AreEqual(3f, map.Levels[0].PlaneArea, 1e-3f);
        }
    }

    public class BuildCleanTests
    {
        const float Cell = RoomSurfaceMap.CellSize;
        const float Seen = 0.8f;

        static SurfaceGrid Grid(int width, int depth)
        {
            var grid = new SurfaceGrid();
            grid.Reset(-20, -20, width, depth);
            return grid;
        }

        /// <summary>Marks the cells inside a rectangle that is turned by <paramref name="yaw"/> as seen by depth.</summary>
        static void FillRectangle(SurfaceGrid grid, Vector2 center, Vector2 size, float yaw, float logOdds = Seen,
            System.Random noise = null)
        {
            MinAreaRectangle.AxesOf(yaw, out var axisX, out var axisZ);
            for (var z = 0; z < grid.Depth; z++)
            {
                for (var x = 0; x < grid.Width; x++)
                {
                    var cell = grid.CellCenter(x, z, 0f);
                    var point = new Vector2(cell.x, cell.z) - center;
                    var edge = noise != null ? ((float)noise.NextDouble() - 0.5f) * 0.06f : 0f;
                    if (Mathf.Abs(Vector2.Dot(point, axisX)) <= size.x * 0.5f + edge
                        && Mathf.Abs(Vector2.Dot(point, axisZ)) <= size.y * 0.5f + edge)
                    {
                        grid.SetCell(x, z, logOdds, false);
                    }
                }
            }
        }

        static SurfaceMeshData Build(SurfaceGrid grid, bool isFloor, float yaw, SurfaceShape shape = null)
        {
            var data = new SurfaceMeshData();
            new SurfaceRegionFilter().Apply(grid, isFloor ? SurfaceFilterSettings.Floor : SurfaceFilterSettings.Raised);
            var builder = new SurfaceMeshBuilder();
            if (shape != null)
            {
                builder.BuildClean(grid, new SurfaceBuildSettings(isFloor, yaw), shape, data);
            }
            else
            {
                builder.BuildClean(grid, new SurfaceBuildSettings(isFloor, yaw), data);
            }

            return data;
        }

        [Test]
        public void TurnedDesk_IsDrawnAsARectangle_AlongTheRoom()
        {
            var grid = Grid(80, 80);
            FillRectangle(grid, new Vector2(0.3f, 0.5f), new Vector2(1.6f, 0.7f), 25f);
            var shape = new SurfaceShape();

            var data = Build(grid, false, 25f, shape);

            Assert.AreEqual(1, shape.Loops.Count);
            var desk = shape.Loops[0];
            Assert.AreEqual(4, desk.Count, "A desk has four corners.");
            for (var i = 0; i < 4; i++)
            {
                var edge = desk[(i + 1) % 4] - desk[i];
                Assert.AreEqual(0f, RoomAxes.Difference(RoomAxes.YawOf(edge), 25f), 0.01f);
            }

            Assert.AreEqual(1.6f * 0.7f, PolygonMath.Area(desk), 1.6f * 0.7f * 0.1f);
            Assert.AreEqual(4, data.FillVertices.Count, "One quad.");
            Assert.AreEqual(PolygonMath.Area(desk), data.FillArea(), 1e-3f);
            Assert.AreEqual(PolygonMath.Perimeter(desk), data.OutlineLength, 1e-3f);
            Assert.AreEqual(8, data.OutlineVertices.Count);
        }

        [Test]
        public void RoundTable_IsNotARectangle()
        {
            var grid = Grid(80, 80);
            for (var z = 0; z < grid.Depth; z++)
            {
                for (var x = 0; x < grid.Width; x++)
                {
                    var cell = grid.CellCenter(x, z, 0f);
                    if (new Vector2(cell.x - 0.5f, cell.z - 0.5f).magnitude <= 0.6f)
                    {
                        grid.SetCell(x, z, Seen, false);
                    }
                }
            }

            var shape = new SurfaceShape();
            var data = Build(grid, false, 0f, shape);

            Assert.AreEqual(1, shape.Loops.Count);
            Assert.Greater(shape.Loops[0].Count, 6);
            var circle = Mathf.PI * 0.6f * 0.6f;
            Assert.AreEqual(circle, data.FillArea(), circle * 0.08f);
        }

        [Test]
        public void Floor_KeepsItsShape_WithStraightEdges()
        {
            // An L-shaped room, turned by 10 degrees: 4 m x 2 m, and 1.6 m x 2.4 m on top of its left end.
            var grid = Grid(140, 140);
            MinAreaRectangle.AxesOf(10f, out var axisX, out var axisZ);
            FillRectangle(grid, axisX * 2f + axisZ * 1f, new Vector2(4f, 2f), 10f);
            FillRectangle(grid, axisX * 0.8f + axisZ * 3.2f, new Vector2(1.6f, 2.4f), 10f);
            var shape = new SurfaceShape();

            var data = Build(grid, true, 10f, shape);

            Assert.AreEqual(1, shape.Loops.Count);
            var floor = shape.Loops[0];
            Assert.LessOrEqual(floor.Count, 8, "An L has six corners.");
            Assert.GreaterOrEqual(floor.Count, 6);
            for (var i = 0; i < floor.Count; i++)
            {
                var edge = floor[(i + 1) % floor.Count] - floor[i];
                if (edge.magnitude > 0.3f)
                {
                    Assert.AreEqual(0f, RoomAxes.Difference(RoomAxes.YawOf(edge), 10f), 0.01f, "Edge " + i);
                }
            }

            Assert.AreEqual(PolygonMath.Area(floor), data.FillArea(), 1e-2f);
        }

        [Test]
        public void BuildClean_DrawnMaskNeverCoversCarvedOrBlockedCells()
        {
            var grid = Grid(80, 80);
            FillRectangle(grid, new Vector2(0.3f, 0.5f), new Vector2(1.6f, 0.9f), 0f);

            // The camera looked through a corner of what ARCore called the desk, and a lamp stands on it.
            var carved = new List<int>();
            var blocked = new List<int>();
            for (var z = 0; z < grid.Depth; z++)
            {
                for (var x = 0; x < grid.Width; x++)
                {
                    var cell = grid.CellCenter(x, z, 0f);
                    var index = grid.IndexOf(x, z);
                    if (cell.x > 0.95f && cell.x < 1.1f && cell.z > 0.8f && cell.z < 0.95f)
                    {
                        grid.SetCell(x, z, -0.6f, false);
                        carved.Add(index);
                    }
                    else if (cell.x > 0f && cell.x < 0.1f && cell.z > 0.4f && cell.z < 0.5f)
                    {
                        grid.SetBlocked(index, true);
                        blocked.Add(index);
                    }
                }
            }

            Assert.Greater(carved.Count, 0);
            Assert.Greater(blocked.Count, 0);
            var data = Build(grid, false, 0f);

            Assert.Greater(data.FillArea(), 1f);
            foreach (var index in carved)
            {
                Assert.IsFalse(grid.IsShown(index), "Seen through: nothing is placed there.");
            }

            foreach (var index in blocked)
            {
                Assert.IsFalse(grid.IsShown(index), "Something stands there.");
            }
        }

        [Test]
        public void DrawnCells_AreTheCellsInsideTheOutline()
        {
            var grid = Grid(80, 80);
            FillRectangle(grid, new Vector2(0.3f, 0.5f), new Vector2(1.6f, 0.7f), 25f);
            var shape = new SurfaceShape();

            Build(grid, false, 25f, shape);

            for (var z = 0; z < grid.Depth; z++)
            {
                for (var x = 0; x < grid.Width; x++)
                {
                    var cell = grid.CellCenter(x, z, 0f);
                    var inside = PolygonMath.Contains(shape.Loops, new Vector2(cell.x, cell.z));
                    Assert.AreEqual(inside, grid.IsShown(grid.IndexOf(x, z)), $"Cell {x}, {z}");
                }
            }
        }

        [Test]
        public void Outline_DoesNotCreepOverTenRebuilds()
        {
            var shape = new SurfaceShape();
            var builder = new SurfaceMeshBuilder();
            var filter = new SurfaceRegionFilter();
            var random = new System.Random(3);
            List<Vector2> first = null;
            for (var rebuild = 0; rebuild < 10; rebuild++)
            {
                // The same desk with edges that are a little different every time.
                var grid = Grid(80, 80);
                FillRectangle(grid, new Vector2(0.3f, 0.5f), new Vector2(1.6f, 0.7f), 25f, Seen, random);
                filter.Apply(grid, SurfaceFilterSettings.Raised);
                builder.BuildClean(grid, new SurfaceBuildSettings(false, 25f), shape, new SurfaceMeshData());
                Assert.AreEqual(1, shape.Loops.Count);
                if (first == null)
                {
                    first = new List<Vector2>(shape.Loops[0]);
                    continue;
                }

                Assert.AreEqual(first.Count, shape.Loops[0].Count, "Rebuild " + rebuild);
                for (var i = 0; i < first.Count; i++)
                {
                    Assert.AreEqual(first[i], shape.Loops[0][i], $"Rebuild {rebuild}, corner {i}: the outline holds still.");
                }
            }
        }

        [Test]
        public void WithoutTheDirectionOfTheRoom_EdgesAreStraightButNotTurned()
        {
            var grid = Grid(80, 80);
            FillRectangle(grid, new Vector2(0.3f, 0.5f), new Vector2(1.6f, 0.7f), 25f);
            var shape = new SurfaceShape();
            new SurfaceRegionFilter().Apply(grid, SurfaceFilterSettings.Raised);

            new SurfaceMeshBuilder().BuildClean(grid, new SurfaceBuildSettings(false, 0f, false), shape, new SurfaceMeshData());

            Assert.AreEqual(1, shape.Loops.Count);
            Assert.AreEqual(4, shape.Loops[0].Count, "The rectangle that fits best.");
            var edge = shape.Loops[0][1] - shape.Loops[0][0];
            Assert.AreEqual(0f, RoomAxes.Difference(RoomAxes.YawOf(edge), 25f), 3f);
        }

        [Test]
        public void EmptyGrid_MakesNothing()
        {
            var grid = Grid(20, 20);
            var shape = new SurfaceShape();

            var data = Build(grid, true, 0f, shape);

            Assert.AreEqual(0, data.FillVertices.Count);
            Assert.AreEqual(0, data.OutlineVertices.Count);
            Assert.AreEqual(0, shape.Loops.Count);
        }
    }

    public class FloorFilterTests
    {
        const float Seen = 0.8f;

        static SurfaceGrid Grid()
        {
            var grid = new SurfaceGrid();
            grid.Reset(0, 0, 120, 60);
            return grid;
        }

        static void Fill(SurfaceGrid grid, RectInt cells, float logOdds = Seen)
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
        public void Floor_KeepsLargePieces_AndDropsSmallOnes()
        {
            var grid = Grid();
            Fill(grid, new RectInt(2, 2, 40, 40));      // 4 m²
            Fill(grid, new RectInt(50, 2, 12, 10));     // 0.3 m²
            Fill(grid, new RectInt(70, 2, 8, 8));       // 0.16 m²

            new SurfaceRegionFilter().Apply(grid, SurfaceFilterSettings.Floor);

            Assert.IsTrue(Shown(grid, 20, 20));
            Assert.IsTrue(Shown(grid, 55, 6));
            Assert.IsFalse(Shown(grid, 73, 5), "A piece of floor of 0.16 m², far from everything, is noise.");
        }

        [Test]
        public void Floor_KeepsItsLargestPiece_HoweverSmall()
        {
            var grid = Grid();
            Fill(grid, new RectInt(2, 2, 8, 8));
            Fill(grid, new RectInt(30, 2, 5, 5));

            new SurfaceRegionFilter().Apply(grid, SurfaceFilterSettings.Floor);

            Assert.IsTrue(Shown(grid, 5, 5), "A scan starts with a small floor.");
            Assert.IsFalse(Shown(grid, 32, 4));
        }

        [Test]
        public void Floor_KeepsSmallPiecesBesideFurniture()
        {
            var grid = Grid();
            Fill(grid, new RectInt(2, 2, 40, 40));
            Fill(grid, new RectInt(50, 2, 8, 8));
            for (var z = 2; z < 10; z++)
            {
                grid.SetBlocked(grid.IndexOf(58, z), true);
            }

            new SurfaceRegionFilter().Apply(grid, SurfaceFilterSettings.Floor);

            Assert.IsTrue(Shown(grid, 53, 5), "The floor between the legs of desks comes in small pieces.");
            Assert.IsFalse(Shown(grid, 58, 5));
        }

        [Test]
        public void Floor_FillsHolesOfHalfASquareMeter_ButNotUnderFurniture()
        {
            var grid = Grid();
            Fill(grid, new RectInt(2, 2, 50, 50));
            Fill(grid, new RectInt(10, 10, 12, 12), 0f);    // 0.36 m², never seen
            Fill(grid, new RectInt(30, 10, 12, 12), 0f);    // the same, under a table
            for (var z = 10; z < 22; z++)
            {
                for (var x = 30; x < 42; x++)
                {
                    grid.SetCovered(grid.IndexOf(x, z), true);
                }
            }

            new SurfaceRegionFilter().Apply(grid, SurfaceFilterSettings.Floor);

            Assert.IsTrue(Shown(grid, 15, 15));
            Assert.IsFalse(Shown(grid, 35, 15), "What lies under a table was not seen: it may not be floor.");
        }

        [Test]
        public void Raised_ThinStrip_IsRemoved_DeskStays()
        {
            var grid = Grid();
            Fill(grid, new RectInt(2, 2, 32, 14));
            Fill(grid, new RectInt(40, 4, 2, 9));

            new SurfaceRegionFilter().Apply(grid, SurfaceFilterSettings.Raised);

            Assert.IsTrue(Shown(grid, 10, 8));
            Assert.IsTrue(Shown(grid, 2, 2));
            Assert.IsFalse(Shown(grid, 40, 8), "The top of the back of a chair.");
        }

        [Test]
        public void Legacy_StillDecidesAsBefore()
        {
            var grid = Grid();
            Fill(grid, new RectInt(2, 2, 32, 14));
            Fill(grid, new RectInt(40, 4, 2, 16));

            new SurfaceRegionFilter().Apply(grid);

            Assert.IsTrue(Shown(grid, 40, 8), "0.08 m²: a piece large enough for the old rules.");
        }
    }
}
