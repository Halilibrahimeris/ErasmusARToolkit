using System.Collections.Generic;
using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// The smallest rectangle around a set of points on the floor, turned as it fits best (rotating
    /// calipers over the convex hull). Furniture mostly stands along the walls, so a rectangle along a
    /// preferred direction is taken instead when it is nearly as small.
    /// </summary>
    public static class MinAreaRectangle
    {
        /// <summary>A rectangle along the preferred direction may be this much larger than the smallest one.</summary>
        public const float PreferredSlack = 1.15f;

        [System.ThreadStatic] static List<Vector2> sorted;
        [System.ThreadStatic] static List<Vector2> hull;

        /// <summary>False for fewer than three points, or points on one line.</summary>
        /// <param name="preferredYaw">Direction (degrees around the vertical axis) rectangles should follow.</param>
        /// <param name="center">Center of the rectangle (x and z in meters).</param>
        /// <param name="size">Its sides along its own x and z axes.</param>
        /// <param name="yaw">Its direction, in degrees around the vertical axis.</param>
        public static bool Fit(IReadOnlyList<Vector2> points, float preferredYaw, out Vector2 center, out Vector2 size,
            out float yaw)
        {
            center = default;
            size = default;
            yaw = preferredYaw;
            if (points == null || points.Count < 3)
            {
                return false;
            }

            hull ??= new List<Vector2>();
            ConvexHull(points, hull);
            if (hull.Count < 3)
            {
                return false;
            }

            var bestArea = float.MaxValue;
            var bestYaw = preferredYaw;
            for (int i = 0, j = hull.Count - 1; i < hull.Count; j = i++)
            {
                var edge = hull[i] - hull[j];
                if (edge.sqrMagnitude < 1e-10f)
                {
                    continue;
                }

                // The x axis of the rectangle lies along the edge.
                var candidate = Mathf.Atan2(-edge.y, edge.x) * Mathf.Rad2Deg;
                var area = Measure(hull, candidate, out _, out _);
                if (area < bestArea)
                {
                    bestArea = area;
                    bestYaw = candidate;
                }
            }

            if (bestArea == float.MaxValue)
            {
                return false;
            }

            var preferredArea = Measure(hull, preferredYaw, out _, out _);
            yaw = preferredArea <= PreferredSlack * bestArea ? preferredYaw : Nearest(bestYaw, preferredYaw);
            Measure(hull, yaw, out center, out size);
            return size.x > 0f && size.y > 0f;
        }

        /// <summary>The convex hull of the points, counter-clockwise (Andrew's monotone chain).</summary>
        public static void ConvexHull(IReadOnlyList<Vector2> points, List<Vector2> output)
        {
            output.Clear();
            sorted ??= new List<Vector2>();
            sorted.Clear();
            for (var i = 0; i < points.Count; i++)
            {
                sorted.Add(points[i]);
            }

            sorted.Sort(Compare);
            for (var i = 0; i < sorted.Count; i++)
            {
                while (output.Count >= 2 && Cross(output[output.Count - 2], output[output.Count - 1], sorted[i]) <= 0f)
                {
                    output.RemoveAt(output.Count - 1);
                }

                output.Add(sorted[i]);
            }

            var lower = output.Count + 1;
            for (var i = sorted.Count - 2; i >= 0; i--)
            {
                while (output.Count >= lower && Cross(output[output.Count - 2], output[output.Count - 1], sorted[i]) <= 0f)
                {
                    output.RemoveAt(output.Count - 1);
                }

                output.Add(sorted[i]);
            }

            if (output.Count > 1)
            {
                output.RemoveAt(output.Count - 1);
            }
        }

        /// <summary>The axes of a rectangle turned by <paramref name="yaw"/> degrees around the vertical axis.</summary>
        public static void AxesOf(float yaw, out Vector2 axisX, out Vector2 axisZ)
        {
            var radians = yaw * Mathf.Deg2Rad;
            var cos = Mathf.Cos(radians);
            var sin = Mathf.Sin(radians);

            // As Quaternion.Euler(0, yaw, 0) turns the x and z axes, seen from above.
            axisX = new Vector2(cos, -sin);
            axisZ = new Vector2(sin, cos);
        }

        /// <summary>Area of the bounding rectangle of the points in a frame turned by <paramref name="yaw"/>.</summary>
        static float Measure(List<Vector2> points, float yaw, out Vector2 center, out Vector2 size)
        {
            AxesOf(yaw, out var axisX, out var axisZ);
            var minX = float.MaxValue;
            var maxX = float.MinValue;
            var minZ = float.MaxValue;
            var maxZ = float.MinValue;
            for (var i = 0; i < points.Count; i++)
            {
                var alongX = Vector2.Dot(points[i], axisX);
                var alongZ = Vector2.Dot(points[i], axisZ);
                minX = Mathf.Min(minX, alongX);
                maxX = Mathf.Max(maxX, alongX);
                minZ = Mathf.Min(minZ, alongZ);
                maxZ = Mathf.Max(maxZ, alongZ);
            }

            size = new Vector2(maxX - minX, maxZ - minZ);
            center = axisX * ((minX + maxX) * 0.5f) + axisZ * ((minZ + maxZ) * 0.5f);
            return size.x * size.y;
        }

        /// <summary>
        /// A rectangle looks the same when it is turned by 90 degrees (with its sides swapped). Of the
        /// four ways to name its direction, the one nearest to the preferred direction is taken, so
        /// boxes do not flip between redraws.
        /// </summary>
        static float Nearest(float yaw, float preferredYaw)
        {
            var difference = Mathf.DeltaAngle(preferredYaw, yaw);
            var quarter = Mathf.Round(difference / 90f) * 90f;
            return preferredYaw + (difference - quarter);
        }

        static int Compare(Vector2 a, Vector2 b)
        {
            var byX = a.x.CompareTo(b.x);
            return byX != 0 ? byX : a.y.CompareTo(b.y);
        }

        static float Cross(Vector2 origin, Vector2 a, Vector2 b)
        {
            return (a.x - origin.x) * (b.y - origin.y) - (a.y - origin.y) * (b.x - origin.x);
        }
    }
}
