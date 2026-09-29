using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// How exact ARCore depth is, and the tolerances the room scan takes from it. The error of depth from
    /// motion grows with the square of the distance: about 1.5 cm at 1 m and 5.5 cm at 3 m. A fixed
    /// tolerance would be too loose nearby and too tight far away. The two numbers are a starting point:
    /// the scan log writes the measured error per distance ("Scan floor depth error") to correct them.
    /// </summary>
    public static class DepthNoise
    {
        /// <summary>Error (meters) of a depth point right in front of the camera.</summary>
        public const float Base = 0.010f;

        /// <summary>How fast the error grows: meters of error per square meter of distance.</summary>
        public const float K = 0.005f;

        public const float MinHitTolerance = 0.03f;
        public const float MaxHitTolerance = 0.06f;
        public const float MinMissMargin = 0.06f;
        public const float MinBand = 0.04f;

        /// <summary>A height sample with this error (meters) or less counts fully.</summary>
        const float FullWeightSigma = 0.015f;

        /// <summary>Expected error (one standard deviation, meters) of a depth point this far away.</summary>
        public static float Sigma(float distance)
        {
            return Base + K * distance * distance;
        }

        /// <summary>A point this close (meters) to a surface height lies on the surface.</summary>
        public static float HitTolerance(float distance)
        {
            return Mathf.Clamp(2f * Sigma(distance), MinHitTolerance, MaxHitTolerance);
        }

        /// <summary>A ray must end this far (meters) past a surface height to prove nothing is there.</summary>
        public static float MissMargin(float distance)
        {
            return Mathf.Max(MinMissMargin, 3f * Sigma(distance));
        }

        /// <summary>A point more than this (meters) below the floor is not the floor measured badly.</summary>
        public static float Band(float distance)
        {
            return Mathf.Max(MinBand, 2.5f * Sigma(distance));
        }

        /// <summary>How much a height sample counts (0..1): less with the square of its expected error.</summary>
        public static float HeightWeight(float distance)
        {
            var ratio = FullWeightSigma / Sigma(distance);
            return Mathf.Min(1f, ratio * ratio);
        }
    }
}
