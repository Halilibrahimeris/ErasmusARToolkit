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
        readonly List<Vector3> points = new List<Vector3>();
        readonly List<bool> upFacing = new List<bool>();

        /// <summary>Camera position when the frame was taken.</summary>
        public Vector3 Origin { get; private set; }

        /// <summary>True when every point knows whether its surface faces up (depth images). False for tracking points.</summary>
        public bool HasNormals { get; private set; }

        public int Count => points.Count;

        public Vector3 PointAt(int index) => points[index];

        /// <summary>The surface at the point faces up (a floor or table top, not a wall or table side).</summary>
        public bool IsUpFacing(int index) => upFacing[index];

        public void Reset(Vector3 origin, bool hasNormals)
        {
            Origin = origin;
            HasNormals = hasNormals;
            points.Clear();
            upFacing.Clear();
        }

        public void Add(Vector3 point, bool facesUp)
        {
            points.Add(point);
            upFacing.Add(facesUp);
        }
    }
}
