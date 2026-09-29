using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>Something objects can be placed on: AR planes or the Desktop virtual ground.</summary>
    public interface IPlacementSurface
    {
        bool TryGetPose(Vector2 screenPosition, out Pose pose);
    }
}
