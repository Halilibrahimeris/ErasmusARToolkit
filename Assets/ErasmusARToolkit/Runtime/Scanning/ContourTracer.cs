using System;
using System.Collections.Generic;
using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// Finds the outlines of a mask of cells: closed loops around every piece and every hole. A loop
    /// runs through the middles of the cell sides that lie between a cell of the mask and a cell
    /// outside of it, so a slanted edge becomes a straight row of points and not a staircase. Outer
    /// loops run counter-clockwise (x to the right, z up) and holes clockwise. Where two cells touch
    /// at a corner only, their loops stay apart.
    /// </summary>
    internal sealed class ContourTracer
    {
        // Sides of a cell, in the order a walk with the cell on its left takes them.
        const int Bottom = 0;
        const int Right = 1;
        const int Top = 2;
        const int Left = 3;

        static readonly int[] StepX = { 1, 0, -1, 0 };
        static readonly int[] StepZ = { 0, 1, 0, -1 };

        readonly Stack<List<Vector2>> pool = new Stack<List<Vector2>>();
        bool[] visited = Array.Empty<bool>();

        /// <summary>Gives the lists of loops that are no longer needed back, so they are used again.</summary>
        public void Recycle(List<List<Vector2>> loops)
        {
            foreach (var loop in loops)
            {
                loop.Clear();
                pool.Push(loop);
            }

            loops.Clear();
        }

        /// <param name="mask">The cells, row by row; cells at the border of the mask have to be off.</param>
        /// <param name="originX">Cell of the scan that cell (0, 0) of the mask is.</param>
        /// <param name="loops">Gets the loops, in meters.</param>
        public void Trace(bool[] mask, int width, int height, int originX, int originZ, List<List<Vector2>> loops)
        {
            Recycle(loops);
            var sides = width * height * 4;
            if (visited.Length < sides)
            {
                visited = new bool[sides];
            }
            else
            {
                Array.Clear(visited, 0, sides);
            }

            for (var z = 1; z < height - 1; z++)
            {
                for (var x = 1; x < width - 1; x++)
                {
                    if (!mask[z * width + x])
                    {
                        continue;
                    }

                    for (var side = 0; side < 4; side++)
                    {
                        if (IsOutline(mask, width, x, z, side) && !visited[(z * width + x) * 4 + side])
                        {
                            loops.Add(Follow(mask, width, x, z, side, originX, originZ));
                        }
                    }
                }
            }
        }

        /// <summary>The side of the cell lies between the cell and a cell that is not in the mask.</summary>
        static bool IsOutline(bool[] mask, int width, int x, int z, int side)
        {
            switch (side)
            {
                case Bottom: return !mask[(z - 1) * width + x];
                case Right: return !mask[z * width + x + 1];
                case Top: return !mask[(z + 1) * width + x];
                default: return !mask[z * width + x - 1];
            }
        }

        List<Vector2> Follow(bool[] mask, int width, int startX, int startZ, int startSide, int originX, int originZ)
        {
            var loop = pool.Count > 0 ? pool.Pop() : new List<Vector2>();
            var x = startX;
            var z = startZ;
            var side = startSide;
            do
            {
                visited[(z * width + x) * 4 + side] = true;
                loop.Add(MiddleOf(x + originX, z + originZ, side));

                // At the end of the side: turn left around the corner of this cell's neighbor if there
                // is one ahead to the right, go straight on along the next cell, or turn around this cell.
                var aheadX = x + StepX[side];
                var aheadZ = z + StepZ[side];
                var outward = (side + 3) % 4;
                var diagonalX = aheadX + StepX[outward];
                var diagonalZ = aheadZ + StepZ[outward];
                var ahead = mask[aheadZ * width + aheadX];
                if (ahead && mask[diagonalZ * width + diagonalX])
                {
                    x = diagonalX;
                    z = diagonalZ;
                    side = outward;
                }
                else if (ahead)
                {
                    x = aheadX;
                    z = aheadZ;
                }
                else
                {
                    side = (side + 1) % 4;
                }
            }
            while (x != startX || z != startZ || side != startSide);

            return loop;
        }

        static Vector2 MiddleOf(int cellX, int cellZ, int side)
        {
            const float size = RoomSurfaceMap.CellSize;
            switch (side)
            {
                case Bottom: return new Vector2((cellX + 0.5f) * size, cellZ * size);
                case Right: return new Vector2((cellX + 1f) * size, (cellZ + 0.5f) * size);
                case Top: return new Vector2((cellX + 0.5f) * size, (cellZ + 1f) * size);
                default: return new Vector2(cellX * size, (cellZ + 0.5f) * size);
            }
        }
    }
}
