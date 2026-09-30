using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// Places objects on detected AR planes. When one real surface was detected as several planes,
    /// the hit is moved to the surface's shared height so every object on it sits at the same level.
    /// After a room scan only the scanned surfaces count, so objects go where the grid is drawn.
    /// </summary>
    [RequireComponent(typeof(ARRaycastManager))]
    public sealed class ARPlacementSurface : MonoBehaviour, IPlacementSurface
    {
        static readonly List<ARRaycastHit> Hits = new List<ARRaycastHit>();

        ARRaycastManager raycastManager;
        ARPlaneSurfaces surfaces;
        RoomScanner scanner;

        void Awake()
        {
            raycastManager = GetComponent<ARRaycastManager>();
            surfaces = GetComponent<ARPlaneSurfaces>();
            scanner = GetComponent<RoomScanner>();
        }

        public bool TryGetPose(Vector2 screenPosition, out Pose pose)
        {
            if (scanner == null)
            {
                scanner = GetComponent<RoomScanner>();
            }

            if (scanner != null && scanner.HasModel)
            {
                return scanner.TryGetPose(screenPosition, out pose);
            }

            if (raycastManager != null && raycastManager.enabled
                && raycastManager.Raycast(screenPosition, Hits, TrackableType.PlaneWithinPolygon))
            {
                // Hits come nearest first. Vertical planes are the walls of the room scan: nothing is
                // placed on them. (Before a scan they are not drawn either, so they are not in the way.)
                var index = FirstHorizontal(Hits, IsHorizontal);
                if (index >= 0)
                {
                    pose = SnapToSurface(Hits[index]);
                    return true;
                }
            }

            pose = default;
            return false;
        }

        /// <summary>Index of the first hit on a horizontal plane, or -1.</summary>
        internal static int FirstHorizontal<T>(IReadOnlyList<T> hits, System.Func<T, bool> isHorizontal)
        {
            for (var i = 0; i < hits.Count; i++)
            {
                if (isHorizontal(hits[i]))
                {
                    return i;
                }
            }

            return -1;
        }

        static bool IsHorizontal(ARRaycastHit hit)
        {
            return !(hit.trackable is ARPlane plane) || plane.alignment.IsHorizontal();
        }

        Pose SnapToSurface(ARRaycastHit hit)
        {
            if (surfaces == null)
            {
                surfaces = GetComponent<ARPlaneSurfaces>();
            }

            var pose = hit.pose;
            if (surfaces == null || !surfaces.TryGetSurfaceHeight(hit.trackableId, out var height))
            {
                return pose;
            }

            return new Pose(new Vector3(pose.position.x, height, pose.position.z), pose.rotation);
        }
    }
}
