using UnityEngine;
using UnityEngine.InputSystem;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// Orbit camera for Desktop mode: right-drag orbits, middle-drag pans, scroll zooms,
    /// WASD moves the pivot, F resets the view. Project modules can switch it to walk mode (a person
    /// walking through a design): WASD walks, right-drag looks around, F goes back to the start.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class DesktopCameraController : MonoBehaviour
    {
        [SerializeField] Vector3 pivot = Vector3.zero;
        [SerializeField] float yaw;
        [SerializeField] float pitch = 32f;
        [SerializeField] float distance = 1.6f;

        [Header("Limits")]
        [SerializeField] Vector2 pitchLimits = new Vector2(5f, 85f);
        [SerializeField] Vector2 distanceLimits = new Vector2(0.4f, 12f);

        [Header("Speeds")]
        [SerializeField] float orbitDegreesPerPixel = 0.25f;
        [SerializeField] float panMetersPerPixel = 0.002f;
        [SerializeField] float zoomStep = 0.12f;
        [SerializeField] float moveSpeed = 1.5f;
        [SerializeField, Tooltip("Walk mode: meters per second (Shift walks twice as fast).")]
        float walkSpeed = 1.4f;

        Vector3 startPivot;
        float startYaw;
        float startPitch;
        float startDistance;
        bool walking;
        Vector3 walkStart;
        float walkStartYaw;
        float walkPitch;

        /// <summary>True while the camera walks like a person instead of orbiting.</summary>
        public bool IsWalking => walking;

        /// <summary>
        /// Walk mode: the camera stands at <paramref name="eyePosition"/> looking toward <paramref name="yawDegrees"/>.
        /// </summary>
        public void EnterWalkMode(Vector3 eyePosition, float yawDegrees)
        {
            walking = true;
            walkStart = eyePosition;
            walkStartYaw = yawDegrees;
            yaw = yawDegrees;
            walkPitch = 0f;
            transform.SetPositionAndRotation(eyePosition, Quaternion.Euler(0f, yawDegrees, 0f));
        }

        /// <summary>Back to the orbit camera.</summary>
        public void ExitWalkMode()
        {
            if (!walking)
            {
                return;
            }

            walking = false;
            yaw = startYaw;
            ApplyTransform();
        }

        void Awake()
        {
            startPivot = pivot;
            startYaw = yaw;
            startPitch = pitch;
            startDistance = distance;
            ApplyTransform();
        }

        void LateUpdate()
        {
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            if (walking)
            {
                Walk(mouse, keyboard);
                return;
            }

            if (mouse != null)
            {
                HandleMouse(mouse, keyboard);
            }

            if (keyboard != null)
            {
                HandleKeyboard(keyboard);
            }

            ApplyTransform();
        }

        void HandleMouse(Mouse mouse, Keyboard keyboard)
        {
            var delta = mouse.delta.ReadValue();
            if (mouse.rightButton.isPressed)
            {
                yaw += delta.x * orbitDegreesPerPixel;
                pitch = Mathf.Clamp(pitch - delta.y * orbitDegreesPerPixel, pitchLimits.x, pitchLimits.y);
            }

            if (mouse.middleButton.isPressed)
            {
                var scale = panMetersPerPixel * distance;
                pivot -= (transform.right * delta.x + FlatForward() * delta.y) * scale;
            }

            var shiftHeld = keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
            var scroll = mouse.scroll.ReadValue().y;
            var overUI = UIPointerGuard.IsOverUI(mouse.position.ReadValue());
            if (!shiftHeld && !overUI && Mathf.Abs(scroll) > 0.01f)
            {
                var factor = scroll > 0f ? 1f - zoomStep : 1f + zoomStep;
                distance = Mathf.Clamp(distance * factor, distanceLimits.x, distanceLimits.y);
            }
        }

        void HandleKeyboard(Keyboard keyboard)
        {
            if (keyboard.fKey.wasPressedThisFrame)
            {
                pivot = startPivot;
                yaw = startYaw;
                pitch = startPitch;
                distance = startDistance;
                return;
            }

            var input = new Vector2(
                (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f),
                (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f));
            if (input.sqrMagnitude > 0f)
            {
                var move = transform.right * input.x + FlatForward() * input.y;
                move.y = 0f;
                pivot += move.normalized * (moveSpeed * Time.deltaTime);
            }
        }

        void Walk(Mouse mouse, Keyboard keyboard)
        {
            if (mouse != null && mouse.rightButton.isPressed)
            {
                var delta = mouse.delta.ReadValue();
                yaw += delta.x * orbitDegreesPerPixel;
                walkPitch = Mathf.Clamp(walkPitch - delta.y * orbitDegreesPerPixel, -80f, 80f);
            }

            var position = transform.position;
            if (keyboard != null)
            {
                if (keyboard.fKey.wasPressedThisFrame)
                {
                    position = walkStart;
                    yaw = walkStartYaw;
                    walkPitch = 0f;
                }

                var input = new Vector2(
                    (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f),
                    (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f));
                if (input.sqrMagnitude > 0f)
                {
                    var heading = Quaternion.Euler(0f, yaw, 0f);
                    var move = heading * new Vector3(input.x, 0f, input.y);
                    var fast = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
                    position += move.normalized * (walkSpeed * (fast ? 2f : 1f) * Time.deltaTime);
                }
            }

            transform.SetPositionAndRotation(position, Quaternion.Euler(walkPitch, yaw, 0f));
        }

        Vector3 FlatForward()
        {
            var forward = transform.forward;
            forward.y = 0f;
            return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        }

        void ApplyTransform()
        {
            transform.position = OrbitMath.ComputePosition(pivot, yaw, pitch, distance);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }
    }
}
