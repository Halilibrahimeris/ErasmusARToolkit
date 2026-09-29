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
            transform.rotation = Quaternion.AngleAxis(degrees, UpAxis) * transform.rotation;
        }

        /// <summary>Moves the object up (positive) or down along the surface normal.</summary>
        public void Raise(float meters)
        {
            transform.position += UpAxis * meters;
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
