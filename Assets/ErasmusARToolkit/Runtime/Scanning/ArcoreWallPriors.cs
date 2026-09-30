using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Erasmus.ARToolkit
{
    /// <summary>Hands the vertical planes ARCore detected to the room scan: they are its walls (see <see cref="WallModel"/>).</summary>
    internal static class ArcoreWallPriors
    {
        /// <param name="worldToMap">From world space into the space of the scan (see <see cref="MapFrame"/>).</param>
        public static void Apply(ARPlaneManager planeManager, WallModel walls, List<Vector3> outlineBuffer,
            Matrix4x4 worldToMap)
        {
            walls.BeginPlanes();
            foreach (var plane in planeManager.trackables)
            {
                // A plane with limited tracking is not where ARCore last saw it.
                if (plane.subsumedBy != null || plane.alignment != PlaneAlignment.Vertical
                    || plane.trackingState != TrackingState.Tracking)
                {
                    continue;
                }

                outlineBuffer.Clear();
                var localToMap = worldToMap * plane.transform.localToWorldMatrix;
                foreach (var point in plane.boundary)
                {
                    outlineBuffer.Add(localToMap.MultiplyPoint3x4(new Vector3(point.x, 0f, point.y)));
                }

                walls.AddPlane(outlineBuffer, localToMap.MultiplyVector(Vector3.up));
            }

            walls.EndPlanes();
        }
    }
}
