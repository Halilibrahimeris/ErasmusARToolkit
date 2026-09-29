using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>Camera position for an orbit around a pivot point.</summary>
    public static class OrbitMath
    {
        /// <param name="yawDegrees">Rotation around the vertical axis. 0 looks along +Z.</param>
        /// <param name="pitchDegrees">Angle above the horizon. 90 looks straight down.</param>
        public static Vector3 ComputePosition(Vector3 pivot, float yawDegrees, float pitchDegrees, float distance)
        {
            var rotation = Quaternion.Euler(pitchDegrees, yawDegrees, 0f);
            return pivot - rotation * Vector3.forward * distance;
        }
    }
}
