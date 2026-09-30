using System.Collections.Generic;
using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>A piece of wall of the scanned room: a straight line on the floor, from the floor up.</summary>
    public readonly struct WallSegment
    {
        public WallSegment(Vector2 a, Vector2 b, float bottom, float top, float confidence, bool fromPlane)
        {
            A = a;
            B = b;
            Bottom = bottom;
            Top = top;
            Confidence = confidence;
            FromPlane = fromPlane;
        }

        /// <summary>One end of the wall on the floor (x and z in meters).</summary>
        public Vector2 A { get; }

        /// <summary>The other end.</summary>
        public Vector2 B { get; }

        /// <summary>Height of the floor the wall stands on.</summary>
        public float Bottom { get; }

        /// <summary>Height up to which the wall was seen.</summary>
        public float Top { get; }

        /// <summary>How sure the scan is of the wall (0..1).</summary>
        public float Confidence { get; }

        /// <summary>True: ARCore detected a vertical plane there. False: depth saw something tall, long and thin.</summary>
        public bool FromPlane { get; }

        public float Length => Vector2.Distance(A, B);

        public Vector2 Direction => (B - A).normalized;

        /// <summary>Distance from a point on the floor to the wall.</summary>
        public float DistanceTo(Vector2 point) => PolygonMath.DistanceToSegment(point, A, B);

        /// <summary>Where a ray goes through the wall. False when it passes beside, above or below it.</summary>
        public bool Raycast(Ray ray, out float distance)
        {
            distance = 0f;
            var along = B - A;
            var length = along.magnitude;
            if (length < 1e-4f)
            {
                return false;
            }

            along /= length;
            var normal = new Vector2(-along.y, along.x);
            var approach = ray.direction.x * normal.x + ray.direction.z * normal.y;
            if (Mathf.Abs(approach) < 1e-6f)
            {
                return false;
            }

            var from = new Vector2(ray.origin.x, ray.origin.z) - A;
            distance = -Vector2.Dot(from, normal) / approach;
            if (distance <= 0f)
            {
                return false;
            }

            var point = ray.GetPoint(distance);
            var on = Vector2.Dot(new Vector2(point.x, point.z) - A, along);
            return on >= 0f && on <= length && point.y >= Bottom && point.y <= Top;
        }
    }

    /// <summary>
    /// The walls of the scanned room, as far as the phone can find them: where the room ends. From two
    /// sources:
    /// <list type="bullet">
    /// <item>vertical planes ARCore detected (walls with pictures, shelves, anything with texture),</item>
    /// <item>tall, long and thin obstacles the depth camera saw (plain walls, on which ARCore finds no
    /// plane).</item>
    /// </list>
    /// Pieces that lie on one line become one wall, walls are laid along the direction of the room, and
    /// walls that nearly meet at a corner are made to meet. All in the space of the scan.
    /// <para>
    /// The room lies on the side of a wall the camera was on. What ARCore claims of the floor on the
    /// other side, and depth never saw, is not drawn: the floor planes of ARCore reach through walls.
    /// </para>
    /// </summary>
    internal sealed class WallModel
    {
        /// <summary>A vertical plane is a wall when it is this high (meters)...</summary>
        public const float MinPlaneHeight = 1f;

        /// <summary>...or reaches this far (meters) above the floor (the lower part may be hidden by furniture).</summary>
        public const float MinTopAboveFloor = 1.5f;

        /// <summary>...and is this long (meters).</summary>
        public const float MinLength = 0.6f;

        /// <summary>Pieces of wall are one wall when their directions differ by less than this (degrees)...</summary>
        public const float MergeDegrees = 10f;

        /// <summary>...they lie this close (meters) to one line...</summary>
        public const float MergeDistance = 0.15f;

        /// <summary>...and the gap between them is smaller than this (meters).</summary>
        public const float MergeGap = 0.5f;

        /// <summary>Walls that are this close (degrees) to a direction of the room are laid along it.</summary>
        public const float SnapDegrees = 10f;

        /// <summary>Walls at a right angle whose ends are this close (meters) meet in a corner.</summary>
        public const float CornerDistance = 0.4f;

        /// <summary>The floor is not drawn this close (meters) to a wall.</summary>
        public const float HalfThickness = 0.05f;

        /// <summary>Walls are drawn up to this far (meters) above the floor.</summary>
        public const float MaxDrawnHeight = 2.4f;

        /// <summary>
        /// A wall that depth saw stays for this many redraws when it is not found again: what stands
        /// in the room is sorted into boxes anew at every redraw, and not always the same way.
        /// </summary>
        public const int KeepRedraws = 6;

        /// <summary>Floor that depth never saw is not drawn up to this far (meters) behind a wall.</summary>
        public const float ShadowDepth = 3f;

        /// <summary>
        /// When this share of the camera positions lies on the other side of a wall, the user was on
        /// both sides (the wall stands between two rooms that were scanned): nothing is behind it.
        /// </summary>
        public const float BothSidesShare = 0.2f;

        const int MaxCameras = 256;
        const float CameraStep = 0.25f;
        const float CameraReach = 2f;
        const float CameraClearance = 0.2f;

        const float PlaneConfidence = 1f;

        readonly List<WallSegment> planes = new List<WallSegment>();
        readonly List<WallSegment> obstacles = new List<WallSegment>();
        readonly List<WallSegment> segments = new List<WallSegment>();
        readonly List<WallSegment> before = new List<WallSegment>();
        readonly List<WallSegment> reading = new List<WallSegment>();
        readonly List<Vector2> cameras = new List<Vector2>();
        readonly List<int> missed = new List<int>();
        readonly List<WallSegment> found = new List<WallSegment>();
        int nextCamera;
        bool isReading;

        /// <summary>The walls, after <see cref="Rebuild"/>.</summary>
        public IReadOnlyList<WallSegment> Segments => segments;

        /// <summary>The walls changed at the last <see cref="Rebuild"/>.</summary>
        public bool Changed { get; private set; }

        public void Clear()
        {
            Changed = segments.Count > 0;
            planes.Clear();
            obstacles.Clear();
            missed.Clear();
            segments.Clear();
            reading.Clear();
            cameras.Clear();
            nextCamera = 0;
            isReading = false;
        }

        /// <summary>Where the camera was when a frame was taken (x and z): the room is where the user walks.</summary>
        public void NoteCamera(Vector2 position)
        {
            for (var i = 0; i < cameras.Count; i++)
            {
                if ((cameras[i] - position).sqrMagnitude < CameraStep * CameraStep)
                {
                    return;
                }
            }

            if (cameras.Count < MaxCameras)
            {
                cameras.Add(position);
                return;
            }

            cameras[nextCamera] = position;
            nextCamera = (nextCamera + 1) % MaxCameras;
        }

        /// <summary>
        /// The side of a wall the room lies on: +1 for the side its normal (the direction turned a
        /// quarter to the left) points to, -1 for the other. False when the camera was on both sides,
        /// or never near the wall.
        /// </summary>
        public bool TryGetInside(in WallSegment wall, out int side)
        {
            side = 0;
            var along = wall.Direction;
            var normal = new Vector2(-along.y, along.x);
            var length = wall.Length;
            var ahead = 0;
            var behind = 0;
            for (var i = 0; i < cameras.Count; i++)
            {
                var from = cameras[i] - wall.A;
                var on = Vector2.Dot(from, along);
                var off = Vector2.Dot(from, normal);
                if (on < -CameraReach || on > length + CameraReach || Mathf.Abs(off) < CameraClearance)
                {
                    continue;
                }

                if (off > 0f)
                {
                    ahead++;
                }
                else
                {
                    behind++;
                }
            }

            var total = ahead + behind;
            if (total == 0 || Mathf.Min(ahead, behind) > BothSidesShare * total)
            {
                return false;
            }

            side = ahead >= behind ? 1 : -1;
            return true;
        }

        /// <summary>Starts a new reading of the vertical planes ARCore detected.</summary>
        public void BeginPlanes()
        {
            reading.Clear();
            isReading = true;
        }

        /// <summary>A vertical plane, as the corners of its outline in the space of the scan.</summary>
        public void AddPlane(IReadOnlyList<Vector3> outline, Vector3 normal)
        {
            if (outline == null || outline.Count < 3)
            {
                return;
            }

            var along = new Vector2(-normal.z, normal.x);
            if (along.sqrMagnitude < 1e-6f)
            {
                return;
            }

            along.Normalize();
            var across = new Vector2(-along.y, along.x);
            var min = float.MaxValue;
            var max = float.MinValue;
            var bottom = float.MaxValue;
            var top = float.MinValue;
            var offset = 0f;
            for (var i = 0; i < outline.Count; i++)
            {
                var point = new Vector2(outline[i].x, outline[i].z);
                var on = Vector2.Dot(point, along);
                min = Mathf.Min(min, on);
                max = Mathf.Max(max, on);
                offset += Vector2.Dot(point, across);
                bottom = Mathf.Min(bottom, outline[i].y);
                top = Mathf.Max(top, outline[i].y);
            }

            offset /= outline.Count;
            var wall = new WallSegment(along * min + across * offset, along * max + across * offset, bottom, top,
                PlaneConfidence, true);
            (isReading ? reading : planes).Add(wall);
        }

        public void EndPlanes()
        {
            if (!isReading)
            {
                return;
            }

            isReading = false;
            planes.Clear();
            planes.AddRange(reading);
            reading.Clear();
        }

        /// <summary>
        /// The obstacles that are tall, long and thin are walls, too. A wall that was found before and
        /// is missing now stays for a few redraws.
        /// </summary>
        public void SetObstacleWalls(IReadOnlyList<ObstacleVolume> volumes)
        {
            found.Clear();
            for (var i = 0; i < volumes.Count; i++)
            {
                var volume = volumes[i];
                if (volume.Kind != ObstacleKind.Wall)
                {
                    continue;
                }

                var alongX = volume.Size.x >= volume.Size.z;
                var half = volume.Pose.rotation * (alongX ? Vector3.right : Vector3.forward)
                           * ((alongX ? volume.Size.x : volume.Size.z) * 0.5f);
                var center = volume.Pose.position;
                found.Add(new WallSegment(new Vector2(center.x - half.x, center.z - half.z),
                    new Vector2(center.x + half.x, center.z + half.z), center.y, volume.Top, volume.Confidence, false));
            }

            // Walls that were not found again grow older; those that were, are replaced by what was found.
            for (var i = obstacles.Count - 1; i >= 0; i--)
            {
                var again = false;
                for (var k = 0; k < found.Count && !again; k++)
                {
                    again = TryMerge(obstacles[i], found[k], out _);
                }

                if (again || ++missed[i] > KeepRedraws)
                {
                    obstacles.RemoveAt(i);
                    missed.RemoveAt(i);
                }
            }

            for (var k = 0; k < found.Count; k++)
            {
                obstacles.Add(found[k]);
                missed.Add(0);
            }
        }

        /// <summary>Forgets the walls depth saw at once (the scan no longer looks for obstacles).</summary>
        public void ClearObstacleWalls()
        {
            obstacles.Clear();
            missed.Clear();
        }

        /// <summary>Makes the walls from what was found. True when they changed.</summary>
        /// <param name="floorHeight">Walls stand on the floor.</param>
        /// <param name="roomYaw">Direction of the room in degrees, or NaN when it is not known.</param>
        public bool Rebuild(float floorHeight, float roomYaw)
        {
            before.Clear();
            before.AddRange(segments);
            segments.Clear();
            for (var i = 0; i < planes.Count; i++)
            {
                var plane = planes[i];
                var tall = plane.Top - plane.Bottom >= MinPlaneHeight || plane.Top >= floorHeight + MinTopAboveFloor;
                if (tall && plane.Length >= MinLength && plane.Top > floorHeight)
                {
                    segments.Add(OnTheFloor(plane, floorHeight));
                }
            }

            for (var i = 0; i < obstacles.Count; i++)
            {
                if (obstacles[i].Length >= MinLength)
                {
                    segments.Add(OnTheFloor(obstacles[i], floorHeight));
                }
            }

            Merge();
            if (!float.IsNaN(roomYaw))
            {
                Snap(roomYaw);
                Merge();
            }

            CloseCorners();
            Changed = !Same(before, segments);
            return Changed;
        }

        /// <summary>A wall stands on the cell, or right beside it: no floor is drawn there.</summary>
        public bool Blocks(Vector2Int cell)
        {
            var center = new Vector2((cell.x + 0.5f) * RoomSurfaceMap.CellSize, (cell.y + 0.5f) * RoomSurfaceMap.CellSize);
            for (var i = 0; i < segments.Count; i++)
            {
                if (segments[i].DistanceTo(center) <= HalfThickness)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Marks the cells of a floor grid that a wall stands on, and the cells behind the wall that
        /// depth never saw.
        /// </summary>
        public void MarkBlocked(SurfaceGrid floorGrid)
        {
            for (var i = 0; i < segments.Count; i++)
            {
                if (TryGetInside(segments[i], out var side))
                {
                    MarkBehind(floorGrid, segments[i], -side);
                }

                var ray = new GridRay();
                ray.Start(segments[i].A, segments[i].B);
                while (ray.Next(out var cell, out _, out _))
                {
                    // The cells the line goes through, and their neighbors where the line passes close by.
                    for (var dz = -1; dz <= 1; dz++)
                    {
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            var x = cell.x + dx - floorGrid.OriginX;
                            var z = cell.y + dz - floorGrid.OriginZ;
                            if (floorGrid.Contains(x, z)
                                && segments[i].DistanceTo(CenterOf(cell.x + dx, cell.y + dz)) <= HalfThickness)
                            {
                                var index = floorGrid.IndexOf(x, z);
                                floorGrid.SetBlocked(index, true);
                                floorGrid.SetWall(index, true);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>The cells behind a wall (on the side the normal times <paramref name="side"/> points to) that depth did not confirm.</summary>
        static void MarkBehind(SurfaceGrid floorGrid, in WallSegment wall, int side)
        {
            var along = wall.Direction;
            var normal = new Vector2(-along.y, along.x) * side;
            var length = wall.Length;
            var far = normal * ShadowDepth;
            var minX = Mathf.Min(Mathf.Min(wall.A.x, wall.B.x), Mathf.Min(wall.A.x + far.x, wall.B.x + far.x));
            var maxX = Mathf.Max(Mathf.Max(wall.A.x, wall.B.x), Mathf.Max(wall.A.x + far.x, wall.B.x + far.x));
            var minZ = Mathf.Min(Mathf.Min(wall.A.y, wall.B.y), Mathf.Min(wall.A.y + far.y, wall.B.y + far.y));
            var maxZ = Mathf.Max(Mathf.Max(wall.A.y, wall.B.y), Mathf.Max(wall.A.y + far.y, wall.B.y + far.y));
            var first = RoomSurfaceMap.CellOf(minX, minZ);
            var last = RoomSurfaceMap.CellOf(maxX, maxZ);
            for (var cellZ = first.y; cellZ <= last.y; cellZ++)
            {
                for (var cellX = first.x; cellX <= last.x; cellX++)
                {
                    var x = cellX - floorGrid.OriginX;
                    var z = cellZ - floorGrid.OriginZ;
                    if (!floorGrid.Contains(x, z))
                    {
                        continue;
                    }

                    var from = CenterOf(cellX, cellZ) - wall.A;
                    var on = Vector2.Dot(from, along);
                    var off = Vector2.Dot(from, normal);
                    var index = floorGrid.IndexOf(x, z);
                    if (on >= 0f && on <= length && off > 0f && off <= ShadowDepth
                        && floorGrid.LogOddsAt(index) < SurfaceEvidence.ConfirmedLogOdds)
                    {
                        floorGrid.SetBlocked(index, true);
                        floorGrid.SetWall(index, true);
                    }
                }
            }
        }

        /// <summary>Walls say more about the direction of the room than the edges of tables.</summary>
        public void AddEdgesTo(RoomAxes axes, float weight)
        {
            for (var i = 0; i < planes.Count; i++)
            {
                axes.AddEdge(planes[i].A, planes[i].B, weight);
            }

            for (var i = 0; i < obstacles.Count; i++)
            {
                axes.AddEdge(obstacles[i].A, obstacles[i].B, weight);
            }
        }

        static Vector2 CenterOf(int cellX, int cellZ)
        {
            return new Vector2((cellX + 0.5f) * RoomSurfaceMap.CellSize, (cellZ + 0.5f) * RoomSurfaceMap.CellSize);
        }

        static WallSegment OnTheFloor(WallSegment wall, float floorHeight)
        {
            return new WallSegment(wall.A, wall.B, floorHeight, wall.Top, wall.Confidence, wall.FromPlane);
        }

        /// <summary>Pieces on one line, with no large gap between them, are one wall.</summary>
        void Merge()
        {
            var merged = true;
            while (merged)
            {
                merged = false;
                for (var i = 0; i < segments.Count && !merged; i++)
                {
                    for (var j = i + 1; j < segments.Count && !merged; j++)
                    {
                        if (!TryMerge(segments[i], segments[j], out var wall))
                        {
                            continue;
                        }

                        segments[i] = wall;
                        segments.RemoveAt(j);
                        merged = true;
                    }
                }
            }
        }

        static bool TryMerge(WallSegment a, WallSegment b, out WallSegment wall)
        {
            wall = a;
            var directionA = a.Direction;
            var directionB = b.Direction;
            if (Vector2.Dot(directionA, directionB) < 0f)
            {
                directionB = -directionB;
            }

            if (Vector2.Angle(directionA, directionB) > MergeDegrees)
            {
                return false;
            }

            // The line of both: through their middle, along their mean direction, the longer one counting more.
            var lengthA = a.Length;
            var lengthB = b.Length;
            var total = lengthA + lengthB;
            var along = (directionA * lengthA + directionB * lengthB).normalized;
            var across = new Vector2(-along.y, along.x);
            var middle = ((a.A + a.B) * 0.5f * lengthA + (b.A + b.B) * 0.5f * lengthB) / total;
            var offsetA = Vector2.Dot((a.A + a.B) * 0.5f - middle, across);
            var offsetB = Vector2.Dot((b.A + b.B) * 0.5f - middle, across);
            if (Mathf.Abs(offsetA - offsetB) > MergeDistance)
            {
                return false;
            }

            Span(a, middle, along, out var minA, out var maxA);
            Span(b, middle, along, out var minB, out var maxB);
            if (Mathf.Max(minA, minB) - Mathf.Min(maxA, maxB) > MergeGap)
            {
                return false;
            }

            var min = Mathf.Min(minA, minB);
            var max = Mathf.Max(maxA, maxB);
            wall = new WallSegment(middle + along * min, middle + along * max, Mathf.Min(a.Bottom, b.Bottom),
                Mathf.Max(a.Top, b.Top), Mathf.Max(a.Confidence, b.Confidence), a.FromPlane || b.FromPlane);
            return true;
        }

        static void Span(WallSegment wall, Vector2 origin, Vector2 along, out float min, out float max)
        {
            var first = Vector2.Dot(wall.A - origin, along);
            var second = Vector2.Dot(wall.B - origin, along);
            min = Mathf.Min(first, second);
            max = Mathf.Max(first, second);
        }

        /// <summary>Walls that run nearly along the room, or nearly at a right angle to it, are turned to do so.</summary>
        void Snap(float roomYaw)
        {
            MinAreaRectangle.AxesOf(roomYaw, out var axisX, out var axisZ);
            for (var i = 0; i < segments.Count; i++)
            {
                var wall = segments[i];
                var turn = RoomAxes.Difference(RoomAxes.YawOf(wall.B - wall.A), roomYaw);
                if (Mathf.Abs(turn) > SnapDegrees)
                {
                    continue;
                }

                var direction = wall.Direction;
                var axis = Mathf.Abs(Vector2.Dot(direction, axisZ)) >= Mathf.Abs(Vector2.Dot(direction, axisX))
                    ? axisZ
                    : axisX;
                if (Vector2.Dot(direction, axis) < 0f)
                {
                    axis = -axis;
                }

                var middle = (wall.A + wall.B) * 0.5f;
                var half = wall.Length * 0.5f;
                segments[i] = new WallSegment(middle - axis * half, middle + axis * half, wall.Bottom, wall.Top,
                    wall.Confidence, wall.FromPlane);
            }
        }

        /// <summary>Two walls at an angle whose ends nearly meet, meet: a room has corners, not gaps.</summary>
        void CloseCorners()
        {
            for (var i = 0; i < segments.Count; i++)
            {
                for (var j = i + 1; j < segments.Count; j++)
                {
                    var a = segments[i];
                    var b = segments[j];
                    if (Mathf.Abs(Vector2.Dot(a.Direction, b.Direction)) > 0.5f
                        || !PolygonMath.Intersect(a.A, a.Direction, b.A, b.Direction, out var corner))
                    {
                        continue;
                    }

                    if (!TryExtend(ref a, corner) || !TryExtend(ref b, corner))
                    {
                        continue;
                    }

                    segments[i] = a;
                    segments[j] = b;
                }
            }
        }

        /// <summary>Moves the end of the wall that is near the corner onto it. False when no end is near.</summary>
        static bool TryExtend(ref WallSegment wall, Vector2 corner)
        {
            var fromA = Vector2.Distance(wall.A, corner);
            var fromB = Vector2.Distance(wall.B, corner);
            if (Mathf.Min(fromA, fromB) > CornerDistance)
            {
                return false;
            }

            wall = fromA <= fromB
                ? new WallSegment(corner, wall.B, wall.Bottom, wall.Top, wall.Confidence, wall.FromPlane)
                : new WallSegment(wall.A, corner, wall.Bottom, wall.Top, wall.Confidence, wall.FromPlane);
            return true;
        }

        static bool Same(List<WallSegment> a, List<WallSegment> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }

            for (var i = 0; i < a.Count; i++)
            {
                if ((a[i].A - b[i].A).sqrMagnitude > 1e-6f || (a[i].B - b[i].B).sqrMagnitude > 1e-6f
                    || Mathf.Abs(a[i].Top - b[i].Top) > 1e-3f || Mathf.Abs(a[i].Bottom - b[i].Bottom) > 1e-3f)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
