using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>Poses of project modules in the real world. Pure math, so it can be tested.</summary>
    public static class ModulePlacementMath
    {
        const float Epsilon = 1e-6f;

        /// <summary>The heading of a rotation only (upright). Looking straight down, the top of the view is the heading.</summary>
        public static Quaternion YawOnly(Quaternion rotation)
        {
            var forward = rotation * Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < Epsilon)
            {
                var up = rotation * Vector3.up;
                forward = new Vector3(up.x, 0f, up.z);
            }

            return forward.sqrMagnitude < Epsilon
                ? Quaternion.identity
                : Quaternion.LookRotation(forward.normalized, Vector3.up);
        }

        /// <summary>
        /// The pose of a module root so that its start point stands where the phone is, on the floor, and
        /// looks the way the phone looks.
        /// </summary>
        /// <param name="startLocal">The start point relative to the root (meters, rotation), or identity.</param>
        public static Pose AtAppStart(Vector3 cameraPosition, Quaternion cameraRotation, float floorY, Pose startLocal)
        {
            var rootRotation = YawOnly(cameraRotation) * Quaternion.Inverse(YawOnly(startLocal.rotation));
            var startFlat = new Vector3(startLocal.position.x, 0f, startLocal.position.z);
            var target = new Vector3(cameraPosition.x, floorY, cameraPosition.z);
            return new Pose(target - rootRotation * startFlat, rootRotation);
        }

        /// <summary>A module put down where the user tapped; its front (+Z) turned to the user.</summary>
        public static Pose TapToPlace(Vector3 floorPoint, Vector3 cameraPosition, bool faceUser)
        {
            var rotation = faceUser ? ManipulationMath.YawTowards(floorPoint, cameraPosition) : Quaternion.identity;
            return new Pose(floorPoint, rotation);
        }

        /// <summary>Where <paramref name="world"/> lies relative to <paramref name="holder"/>.</summary>
        public static Pose LocalOffset(Pose holder, Pose world)
        {
            var inverse = Quaternion.Inverse(holder.rotation);
            return new Pose(inverse * (world.position - holder.position), inverse * world.rotation);
        }

        /// <summary>The world pose of an offset kept relative to a holder that may have moved.</summary>
        public static Pose Follow(Pose holder, Pose offset)
        {
            return new Pose(holder.position + holder.rotation * offset.position, holder.rotation * offset.rotation);
        }

        public static Pose WithHeight(Pose pose, float y)
        {
            return new Pose(new Vector3(pose.position.x, y, pose.position.z), pose.rotation);
        }

        /// <summary>A child's pose relative to its root, in meters (whatever the root's scale).</summary>
        public static Pose Relative(Transform root, Transform child)
        {
            if (root == null || child == null || child == root)
            {
                return new Pose(Vector3.zero, Quaternion.identity);
            }

            return LocalOffset(new Pose(root.position, root.rotation), new Pose(child.position, child.rotation));
        }
    }
}
