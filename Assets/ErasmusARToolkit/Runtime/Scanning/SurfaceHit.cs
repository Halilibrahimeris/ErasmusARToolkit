using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>A point on a scanned surface, as found under a screen position.</summary>
    public readonly struct SurfaceHit
    {
        public SurfaceHit(Pose pose, int levelId, bool blocked = false, bool behindWall = false)
        {
            Pose = pose;
            LevelId = levelId;
            Blocked = blocked;
            BehindWall = behindWall;
        }

        /// <summary>Where the surface was hit, in world space. Faces up like a detected plane.</summary>
        public Pose Pose { get; }

        /// <summary>The surface that was hit (see <see cref="SurfaceLevel.Id"/>), or -1.</summary>
        public int LevelId { get; }

        /// <summary>Something stands in the way: the spot cannot be reached.</summary>
        public bool Blocked { get; }

        /// <summary>What stands in the way is a wall: the spot lies outside of the room.</summary>
        public bool BehindWall { get; }
    }
}
