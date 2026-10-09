using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Lumenfall.Controls
{
    /// <summary>
    /// Shared IMGUI settings content (master volume and key rebinding) used by both the main menu and the journal.
    /// The host draws it inside its own GUILayout area and calls <see cref="Tick"/> from Update to capture rebinds.
    /// </summary>
    public sealed class GameSettingsPanel
    {
        private const string MasterVolumeKey = "lumenfall.master-volume";
        private GameAction? rebinding;
        private Vector2 scroll;

        public string Message { get; set; }
        public bool IsRebinding => rebinding.HasValue;

        public static float MasterVolume
        {
            get => Mathf.Clamp01(PlayerPrefs.GetFloat(MasterVolumeKey, 1f));
            set
            {
                float volume = Mathf.Clamp01(value);
                AudioListener.volume = volume;
                PlayerPrefs.SetFloat(MasterVolumeKey, volume);
                PlayerPrefs.Save();
            }
        }

        public static void ApplySavedVolume() { AudioListener.volume = MasterVolume; }

        public void DrawVolume(GUIStyle labelStyle)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Master volume", labelStyle, GUILayout.Width(180f));
            float current = MasterVolume;
            float next = GUILayout.HorizontalSlider(current, 0f, 1f, GUILayout.ExpandWidth(true), GUILayout.Height(24f));
            GUILayout.Label(Mathf.RoundToInt(next * 100f) + "%", labelStyle, GUILayout.Width(60f));
            GUILayout.EndHorizontal();
            if (!Mathf.Approximately(next, current)) MasterVolume = next;
        }

        public void DrawControls(GUIStyle labelStyle, GUIStyle keyStyle, GUIStyle messageStyle, float listHeight)
        {
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(listHeight));
            foreach (GameAction action in KeyBindings.AllActions)
            {
                GUILayout.BeginHorizontal(GUILayout.Height(32f));
                GUILayout.Label(KeyBindings.GetDisplayName(action), labelStyle, GUILayout.ExpandWidth(true));
                string keyText = rebinding == action ? "Press a key..." : KeyBindings.GetKeyLabel(action);
                if (GUILayout.Button(keyText, keyStyle, GUILayout.Width(180f), GUILayout.Height(28f)))
                {
                    rebinding = action;
                    Message = "Press a key for " + KeyBindings.GetDisplayName(action) + ". Esc cancels.";
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset controls to defaults", keyStyle, GUILayout.Width(260f), GUILayout.Height(30f)))
            {
                KeyBindings.ResetToDefaults();
                rebinding = null;
                Message = "Controls reset to defaults.";
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(Message)) GUILayout.Label(Message, messageStyle);
        }

        /// <summary>Captures the next key press while an action is waiting to be rebound.</summary>
        public void Tick()
        {
            Keyboard keyboard = Keyboard.current;
            if (!rebinding.HasValue || keyboard == null) return;
            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                rebinding = null;
                Message = "Rebinding cancelled.";
                return;
            }

            foreach (KeyControl control in keyboard.allKeys)
            {
                if (control == null || !control.wasPressedThisFrame) continue;
                if (!KeyBindings.IsBindable(control.keyCode))
                {
                    Message = "Arrow keys are reserved for movement. Pick another key.";
                    return;
                }

                GameAction action = rebinding.Value;
                GameAction? swapped = KeyBindings.SetKey(action, control.keyCode);
                Message = KeyBindings.GetDisplayName(action) + " set to " + KeyBindings.GetKeyLabel(action) +
                    (swapped.HasValue ? "; " + KeyBindings.GetDisplayName(swapped.Value) + " moved to " + KeyBindings.GetKeyLabel(swapped.Value) + "." : ".");
                rebinding = null;
                return;
            }
        }

        public void CancelRebind() { rebinding = null; }
    }
}
