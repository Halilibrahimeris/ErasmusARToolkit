using System.Collections.Generic;
using UnityEngine;

namespace Erasmus.ARToolkit.Tests
{
    /// <summary>
    /// What the simulated depth camera gets wrong. The defaults are a phone that measures well in a room
    /// with a matte floor: one centimeter of noise and nothing else.
    /// </summary>
    public sealed class SimulationOptions
    {
        public int Columns = 40;
        public int Rows = 40;
        public float HalfFieldOfViewDegrees = 25f;

        /// <summary>Largest error (meters) of a depth right in front of the camera.</summary>
        public float NoiseBase = 0.01f;

        /// <summary>The error grows by this much per square meter of distance, as depth from motion does.</summary>
        public float NoiseK;

        /// <summary>Every depth is multiplied by this (a phone that measures 3 % too far: 1.03).</summary>
        public float ScaleBias = 1f;

        /// <summary>Share (0..1) of the floor pixels that show what the floor mirrors instead of the floor.</summary>
        public float GlossyFloor;

        /// <summary>Share (0..1) of the floor pixels ARCore is not sure of.</summary>
        public float LowConfidenceShare;

        /// <summary>The confidence of those pixels.</summary>
        public byte LowConfidence = 60;

        /// <summary>
        /// The pose the frame reports is this many degrees behind (tilted up or down), as when the depth
        /// image is older than the camera pose it is used with.
        /// </summary>
        public float PoseLagDegrees;

        public int Seed = 42;
    }

    /// <summary>
    /// A room for tests of the room scan: a floor, boxes (desks, chairs, a podium), a ceiling and, when
    /// asked for, walls. <see cref="Capture"/> fills a frame with what a phone's depth camera sees from a
    /// position, with the mistakes of <see cref="SimulationOptions"/>.
    /// </summary>
    public sealed class RoomSimulator
    {
        const float FaceTolerance = 1e-3f;
        const float ReflectionStart = 1e-3f;

        readonly List<Bounds> boxes = new List<Bounds>();
        readonly System.Random random;

        public RoomSimulator(SimulationOptions options = null)
        {
            Options = options ?? new SimulationOptions();
            random = new System.Random(Options.Seed);
        }

        public SimulationOptions Options { get; }

        public float FloorHeight { get; set; }

        /// <summary>Only what the floor mirrors and rays that look up reach the ceiling. 0 or less: no ceiling.</summary>
        public float CeilingHeight { get; set; } = 2.8f;

        /// <summary>The walls around the room (x and z of their inner sides), or null for a room without walls.</summary>
        public Rect? Walls { get; set; }

        public IReadOnlyList<Bounds> Boxes => boxes;

        /// <summary>Adds a solid box and returns its index.</summary>
        public int AddBox(Vector3 min, Vector3 max)
        {
            var bounds = new Bounds();
            bounds.SetMinMax(min, max);
            boxes.Add(bounds);
            return boxes.Count - 1;
        }

        public void MoveBox(int index, Vector3 offset)
        {
            var bounds = boxes[index];
            bounds.center += offset;
            boxes[index] = bounds;
        }

        public void RemoveBox(int index)
        {
            boxes.RemoveAt(index);
        }

        /// <summary>A depth frame of a camera at <paramref name="origin"/> that looks at <paramref name="target"/>.</summary>
        public void Capture(Vector3 origin, Vector3 target, ScanFrame frame, bool hasNormals = true)
        {
            Capture(origin, Quaternion.LookRotation(target - origin), frame, hasNormals);
        }

