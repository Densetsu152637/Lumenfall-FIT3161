using UnityEngine;
using UnityEngine.InputSystem;
using Lumenfall.GameEvents;

namespace Lumenfall.Startup
{
    /// <summary>Save shortcuts share the menu's save authority; Start belongs to settings accessibility.</summary>
    public sealed class GameSaveInput : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var view = FindAnyObjectByType<GameSaveOverlay>();
            if (view != null && view.GetComponent<GameSaveInput>() == null) view.gameObject.AddComponent<GameSaveInput>();
        }
        private void Update()
        {
            if (OverlayInputGate.IsBlocked || Time.timeScale <= 0f) return;
            if (Keyboard.current?.f5Key.wasPressedThisFrame == true) GameAutosave.SaveNow();
            else if (Keyboard.current?.f9Key.wasPressedThisFrame == true) StartupMenu.ReturnToMainMenu();
        }
    }
}
