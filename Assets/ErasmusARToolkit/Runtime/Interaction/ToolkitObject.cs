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
        SurfaceHolder surfaceHolder;

        public string DisplayName { get; private set; } = string.Empty;
        public InfoLabelData Info { get; private set; } = new InfoLabelData();
        public ObjectManipulator Manipulator => manipulator;
        public InfoLabel Label => label;
        public bool IsManipulatable => manipulator != null;

        /// <summary>True for objects the user placed (they can be deleted); false for image target content.</summary>
        public bool IsPlaced { get; private set; }

        /// <summary>The object stands on a scanned surface and follows it (see <see cref="SurfaceHolder"/>).</summary>
        public bool HasSurface => surfaceHolder != null && surfaceHolder.HasSurface;

        /// <summary>How far (meters) the object was raised above the surface it stands on.</summary>
        public float HeightAboveSurface => surfaceHolder != null ? transform.localPosition.y : 0f;

        /// <summary>What the object hangs from when it stands on a scanned surface, else null.</summary>
        internal SurfaceHolder SurfaceHolder => surfaceHolder;

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
            surfaceHolder = holder != null ? holder.GetComponent<SurfaceHolder>() : null;
            ApplySurfaceLimit();
        }

        /// <summary>
        /// Moves the object from what it hangs from now onto a holder of the room scan: it stands on the
        /// scanned surface from now on. Its anchor is no longer needed.
        /// </summary>
        internal void HangFrom(SurfaceHolder holder)
        {
            if (holder == null)
            {
                return;
            }

            var before = anchorHolder;
            if (manipulator != null)
            {
                manipulator.MoveUnder(holder.transform, Vector3.zero);
            }
            else
            {
                transform.SetParent(holder.transform, true);
                transform.localPosition = Vector3.zero;
            }

            anchorHolder = holder.gameObject;
            surfaceHolder = holder;
            ApplySurfaceLimit();
            if (before != null && before != anchorHolder)
            {
                ObjectUtility.SafeDestroy(before);
            }
        }

        /// <summary>Puts the object onto its holder, which was moved to the surface under the object.</summary>
        internal void StandOnHolder()
        {
            if (surfaceHolder == null)
            {
                return;
            }

            if (manipulator != null)
            {
                manipulator.MoveUnder(surfaceHolder.transform, Vector3.zero);
            }
            else
            {
                transform.localPosition = Vector3.zero;
            }
        }

        /// <summary>An object on a scanned surface cannot be lowered into it.</summary>
        void ApplySurfaceLimit()
        {
            if (manipulator != null)
            {
                manipulator.MinLocalHeight = surfaceHolder != null ? 0f : (float?)null;
            }
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
