using System;
using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// A dense rectangle of cells with what stands on the floor in each: cut out of the
    /// <see cref="ObstacleMap"/> and the raised surfaces for one redraw, with a border of free cells.
    /// Cell (0, 0) is the cell (<see cref="OriginX"/>, <see cref="OriginZ"/>) of the scan.
    /// </summary>
    internal sealed class ObstacleGrid
    {
        /// <summary>The cell is blocked by something that is not a scanned surface.</summary>
        public const int NoLevel = -1;

        bool[] blocked = Array.Empty<bool>();
        float[] tops = Array.Empty<float>();
        float[] confidence = Array.Empty<float>();
        int[] levels = Array.Empty<int>();

        public int OriginX { get; private set; }
        public int OriginZ { get; private set; }
        public int Width { get; private set; }
        public int Depth { get; private set; }
        public int CellCount => Width * Depth;

        public void Reset(int originX, int originZ, int width, int depth)
        {
            OriginX = originX;
            OriginZ = originZ;
            Width = Mathf.Max(width, 1);
            Depth = Mathf.Max(depth, 1);
            var count = Width * Depth;
            if (blocked.Length < count)
            {
                blocked = new bool[count];
                tops = new float[count];
                confidence = new float[count];
                levels = new int[count];
                return;
            }

            Array.Clear(blocked, 0, count);
            Array.Clear(tops, 0, count);
            Array.Clear(confidence, 0, count);
            Array.Clear(levels, 0, count);
        }

        public bool Contains(int x, int z) => x >= 0 && z >= 0 && x < Width && z < Depth;

        public int IndexOf(int x, int z) => z * Width + x;

        public bool IsBlocked(int index) => blocked[index];

        public bool IsBlockedAt(int x, int z) => Contains(x, z) && blocked[IndexOf(x, z)];

        /// <summary>Height of the top of what stands in the cell, or NaN.</summary>
        public float TopAt(int index) => tops[index];

        public float ConfidenceAt(int index) => confidence[index];

        /// <summary>Id of the scanned surface that blocks the cell, or <see cref="NoLevel"/>.</summary>
        public int LevelAt(int index) => levels[index];

        /// <param name="top">Height of the top, or NaN when it is not known.</param>
        /// <param name="levelId">The scanned surface that lies there, or <see cref="NoLevel"/>.</param>
        public void Set(int index, float top, float cellConfidence, int levelId)
        {
            if (blocked[index] && levels[index] != NoLevel && levelId == NoLevel)
            {
                // A scanned surface knows its height better than single points do.
                confidence[index] = Mathf.Max(confidence[index], cellConfidence);
                return;
            }

            blocked[index] = true;
            tops[index] = top;
            confidence[index] = Mathf.Max(confidence[index], cellConfidence);
            levels[index] = levelId;
        }

        public void Free(int index)
        {
            blocked[index] = false;
        }

        /// <summary>Corner of a cell with the smallest x and z, in meters.</summary>
        public Vector2 CornerOf(int x, int z)
        {
            return new Vector2((OriginX + x) * RoomSurfaceMap.CellSize, (OriginZ + z) * RoomSurfaceMap.CellSize);
        }
    }
}
