using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Lumenfall.GridMovement;
using Lumenfall.GameEvents;
using Lumenfall.Inventory;
using Lumenfall.Startup;

namespace Lumenfall.Accessibility
{
    /// <summary>Independent baseline launcher. At merge, production UI should host the view through its overlay gate.</summary>
    public sealed class AccessibilitySettingsOverlay : MonoBehaviour
    {
        public static bool IsOpen { get; private set; }
        private static int closedFrame = -1;
        public static bool InputBlocked => IsOpen || Time.frameCount <= closedFrame;
        private readonly AccessibilitySettingsView view = new AccessibilitySettingsView();
        private readonly AccessibilityMenuInput input = new AccessibilityMenuInput();
        private readonly List<Behaviour> suspended = new List<Behaviour>();
        private float previousTimeScale;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntime() { IsOpen = false; closedFrame = -1; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindAnyObjectByType<AccessibilitySettingsOverlay>() != null) return;
            new GameObject("[AccessibilitySettingsOverlay]").AddComponent<AccessibilitySettingsOverlay>();
        }
        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            view.AudioAdjustRequested += StartupMenu.AdjustMasterVolume;
            view.AudioResetRequested += StartupMenu.ResetMasterVolume;
        }
        private void OnDisable() => Close();
        private void Update()
        {
            bool toggle = Keyboard.current?.f10Key.wasPressedThisFrame == true || Gamepad.current?.startButton.wasPressedThisFrame == true;
            if (toggle) { if (IsOpen) Close(); else Open(); return; }
            if (!IsOpen) return;
            input.Poll();
            if (input.Cancel) { Close(); return; }
            view.HandleNavigation(input.Direction.y, input.Direction.x, input.Submit);
        }
        public void Open()
        {
            if (IsOpen || Time.timeScale <= 0f || InventoryUiState.IsOpen || FindAnyObjectByType<StartupMenu>() != null) return;
            // Pause dialogue presentation without disabling it or releasing its movement lock.
            IsOpen = true;
            previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            Suspend(FindObjectsByType<GridPlayerInput>());
            Suspend(FindObjectsByType<InventoryEncyclopediaUI>());
            Suspend(FindObjectsByType<GameSaveOverlay>());
            view.ResetFocus();
        }
        private void Suspend<T>(T[] components) where T : Behaviour
        {
            foreach (T component in components)
            {
                if (!component.enabled) continue;
                suspended.Add(component);
                component.enabled = false;
            }
        }
        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            closedFrame = Time.frameCount;
            Time.timeScale = previousTimeScale;
            foreach (Behaviour component in suspended) if (component != null) component.enabled = true;
            suspended.Clear();
        }
        private void OnGUI()
        {
            if (!IsOpen)
            {
                if (Time.timeScale <= 0f || InventoryUiState.IsOpen || GameEventManager.Instance?.Runtime?.ActiveDialogue != null) return;
                GUIStyle label = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(16f * AccessibilitySettings.Current.TextScale) };
                if (GUI.Button(new Rect(12f, 12f, 210f, 48f), Localization.Resolve("settings.open", "Settings (F10)"), label)) Open();
                return;
            }
            AccessibilityMenuInput.ConsumeGuiNavigation();
            float width = Mathf.Min(Screen.width - 24f, 680f);
            float height = Mathf.Min(Screen.height - 24f, 760f);
            Rect panel = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
            if (view.Draw(panel, StartupMenu.DrawMasterVolume)) Close();
        }
    }
}
