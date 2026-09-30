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
        bool labelWanted;

        public string DisplayName { get; private set; } = string.Empty;
        public InfoLabelData Info { get; private set; } = new InfoLabelData();
        public ObjectManipulator Manipulator => manipulator;
        public InfoLabel Label => label;
        public bool IsManipulatable => manipulator != null;

        /// <summary>True for objects the user placed (they can be deleted); false for image target content.</summary>
        public bool IsPlaced { get; private set; }

        /// <summary>The object hangs on a wall: it moves along walls and turns around the wall's normal.</summary>
        public bool IsOnWall { get; private set; }

        /// <summary>The pose of the wall it hangs on (forward = out of the wall).</summary>
        internal Quaternion WallRotation { get; private set; } = Quaternion.identity;

        /// <summary>Marks the object as hanging on a wall with this rotation.</summary>
        internal void SetOnWall(Quaternion wallRotation)
        {
            IsOnWall = true;
            WallRotation = wallRotation;
            if (manipulator != null)
            {
                manipulator.OnWall = true;
            }
        }

        /// <summary>The object stands on a scanned surface and follows it (see <see cref="SurfaceHolder"/>).</summary>
        public bool HasSurface => surfaceHolder != null && surfaceHolder.HasSurface;

        /// <summary>How far (meters) the object was raised above the surface it stands on.</summary>
        public float HeightAboveSurface => surfaceHolder != null ? transform.localPosition.y : 0f;

        /// <summary>What the object hangs from when it stands on a scanned surface, else null.</summary>
        internal SurfaceHolder SurfaceHolder => surfaceHolder;

        public bool HasLabel => label != null;
        public bool IsLabelVisible => label != null && label.IsVisible;

        /// <summary>
        /// The card opens when the object is selected and closes when it is not (objects with an
        /// <see cref="InfoCard"/>). Cards set to show on start stay open.
        /// </summary>
        internal bool ShowsLabelOnSelect { get; set; }

        internal void Initialize(string displayName, InfoLabelData info, ObjectManipulator objectManipulator,
            InfoLabel infoLabel, SelectionIndicator selectionIndicator, bool isPlaced)
        {
            DisplayName = displayName ?? string.Empty;
            Info = info ?? new InfoLabelData();
            manipulator = objectManipulator;
            label = infoLabel;
            indicator = selectionIndicator;
            IsPlaced = isPlaced;
            labelWanted = label != null && label.IsVisible;
            if (label != null && !IsShown)
            {
                label.SetVisible(false);
            }
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

            if (ShowsLabelOnSelect && label != null)
            {
                SetLabelVisible(selected || Info.ShowOnStart);
            }
        }

        public void SetLabelVisible(bool visible)
        {
            labelWanted = visible;
            if (label != null)
            {
                label.SetVisible(visible && IsShown);
            }
        }

        /// <summary>The object is switched on (works in Edit Mode too, unlike isActiveAndEnabled).</summary>
        bool IsShown => enabled && gameObject.activeInHierarchy;

        /// <summary>
        /// The card is an object of its own (it must not take the object's scale), so it is hidden and
        /// shown with the object: image content that lost its image, a part of a design that is switched off.
        /// </summary>
        void OnEnable()
        {
            if (label != null && labelWanted)
            {
                label.SetVisible(true);
            }
        }

        void OnDisable()
        {
            if (label != null)
            {
                label.SetVisible(false);
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
