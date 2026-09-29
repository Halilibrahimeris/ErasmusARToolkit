using System.Collections.Generic;
using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// Groups AR planes that belong to the same real surface. ARCore often reports one floor as several
    /// overlapping planes a few centimeters apart; planes at (almost) the same height whose footprints
    /// touch are treated as one surface. Each surface gets the height of its largest plane and, for the
    /// largest surfaces, a stencil bit so the plane shader can draw it once. Buffers are reused, so
    /// recomputing does not allocate.
    /// </summary>
    public sealed class PlaneGrouping
    {
        /// <summary>Planes closer than this in height (meters) can be the same surface.</summary>
        public const float HeightTolerance = 0.05f;

        /// <summary>Footprints closer than this (meters) count as touching, so small gaps are joined.</summary>
        public const float BoundsMargin = 0.1f;

        /// <summary>One stencil bit per surface; surfaces beyond this many get no bit (0).</summary>
        public const int StencilBitCount = 8;

        readonly List<int> parents = new List<int>();
        readonly List<int> planeGroups = new List<int>();
        readonly List<int> rootGroups = new List<int>();
        readonly List<float> groupHeights = new List<float>();
        readonly List<float> groupLargestAreas = new List<float>();
        readonly List<float> groupAreas = new List<float>();
        readonly List<int> groupBits = new List<int>();

        public int GroupCount => groupHeights.Count;

        /// <summary>Surface index of the plane at <paramref name="planeIndex"/> in the last computed list.</summary>
        public int GroupOf(int planeIndex) => planeGroups[planeIndex];

        /// <summary>Shared height of a surface: the height of its largest plane.</summary>
        public float HeightOf(int group) => groupHeights[group];

        /// <summary>Stencil mask of a surface (1, 2, 4 ... 128), or 0 when all bits are taken.</summary>
        public int StencilBitOf(int group) => groupBits[group];

        public static bool IsSameSurface(PlaneFootprint a, PlaneFootprint b)
        {
            if (Mathf.Abs(a.Height - b.Height) > HeightTolerance)
            {
                return false;
            }

            var grown = Rect.MinMaxRect(
                a.Bounds.xMin - BoundsMargin, a.Bounds.yMin - BoundsMargin,
                a.Bounds.xMax + BoundsMargin, a.Bounds.yMax + BoundsMargin);
            return grown.Overlaps(b.Bounds);
        }

        public void Compute(IReadOnlyList<PlaneFootprint> planes)
        {
            JoinTouchingPlanes(planes);
            CollectGroups(planes);
            AssignStencilBits();
        }

        void JoinTouchingPlanes(IReadOnlyList<PlaneFootprint> planes)
        {
            parents.Clear();
            for (var i = 0; i < planes.Count; i++)
            {
                parents.Add(i);
            }

            for (var i = 0; i < planes.Count; i++)
            {
                for (var j = i + 1; j < planes.Count; j++)
                {
                    if (IsSameSurface(planes[i], planes[j]))
                    {
                        parents[FindRoot(j)] = FindRoot(i);
                    }
                }
            }
        }

        void CollectGroups(IReadOnlyList<PlaneFootprint> planes)
        {
            planeGroups.Clear();
            rootGroups.Clear();
            groupHeights.Clear();
            groupLargestAreas.Clear();
            groupAreas.Clear();
            for (var i = 0; i < planes.Count; i++)
            {
                rootGroups.Add(-1);
            }

            for (var i = 0; i < planes.Count; i++)
            {
                var root = FindRoot(i);
                var group = rootGroups[root];
                if (group < 0)
                {
                    group = groupHeights.Count;
                    rootGroups[root] = group;
                    groupHeights.Add(planes[i].Height);
                    groupLargestAreas.Add(planes[i].Area);
                    groupAreas.Add(0f);
                }
                else if (planes[i].Area > groupLargestAreas[group])
                {
                    groupHeights[group] = planes[i].Height;
                    groupLargestAreas[group] = planes[i].Area;
                }

                groupAreas[group] += planes[i].Area;
                planeGroups.Add(group);
            }
        }

        void AssignStencilBits()
        {
            groupBits.Clear();
            for (var group = 0; group < GroupCount; group++)
            {
                groupBits.Add(0);
            }

            for (var bit = 0; bit < StencilBitCount; bit++)
            {
                var largest = -1;
                for (var group = 0; group < GroupCount; group++)
                {
                    if (groupBits[group] == 0 && (largest < 0 || groupAreas[group] > groupAreas[largest]))
                    {
                        largest = group;
                    }
                }

                if (largest < 0)
                {
                    return;
                }

                groupBits[largest] = 1 << bit;
            }
        }

        int FindRoot(int index)
        {
            while (parents[index] != index)
            {
                parents[index] = parents[parents[index]];
                index = parents[index];
            }

            return index;
        }
    }
}
