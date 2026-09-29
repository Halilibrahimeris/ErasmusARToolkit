using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>What the room scan knows about one cell of a surface.</summary>
    public enum CellState
    {
        /// <summary>Not seen well enough yet.</summary>
        Unknown,

        /// <summary>A surface is there.</summary>
        Occupied,

        /// <summary>The camera looked through this spot, so there is no surface at this height.</summary>
        Carved,
    }

    /// <summary>
    /// How observations turn into "surface here" / "nothing here" for one cell. Each cell keeps a
    /// log-odds value (as in OctoMap): a camera frame that sees the surface adds <see cref="HitWeight"/>,
    /// a frame that looks through it subtracts <see cref="MissWeight"/>. Every frame counts once per cell
    /// and the value is clamped, so a cell only changes after several consistent frames and never gets
    /// stuck. Cells inside a plane that ARCore detected start with <see cref="PriorWeight"/>, so they show
    /// until the camera proves them wrong.
    /// </summary>
    public static class SurfaceEvidence
    {
        public const float HitWeight = 0.2f;

        /// <summary>Hit weight for tracking points: they have no normal and may lie on a table's side.</summary>
        public const float SparseHitWeight = 0.1f;

        public const float MissWeight = 0.15f;
        public const float PriorWeight = 0.5f;
        public const float MinLogOdds = -1.2f;
        public const float MaxLogOdds = 1.6f;

        /// <summary>Score above which a cell is a surface: two frames of hits, or an ARCore plane seen through less than twice.</summary>
        public const float OccupiedThreshold = 0.3f;

        public static float Score(float logOdds, bool prior)
        {
            return logOdds + (prior ? PriorWeight : 0f);
        }

        public static CellState Classify(float logOdds, bool prior)
        {
            if (Score(logOdds, prior) > OccupiedThreshold)
            {
                return CellState.Occupied;
            }

            return logOdds < 0f ? CellState.Carved : CellState.Unknown;
        }

        public static float AddHit(float logOdds, float weight)
        {
            return Mathf.Min(logOdds + weight, MaxLogOdds);
        }

        public static float AddMiss(float logOdds)
        {
            return Mathf.Max(logOdds - MissWeight, MinLogOdds);
        }
    }
}
