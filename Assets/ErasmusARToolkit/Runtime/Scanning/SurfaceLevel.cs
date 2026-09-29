using System.Collections.Generic;
using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// One horizontal surface height of the room (the floor, the table tops, the chair seats...) and what
    /// the scan knows about every 5 cm cell at that height. Cells live in 32 x 32 chunks that are made on
    /// first use, so a level costs memory only where the camera looked. The height starts at the value
    /// ARCore (or the scan) found and is corrected by the depth points that land on the surface.
    /// </summary>
    public sealed class SurfaceLevel
    {
        /// <summary>Height samples needed before the level may carve cells (its height is trusted).</summary>
        public const int ConfirmSamples = 30;

        /// <summary>Points this far (meters) above or below the starting height count toward the height estimate.</summary>
        public const float HeightWindow = HeightBinRadius * HeightBinSize;

        const float HeightBinSize = 0.01f;
        const int HeightBinRadius = 15;
        const int PeakHalfWidth = 2;
        const double SeedSamples = 20.0;
        const int MaxGridCells = 1 << 20;

        readonly Dictionary<Vector2Int, SurfaceChunk> chunks = new Dictionary<Vector2Int, SurfaceChunk>();
        readonly double[] binCounts = new double[HeightBinRadius * 2 + 1];
        readonly double[] binSums = new double[HeightBinRadius * 2 + 1];
        double sampleCount;
        bool warnedTooLarge;

        internal SurfaceLevel(int id, float seedHeight)
        {
            Id = id;
            SeedHeight = seedHeight;
            Height = seedHeight;
        }

        /// <summary>Stays the same while the level exists (list positions change when levels merge).</summary>
        public int Id { get; }

        /// <summary>The height the level was created with.</summary>
        public float SeedHeight { get; }

        /// <summary>Best estimate of the surface height in meters (world y).</summary>
        public float Height { get; private set; }

        public float SampleCount => (float)sampleCount;

        public bool IsConfirmed => sampleCount >= ConfirmSamples;

        /// <summary>Area of the shown cells after the last <see cref="StoreShown"/>, in square meters.</summary>
        public float ShownArea { get; private set; }

        /// <summary>Cells changed since the level was last drawn.</summary>
        internal bool IsDirty { get; private set; } = true;

        public CellState GetState(Vector2Int cell)
        {
            return TryGetCell(cell, out var chunk, out var index)
                ? SurfaceEvidence.Classify(chunk.LogOdds[index], chunk.Prior[index])
                : CellState.Unknown;
        }

        public float GetLogOdds(Vector2Int cell)
        {
            return TryGetCell(cell, out var chunk, out var index) ? chunk.LogOdds[index] : 0f;
        }

        public bool HasPrior(Vector2Int cell)
        {
            return TryGetCell(cell, out var chunk, out var index) && chunk.Prior[index];
        }

        /// <summary>The cell is part of the surface as drawn (after small pieces were removed and holes filled).</summary>
        public bool IsShown(Vector2Int cell)
        {
            return TryGetCell(cell, out var chunk, out var index) && chunk.Shown[index];
        }

        internal void MarkDirty() => IsDirty = true;

        internal void MarkClean() => IsDirty = false;

        /// <summary>Counts a point near this height toward the height estimate. False if it is too far from the seed.</summary>
        internal bool AddHeightSample(float y)
        {
            var bin = Mathf.RoundToInt((y - SeedHeight) / HeightBinSize) + HeightBinRadius;
            if (bin < 0 || bin >= binCounts.Length)
            {
                return false;
            }

            binCounts[bin] += 1.0;
            binSums[bin] += y;
            sampleCount += 1.0;
            return true;
        }

        /// <summary>
        /// Height = mean of the samples around the most crowded height (a 5 cm window), so a smaller second
        /// surface nearby does not pull the estimate. A level made from a drifted ARCore plane moves onto the
        /// real surface next to it, where it merges with that surface's level.
        /// </summary>
        internal void RefreshHeight()
        {
            var bestCenter = HeightBinRadius;
            var bestCount = -1.0;
            for (var center = 0; center < binCounts.Length; center++)
            {
                var count = WindowCount(center);
                if (count > bestCount)
                {
                    bestCount = count;
                    bestCenter = center;
                }
            }

            var sum = 0.0;
            var weight = 0.0;
            for (var bin = bestCenter - PeakHalfWidth; bin <= bestCenter + PeakHalfWidth; bin++)
            {
                if (bin >= 0 && bin < binCounts.Length)
                {
                    sum += binSums[bin];
                    weight += binCounts[bin];
                }
            }

            if (Mathf.Abs(bestCenter - HeightBinRadius) <= PeakHalfWidth)
            {
                sum += SeedHeight * SeedSamples;
                weight += SeedSamples;
            }

            Height = weight > 0.0 ? (float)(sum / weight) : SeedHeight;
        }

        double WindowCount(int center)
        {
            var count = Mathf.Abs(center - HeightBinRadius) <= PeakHalfWidth ? SeedSamples : 0.0;
            for (var bin = center - PeakHalfWidth; bin <= center + PeakHalfWidth; bin++)
            {
                if (bin >= 0 && bin < binCounts.Length)
                {
                    count += binCounts[bin];
                }
            }

            return count;
        }

        internal void ApplyVote(Vector2Int cell, bool hit, float hitWeight)
        {
            var key = SurfaceChunk.ChunkOf(cell);
            var chunk = GetOrCreateChunk(key);
            var index = SurfaceChunk.LocalIndex(cell, key);
            chunk.LogOdds[index] = hit
                ? SurfaceEvidence.AddHit(chunk.LogOdds[index], hitWeight)
                : SurfaceEvidence.AddMiss(chunk.LogOdds[index]);
            IsDirty = true;
        }

        /// <summary>Marks cells <paramref name="firstX"/>..<paramref name="lastX"/> of one row as inside a detected plane.</summary>
        internal void SetPriorRun(int row, int firstX, int lastX)
        {
            var chunkZ = row >> SurfaceChunk.Shift;
            var rowStart = (row - (chunkZ << SurfaceChunk.Shift)) * SurfaceChunk.Size;
            for (var chunkX = firstX >> SurfaceChunk.Shift; chunkX <= lastX >> SurfaceChunk.Shift; chunkX++)
            {
                var chunk = GetOrCreateChunk(new Vector2Int(chunkX, chunkZ));
                var chunkStart = chunkX << SurfaceChunk.Shift;
                var from = Mathf.Max(firstX, chunkStart) - chunkStart;
                var to = Mathf.Min(lastX, chunkStart + SurfaceChunk.Size - 1) - chunkStart;
                for (var x = from; x <= to; x++)
                {
                    chunk.Prior[rowStart + x] = true;
                }
            }

            IsDirty = true;
        }

        internal void ClearPriors()
        {
            foreach (var chunk in chunks.Values)
            {
                System.Array.Clear(chunk.Prior, 0, chunk.Prior.Length);
            }

            IsDirty |= chunks.Count > 0;
        }

        /// <summary>Takes over the cells of a level that turned out to be the same surface.</summary>
        internal void Absorb(SurfaceLevel other)
        {
            foreach (var pair in other.chunks)
            {
                var mine = GetOrCreateChunk(pair.Key);
                var theirs = pair.Value;
                for (var i = 0; i < SurfaceChunk.CellCount; i++)
                {
                    mine.LogOdds[i] = Mathf.Clamp(mine.LogOdds[i] + theirs.LogOdds[i],
                        SurfaceEvidence.MinLogOdds, SurfaceEvidence.MaxLogOdds);
                    mine.Prior[i] |= theirs.Prior[i];
                    mine.Shown[i] |= theirs.Shown[i];
                }
            }

            sampleCount += other.sampleCount;
            IsDirty = true;
        }

        /// <summary>Copies every cell into <paramref name="grid"/>, with a border of unknown cells. False if there are none.</summary>
        internal bool CopyTo(SurfaceGrid grid)
        {
            if (!TryGetChunkBounds(out var min, out var max))
            {
                return false;
            }

            var width = (max.x - min.x + 1) * SurfaceChunk.Size + 2;
            var depth = (max.y - min.y + 1) * SurfaceChunk.Size + 2;
            if ((long)width * depth > MaxGridCells)
            {
                if (!warnedTooLarge)
                {
                    warnedTooLarge = true;
                    Debug.LogWarning("[Erasmus AR Toolkit] A scanned surface is too spread out to draw. Reset the scan.");
                }

                return false;
            }

            grid.Reset(min.x * SurfaceChunk.Size - 1, min.y * SurfaceChunk.Size - 1, width, depth);
            foreach (var pair in chunks)
            {
                var chunk = pair.Value;
                var startX = pair.Key.x * SurfaceChunk.Size - grid.OriginX;
                var startZ = pair.Key.y * SurfaceChunk.Size - grid.OriginZ;
                for (var i = 0; i < SurfaceChunk.CellCount; i++)
                {
                    var x = startX + i % SurfaceChunk.Size;
                    var z = startZ + i / SurfaceChunk.Size;
                    grid.SetCell(x, z, chunk.LogOdds[i], chunk.Prior[i]);
                    grid.SetShown(grid.IndexOf(x, z), chunk.Shown[i]);
                }
            }

            return true;
        }

        /// <summary>Remembers which cells the grid shows (for raycasts) and updates <see cref="ShownArea"/>.</summary>
        internal void StoreShown(SurfaceGrid grid)
        {
            var shownCells = 0;
            foreach (var pair in chunks)
            {
                var chunk = pair.Value;
                var startX = pair.Key.x * SurfaceChunk.Size - grid.OriginX;
                var startZ = pair.Key.y * SurfaceChunk.Size - grid.OriginZ;
                for (var i = 0; i < SurfaceChunk.CellCount; i++)
                {
                    var x = startX + i % SurfaceChunk.Size;
                    var z = startZ + i / SurfaceChunk.Size;
                    var shown = grid.Contains(x, z) && grid.IsShown(grid.IndexOf(x, z));
                    chunk.Shown[i] = shown;
                    if (shown)
                    {
                        shownCells++;
                    }
                }
            }

            ShownArea = shownCells * RoomSurfaceMap.CellSize * RoomSurfaceMap.CellSize;
        }

        /// <summary>Nothing of the level is drawn (used when it cannot be drawn at all).</summary>
        internal void HideAll()
        {
            foreach (var chunk in chunks.Values)
            {
                System.Array.Clear(chunk.Shown, 0, chunk.Shown.Length);
            }

            ShownArea = 0f;
        }

        bool TryGetChunkBounds(out Vector2Int min, out Vector2Int max)
        {
            min = new Vector2Int(int.MaxValue, int.MaxValue);
            max = new Vector2Int(int.MinValue, int.MinValue);
            foreach (var key in chunks.Keys)
            {
                min = Vector2Int.Min(min, key);
                max = Vector2Int.Max(max, key);
            }

            return chunks.Count > 0;
        }

        bool TryGetCell(Vector2Int cell, out SurfaceChunk chunk, out int index)
        {
            var key = SurfaceChunk.ChunkOf(cell);
            index = SurfaceChunk.LocalIndex(cell, key);
            return chunks.TryGetValue(key, out chunk);
        }

        SurfaceChunk GetOrCreateChunk(Vector2Int key)
        {
            if (!chunks.TryGetValue(key, out var chunk))
            {
                chunk = new SurfaceChunk();
                chunks.Add(key, chunk);
            }

            return chunk;
        }
    }

    /// <summary>32 x 32 cells of one surface level.</summary>
    internal sealed class SurfaceChunk
    {
        public const int Shift = 5;
        public const int Size = 1 << Shift;
        public const int CellCount = Size * Size;

        public readonly float[] LogOdds = new float[CellCount];
        public readonly bool[] Prior = new bool[CellCount];
        public readonly bool[] Shown = new bool[CellCount];

        /// <summary>Chunk containing a cell (arithmetic shift rounds negative cells down, as needed).</summary>
        public static Vector2Int ChunkOf(Vector2Int cell) => new Vector2Int(cell.x >> Shift, cell.y >> Shift);

        public static int LocalIndex(Vector2Int cell, Vector2Int chunk)
        {
            return (cell.y - (chunk.y << Shift)) * Size + (cell.x - (chunk.x << Shift));
        }
    }
}
