using System;
using System.Collections.Generic;
using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>Vertex and index lists of one scanned surface. Reused between rebuilds.</summary>
    public sealed class SurfaceMeshData
    {
        public readonly List<Vector3> FillVertices = new List<Vector3>();
        public readonly List<int> FillTriangles = new List<int>();
        public readonly List<Vector3> OutlineVertices = new List<Vector3>();
        public readonly List<int> OutlineTriangles = new List<int>();
        public readonly List<Vector3> OccluderVertices = new List<Vector3>();
        public readonly List<int> OccluderTriangles = new List<int>();

        /// <summary>Total length of the outline (meters) from the last build.</summary>
        public float OutlineLength { get; internal set; }

        public void Clear()
        {
            OutlineLength = 0f;
            FillVertices.Clear();
            FillTriangles.Clear();
            OutlineVertices.Clear();
            OutlineTriangles.Clear();
            OccluderVertices.Clear();
            OccluderTriangles.Clear();
        }

        /// <summary>Area of the fill seen from above, in square meters.</summary>
        public float FillArea()
        {
            var area = 0f;
            for (var i = 0; i < FillTriangles.Count; i += 3)
            {
                var a = FillVertices[FillTriangles[i]];
                var b = FillVertices[FillTriangles[i + 1]];
                var c = FillVertices[FillTriangles[i + 2]];
                area += Mathf.Abs((b.x - a.x) * (c.z - a.z) - (c.x - a.x) * (b.z - a.z)) * 0.5f;
            }

            return area;
        }
    }

    /// <summary>
    /// Turns the shown cells of a <see cref="SurfaceGrid"/> into meshes with marching squares, so edges
    /// follow the cell evidence smoothly instead of in 5 cm steps:
    /// <list type="bullet">
    /// <item>fill: the surface itself (drawn with the grid material),</item>
    /// <item>outline: a thin flat ribbon along the outer edge and around holes,</item>
    /// <item>occluder (optional): the surface plus walls down to the floor, like the table volume of a VR
    /// headset's room model. Drawn into depth only, it hides the floor grid and virtual objects behind or
    /// under the table.</item>
    /// </list>
    /// Buffers are reused, so keep one builder and call <see cref="Build"/> for every surface.
    /// </summary>
    public sealed class SurfaceMeshBuilder
    {
        public const float OutlineWidth = 0.01f;

        /// <summary>Surfaces less than this high above the floor get no occluder.</summary>
        public const float MinOccluderHeight = 0.15f;

        const float MinInsideValue = 0.05f;

        // Corner order around a square: 0 = (x, z), 1 = (x + 1, z), 2 = (x + 1, z + 1), 3 = (x, z + 1).
        // Edge k runs from corner k to corner (k + 1) % 4.
        static readonly int[] CornerX = { 0, 1, 1, 0 };
        static readonly int[] CornerZ = { 0, 0, 1, 1 };

        readonly Vector3[] polygon = new Vector3[8];
        readonly List<Vector3> segments = new List<Vector3>();
        readonly float[] values = new float[4];
        readonly Vector3[] corners = new Vector3[4];
        readonly Vector3[] edges = new Vector3[4];
        float[] field = Array.Empty<float>();
        float[] blurred = Array.Empty<float>();

        /// <summary>
        /// Softens the edge field (a small 1-2-1 blur) before the edges are traced, so outlines follow the
        /// surface smoothly instead of zig-zagging along noisy cells. On by default.
        /// </summary>
        public bool SmoothEdges { get; set; } = true;

        /// <param name="occluderBottom">Floor height for the occluder walls, or null for no occluder.</param>
        public void Build(SurfaceGrid grid, float height, float? occluderBottom, SurfaceMeshData output)
        {
            output.Clear();
            segments.Clear();
            PrepareField(grid);
            for (var z = 0; z < grid.Depth - 1; z++)
            {
                // Squares fully inside the surface are merged along the row into one quad.
                var runStart = -1;
                for (var x = 0; x < grid.Width - 1; x++)
                {
                    var caseIndex = 0;
                    for (var k = 0; k < 4; k++)
                    {
                        values[k] = field[grid.IndexOf(x + CornerX[k], z + CornerZ[k])];
                        if (values[k] > 0f)
                        {
                            caseIndex |= 1 << k;
                        }
                    }

                    if (caseIndex == 15)
                    {
                        runStart = runStart < 0 ? x : runStart;
                        continue;
                    }

                    if (runStart >= 0)
                    {
                        AddRun(grid, height, runStart, x, z, output);
                        runStart = -1;
                    }

                    // Most squares of a level are empty: skip them before any geometry work.
                    if (caseIndex == 0)
                    {
                        continue;
                    }

                    for (var k = 0; k < 4; k++)
                    {
                        corners[k] = grid.CellCenter(x + CornerX[k], z + CornerZ[k], height);
                    }

                    for (var k = 0; k < 4; k++)
                    {
                        var next = (k + 1) % 4;
                        if ((values[k] > 0f) != (values[next] > 0f))
                        {
                            var t = values[k] / (values[k] - values[next]);
                            edges[k] = Vector3.Lerp(corners[k], corners[next], t);
                        }
                    }

                    AddSquare(caseIndex, output);
                }

                if (runStart >= 0)
                {
                    AddRun(grid, height, runStart, grid.Width - 1, z, output);
                }
            }

            AddOutline(output);
            if (occluderBottom.HasValue && height - occluderBottom.Value >= MinOccluderHeight)
            {
                AddOccluder(output, occluderBottom.Value);
            }
        }

        /// <summary>One quad over the fully inside squares <paramref name="firstX"/>..<paramref name="endX"/> - 1 of row z.</summary>
        void AddRun(SurfaceGrid grid, float height, int firstX, int endX, int z, SurfaceMeshData output)
        {
            polygon[0] = grid.CellCenter(firstX, z, height);
            polygon[1] = grid.CellCenter(endX, z, height);
            polygon[2] = grid.CellCenter(endX, z + 1, height);
            polygon[3] = grid.CellCenter(firstX, z + 1, height);
            AddPolygon(output.FillVertices, output.FillTriangles, 4);
        }

        void PrepareField(SurfaceGrid grid)
        {
            var count = grid.CellCount;
            if (field.Length < count)
            {
                field = new float[count];
                blurred = new float[count];
            }

            for (var i = 0; i < count; i++)
            {
                field[i] = FieldValue(grid, i);
            }

            if (!SmoothEdges)
            {
                return;
            }

            // Separable 1-2-1 blur, edges clamped: rows into the buffer, then columns back.
            for (var z = 0; z < grid.Depth; z++)
            {
                var row = z * grid.Width;
                for (var x = 0; x < grid.Width; x++)
                {
                    var left = field[row + Mathf.Max(x - 1, 0)];
                    var right = field[row + Mathf.Min(x + 1, grid.Width - 1)];
                    blurred[row + x] = (left + 2f * field[row + x] + right) * 0.25f;
                }
            }

            for (var z = 0; z < grid.Depth; z++)
            {
                var below = Mathf.Max(z - 1, 0) * grid.Width;
                var above = Mathf.Min(z + 1, grid.Depth - 1) * grid.Width;
                var row = z * grid.Width;
                for (var x = 0; x < grid.Width; x++)
                {
                    field[row + x] = (blurred[below + x] + 2f * blurred[row + x] + blurred[above + x]) * 0.25f;
                }
            }
        }

        /// <summary>Positive inside the shown surface, negative outside; the edge runs where it is zero.</summary>
        static float FieldValue(SurfaceGrid grid, int index)
        {
            var value = grid.ScoreAt(index) - SurfaceEvidence.OccupiedThreshold;
            return grid.IsShown(index) ? Mathf.Max(value, MinInsideValue) : Mathf.Min(value, -MinInsideValue);
        }

        void AddSquare(int caseIndex, SurfaceMeshData output)
        {
            var isSaddle = caseIndex == 5 || caseIndex == 10;
            if (isSaddle && (values[0] + values[1] + values[2] + values[3]) <= 0f)
            {
                // Two separate corners: a triangle at each inside corner.
                var first = caseIndex == 5 ? 0 : 1;
                for (var corner = first; corner < 4; corner += 2)
                {
                    var before = (corner + 3) % 4;
                    AddTriangle(output.FillVertices, output.FillTriangles, corners[corner], edges[before], edges[corner]);
                    AddSegment(edges[before], edges[corner]);
                }

                return;
            }

            // Walk around the square: inside corners and edge crossings form one convex polygon.
            var count = 0;
            for (var k = 0; k < 4; k++)
            {
                var next = (k + 1) % 4;
                if (values[k] > 0f)
                {
                    polygon[count++] = corners[k];
                }

                if ((values[k] > 0f) != (values[next] > 0f))
                {
                    polygon[count++] = edges[k];
                }
            }

            AddPolygon(output.FillVertices, output.FillTriangles, count);
            if (isSaddle)
            {
                // Connected through the middle: the edge cuts off each outside corner.
                var first = caseIndex == 5 ? 1 : 0;
                for (var corner = first; corner < 4; corner += 2)
                {
                    AddSegment(edges[(corner + 3) % 4], edges[corner]);
                }

                return;
            }

            AddSingleSegment();
        }

        void AddSingleSegment()
        {
            var found = 0;
            var a = Vector3.zero;
            for (var k = 0; k < 4; k++)
            {
                if ((values[k] > 0f) == (values[(k + 1) % 4] > 0f))
                {
                    continue;
                }

                if (found++ == 0)
                {
                    a = edges[k];
                }
                else
                {
                    AddSegment(a, edges[k]);
                }
            }
        }

        void AddSegment(Vector3 a, Vector3 b)
        {
            segments.Add(a);
            segments.Add(b);
        }

        void AddPolygon(List<Vector3> vertices, List<int> triangles, int count)
        {
            var start = vertices.Count;
            for (var i = 0; i < count; i++)
            {
                vertices.Add(polygon[i]);
            }

            for (var i = 1; i < count - 1; i++)
            {
                triangles.Add(start);
                triangles.Add(start + i + 1);
                triangles.Add(start + i);
            }
        }

        static void AddTriangle(List<Vector3> vertices, List<int> triangles, Vector3 a, Vector3 b, Vector3 c)
        {
            var start = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
        }

        /// <summary>A flat quad along each edge segment, a little longer than it so corners have no gaps.</summary>
        void AddOutline(SurfaceMeshData output)
        {
            var half = OutlineWidth * 0.5f;
            for (var i = 0; i < segments.Count; i += 2)
            {
                var a = segments[i];
                var b = segments[i + 1];
                var along = b - a;
                along.y = 0f;
                if (along.sqrMagnitude < 1e-8f)
                {
                    continue;
                }

                output.OutlineLength += along.magnitude;
                along = along.normalized * half;
                var side = new Vector3(-along.z, 0f, along.x);
                AddQuad(output.OutlineVertices, output.OutlineTriangles,
                    a - along - side, a - along + side, b + along + side, b + along - side);
            }
        }

        /// <summary>The fill again plus a wall from every edge segment down to the floor.</summary>
        void AddOccluder(SurfaceMeshData output, float bottom)
        {
            output.OccluderVertices.AddRange(output.FillVertices);
            output.OccluderTriangles.AddRange(output.FillTriangles);
            for (var i = 0; i < segments.Count; i += 2)
            {
                var a = segments[i];
                var b = segments[i + 1];
                AddQuad(output.OccluderVertices, output.OccluderTriangles,
                    a, b, new Vector3(b.x, bottom, b.z), new Vector3(a.x, bottom, a.z));
            }
        }

        static void AddQuad(List<Vector3> vertices, List<int> triangles, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            var start = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            vertices.Add(d);
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
        }
    }
}
