using System.Collections.Generic;
using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// The world points one camera frame saw, and where the camera was. Filled by a scan source
    /// (depth image or tracking points) and read by <see cref="RoomSurfaceMap.Integrate"/>. The buffers
    /// are reused from frame to frame, so scanning does not allocate.
    /// </summary>
    public sealed class ScanFrame
    {
        /// <summary>Confidence of a point whose source does not measure it.</summary>
        public const byte FullConfidence = 255;

        readonly List<Vector3> points = new List<Vector3>();
        readonly List<bool> upFacing = new List<bool>();
        readonly List<byte> confidence = new List<byte>();

        /// <summary>Camera position when the frame was taken.</summary>
        public Vector3 Origin { get; private set; }

        /// <summary>Where the camera looked when the frame was taken (identity when the source does not say).</summary>
        public Quaternion Rotation { get; set; } = Quaternion.identity;

        /// <summary>True when every point knows whether its surface faces up (depth images). False for tracking points.</summary>
        public bool HasNormals { get; private set; }

        /// <summary>
        /// How much the frame counts as evidence (1 = a frame from a new point of view; less for a frame
        /// that shows what the frame before already showed).
        /// </summary>
        public float Weight { get; set; } = 1f;

        /// <summary>When the frame was taken, in seconds (0 when the source does not say).</summary>
        public double Time { get; set; }

        /// <summary>
        /// False while it is not certain how the depth image lines up with the screen: points of such a
        /// frame may confirm surfaces but must not remove any.
        /// </summary>
        public bool AllowCarving { get; set; } = true;

        public int Count => points.Count;

        public Vector3 PointAt(int index) => points[index];

        /// <summary>The surface at the point faces up (a floor or table top, not a wall or table side).</summary>
        public bool IsUpFacing(int index) => upFacing[index];

        /// <summary>How sure the source is of the point (0..255; depth images: the confidence ARCore gives).</summary>
        public byte ConfidenceAt(int index) => confidence[index];

        public void Reset(Vector3 origin, bool hasNormals)
        {
            Origin = origin;
            HasNormals = hasNormals;
            Rotation = Quaternion.identity;
            Weight = 1f;
            Time = 0.0;
            AllowCarving = true;
            points.Clear();
            upFacing.Clear();
            confidence.Clear();
        }

        public void Add(Vector3 point, bool facesUp)
        {
            Add(point, facesUp, FullConfidence);
        }

        public void Add(Vector3 point, bool facesUp, byte pointConfidence)
        {
            points.Add(point);
            upFacing.Add(facesUp);
            confidence.Add(pointConfidence);
        }

        /// <summary>Moves the camera position and every point into another space (world to map).</summary>
        internal void Transform(in Matrix4x4 matrix)
        {
            Origin = matrix.MultiplyPoint3x4(Origin);
            Rotation = matrix.rotation * Rotation;
            for (var i = 0; i < points.Count; i++)
            {
                points[i] = matrix.MultiplyPoint3x4(points[i]);
            }
        }
    }
}
