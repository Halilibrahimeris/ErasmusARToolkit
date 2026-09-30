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

        /// <summary>The boxes around what stands in the room, in the space of the scan.</summary>
        void ShowObstacles(ObstacleMeshData data);

        /// <summary>The walls of the room, in the space of the scan.</summary>
        /// <param name="floorHeight">The height the walls stand on.</param>
        void ShowWalls(ObstacleMeshData data, float floorHeight);

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

        /// <summary>
        /// Surfaces that are drawn anew at one redraw, at most, while the scan runs. The others wait
        /// for the next redraw: a redraw must not take longer than a frame of the app.
        /// </summary>
        public const int MaxRedraws = 3;

        /// <summary>What was taken for an obstacle up to this far (meters) above a podium is the podium.</summary>
        const float TierTolerance = 0.1f;

        const long MaxObstacleCells = 1 << 20;

        readonly SurfaceGrid grid = new SurfaceGrid();
        readonly SurfaceRegionFilter filter = new SurfaceRegionFilter();
        readonly SurfaceMeshBuilder builder = new SurfaceMeshBuilder();
        readonly SurfaceMeshData meshData = new SurfaceMeshData();
        readonly HashSet<int> levelIds = new HashSet<int>();
        readonly ObstacleGrid obstacleGrid = new ObstacleGrid();
        readonly ObstacleClusterer clusterer = new ObstacleClusterer();
        readonly ObstacleMeshData obstacleMesh = new ObstacleMeshData();
        readonly ObstacleMeshData wallMesh = new ObstacleMeshData();
        readonly List<ObstacleVolume> boxes = new List<ObstacleVolume>();
        readonly List<ObstacleVolume> before = new List<ObstacleVolume>();
        readonly Dictionary<int, SurfaceShape> shapes = new Dictionary<int, SurfaceShape>();
        readonly List<int> goneShapes = new List<int>();
        readonly RoomAxes axes = new RoomAxes();
        System.Func<int, float> heightOfLevel;
        RoomSurfaceMap current;
        bool settle;
        int nextLevel;
        int lastDrawn;
        int obstacleVersion = -1;

        /// <summary>Height of the floor at the last rebuild, if there is a floor.</summary>
        public float? DrawnFloor { get; private set; }

        /// <summary>Surfaces (heights) with something drawn on them.</summary>
        public int SurfaceCount { get; private set; }

        /// <summary>Drawn surface area in square meters.</summary>
        public float SurfaceArea { get; private set; }

        /// <summary>Surfaces that got new meshes at the last rebuild.</summary>
        public int Redrawn { get; private set; }

        /// <summary>The boxes around the obstacles changed at the last rebuild.</summary>
        public bool ObstaclesChanged { get; private set; }

        /// <summary>The walls changed at the last rebuild.</summary>
        public bool WallsChanged { get; private set; }

        /// <summary>Walls say this many times more about the direction of the room than the edges of surfaces.</summary>
        public const float WallWeight = 3f;

        /// <summary>
        /// Direction of the walls of the room (degrees around the vertical axis, in the space of the
        /// scan): outlines, boxes and the grid are laid along it. 0 until it is known.
        /// </summary>
        public float RoomYaw => axes.Yaw;

        /// <summary>The direction of the room, as it is found from the edges of the scanned surfaces.</summary>
        internal RoomAxes Axes => axes;

        /// <summary>
        /// Edges that say where the walls of the room are, apart from the outlines of the surfaces: they
        /// are added to <see cref="Axes"/> at every redraw.
        /// </summary>
        internal System.Action<RoomAxes> MoreEdges { get; set; }

        /// <summary>
        /// The scan is finished: the direction of the room is taken as the surfaces show it now, and
        /// stays. Redraw everything afterwards.
        /// </summary>
        public void SettleAxes()
        {
            axes.Settle();
            axes.Freeze();
            settle = true;
        }

        /// <summary>The outlines a surface is drawn with, or null.</summary>
        internal SurfaceShape ShapeOf(int levelId)
        {
            return shapes.TryGetValue(levelId, out var shape) ? shape : null;
        }

        public void Reset()
        {
            DrawnFloor = null;
            SurfaceCount = 0;
            SurfaceArea = 0f;
            Redrawn = 0;
            ObstaclesChanged = false;
            WallsChanged = false;
            obstacleVersion = -1;
            clusterer.Reset();
            shapes.Clear();
            axes.Reset();
            nextLevel = 0;
            lastDrawn = 0;
        }

        /// <summary>Surfaces that changed and were not drawn yet.</summary>
        public int Waiting(RoomSurfaceMap map)
        {
            var waiting = 0;
            foreach (var level in map.Levels)
            {
                waiting += level.IsDirty ? 1 : 0;
            }

            return waiting;
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
            current = map;
            SurfaceCount = 0;
            SurfaceArea = 0f;
            Redrawn = 0;
            levelIds.Clear();
            MarkFloorDirty(map);

            // Raised surfaces first: the floor is not drawn under what stands on it, and that may be a
            // table. When many changed, those that waited longest come first (the list is walked from
            // where the last redraw stopped).
            var count = map.Levels.Count;
            var raisedLimit = all ? int.MaxValue : MaxRedraws - 1;
            for (var i = 0; i < count && Redrawn < raisedLimit; i++)
            {
                var level = map.Levels[(nextLevel + i) % count];
                if (!level.IsFloor && (all || level.IsDirty))
                {
                    level.MarkClean();
                    DrawLevel(level, sink);
                    Redrawn++;
                    lastDrawn = (nextLevel + i) % count;
                    map.Floor.Level?.MarkDirty();
                }
            }

            nextLevel = count > 0 ? (lastDrawn + 1) % count : 0;
            foreach (var level in map.Levels)
            {
                levelIds.Add(level.Id);
                if (level.IsFloor && (all || level.IsDirty))
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
            FindRoomAxes();
            FindObstacles(map);
            FindWalls(map);
            settle = false;
            if (sink == null)
            {
                return;
            }

            if (ObstaclesChanged || WallsChanged || all)
            {
                // A wall is drawn as a wall, not as a box.
                boxes.Clear();
                foreach (var volume in map.Volumes)
                {
                    if (volume.Kind != ObstacleKind.Wall || !map.DetectWalls)
                    {
                        boxes.Add(volume);
                    }
                }

                ObstacleMeshBuilder.Build(boxes, obstacleMesh);
                sink.ShowObstacles(obstacleMesh);
            }

            if (WallsChanged || all)
            {
                WallMeshBuilder.Build(map.Walls.Segments, wallMesh);
                sink.ShowWalls(wallMesh, DrawnFloor ?? 0f);
            }

            var trusted = map.FloorState == FloorState.Trusted;
            foreach (var level in map.Levels)
            {
                sink.Place(level.Id, new LevelPlacement(level.DrawnHeight, DrawnFloor, level.IsFloor,
                    level.IsFloor && trusted && level.IsUsable, level.ShownArea));
            }

            sink.Keep(levelIds);
        }

        /// <summary>
        /// The direction of the walls, from the edges of all surfaces as they were scanned. Surfaces
        /// that are drawn from now on are laid along it.
        /// </summary>
        void FindRoomAxes()
        {
            goneShapes.Clear();
            foreach (var id in shapes.Keys)
            {
                if (!levelIds.Contains(id))
                {
                    goneShapes.Add(id);
                }
            }

            foreach (var id in goneShapes)
            {
                shapes.Remove(id);
            }

            if (axes.IsFrozen)
            {
                return;
            }

            axes.Begin();
            foreach (var shape in shapes.Values)
            {
                for (var i = 0; i < shape.Scanned.Count; i++)
                {
                    axes.AddLoop(shape.Scanned[i]);
                }
            }

            if (current != null && current.DetectWalls)
            {
                current.Walls.AddEdgesTo(axes, WallWeight);
            }

            MoreEdges?.Invoke(axes);
            axes.Update();
        }

        /// <summary>
        /// The walls of the room, from the vertical planes and from what depth saw. They stand on the
        /// floor, so there are none before the floor is known. Walls that changed redraw the floor: it
        /// ends at them.
        /// </summary>
        void FindWalls(RoomSurfaceMap map)
        {
            var walls = map.Walls;
            var floor = map.Floor.Level;
            if (!map.DetectWalls || floor == null || map.FloorState == FloorState.Unknown || !floor.IsUsable)
            {
                WallsChanged = walls.Segments.Count > 0;
                if (WallsChanged)
                {
                    walls.ClearObstacleWalls();
                    walls.Rebuild(float.MaxValue, float.NaN);
                }

                return;
            }

            walls.SetObstacleWalls(map.Volumes);
            WallsChanged = walls.Rebuild(floor.DrawnHeight, axes.HasYaw ? axes.Yaw : float.NaN);
            if (WallsChanged)
            {
                floor.MarkDirty();
            }
        }

        /// <summary>The floor is drawn anew when something on it appeared or went away.</summary>
        void MarkFloorDirty(RoomSurfaceMap map)
        {
            var version = map.Obstacles.Version;
            if (version != obstacleVersion)
            {
                obstacleVersion = version;
                map.Floor.Level?.MarkDirty();
            }
        }

        /// <summary>
        /// Lays boxes around what stands in the room: the blocked cells of the floor and the raised
        /// surfaces, except podiums (see <see cref="RoomSurfaceMap.IsTier"/>).
        /// </summary>
        void FindObstacles(RoomSurfaceMap map)
        {
            before.Clear();
            before.AddRange(map.Volumes);
            var floor = map.Floor.Level;
            if (!map.DetectObstacles || floor == null || map.FloorState != FloorState.Trusted || !floor.IsUsable
                || !TryGetObstacleBounds(map, out var min, out var max))
            {
                map.Volumes.Clear();
                ObstaclesChanged = before.Count > 0;
                return;
            }

            obstacleGrid.Reset(min.x - 1, min.y - 1, max.x - min.x + 3, max.y - min.y + 3);
            map.Obstacles.CopyTo(obstacleGrid);
            foreach (var level in map.Levels)
            {
                if (level == floor || !level.IsUsable || level.ShownArea <= 0f
                    || level.DrawnHeight < floor.DrawnHeight + SurfaceMeshBuilder.MinOccluderHeight)
                {
                    continue;
                }

                if (map.IsTier(level))
                {
                    level.FreeObstaclesOn(obstacleGrid, TierTolerance);
                }
                else
                {
                    level.MarkObstacle(obstacleGrid);
                }
            }

            heightOfLevel ??= HeightOfLevel;
            clusterer.Cluster(obstacleGrid, heightOfLevel, floor.DrawnHeight, axes.Yaw, map.Clock, map.Volumes);
            ObstaclesChanged = !Same(before, map.Volumes);
        }

        float HeightOfLevel(int id)
        {
            var level = current != null ? current.FindLevel(id) : null;
            return level != null ? level.DrawnHeight : float.NaN;
        }

        static bool TryGetObstacleBounds(RoomSurfaceMap map, out Vector2Int min, out Vector2Int max)
        {
            var any = map.Obstacles.TryGetBounds(out min, out max);
            foreach (var level in map.Levels)
            {
                if (level.IsFloor || !level.IsUsable || level.ShownArea <= 0f
                    || !level.TryGetCellBounds(out var levelMin, out var levelMax))
                {
                    continue;
                }

                min = any ? Vector2Int.Min(min, levelMin) : levelMin;
                max = any ? Vector2Int.Max(max, levelMax) : levelMax;
                any = true;
            }

            // A room is not larger than this; a grid that is, comes from a mistake.
            return any && (long)(max.x - min.x + 3) * (max.y - min.y + 3) <= MaxObstacleCells;
        }

        /// <summary>The same boxes at the same places: nothing to draw anew. Their age does not count.</summary>
        static bool Same(List<ObstacleVolume> a, List<ObstacleVolume> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }

            for (var i = 0; i < a.Count; i++)
            {
                if (a[i].Id != b[i].Id || a[i].Kind != b[i].Kind || a[i].Pose != b[i].Pose || a[i].Size != b[i].Size)
                {
                    return false;
                }
            }

            return true;
        }

        void DrawLevel(SurfaceLevel level, IRoomModelSink sink)
        {
            if (level.IsUsable && level.CopyTo(grid))
            {
                if (level.IsFloor)
                {
                    MarkFloorCells(level);
                }

                filter.Apply(grid, level.IsFloor ? SurfaceFilterSettings.Floor : SurfaceFilterSettings.Raised);
                if (!shapes.TryGetValue(level.Id, out var shape))
                {
                    shape = new SurfaceShape();
                    shapes.Add(level.Id, shape);
                }

                builder.BuildClean(grid, new SurfaceBuildSettings(level.IsFloor, axes.Yaw, axes.HasYaw, settle), shape,
                    meshData);
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
                shapes.Remove(level.Id);
            }

            sink?.Show(level.Id, meshData);
        }

        /// <summary>
        /// On the grid of the floor: the cells something stands on are not drawn (the legs of a chair, a
        /// bag, a table), and the cells under a raised surface are not filled in when the camera never
        /// saw them. The floor under a podium is left alone: a podium is a floor itself.
        /// </summary>
        void MarkFloorCells(SurfaceLevel floor)
        {
            if (current == null)
            {
                return;
            }

            var findsObstacles = current.DetectObstacles && current.FloorState == FloorState.Trusted;
            if (findsObstacles)
            {
                current.Obstacles.MarkBlocked(grid);
            }

            if (current.DetectWalls)
            {
                current.Walls.MarkBlocked(grid);
            }

            foreach (var level in current.Levels)
            {
                if (level == floor || !level.IsUsable || level.Height <= floor.Height)
                {
                    continue;
                }

                var stands = findsObstacles && !current.IsTier(level)
                             && level.DrawnHeight >= floor.DrawnHeight + SurfaceMeshBuilder.MinOccluderHeight;
                level.MarkCovered(grid, stands);
            }
        }
    }
}
