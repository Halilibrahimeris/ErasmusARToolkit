using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>Small helpers for Unity objects that work in both Edit Mode and Play Mode.</summary>
    public static class ObjectUtility
    {
        public static void SafeDestroy(Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(target);
            }
            else
            {
                Object.DestroyImmediate(target);
            }
        }

        /// <summary>World-space bounds of all renderers under the object, or a small box at its position.</summary>
        public static Bounds GetRendererBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            var hasBounds = false;
            var bounds = new Bounds(root.transform.position, Vector3.zero);
            foreach (var renderer in renderers)
            {
                if (renderer is LineRenderer || renderer is ParticleSystemRenderer)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return hasBounds ? bounds : new Bounds(root.transform.position, Vector3.one * 0.1f);
        }

        /// <summary>Makes sure the object can be picked with a raycast by adding a fitted BoxCollider if needed.</summary>
        public static Collider EnsureCollider(GameObject root)
        {
            var existing = root.GetComponentInChildren<Collider>();
            if (existing != null)
            {
                return existing;
            }

            var worldBounds = GetRendererBounds(root);
            var box = root.AddComponent<BoxCollider>();
            var transform = root.transform;
            box.center = transform.InverseTransformPoint(worldBounds.center);
            var scale = transform.lossyScale;
            box.size = new Vector3(
                SafeDivide(worldBounds.size.x, scale.x),
                SafeDivide(worldBounds.size.y, scale.y),
                SafeDivide(worldBounds.size.z, scale.z));
            return box;
        }

        static float SafeDivide(float value, float divisor)
        {
            return Mathf.Abs(divisor) < 0.0001f ? value : value / Mathf.Abs(divisor);
        }
    }
}
