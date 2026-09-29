using System.Collections.Generic;
using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>Where a scanned surface lies and what it is, for drawing it.</summary>
    internal readonly struct LevelPlacement
    {
        public LevelPlacement(float height, float? floorHeight, bool isFloor, bool wholeFloor, float area)
        {
            Height = height;
            FloorHeight = floorHeight;
            IsFloor = isFloor;
            WholeFloor = wholeFloor;
            Area = area;
        }

        /// <summary>The height the surface is drawn at, in the space of the scan.</summary>
        public float Height { get; }

        /// <summary>The height the floor is drawn at, if there is a floor.</summary>
        public float? FloorHeight { get; }

        public bool IsFloor { get; }

        /// <summary>
        /// The surface is the trusted floor: its depth is steadied all around the camera, not only where
        /// it was scanned.
        /// </summary>
        public bool WholeFloor { get; }

        /// <summary>Drawn area in square meters.</summary>
        public float Area { get; }
    }

    /// <summary>Takes the meshes of the scanned surfaces (the view of the room scan, or nothing in tests).</summary>
    internal interface IRoomModelSink
    {
        /// <summary>Tables get an occluder down to the floor.</summary>
        bool WantsOccluders { get; }

        /// <summary>New meshes of a surface. They lie at height 0: <see cref="Place"/> says where the surface is.</summary>
        void Show(int levelId, SurfaceMeshData data);

        /// <summary>Called for every surface at every rebuild: heights change without new meshes.</summary>
        void Place(int levelId, in LevelPlacement placement);

        /// <summary>Removes the levels that no longer exist (merged or reset). Ends a rebuild.</summary>
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

        /// <summary>Surfaces that got new meshes at the last rebuild.</summary>
        public int Redrawn { get; private set; }

        public void Reset()
        {
            DrawnFloor = null;
            SurfaceCount = 0;
            SurfaceArea = 0f;
            Redrawn = 0;
        }

        /// <summary>
        /// Filters and redraws the levels that changed (all of them when <paramref name="all"/>), and tells
        /// the sink where every level lies now.
        /// </summary>
        /// <param name="seconds">Time since the last rebuild (the drawn heights move at a steady pace).</param>
        /// <param name="sink">Takes the meshes, or null to only decide what is shown.</param>
        public void Rebuild(RoomSurfaceMap map, float seconds, bool all, IRoomModelSink sink)
        {
            map.UpdateDrawnHeights(seconds);
            SurfaceCount = 0;
            SurfaceArea = 0f;
            Redrawn = 0;
            levelIds.Clear();
            foreach (var level in map.Levels)
            {
                levelIds.Add(level.Id);
                if (all || level.IsDirty)
                {
                    level.MarkClean();
                    DrawLevel(level, sink);
                    Redrawn++;
                }

                if (level.ShownArea > 0f)
                {
                    SurfaceCount++;
                    SurfaceArea += level.ShownArea;
                }
            }

            // The floor is looked for after the levels were drawn: until one is picked, it is the lowest drawn surface.
            DrawnFloor = map.FloorHeight(MinFloorArea);
            if (sink == null)
            {
                return;
            }

            var trusted = map.FloorState == FloorState.Trusted;
            foreach (var level in map.Levels)
            {
                sink.Place(level.Id, new LevelPlacement(level.DrawnHeight, DrawnFloor, level.IsFloor,
                    level.IsFloor && trusted && level.IsUsable, level.ShownArea));
            }

            sink.Keep(levelIds);
        }

        void DrawLevel(SurfaceLevel level, IRoomModelSink sink)
        {
            if (level.IsUsable && level.CopyTo(grid))
            {
                filter.Apply(grid, SurfaceFilterSettings.Scan);
                builder.Build(grid, 0f, null, meshData);
                if (sink != null)
                {
                    builder.BuildSteady(grid, meshData);
                    if (sink.WantsOccluders && !level.IsFloor)
                    {
                        builder.BuildOccluder(grid, meshData);
                    }
                }

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
