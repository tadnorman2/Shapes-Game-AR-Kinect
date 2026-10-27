using UnityEngine;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

namespace ShapesKinect.Game
{
    /// <summary>
    /// Keyboard, gamepad and mouse, through whichever of Unity's input systems the project uses.
    /// Gamepad buttons are named by position: South is A on Xbox pads, West is X, North is Y.
    /// </summary>
    public static class Controls
    {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        /// <summary>Left stick, WASD or arrow keys; up is away from the Kinect.</summary>
        public static Vector2 Move
        {
            get
            {
                var move = Vector2.zero;
                var keyboard = Keyboard.current;
                if (keyboard != null)
                {
                    if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) move.x -= 1f;
                    if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) move.x += 1f;
                    if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) move.y -= 1f;
                    if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) move.y += 1f;
                }
                var pad = Gamepad.current;
                if (pad != null) move += pad.leftStick.ReadValue();
                return Vector2.ClampMagnitude(move, 1f);
            }
        }

        public static bool Confirm =>
            Pressed(Keyboard.current?.spaceKey) || Pressed(Keyboard.current?.enterKey) || Pressed(Gamepad.current?.buttonSouth);
        public static bool FindTableAgain => Pressed(Keyboard.current?.rKey) || Pressed(Gamepad.current?.buttonNorth);
        public static bool SwitchControl => Pressed(Keyboard.current?.tabKey) || Pressed(Gamepad.current?.buttonWest);
        public static bool SwitchSource => Pressed(Keyboard.current?.kKey);
        public static bool SwitchView => Pressed(Keyboard.current?.cKey);
        public static bool Quit => Pressed(Keyboard.current?.escapeKey);

        /// <summary>Which number key (1-9) went down this frame, as 0-8, or -1.</summary>
        public static int NumberKey
        {
            get
            {
                var keyboard = Keyboard.current;
                if (keyboard == null) return -1;
                if (keyboard.digit1Key.wasPressedThisFrame) return 0;
                if (keyboard.digit2Key.wasPressedThisFrame) return 1;
                if (keyboard.digit3Key.wasPressedThisFrame) return 2;
                return -1;
            }
        }

        public static Vector2? Pointer => Mouse.current == null ? (Vector2?)null : Mouse.current.position.ReadValue();

        private static bool Pressed(UnityEngine.InputSystem.Controls.ButtonControl button) =>
            button != null && button.wasPressedThisFrame;
#else
        /// <summary>Left stick, WASD or arrow keys; up is away from the Kinect.</summary>
        public static Vector2 Move =>
            Vector2.ClampMagnitude(new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")), 1f);

        public static bool Confirm =>
            Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.JoystickButton0);
        public static bool FindTableAgain => Input.GetKeyDown(KeyCode.R) || Input.GetKeyDown(KeyCode.JoystickButton3);
        public static bool SwitchControl => Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.JoystickButton2);
        public static bool SwitchSource => Input.GetKeyDown(KeyCode.K);
        public static bool SwitchView => Input.GetKeyDown(KeyCode.C);
        public static bool Quit => Input.GetKeyDown(KeyCode.Escape);

        /// <summary>Which number key (1-9) went down this frame, as 0-8, or -1.</summary>
        public static int NumberKey
        {
            get
            {
                for (var i = 0; i < 9; i++)
                {
                    if (Input.GetKeyDown(KeyCode.Alpha1 + i)) return i;
                }
                return -1;
            }
        }

        public static Vector2? Pointer => Input.mousePresent ? (Vector2?)Input.mousePosition : null;
#endif
    }
}
