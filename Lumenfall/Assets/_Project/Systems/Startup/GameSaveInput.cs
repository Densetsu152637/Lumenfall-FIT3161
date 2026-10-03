using UnityEngine;
using UnityEngine.InputSystem;

namespace Lumenfall.Startup
{
    /// Input adapter lives in Runtime; the save presenter remains in GameEvents.
    public sealed class GameSaveInput : MonoBehaviour
    {
        private readonly UiNavigationInput navigation = new UiNavigationInput();
        private Lumenfall.GameEvents.GameSaveOverlay overlay;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var view = FindAnyObjectByType<Lumenfall.GameEvents.GameSaveOverlay>();
            if (view != null && view.GetComponent<GameSaveInput>() == null) view.gameObject.AddComponent<GameSaveInput>();
        }
        private void Awake() { overlay = GetComponent<Lumenfall.GameEvents.GameSaveOverlay>(); }
        private void Update()
        {
            if (overlay == null || !overlay.isActiveAndEnabled) return;
            var key = Keyboard.current; var pad = Gamepad.current;
            if (overlay.IsOpen)
            {
                navigation.Read();
                overlay.HandleNavigation(navigation.Vertical, navigation.Submit, navigation.Cancel);
            }
            else if (!Lumenfall.GameEvents.OverlayInputGate.IsBlocked)
            {
                if (key?.f5Key.wasPressedThisFrame == true) overlay.SaveCurrent();
                if (key?.f9Key.wasPressedThisFrame == true || pad?.startButton.wasPressedThisFrame == true) overlay.TogglePanel();
            }
        }
    }
}
