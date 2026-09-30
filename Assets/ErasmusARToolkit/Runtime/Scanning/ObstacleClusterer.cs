using System;
using System.Collections.Generic;
using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// Turns the blocked cells of the floor into boxes, one per thing that stands in the room, as a VR
    /// headset shows furniture in its room model:
    /// <list type="bullet">
    /// <item>small gaps between blocked cells are closed and the cells that touch are one obstacle,</item>
    /// <item>a box is laid around each: the smallest rectangle on the floor, along the walls of the room
    /// when that fits nearly as well, as high as the obstacle,</item>
    /// <item>an obstacle that fills its box badly (an L-shaped desk) is cut in two, each with a box,</item>
    /// <item>a box that is found again keeps its number, and its place unless it clearly moved: boxes
    /// must not tremble with every redraw.</item>
    /// </list>
    /// </summary>
    internal sealed class ObstacleClusterer
    {
        /// <summary>Fewer cells than this are noise, not an obstacle.</summary>
        public const int MinCells = 8;

        /// <summary>An obstacle fills at least this share of its box, or it is cut in two.</summary>
        public const float MinFill = 0.6f;

        /// <summary>The box reaches this far (meters) past the cells on every side.</summary>
        public const float Padding = 0.025f;

        /// <summary>A box is found again when its center is this close (meters) to where it was.</summary>
        public const float MatchDistance = 0.3f;

        /// <summary>A box that was found again stays as it was unless it moved this far (meters)...</summary>
        public const float KeepDistance = 0.05f;

        /// <summary>...or turned this far (degrees).</summary>
        public const float KeepDegrees = 5f;

        /// <summary>Lower things than this (meters) are not shown as obstacles.</summary>
        public const float MinHeight = 0.05f;

        public const float TableMinHeight = 0.55f;
        public const float TableMaxHeight = 1.2f;
        public const float TableMinArea = 0.3f;
        public const float SeatMinHeight = 0.3f;
        public const float SeatMinArea = 0.08f;
        public const float SeatMaxArea = 0.5f;
        public const float WallMinHeight = 1.5f;
        public const float WallMaxThickness = 0.25f;
        public const float WallMinLength = 1f;

        const float TopPercentile = 0.9f;
        const float LevelShare = 0.5f;
        /// <summary>A thing is cut this many times at most: the walls of a room that meet in corners come apart in three cuts.</summary>
        const int MaxCuts = 3;

        /// <summary>
        /// A cut is made when the boxes of the parts cover less than the box of the whole, by however
        /// little: the thing fills its box badly, so it is not a box. The first cut of a U frees one arm,
        /// the next ones the rest.
        /// </summary>
        const float MaxCutArea = 0.999f;
        const float CellArea = RoomSurfaceMap.CellSize * RoomSurfaceMap.CellSize;

        readonly List<ObstacleVolume> previous = new List<ObstacleVolume>();
        readonly Dictionary<int, double> firstSeen = new Dictionary<int, double>();
        readonly HashSet<int> matched = new HashSet<int>();
        readonly List<int> component = new List<int>();
        readonly List<int> part = new List<int>();
        readonly List<Vector2> corners = new List<Vector2>();
        readonly List<float> heights = new List<float>();
        readonly List<int> gone = new List<int>();
        int[] cutCells = Array.Empty<int>();
        float[] cutAlong = Array.Empty<float>();
        float[] cutAcross = Array.Empty<float>();
        float[] cutWidthAfter = Array.Empty<float>();
        readonly Dictionary<int, int> levelCells = new Dictionary<int, int>();
        readonly Stack<Range> pending = new Stack<Range>();
        bool[] closed = Array.Empty<bool>();
        bool[] grown = Array.Empty<bool>();
        bool[] visited = Array.Empty<bool>();
        int[] queue = Array.Empty<int>();
        int nextId = 1;

        public void Reset()
        {
            previous.Clear();
            firstSeen.Clear();
            nextId = 1;
        }

        /// <param name="levelHeight">The height a scanned surface is drawn at, by its id (NaN when it is gone).</param>
        /// <param name="yaw">Direction of the walls of the room, in degrees.</param>
        /// <param name="time">Now, in seconds: boxes grow older.</param>
        public void Cluster(ObstacleGrid grid, Func<int, float> levelHeight, float floorHeight, float yaw, double time,
            List<ObstacleVolume> output)
        {
            output.Clear();
            matched.Clear();
            Close(grid);
            Array.Clear(visited, 0, grid.CellCount);
            for (var start = 0; start < grid.CellCount; start++)
            {
                if (!closed[start] || visited[start])
                {
                    continue;
                }

                Collect(grid, start);
                if (component.Count >= MinCells)
                {
                    AddBoxes(grid, levelHeight, floorHeight, yaw, time, output);
                }
            }

            previous.Clear();
            previous.AddRange(output);
            Forget();
        }

        /// <summary>Blocked cells, with the gaps of one cell between them closed (grown and shrunk again).</summary>
        void Close(ObstacleGrid grid)
        {
            var count = grid.CellCount;
            if (closed.Length < count)
            {
                closed = new bool[count];
                grown = new bool[count];
                visited = new bool[count];
                queue = new int[count];
            }

            for (var z = 0; z < grid.Depth; z++)
            {
                for (var x = 0; x < grid.Width; x++)
                {
                    grown[grid.IndexOf(x, z)] = AnyAround(grid, x, z);
                }
            }

            for (var z = 0; z < grid.Depth; z++)
            {
                for (var x = 0; x < grid.Width; x++)
                {
                    var index = grid.IndexOf(x, z);
                    closed[index] = grid.IsBlocked(index) || (grown[index] && AllAround(grid, x, z));
                }
            }
        }

        static bool AnyAround(ObstacleGrid grid, int x, int z)
        {
            for (var dz = -1; dz <= 1; dz++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (grid.IsBlockedAt(x + dx, z + dz))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        bool AllAround(ObstacleGrid grid, int x, int z)
        {
            for (var dz = -1; dz <= 1; dz++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (!grid.Contains(x + dx, z + dz) || !grown[grid.IndexOf(x + dx, z + dz)])
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>The cells that touch the start cell (also at their corners).</summary>
        void Collect(ObstacleGrid grid, int start)
        {
            component.Clear();
            var head = 0;
            var tail = 0;
            queue[tail++] = start;
            visited[start] = true;
            while (head < tail)
            {
                var index = queue[head++];
                component.Add(index);
                var x = index % grid.Width;
                var z = index / grid.Width;
                for (var dz = -1; dz <= 1; dz++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        if (!grid.Contains(x + dx, z + dz))
                        {
                            continue;
                        }

                        var next = grid.IndexOf(x + dx, z + dz);
                        if (closed[next] && !visited[next])
                        {
                            visited[next] = true;
                            queue[tail++] = next;
                        }
                    }
                }
            }
        }

        void AddBoxes(ObstacleGrid grid, Func<int, float> levelHeight, float floorHeight, float yaw, double time,
            List<ObstacleVolume> output)
        {
            pending.Clear();
            pending.Push(new Range(0, component.Count, 0));
            while (pending.Count > 0)
            {
                var range = pending.Pop();
                part.Clear();
                for (var i = range.Start; i < range.End; i++)
                {
                    part.Add(component[i]);
                }

                if (!Fit(grid, yaw, out var center, out var size, out var boxYaw))
                {
                    continue;
                }

                var area = size.x * size.y;
                var fill = part.Count * CellArea / Mathf.Max(area, 1e-6f);
                if (fill < MinFill && range.Cuts < MaxCuts && part.Count >= 2 * MinCells
                    && TryCut(grid, range, boxYaw, area, out var middle))
                {
                    pending.Push(new Range(range.Start, middle, range.Cuts + 1));
                    pending.Push(new Range(middle, range.End, range.Cuts + 1));
                    continue;
                }

                AddBox(grid, levelHeight, floorHeight, time, center, size, boxYaw, output);
            }
        }

        bool Fit(ObstacleGrid grid, float yaw, out Vector2 center, out Vector2 size, out float boxYaw)
        {
            corners.Clear();
            const float cell = RoomSurfaceMap.CellSize;
            foreach (var index in part)
            {
                var corner = grid.CornerOf(index % grid.Width, index / grid.Width);
                corners.Add(corner);
                corners.Add(corner + new Vector2(cell, 0f));
                corners.Add(corner + new Vector2(cell, cell));
                corners.Add(corner + new Vector2(0f, cell));
            }

            return MinAreaRectangle.Fit(corners, yaw, out center, out size, out boxYaw);
        }

        /// <summary>
        /// Looks for the cut, straight across the box, after which the two parts fit boxes of their own
        /// best: an L falls apart where its arms meet. The cells of the range are sorted so that the
        /// parts lie before and after <paramref name="middle"/>. False when no cut makes the boxes
        /// clearly smaller.
        /// </summary>
        bool TryCut(ObstacleGrid grid, Range range, float boxYaw, float boxArea, out int middle)
        {
            middle = range.Start;
            MinAreaRectangle.AxesOf(boxYaw, out var axisX, out var axisZ);
            var count = range.End - range.Start;
            EnsureCutBuffers(count);
            var bestArea = boxArea * MaxCutArea;
            var bestAxis = -1;
            var bestIndex = 0;
            for (var axis = 0; axis < 2; axis++)
            {
                SortAlong(grid, range, axis == 0 ? axisX : axisZ, axis == 0 ? axisZ : axisX);
                var index = FindCut(count, ref bestArea);
                if (index > 0)
                {
                    bestAxis = axis;
                    bestIndex = index;
                }
            }

            if (bestAxis < 0)
            {
                return false;
            }

            if (bestAxis == 0)
            {
                SortAlong(grid, range, axisX, axisZ);
            }

            for (var i = 0; i < count; i++)
            {
                component[range.Start + i] = cutCells[i];
            }

            middle = range.Start + bestIndex;
            return true;
        }

        /// <summary>The cells of the range, sorted along an axis, with where they lie along and across it.</summary>
        void SortAlong(ObstacleGrid grid, Range range, Vector2 axis, Vector2 across)
        {
            const float half = RoomSurfaceMap.CellSize * 0.5f;
            var count = range.End - range.Start;
            for (var i = 0; i < count; i++)
            {
                var index = component[range.Start + i];
                cutCells[i] = index;
                cutAlong[i] = Vector2.Dot(grid.CornerOf(index % grid.Width, index / grid.Width) + Vector2.one * half, axis);
            }

            Array.Sort(cutAlong, cutCells, 0, count);
            for (var i = 0; i < count; i++)
            {
                var index = cutCells[i];
                cutAcross[i] = Vector2.Dot(grid.CornerOf(index % grid.Width, index / grid.Width) + Vector2.one * half, across);
            }
        }

        /// <summary>
        /// The place in the sorted cells where a cut leaves the smallest boxes, if they are smaller than
        /// <paramref name="bestArea"/> together. Returns 0 when there is none.
        /// </summary>
        int FindCut(int count, ref float bestArea)
        {
            const float cell = RoomSurfaceMap.CellSize;
            var min = float.MaxValue;
            var max = float.MinValue;
            for (var i = count - 1; i >= 0; i--)
            {
                min = Mathf.Min(min, cutAcross[i]);
                max = Mathf.Max(max, cutAcross[i]);
                cutWidthAfter[i] = max - min + cell;
            }

            var best = 0;
            min = float.MaxValue;
            max = float.MinValue;
            for (var i = 1; i < count; i++)
            {
                min = Mathf.Min(min, cutAcross[i - 1]);
                max = Mathf.Max(max, cutAcross[i - 1]);
                if (i < MinCells || count - i < MinCells || cutAlong[i] - cutAlong[i - 1] < cell * 0.5f)
                {
                    continue;
                }

                var before = (cutAlong[i - 1] - cutAlong[0] + cell) * (max - min + cell);
                var after = (cutAlong[count - 1] - cutAlong[i] + cell) * cutWidthAfter[i];
                if (before + after < bestArea)
                {
                    bestArea = before + after;
                    best = i;
                }
            }

            return best;
        }

        void EnsureCutBuffers(int count)
        {
            if (cutCells.Length < count)
            {
                cutCells = new int[count];
                cutAlong = new float[count];
                cutAcross = new float[count];
                cutWidthAfter = new float[count];
            }
        }

        void AddBox(ObstacleGrid grid, Func<int, float> levelHeight, float floorHeight, double time, Vector2 center,
            Vector2 size, float yaw, List<ObstacleVolume> output)
        {
            heights.Clear();
            levelCells.Clear();
            var confidence = 0f;
            foreach (var index in part)
            {
                confidence += grid.IsBlocked(index) ? grid.ConfidenceAt(index) : 0f;
                if (!grid.IsBlocked(index))
                {
                    continue;
                }

                var top = grid.TopAt(index);
                if (!float.IsNaN(top))
                {
                    heights.Add(top);
                }

                var level = grid.LevelAt(index);
                if (level != ObstacleGrid.NoLevel)
                {
                    levelCells.TryGetValue(level, out var cells);
                    levelCells[level] = cells + 1;
                }
            }

            if (heights.Count == 0)
            {
                return;
            }

            heights.Sort();
            var boxTop = heights[Mathf.Min(heights.Count - 1, Mathf.FloorToInt(TopPercentile * heights.Count))];
            var onLevel = false;
            foreach (var pair in levelCells)
            {
                if (pair.Value < LevelShare * part.Count)
                {
                    continue;
                }

                var height = levelHeight != null ? levelHeight(pair.Key) : float.NaN;
                if (!float.IsNaN(height))
                {
                    boxTop = height;
                    onLevel = true;
                }
            }

            var tall = boxTop - floorHeight;
            if (tall < MinHeight)
            {
                return;
            }

            var area = size.x * size.y;
            var kind = KindOf(tall, size, area, onLevel);
            size += Vector2.one * (2f * Padding);
            var pose = new Pose(new Vector3(center.x, floorHeight, center.y), Quaternion.Euler(0f, yaw, 0f));
            var volume = new ObstacleVolume(0, pose, new Vector3(size.x, tall, size.y), kind,
                Mathf.Clamp01(confidence / part.Count), 0f);
            output.Add(Match(volume, time));
        }

        static ObstacleKind KindOf(float tall, Vector2 size, float area, bool hasFlatTop)
        {
            if (hasFlatTop && tall >= TableMinHeight && tall <= TableMaxHeight && area >= TableMinArea)
            {
                return ObstacleKind.Table;
            }

            if (hasFlatTop && tall >= SeatMinHeight && tall < TableMinHeight && area >= SeatMinArea && area <= SeatMaxArea)
            {
                return ObstacleKind.Seat;
            }

            var thickness = Mathf.Min(size.x, size.y);
            var length = Mathf.Max(size.x, size.y);
            return tall >= WallMinHeight && thickness <= WallMaxThickness && length >= WallMinLength
                ? ObstacleKind.Wall
                : ObstacleKind.Other;
        }

        /// <summary>The box with the number and the age of the box that was there before, if there was one.</summary>
        ObstacleVolume Match(ObstacleVolume volume, double time)
        {
            var best = -1;
            var bestDistance = MatchDistance;
            for (var i = 0; i < previous.Count; i++)
            {
                if (matched.Contains(previous[i].Id))
                {
                    continue;
                }

                var distance = Vector3.Distance(previous[i].Pose.position, volume.Pose.position);
                if (distance <= bestDistance)
                {
                    best = i;
                    bestDistance = distance;
                }
            }

            if (best < 0)
            {
                var id = nextId++;
                firstSeen[id] = time;
                matched.Add(id);
                return new ObstacleVolume(id, volume.Pose, volume.Size, volume.Kind, volume.Confidence, 0f);
            }

            var old = previous[best];
            matched.Add(old.Id);
            var age = firstSeen.TryGetValue(old.Id, out var since) ? (float)(time - since) : 0f;
            var same = bestDistance <= KeepDistance
                       && Quaternion.Angle(old.Pose.rotation, volume.Pose.rotation) <= KeepDegrees
                       && Mathf.Abs(old.Size.x - volume.Size.x) <= KeepDistance
                       && Mathf.Abs(old.Size.y - volume.Size.y) <= KeepDistance
                       && Mathf.Abs(old.Size.z - volume.Size.z) <= KeepDistance
                       && Mathf.Abs(old.Pose.position.y - volume.Pose.position.y) <= 1e-3f;
            return same
                ? new ObstacleVolume(old.Id, old.Pose, old.Size, volume.Kind, volume.Confidence, age)
                : new ObstacleVolume(old.Id, volume.Pose, volume.Size, volume.Kind, volume.Confidence, age);
        }

        /// <summary>Forgets when the boxes that are gone were first seen.</summary>
        void Forget()
        {
            if (firstSeen.Count == previous.Count)
            {
                return;
            }

            gone.Clear();
            foreach (var id in firstSeen.Keys)
            {
                if (!matched.Contains(id))
                {
                    gone.Add(id);
                }
            }

            foreach (var id in gone)
            {
                firstSeen.Remove(id);
            }
        }

        readonly struct Range
        {
            public Range(int start, int end, int cuts)
            {
                Start = start;
                End = end;
                Cuts = cuts;
            }

            public int Start { get; }
            public int End { get; }
            public int Cuts { get; }
        }
    }
}
