using System.Globalization;
using System.Text;
using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// How far the depth points on the floor lie from the floor height, per distance from the camera.
    /// The mean shows a bias (points below the floor on shiny tiles, a depth scale error), the spread shows
    /// the noise. Written to the log while scanning, to tune the scan for a phone and a room.
    /// </summary>
    internal sealed class DepthResiduals
    {
        /// <summary>Points farther than this (meters) from the reference height are not floor points.</summary>
        public const float Window = 0.15f;

        const float FirstRange = 0.5f;
        const float RangeStep = 1f;
        const int Bins = 4;

        readonly double[] sums = new double[Bins];
        readonly double[] squares = new double[Bins];
        readonly int[] counts = new int[Bins];

        public int Count(int bin) => counts[bin];

        public float Mean(int bin) => counts[bin] > 0 ? (float)(sums[bin] / counts[bin]) : float.NaN;

        public float Spread(int bin)
        {
            if (counts[bin] < 2)
            {
                return float.NaN;
            }

            var mean = sums[bin] / counts[bin];
            var variance = squares[bin] / counts[bin] - mean * mean;
            return variance > 0.0 ? Mathf.Sqrt((float)variance) : 0f;
        }

        public void Clear()
        {
            System.Array.Clear(sums, 0, Bins);
            System.Array.Clear(squares, 0, Bins);
            System.Array.Clear(counts, 0, Bins);
        }

        /// <summary>Adds the upward-facing points of a frame that lie near <paramref name="referenceHeight"/>.</summary>
        public void Add(ScanFrame frame, float referenceHeight)
        {
            var origin = frame.Origin;
            for (var i = 0; i < frame.Count; i++)
            {
                if (frame.HasNormals && !frame.IsUpFacing(i))
                {
                    continue;
                }

                var point = frame.PointAt(i);
                var residual = point.y - referenceHeight;
                if (Mathf.Abs(residual) > Window)
                {
                    continue;
                }

                var bin = Mathf.FloorToInt((Vector3.Distance(origin, point) - FirstRange) / RangeStep);
                if (bin < 0 || bin >= Bins)
                {
                    continue;
                }

                sums[bin] += residual;
                squares[bin] += (double)residual * residual;
                counts[bin]++;
            }
        }

        /// <summary>Mean and spread in centimeters with the point count, for every distance range.</summary>
        public string ToLine()
        {
            var culture = CultureInfo.InvariantCulture;
            var line = new StringBuilder(160);
            for (var bin = 0; bin < Bins; bin++)
            {
                if (bin > 0)
                {
                    line.Append(" | ");
                }

                var from = FirstRange + bin * RangeStep;
                line.Append(from.ToString("0.0", culture)).Append('-');
                line.Append((from + RangeStep).ToString("0.0", culture)).Append("m ");
                if (counts[bin] == 0)
                {
                    line.Append('-');
                    continue;
                }

                line.Append((Mean(bin) * 100f).ToString("+0.0;-0.0", culture));
                var spread = Spread(bin);
                line.Append('±').Append(float.IsNaN(spread) ? "-" : (spread * 100f).ToString("0.0", culture));
                line.Append("cm (").Append(counts[bin]).Append(')');
            }

            return line.ToString();
        }
    }
}
