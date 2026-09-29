using System;
using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// A dense rectangle of cells cut out of one surface level, with a one-cell border of unknown cells
    /// around it. <see cref="SurfaceRegionFilter"/> decides which cells are shown and
    /// <see cref="SurfaceMeshBuilder"/> turns the shown cells into meshes. Cell (0, 0) is the world cell
    /// (<see cref="OriginX"/>, <see cref="OriginZ"/>). Arrays grow when needed and are reused.
    /// </summary>
    public sealed class SurfaceGrid
    {
        float[] logOdds = Array.Empty<float>();
        bool[] prior = Array.Empty<bool>();
        bool[] shown = Array.Empty<bool>();
        bool[] kept = Array.Empty<bool>();
        CellState[] states = Array.Empty<CellState>();
        float priorWeight = SurfaceEvidence.PriorWeight;

        public int OriginX { get; private set; }
        public int OriginZ { get; private set; }
        public int Width { get; private set; }
        public int Depth { get; private set; }
        public int CellCount => Width * Depth;

        /// <summary>Resizes the grid and marks every cell unknown and hidden.</summary>
        /// <param name="cellPriorWeight">Head start of the cells inside a detected plane (see <see cref="SurfaceEvidence"/>).</param>
        public void Reset(int originX, int originZ, int width, int depth,
            float cellPriorWeight = SurfaceEvidence.PriorWeight)
        {
            if (width <= 0 || depth <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "A surface grid needs at least one cell.");
            }

            OriginX = originX;
            OriginZ = originZ;
            Width = width;
            Depth = depth;
            priorWeight = cellPriorWeight;
            var count = width * depth;
            if (logOdds.Length < count)
            {
                logOdds = new float[count];
                prior = new bool[count];
                shown = new bool[count];
                kept = new bool[count];
                states = new CellState[count];
                return;
            }

            Array.Clear(logOdds, 0, count);
            Array.Clear(prior, 0, count);
            Array.Clear(shown, 0, count);
            Array.Clear(kept, 0, count);
            Array.Clear(states, 0, count);
        }

        /// <summary>Head start of the cells inside a detected plane.</summary>
        public float PriorWeight => priorWeight;

        public bool Contains(int x, int z) => x >= 0 && z >= 0 && x < Width && z < Depth;

        public int IndexOf(int x, int z) => z * Width + x;

        public void SetCell(int x, int z, float cellLogOdds, bool cellPrior)
        {
            var index = IndexOf(x, z);
            logOdds[index] = cellLogOdds;
            prior[index] = cellPrior;
            states[index] = SurfaceEvidence.Classify(cellLogOdds, cellPrior, priorWeight);
        }

        public CellState StateAt(int index) => states[index];

        public float ScoreAt(int index) => SurfaceEvidence.Score(logOdds[index], prior[index], priorWeight);

        public float LogOddsAt(int index) => logOdds[index];

        public bool HasPriorAt(int index) => prior[index];

        /// <summary>The cell counted as a surface at the last redraw (before pieces were dropped and holes filled).</summary>
        public bool WasKept(int index) => kept[index];

        public void SetKept(int index, bool value) => kept[index] = value;

        public bool IsShown(int index) => shown[index];

        public void SetShown(int index, bool value) => shown[index] = value;

        /// <summary>World position of a cell center at the given height.</summary>
        public Vector3 CellCenter(int x, int z, float height)
        {
            return new Vector3((OriginX + x + 0.5f) * RoomSurfaceMap.CellSize, height,
                (OriginZ + z + 0.5f) * RoomSurfaceMap.CellSize);
        }
    }
}
