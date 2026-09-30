using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// Applies move, rotate, raise and scale changes to a toolkit object, respecting the scale
    /// limits, and can reset it to where it was placed.
    /// </summary>
    public sealed class ObjectManipulator : MonoBehaviour
    {
        Vector3 baseScale = Vector3.one;
        Vector2 scaleLimits = new Vector2(0.2f, 5f);
        float scaleMultiplier = 1f;
        Vector3 initialLocalPosition;
        Quaternion initialLocalRotation = Quaternion.identity;

        /// <summary>Current scale relative to the placed size (1 = as placed).</summary>
        public float ScaleMultiplier => scaleMultiplier;

        public Vector2 ScaleLimits => scaleLimits;

        /// <summary>False for content fixed to an image: it can be edited but not dragged away.</summary>
        public bool CanMove { get; private set; } = true;

        /// <summary>
        /// The object cannot be lowered below this height above its parent (objects on a scanned surface:
        /// 0, the surface). Null: no limit.
        /// </summary>
        internal float? MinLocalHeight { get; set; }

        /// <summary>The object hangs on a wall: it turns around its own forward axis (the wall's normal).</summary>
        internal bool OnWall { get; set; }

        /// <summary>Rotation around the parent's up axis, 0-360 degrees.</summary>
        public float YawDegrees => Mathf.Repeat(transform.localEulerAngles.y, 360f);

        Vector3 UpAxis => transform.parent != null ? transform.parent.up : Vector3.up;

        internal void Initialize(Vector3 placedScale, Vector2 limits, bool canMove = true)
        {
            baseScale = placedScale;
            scaleLimits = limits;
            CanMove = canMove;
            initialLocalPosition = transform.localPosition;
            initialLocalRotation = transform.localRotation;
            SetScaleMultiplier(1f);
        }

        public void MoveTo(Vector3 position)
        {
            transform.position = position;
        }

        public void RotateYaw(float degrees)
        {
            var axis = OnWall ? transform.forward : UpAxis;
            transform.rotation = Quaternion.AngleAxis(degrees, axis) * transform.rotation;
        }

        /// <summary>Moves the object up (positive) or down along the surface normal.</summary>
        public void Raise(float meters)
        {
            transform.position += UpAxis * meters;
            if (MinLocalHeight.HasValue && transform.localPosition.y < MinLocalHeight.Value)
            {
                var local = transform.localPosition;
                local.y = MinLocalHeight.Value;
                transform.localPosition = local;
            }
        }

        /// <summary>
        /// Gives the object another parent and a place under it. It keeps the direction it faces, and
        /// <see cref="ResetPose"/> brings it back to this place, facing the way it was placed.
        /// </summary>
        internal void MoveUnder(Transform parent, Vector3 localPosition)
        {
            var placedRotation = transform.parent != null
                ? transform.parent.rotation * initialLocalRotation
                : initialLocalRotation;
            transform.SetParent(parent, true);
            transform.localPosition = localPosition;
            initialLocalPosition = localPosition;
            initialLocalRotation = parent != null
                ? Quaternion.Inverse(parent.rotation) * placedRotation
                : placedRotation;
        }

        public void SetScaleMultiplier(float multiplier)
        {
            scaleMultiplier = ManipulationMath.ClampScale(multiplier, scaleLimits);
            transform.localScale = baseScale * scaleMultiplier;
        }

        public void ResetPose()
        {
            transform.localPosition = initialLocalPosition;
            transform.localRotation = initialLocalRotation;
            SetScaleMultiplier(1f);
        }
    }
}