        public void Capture(Vector3 origin, Quaternion rotation, ScanFrame frame, bool hasNormals = true)
        {
            frame.Reset(origin, hasNormals);
            frame.Rotation = rotation;
            var options = Options;
            var reported = rotation * Quaternion.AngleAxis(options.PoseLagDegrees, Vector3.right);
            var extent = Mathf.Tan(options.HalfFieldOfViewDegrees * Mathf.Deg2Rad);
            for (var row = 0; row < options.Rows; row++)
            {
                for (var column = 0; column < options.Columns; column++)
                {
                    var local = new Vector3(
                        Mathf.Lerp(-extent, extent, column / (options.Columns - 1f)),
                        Mathf.Lerp(-extent, extent, row / (options.Rows - 1f)), 1f).normalized;
                    var direction = rotation * local;
                    if (!Cast(origin, direction, true, out var distance, out var face))
                    {
                        continue;
                    }

                    var noise = ((float)random.NextDouble() * 2f - 1f)
                                * (options.NoiseBase + options.NoiseK * distance * distance);
                    var facesUp = face == Face.Floor || face == Face.Top;
                    var confidence = ScanFrame.FullConfidence;
                    if (face == Face.Floor)
                    {
                        if (options.GlossyFloor > 0f && random.NextDouble() < options.GlossyFloor)
                        {
                            Mirror(origin, direction, ref distance, ref facesUp);
                        }

                        if (options.LowConfidenceShare > 0f && random.NextDouble() < options.LowConfidenceShare)
                        {
                            confidence = options.LowConfidence;
                        }
                    }

                    var measured = distance * options.ScaleBias + noise;
                    frame.Add(origin + reported * local * measured, facesUp, confidence);
                }
            }
        }

        /// <summary>
        /// A shiny floor works like a mirror: the pixel shows what lies along the mirrored ray, and its
        /// depth is the whole way there. The point ends up below the floor, and what faced down faces up.
        /// </summary>
        void Mirror(Vector3 origin, Vector3 direction, ref float distance, ref bool facesUp)
        {
            var onFloor = origin + direction * distance;
            var mirrored = new Vector3(direction.x, -direction.y, direction.z);
            if (!Cast(onFloor + mirrored * ReflectionStart, mirrored, false, out var beyond, out var face))
            {
                return;
            }

            distance += beyond + ReflectionStart;
            facesUp = face == Face.Bottom || face == Face.Ceiling;
        }

        bool Cast(Vector3 origin, Vector3 direction, bool withFloor, out float distance, out Face face)
        {
            distance = float.MaxValue;
            face = Face.None;
            if (withFloor && direction.y < 0f && origin.y > FloorHeight)
            {
                distance = (FloorHeight - origin.y) / direction.y;
                face = Face.Floor;
            }

            if (CeilingHeight > FloorHeight && direction.y > 0f && origin.y < CeilingHeight)
            {
                var hit = (CeilingHeight - origin.y) / direction.y;
                if (hit < distance)
                {
                    distance = hit;
                    face = Face.Ceiling;
                }
            }

            if (Walls.HasValue)
            {
                CastWalls(origin, direction, Walls.Value, ref distance, ref face);
            }

            var ray = new Ray(origin, direction);
            for (var i = 0; i < boxes.Count; i++)
            {
                var box = boxes[i];
                if (!box.IntersectRay(ray, out var hit) || hit <= 0f || hit >= distance)
                {
                    continue;
                }

                distance = hit;
                var height = origin.y + direction.y * hit;
                face = Mathf.Abs(height - box.max.y) < FaceTolerance ? Face.Top
                    : Mathf.Abs(height - box.min.y) < FaceTolerance ? Face.Bottom
                    : Face.Side;
            }

            return face != Face.None;
        }

        static void CastWalls(Vector3 origin, Vector3 direction, Rect walls, ref float distance, ref Face face)
        {
            CastWall(origin.x, direction.x, walls.xMin, walls.xMax, ref distance, ref face);
            CastWall(origin.z, direction.z, walls.yMin, walls.yMax, ref distance, ref face);
        }

        static void CastWall(float origin, float direction, float min, float max, ref float distance, ref Face face)
        {
            if (Mathf.Abs(direction) < 1e-6f)
            {
                return;
            }

            var hit = ((direction > 0f ? max : min) - origin) / direction;
            if (hit > 0f && hit < distance)
            {
                distance = hit;
                face = Face.Side;
            }
        }

        enum Face
        {
            None,
            Floor,
            Top,
            Bottom,
            Side,
            Ceiling,
        }
    }
}
