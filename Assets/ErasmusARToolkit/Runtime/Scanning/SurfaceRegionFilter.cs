using System;
using System.Collections.Generic;

namespace Erasmus.ARToolkit
{
    /// <summary>How <see cref="SurfaceRegionFilter"/> decides which cells are drawn.</summary>
    public readonly struct SurfaceFilterSettings
    {
        public SurfaceFilterSettings(float minRegionArea, float maxHoleArea, bool hysteresis)
            : this(minRegionArea, maxHoleArea, hysteresis, false, false, minRegionArea)
        {
        }

        /// <param name="tidy">Closes small gaps and removes what is thinner than three cells.</param>
        /// <param name="keepLargest">The largest piece stays, however small it is.</param>
        /// <param name="minAreaAtObstacles">Pieces beside something that stands on the surface stay from this area on.</param>
        public SurfaceFilterSettings(float minRegionArea, float maxHoleArea, bool hysteresis, bool tidy,
            bool keepLargest, float minAreaAtObstacles)
        {
            MinRegionArea = minRegionArea;
            MaxHoleArea = maxHoleArea;
            Hysteresis = hysteresis;
            Tidy = tidy;
            KeepLargest = keepLargest;
            MinAreaAtObstacles = minAreaAtObstacles;
        }

        /// <summary>Smaller pieces (square meters) are noise, not a surface to place things on.</summary>
        public float MinRegionArea { get; }

        /// <summary>Unseen holes up to this size (square meters) inside a surface are filled.</summary>
        public float MaxHoleArea { get; }

        /// <summary>A cell that was drawn stays until its evidence is clearly gone (no flickering edges).</summary>
        public bool Hysteresis { get; }

        /// <summary>
        /// Gaps of a cell or two are closed (the camera sampled the surface too coarsely there) and what
        /// is thinner than three cells is removed (the top of a chair back is no surface to put things on).
        /// Pieces that touch at a corner count as one piece.
        /// </summary>
        public bool Tidy { get; }

        /// <summary>The largest piece stays, however small it is: a scan starts with a small floor.</summary>
        public bool KeepLargest { get; }

        /// <summary>
        /// Smallest area (square meters) of a piece that lies beside something standing on the surface:
        /// the floor between the legs of chairs and desks comes in small pieces.
        /// </summary>
        public float MinAreaAtObstacles { get; }

        /// <summary>
        /// A piece of floor that lies at a wall, and is not the largest piece, stays when depth confirmed
        /// this much of it (square meters). Less is what the floor plane of ARCore claims behind the
        /// wall, where the camera cannot look.
        /// </summary>
        public float MinConfirmedAtWalls => KeepLargest ? SurfaceRegionFilter.MinAreaBehindWalls : 0f;

        /// <summary>Every redraw decides anew, as <see cref="SurfaceRegionFilter.Apply(SurfaceGrid)"/> does.</summary>
        public static readonly SurfaceFilterSettings Legacy = new SurfaceFilterSettings(
            SurfaceRegionFilter.MinRegionArea, SurfaceRegionFilter.MaxHoleArea, false);

        /// <summary>The room scan: steady edges.</summary>
        public static readonly SurfaceFilterSettings Scan = new SurfaceFilterSettings(
            SurfaceRegionFilter.MinRegionArea, SurfaceRegionFilter.MaxHoleArea, true);

        /// <summary>The floor of the room scan: one large surface, with aisles between the furniture.</summary>
        public static readonly SurfaceFilterSettings Floor = new SurfaceFilterSettings(
            0.25f, 0.5f, true, true, true, 0.1f);

        /// <summary>The raised surfaces of the room scan: table tops, seats, shelves.</summary>
        public static readonly SurfaceFilterSettings Raised = new SurfaceFilterSettings(
            SurfaceRegionFilter.MinRegionArea, SurfaceRegionFilter.MaxHoleArea, true, true, false,
            SurfaceRegionFilter.MinRegionArea);
    }

    /// <summary>
    /// Decides which cells of a <see cref="SurfaceGrid"/> are drawn. Starts from the occupied cells, then
    /// <list type="bullet">
    /// <item>drops pieces smaller than <see cref="MinRegionArea"/> (the scattered fragments), and</item>
    /// <item>fills small holes the camera could not see into (under a monitor stand, a bag), but never
    /// cells the camera looked through (<see cref="CellState.Carved"/>), cells something stands on and
    /// cells under a raised surface.</item>
    /// </list>
    /// Cells something stands on are never drawn.
    /// Buffers are reused, so filtering does not allocate once they are big enough.
    /// </summary>
    public sealed class SurfaceRegionFilter
    {
        /// <summary>Smaller pieces (square meters) are noise, not a surface to place things on.</summary>
        public const float MinRegionArea = 0.06f;

        /// <summary>Unseen holes up to this size (square meters) inside a surface are filled.</summary>
        public const float MaxHoleArea = 0.25f;

        /// <summary>A piece of floor that is cut off by a wall stays when depth confirmed this much of it (square meters).</summary>
        public const float MinAreaBehindWalls = 1f;

        const float CellArea = RoomSurfaceMap.CellSize * RoomSurfaceMap.CellSize;

