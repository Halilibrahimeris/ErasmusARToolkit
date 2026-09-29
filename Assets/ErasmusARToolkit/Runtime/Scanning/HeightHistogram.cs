using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// The heights (world y) of the upward-facing depth points of the last seconds, in 1 cm bins. Every
    /// horizontal surface in view shows as a peak. A surface level takes its height from the peak nearest
    /// to it, so a smaller surface close by does not pull the estimate, and samples fade with every frame,
    /// so the estimate follows when ARCore corrects its map.
    /// <para>
    /// One histogram serves all levels. The range is 5.12 m around the first sample, which covers every
    /// surface a phone can look at from above.
    /// </para>
    /// </summary>
    internal sealed class HeightHistogram
    {
        public const float BinSize = 0.01f;
        public const int BinCount = 512;

        /// <summary>Bins on each side of a peak's center that belong to the peak (5 cm in all).</summary>
        public const int PeakHalfWidth = 2;

        /// <summary>The peak position is refined with a Gaussian window of this width (meters).</summary>
        public const float Bandwidth = 0.015f;

        const int RefineSteps = 3;
        const float SharpInner = 0.015f;
        const float SharpOuter = 0.05f;
        const float SharpShare = 0.6f;
        const float Forget = 1e-3f;

        readonly float[] mass = new float[BinCount];
        readonly float[] moment = new float[BinCount];
        readonly float[] cells = new float[BinCount];
        float origin;
        bool hasOrigin;

        public bool IsEmpty => !hasOrigin;

        public void Clear()
        {
            System.Array.Clear(mass, 0, BinCount);
            System.Array.Clear(moment, 0, BinCount);
            System.Array.Clear(cells, 0, BinCount);
            hasOrigin = false;
        }

        /// <param name="weight">How much the sample counts toward the height (exact samples count more).</param>
        /// <param name="count">How much it counts as an observation (a frame's weight; 1 for a normal frame).</param>
        public void Add(float y, float weight, float count)
        {
            if (!hasOrigin)
            {
                origin = y - BinCount * BinSize * 0.5f;
                hasOrigin = true;
            }

            var bin = Mathf.FloorToInt((y - origin) / BinSize);
            if (bin < 0 || bin >= BinCount || weight <= 0f)
            {
                return;
            }

            mass[bin] += weight;
            moment[bin] += weight * y;
            cells[bin] += count;
        }

        /// <summary>Lets older samples fade: call once per frame.</summary>
        public void Decay(float factor)
        {
            if (!hasOrigin)
            {
                return;
            }

            for (var bin = 0; bin < BinCount; bin++)
            {
                if (cells[bin] <= 0f)
                {
                    continue;
                }

                mass[bin] *= factor;
                moment[bin] *= factor;
                cells[bin] *= factor;
                if (cells[bin] < Forget)
                {
                    mass[bin] = moment[bin] = cells[bin] = 0f;
                }
            }
        }

        /// <summary>Observations within <paramref name="radius"/> meters of a height.</summary>
        public float CountAround(float y, float radius)
        {
            return Sum(cells, y, radius);
        }

        /// <summary>
        /// The peak nearest to <paramref name="center"/> within <paramref name="radius"/> that holds at
        /// least <paramref name="minCount"/> observations. <paramref name="sharp"/>: most of what lies
        /// around the peak lies within 1.5 cm of it, as on a surface that is measured well.
        /// </summary>
        public bool TryNearestMode(float center, float radius, float minCount, out float height, out bool sharp)
        {
            height = center;
            sharp = false;
            if (!hasOrigin)
            {
                return false;
            }

            var first = Mathf.Max(BinOf(center - radius), 0);
            var last = Mathf.Min(BinOf(center + radius), BinCount - 1);
            var best = -1;
            var bestDistance = float.MaxValue;
            for (var bin = first; bin <= last; bin++)
            {
                if (Window(cells, bin) < minCount)
                {
                    continue;
                }

                var here = Window(mass, bin);
                if (here < Window(mass, bin - 1) || here < Window(mass, bin + 1))
                {
                    continue;
                }

                var distance = Mathf.Abs(CenterOf(bin) - center);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = bin;
                }
            }

            if (best < 0)
            {
                return false;
            }

            height = Refine(CenterOf(best));
            var outer = Sum(mass, height, SharpOuter);
            sharp = outer > 0f && Sum(mass, height, SharpInner) >= SharpShare * outer;
            return true;
        }

        /// <summary>The peak with the most observations, if it holds at least <paramref name="minCount"/>.</summary>
        public bool TryStrongestMode(float minCount, out float height)
        {
            height = 0f;
            if (!hasOrigin)
            {
                return false;
            }

            var best = -1;
            var bestCount = minCount;
            for (var bin = 0; bin < BinCount; bin++)
            {
                var count = Window(cells, bin);
                if (count >= bestCount)
                {
                    bestCount = count;
                    best = bin;
                }
            }

            if (best < 0)
            {
                return false;
            }

            height = Refine(CenterOf(best));
            return true;
        }

        /// <summary>Mean shift: the weighted mean of the samples around the peak, repeated a few times.</summary>
        float Refine(float height)
        {
            var reach = Mathf.CeilToInt(3f * Bandwidth / BinSize);
            for (var step = 0; step < RefineSteps; step++)
            {
                var center = BinOf(height);
                var sum = 0f;
                var weight = 0f;
                for (var bin = Mathf.Max(center - reach, 0); bin <= Mathf.Min(center + reach, BinCount - 1); bin++)
                {
                    if (mass[bin] <= 0f)
                    {
                        continue;
                    }

                    var mean = moment[bin] / mass[bin];
                    var offset = (mean - height) / Bandwidth;
                    var kernel = mass[bin] * Mathf.Exp(-0.5f * offset * offset);
                    sum += kernel * mean;
                    weight += kernel;
                }

                if (weight <= 0f)
                {
                    break;
                }

                height = sum / weight;
            }

            return height;
        }

        float Window(float[] values, int center)
        {
            var sum = 0f;
            for (var bin = Mathf.Max(center - PeakHalfWidth, 0); bin <= Mathf.Min(center + PeakHalfWidth, BinCount - 1); bin++)
            {
                sum += values[bin];
            }

            return sum;
        }

        float Sum(float[] values, float y, float radius)
        {
            if (!hasOrigin)
            {
                return 0f;
            }

            var sum = 0f;
            for (var bin = Mathf.Max(BinOf(y - radius), 0); bin <= Mathf.Min(BinOf(y + radius), BinCount - 1); bin++)
            {
                sum += values[bin];
            }

            return sum;
        }

        int BinOf(float y) => Mathf.FloorToInt((y - origin) / BinSize);

        float CenterOf(int bin) => origin + (bin + 0.5f) * BinSize;
    }
}
