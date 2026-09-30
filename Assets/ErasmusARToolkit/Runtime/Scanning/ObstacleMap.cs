using System.Collections.Generic;
using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// What stands on the floor: for every 5 cm cell of the floor, whether the camera keeps seeing
    /// something above it, and how high that reaches. Filled by <see cref="RoomSurfaceMap"/> from the
    /// depth points that lie above the trusted floor:
    /// <list type="bullet">
    /// <item>a point above the floor is a hit for the cell under it,</item>
    /// <item>the floor seen in a cell, or a camera ray that passes through the space above a cell on its
    /// way to the floor, is a miss,</item>
    /// <item>a cell is blocked when its hits outweigh its misses and lie at least a second and a half
    /// apart: a person who walks through the room is not furniture,</item>
    /// <item>newer evidence wins: once the floor is seen where something stood, the cell is free
    /// again (the chair was moved).</item>
    /// </list>
    /// Heights are kept as heights of the space of the scan, so they stay right when the height of the
    /// floor is corrected.
    /// </summary>
    internal sealed class ObstacleMap
    {
        public const float HitWeight = 0.25f;
        public const float MissWeight = 0.15f;
        public const float MinLogOdds = -1f;
        public const float MaxLogOdds = 2f;

        /// <summary>A cell becomes blocked above this value...</summary>
        public const float BlockedOn = 0.5f;

        /// <summary>...and free again below this one.</summary>
        public const float BlockedOff = 0.2f;

        /// <summary>The hits of a blocked cell lie at least this far (seconds) apart.</summary>
        public const float MinSecondsApart = 1.5f;

        /// <summary>The floor seen this long (seconds) after the last hit frees the cell.</summary>
        public const float FloorSeenAfter = 0.5f;

        /// <summary>What cells that are not blocked and were not seen lose at every redraw.</summary>
        public const float WeakDecay = 0.08f;

        /// <summary>A top that is higher than the known one counts after this many frames...</summary>
        public const int TopFrames = 3;

        /// <summary>...that agree within this height (meters).</summary>
        public const float TopAgreement = 0.10f;

        /// <summary>A top that is lower than the known one takes it down by this much (meters) per frame.</summary>
        public const float TopSink = 0.02f;

        readonly Dictionary<Vector2Int, Chunk> chunks = new Dictionary<Vector2Int, Chunk>();
        readonly Dictionary<long, FrameVote> votes = new Dictionary<long, FrameVote>();
        readonly List<Vector2Int> emptyChunks = new List<Vector2Int>();
        Vector2Int cachedKey;
        Chunk cachedChunk;
        float clock;
        float frameWeight = 1f;

        /// <summary>Cells that are blocked right now.</summary>
        public int BlockedCount { get; private set; }

        /// <summary>Changes whenever a cell becomes blocked or free.</summary>
        public int Version { get; private set; }

        public bool IsEmpty => chunks.Count == 0;

        public void Clear()
        {
            if (chunks.Count > 0 || BlockedCount > 0)
            {
                Version++;
            }

            chunks.Clear();
            votes.Clear();
            cachedChunk = null;
            BlockedCount = 0;
        }

        /// <param name="time">Time of the frame in seconds.</param>
        /// <param name="weight">How much the frame counts (see <see cref="ScanFrame.Weight"/>).</param>
        public void BeginFrame(double time, float weight)
        {
            clock = (float)time;
            frameWeight = weight;
            votes.Clear();
        }

        /// <summary>The camera saw something at height <paramref name="y"/> above the cell.</summary>
        public void AddHit(Vector2Int cell, float y)
        {
            var key = KeyOf(cell);
            votes.TryGetValue(key, out var vote);
            vote.Cell = cell;
            vote.Top = vote.Hits == 0 ? y : Mathf.Max(vote.Top, y);
            vote.Hits++;
            votes[key] = vote;
        }

        /// <summary>The camera saw the floor in the cell.</summary>
        public void AddFloorSeen(Vector2Int cell)
        {
            var key = KeyOf(cell);
            votes.TryGetValue(key, out var vote);
            vote.Cell = cell;
            vote.FloorSeen = true;
            votes[key] = vote;
        }

        /// <summary>A camera ray passed through the space above the cell, below the top of what stood there.</summary>
        public void AddMiss(Vector2Int cell)
        {
            var key = KeyOf(cell);
            votes.TryGetValue(key, out var vote);
            vote.Cell = cell;
            vote.Misses++;
            votes[key] = vote;
        }

        public void EndFrame()
        {
            foreach (var vote in votes.Values)
            {
                Apply(vote);
            }

            votes.Clear();
        }

        public bool IsBlocked(Vector2Int cell)
        {
            return TryGetCell(cell, out var chunk, out var index) && chunk.Blocked[index];
        }

        /// <summary>Height of the top of what stands in the cell, or NaN when nothing is known there.</summary>
        public float TopOf(Vector2Int cell)
        {
            return TryGetCell(cell, out var chunk, out var index) && chunk.HasTop[index] ? chunk.Top[index] : float.NaN;
        }

        public float LogOddsOf(Vector2Int cell)
        {
            return TryGetCell(cell, out var chunk, out var index) ? chunk.LogOdds[index] : 0f;
        }

        /// <summary>Something may stand in the chunk of the cell (false: a ray can skip the chunk).</summary>
        public bool MayHaveObstacles(Vector2Int cell)
        {
            var key = SurfaceChunk.ChunkOf(cell);
            if (cachedChunk == null || key != cachedKey)
            {
                if (!chunks.TryGetValue(key, out var chunk))
                {
                    return false;
                }

                cachedKey = key;
                cachedChunk = chunk;
            }

            return cachedChunk.Known > 0;
        }

        /// <summary>
        /// Cells with a little evidence that were not seen again lose it, so single noisy frames do not
        /// add up over minutes. Call at every redraw.
        /// </summary>
        public void DecayWeak()
        {
            emptyChunks.Clear();
            foreach (var pair in chunks)
            {
                var chunk = pair.Value;
                for (var i = 0; i < SurfaceChunk.CellCount; i++)
                {
                    var value = chunk.LogOdds[i];
                    if (value > 0f && value <= BlockedOn && !chunk.Blocked[i] && !chunk.Touched[i])
                    {
                        SetLogOdds(chunk, i, Mathf.Max(value - WeakDecay, 0f));
                    }

                    chunk.Touched[i] = false;
                }

                if (chunk.Known == 0 && chunk.BlockedCells == 0)
                {
                    emptyChunks.Add(pair.Key);
                }
            }

            foreach (var key in emptyChunks)
            {
                chunks.Remove(key);
            }

            cachedChunk = null;
        }

        /// <summary>Cell bounds of everything that is known. False when there is nothing.</summary>
        public bool TryGetBounds(out Vector2Int min, out Vector2Int max)
        {
            min = new Vector2Int(int.MaxValue, int.MaxValue);
            max = new Vector2Int(int.MinValue, int.MinValue);
            foreach (var key in chunks.Keys)
            {
                min = Vector2Int.Min(min, key * SurfaceChunk.Size);
                max = Vector2Int.Max(max, key * SurfaceChunk.Size + Vector2Int.one * (SurfaceChunk.Size - 1));
            }

            return chunks.Count > 0;
        }

        /// <summary>
        /// Marks the cells of a floor grid that something stands on, and the cells around them: the grid
        /// of the floor keeps a little distance to the legs of a chair.
        /// </summary>
        public void MarkBlocked(SurfaceGrid floorGrid)
        {
            foreach (var pair in chunks)
            {
                var chunk = pair.Value;
                if (chunk.BlockedCells == 0)
                {
                    continue;
                }

                var startX = pair.Key.x * SurfaceChunk.Size - floorGrid.OriginX;
                var startZ = pair.Key.y * SurfaceChunk.Size - floorGrid.OriginZ;
                for (var i = 0; i < SurfaceChunk.CellCount; i++)
                {
                    if (!chunk.Blocked[i])
                    {
                        continue;
                    }

                    var x = startX + i % SurfaceChunk.Size;
                    var z = startZ + i / SurfaceChunk.Size;
                    for (var dz = -1; dz <= 1; dz++)
                    {
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            if (floorGrid.Contains(x + dx, z + dz))
                            {
                                floorGrid.SetBlocked(floorGrid.IndexOf(x + dx, z + dz), true);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>Marks the blocked cells in a grid that was reset to hold them.</summary>
        public void CopyTo(ObstacleGrid grid)
        {
            foreach (var pair in chunks)
            {
                var chunk = pair.Value;
                if (chunk.BlockedCells == 0)
                {
                    continue;
                }

                var startX = pair.Key.x * SurfaceChunk.Size - grid.OriginX;
                var startZ = pair.Key.y * SurfaceChunk.Size - grid.OriginZ;
                for (var i = 0; i < SurfaceChunk.CellCount; i++)
                {
                    if (!chunk.Blocked[i])
                    {
                        continue;
                    }

                    var x = startX + i % SurfaceChunk.Size;
                    var z = startZ + i / SurfaceChunk.Size;
                    if (grid.Contains(x, z))
                    {
                        grid.Set(grid.IndexOf(x, z), chunk.HasTop[i] ? chunk.Top[i] : float.NaN,
                            Mathf.InverseLerp(BlockedOff, MaxLogOdds, chunk.LogOdds[i]), ObstacleGrid.NoLevel);
                    }
                }
            }
        }

        void Apply(FrameVote vote)
        {
            var key = SurfaceChunk.ChunkOf(vote.Cell);
            if (!chunks.TryGetValue(key, out var chunk))
            {
                if (vote.Hits == 0)
                {
                    // Nothing was known about the cell and nothing stands there.
                    return;
                }

                chunk = new Chunk();
                chunks.Add(key, chunk);
            }

            var index = SurfaceChunk.LocalIndex(vote.Cell, key);
            chunk.Touched[index] = true;
            var value = chunk.LogOdds[index];
            if (vote.Hits > 0 && vote.Hits >= vote.Misses)
            {
                if (value <= 0f || !chunk.HasHit[index])
                {
                    chunk.FirstHit[index] = clock;
                    chunk.HasHit[index] = true;
                }

                chunk.LastHit[index] = clock;
                value = Mathf.Min(Mathf.Max(value, 0f) + HitWeight * frameWeight, MaxLogOdds);
                UpdateTop(chunk, index, vote.Top);
            }
            else if (vote.Misses > 0 || vote.FloorSeen)
            {
                value = Mathf.Max(value - MissWeight * frameWeight, MinLogOdds);
            }

            if (vote.FloorSeen && vote.Hits == 0 && chunk.HasHit[index]
                && clock > chunk.LastHit[index] + FloorSeenAfter)
            {
                value = Mathf.Min(value, 0f);
            }

            SetLogOdds(chunk, index, value);
        }

        void SetLogOdds(Chunk chunk, int index, float value)
        {
            var before = chunk.LogOdds[index];
            chunk.LogOdds[index] = value;
            if ((before > 0f) != (value > 0f))
            {
                chunk.Known += value > 0f ? 1 : -1;
            }

            if (value <= 0f)
            {
                chunk.HasHit[index] = false;
                chunk.HasTop[index] = false;
                chunk.TopCount[index] = 0;
            }

            var blocked = chunk.Blocked[index];
            if (!blocked && value > BlockedOn && chunk.HasHit[index]
                && chunk.LastHit[index] - chunk.FirstHit[index] >= MinSecondsApart)
            {
                blocked = true;
            }
            else if (blocked && value < BlockedOff)
            {
                blocked = false;
            }

            if (blocked != chunk.Blocked[index])
            {
                chunk.Blocked[index] = blocked;
                chunk.BlockedCells += blocked ? 1 : -1;
                BlockedCount += blocked ? 1 : -1;
                Version++;
            }
        }

        /// <summary>
        /// The top follows what is seen, carefully: one frame with a point high above the known top (a
        /// flying pixel) does not raise it, and a lower top takes it down step by step.
        /// </summary>
        static void UpdateTop(Chunk chunk, int index, float seen)
        {
            if (!chunk.HasTop[index])
            {
                chunk.Top[index] = seen;
                chunk.HasTop[index] = true;
                chunk.TopCount[index] = 0;
                return;
            }

            var top = chunk.Top[index];
            if (seen <= top)
            {
                chunk.Top[index] = Mathf.Max(seen, top - TopSink);
                chunk.TopCount[index] = 0;
                return;
            }

            if (chunk.TopCount[index] == 0 || Mathf.Abs(seen - chunk.TopCandidate[index]) > TopAgreement)
            {
                chunk.TopCandidate[index] = seen;
                chunk.TopCount[index] = 1;
                return;
            }

            chunk.TopCandidate[index] = Mathf.Min(chunk.TopCandidate[index], seen);
            if (++chunk.TopCount[index] >= TopFrames)
            {
                chunk.Top[index] = chunk.TopCandidate[index];
                chunk.TopCount[index] = 0;
            }
        }

        bool TryGetCell(Vector2Int cell, out Chunk chunk, out int index)
        {
            var key = SurfaceChunk.ChunkOf(cell);
            index = SurfaceChunk.LocalIndex(cell, key);
            return chunks.TryGetValue(key, out chunk);
        }

        static long KeyOf(Vector2Int cell) => ((long)cell.x << 32) ^ (uint)cell.y;

        struct FrameVote
        {
            public Vector2Int Cell;
            public int Hits;
            public int Misses;
            public float Top;
            public bool FloorSeen;
        }

        sealed class Chunk
        {
            public readonly float[] LogOdds = new float[SurfaceChunk.CellCount];
            public readonly float[] Top = new float[SurfaceChunk.CellCount];
            public readonly float[] TopCandidate = new float[SurfaceChunk.CellCount];
            public readonly float[] FirstHit = new float[SurfaceChunk.CellCount];
            public readonly float[] LastHit = new float[SurfaceChunk.CellCount];
            public readonly byte[] TopCount = new byte[SurfaceChunk.CellCount];
            public readonly bool[] HasTop = new bool[SurfaceChunk.CellCount];
            public readonly bool[] HasHit = new bool[SurfaceChunk.CellCount];
            public readonly bool[] Blocked = new bool[SurfaceChunk.CellCount];
            public readonly bool[] Touched = new bool[SurfaceChunk.CellCount];

            /// <summary>Cells with evidence that something stands there.</summary>
            public int Known;

            public int BlockedCells;
        }
    }
}
