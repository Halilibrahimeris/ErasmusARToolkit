using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Erasmus.ARToolkit
{
    /// <summary>Hands the upward-facing ARCore planes to the room scan as a head start (see <see cref="RoomSurfaceMap.AddPrior"/>).</summary>
    internal static class ArcorePlanePriors
    {
        public static void Apply(ARPlaneManager planeManager, RoomSurfaceMap map, List<Vector2> polygonBuffer)
        {
            Apply(planeManager, map, polygonBuffer, Matrix4x4.identity);
        }

        /// <param name="worldToMap">From world space into the space of the map (see <see cref="MapFrame"/>).</param>
        public static void Apply(ARPlaneManager planeManager, RoomSurfaceMap map, List<Vector2> polygonBuffer,
            Matrix4x4 worldToMap)
        {
            map.BeginPriors();
            foreach (var plane in planeManager.trackables)
            {
                // A plane with limited tracking is not where ARCore last saw it.
                if (plane.subsumedBy != null || plane.alignment != PlaneAlignment.HorizontalUp
                    || plane.trackingState != TrackingState.Tracking)
                {
                    continue;
                }

                polygonBuffer.Clear();
                var localToMap = worldToMap * plane.transform.localToWorldMatrix;
                foreach (var point in plane.boundary)
                {
                    var inMap = localToMap.MultiplyPoint3x4(new Vector3(point.x, 0f, point.y));
                    polygonBuffer.Add(new Vector2(inMap.x, inMap.z));
                }

                map.AddPrior(worldToMap.MultiplyPoint3x4(plane.transform.position).y, polygonBuffer);
            }

            map.EndPriors();
        }
    }
}
