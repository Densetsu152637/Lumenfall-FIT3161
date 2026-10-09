using UnityEngine;
using UnityEngine.InputSystem;
using Lumenfall.Quests;

namespace Lumenfall.Accessibility
{
    /// <summary>Accessibility shortcut into the same Main/Graphics settings used by the pause menu and journal.</summary>
    public sealed class AccessibilitySettingsOverlay : MonoBehaviour
    {
        public static bool IsOpen => QuestHintOverlay.IsSettingsOpen;
        public static bool InputBlocked => IsOpen || QuestHintOverlay.SettingsClosedFrame == Time.frameCount;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindAnyObjectByType<AccessibilitySettingsOverlay>() == null)
                new GameObject("[AccessibilitySettingsOverlay]").AddComponent<AccessibilitySettingsOverlay>();
        }
        private void Awake() { DontDestroyOnLoad(gameObject); }
        private void Update()
        {
            if (Keyboard.current?.f10Key.wasPressedThisFrame == true || Gamepad.current?.startButton.wasPressedThisFrame == true)
            { if (IsOpen) Close(); else Open(); }
        }
        public void Open() => QuestHintOverlay.OpenSettings();
        public void Close() => QuestHintOverlay.CloseSettings();
    }
}
