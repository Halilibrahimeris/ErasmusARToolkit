using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// Where the phone starts in a design, and the way it looks. Drawn like a camera in the Scene view;
    /// it renders nothing. Put it where a visitor would stand, at eye height (about 1.5 m).
    /// </summary>
    [DisallowMultipleComponent, AddComponentMenu("Erasmus AR Toolkit/Design Start Point")]
    public sealed class DesignStartPoint : MonoBehaviour
    {
#if UNITY_EDITOR
        static readonly Color GizmoColor = new Color(1f, 0.8f, 0f, 1f);

        void OnDrawGizmos()
        {
            var flat = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            Gizmos.color = GizmoColor;
            Gizmos.matrix = Matrix4x4.TRS(transform.position, flat, Vector3.one);
            Gizmos.DrawFrustum(Vector3.zero, 55f, 0.7f, 0.05f, 0.56f);
            Gizmos.DrawWireSphere(Vector3.zero, 0.08f);
            Gizmos.matrix = Matrix4x4.identity;

            // A line down to the floor of the design shows the eye height.
            var parent = transform.parent;
            var floorY = parent != null ? parent.position.y : 0f;
            Gizmos.DrawLine(transform.position, new Vector3(transform.position.x, floorY, transform.position.z));
        }
#endif
    }
}
