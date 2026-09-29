using System.Collections.Generic;
using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>Takes the meshes of the scanned surfaces (the view of the room scan, or nothing in tests).</summary>
    internal interface IRoomModelSink
    {
        /// <summary>Tables get an occluder down to the floor.</summary>
        bool WantsOccluders { get; }

        void Show(int levelId, SurfaceMeshData data);

        /// <summary>Removes the levels that no longer exist (merged or reset).</summary>
        void Keep(ICollection<int> levelIds);
    }

    /// <summary>
    /// Turns the evidence of a <see cref="RoomSurfaceMap"/> into what is drawn and what placement hits:
    /// per level the cells that count as surface, the drawn height, and the meshes. The room scan and the
    /// tests go through the same steps, so a test sees what the phone draws.
    /// </summary>
    internal sealed class RoomModelBuilder
    {
        /// <summary>Until a floor is picked, the lowest surface of this area (square meters) stands in for it.</summary>
        public const float MinFloorArea = 0.5f;

        /// <summary>Tables are redrawn (their occluders reach the floor) when the floor moved this far (meters).</summary>
        public const float FloorChangeToRedraw = 0.02f;

        readonly SurfaceGrid grid = new SurfaceGrid();
        readonly SurfaceRegionFilter filter = new SurfaceRegionFilter();
        readonly SurfaceMeshBuilder builder = new SurfaceMeshBuilder();
        readonly SurfaceMeshData meshData = new SurfaceMeshData();
        readonly HashSet<int> levelIds = new HashSet<int>();

        /// <summary>Height of the floor at the last rebuild, if there is a floor.</summary>
        public float? DrawnFloor { get; private set; }

        /// <summary>Surfaces (heights) with something drawn on them.</summary>
        public int SurfaceCount { get; private set; }

        /// <summary>Drawn surface area in square meters.</summary>
        public float SurfaceArea { get; private set; }

        public void Reset()
        {
            DrawnFloor = null;
            SurfaceCount = 0;
            SurfaceArea = 0f;
        }

        /// <summary>
        /// Filters and redraws the levels that changed (all of them when <paramref name="all"/>). Tables
        /// get occluders down to the floor; when the floor moves, all are redrawn.
        /// </summary>
        /// <param name="seconds">Time since the last rebuild (the drawn heights move at a steady pace).</param>
        /// <param name="sink">Takes the meshes, or null to only decide what is shown.</param>
        public void Rebuild(RoomSurfaceMap map, float seconds, bool all, IRoomModelSink sink)
        {
            map.UpdateDrawnHeights(seconds);
            var floor = map.FloorHeight(MinFloorArea);
            if (floor.HasValue != DrawnFloor.HasValue
                || (floor.HasValue && Mathf.Abs(floor.Value - DrawnFloor.Value) > FloorChangeToRedraw))
            {
                all = true;
                DrawnFloor = floor;
            }

            var occluderBottom = sink != null && sink.WantsOccluders ? DrawnFloor : null;
            SurfaceCount = 0;
            SurfaceArea = 0f;
            levelIds.Clear();
            foreach (var level in map.Levels)
            {
                levelIds.Add(level.Id);
                if (all || level.IsDirty)
                {
                    level.MarkClean();
                    DrawLevel(level, occluderBottom, sink);
                }

                if (level.ShownArea > 0f)
                {
                    SurfaceCount++;
                    SurfaceArea += level.ShownArea;
                }
            }

            sink?.Keep(levelIds);
        }

        void DrawLevel(SurfaceLevel level, float? occluderBottom, IRoomModelSink sink)
        {
            if (level.IsUsable && level.CopyTo(grid))
            {
                filter.Apply(grid, SurfaceFilterSettings.Scan);
                builder.Build(grid, level.DrawnHeight, level.IsFloor ? null : occluderBottom, meshData);
                level.StoreShown(grid);
            }
            else
            {
                level.HideAll();
                meshData.Clear();
            }

            sink?.Show(level.Id, meshData);
        }
    }
}
