using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>A flat ring drawn under the selected object.</summary>
    public sealed class SelectionIndicator : MonoBehaviour
    {
        const int Segments = 48;
        const float LineWidth = 0.008f;

        LineRenderer line;

        public static SelectionIndicator Create(GameObject target, Material material, Color color)
        {
            var bounds = ObjectUtility.GetRendererBounds(target);
            var targetTransform = target.transform;
            var scale = Mathf.Max(0.0001f, Mathf.Abs(targetTransform.lossyScale.x));
            var radius = Mathf.Max(bounds.extents.x, bounds.extents.z) * 1.15f / scale;
            var bottom = (bounds.min.y - targetTransform.position.y + 0.005f) / scale;

            var ring = new GameObject("Selection Ring");
            ring.transform.SetParent(targetTransform, false);
            ring.transform.localPosition = new Vector3(0f, bottom, 0f);

            var indicator = ring.AddComponent<SelectionIndicator>();
            indicator.Build(radius, material, color);
            indicator.SetVisible(false);
            return indicator;
        }

        public void SetVisible(bool visible)
        {
            gameObject.SetActive(visible);
        }

        void Build(float radius, Material material, Color color)
        {
            line = gameObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = Segments;
            line.widthMultiplier = LineWidth;
            line.startColor = color;
            line.endColor = color;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            if (material != null)
            {
                line.sharedMaterial = material;
            }

            for (var i = 0; i < Segments; i++)
            {
                var angle = i * Mathf.PI * 2f / Segments;
                line.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
            }
        }
    }
}
