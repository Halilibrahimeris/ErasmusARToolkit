using System;
using System.Collections.Generic;

namespace Erasmus.ARToolkit
{
    /// <summary>How <see cref="SurfaceRegionFilter"/> decides which cells are drawn.</summary>
    public readonly struct SurfaceFilterSettings
    {
        public SurfaceFilterSettings(float minRegionArea, float maxHoleArea, bool hysteresis)
        {
            MinRegionArea = minRegionArea;
            MaxHoleArea = maxHoleArea;
            Hysteresis = hysteresis;
        }

        /// <summary>Smaller pieces (square meters) are noise, not a surface to place things on.</summary>
        public float MinRegionArea { get; }

        /// <summary>Unseen holes up to this size (square meters) inside a surface are filled.</summary>
        public float MaxHoleArea { get; }

        /// <summary>A cell that was drawn stays until its evidence is clearly gone (no flickering edges).</summary>
        public bool Hysteresis { get; }

        /// <summary>Every redraw decides anew, as <see cref="SurfaceRegionFilter.Apply(SurfaceGrid)"/> does.</summary>
        public static readonly SurfaceFilterSettings Legacy = new SurfaceFilterSettings(
            SurfaceRegionFilter.MinRegionArea, SurfaceRegionFilter.MaxHoleArea, false);

        /// <summary>The room scan: steady edges.</summary>
        public static readonly SurfaceFilterSettings Scan = new SurfaceFilterSettings(
            SurfaceRegionFilter.MinRegionArea, SurfaceRegionFilter.MaxHoleArea, true);
    }

    /// <summary>
    /// Decides which cells of a <see cref="SurfaceGrid"/> are drawn. Starts from the occupied cells, then
    /// <list type="bullet">
    /// <item>drops pieces smaller than <see cref="MinRegionArea"/> (the scattered fragments), and</item>
    /// <item>fills small holes the camera could not see into (under a monitor stand, a bag), but never
    /// cells the camera looked through (<see cref="CellState.Carved"/>).</item>
    /// </list>
    /// Buffers are reused, so filtering does not allocate once they are big enough.
    /// </summary>
    public sealed class SurfaceRegionFilter
    {
        /// <summary>Smaller pieces (square meters) are noise, not a surface to place things on.</summary>
        public const float MinRegionArea = 0.06f;

        /// <summary>Unseen holes up to this size (square meters) inside a surface are filled.</summary>
        public const float MaxHoleArea = 0.25f;

        const float CellArea = RoomSurfaceMap.CellSize * RoomSurfaceMap.CellSize;

        readonly List<int> region = new List<int>();
        bool[] visited = Array.Empty<bool>();
        int[] queue = Array.Empty<int>();
        float minRegionArea = MinRegionArea;
        float maxHoleArea = MaxHoleArea;

        public void Apply(SurfaceGrid grid)
        {
            Apply(grid, SurfaceFilterSettings.Legacy);
        }

        public void Apply(SurfaceGrid grid, in SurfaceFilterSettings settings)
        {
            var count = grid.CellCount;
            if (visited.Length < count)
            {
                visited = new bool[count];
                queue = new int[count];
            }

            for (var i = 0; i < count; i++)
            {
                var keep = settings.Hysteresis
                    ? SurfaceEvidence.Keeps(grid.LogOddsAt(i), grid.HasPriorAt(i), grid.PriorWeight, grid.WasKept(i))
                    : grid.StateAt(i) == CellState.Occupied;
                grid.SetKept(i, keep);
                grid.SetShown(i, keep);
            }

            minRegionArea = settings.MinRegionArea;
            maxHoleArea = settings.MaxHoleArea;
            RemoveSmallRegions(grid);
            FillSmallHoles(grid);
        }

        void RemoveSmallRegions(SurfaceGrid grid)
        {
            Array.Clear(visited, 0, grid.CellCount);
            for (var start = 0; start < grid.CellCount; start++)
            {
                if (visited[start] || !grid.IsShown(start))
                {
                    continue;
                }

                CollectRegion(grid, start, true);
                if (region.Count * CellArea < minRegionArea)
                {
                    foreach (var index in region)
                    {
                        grid.SetShown(index, false);
                    }
                }
            }
        }

        void FillSmallHoles(SurfaceGrid grid)
        {
            Array.Clear(visited, 0, grid.CellCount);

            // Everything reachable from the border without crossing the surface is outside, not a hole.
            for (var x = 0; x < grid.Width; x++)
            {
                MarkOutside(grid, grid.IndexOf(x, 0));
                MarkOutside(grid, grid.IndexOf(x, grid.Depth - 1));
            }

            for (var z = 0; z < grid.Depth; z++)
            {
                MarkOutside(grid, grid.IndexOf(0, z));
                MarkOutside(grid, grid.IndexOf(grid.Width - 1, z));
            }

            for (var start = 0; start < grid.CellCount; start++)
            {
                if (visited[start] || grid.IsShown(start))
                {
                    continue;
                }

                CollectRegion(grid, start, false);
                if (region.Count * CellArea > maxHoleArea)
                {
                    continue;
                }

                foreach (var index in region)
                {
                    if (grid.StateAt(index) != CellState.Carved)
                    {
                        grid.SetShown(index, true);
                    }
                }
            }
        }

        void MarkOutside(SurfaceGrid grid, int index)
        {
            if (!visited[index] && !grid.IsShown(index))
            {
                CollectRegion(grid, index, false);
            }
        }

        /// <summary>Flood fill (4 neighbors) over cells whose shown flag equals <paramref name="shown"/>.</summary>
        void CollectRegion(SurfaceGrid grid, int start, bool shown)
        {
            region.Clear();
            var head = 0;
            var tail = 0;
            queue[tail++] = start;
            visited[start] = true;
            while (head < tail)
            {
                var index = queue[head++];
                region.Add(index);
                var x = index % grid.Width;
                var z = index / grid.Width;
                Visit(grid, x - 1, z, shown, ref tail);
                Visit(grid, x + 1, z, shown, ref tail);
                Visit(grid, x, z - 1, shown, ref tail);
                Visit(grid, x, z + 1, shown, ref tail);
            }
        }

        void Visit(SurfaceGrid grid, int x, int z, bool shown, ref int tail)
        {
            if (!grid.Contains(x, z))
            {
                return;
            }

            var index = grid.IndexOf(x, z);
            if (!visited[index] && grid.IsShown(index) == shown)
            {
                visited[index] = true;
                queue[tail++] = index;
            }
        }
    }
}
