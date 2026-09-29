using UnityEngine;

namespace Erasmus.ARToolkit.Editor
{
    /// <summary>Version-safe scene object queries.</summary>
    static class SceneQueries
    {
        public static T[] FindAll<T>() where T : Object
        {
#if UNITY_6000_4_OR_NEWER
            return Object.FindObjectsByType<T>(FindObjectsInactive.Include);
#else
            return Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#endif
        }

        public static T FindFirst<T>() where T : Object
        {
            var all = FindAll<T>();
            return all.Length > 0 ? all[0] : null;
        }
    }
}
