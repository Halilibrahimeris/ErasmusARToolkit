using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>Pure math used by object manipulation. Kept free of state so it is easy to test.</summary>
    public static class ManipulationMath
    {
        const float Epsilon = 0.0001f;

        /// <summary>
        /// Signed rotation, in degrees, of the line between two fingers from the previous
        /// frame to this one. Counter-clockwise on screen is positive.
        /// </summary>
        public static float TwistDeltaDegrees(Vector2 prevA, Vector2 prevB, Vector2 curA, Vector2 curB)
        {
            var previous = prevB - prevA;
            var current = curB - curA;
            if (previous.sqrMagnitude < Epsilon || current.sqrMagnitude < Epsilon)
            {
                return 0f;
            }

            return Vector2.SignedAngle(previous, current);
        }

        /// <summary>New scale after a pinch, proportional to the change in finger distance.</summary>
        public static float PinchScale(float currentScale, float previousDistance, float currentDistance, Vector2 limits)
        {
            if (previousDistance < Epsilon)
            {
                return ClampScale(currentScale, limits);
            }

            return ClampScale(currentScale * (currentDistance / previousDistance), limits);
        }

        /// <summary>Scale change for discrete steps (mouse wheel notches). Each step multiplies by 1 + stepFactor.</summary>
        public static float StepScale(float currentScale, float steps, float stepFactor, Vector2 limits)
        {
            return ClampScale(currentScale * Mathf.Pow(1f + stepFactor, steps), limits);
        }

        public static float ClampScale(float value, Vector2 limits)
        {
            var min = Mathf.Min(limits.x, limits.y);
            var max = Mathf.Max(limits.x, limits.y);
            return Mathf.Clamp(value, min, max);
        }

        /// <summary>Rotation around the Y axis only, so an object faces the target but stays upright.</summary>
        public static Quaternion YawTowards(Vector3 from, Vector3 target)
        {
            var direction = target - from;
            direction.y = 0f;
            if (direction.sqrMagnitude < Epsilon)
            {
                return Quaternion.identity;
            }

            return Quaternion.LookRotation(direction.normalized, Vector3.up);
        }
    }
}
