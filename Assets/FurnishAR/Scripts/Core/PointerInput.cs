using UnityEngine;
using UnityEngine.InputSystem;

namespace FurnishAR.Core
{
    /// <summary>
    /// One device-independent pointer source for the whole app.
    ///
    /// WHY THIS EXISTS:
    /// The first device builds drove placement through
    /// <c>UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches</c>. That API is
    /// fragile: it only reports touches after <c>EnhancedTouchSupport.Enable()</c> has
    /// run AND stays enabled (any manager calling <c>Disable()</c> silently kills it
    /// for everybody). On at least one Android build UI taps worked through the
    /// EventSystem while placement never saw a single touch.
    ///
    /// <see cref="Pointer.current"/> is the canonical Input System pointer and covers
    /// real touchscreens, styli and the mouse (editor). We additionally probe
    /// <see cref="Touchscreen.current"/> directly, so a device that exposes one path
    /// but not the other still works.
    /// </summary>
    public static class PointerInput
    {
        /// <summary>True on the frame a new press (finger down / mouse down) begins.</summary>
        public static bool TryGetPress(out Vector2 screenPosition)
        {
            screenPosition = Vector2.zero;

            // 1) Canonical pointer: Touchscreen on Android, Mouse in the Editor.
            Pointer pointer = Pointer.current;
            if (pointer != null && pointer.press.wasPressedThisFrame)
            {
                screenPosition = pointer.position.ReadValue();
                return true;
            }

            // 2) Direct touchscreen probe (some devices only surface touches here).
            Touchscreen touchscreen = Touchscreen.current;
            if (touchscreen != null)
            {
                var primary = touchscreen.primaryTouch;
                if (primary.press.wasPressedThisFrame)
                {
                    screenPosition = primary.position.ReadValue();
                    return true;
                }
            }

            return false;
        }

        /// <summary>True on the frame the press ends.</summary>
        public static bool TryGetRelease(out Vector2 screenPosition)
        {
            screenPosition = Vector2.zero;

            Pointer pointer = Pointer.current;
            if (pointer != null && pointer.press.wasReleasedThisFrame)
            {
                screenPosition = pointer.position.ReadValue();
                return true;
            }

            Touchscreen touchscreen = Touchscreen.current;
            if (touchscreen != null)
            {
                var primary = touchscreen.primaryTouch;
                if (primary.press.wasReleasedThisFrame)
                {
                    screenPosition = primary.position.ReadValue();
                    return true;
                }
            }

            return false;
        }

        /// <summary>Current pointer position while a press is held.</summary>
        public static bool TryGetHoldPosition(out Vector2 screenPosition)
        {
            screenPosition = Vector2.zero;

            Pointer pointer = Pointer.current;
            if (pointer != null && pointer.press.isPressed)
            {
                screenPosition = pointer.position.ReadValue();
                return true;
            }

            Touchscreen touchscreen = Touchscreen.current;
            if (touchscreen != null)
            {
                var primary = touchscreen.primaryTouch;
                if (primary.press.isPressed)
                {
                    screenPosition = primary.position.ReadValue();
                    return true;
                }
            }

            return false;
        }

        /// <summary>Number of fingers currently on the glass (0 when using a mouse).</summary>
        public static int ActiveFingerCount
        {
            get
            {
                Touchscreen touchscreen = Touchscreen.current;
                if (touchscreen == null) return 0;

                int count = 0;
                var touches = touchscreen.touches;
                for (int i = 0; i < touches.Count; i++)
                {
                    if (touches[i].press.isPressed) count++;
                }
                return count;
            }
        }

        /// <summary>Screen position of the n-th active finger, if available.</summary>
        public static bool TryGetFingerPosition(int index, out Vector2 screenPosition)
        {
            screenPosition = Vector2.zero;

            Touchscreen touchscreen = Touchscreen.current;
            if (touchscreen == null) return false;

            var touches = touchscreen.touches;
            int seen = 0;
            for (int i = 0; i < touches.Count; i++)
            {
                var touch = touches[i];
                if (!touch.press.isPressed) continue;
                if (seen == index)
                {
                    screenPosition = touch.position.ReadValue();
                    return true;
                }
                seen++;
            }

            return false;
        }
    }
}
