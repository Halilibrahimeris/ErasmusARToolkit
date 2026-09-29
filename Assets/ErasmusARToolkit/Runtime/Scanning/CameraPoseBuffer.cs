using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// The camera poses of the last second with their frame times. A depth image can be older than the
    /// camera frame it arrives with; its points belong to the pose the camera had when the image was taken.
    /// </summary>
    internal sealed class CameraPoseBuffer
    {
        public const int Capacity = 32;

        /// <summary>A pose this close in time (seconds) is used as it is.</summary>
        public const double ExactSeconds = 0.008;

        /// <summary>Between two poses up to this far apart (seconds) the pose is interpolated.</summary>
        public const double MaxGapSeconds = 0.1;

        readonly double[] times = new double[Capacity];
        readonly Vector3[] positions = new Vector3[Capacity];
        readonly Quaternion[] rotations = new Quaternion[Capacity];
        int next;

        public int Count { get; private set; }

        public void Clear()
        {
            next = 0;
            Count = 0;
        }

        /// <summary>Adds the pose of a camera frame. Frames come in the order of their times.</summary>
        public void Push(double time, Pose pose)
        {
            if (Count > 0)
            {
                var newest = times[(next + Capacity - 1) % Capacity];
                if (time == newest)
                {
                    return;
                }

                if (time < newest)
                {
                    // Time went back: the AR session started over.
                    Clear();
                }
            }

            times[next] = time;
            positions[next] = pose.position;
            rotations[next] = pose.rotation;
            next = (next + 1) % Capacity;
            Count = Mathf.Min(Count + 1, Capacity);
        }

        /// <summary>The pose at a time, or false when the buffer has nothing close enough.</summary>
        public bool TrySample(double time, out Pose pose)
        {
            pose = default;
            var before = -1;
            var after = -1;
            for (var i = 0; i < Count; i++)
            {
                var index = (next + Capacity - 1 - i) % Capacity;
                if (times[index] >= time)
                {
                    after = index;
                }
                else
                {
                    before = index;
                    break;
                }
            }

            var useAfter = after >= 0 && times[after] - time <= ExactSeconds;
            var useBefore = before >= 0 && time - times[before] <= ExactSeconds;
            if (useAfter && (!useBefore || times[after] - time <= time - times[before]))
            {
                pose = new Pose(positions[after], rotations[after]);
                return true;
            }

            if (useBefore)
            {
                pose = new Pose(positions[before], rotations[before]);
                return true;
            }

            if (before < 0 || after < 0 || times[after] - times[before] > MaxGapSeconds)
            {
                return false;
            }

            var t = (float)((time - times[before]) / (times[after] - times[before]));
            pose = new Pose(Vector3.Lerp(positions[before], positions[after], t),
                Quaternion.Slerp(rotations[before], rotations[after], t));
            return true;
        }
    }
}
