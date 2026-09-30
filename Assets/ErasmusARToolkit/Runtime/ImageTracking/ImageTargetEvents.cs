using System;
using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// Tells project modules when an image target is found: the camera sees it (again). Raised by the AR
    /// image tracking on phones, by webcam recognition on the PC and by the simulated images of Desktop
    /// mode when they come into view.
    /// </summary>
    public static class ImageTargetEvents
    {
        /// <summary>An image was found (or seen again after it was lost), with the content shown on it.</summary>
        public static event Action<ImageTargetItem, ToolkitObject> Found;

        internal static void RaiseFound(ImageTargetItem item, ToolkitObject content)
        {
            if (item != null)
            {
                Found?.Invoke(item, content);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            Found = null;
        }
    }
}
