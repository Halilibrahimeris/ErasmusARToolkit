using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>Creates placed objects and image target content with their label and selection ring.</summary>
    public static class ToolkitObjectFactory
    {
        /// <param name="parent">Placed-objects root, or an AR anchor in AR mode.</param>
        public static ToolkitObject SpawnPlaceable(PlaceableItem item, Pose pose, Transform parent,
            Vector2 scaleLimits, ToolkitTheme theme)
        {
            var instance = Object.Instantiate(item.Prefab, pose.position, pose.rotation, parent);
            instance.name = item.ResolvedName;
            var placedScale = item.Prefab.transform.localScale * item.DefaultScale;
            instance.transform.localScale = placedScale;
            ObjectUtility.EnsureCollider(instance);

            var manipulator = instance.AddComponent<ObjectManipulator>();
            manipulator.Initialize(placedScale, scaleLimits);
            return Attach(instance, item.ResolvedName, item.Info, manipulator, theme, true, false);
        }

        /// <summary>
        /// Content for a recognised image: an outline around the image plus the prefab and/or label.
        /// It stays on the image but can be rotated, raised and scaled.
        /// </summary>
        /// <param name="imageSize">Size of the image in meters (detected size in AR, configured size in Desktop mode).</param>
        public static ToolkitObject SpawnImageContent(ImageTargetItem item, Transform anchor, Vector2 imageSize,
            ToolkitTheme theme, Vector2? scaleLimits = null)
        {
            ImageTargetOutline.Create(anchor, imageSize, theme);

            GameObject instance;
            if (item.Prefab != null)
            {
                // Keep the model's own rotation and scale, but not the position it was modelled at:
                // imported models (e.g. a raw .fbx) can carry an offset of a meter or more, which would
                // put the content far beside the image.
                instance = Object.Instantiate(item.Prefab, anchor, false);
                instance.transform.localPosition = Vector3.zero;
                ObjectUtility.EnsureCollider(instance);
            }
            else
            {
                instance = new GameObject();
                instance.transform.SetParent(anchor, false);
                var box = instance.AddComponent<BoxCollider>();
                box.size = new Vector3(imageSize.x, 0.01f, imageSize.y);
            }

            instance.name = item.ResolvedName + " Content";
            var manipulator = instance.AddComponent<ObjectManipulator>();
            manipulator.Initialize(instance.transform.localScale, scaleLimits ?? new Vector2(0.2f, 5f), false);

            // Without a prefab the label is the only content, so it is always shown.
            var alwaysShowLabel = item.Prefab == null;
            return Attach(instance, item.ResolvedName, item.Info, manipulator, theme, false, alwaysShowLabel);
        }

        static ToolkitObject Attach(GameObject instance, string displayName, InfoLabelData info,
            ObjectManipulator manipulator, ToolkitTheme theme, bool isPlaced, bool alwaysShowLabel)
        {
            var label = info.HasContent ? InfoLabel.Create(instance.transform, info, theme) : null;
            if (label != null && alwaysShowLabel)
            {
                label.SetVisible(true);
            }

            var ringColor = theme != null ? theme.AccentColor : Color.yellow;
            var indicator = SelectionIndicator.Create(instance, theme != null ? theme.HighlightMaterial : null, ringColor);

            var toolkitObject = instance.AddComponent<ToolkitObject>();
            toolkitObject.Initialize(displayName, info, manipulator, label, indicator, isPlaced);
            return toolkitObject;
        }
    }
}
