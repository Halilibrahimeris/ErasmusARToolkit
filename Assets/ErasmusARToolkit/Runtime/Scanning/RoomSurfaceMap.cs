using System.Collections.Generic;
using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// The room model built by the room scan: every horizontal surface height (<see cref="SurfaceLevel"/>)
    /// with what is known about each 5 cm cell at that height. Like the space setup of a VR headset it
    /// fuses many camera frames instead of trusting one:
    /// <list type="bullet">
    /// <item>A depth point on an upward-facing surface near a level's height is a hit for its cell.</item>
    /// <item>A camera ray that passes a level's height and ends clearly beyond it is a miss for the cell
    /// where it crossed: the camera saw through that spot, so nothing lies there at that height. This
    /// removes the parts of ARCore planes that reach past a table edge (ARCore planes are convex hulls).</item>
    /// <item>Planes ARCore detected give their cells a head start (<see cref="AddPrior"/>).</item>
    /// </list>
    /// Levels closer than <see cref="LevelMergeDistance"/> are the same surface, so ARCore's duplicate
    /// floors become one. A dense group of upward-facing depth points at a new height adds a level.
    /// </summary>
    public sealed class RoomSurfaceMap
    {
        public const float CellSize = 0.05f;

        /// <summary>Surfaces closer than this in height (meters) are one surface.</summary>
        public const float LevelMergeDistance = 0.10f;

        /// <summary>Levels whose corrected heights come this close are merged.</summary>
        public const float RefinedMergeDistance = 0.04f;

        public const float MinRange = 0.3f;
        public const float MaxRange = 4.5f;

        /// <summary>A point this close (plus <see cref="HitToleranceSlope"/> per meter) to a level's height lies on it.</summary>
        public const float HitTolerance = 0.03f;
        public const float HitToleranceSlope = 0.01f;

        /// <summary>A ray must end this far (plus <see cref="MissMarginSlope"/> per meter) past a level to carve it.</summary>
        public const float MissMargin = 0.04f;
        public const float MissMarginSlope = 0.02f;

        /// <summary>Flatter rays are skipped: where they cross a height depends too much on depth noise.</summary>
        public const float MinRayVertical = 0.15f;

        const float DiscoveryBinSize = 0.02f;
        const float DiscoveryDecay = 0.7f;
        const float DiscoveryFrameCap = 60f;
        const float DiscoveryThreshold = 150f;
        const float DiscoveryForget = 0.5f;

        /// <summary>New surfaces are only looked for this far below the camera (not the ceiling).</summary>
        const float DiscoveryBelowCamera = 0.2f;

        readonly List<SurfaceLevel> levels = new List<SurfaceLevel>();
        readonly Dictionary<long, Vote> votes = new Dictionary<long, Vote>();
        readonly HashSet<long> checkedCells = new HashSet<long>();
        readonly Dictionary<int, float> discovery = new Dictionary<int, float>();
        readonly Dictionary<int, float> discoveryThisFrame = new Dictionary<int, float>();
        readonly List<int> discoveryKeys = new List<int>();
        readonly List<float> crossings = new List<float>();
        int nextLevelId;

        public IReadOnlyList<SurfaceLevel> Levels => levels;

        /// <summary>Floor area (square meters) the camera has already checked at any height.</summary>
        public float CheckedArea => checkedCells.Count * CellSize * CellSize;

        public int FramesIntegrated { get; private set; }

        public static Vector2Int CellOf(float x, float z)
        {
            return new Vector2Int(Mathf.FloorToInt(x / CellSize), Mathf.FloorToInt(z / CellSize));
        }

        public void Clear()
        {
            levels.Clear();
            votes.Clear();
            checkedCells.Clear();
            discovery.Clear();
            FramesIntegrated = 0;
        }

        /// <summary>Index of the level at (about) this height, made if there is none.</summary>
        public int EnsureLevel(float height)
        {
            var nearest = NearestLevel(height, LevelMergeDistance);
            if (nearest >= 0)
            {
                return nearest;
            }

            levels.Add(new SurfaceLevel(nextLevelId++, height));
            return levels.Count - 1;
        }

        public void ClearPriors()
        {
            foreach (var level in levels)
            {
                level.ClearPriors();
            }
        }

        /// <summary>
        /// Marks the cells inside a detected plane (world x/z polygon, any simple shape) as probably a
        /// surface. Filled row by row (even-odd rule), so large planes stay cheap.
        /// </summary>
        public void AddPrior(float height, IReadOnlyList<Vector2> polygon)
        {
            if (polygon == null || polygon.Count < 3)
            {
                return;
            }

            var level = levels[EnsureLevel(height)];
            var minZ = polygon[0].y;
            var maxZ = polygon[0].y;
            for (var i = 1; i < polygon.Count; i++)
            {
                minZ = Mathf.Min(minZ, polygon[i].y);
                maxZ = Mathf.Max(maxZ, polygon[i].y);
            }

            var lastRow = Mathf.FloorToInt(maxZ / CellSize);
            for (var row = Mathf.FloorToInt(minZ / CellSize); row <= lastRow; row++)
            {
                var z = (row + 0.5f) * CellSize;
                crossings.Clear();
                for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
                {
                    var a = polygon[i];
                    var b = polygon[j];
                    if ((a.y > z) != (b.y > z))
                    {
                        crossings.Add(a.x + (z - a.y) * (b.x - a.x) / (b.y - a.y));
                    }
                }

                crossings.Sort();
                for (var k = 0; k + 1 < crossings.Count; k += 2)
                {
                    // Cells whose centers lie between the two crossings.
                    var first = Mathf.CeilToInt(crossings[k] / CellSize - 0.5f);
                    var last = Mathf.FloorToInt(crossings[k + 1] / CellSize - 0.5f);
                    if (first <= last)
                    {
                        level.SetPriorRun(row, first, last);
                    }
                }
            }
        }

        /// <summary>Adds one camera frame of evidence. Each cell counts at most once per frame.</summary>
        public void Integrate(ScanFrame frame)
        {
            if (frame == null || frame.Count == 0)
            {
                return;
            }

            FramesIntegrated++;
            RefineHeights(frame);
            DiscoverLevels();
            MergeCloseLevels();
            CollectVotes(frame);
            ApplyVotes(frame.HasNormals ? SurfaceEvidence.HitWeight : SurfaceEvidence.SparseHitWeight);
        }

        /// <summary>
        /// Height of the lowest drawn surface of at least <paramref name="minArea"/> square meters (the floor),
        /// else of the lowest drawn surface, else null.
        /// </summary>
        public float? FloorHeight(float minArea)
        {
            float? floor = null;
            float? lowest = null;
            foreach (var level in levels)
            {
                if (level.ShownArea <= 0f)
                {
                    continue;
                }

                if (!lowest.HasValue || level.Height < lowest.Value)
                {
                    lowest = level.Height;
                }

                if (level.ShownArea >= minArea && (!floor.HasValue || level.Height < floor.Value))
                {
                    floor = level.Height;
                }
            }

            return floor ?? lowest;
        }

        /// <summary>Nearest shown surface along the ray. The pose faces up (y) like a detected plane.</summary>
        public bool Raycast(Ray ray, float maxDistance, out Pose pose)
        {
            pose = default;
            var bestDistance = maxDistance;
            var found = false;
            if (Mathf.Abs(ray.direction.y) < 1e-4f)
            {
                return false;
            }

            foreach (var level in levels)
            {
                var distance = (level.Height - ray.origin.y) / ray.direction.y;
                if (distance <= 0f || distance >= bestDistance)
                {
                    continue;
                }

                var point = ray.GetPoint(distance);
                if (level.IsShown(CellOf(point.x, point.z)))
                {
                    bestDistance = distance;
                    pose = new Pose(new Vector3(point.x, level.Height, point.z), Quaternion.identity);
                    found = true;
                }
            }

            return found;
        }

        void RefineHeights(ScanFrame frame)
        {
            discoveryThisFrame.Clear();
            for (var i = 0; i < frame.Count; i++)
            {
                if (frame.HasNormals && !frame.IsUpFacing(i))
                {
                    continue;
                }

                var point = frame.PointAt(i);
                if (!InRange(frame.Origin, point))
                {
                    continue;
                }

                foreach (var level in levels)
                {
                    level.AddHeightSample(point.y);
                }

                if (frame.HasNormals && point.y < frame.Origin.y - DiscoveryBelowCamera
                    && NearestLevel(point.y, LevelMergeDistance) < 0)
                {
                    var bin = Mathf.FloorToInt(point.y / DiscoveryBinSize);
                    discoveryThisFrame.TryGetValue(bin, out var count);
                    discoveryThisFrame[bin] = count + 1f;
                }
            }

            foreach (var level in levels)
            {
                level.RefreshHeight();
            }
        }

        /// <summary>
        /// Upward-facing points at a height with no level build up evidence that fades each frame. Only a
        /// surface seen over several frames makes a new level; one noisy frame does not.
        /// </summary>
        void DiscoverLevels()
        {
            discoveryKeys.Clear();
            discoveryKeys.AddRange(discovery.Keys);
            foreach (var bin in discoveryKeys)
            {
                discovery[bin] *= DiscoveryDecay;
            }

            foreach (var pair in discoveryThisFrame)
            {
                discovery.TryGetValue(pair.Key, out var value);
                discovery[pair.Key] = value + Mathf.Min(pair.Value, DiscoveryFrameCap);
            }

            discoveryKeys.Clear();
            discoveryKeys.AddRange(discovery.Keys);
            foreach (var bin in discoveryKeys)
            {
                if (!discovery.TryGetValue(bin, out var value))
                {
                    continue;
                }

                if (value < DiscoveryForget)
                {
                    discovery.Remove(bin);
                    continue;
                }

                discovery.TryGetValue(bin - 1, out var below);
                discovery.TryGetValue(bin + 1, out var above);
                var total = below + value + above;
                if (total < DiscoveryThreshold || value < below || value < above)
                {
                    continue;
                }

                var center = (bin + 0.5f + (above - below) / total) * DiscoveryBinSize;
                EnsureLevel(center);
                discovery.Remove(bin - 1);
                discovery.Remove(bin);
                discovery.Remove(bin + 1);
            }
        }

        void MergeCloseLevels()
        {
            for (var i = 0; i < levels.Count; i++)
            {
                for (var j = i + 1; j < levels.Count; j++)
                {
                    if (Mathf.Abs(levels[i].Height - levels[j].Height) >= RefinedMergeDistance)
                    {
                        continue;
                    }

                    var keep = levels[i].SampleCount >= levels[j].SampleCount ? i : j;
                    var drop = keep == i ? j : i;
                    levels[keep].Absorb(levels[drop]);
                    levels.RemoveAt(drop);
                    i = -1;
                    break;
                }
            }
        }

        void CollectVotes(ScanFrame frame)
        {
            votes.Clear();
            var origin = frame.Origin;
            for (var i = 0; i < frame.Count; i++)
            {
                var point = frame.PointAt(i);
                if (!InRange(origin, point))
                {
                    continue;
                }

                var ray = point - origin;
                var distance = ray.magnitude;
                var canHit = !frame.HasNormals || frame.IsUpFacing(i);
                var steepEnough = Mathf.Abs(ray.y) >= MinRayVertical * distance;
                for (var level = 0; level < levels.Count; level++)
                {
                    VoteForLevel(level, origin, point, ray, distance, canHit, steepEnough);
                }
            }
        }

        void VoteForLevel(int levelIndex, Vector3 origin, Vector3 point, Vector3 ray, float distance,
            bool canHit, bool steepEnough)
        {
            var level = levels[levelIndex];
            var pointOffset = point.y - level.Height;
            if (Mathf.Abs(pointOffset) <= HitTolerance + HitToleranceSlope * distance)
            {
                if (canHit)
                {
                    AddVote(levelIndex, CellOf(point.x, point.z), true);
                }

                return;
            }

            // Until depth points confirm the level's height, only rays that end far past it may carve.
            var margin = MissMargin + MissMarginSlope * distance + (level.IsConfirmed ? 0f : SurfaceLevel.HeightWindow);
            var originOffset = origin.y - level.Height;
            if (!steepEnough || originOffset * pointOffset >= 0f || Mathf.Abs(pointOffset) <= margin)
            {
                return;
            }

            var crossing = origin + ray * (originOffset / (originOffset - pointOffset));
            AddVote(levelIndex, CellOf(crossing.x, crossing.z), false);
        }

        void AddVote(int level, Vector2Int cell, bool hit)
        {
            var key = ((long)level << 48) ^ ((long)(cell.x & 0xFFFFFF) << 24) ^ (cell.y & 0xFFFFFF);
            votes.TryGetValue(key, out var vote);
            vote.Level = level;
            vote.Cell = cell;
            if (hit)
            {
                vote.Hits++;
            }
            else
            {
                vote.Misses++;
            }

            votes[key] = vote;
        }

        void ApplyVotes(float hitWeight)
        {
            foreach (var vote in votes.Values)
            {
                levels[vote.Level].ApplyVote(vote.Cell, vote.Hits >= vote.Misses, hitWeight);
                checkedCells.Add(((long)vote.Cell.x << 32) ^ (uint)vote.Cell.y);
            }
        }

        int NearestLevel(float height, float maxDistance)
        {
            var nearest = -1;
            var nearestDistance = maxDistance;
            for (var i = 0; i < levels.Count; i++)
            {
                var distance = Mathf.Abs(levels[i].Height - height);
                if (distance < nearestDistance)
                {
                    nearest = i;
                    nearestDistance = distance;
                }
            }

            return nearest;
        }

        static bool InRange(Vector3 origin, Vector3 point)
        {
            var sqr = (point - origin).sqrMagnitude;
            return sqr >= MinRange * MinRange && sqr <= MaxRange * MaxRange;
        }

        struct Vote
        {
            public int Level;
            public Vector2Int Cell;
            public int Hits;
            public int Misses;
        }
    }
}
