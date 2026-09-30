using System.Collections.Generic;
using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// Lays the edges of an outline along the walls of the room. An edge that runs nearly along the
    /// direction of the room, or nearly at a right angle to it, is turned to run exactly so; edges that
    /// follow each other on nearly one line become one edge; and the corners are where the edges meet.
    /// A scanned table gets straight sides and square corners that way. Edges that run another way (a
    /// round table, a slanted wall) stay as they are. When the result is far from what was scanned, the
    /// outline is left alone.
    /// </summary>
    internal sealed class PolygonSnapper
    {
        /// <summary>Edges that are this close (degrees) to a direction of the room are turned to it.</summary>
        public const float SnapDegrees = 10f;

        /// <summary>Parallel edges that lie closer than this (meters) become one edge.</summary>
        public const float MergeDistance = 0.05f;

        /// <summary>Shorter edges (meters) are left out.</summary>
        public const float MinEdge = 0.05f;

        /// <summary>
        /// An edge shorter than this (meters) between two edges that lie nearly on one line is a dent of
        /// the scan, not a corner of the surface.
        /// </summary>
        public const float MaxDent = 0.1f;

        /// <summary>The outline is left alone when its area would change by more than this share...</summary>
        public const float MaxAreaChange = 0.15f;

        /// <summary>...or a corner would end up farther than this (meters) from the scanned outline.</summary>
        public const float MaxMove = 0.1f;

        readonly List<Line> lines = new List<Line>();
        readonly List<Vector2> corners = new List<Vector2>();

        /// <summary>False when the outline is better left as it is (<paramref name="output"/> is empty then).</summary>
        /// <param name="yaw">Direction of the walls of the room, in degrees.</param>
        public bool Snap(IReadOnlyList<Vector2> loop, float yaw, List<Vector2> output)
        {
            output.Clear();
            if (loop.Count < 3)
            {
                return false;
            }

            MinAreaRectangle.AxesOf(yaw, out var axisX, out var axisZ);
            lines.Clear();
            var snapped = 0;
            for (var i = 0; i < loop.Count; i++)
            {
                var from = loop[i];
                var to = loop[(i + 1) % loop.Count];
                var edge = to - from;
                var length = edge.magnitude;
                if (length < 1e-5f)
                {
                    continue;
                }

                var line = new Line { Point = (from + to) * 0.5f, Direction = edge / length, Length = length, Axis = -1 };
                var turn = RoomAxes.Difference(RoomAxes.YawOf(edge), yaw);
                if (Mathf.Abs(turn) <= SnapDegrees)
                {
                    // Along the axis of the room that is nearest, pointing the way the edge does.
                    var alongZ = Mathf.Abs(Vector2.Dot(line.Direction, axisZ)) >= Mathf.Abs(Vector2.Dot(line.Direction, axisX));
                    var axis = alongZ ? axisZ : axisX;
                    line.Direction = Vector2.Dot(line.Direction, axis) >= 0f ? axis : -axis;
                    line.Axis = alongZ ? 1 : 0;
                    snapped++;
                }

                lines.Add(line);
            }

            if (snapped == 0 || lines.Count < 3)
            {
                return false;
            }

            DropDents();
            MergeParallel();
            if (lines.Count < 3 || !FindCorners(loop) || !DropShortEdges())
            {
                return false;
            }

            if (!IsCloseTo(loop))
            {
                return false;
            }

            output.AddRange(corners);
            return true;
        }

        /// <summary>
        /// A short edge between two edges that point the same way and lie nearly on one line is left
        /// out, whatever its direction: the two become one edge afterwards.
        /// </summary>
        void DropDents()
        {
            var dropped = true;
            while (dropped && lines.Count > 3)
            {
                dropped = false;
                for (var i = 0; i < lines.Count; i++)
                {
                    var before = lines[(i + lines.Count - 1) % lines.Count];
                    var after = lines[(i + 1) % lines.Count];
                    if (lines[i].Length >= MaxDent || !AreOnOneLine(before, after))
                    {
                        continue;
                    }

                    lines.RemoveAt(i);
                    dropped = true;
                    break;
                }
            }
        }

        static bool AreOnOneLine(Line a, Line b)
        {
            if (a.Axis < 0 || a.Axis != b.Axis || Vector2.Dot(a.Direction, b.Direction) < 0f)
            {
                return false;
            }

            var normal = new Vector2(-a.Direction.y, a.Direction.x);
            return Mathf.Abs(Vector2.Dot(b.Point - a.Point, normal)) <= MergeDistance;
        }

        /// <summary>
        /// Edges that follow each other, point the same way along the same axis and lie nearly on one
        /// line are one edge: the scan broke a straight side in pieces.
        /// </summary>
        void MergeParallel()
        {
            var merged = true;
            while (merged && lines.Count > 3)
            {
                merged = false;
                for (var i = 0; i < lines.Count && lines.Count > 3; i++)
                {
                    var next = (i + 1) % lines.Count;
                    var a = lines[i];
                    var b = lines[next];
                    if (!AreOnOneLine(a, b))
                    {
                        continue;
                    }

                    var length = a.Length + b.Length;
                    a.Point = (a.Point * a.Length + b.Point * b.Length) / length;
                    a.Length = length;
                    lines[i] = a;
                    lines.RemoveAt(next);
                    merged = true;
                    break;
                }
            }
        }

        /// <summary>The corners are where edges that follow each other cross.</summary>
        bool FindCorners(IReadOnlyList<Vector2> loop)
        {
            corners.Clear();
            for (var i = 0; i < lines.Count; i++)
            {
                var previous = lines[(i + lines.Count - 1) % lines.Count];
                var line = lines[i];
                if (PolygonMath.Intersect(previous.Point, previous.Direction, line.Point, line.Direction, out var corner)
                    && Mathf.Abs(Vector2.Dot(previous.Direction, line.Direction)) < 0.985f)
                {
                    corners.Add(corner);
                    continue;
                }

                // Parallel edges, a step apart: a short edge at a right angle joins them, halfway.
                var middle = (previous.Point + previous.Direction * (previous.Length * 0.5f)
                              + line.Point - line.Direction * (line.Length * 0.5f)) * 0.5f;
                corners.Add(previous.Point + previous.Direction * Vector2.Dot(middle - previous.Point, previous.Direction));
                corners.Add(line.Point + line.Direction * Vector2.Dot(middle - line.Point, line.Direction));
            }

            return corners.Count >= 3;
        }

        bool DropShortEdges()
        {
            var dropped = true;
            while (dropped && corners.Count > 3)
            {
                dropped = false;
                for (var i = 0; i < corners.Count; i++)
                {
                    var next = (i + 1) % corners.Count;
                    if (Vector2.Distance(corners[i], corners[next]) >= MinEdge)
                    {
                        continue;
                    }

                    corners[i] = (corners[i] + corners[next]) * 0.5f;
                    corners.RemoveAt(next);
                    dropped = true;
                    break;
                }
            }

            return corners.Count >= 3;
        }

        /// <summary>The new outline is the scanned one, tidied up: nearly the same area, corners near the old outline, no loops.</summary>
        bool IsCloseTo(IReadOnlyList<Vector2> loop)
        {
            var before = PolygonMath.SignedArea(loop);
            var after = PolygonMath.SignedArea(corners);
            if (Mathf.Abs(before) < 1e-6f || before * after <= 0f
                || Mathf.Abs(after - before) > MaxAreaChange * Mathf.Abs(before))
            {
                return false;
            }

            for (var i = 0; i < corners.Count; i++)
            {
                if (PolygonMath.DistanceToOutline(corners[i], loop) > MaxMove)
                {
                    return false;
                }
            }

            return PolygonMath.IsSimple(corners);
        }

        struct Line
        {
            public Vector2 Point;
            public Vector2 Direction;
            public float Length;

            /// <summary>0: along the x axis of the room, 1: along its z axis, -1: neither.</summary>
            public int Axis;
        }
    }
}
