using UnityEngine;
using UnityEngine.InputSystem;

namespace Lumenfall.Accessibility
{
    /// <summary>Discrete menu navigation. Hosts may instead feed their production UI action map to the view.</summary>
    public sealed class AccessibilityMenuInput
    {
        private float nextRepeat;
        private Vector2Int held;
        public bool Cancel { get; private set; }
        public bool Submit { get; private set; }
        public Vector2Int Direction { get; private set; }
        public void Poll()
        {
            Keyboard keyboard = Keyboard.current;
            Gamepad pad = Gamepad.current;
            Cancel = keyboard?.escapeKey.wasPressedThisFrame == true || pad?.buttonEast.wasPressedThisFrame == true;
            Submit = keyboard?.enterKey.wasPressedThisFrame == true || keyboard?.spaceKey.wasPressedThisFrame == true || pad?.buttonSouth.wasPressedThisFrame == true;
            int x = keyboard?.leftArrowKey.wasPressedThisFrame == true ? -1 : keyboard?.rightArrowKey.wasPressedThisFrame == true ? 1 : 0;
            int y = keyboard?.upArrowKey.wasPressedThisFrame == true ? -1 : keyboard?.downArrowKey.wasPressedThisFrame == true ? 1 : 0;
            if (keyboard?.tabKey.wasPressedThisFrame == true) y = keyboard.shiftKey.isPressed ? -1 : 1;
            Vector2 stick = pad != null ? pad.dpad.ReadValue() + pad.leftStick.ReadValue() : Vector2.zero;
            Vector2Int current = new Vector2Int(Mathf.Abs(stick.x) > 0.55f ? (int)Mathf.Sign(stick.x) : 0,
                Mathf.Abs(stick.y) > 0.55f ? -(int)Mathf.Sign(stick.y) : 0);
            if (current != Vector2Int.zero && (current != held || Time.unscaledTime >= nextRepeat))
            {
                x = current.x; y = current.y; nextRepeat = Time.unscaledTime + 0.25f;
            }
            held = current;
            Direction = new Vector2Int(x, y);
        }
        public static void ConsumeGuiNavigation()
        {
            Event e = Event.current;
            if (e.type != EventType.KeyDown) return;
            if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.Space || e.keyCode == KeyCode.Tab || e.keyCode == KeyCode.Escape ||
                e.keyCode == KeyCode.UpArrow || e.keyCode == KeyCode.DownArrow || e.keyCode == KeyCode.LeftArrow || e.keyCode == KeyCode.RightArrow) e.Use();
        }
    }
}
