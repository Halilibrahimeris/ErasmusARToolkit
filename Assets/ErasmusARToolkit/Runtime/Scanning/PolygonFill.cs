using System.Collections.Generic;
using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// Fills outlines with triangles. The outlines are cut into strips along the direction of the room,
    /// and every strip into trapezoids between the edges that cross it (even-odd rule: a loop inside
    /// another one is a hole). A table that lies along the walls is one rectangle that way, whatever
    /// its direction in the space of the scan.
    /// </summary>
    internal sealed class PolygonFill
    {
        const float SameHeight = 1e-5f;

        readonly List<float> levels = new List<float>();
        readonly List<Edge> edges = new List<Edge>();
        readonly List<Span> spans = new List<Span>();

        /// <param name="loops">Closed outlines (x and z in meters).</param>
        /// <param name="yaw">Direction of the strips, in degrees around the vertical axis.</param>
        /// <param name="vertices">The triangles are added, at height 0.</param>
        public void Trapezoids(IReadOnlyList<List<Vector2>> loops, float yaw, List<Vector3> vertices, List<int> triangles)
        {
            MinAreaRectangle.AxesOf(yaw, out var axisX, out var axisZ);
            levels.Clear();
            edges.Clear();
            for (var l = 0; l < loops.Count; l++)
            {
                var loop = loops[l];
                if (loop.Count < 3)
                {
                    continue;
                }

                for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
                {
                    var from = new Vector2(Vector2.Dot(loop[j], axisX), Vector2.Dot(loop[j], axisZ));
                    var to = new Vector2(Vector2.Dot(loop[i], axisX), Vector2.Dot(loop[i], axisZ));
                    levels.Add(to.y);
                    if (Mathf.Abs(from.y - to.y) > SameHeight)
                    {
                        edges.Add(from.y < to.y ? new Edge(from, to) : new Edge(to, from));
                    }
                }
            }

            levels.Sort();
            for (var i = 0; i + 1 < levels.Count; i++)
            {
                var low = levels[i];
                var high = levels[i + 1];
                if (high - low <= SameHeight)
                {
                    continue;
                }

                AddStrip(low, high, axisX, axisZ, vertices, triangles);
            }
        }

        void AddStrip(float low, float high, Vector2 axisX, Vector2 axisZ, List<Vector3> vertices, List<int> triangles)
        {
            spans.Clear();
            var middle = (low + high) * 0.5f;
            for (var i = 0; i < edges.Count; i++)
            {
                var edge = edges[i];
                if (edge.Low.y <= middle && edge.High.y > middle)
                {
                    spans.Add(new Span(edge.At(low), edge.At(high), edge.At(middle)));
                }
            }

            spans.Sort(Span.ByMiddle);
            for (var i = 0; i + 1 < spans.Count; i += 2)
            {
                var left = spans[i];
                var right = spans[i + 1];
                if (right.Low - left.Low <= SameHeight && right.High - left.High <= SameHeight)
                {
                    continue;
                }

                var start = vertices.Count;
                vertices.Add(ToWorld(left.Low, low, axisX, axisZ));
                vertices.Add(ToWorld(left.High, high, axisX, axisZ));
                vertices.Add(ToWorld(right.High, high, axisX, axisZ));
                vertices.Add(ToWorld(right.Low, low, axisX, axisZ));
                triangles.Add(start);
                triangles.Add(start + 1);
                triangles.Add(start + 2);
                triangles.Add(start);
                triangles.Add(start + 2);
                triangles.Add(start + 3);
            }
        }

        static Vector3 ToWorld(float along, float across, Vector2 axisX, Vector2 axisZ)
        {
            var point = axisX * along + axisZ * across;
            return new Vector3(point.x, 0f, point.y);
        }

        readonly struct Edge
        {
            public Edge(Vector2 low, Vector2 high)
            {
                Low = low;
                High = high;
            }

            public Vector2 Low { get; }
            public Vector2 High { get; }

            /// <summary>Where the edge is at a height (the edge is clamped to its ends).</summary>
            public float At(float height)
            {
                var t = Mathf.Clamp01((height - Low.y) / (High.y - Low.y));
                return Mathf.LerpUnclamped(Low.x, High.x, t);
            }
        }

        readonly struct Span
        {
            public static readonly System.Comparison<Span> ByMiddle = (a, b) => a.Middle.CompareTo(b.Middle);

            public Span(float low, float high, float middle)
            {
                Low = low;
                High = high;
                Middle = middle;
            }

            public float Low { get; }
            public float High { get; }
            public float Middle { get; }
        }
    }
}
