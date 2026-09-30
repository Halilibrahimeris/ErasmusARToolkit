using System.Collections.Generic;
using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>Small helpers for polygons on the floor (x and z in meters).</summary>
    internal static class PolygonMath
    {
        /// <summary>Area of a closed polygon: positive when it runs counter-clockwise (x to the right, z up).</summary>
        public static float SignedArea(IReadOnlyList<Vector2> loop)
        {
            var sum = 0f;
            for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
            {
                sum += loop[j].x * loop[i].y - loop[i].x * loop[j].y;
            }

            return sum * 0.5f;
        }

        public static float Area(IReadOnlyList<Vector2> loop) => Mathf.Abs(SignedArea(loop));

        /// <summary>Area of what the loops enclose together, holes taken out (outer loops and holes run opposite ways).</summary>
        public static float Area(IReadOnlyList<List<Vector2>> loops)
        {
            var sum = 0f;
            for (var i = 0; i < loops.Count; i++)
            {
                sum += SignedArea(loops[i]);
            }

            return Mathf.Abs(sum);
        }

        public static float Perimeter(IReadOnlyList<Vector2> loop)
        {
            var sum = 0f;
            for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
            {
                sum += Vector2.Distance(loop[i], loop[j]);
            }

            return sum;
        }

        public static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            var along = b - a;
            var length = along.sqrMagnitude;
            if (length < 1e-12f)
            {
                return Vector2.Distance(point, a);
            }

            var t = Mathf.Clamp01(Vector2.Dot(point - a, along) / length);
            return Vector2.Distance(point, a + along * t);
        }

        /// <summary>Distance from a point to the outline of a closed polygon.</summary>
        public static float DistanceToOutline(Vector2 point, IReadOnlyList<Vector2> loop)
        {
            var nearest = float.MaxValue;
            for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
            {
                nearest = Mathf.Min(nearest, DistanceToSegment(point, loop[j], loop[i]));
            }

            return nearest;
        }

        /// <summary>The point lies inside the loops (even-odd rule: inside a hole is outside).</summary>
        public static bool Contains(IReadOnlyList<List<Vector2>> loops, Vector2 point)
        {
            var inside = false;
            for (var l = 0; l < loops.Count; l++)
            {
                var loop = loops[l];
                for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
                {
                    var a = loop[i];
                    var b = loop[j];
                    if ((a.y > point.y) != (b.y > point.y)
                        && point.x < a.x + (point.y - a.y) * (b.x - a.x) / (b.y - a.y))
                    {
                        inside = !inside;
                    }
                }
            }

            return inside;
        }

        /// <summary>Where two lines cross (each given by a point and a direction). False when they are parallel.</summary>
        public static bool Intersect(Vector2 pointA, Vector2 directionA, Vector2 pointB, Vector2 directionB,
            out Vector2 crossing)
        {
            var cross = directionA.x * directionB.y - directionA.y * directionB.x;
            if (Mathf.Abs(cross) < 1e-6f)
            {
                crossing = default;
                return false;
            }

            var between = pointB - pointA;
            var t = (between.x * directionB.y - between.y * directionB.x) / cross;
            crossing = pointA + directionA * t;
            return true;
        }

        /// <summary>Two closed segments cross or touch (shared end points do not count).</summary>
        public static bool SegmentsCross(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            var d1 = Side(c, d, a);
            var d2 = Side(c, d, b);
            var d3 = Side(a, b, c);
            var d4 = Side(a, b, d);
            return d1 * d2 < 0f && d3 * d4 < 0f;
        }

        /// <summary>No two edges of the loop cross each other.</summary>
        public static bool IsSimple(IReadOnlyList<Vector2> loop)
        {
            var count = loop.Count;
            for (var i = 0; i < count; i++)
            {
                var a = loop[i];
                var b = loop[(i + 1) % count];
                for (var j = i + 2; j < count; j++)
                {
                    if (i == 0 && j == count - 1)
                    {
                        continue;
                    }

                    if (SegmentsCross(a, b, loop[j], loop[(j + 1) % count]))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        static float Side(Vector2 a, Vector2 b, Vector2 point)
        {
            return (b.x - a.x) * (point.y - a.y) - (b.y - a.y) * (point.x - a.x);
        }
    }
}
