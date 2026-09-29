using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>Places objects on the virtual ground in Desktop mode. Pure math, no physics needed.</summary>
    public sealed class DesktopPlacementSurface : MonoBehaviour, IPlacementSurface
    {
        [SerializeField] Camera viewCamera;
        [SerializeField] VirtualGround ground;

        internal void Initialize(Camera camera, VirtualGround virtualGround)
        {
            viewCamera = camera;
            ground = virtualGround;
        }

        public bool TryGetPose(Vector2 screenPosition, out Pose pose)
        {
            pose = default;
            if (viewCamera == null || ground == null)
            {
                return false;
            }

            var ray = viewCamera.ScreenPointToRay(screenPosition);
            if (!PlaneRaycast.TryIntersect(ray, ground.Height, ground.HalfExtent, out var point))
            {
                return false;
            }

            pose = new Pose(point, Quaternion.identity);
            return true;
        }
    }
}
