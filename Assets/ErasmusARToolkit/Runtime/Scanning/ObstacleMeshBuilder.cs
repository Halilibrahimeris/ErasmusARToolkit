using System.Collections.Generic;
using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>Vertex and index lists of the boxes around the obstacles. Reused between rebuilds.</summary>
    internal sealed class ObstacleMeshData
    {
        public readonly List<Vector3> FillVertices = new List<Vector3>();
        public readonly List<int> FillTriangles = new List<int>();
        public readonly List<Vector3> OutlineVertices = new List<Vector3>();
        public readonly List<int> OutlineTriangles = new List<int>();

        public void Clear()
        {
            FillVertices.Clear();
            FillTriangles.Clear();
            OutlineVertices.Clear();
            OutlineTriangles.Clear();
        }
    }

    /// <summary>
    /// Meshes of the boxes around obstacles: the four sides and the top of each box as a see-through
    /// fill, and its twelve edges as thin bars. The faces point outward, so the fill can be drawn
    /// without the faces at the back. Panels (walls) are boxes without a top that are open to the room.
    /// </summary>
    internal static class ObstacleMeshBuilder
    {
        /// <summary>Thickness of the bars along the edges, in meters.</summary>
        public const float EdgeWidth = 0.01f;

        static readonly Vector3[] Corners = new Vector3[8];

        public static void Build(IReadOnlyList<ObstacleVolume> volumes, ObstacleMeshData output)
        {
            output.Clear();
            for (var i = 0; i < volumes.Count; i++)
            {
                Add(volumes[i], output);
            }
        }

        public static void Add(in ObstacleVolume volume, ObstacleMeshData output)
        {
            SetCorners(volume);
            var fill = output.FillVertices;
            var triangles = output.FillTriangles;

            // Corners 0..3 lie on the floor, 4..7 above them, both clockwise seen from above: Unity
            // takes the side from which a triangle runs clockwise for its front.
            AddQuad(fill, triangles, Corners[4], Corners[5], Corners[6], Corners[7]);
            for (var side = 0; side < 4; side++)
            {
                var next = (side + 1) % 4;
                AddQuad(fill, triangles, Corners[next], Corners[next + 4], Corners[side + 4], Corners[side]);
            }

            for (var side = 0; side < 4; side++)
            {
                var next = (side + 1) % 4;
                AddBar(output, Corners[side], Corners[next]);
                AddBar(output, Corners[side + 4], Corners[next + 4]);
                AddBar(output, Corners[side], Corners[side + 4]);
            }
        }

        static void SetCorners(in ObstacleVolume volume)
        {
            var halfX = volume.Size.x * 0.5f;
            var halfZ = volume.Size.z * 0.5f;
            var pose = volume.Pose;
            Corners[0] = pose.position + pose.rotation * new Vector3(-halfX, 0f, -halfZ);
            Corners[1] = pose.position + pose.rotation * new Vector3(-halfX, 0f, halfZ);
            Corners[2] = pose.position + pose.rotation * new Vector3(halfX, 0f, halfZ);
            Corners[3] = pose.position + pose.rotation * new Vector3(halfX, 0f, -halfZ);
            var up = pose.rotation * new Vector3(0f, volume.Size.y, 0f);
            for (var i = 0; i < 4; i++)
            {
                Corners[i + 4] = Corners[i] + up;
            }
        }

        /// <summary>A bar with a square cross section, so an edge shows from every side.</summary>
        static void AddBar(ObstacleMeshData output, Vector3 from, Vector3 to)
        {
            var direction = to - from;
            var length = direction.magnitude;
            if (length < 1e-5f)
            {
                return;
            }

            direction /= length;
            var side = Vector3.Cross(direction, Mathf.Abs(direction.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
            var other = Vector3.Cross(direction, side);
            var half = EdgeWidth * 0.5f;
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