        readonly List<int> region = new List<int>();
        readonly List<int> largest = new List<int>();
        bool[] visited = Array.Empty<bool>();
        bool[] mask = Array.Empty<bool>();
        bool[] locked = Array.Empty<bool>();
        bool[] scratch = Array.Empty<bool>();
        int[] queue = Array.Empty<int>();
        SurfaceFilterSettings current = SurfaceFilterSettings.Legacy;
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
                mask = new bool[count];
                locked = new bool[count];
                scratch = new bool[count];
                queue = new int[count];
            }

            for (var i = 0; i < count; i++)
            {
                var keep = !grid.IsBlocked(i) && (settings.Hysteresis
                    ? SurfaceEvidence.Keeps(grid.LogOddsAt(i), grid.HasPriorAt(i), grid.PriorWeight, grid.WasKept(i))
                    : grid.StateAt(i) == CellState.Occupied);
                grid.SetKept(i, keep);
                grid.SetShown(i, keep);
            }

            current = settings;
            maxHoleArea = settings.MaxHoleArea;
            if (settings.Tidy)
            {
                Tidy(grid);
            }

            RemoveSmallRegions(grid);
            FillSmallHoles(grid);
        }

        /// <summary>Closes small gaps, then removes thin strips. Cells the scan knows to be empty stay empty.</summary>
        void Tidy(SurfaceGrid grid)
        {
            var count = grid.CellCount;
            for (var i = 0; i < count; i++)
            {
                mask[i] = grid.IsShown(i);
                locked[i] = grid.IsBlocked(i) || grid.StateAt(i) == CellState.Carved;
            }

            MaskMorphology.Close(mask, grid.Width, grid.Depth, locked, scratch);
            MaskMorphology.Open(mask, grid.Width, grid.Depth, locked, scratch);
            for (var i = 0; i < count; i++)
            {
                grid.SetShown(i, mask[i]);
            }
        }

        void RemoveSmallRegions(SurfaceGrid grid)
        {
            Array.Clear(visited, 0, grid.CellCount);
            largest.Clear();
            var largestIsLargeEnough = true;
            for (var start = 0; start < grid.CellCount; start++)
            {
                if (visited[start] || !grid.IsShown(start))
                {
                    continue;
                }

                CollectRegion(grid, start, true);
                var area = region.Count * CellArea;
                var touches = Touches(grid, out var atWall);
                var isLargeEnough = area >= current.MinRegionArea || (area >= current.MinAreaAtObstacles && touches);
                if (atWall && current.MinConfirmedAtWalls > 0f)
                {
                    isLargeEnough &= ConfirmedArea(grid) >= current.MinConfirmedAtWalls;
                }

                if (current.KeepLargest && region.Count > largest.Count)
                {
                    // The piece that was the largest until now stays only if it is large enough by itself.
                    if (!largestIsLargeEnough)
                    {
                        Hide(grid, largest);
                    }

                    largest.Clear();
                    largest.AddRange(region);
                    largestIsLargeEnough = isLargeEnough;
                }
                else if (!isLargeEnough)
                {
                    Hide(grid, region);
                }
            }
        }

        static void Hide(SurfaceGrid grid, List<int> cells)
        {
            foreach (var index in cells)
            {
                grid.SetShown(index, false);
            }
        }

        /// <summary>Area (square meters) of the cells of the region that depth itself saw as surface.</summary>
        float ConfirmedArea(SurfaceGrid grid)
        {
            var cells = 0;
            foreach (var index in region)
            {
                if (grid.LogOddsAt(index) >= SurfaceEvidence.ConfirmedLogOdds)
                {
                    cells++;
                }
            }

            return cells * CellArea;
        }

        /// <summary>A cell of the region lies beside a cell that something stands on.</summary>
        /// <param name="atWall">One of those cells is a cell of a wall.</param>
        bool Touches(SurfaceGrid grid, out bool atWall)
        {
            atWall = false;
            var touches = false;
            foreach (var index in region)
            {
                var x = index % grid.Width;
                var z = index / grid.Width;
                touches |= IsBlockedAt(grid, x - 1, z, ref atWall) | IsBlockedAt(grid, x + 1, z, ref atWall)
                           | IsBlockedAt(grid, x, z - 1, ref atWall) | IsBlockedAt(grid, x, z + 1, ref atWall);
                if (atWall)
                {
                    return true;
                }
            }

            return touches;
        }

        static bool IsBlockedAt(SurfaceGrid grid, int x, int z, ref bool atWall)
        {
            if (!grid.Contains(x, z))
            {
                return false;
            }

            var index = grid.IndexOf(x, z);
            atWall |= grid.IsWall(index);
            return grid.IsBlocked(index);
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
                    if (grid.StateAt(index) != CellState.Carved && !grid.IsBlocked(index) && !grid.IsCovered(index))
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

        /// <summary>Flood fill over cells whose shown flag equals <paramref name="shown"/>.</summary>
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
                if (shown && current.Tidy)
                {
                    // Pieces that touch at a corner are one piece. (Holes are not: a hole that touches
                    // the outside at a corner only is still a hole.)
                    Visit(grid, x - 1, z - 1, true, ref tail);
                    Visit(grid, x + 1, z - 1, true, ref tail);
                    Visit(grid, x - 1, z + 1, true, ref tail);
                    Visit(grid, x + 1, z + 1, true, ref tail);
                }
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
