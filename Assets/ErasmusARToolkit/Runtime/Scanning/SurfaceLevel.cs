using System.Collections.Generic;
using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// One horizontal surface height of the room (the floor, the table tops, the chair seats...) and what
    /// the scan knows about every 5 cm cell at that height. Cells live in 32 x 32 chunks that are made on
    /// first use, so a level costs memory only where the camera looked. The height starts at the value
    /// ARCore (or the scan) found; <see cref="RoomSurfaceMap"/> corrects it from the depth points on the
    /// surface and from the planes ARCore detects at that height.
    /// </summary>
    public sealed class SurfaceLevel
    {
        /// <summary>Depth observations needed before the level may carve cells (its height is trusted).</summary>
        public const int ConfirmSamples = 30;

        /// <summary>
        /// Depth points this far (meters) above or below the height can move it. Until the height is
        /// confirmed, a camera ray must end this much farther past the level to carve it.
        /// </summary>
        public const float HeightWindow = 0.15f;

        /// <summary>A detected plane this close (meters) to the height is a plane of this surface.</summary>
        const float PlaneNear = 0.05f;

        const int MaxGridCells = 1 << 20;
        const float CellArea = RoomSurfaceMap.CellSize * RoomSurfaceMap.CellSize;

        readonly Dictionary<Vector2Int, SurfaceChunk> chunks = new Dictionary<Vector2Int, SurfaceChunk>();
        readonly List<Vector2> planeSamples = new List<Vector2>();
        HeightLock heightLock;
        int confirmedCells;
        int priorCells;
        bool priorCellsCounted = true;
        bool inPriorBatch;
        bool warnedTooLarge;

        internal SurfaceLevel(int id, float seedHeight, int createdFrame = 0)
        {
            Id = id;
            SeedHeight = seedHeight;
            Height = seedHeight;
            CreatedFrame = createdFrame;
        }

        /// <summary>Stays the same while the level exists (list positions change when levels merge).</summary>
        public int Id { get; }

        /// <summary>The height the level was created with.</summary>
        public float SeedHeight { get; }

        /// <summary>Best estimate of the surface height in meters (y in the space of the map).</summary>
        public float Height { get; private set; }

        /// <summary>Cells that depth itself confirmed as surface.</summary>
        public float SampleCount => confirmedCells;

        /// <summary>Depth points have shown where the surface is, so its height is trusted.</summary>
        public bool IsConfirmed { get; private set; }

        /// <summary>Area of the shown cells after the last <see cref="StoreShown"/>, in square meters.</summary>
        public float ShownArea { get; private set; }

        /// <summary>Cells changed since the level was last drawn.</summary>
        internal bool IsDirty { get; private set; } = true;

        /// <summary>The height the level is drawn at: <see cref="Height"/>, held steady (see <see cref="HeightLock"/>).</summary>
        internal float DrawnHeight => heightLock.HasValue ? heightLock.Value : Height;

        /// <summary>Area (square meters) of the cells that depth confirmed.</summary>
        internal float ConfirmedArea => confirmedCells * CellArea;

        /// <summary>Area (square meters) of the cells inside the planes ARCore detected.</summary>
        internal float PlaneArea
        {
            get
            {
                if (!priorCellsCounted)
                {
                    priorCells = CountPriorCells();
                    priorCellsCounted = true;
                }

                return priorCells * CellArea;
            }
        }

        /// <summary>Height of the largest plane ARCore detected for this surface, if there is one.</summary>
        internal float? PlaneHeight
        {
            get
            {
                float? height = null;
                var largest = 0f;
                for (var i = 0; i < planeSamples.Count; i++)
                {
                    var sample = planeSamples[i];
                    if (sample.y > largest && Mathf.Abs(sample.x - Height) <= PlaneNear)
                    {
                        largest = sample.y;
                        height = sample.x;
                    }
                }

                return height;
            }
        }

        /// <summary>The level is the floor the scan has picked.</summary>
        internal bool IsFloor { get; set; }

        /// <summary>Below the trusted floor: not drawn and not hit while the floor is trusted.</summary>
        internal bool IsSuppressed { get; private set; }

        /// <summary>Its points were seen through the floor: a reflection, not a surface.</summary>
        internal bool IsGhost { get; private set; }

        /// <summary>Drawn and used for placement.</summary>
        internal bool IsUsable => !IsSuppressed && !IsGhost;

        /// <summary>The height came from depth at least once (not only from a plane or the seed).</summary>
        internal bool HasDepthHeight { get; private set; }

        /// <summary>Head start of the cells inside a detected plane (see <see cref="SurfaceEvidence"/>).</summary>
        internal float PriorWeight { get; private set; } = SurfaceEvidence.PriorWeight;

        /// <summary>Frames in a row in which depth and the plane disagreed about the height.</summary>
        internal int PlaneDisagreement { get; set; }

        /// <summary>Hits on this level whose rays were checked against the floor above it.</summary>
        internal float SeenCells { get; private set; }

        /// <summary>Of <see cref="SeenCells"/>, those whose rays went through a solid cell of the floor above.</summary>
        internal float ThroughCells { get; private set; }

        /// <summary>Number of the frame (see <see cref="RoomSurfaceMap.FramesIntegrated"/>) the level was made in.</summary>
        internal int CreatedFrame { get; }

        public CellState GetState(Vector2Int cell)
        {
            return TryGetCell(cell, out var chunk, out var index)
                ? SurfaceEvidence.Classify(chunk.LogOdds[index], chunk.Prior[index], PriorWeight)
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

        internal void SetHeight(float height)
        {
            Height = height;
        }

        /// <summary>Depth points showed where the surface is.</summary>
        internal void ConfirmHeight()
        {
            HasDepthHeight = true;
            IsConfirmed = true;
        }

        /// <summary>
        /// Moves the drawn height toward <see cref="Height"/>. True when it changed. The cells stay as
        /// they are: a surface at another height is the same surface.
        /// </summary>
        internal bool UpdateDrawnHeight(float seconds)
        {
            return heightLock.Update(Height, seconds, PlaneHeight, IsFloor);
        }

        internal void SetPriorWeight(float weight)
        {
            if (!Mathf.Approximately(PriorWeight, weight))
            {
                PriorWeight = weight;
                IsDirty = true;
            }
        }

        internal void SetSuppressed(bool value)
        {
            if (IsSuppressed != value)
            {
                IsSuppressed = value;
                IsDirty = true;
            }
        }

        internal void SetGhost(bool value)
        {
            if (IsGhost != value)
            {
                IsGhost = value;
                IsDirty = true;
            }
        }

        /// <summary>One more hit on this level was checked against the floor above it.</summary>
        internal void NoteSeen(bool throughSolidCell, float weight)
        {
            SeenCells += weight;
            if (throughSolidCell)
            {
                ThroughCells += weight;
            }
        }

        internal void DecaySeen(float factor)
        {
            SeenCells *= factor;
            ThroughCells *= factor;
        }

        internal void ApplyVote(Vector2Int cell, bool hit, float weight)
        {
            var key = SurfaceChunk.ChunkOf(cell);
            var chunk = GetOrCreateChunk(key);
            var index = SurfaceChunk.LocalIndex(cell, key);
            var before = chunk.LogOdds[index];
            var after = hit ? SurfaceEvidence.AddHit(before, weight) : SurfaceEvidence.AddMiss(before, weight);
            chunk.LogOdds[index] = after;
            CountConfirmed(before, after);
            IsDirty = true;
        }

        /// <summary>
        /// Floor cells inside a detected plane that were carved before the floor was known start over:
        /// what carved them were points below the floor, which a floor cannot have.
        /// </summary>
        internal void ForgiveCarvedPriors()
        {
            foreach (var chunk in chunks.Values)
            {
                for (var i = 0; i < SurfaceChunk.CellCount; i++)
                {
                    if (chunk.Prior[i] && chunk.LogOdds[i] < 0f)
                    {
                        chunk.LogOdds[i] = 0f;
                        IsDirty = true;
                    }
                }
            }
        }

        /// <summary>A plane ARCore detected at (about) this height, with the area of its cells.</summary>
        internal void AddPlaneSample(float height, float area)
        {
            planeSamples.Add(new Vector2(height, area));
        }

        /// <summary>
        /// Starts a new reading of the detected planes. Until <see cref="EndPriors"/> the cells are collected
        /// on the side, so a reading that finds the same planes again changes nothing (and redraws nothing).
        /// </summary>
        internal void BeginPriors()
        {
            inPriorBatch = true;
            planeSamples.Clear();
            foreach (var chunk in chunks.Values)
            {
                System.Array.Clear(chunk.PriorNext, 0, SurfaceChunk.CellCount);
            }
        }

        internal void EndPriors()
        {
            if (!inPriorBatch)
            {
                return;
            }

            inPriorBatch = false;
            foreach (var chunk in chunks.Values)
            {
                var changed = false;
                for (var i = 0; i < SurfaceChunk.CellCount && !changed; i++)
                {
                    changed = chunk.Prior[i] != chunk.PriorNext[i];
                }

                if (changed)
                {
                    System.Array.Copy(chunk.PriorNext, chunk.Prior, SurfaceChunk.CellCount);
                    IsDirty = true;
                    priorCellsCounted = false;
                }
            }
        }

        /// <summary>
        /// Marks cells <paramref name="firstX"/>..<paramref name="lastX"/> of one row as inside a detected
        /// plane. Returns how many cells that are.
        /// </summary>
        internal int SetPriorRun(int row, int firstX, int lastX)
        {
            var chunkZ = row >> SurfaceChunk.Shift;
            var rowStart = (row - (chunkZ << SurfaceChunk.Shift)) * SurfaceChunk.Size;
            for (var chunkX = firstX >> SurfaceChunk.Shift; chunkX <= lastX >> SurfaceChunk.Shift; chunkX++)
            {
                var chunk = GetOrCreateChunk(new Vector2Int(chunkX, chunkZ));
                var target = inPriorBatch ? chunk.PriorNext : chunk.Prior;
                var chunkStart = chunkX << SurfaceChunk.Shift;
                var from = Mathf.Max(firstX, chunkStart) - chunkStart;
                var to = Mathf.Min(lastX, chunkStart + SurfaceChunk.Size - 1) - chunkStart;
                for (var x = from; x <= to; x++)
                {
                    target[rowStart + x] = true;
                }
            }

            if (!inPriorBatch)
            {
                IsDirty = true;
                priorCellsCounted = false;
            }

            return lastX - firstX + 1;
        }

        internal void ClearPriors()
        {
            planeSamples.Clear();
            foreach (var chunk in chunks.Values)
            {
                System.Array.Clear(chunk.Prior, 0, chunk.Prior.Length);
            }

            priorCells = 0;
            priorCellsCounted = true;
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
                    // Hits on the other level are hits on this surface. Its misses are not: they came from
                    // rays that crossed the other height, which says nothing about this one.
                    mine.LogOdds[i] = Mathf.Max(mine.LogOdds[i], theirs.LogOdds[i]);
                    mine.Prior[i] |= theirs.Prior[i];
                    mine.PriorNext[i] |= theirs.PriorNext[i];
                    mine.Shown[i] |= theirs.Shown[i];
                    mine.Kept[i] |= theirs.Kept[i];
                }
            }

            planeSamples.AddRange(other.planeSamples);
            confirmedCells = CountConfirmedCells();
            priorCellsCounted = false;
            IsConfirmed |= other.IsConfirmed;
            HasDepthHeight |= other.HasDepthHeight;
            SeenCells += other.SeenCells;
            ThroughCells += other.ThroughCells;
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

            grid.Reset(min.x * SurfaceChunk.Size - 1, min.y * SurfaceChunk.Size - 1, width, depth, PriorWeight);
            foreach (var pair in chunks)
            {
                var chunk = pair.Value;
                var startX = pair.Key.x * SurfaceChunk.Size - grid.OriginX;
                var startZ = pair.Key.y * SurfaceChunk.Size - grid.OriginZ;
                for (var i = 0; i < SurfaceChunk.CellCount; i++)
                {
                    var x = startX + i % SurfaceChunk.Size;
                    var z = startZ + i / SurfaceChunk.Size;
                    var index = grid.IndexOf(x, z);
                    grid.SetCell(x, z, chunk.LogOdds[i], chunk.Prior[i]);
                    grid.SetShown(index, chunk.Shown[i]);
                    grid.SetKept(index, chunk.Kept[i]);
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
                    var inside = grid.Contains(x, z);
                    var index = inside ? grid.IndexOf(x, z) : 0;
                    var shown = inside && grid.IsShown(index);
                    chunk.Shown[i] = shown;
                    chunk.Kept[i] = inside && grid.WasKept(index);
                    if (shown)
                    {
                        shownCells++;
                    }
                }
            }

            ShownArea = shownCells * CellArea;
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

        void CountConfirmed(float before, float after)
        {
            var was = before >= SurfaceEvidence.ConfirmedLogOdds;
            var isNow = after >= SurfaceEvidence.ConfirmedLogOdds;
            if (was != isNow)
            {
                confirmedCells += isNow ? 1 : -1;
            }
        }

        int CountConfirmedCells()
        {
            var count = 0;
            foreach (var chunk in chunks.Values)
            {
                for (var i = 0; i < SurfaceChunk.CellCount; i++)
                {
                    if (chunk.LogOdds[i] >= SurfaceEvidence.ConfirmedLogOdds)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        int CountPriorCells()
        {
            var count = 0;
            foreach (var chunk in chunks.Values)
            {
                for (var i = 0; i < SurfaceChunk.CellCount; i++)
                {
                    if (chunk.Prior[i])
                    {
                        count++;
                    }
                }
            }

            return count;
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

        /// <summary>The planes of the reading that is going on (see <see cref="SurfaceLevel.BeginPriors"/>).</summary>
        public readonly bool[] PriorNext = new bool[CellCount];

        public readonly bool[] Shown = new bool[CellCount];

        /// <summary>Counted as a surface at the last redraw: such a cell is not dropped for one frame of doubt.</summary>
        public readonly bool[] Kept = new bool[CellCount];

        /// <summary>Chunk containing a cell (arithmetic shift rounds negative cells down, as needed).</summary>
        public static Vector2Int ChunkOf(Vector2Int cell) => new Vector2Int(cell.x >> Shift, cell.y >> Shift);

        public static int LocalIndex(Vector2Int cell, Vector2Int chunk)
        {
            return (cell.y - (chunk.y << Shift)) * Size + (cell.x - (chunk.x << Shift));
        }
    }
}
