using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>What an obstacle probably is, judged by its size.</summary>
    public enum ObstacleKind
    {
        /// <summary>Something that stands on the floor: a bag, a box, a cabinet.</summary>
        Other,

        /// <summary>It has a flat top at the height of a table.</summary>
        Table,

        /// <summary>It has a small flat top at the height of a seat.</summary>
        Seat,

        /// <summary>Tall, long and thin.</summary>
        Wall,
    }

    /// <summary>
    /// A box around something that stands in the room, as the room scan found it: furniture, a bag, a
    /// wall. Like the volumes of a VR headset's room model. The box stands on the floor.
    /// </summary>
    public readonly struct ObstacleVolume
    {
        public ObstacleVolume(int id, Pose pose, Vector3 size, ObstacleKind kind, float confidence, float age)
        {
            Id = id;
            Pose = pose;
            Size = size;
            Kind = kind;
            Confidence = confidence;
            Age = age;
        }

        /// <summary>Stays the same while the scan sees the obstacle as the same thing.</summary>
        public int Id { get; }

        /// <summary>Center of the bottom of the box (on the floor) and the direction of its sides.</summary>
        public Pose Pose { get; }

        /// <summary>Width (x), height (y) and depth (z) in meters.</summary>
        public Vector3 Size { get; }

        public ObstacleKind Kind { get; }

        /// <summary>How sure the scan is that something stands there (0..1).</summary>
        public float Confidence { get; }

        /// <summary>Seconds since the obstacle was first found.</summary>
        public float Age { get; }

        /// <summary>Height of the top of the box.</summary>
        public float Top => Pose.position.y + Size.y;

        /// <summary>The point lies inside the box, seen from above (with a margin in meters).</summary>
        public bool ContainsXZ(Vector3 point, float margin = 0f)
        {
            var local = Quaternion.Inverse(Pose.rotation) * (point - Pose.position);
            return Mathf.Abs(local.x) <= Size.x * 0.5f + margin && Mathf.Abs(local.z) <= Size.z * 0.5f + margin;
        }

        /// <summary>
        /// Where a ray enters the box. False when it misses the box or starts beyond it.
        /// </summary>
        /// <param name="distance">Distance along the ray to the point where it enters (0 when it starts inside).</param>
        public bool Raycast(Ray ray, out float distance)
        {
            distance = 0f;
            var inverse = Quaternion.Inverse(Pose.rotation);
            var origin = inverse * (ray.origin - Pose.position);
            var direction = inverse * ray.direction;
            var min = new Vector3(-Size.x * 0.5f, 0f, -Size.z * 0.5f);
            var max = new Vector3(Size.x * 0.5f, Size.y, Size.z * 0.5f);
            var near = 0f;
            var far = float.MaxValue;
            for (var axis = 0; axis < 3; axis++)
            {
                if (Mathf.Abs(direction[axis]) < 1e-7f)
                {
                    if (origin[axis] < min[axis] || origin[axis] > max[axis])
                    {
                        return false;
                    }

                    continue;
                }

                var first = (min[axis] - origin[axis]) / direction[axis];
                var second = (max[axis] - origin[axis]) / direction[axis];
                if (first > second)
                {
                    (first, second) = (second, first);
                }

                near = Mathf.Max(near, first);
                far = Mathf.Min(far, second);
                if (near > far)
                {
                    return false;
                }
            }

            distance = near;
            return true;
        }

        /// <summary>The same box at another place (the scan moved in the world).</summary>
        internal ObstacleVolume At(Pose pose)
        {
            return new ObstacleVolume(Id, pose, Size, Kind, Confidence, Age);
        }
    }
}
