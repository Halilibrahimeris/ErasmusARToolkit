using System;
using System.Collections.Generic;
using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>How <see cref="SurfaceMeshBuilder.BuildClean(SurfaceGrid, in SurfaceBuildSettings, SurfaceMeshData)"/> draws a surface.</summary>
    public readonly struct SurfaceBuildSettings
    {
        public SurfaceBuildSettings(bool isFloor, float roomYaw, bool hasRoomYaw = true, bool settle = false)
        {
            IsFloor = isFloor;
            RoomYaw = roomYaw;
            HasRoomYaw = hasRoomYaw;
            Settle = settle;
        }

        /// <summary>The floor keeps the shape it has; a raised surface that is nearly a rectangle is drawn as one.</summary>
        public bool IsFloor { get; }

        /// <summary>Direction of the walls of the room, in degrees around the vertical axis.</summary>
        public float RoomYaw { get; }

        /// <summary>False while the direction of the room is not known: edges are made straight, but not turned.</summary>
        public bool HasRoomYaw { get; }

        /// <summary>
        /// The scan is finished: outlines are drawn as they are found now (while scanning they wait
        /// until a change has lasted, so they hold still).
        /// </summary>
        public bool Settle { get; }

        /// <summary>Points closer than this (meters) to the outline are left out of it.</summary>
        public float Tolerance => IsFloor ? SurfaceMeshBuilder.FloorTolerance : SurfaceMeshBuilder.RaisedTolerance;
    }

    /// <summary>The outlines of one surface, kept from redraw to redraw so they hold still.</summary>
    internal sealed class SurfaceShape
    {
        /// <summary>The outlines as they were scanned: straightened, not yet laid along the room.</summary>
        public readonly List<List<Vector2>> Scanned = new List<List<Vector2>>();

        /// <summary>The outlines as they are drawn.</summary>
        public readonly List<List<Vector2>> Loops = new List<List<Vector2>>();

        /// <summary>Redraws in a row at which the outlines were found a little different from the drawn ones.</summary>
        public int Pending { get; set; }

        /// <summary>The direction of the room the drawn outlines were laid along (NaN: none).</summary>
        public float Yaw { get; set; } = float.NaN;

        /// <summary>Corners of all drawn outlines together.</summary>
        public int CornerCount
        {
            get
            {
                var count = 0;
                for (var i = 0; i < Loops.Count; i++)
                {
                    count += Loops[i].Count;
                }

                return count;
            }
        }

        public void Clear()
        {
            Scanned.Clear();
            Loops.Clear();
            Pending = 0;
            Yaw = float.NaN;
        }
    }

    public sealed partial class SurfaceMeshBuilder
    {
        /// <summary>Points of the outline of the floor closer than this (meters) to a straight edge are left out.</summary>
        public const float FloorTolerance = 0.04f;

        public const float RaisedTolerance = 0.03f;

        /// <summary>A raised surface that fills this share of the rectangle around it is drawn as that rectangle.</summary>
        public const float RectangleFill = 0.85f;

        /// <summary>
        /// An outline that lies less than this (meters) from the one that is drawn is the same outline:
        /// one cell is what the edge of a scanned surface moves from redraw to redraw by itself.
        /// </summary>
        public const float RedrawDistance = 0.05f;

        /// <summary>An outline that lies farther than this (meters) from the drawn one is drawn at once: the surface grew.</summary>
        public const float JumpDistance = 0.15f;

        /// <summary>
        /// A smaller change is drawn when it was found this many redraws in a row: it has lasted, so it
        /// is not noise.
        /// </summary>
        public const int PendingRedraws = 3;

        /// <summary>Outlines are laid anew when the direction of the room changed by this much (degrees).</summary>
        public const float RedrawDegrees = 1f;

        const float MaxMiter = 3f;

        readonly ContourTracer tracer = new ContourTracer();
        readonly PolygonSimplifier simplifier = new PolygonSimplifier();
        readonly PolygonSnapper snapper = new PolygonSnapper();
        readonly PolygonFill polygonFill = new PolygonFill();
        readonly SurfaceShape ownShape = new SurfaceShape();
        readonly List<List<Vector2>> traced = new List<List<Vector2>>();
        readonly List<List<Vector2>> candidates = new List<List<Vector2>>();
        readonly List<Vector2> simplified = new List<Vector2>();
        readonly List<Vector2> snapped = new List<Vector2>();
        readonly List<float> crossings = new List<float>();
        bool[] cleanMask = Array.Empty<bool>();

        /// <summary>
        /// Draws the shown cells of a grid as clean shapes instead of as the cells themselves: outlines
        /// with straight edges, laid along the walls of the room, and tables as rectangles. The mesh lies
        /// at height 0. The shown cells of the grid become the cells inside the outlines, so what is
        /// drawn is what placement hits.
        /// </summary>
        public void BuildClean(SurfaceGrid grid, in SurfaceBuildSettings settings, SurfaceMeshData output)
        {
            ownShape.Clear();
            BuildClean(grid, settings, ownShape, output);
        }

        /// <param name="shape">The outlines of this surface from the last redraw; they are kept when little changed.</param>
        internal void BuildClean(SurfaceGrid grid, in SurfaceBuildSettings settings, SurfaceShape shape,
            SurfaceMeshData output)
        {
            output.Clear();
            var count = grid.CellCount;
            if (cleanMask.Length < count)
            {
                cleanMask = new bool[count];
            }

            for (var i = 0; i < count; i++)
            {
                cleanMask[i] = grid.IsShown(i);
            }

            tracer.Trace(cleanMask, grid.Width, grid.Depth, grid.OriginX, grid.OriginZ, traced);
            FindOutlines(settings, shape);
            polygonFill.Trapezoids(shape.Loops, settings.HasRoomYaw ? settings.RoomYaw : 0f, output.FillVertices,
                output.FillTriangles);
            for (var i = 0; i < shape.Loops.Count; i++)
            {
                AddOutline(shape.Loops[i], output);
            }

            ShowCellsInside(grid, shape.Loops);
        }

        void FindOutlines(in SurfaceBuildSettings settings, SurfaceShape shape)
        {
            shape.Scanned.Clear();
            candidates.Clear();
            var yaw = settings.HasRoomYaw ? settings.RoomYaw : 0f;
            for (var i = 0; i < traced.Count; i++)
            {
                simplifier.Simplify(traced[i], settings.Tolerance, simplified);
                if (simplified.Count < 3)
                {
                    continue;
                }

                var scanned = new List<Vector2>(simplified);
                shape.Scanned.Add(scanned);
                candidates.Add(Tidy(scanned, settings, yaw));
            }

            if (!FitTogether(candidates))
            {
                // Tidying made outlines overlap: the scanned ones never do.
                candidates.Clear();
                candidates.AddRange(shape.Scanned);
            }

            var laidAlong = settings.HasRoomYaw ? settings.RoomYaw : float.NaN;
            var roomTurned = float.IsNaN(laidAlong) != float.IsNaN(shape.Yaw)
                             || (!float.IsNaN(laidAlong)
                                 && Mathf.Abs(RoomAxes.Difference(laidAlong, shape.Yaw)) > RedrawDegrees);
            if (roomTurned || !Keeps(candidates, shape, settings.Settle))
            {
                shape.Loops.Clear();
                shape.Loops.AddRange(candidates);
                shape.Pending = 0;
                shape.Yaw = laidAlong;
            }
        }

        /// <summary>
        /// The drawn outlines stay when the new ones are the same, give or take a cell, or differ a
        /// little for the first time: the edges of scanned surfaces move with every redraw, and what is
        /// drawn should hold still. A change that lasts, or a large one, is drawn.
        /// </summary>
        static bool Keeps(List<List<Vector2>> outlines, SurfaceShape shape, bool settle)
        {
            if (shape.Loops.Count == 0 || outlines.Count == 0)
            {
                return false;
            }

            if (IsSame(outlines, shape.Loops, RedrawDistance))
            {
                // Of two outlines that are the same, the tidier one is drawn: the rectangle of a desk
                // takes the place of a ragged outline, and never the other way round.
                shape.Pending = 0;
                return CountCorners(outlines) >= shape.CornerCount;
            }

            if (settle || !IsSame(outlines, shape.Loops, JumpDistance))
            {
                return false;
            }

            return ++shape.Pending < PendingRedraws;
        }

        static int CountCorners(List<List<Vector2>> outlines)
        {
            var count = 0;
            for (var i = 0; i < outlines.Count; i++)
            {
                count += outlines[i].Count;
            }

            return count;
        }

        /// <summary>The outline laid along the room; a raised surface that is nearly a rectangle, as that rectangle.</summary>
        List<Vector2> Tidy(List<Vector2> scanned, in SurfaceBuildSettings settings, float yaw)
        {
            var outline = scanned;
            if (settings.HasRoomYaw && snapper.Snap(scanned, yaw, snapped))
            {
                outline = new List<Vector2>(snapped);
            }

            var area = PolygonMath.SignedArea(outline);
            if (settings.IsFloor || area <= 0f
                || !MinAreaRectangle.Fit(outline, yaw, out var center, out var size, out var rectangleYaw)
                || area < RectangleFill * size.x * size.y)
            {
                return outline;
            }

            MinAreaRectangle.AxesOf(rectangleYaw, out var axisX, out var axisZ);
            var halfX = axisX * (size.x * 0.5f);
            var halfZ = axisZ * (size.y * 0.5f);

            // Counter-clockwise, as outer outlines run. Which way that is depends on the axes.
            var rectangle = new List<Vector2>
            {
                center - halfX - halfZ, center + halfX - halfZ, center + halfX + halfZ, center - halfX + halfZ,
            };
            if (PolygonMath.SignedArea(rectangle) < 0f)
            {
                rectangle.Reverse();
            }

            return rectangle;
        }

        /// <summary>
        /// Holes lie inside of outer outlines, and outer outlines beside each other. After tidying, a
        /// hole that reaches out of its outline is dropped; outer outlines that overlap are refused.
        /// </summary>
        static bool FitTogether(List<List<Vector2>> outlines)
        {
            for (var i = outlines.Count - 1; i >= 0; i--)
            {
                var isHole = PolygonMath.SignedArea(outlines[i]) < 0f;
                var inside = 0;
                for (var j = 0; j < outlines.Count && inside < outlines[i].Count; j++)
                {
                    if (j == i || PolygonMath.SignedArea(outlines[j]) < 0f)
                    {
                        continue;
                    }

                    inside = CountInside(outlines[i], outlines[j]);
                    if (inside > 0)
                    {
                        break;
                    }
                }

                if (isHole && inside < outlines[i].Count)
                {
                    outlines.RemoveAt(i);
                }
                else if (!isHole && inside > 0 && inside < outlines[i].Count)
                {
                    return false;
                }
            }

            return true;
        }

        static int CountInside(List<Vector2> points, List<Vector2> outline)
        {
            var count = 0;
            for (var i = 0; i < points.Count; i++)
            {
                if (Contains(outline, points[i]))
                {
                    count++;
                }
            }

            return count;
        }

        static bool Contains(List<Vector2> outline, Vector2 point)
        {
            var inside = false;
            for (int i = 0, j = outline.Count - 1; i < outline.Count; j = i++)
            {
                var a = outline[i];
                var b = outline[j];
                if ((a.y > point.y) != (b.y > point.y) && point.x < a.x + (point.y - a.y) * (b.x - a.x) / (b.y - a.y))
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        /// <summary>
        /// Outlines are the same when every corner of each lies near the other one, however many corners
        /// they have: the rectangle of a desk is the same desk as the ragged outline the scan finds
        /// for once.
        /// </summary>
        static bool IsSame(List<List<Vector2>> outlines, List<List<Vector2>> before, float distance)
        {
            if (outlines.Count != before.Count || outlines.Count == 0)
            {
                return false;
            }

            for (var i = 0; i < outlines.Count; i++)
            {
                if (before[i].Count < 3 || PolygonMath.SignedArea(outlines[i]) * PolygonMath.SignedArea(before[i]) <= 0f
                    || !LiesNear(outlines[i], before[i], distance) || !LiesNear(before[i], outlines[i], distance))
                {
                    return false;
                }
            }

            return true;
        }

        static bool LiesNear(List<Vector2> corners, List<Vector2> outline, float distance)
        {
            for (var i = 0; i < corners.Count; i++)
            {
                if (PolygonMath.DistanceToOutline(corners[i], outline) > distance)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>A ribbon along a closed outline, with corners that meet in a point.</summary>
        void AddOutline(List<Vector2> loop, SurfaceMeshData output)
        {
            var count = loop.Count;
            if (count < 3)
            {
                return;
            }

            var half = OutlineWidth * 0.5f;
            var start = output.OutlineVertices.Count;
            for (var i = 0; i < count; i++)
            {
                var previous = loop[(i + count - 1) % count];
                var here = loop[i];
                var next = loop[(i + 1) % count];
                var incoming = (here - previous).normalized;
                var outgoing = (next - here).normalized;
                var normalIn = new Vector2(-incoming.y, incoming.x);
                var normalOut = new Vector2(-outgoing.y, outgoing.x);
                var miter = normalIn + normalOut;
                var length = miter.magnitude;
                var offset = length < 1e-4f
                    ? normalIn * half
                    : miter / length * Mathf.Min(half / Mathf.Max(Vector2.Dot(miter / length, normalIn), 1f / MaxMiter),
                        half * MaxMiter);
                output.OutlineVertices.Add(new Vector3(here.x - offset.x, 0f, here.y - offset.y));
                output.OutlineVertices.Add(new Vector3(here.x + offset.x, 0f, here.y + offset.y));
                output.OutlineLength += Vector2.Distance(here, next);
            }

            for (var i = 0; i < count; i++)
            {
                var a = start + i * 2;
                var b = start + (i + 1) % count * 2;
                output.OutlineTriangles.Add(a);
                output.OutlineTriangles.Add(a + 1);
                output.OutlineTriangles.Add(b + 1);
                output.OutlineTriangles.Add(a);
                output.OutlineTriangles.Add(b + 1);
                output.OutlineTriangles.Add(b);
            }
        }

        /// <summary>
        /// What is drawn is what placement hits: the cells inside the outlines are shown, except where
        /// the camera looked through the surface or something stands on it.
        /// </summary>
        void ShowCellsInside(SurfaceGrid grid, List<List<Vector2>> loops)
        {
            for (var i = 0; i < grid.CellCount; i++)
            {
                grid.SetShown(i, false);
            }

            var sink = new ShownCells(grid);
            PolygonRaster.FillRows(loops, RoomSurfaceMap.CellSize, ref sink, crossings);
        }

        struct ShownCells : IRowSink
        {
            readonly SurfaceGrid grid;

            public ShownCells(SurfaceGrid surfaceGrid)
            {
                grid = surfaceGrid;
            }

            public void AddRun(int row, int first, int last)
            {
                var z = row - grid.OriginZ;
                if (z < 0 || z >= grid.Depth)
                {
                    return;
                }

                var from = Mathf.Max(first - grid.OriginX, 0);
                var to = Mathf.Min(last - grid.OriginX, grid.Width - 1);
                for (var x = from; x <= to; x++)
                {
                    var index = grid.IndexOf(x, z);
                    if (!grid.IsBlocked(index) && grid.StateAt(index) != CellState.Carved)
                    {
                        grid.SetShown(index, true);
                    }
                }
            }
        }
    }
}
