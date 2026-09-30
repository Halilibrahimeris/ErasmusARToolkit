using System.Collections.Generic;
using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// Something that can also say where a screen point hits a wall (for pictures and other things that
    /// hang on walls).
    /// </summary>
    public interface IWallPlacementSurface
    {
        /// <summary>
        /// The wall under a screen position. The pose lies on the wall; its forward (+Z) points out of the
        /// wall into the room, its up is the world's up.
        /// </summary>
        bool TryGetWallPose(Vector2 screenPosition, out Pose pose);
    }

    /// <summary>Messages about walls.</summary>
    public static class WallText
    {
        public const string NoWallAR = "No wall there yet. Point the phone at the wall for a moment, or scan the room.";
        public const string NoWallDesktop = "Click on the wall.";
    }

    /// <summary>Wall hits and wall poses. Pure math, so it can be tested.</summary>
    public static class WallRaycast
    {
        /// <summary>Pictures can hang this high on a scanned wall even where the scan saw less of it.</summary>
        public const float MinWallHeight = 2.4f;

        /// <summary>The nearest scanned wall the ray hits, as a wall pose facing the ray's origin.</summary>
        public static bool TryHit(Ray ray, IReadOnlyList<WallSegment> walls, out Pose pose)
        {
            pose = default;
            if (walls == null)
            {
                return false;
            }

            var best = float.MaxValue;
            var found = false;
            var normal = Vector3.zero;
            for (var i = 0; i < walls.Count; i++)
            {
                var wall = walls[i];
                if (TryHit(ray, wall.A, wall.B, wall.Bottom, Mathf.Max(wall.Top, wall.Bottom + MinWallHeight),
                        out var distance) && distance < best)
                {
                    best = distance;
                    found = true;
                    var along = wall.Direction;
                    normal = new Vector3(-along.y, 0f, along.x);
                }
            }

            if (!found)
            {
                return false;
            }

            pose = WallPose(ray.GetPoint(best), normal, ray.origin);
            return true;
        }

        /// <summary>Where a ray goes through a wall from <paramref name="a"/> to <paramref name="b"/> (x, z on the floor).</summary>
        public static bool TryHit(Ray ray, Vector2 a, Vector2 b, float bottom, float top, out float distance)
        {
            distance = 0f;
            var along = b - a;
            var length = along.magnitude;
            if (length < 1e-4f)
            {
                return false;
            }

            along /= length;
            var normal = new Vector2(-along.y, along.x);
            var approach = ray.direction.x * normal.x + ray.direction.z * normal.y;
            if (Mathf.Abs(approach) < 1e-6f)
            {
                return false;
            }

            var from = new Vector2(ray.origin.x, ray.origin.z) - a;
            distance = -Vector2.Dot(from, normal) / approach;
            if (distance <= 0f)
            {
                return false;
            }

            var point = ray.GetPoint(distance);
            var on = Vector2.Dot(new Vector2(point.x, point.z) - a, along);
            return on >= 0f && on <= length && point.y >= bottom && point.y <= top;
        }

        /// <summary>
        /// A pose on a wall: forward along the wall's normal, turned to the side of <paramref name="viewer"/>,
        /// up along the world's up.
        /// </summary>
        public static Pose WallPose(Vector3 point, Vector3 normal, Vector3 viewer)
        {
            normal.y = 0f;
            var toViewer = viewer - point;
            toViewer.y = 0f;
            if (normal.sqrMagnitude < 1e-8f)
            {
                normal = toViewer;
            }

            if (normal.sqrMagnitude < 1e-8f)
            {
                normal = Vector3.back;
            }

            normal.Normalize();
            if (Vector3.Dot(normal, toViewer) < 0f)
            {
                normal = -normal;
            }

            return new Pose(point, Quaternion.LookRotation(normal, Vector3.up));
        }

        /// <summary>
        /// Where a ray hits an upright rectangle (a virtual wall) of <paramref name="halfWidth"/> around
        /// <paramref name="center"/>, from <paramref name="bottom"/> to <paramref name="top"/>.
        /// </summary>
        public static bool TryIntersectRectangle(Ray ray, Vector3 center, Vector3 normal, float halfWidth, float bottom,
            float top, out Vector3 point)
        {
            point = default;
            var denominator = Vector3.Dot(ray.direction, normal);
            if (Mathf.Abs(denominator) < 1e-6f)
            {
                return false;
            }

            var distance = Vector3.Dot(center - ray.origin, normal) / denominator;
            if (distance <= 0f)
            {
                return false;
            }

            point = ray.GetPoint(distance);
            var right = Vector3.Cross(Vector3.up, normal);
            if (right.sqrMagnitude < 1e-8f)
            {
                return false;
            }

            var sideways = Vector3.Dot(point - center, right.normalized);
            return Mathf.Abs(sideways) <= halfWidth && point.y >= bottom && point.y <= top;
        }
    }
}
