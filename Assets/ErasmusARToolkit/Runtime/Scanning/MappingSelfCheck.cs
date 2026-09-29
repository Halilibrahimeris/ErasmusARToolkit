namespace Erasmus.ARToolkit
{
    /// <summary>
    /// Decides which of the depth image mappings (<see cref="DepthImageMapping.Candidates"/>) is the right
    /// one on this phone. For every depth image the scan compares each candidate with ARCore's own depth
    /// hit test at a few screen points and adds up the errors:
    /// <list type="bullet">
    /// <item><see cref="MappingState.Verified"/>: one candidate is close and clearly better than the rest.</item>
    /// <item><see cref="MappingState.Assumed"/>: the candidates stay tied, as they do in front of a flat floor
    /// that looks the same mirrored. After enough images the first of the tied candidates is used (the
    /// documented mapping comes first). The scan must not carve while the mapping is assumed, and the check
    /// goes on: a later image can verify a candidate.</item>
    /// <item><see cref="MappingState.Unverifiable"/>: no depth hit test, or no candidate is close. The
    /// depth image cannot be used.</item>
    /// </list>
    /// The check always ends: the scan never waits for a view that may not come.
    /// </summary>
    internal sealed class MappingSelfCheck
    {
        /// <summary>A candidate needs this many compared points before it can be judged.</summary>
        public const int MinPoints = 6;

        /// <summary>Mean error (meters) a usable candidate may have.</summary>
        public const float MaxError = 0.12f;

        /// <summary>The best candidate must beat the second by this much (meters) to be verified.</summary>
        public const float MinLead = 0.04f;

        /// <summary>Candidates this close (meters) to the best are tied with it.</summary>
        public const float Tie = 0.02f;

        /// <summary>Images with depth hits after which a tie is settled by assuming.</summary>
        public const int ImagesToAssume = 24;

        /// <summary>Images without any depth hit after which the check gives up.</summary>
        public const int ImagesWithoutHits = 16;

        readonly float[] errors;
        readonly int[] counts;
        int images;
        int imagesWithHits;

        public MappingSelfCheck(int candidates)
        {
            errors = new float[candidates];
            counts = new int[candidates];
        }

        public MappingState State { get; private set; } = MappingState.Checking;

        /// <summary>The candidate to use (the first one until the check has decided).</summary>
        public int Best { get; private set; }

        public int Images => images;

        public int ImagesWithHits => imagesWithHits;

        /// <summary>The depth image can be read with <see cref="Best"/>.</summary>
        public bool IsUsable => State == MappingState.Verified || State == MappingState.Assumed;

        /// <summary>The check has nothing more to find out.</summary>
        public bool IsFinal => State == MappingState.Verified || State == MappingState.Unverifiable;

        public int Points(int candidate) => counts[candidate];

        /// <summary>Mean error (meters) of a candidate, NaN without points.</summary>
        public float MeanError(int candidate)
        {
            return candidate >= 0 && candidate < counts.Length && counts[candidate] > 0
                ? errors[candidate] / counts[candidate]
                : float.NaN;
        }

        public void Reset()
        {
            System.Array.Clear(errors, 0, errors.Length);
            System.Array.Clear(counts, 0, counts.Length);
            images = 0;
            imagesWithHits = 0;
            State = MappingState.Checking;
            Best = 0;
        }

        /// <summary>One compared point: how far (meters) a candidate's depth is from the depth hit test.</summary>
        public void AddSample(int candidate, float error)
        {
            errors[candidate] += error;
            counts[candidate]++;
        }

        /// <summary>
        /// Call after the samples of one depth image. Returns true when <see cref="State"/> or
        /// <see cref="Best"/> changed.
        /// </summary>
        /// <param name="anyHit">The depth hit test answered for at least one screen point.</param>
        public bool EndImage(bool anyHit)
        {
            if (IsFinal)
            {
                return false;
            }

            images++;
            if (anyHit)
            {
                imagesWithHits++;
            }

            Rank(out var best, out var bestError, out var secondError);
            if (best >= 0 && bestError <= MaxError && secondError - bestError >= MinLead)
            {
                return Decide(MappingState.Verified, best);
            }

            if (State != MappingState.Checking)
            {
                return false;
            }

            if (imagesWithHits >= ImagesToAssume)
            {
                return best >= 0 && bestError <= MaxError
                    ? Decide(MappingState.Assumed, FirstTied(bestError))
                    : Decide(MappingState.Unverifiable, 0);
            }

            return images >= ImagesWithoutHits && imagesWithHits == 0 && Decide(MappingState.Unverifiable, 0);
        }

        bool Decide(MappingState state, int candidate)
        {
            var changed = state != State || candidate != Best;
            State = state;
            Best = candidate;
            return changed;
        }

        void Rank(out int best, out float bestError, out float secondError)
        {
            best = -1;
            bestError = float.MaxValue;
            secondError = float.MaxValue;
            for (var k = 0; k < counts.Length; k++)
            {
                if (counts[k] < MinPoints)
                {
                    continue;
                }

                var error = errors[k] / counts[k];
                if (error < bestError)
                {
                    secondError = bestError;
                    bestError = error;
                    best = k;
                }
                else if (error < secondError)
                {
                    secondError = error;
                }
            }
        }

        int FirstTied(float bestError)
        {
            for (var k = 0; k < counts.Length; k++)
            {
                if (counts[k] >= MinPoints && errors[k] / counts[k] <= bestError + Tie)
                {
                    return k;
                }
            }

            return 0;
        }
    }
}
