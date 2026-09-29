using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace Erasmus.ARToolkit
{
    /// <summary>Reads touch, mouse and keyboard through the Input System into a <see cref="GestureFrame"/>.</summary>
    public sealed class GestureReader
    {
        bool enabled;

        public void Enable()
        {
            if (!enabled)
            {
                EnhancedTouchSupport.Enable();
                enabled = true;
            }
        }

        public void Disable()
        {
            if (enabled)
            {
                EnhancedTouchSupport.Disable();
                enabled = false;
            }
        }

        public GestureFrame Read()
        {
            var keyboard = Keyboard.current;
            var shift = keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
            var rotate = keyboard == null ? 0f : Axis(keyboard.qKey.isPressed, keyboard.eKey.isPressed);
            var delete = keyboard != null
                         && (keyboard.deleteKey.wasPressedThisFrame || keyboard.backspaceKey.wasPressedThisFrame);

            if (enabled && Touch.activeTouches.Count > 0)
            {
                return ReadTouches(shift, rotate, delete);
            }

            return ReadMouse(shift, rotate, delete);
        }

        static GestureFrame ReadTouches(bool shift, float rotate, bool delete)
        {
            var touches = Touch.activeTouches;
            var first = touches[0];
            var second = touches.Count > 1 ? touches[1].screenPosition : first.screenPosition;
            var released = first.phase == TouchPhase.Ended || first.phase == TouchPhase.Canceled;
            return new GestureFrame(touches.Count, first.screenPosition, second, first.phase == TouchPhase.Began,
                released, 0f, shift, rotate, delete, true);
        }

        static GestureFrame ReadMouse(bool shift, float rotate, bool delete)
        {
            var mouse = Mouse.current;
            if (mouse == null)
            {
                return new GestureFrame(0, Vector2.zero, Vector2.zero, false, false, 0f, shift, rotate, delete, false);
            }

            var left = mouse.leftButton;
            var involved = left.isPressed || left.wasPressedThisFrame || left.wasReleasedThisFrame;
            var position = mouse.position.ReadValue();
            var scroll = mouse.scroll.ReadValue().y;
            var steps = Mathf.Abs(scroll) > 0.01f ? Mathf.Sign(scroll) : 0f;
            return new GestureFrame(involved ? 1 : 0, position, position, left.wasPressedThisFrame,
                left.wasReleasedThisFrame, steps, shift, rotate, delete, false);
        }

        static float Axis(bool negative, bool positive)
        {
            return (positive ? 1f : 0f) - (negative ? 1f : 0f);
        }
    }
}
