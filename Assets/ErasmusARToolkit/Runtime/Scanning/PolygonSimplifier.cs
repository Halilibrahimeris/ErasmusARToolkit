using System.Collections.Generic;
using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// Leaves out the points of an outline that say nothing: those that lie closer than a tolerance to
    /// the line between the points that stay (Douglas and Peucker). A long edge that the scan found as
    /// hundreds of points becomes two points.
    /// </summary>
    internal sealed class PolygonSimplifier
    {
        readonly Stack<Vector2Int> ranges = new Stack<Vector2Int>();
        bool[] keep = System.Array.Empty<bool>();

        /// <param name="loop">A closed outline.</param>
        /// <param name="tolerance">Points closer than this (meters) to the simplified outline are left out.</param>
        /// <param name="output">Gets the points that stay, in their order. Fewer than three: the outline was a sliver.</param>
        public void Simplify(IReadOnlyList<Vector2> loop, float tolerance, List<Vector2> output)
        {
            output.Clear();
            var count = loop.Count;
            if (count < 3)
            {
                return;
            }

            if (keep.Length < count)
            {
                keep = new bool[count];
            }
            else
            {
                System.Array.Clear(keep, 0, count);
            }

            // A closed outline has no ends: it is cut in two at the two points that lie farthest apart.
            var first = 0;
            var second = FarthestFrom(loop, first);
            first = FarthestFrom(loop, second);
            if (first > second)
            {
                (first, second) = (second, first);
            }

            keep[first] = true;
            keep[second] = true;
            Reduce(loop, first, second, tolerance);
            Reduce(loop, second, first + count, tolerance);
            for (var i = 0; i < count; i++)
            {
                if (keep[i])
                {
                    output.Add(loop[i]);
                }
            }

            if (output.Count < 3)
            {
                output.Clear();
            }
        }

        static int FarthestFrom(IReadOnlyList<Vector2> loop, int index)
        {
            var farthest = index;
            var distance = -1f;
            for (var i = 0; i < loop.Count; i++)
            {
                var here = (loop[i] - loop[index]).sqrMagnitude;
                if (here > distance)
                {
                    distance = here;
                    farthest = i;
                }
            }

            return farthest;
        }

        /// <summary>Indices run around the loop: <paramref name="to"/> may lie beyond its end.</summary>
        void Reduce(IReadOnlyList<Vector2> loop, int from, int to, float tolerance)
        {
            var count = loop.Count;
            ranges.Clear();
            ranges.Push(new Vector2Int(from, to));
            while (ranges.Count > 0)
            {
                var range = ranges.Pop();
                var a = loop[range.x % count];
                var b = loop[range.y % count];
                var farthest = -1;
                var distance = tolerance;
                for (var i = range.x + 1; i < range.y; i++)
                {
                    var here = PolygonMath.DistanceToSegment(loop[i % count], a, b);
                    if (here > distance)
                    {
                        distance = here;
                        farthest = i;
                    }
                }

                if (farthest < 0)
                {
                    continue;
                }

                keep[farthest % count] = true;
                ranges.Push(new Vector2Int(range.x, farthest));
                ranges.Push(new Vector2Int(farthest, range.y));
            }
        }
    }
}
