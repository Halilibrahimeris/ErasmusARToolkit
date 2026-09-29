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

        /// <summary>
        /// Head start of a cell inside a plane ARCore detected, when nothing better is known: on phones
        /// without depth, and for the floor. Above <see cref="OccupiedThreshold"/>: the cell shows at once.
        /// </summary>
        public const float PriorWeight = 0.5f;

        /// <summary>
        /// Head start of a cell of a raised surface on phones with depth. Below
        /// <see cref="OccupiedThreshold"/>: ARCore planes are hulls that reach past table edges, so a cell
        /// shows after one frame of depth hits, and one frame that looks through it removes it.
        /// </summary>
        public const float PrecisePriorWeight = 0.25f;

        /// <summary>
        /// A cell that is shown stays shown until its score falls below this: one frame of doubt does not
        /// make an edge flicker.
        /// </summary>
        public const float HideThreshold = 0.12f;

        /// <summary>From this value on a cell was seen as a surface by depth itself (two frames of hits).</summary>
        public const float ConfirmedLogOdds = 0.4f;

        /// <summary>
        /// Weight of a depth point that was seen through a confirmed floor cell: a reflection in a shiny
        /// floor. It says little about where the point is, but that the floor is there.
        /// </summary>
        public const float ReflectionHitWeight = 0.1f;
        public const float MinLogOdds = -1.2f;
        public const float MaxLogOdds = 1.6f;

        /// <summary>Score above which a cell is a surface: two frames of hits, or an ARCore plane seen through less than twice.</summary>
        public const float OccupiedThreshold = 0.3f;

        public static float Score(float logOdds, bool prior)
        {
            return Score(logOdds, prior, PriorWeight);
        }

        public static float Score(float logOdds, bool prior, float priorWeight)
        {
            return logOdds + (prior ? priorWeight : 0f);
        }

        public static CellState Classify(float logOdds, bool prior)
        {
            return Classify(logOdds, prior, PriorWeight);
        }

        public static CellState Classify(float logOdds, bool prior, float priorWeight)
        {
            if (Score(logOdds, prior, priorWeight) > OccupiedThreshold)
            {
                return CellState.Occupied;
            }

            return logOdds < 0f ? CellState.Carved : CellState.Unknown;
        }

        /// <summary>
        /// A shown cell stays shown while it has more hits than misses and its score is above
        /// <see cref="HideThreshold"/>; a hidden cell needs <see cref="OccupiedThreshold"/> to show.
        /// </summary>
        public static bool Keeps(float logOdds, bool prior, float priorWeight, bool wasKept)
        {
            var score = Score(logOdds, prior, priorWeight);
            return score > OccupiedThreshold || (wasKept && logOdds > 0f && score > HideThreshold);
        }

        public static float AddHit(float logOdds, float weight)
        {
            return Mathf.Min(logOdds + weight, MaxLogOdds);
        }

        public static float AddMiss(float logOdds)
        {
            return AddMiss(logOdds, 1f);
        }

        /// <param name="frameWeight">How much the frame counts (see <see cref="ScanFrame.Weight"/>).</param>
        public static float AddMiss(float logOdds, float frameWeight)
        {
            return Mathf.Max(logOdds - MissWeight * frameWeight, MinLogOdds);
        }
    }
}
