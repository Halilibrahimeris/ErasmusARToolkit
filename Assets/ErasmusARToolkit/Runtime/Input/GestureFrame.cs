using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>One frame of pointer and keyboard input, the same shape for touch and mouse.</summary>
    public readonly struct GestureFrame
    {
        public GestureFrame(int pointerCount, Vector2 primary, Vector2 secondary, bool pressedThisFrame,
            bool releasedThisFrame, float scrollSteps, bool modifierHeld, float rotateAxis, bool deletePressed,
            bool isTouch)
        {
            PointerCount = pointerCount;
            Primary = primary;
            Secondary = secondary;
            PressedThisFrame = pressedThisFrame;
            ReleasedThisFrame = releasedThisFrame;
            ScrollSteps = scrollSteps;
            ModifierHeld = modifierHeld;
            RotateAxis = rotateAxis;
            DeletePressed = deletePressed;
            IsTouch = isTouch;
        }

        /// <summary>Fingers on screen, or 1 while the left mouse button is involved.</summary>
        public int PointerCount { get; }
        public Vector2 Primary { get; }
        public Vector2 Secondary { get; }
        public bool PressedThisFrame { get; }
        public bool ReleasedThisFrame { get; }

        /// <summary>Mouse wheel notches this frame: -1, 0 or 1.</summary>
        public float ScrollSteps { get; }

        /// <summary>Shift is held (switches the mouse wheel from zoom to scale).</summary>
        public bool ModifierHeld { get; }

        /// <summary>-1 (Q), 0 or 1 (E).</summary>
        public float RotateAxis { get; }

        public bool DeletePressed { get; }
        public bool IsTouch { get; }
        public bool IsHeld => PointerCount > 0 && !ReleasedThisFrame;
    }
}
