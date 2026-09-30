using System.Collections.Generic;
using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// The direction of the walls of the room. Rooms are mostly rectangles and furniture mostly stands
    /// along the walls, so the edges of the scanned surfaces point in two directions that are at right
    /// angles. Those directions are found from the long edges (longer edges count more), and the
    /// outlines, the boxes around obstacles and the grid are laid along them. The direction changes
    /// slowly, so nothing turns with every redraw.
    /// </summary>
    internal sealed class RoomAxes
    {
        /// <summary>Shorter edges (meters) say nothing about the room.</summary>
        public const float MinEdge = 0.3f;

        /// <summary>Edges of this length (meters) in all are needed before a direction is given.</summary>
        public const float MinTotal = 2f;

        /// <summary>Share of the way the direction goes toward a new estimate with every redraw.</summary>
        public const float Follow = 0.2f;

        const int Bins = 90;
        const int Smooth = 3;

        readonly float[] bins = new float[Bins];
        readonly float[] sines = new float[Bins];
        readonly float[] cosines = new float[Bins];
        readonly float[] smoothed = new float[Bins];
        float total;
        bool frozen;

        /// <summary>Direction of the walls, in degrees around the vertical axis (0 up to 90).</summary>
        public float Yaw { get; private set; }

        /// <summary>A direction was found (before that, <see cref="Yaw"/> is 0: the axes of the scan).</summary>
        public bool HasYaw { get; private set; }

        public bool IsFrozen => frozen;

        public void Reset()
        {
            Begin();
            Yaw = 0f;
            HasYaw = false;
            frozen = false;
        }

        /// <summary>The direction stays as it is from now on (the scan is finished).</summary>
        public void Freeze()
        {
            frozen = true;
        }

        public void Unfreeze()
        {
            frozen = false;
        }

        /// <summary>Starts a new collection of edges.</summary>
        public void Begin()
        {
            System.Array.Clear(bins, 0, Bins);
            System.Array.Clear(sines, 0, Bins);
            System.Array.Clear(cosines, 0, Bins);
            total = 0f;
        }

        /// <summary>An edge of a scanned surface, from <paramref name="a"/> to <paramref name="b"/> (x and z in meters).</summary>
        /// <param name="weight">Walls say more about the room than the edge of a table.</param>
        public void AddEdge(Vector2 a, Vector2 b, float weight = 1f)
        {
            var edge = b - a;
            var length = edge.magnitude;
            if (length < MinEdge)
            {
                return;
            }

            var yaw = Wrap(YawOf(edge));
            var bin = Mathf.FloorToInt(yaw) % Bins;
            bins[bin] += length * weight;

            // A direction of walls comes back after 90 degrees: four times the angle comes back after 360.
            var turn = yaw * 4f * Mathf.Deg2Rad;
            sines[bin] += Mathf.Sin(turn) * length * weight;
            cosines[bin] += Mathf.Cos(turn) * length * weight;
            total += length * weight;
        }

        public void AddLoop(IReadOnlyList<Vector2> loop, float weight = 1f)
        {
            for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
            {
                AddEdge(loop[j], loop[i], weight);
            }
        }

        /// <summary>
        /// Takes the edges that were collected into account: the direction goes a part of the way toward
        /// what they say. True when there is a direction.
        /// </summary>
        public bool Update()
        {
            if (frozen || !TryEstimate(out var estimate))
            {
                return HasYaw;
            }

            if (!HasYaw)
            {
                Yaw = estimate;
                HasYaw = true;
                return true;
            }

            Yaw = Wrap(Yaw + Follow * Difference(estimate, Yaw));
            return true;
        }

        /// <summary>Takes the direction the collected edges point in, all the way. True when there is a direction.</summary>
        public bool Settle()
        {
            if (!frozen && TryEstimate(out var estimate))
            {
                Yaw = estimate;
                HasYaw = true;
            }

            return HasYaw;
        }

        /// <summary>The direction the collected edges point in, if there are enough of them.</summary>
        public bool TryEstimate(out float yawDegrees)
        {
            yawDegrees = 0f;
            if (total < MinTotal)
            {
                return false;
            }

            var best = 0;
            for (var bin = 0; bin < Bins; bin++)
            {
                var sum = 0f;
                for (var offset = -Smooth; offset <= Smooth; offset++)
                {
                    sum += bins[(bin + offset + Bins) % Bins];
                }

                smoothed[bin] = sum;
                if (sum > smoothed[best])
                {
                    best = bin;
                }
            }

            // The middle of the peak: the mean direction of the edges around it, by their length.
            var sine = 0f;
            var cosine = 0f;
            for (var offset = -Smooth; offset <= Smooth; offset++)
            {
                var bin = (best + offset + Bins) % Bins;
                sine += sines[bin];
                cosine += cosines[bin];
            }

            if (smoothed[best] <= 0f || (Mathf.Abs(sine) < 1e-6f && Mathf.Abs(cosine) < 1e-6f))
            {
                return false;
            }

            yawDegrees = Wrap(Mathf.Atan2(sine, cosine) * Mathf.Rad2Deg / 4f);

            // Walls along the axes give 0 or a hair below 90, which is the same direction: 0 it is.
            if (yawDegrees > 90f - 1e-3f)
            {
                yawDegrees = 0f;
            }

            return true;
        }

        /// <summary>Direction of an edge in degrees around the vertical axis, as a turn of the z axis toward x.</summary>
        public static float YawOf(Vector2 edge)
        {
            return Mathf.Atan2(edge.x, edge.y) * Mathf.Rad2Deg;
        }

        /// <summary>A direction of walls: turned by 90 degrees it is the same (0 up to 90).</summary>
        public static float Wrap(float degrees)
        {
            var wrapped = degrees % 90f;
            return wrapped < 0f ? wrapped + 90f : wrapped;
        }

        /// <summary>The smallest turn from one direction of walls to another (-45 up to 45).</summary>
        public static float Difference(float to, float from)
        {
            var difference = (to - from) % 90f;
            if (difference > 45f)
            {
                difference -= 90f;
            }
            else if (difference < -45f)
            {
                difference += 90f;
            }

            return difference;
        }
    }
}
