using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// Marks an object created by the toolkit (placed object or image target content)
    /// and links it to its info, 3D label, selection ring, manipulator and AR anchor.
    /// </summary>
    public sealed class ToolkitObject : MonoBehaviour
    {
        ObjectManipulator manipulator;
        InfoLabel label;
        SelectionIndicator indicator;
        GameObject anchorHolder;

        public string DisplayName { get; private set; } = string.Empty;
        public InfoLabelData Info { get; private set; } = new InfoLabelData();
        public ObjectManipulator Manipulator => manipulator;
        public InfoLabel Label => label;
        public bool IsManipulatable => manipulator != null;

        /// <summary>True for objects the user placed (they can be deleted); false for image target content.</summary>
        public bool IsPlaced { get; private set; }

        public bool HasLabel => label != null;
        public bool IsLabelVisible => label != null && label.IsVisible;

        internal void Initialize(string displayName, InfoLabelData info, ObjectManipulator objectManipulator,
            InfoLabel infoLabel, SelectionIndicator selectionIndicator, bool isPlaced)
        {
            DisplayName = displayName ?? string.Empty;
            Info = info ?? new InfoLabelData();
            manipulator = objectManipulator;
            label = infoLabel;
            indicator = selectionIndicator;
            IsPlaced = isPlaced;
        }

        /// <summary>The AR anchor this object hangs from; it is removed together with the object.</summary>
        internal void SetAnchorHolder(GameObject holder)
        {
            anchorHolder = holder;
        }

        public void SetSelected(bool selected)
        {
            if (indicator != null)
            {
                indicator.SetVisible(selected);
            }
        }

        public void SetLabelVisible(bool visible)
        {
            if (label != null)
            {
                label.SetVisible(visible);
            }
        }

        public void ToggleLabel()
        {
            SetLabelVisible(!IsLabelVisible);
        }

        void OnDestroy()
        {
            if (label != null)
            {
                ObjectUtility.SafeDestroy(label.gameObject);
            }

            if (anchorHolder != null)
            {
                ObjectUtility.SafeDestroy(anchorHolder);
            }
        }
    }
}
