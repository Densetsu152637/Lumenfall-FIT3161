using UnityEngine;
using UnityEngine.InputSystem;

namespace Lumenfall.Startup
{
    /// Uses existing InputSystem devices, with edge-triggered stick navigation while paused.
    public sealed class UiNavigationInput
    {
        private int previousVertical;
        private int previousHorizontal;
        public int Vertical { get; private set; }
        public int Horizontal { get; private set; }
        public bool Submit { get; private set; }
        public bool Cancel { get; private set; }
        public void Read()
        {
            Keyboard key = Keyboard.current;
            Gamepad pad = Gamepad.current;
            Vector2 axis = pad == null ? Vector2.zero : pad.dpad.ReadValue() + pad.leftStick.ReadValue();
            int vertical = Mathf.Abs(axis.y) > 0.55f ? (axis.y > 0f ? -1 : 1) : 0;
            int horizontal = Mathf.Abs(axis.x) > 0.55f ? (axis.x > 0f ? 1 : -1) : 0;
            Vertical = vertical != previousVertical ? vertical : 0;
            Horizontal = horizontal != previousHorizontal ? horizontal : 0;
            previousVertical = vertical; previousHorizontal = horizontal;
            if (key?.upArrowKey.wasPressedThisFrame == true) Vertical = -1;
            if (key?.downArrowKey.wasPressedThisFrame == true) Vertical = 1;
            if (key?.leftArrowKey.wasPressedThisFrame == true) Horizontal = -1;
            if (key?.rightArrowKey.wasPressedThisFrame == true) Horizontal = 1;
            Submit = key?.enterKey.wasPressedThisFrame == true || key?.spaceKey.wasPressedThisFrame == true || pad?.buttonSouth.wasPressedThisFrame == true;
            Cancel = key?.escapeKey.wasPressedThisFrame == true || pad?.buttonEast.wasPressedThisFrame == true;
        }
    }

}
