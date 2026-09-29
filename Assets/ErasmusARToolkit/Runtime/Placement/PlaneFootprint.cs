using Unity.Collections;
using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>Where a horizontal AR plane lies in the world: its height and its area seen from above.</summary>
    public readonly struct PlaneFootprint
    {
        public PlaneFootprint(float height, Rect bounds, float area)
        {
            Height = height;
            Bounds = bounds;
            Area = area;
        }

        /// <summary>World height (y) of the plane center in meters.</summary>
        public float Height { get; }

        /// <summary>World-space bounds seen from above: x is world x, y is world z.</summary>
        public Rect Bounds { get; }

        /// <summary>Plane area in square meters. Only used to rank planes, so an estimate is fine.</summary>
        public float Area { get; }

        /// <summary>Builds a footprint from a plane's boundary (plane-local 2D points, as AR Foundation gives them).</summary>
        public static PlaneFootprint FromBoundary(Matrix4x4 localToWorld, NativeArray<Vector2> boundary, float area)
        {
            var center = localToWorld.GetColumn(3);
            if (boundary.Length == 0)
            {
                return new PlaneFootprint(center.y, new Rect(center.x, center.z, 0f, 0f), area);
            }

            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            for (var i = 0; i < boundary.Length; i++)
            {
                var world = localToWorld.MultiplyPoint3x4(new Vector3(boundary[i].x, 0f, boundary[i].y));
                min = Vector2.Min(min, new Vector2(world.x, world.z));
                max = Vector2.Max(max, new Vector2(world.x, world.z));
            }

            return new PlaneFootprint(center.y, Rect.MinMaxRect(min.x, min.y, max.x, max.y), area);
        }
    }
}
