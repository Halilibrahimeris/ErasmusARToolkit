using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>Places objects on the virtual ground in Desktop mode. Pure math, no physics needed.</summary>
    public sealed class DesktopPlacementSurface : MonoBehaviour, IPlacementSurface, IWallPlacementSurface
    {
        [SerializeField] Camera viewCamera;
        [SerializeField] VirtualGround ground;

        VirtualWall wall;

        /// <summary>The wall for pictures (null when the config has nothing that hangs on walls).</summary>
        public VirtualWall Wall => wall;

        internal void SetWall(VirtualWall virtualWall)
        {
            wall = virtualWall;
        }

        public bool TryGetWallPose(Vector2 screenPosition, out Pose pose)
        {
            pose = default;
            return viewCamera != null && wall != null && wall.TryGetPose(viewCamera.ScreenPointToRay(screenPosition), out pose);
        }

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
