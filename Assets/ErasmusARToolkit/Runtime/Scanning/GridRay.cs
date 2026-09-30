using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// Walks along a line over the 5 cm cells of the scan (seen from above), cell by cell and without
    /// skipping any (Amanatides and Woo). Used to find the cells a camera ray passes over.
    /// </summary>
    internal struct GridRay
    {
        const float Size = RoomSurfaceMap.CellSize;

        int x;
        int z;
        int stepX;
        int stepZ;
        float nextX;
        float nextZ;
        float deltaX;
        float deltaZ;
        float reached;
        bool started;

        /// <summary>The cell the walk is in.</summary>
        public Vector2Int Cell => new Vector2Int(x, z);

        /// <summary>Starts a walk from <paramref name="origin"/> to <paramref name="end"/> (x and z in meters).</summary>
        public void Start(Vector2 origin, Vector2 end)
        {
            x = Mathf.FloorToInt(origin.x / Size);
            z = Mathf.FloorToInt(origin.y / Size);
            var direction = end - origin;
            stepX = direction.x > 0f ? 1 : -1;
            stepZ = direction.y > 0f ? 1 : -1;

            // Everything is measured in shares of the way from the origin to the end.
            deltaX = Mathf.Abs(direction.x) > 1e-9f ? Size / Mathf.Abs(direction.x) : float.PositiveInfinity;
            deltaZ = Mathf.Abs(direction.y) > 1e-9f ? Size / Mathf.Abs(direction.y) : float.PositiveInfinity;
            var borderX = (direction.x > 0f ? x + 1 : x) * Size;
            var borderZ = (direction.y > 0f ? z + 1 : z) * Size;
            nextX = float.IsInfinity(deltaX) ? float.PositiveInfinity : (borderX - origin.x) / direction.x;
            nextZ = float.IsInfinity(deltaZ) ? float.PositiveInfinity : (borderZ - origin.y) / direction.y;
            reached = 0f;
            started = false;
        }

        /// <summary>
        /// The next cell of the walk, beginning with the cell of the origin. False when the end is reached.
        /// </summary>
        /// <param name="enter">Share (0..1) of the way at which the line enters the cell.</param>
        /// <param name="exit">Share of the way at which it leaves the cell (at most 1).</param>
        public bool Next(out Vector2Int cell, out float enter, out float exit)
        {
            if (started)
            {
                if (reached >= 1f)
                {
                    cell = default;
                    enter = exit = 1f;
                    return false;
                }

                if (nextX < nextZ)
                {
                    x += stepX;
                    nextX += deltaX;
                }
                else
                {
                    z += stepZ;
                    nextZ += deltaZ;
                }
            }

            started = true;
            cell = new Vector2Int(x, z);
            enter = reached;
            reached = Mathf.Min(Mathf.Min(nextX, nextZ), 1f);
            exit = reached;
            return true;
        }
    }
}
