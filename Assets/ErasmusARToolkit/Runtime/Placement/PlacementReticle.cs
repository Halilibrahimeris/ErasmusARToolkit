using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// A ring that lies on the surface in the middle of the view, showing "you can place here".
    /// It follows AR planes in AR mode and the virtual floor in Desktop mode, and hides when
    /// there is no surface under the screen center.
    /// </summary>
    public sealed class PlacementReticle : MonoBehaviour
    {
        const int Segments = 40;
        const float Radius = 0.06f;
        const float LineWidth = 0.006f;
        const float HeightAboveSurface = 0.003f;

        ARToolkitManager manager;
        LineRenderer line;

        public bool IsShown => line != null && line.enabled;

        public static PlacementReticle Create(ARToolkitManager toolkitManager)
        {
            var go = new GameObject("Placement Reticle");
            var reticle = go.AddComponent<PlacementReticle>();
            reticle.manager = toolkitManager;
            reticle.Build(toolkitManager.Theme);
            return reticle;
        }

        void Build(ToolkitTheme theme)
        {
            line = gameObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = Segments;
            line.widthMultiplier = LineWidth;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            if (theme != null && theme.HighlightMaterial != null)
            {
                line.sharedMaterial = theme.HighlightMaterial;
            }

            for (var i = 0; i < Segments; i++)
            {
                var angle = i * Mathf.PI * 2f / Segments;
                line.SetPosition(i, new Vector3(Mathf.Cos(angle) * Radius, 0f, Mathf.Sin(angle) * Radius));
            }

            line.enabled = false;
        }

        void LateUpdate()
        {
            var visible = TryGetCenterPose(out var pose);
            line.enabled = visible;
            if (visible)
            {
                transform.SetPositionAndRotation(pose.position + pose.up * HeightAboveSurface, pose.rotation);
            }
        }

        bool TryGetCenterPose(out Pose pose)
        {
            pose = default;
            if (manager == null || manager.Config == null || manager.Config.Placeables.Count == 0
                || manager.ActiveSurface == null || manager.ActiveCamera == null)
            {
                return false;
            }

            var center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            return manager.ActiveSurface.TryGetPose(center, out pose);
        }
    }
}
