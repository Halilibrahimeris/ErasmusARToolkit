using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// What an object placed on a scanned surface hangs from: a point of the surface, in the space of the
    /// scan. It moves with the scan when ARCore corrects its map and follows the height of its surface,
    /// so the object keeps standing on the grid that is drawn under it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SurfaceHolder : MonoBehaviour
    {
        /// <summary>The surface the holder lies on (see <see cref="SurfaceLevel.Id"/>), or -1 when that surface is gone.</summary>
        public int LevelId { get; internal set; } = -1;

        public bool HasSurface => LevelId >= 0;
    }
}
