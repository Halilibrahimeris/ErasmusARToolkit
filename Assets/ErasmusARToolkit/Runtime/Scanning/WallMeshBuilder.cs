using System.Collections.Generic;
using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// Meshes of the walls of the scanned room: a see-through panel from the floor up (the material
    /// lets it fade toward the top), and bars along the floor, up the ends and along the top. Seen
    /// from both sides: the scan does not know on which side of a wall the room lies.
    /// </summary>
    internal static class WallMeshBuilder
    {
        /// <summary>Thickness of the bar along the floor, in meters.</summary>
        public const float BaseWidth = 0.02f;

        /// <summary>Thickness of the other bars.</summary>
        public const float EdgeWidth = 0.01f;

        public static void Build(IReadOnlyList<WallSegment> walls, ObstacleMeshData output)
        {
            output.Clear();
            for (var i = 0; i < walls.Count; i++)
            {
                Add(walls[i], output);
            }
        }

        public static void Add(in WallSegment wall, ObstacleMeshData output)
        {
            var top = Mathf.Min(wall.Top, wall.Bottom + WallModel.MaxDrawnHeight);
            if (top <= wall.Bottom || wall.Length < 1e-3f)
            {
                return;
            }

            var lowA = new Vector3(wall.A.x, wall.Bottom, wall.A.y);
            var lowB = new Vector3(wall.B.x, wall.Bottom, wall.B.y);
            var highA = new Vector3(wall.A.x, top, wall.A.y);
            var highB = new Vector3(wall.B.x, top, wall.B.y);
            AddQuad(output.FillVertices, output.FillTriangles, lowA, highA, highB, lowB);
            AddBar(output, lowA, lowB, BaseWidth);
            AddBar(output, highA, highB, EdgeWidth);
            AddBar(output, lowA, highA, EdgeWidth);
            AddBar(output, lowB, highB, EdgeWidth);
        }

        static void AddBar(ObstacleMeshData output, Vector3 from, Vector3 to, float width)
        {
            var direction = (to - from).normalized;
            var side = Vector3.Cross(direction, Mathf.Abs(direction.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
            var other = Vector3.Cross(direction, side);
            var half = width * 0.5f;
            side *= half;
            other *= half;
            var vertices = output.OutlineVertices;
            var triangles = output.OutlineTriangles;
            AddQuad(vertices, triangles, from - side - other, from + side - other, to + side - other, to - side - other);
            AddQuad(vertices, triangles, from + side - other, from + side + other, to + side + other, to + side - other);
            AddQuad(vertices, triangles, from + side + other, from - side + other, to - side + other, to + side + other);
            AddQuad(vertices, triangles, from - side + other, from - side - other, to - side - other, to - side + other);
        }

        static void AddQuad(List<Vector3> vertices, List<int> triangles, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            var start = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            vertices.Add(d);
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
        }
    }
}
