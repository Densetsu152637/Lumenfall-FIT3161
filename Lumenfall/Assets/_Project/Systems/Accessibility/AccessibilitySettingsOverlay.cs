using UnityEngine;
using Lumenfall.Quests;

namespace Lumenfall.Accessibility
{
    /// <summary>Opens and closes the shared Settings pop-out, where the accessibility options live, and reports whether it blocks input.</summary>
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
        public void Open() => QuestHintOverlay.OpenSettings();
        public void Close() => QuestHintOverlay.CloseSettings();
    }
}
