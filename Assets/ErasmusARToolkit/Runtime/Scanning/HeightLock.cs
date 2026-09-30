using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// The height a surface is drawn at. The measured height moves a little with every frame; drawing it
    /// as it comes would make the grid, and everything standing on it, swim. The drawn height holds still
    /// until the measured one has left a dead band for a few redraws, and then follows at a slow, even
    /// pace. A floor that ARCore detected as a plane may not sink more than a few centimeters below that
    /// plane: depth on shiny floors is measured too low, never too high. When the plane itself moves
    /// down, the floor follows.
    /// <para>A struct that changes itself: keep it in a field, never in a property.</para>
    /// </summary>
    internal struct HeightLock
    {
        /// <summary>The drawn height stays while the measured one is this close (meters).</summary>
        public const float DeadBand = 0.015f;

        /// <summary>Redraws in a row outside the dead band before the drawn height starts to move.</summary>
        public const int PendingRedraws = 3;

        /// <summary>Pace of the drawn height in meters per second.</summary>
        public const float Slew = 0.02f;

        /// <summary>This close (meters) to the measured height the move is over.</summary>
        public const float Settle = 0.005f;

        /// <summary>A floor sinks at most this far (meters) below the plane it last agreed with.</summary>
        public const float DownLimit = 0.03f;

        /// <summary>The plane agrees with a drawn height that is this close (meters) to it.</summary>
        public const float PlaneAgreement = 0.02f;

        /// <summary>After the map moved, a change is followed at once for this many redraws.</summary>
        public const int HurriedRedraws = 8;

        float agreedPlane;
        bool hasAgreed;
        int pending;
        int hurried;
        bool moving;

        public bool HasValue { get; private set; }

        public float Value { get; private set; }

        /// <summary>Returns true when the drawn height changed.</summary>
        /// <param name="target">The measured height.</param>
        /// <param name="seconds">Time since the last call.</param>
        /// <param name="plane">Height of the plane ARCore detected for this surface, if any.</param>
        /// <param name="limitDown">The surface is the floor: it sinks only as far as its plane allows.</param>
        public bool Update(float target, float seconds, float? plane, bool limitDown)
        {
            var isFirst = !HasValue;
            if (isFirst)
            {
                Value = target;
                HasValue = true;
            }

            if (plane.HasValue && Mathf.Abs(Value - plane.Value) <= PlaneAgreement)
            {
                agreedPlane = plane.Value;
                hasAgreed = true;
            }

            if (isFirst)
            {
                return true;
            }

            if (limitDown && hasAgreed)
            {
                // A plane that is lower now than when it agreed takes the limit down with it.
                var lowest = (plane.HasValue ? Mathf.Min(plane.Value, agreedPlane) : agreedPlane) - DownLimit;
                target = Mathf.Max(target, lowest);
            }

            var delta = target - Value;
            var needed = hurried > 0 ? 1 : PendingRedraws;
            hurried = Mathf.Max(hurried - 1, 0);
            if (!moving)
            {
                if (Mathf.Abs(delta) <= DeadBand)
                {
                    pending = 0;
                    return false;
                }

                if (++pending < needed)
                {
                    return false;
                }

                moving = true;
            }

            var step = Slew * Mathf.Max(seconds, 0f);
            Value += Mathf.Clamp(delta, -step, step);
            if (Mathf.Abs(target - Value) <= Settle)
            {
                moving = false;
                pending = 0;
            }

            return true;
        }

        /// <summary>
        /// ARCore corrected its map, and the surface moved with it. A measured height that leaves the
        /// dead band in the next redraws is no noise: the drawn height follows without waiting.
        /// </summary>
        public void Hurry()
        {
            hurried = HurriedRedraws;
        }

        /// <summary>The surface starts over (its level merged into another one, or the scan was reset).</summary>
        public void Reset()
        {
            this = default;
        }
    }
}
