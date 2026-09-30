using System.Collections.Generic;
using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>Takes the cells inside a polygon, row by row (see <see cref="PolygonRaster"/>).</summary>
    internal interface IRowSink
    {
        /// <summary>Cells <paramref name="first"/>..<paramref name="last"/> of a row lie inside.</summary>
        void AddRun(int row, int first, int last);
    }

    /// <summary>
    /// Finds the cells inside a polygon on the floor (x and z in meters): the cells whose centers lie
    /// inside. Filled row by row with the even-odd rule, so polygons of any shape work, holes (loops
    /// inside loops) stay open, and large polygons stay cheap.
    /// </summary>
    internal static class PolygonRaster
    {
        /// <param name="sink">A struct, so nothing is allocated per call.</param>
        /// <param name="crossings">A buffer.</param>
        public static void FillRows<T>(IReadOnlyList<Vector2> polygon, float cellSize, ref T sink, List<float> crossings)
            where T : struct, IRowSink
        {
            if (polygon == null || polygon.Count < 3)
            {
                return;
            }

            GetRows(polygon, cellSize, out var firstRow, out var lastRow);
            for (var row = firstRow; row <= lastRow; row++)
            {
                crossings.Clear();
                AddCrossings(polygon, (row + 0.5f) * cellSize, crossings);
                AddRuns(row, cellSize, crossings, ref sink);
            }
        }

        /// <summary>All loops together: a loop inside another one is a hole in it.</summary>
        public static void FillRows<T>(IReadOnlyList<List<Vector2>> loops, float cellSize, ref T sink,
            List<float> crossings)
            where T : struct, IRowSink
        {
            var firstRow = int.MaxValue;
            var lastRow = int.MinValue;
            for (var i = 0; i < loops.Count; i++)
            {
                if (loops[i].Count < 3)
                {
                    continue;
                }

                GetRows(loops[i], cellSize, out var first, out var last);
                firstRow = Mathf.Min(firstRow, first);
                lastRow = Mathf.Max(lastRow, last);
            }

            for (var row = firstRow; row <= lastRow; row++)
            {
                crossings.Clear();
                for (var i = 0; i < loops.Count; i++)
                {
                    if (loops[i].Count >= 3)
                    {
                        AddCrossings(loops[i], (row + 0.5f) * cellSize, crossings);
                    }
                }

                AddRuns(row, cellSize, crossings, ref sink);
            }
        }

        static void GetRows(IReadOnlyList<Vector2> polygon, float cellSize, out int firstRow, out int lastRow)
        {
            var minZ = polygon[0].y;
            var maxZ = polygon[0].y;
            for (var i = 1; i < polygon.Count; i++)
            {
                minZ = Mathf.Min(minZ, polygon[i].y);
                maxZ = Mathf.Max(maxZ, polygon[i].y);
            }

            firstRow = Mathf.FloorToInt(minZ / cellSize);
            lastRow = Mathf.FloorToInt(maxZ / cellSize);
        }

        static void AddCrossings(IReadOnlyList<Vector2> polygon, float z, List<float> crossings)
        {
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                var a = polygon[i];
                var b = polygon[j];
                if ((a.y > z) != (b.y > z))
                {
                    crossings.Add(a.x + (z - a.y) * (b.x - a.x) / (b.y - a.y));
                }
            }
        }

        static void AddRuns<T>(int row, float cellSize, List<float> crossings, ref T sink)
            where T : struct, IRowSink
        {
            crossings.Sort();
            for (var k = 0; k + 1 < crossings.Count; k += 2)
            {
                // Cells whose centers lie between the two crossings.
                var first = Mathf.CeilToInt(crossings[k] / cellSize - 0.5f);
                var last = Mathf.FloorToInt(crossings[k + 1] / cellSize - 0.5f);
                if (first <= last)
                {
                    sink.AddRun(row, first, last);
                }
            }
        }
    }
}
