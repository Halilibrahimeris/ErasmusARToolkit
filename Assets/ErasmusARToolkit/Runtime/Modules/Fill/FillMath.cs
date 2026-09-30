using System;
using System.Collections.Generic;
using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>How a room fill rises. Pure math, so it can be tested.</summary>
    public static class FillLevel
    {
        /// <summary>The level after <paramref name="deltaSeconds"/> when it rises from 0 to the top in <paramref name="durationSeconds"/>.</summary>
        public static float Advance(float level, float maxLevel, float durationSeconds, float deltaSeconds)
        {
            if (durationSeconds <= 0f || maxLevel <= 0f)
            {
                return Clamp(level, maxLevel);
            }

            return Clamp(level + maxLevel * deltaSeconds / durationSeconds, maxLevel);
        }

        public static float Clamp(float level, float maxLevel) => Mathf.Clamp(level, 0f, Mathf.Max(0f, maxLevel));

        /// <summary>The level for a slider at <paramref name="fraction"/> (0..1).</summary>
        public static float FromFraction(float fraction, float maxLevel) => Mathf.Clamp01(fraction) * Mathf.Max(0f, maxLevel);

        public static float Fraction(float level, float maxLevel) => maxLevel <= 0f ? 0f : Mathf.Clamp01(level / maxLevel);
    }

    /// <summary>
    /// Where a room fill lies, in the space of the module (x and z): the scanned floor, or a rectangle. Loops
    /// inside other loops are holes.
    /// </summary>
    public sealed class FillArea
    {
        readonly List<List<Vector2>> loops;

        FillArea(List<List<Vector2>> areaLoops)
        {
            loops = areaLoops;
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            foreach (var loop in loops)
            {
                foreach (var point in loop)
                {
                    min = Vector2.Min(min, point);
                    max = Vector2.Max(max, point);
                }
            }

            Bounds = loops.Count > 0 ? Rect.MinMaxRect(min.x, min.y, max.x, max.y) : Rect.zero;
        }

        public IReadOnlyList<List<Vector2>> Loops => loops;

        public Rect Bounds { get; }

        public bool IsScanned { get; private set; }

        public float Area => PolygonMath.Area(loops);

        public bool Contains(Vector2 point) => PolygonMath.Contains(loops, point);

        /// <summary>A rectangle of <paramref name="size"/> meters around the module's origin.</summary>
        public static FillArea Rectangle(Vector2 size)
        {
            var half = new Vector2(Mathf.Max(0.1f, size.x), Mathf.Max(0.1f, size.y)) * 0.5f;
            return new FillArea(new List<List<Vector2>>
            {
                new List<Vector2> { new Vector2(-half.x, -half.y), new Vector2(half.x, -half.y), new Vector2(half.x, half.y), new Vector2(-half.x, half.y) },
            });
        }

        /// <summary>World loops (x, z) seen from the module: in the space of <paramref name="root"/>.</summary>
        public static FillArea FromWorld(IReadOnlyList<List<Vector2>> worldLoops, Transform root)
        {
            var local = new List<List<Vector2>>(worldLoops.Count);
            foreach (var loop in worldLoops)
            {
                var converted = new List<Vector2>(loop.Count);
                foreach (var point in loop)
                {
                    var position = root != null ? root.InverseTransformPoint(new Vector3(point.x, root.position.y, point.y)) : new Vector3(point.x, 0f, point.y);
                    converted.Add(new Vector2(position.x, position.z));
                }

                local.Add(converted);
            }

            return new FillArea(local) { IsScanned = true };
        }
    }

    /// <summary>
    /// Objects piled up in a room fill (bottles, bags): layer after layer on a grid inside the area, never
    /// inside what stands in the room. The top layer fills up as the level rises. Pure logic, so it can be tested.
    /// </summary>
    public static class ObjectPile
    {
        /// <summary>More objects than this would make a phone slow.</summary>
        public const int MaxCount = 1500;

        /// <param name="blocked">True for a point (module space x, z) where something stands; null: nothing.</param>
        /// <param name="output">Positions (module space) of the objects' centers.</param>
        public static int Layout(FillArea area, float spacing, float layerHeight, float level, Func<Vector2, bool> blocked,
            int seed, List<Vector3> output, List<float> yaws = null, int maxCount = MaxCount)
        {
            output.Clear();
            yaws?.Clear();
            if (area == null || spacing <= 0f || layerHeight <= 0f || level <= 0f)
            {
                return 0;
            }

            var cells = Cells(area, spacing, blocked, seed);
            if (cells.Count == 0)
            {
                return 0;
            }

            var random = new System.Random(seed);
            var jitter = spacing * 0.2f;
            var layers = level / layerHeight;
            var fullLayers = Mathf.FloorToInt(layers);
            var topCount = Mathf.RoundToInt((layers - fullLayers) * cells.Count);
            for (var layer = 0; layer <= fullLayers; layer++)
            {
                var count = layer < fullLayers ? cells.Count : topCount;
                for (var i = 0; i < count; i++)
                {
                    if (output.Count >= maxCount)
                    {
                        return output.Count;
                    }

                    var cell = cells[i];
                    var offset = new Vector2(((float)random.NextDouble() * 2f - 1f) * jitter, ((float)random.NextDouble() * 2f - 1f) * jitter);
                    output.Add(new Vector3(cell.x + offset.x, (layer + 0.5f) * layerHeight, cell.y + offset.y));
                    yaws?.Add((float)random.NextDouble() * 360f);
                }
            }

            return output.Count;
        }

        /// <summary>Grid cells inside the area and free, in a fixed shuffled order (the top layer fills evenly).</summary>
        internal static List<Vector2> Cells(FillArea area, float spacing, Func<Vector2, bool> blocked, int seed)
        {
            var cells = new List<Vector2>();
            var bounds = area.Bounds;
            for (var x = bounds.xMin + spacing * 0.5f; x < bounds.xMax; x += spacing)
            {
                for (var z = bounds.yMin + spacing * 0.5f; z < bounds.yMax; z += spacing)
                {
                    var point = new Vector2(x, z);
                    if (area.Contains(point) && (blocked == null || !blocked(point)))
                    {
                        cells.Add(point);
                    }
                }
            }

            var random = new System.Random(seed ^ 0x5bd1e995);
            for (var i = cells.Count - 1; i > 0; i--)
            {
                var j = random.Next(i + 1);
                (cells[i], cells[j]) = (cells[j], cells[i]);
            }

            return cells;
        }
    }

    /// <summary>The flat mesh of an area (x, z at height 0), for water and smoke layers.</summary>
    public static class FillSurface
    {
        public static Mesh Build(FillArea area, Mesh mesh = null)
        {
            mesh = mesh != null ? mesh : new Mesh { name = "Fill Surface" };
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            new PolygonFill().Trapezoids(area.Loops, 0f, vertices, triangles);
            mesh.Clear();
            mesh.indexFormat = vertices.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
