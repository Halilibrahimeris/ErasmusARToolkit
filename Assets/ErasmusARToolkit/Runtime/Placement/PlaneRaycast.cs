using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>Intersects a ray with a finite horizontal square. Used by the Desktop virtual ground.</summary>
    public static class PlaneRaycast
    {
        public static bool TryIntersect(Ray ray, float planeHeight, float halfExtent, out Vector3 point)
        {
            var plane = new Plane(Vector3.up, new Vector3(0f, planeHeight, 0f));
            if (!plane.Raycast(ray, out var distance) || distance <= 0f)
            {
                point = default;
                return false;
            }

            point = ray.GetPoint(distance);
            return Mathf.Abs(point.x) <= halfExtent && Mathf.Abs(point.z) <= halfExtent;
        }
    }
}
