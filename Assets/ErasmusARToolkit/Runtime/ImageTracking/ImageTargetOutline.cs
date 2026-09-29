using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// A thin frame drawn around a recognised image so the user sees that tracking worked.
    /// The image lies in the anchor's XZ plane (Y is the image normal), like ARTrackedImage.
    /// </summary>
    public static class ImageTargetOutline
    {
        const float LineWidth = 0.004f;
        const float HeightAboveImage = 0.002f;

        public static LineRenderer Create(Transform anchor, Vector2 size, ToolkitTheme theme)
        {
            var go = new GameObject("Image Outline");
            go.transform.SetParent(anchor, false);

            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.widthMultiplier = LineWidth;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            if (theme != null && theme.HighlightMaterial != null)
            {
                line.sharedMaterial = theme.HighlightMaterial;
            }

            var halfX = size.x * 0.5f;
            var halfZ = size.y * 0.5f;
            line.positionCount = 4;
            line.SetPosition(0, new Vector3(-halfX, HeightAboveImage, -halfZ));
            line.SetPosition(1, new Vector3(-halfX, HeightAboveImage, halfZ));
            line.SetPosition(2, new Vector3(halfX, HeightAboveImage, halfZ));
            line.SetPosition(3, new Vector3(halfX, HeightAboveImage, -halfZ));
            return line;
        }
    }
}
