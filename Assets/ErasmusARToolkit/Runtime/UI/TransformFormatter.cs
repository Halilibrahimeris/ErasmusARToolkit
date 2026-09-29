using System.Globalization;
using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>Short, readable transform values for the edit panel.</summary>
    public static class TransformFormatter
    {
        public static string Format(Vector3 position, float yawDegrees, float scaleMultiplier)
        {
            var culture = CultureInfo.InvariantCulture;
            var yaw = Mathf.RoundToInt(Mathf.Repeat(yawDegrees, 360f)) % 360;
            return string.Format(culture,
                "Position  X {0:0.00}  Y {1:0.00}  Z {2:0.00} m\nRotation {3}°    Scale {4:0.00}x",
                position.x, position.y, position.z, yaw, scaleMultiplier);
        }
    }
}
