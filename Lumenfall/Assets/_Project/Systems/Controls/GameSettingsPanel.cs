using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using Lumenfall.Audio;
using Lumenfall.Startup;

namespace Lumenfall.Controls
{
    /// <summary>
    /// Shared player preferences (volume, display, motion) and the IMGUI key-rebinding list used by the main menu, the
    /// Settings pop-out and the journal. Hosts draw the list inside their own GUILayout area and call <see cref="Tick"/>
    /// from Update to capture rebinds and swap confirmations.
    /// </summary>
    public sealed class GameSettingsPanel
    {
        private const string ReduceMotionKey = "lumenfall.reduce-motion";

        // The rebinding list is grouped; the seven spell slots fold into one row because they rarely change.
        private static readonly GameAction[] MovementActions =
            { GameAction.MoveUp, GameAction.MoveDown, GameAction.MoveLeft, GameAction.MoveRight, GameAction.Sprint };
        private static readonly GameAction[] SpellActions = { GameAction.SpellWheel };
        private static readonly GameAction[] SpellSlotActions =
            { GameAction.Spell1, GameAction.Spell2, GameAction.Spell3, GameAction.Spell4, GameAction.Spell5, GameAction.Spell6, GameAction.Spell7 };
        private static readonly GameAction[] InterfaceActions = { GameAction.Talk, GameAction.Interact, GameAction.Journal };

        private static int reduceMotionCache = -1;
        private static int windowedWidth;
        private static int windowedHeight;

        private GameAction? rebinding;
        // A key that is already in use waits here until the player confirms or cancels the swap.
        private GameAction? pendingAction;
        private Key pendingKey;
        private GameAction pendingOther;
        private bool spellSlotsOpen;
        private Vector2 scroll;
        private GUIStyle fallbackHeading;

        public string Message { get; set; }
        /// <summary>Optional hover highlight; called on repaint with the rect of the row under the pointer.</summary>
        public System.Action<Rect> RowHover { get; set; }
        public bool IsRebinding => rebinding.HasValue || pendingAction.HasValue;

        public static float MasterVolume
        {
            get => AudioMixSettings.Master;
            set
            {
                AudioMixSettings.SetVolume(AudioBus.Master, value);
                AudioListener.volume = AudioMixSettings.Master;
            }
        }

        /// <summary>
        /// Unity remembers the window mode itself, so this is not stored in PlayerPrefs. Entering fullscreen explicitly
        /// adopts the monitor resolution instead of reusing a stretched window's dimensions; leaving restores that window.
        /// No effect in the editor.
        /// </summary>
        public static bool Fullscreen
        {
            get => Screen.fullScreen;
            set
            {
                if (Application.isEditor || Screen.fullScreen == value) return;

                if (value)
                {
                    windowedWidth = Screen.width;
                    windowedHeight = Screen.height;
                    Resolution display = Screen.currentResolution;
                    Screen.SetResolution(display.width, display.height, FullScreenMode.FullScreenWindow,
                        display.refreshRateRatio);
                    return;
                }

                if (windowedWidth > 0 && windowedHeight > 0)
                    Screen.SetResolution(windowedWidth, windowedHeight, FullScreenMode.Windowed);
                else
                    Screen.fullScreen = false;
            }
        }

        public static bool VSync
        {
            get => GraphicsOptions.Instance != null ? GraphicsOptions.Instance.Preferences.VSync : GraphicsPreferences.Load().VSync;
            set
            {
                GraphicsOptions graphics = GraphicsOptions.Instance;
                GraphicsPreferences current = graphics != null ? graphics.Preferences : GraphicsPreferences.Load();
                GraphicsPreferences next = new GraphicsPreferences(value, current.Antialiasing, current.AmbientOcclusion, current.Shading);
                if (graphics != null) graphics.SetPreferences(next);
                else { next.Save(); QualitySettings.vSyncCount = value ? 1 : 0; }
            }
        }

        /// <summary>Turns off decorative menu motion: drifting motes, parallax, camera drift, the wand trail and panel rises.</summary>
        public static bool ReduceMotion
        {
            get
            {
                if (reduceMotionCache < 0) reduceMotionCache = PlayerPrefs.GetInt(ReduceMotionKey, 0);
                return reduceMotionCache != 0;
            }
            set
            {
                reduceMotionCache = value ? 1 : 0;
                PlayerPrefs.SetInt(ReduceMotionKey, reduceMotionCache);
                PlayerPrefs.Save();
            }
        }

        public static void ApplySavedVolume() { AudioListener.volume = MasterVolume; }

        /// <summary>
        /// GraphicsOptions owns VSync and applies it at startup. VSync used to be saved on its own under
        /// <c>lumenfall.vsync</c>; an earlier choice is carried into the graphics preferences once, then the old key is dropped.
        /// </summary>
        public static void ApplySavedDisplay()
        {
            const string LegacyVSyncKey = "lumenfall.vsync";
            if (!PlayerPrefs.HasKey(LegacyVSyncKey)) return;
            VSync = PlayerPrefs.GetInt(LegacyVSyncKey) != 0;
            PlayerPrefs.DeleteKey(LegacyVSyncKey);
            PlayerPrefs.Save();
        }

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

        /// <summary>
        /// Draws the grouped rebinding list (scrolling within <paramref name="listHeight"/>), then either the swap prompt or
        /// the reset button, then the message. Hosts reserve about 96 pixels below the list for those.
        /// </summary>
        public void DrawControls(GUIStyle labelStyle, GUIStyle keyStyle, GUIStyle messageStyle, float listHeight, GUIStyle headingStyle = null)
        {
            GUIStyle heading = headingStyle ?? FallbackHeading(labelStyle);
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(listHeight));
            DrawGroupHeading("Movement", heading, false);
            foreach (GameAction action in MovementActions) DrawRow(action, labelStyle, keyStyle);

            DrawGroupHeading("Spells", heading, true);
            foreach (GameAction action in SpellActions) DrawRow(action, labelStyle, keyStyle);
            GUILayout.BeginHorizontal(GUILayout.Height(32f));
            GUILayout.Label("Spell slots 1–7", labelStyle, GUILayout.ExpandWidth(true));
            if (GUILayout.Button(spellSlotsOpen ? "Hide slots" : "Show slots", keyStyle, GUILayout.Width(180f), GUILayout.Height(28f)))
                spellSlotsOpen = !spellSlotsOpen;
            GUILayout.EndHorizontal();
            HighlightLastRow();
            if (spellSlotsOpen || IsSpellSlot(rebinding) || IsSpellSlot(pendingAction))
                foreach (GameAction action in SpellSlotActions) DrawRow(action, labelStyle, keyStyle);

            DrawGroupHeading("Interface", heading, true);
            foreach (GameAction action in InterfaceActions) DrawRow(action, labelStyle, keyStyle);
            GUILayout.EndScrollView();

            if (pendingAction.HasValue)
            {
                if (!string.IsNullOrEmpty(Message)) GUILayout.Label(Message, messageStyle);
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Swap (Enter)", keyStyle, GUILayout.Width(170f), GUILayout.Height(30f))) ConfirmSwap();
                GUILayout.Space(12f);
                if (GUILayout.Button("Cancel (Esc)", keyStyle, GUILayout.Width(170f), GUILayout.Height(30f))) CancelSwap();
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                return;
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset controls to defaults", keyStyle, GUILayout.Width(260f), GUILayout.Height(30f)))
            {
                KeyBindings.ResetToDefaults();
                CancelRebind();
                Message = "Controls reset to defaults.";
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(Message)) GUILayout.Label(Message, messageStyle);
        }

        /// <summary>Captures the next key press while an action is waiting to be rebound, and Enter/Esc for a pending swap.</summary>
        public void Tick()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (pendingAction.HasValue)
            {
                if (keyboard.escapeKey.wasPressedThisFrame) CancelSwap();
                else if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame) ConfirmSwap();
                return;
            }
            if (!rebinding.HasValue) return;
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
                rebinding = null;
                Key key = control.keyCode;
                if (KeyBindings.GetKey(action) == key)
                {
                    Message = KeyBindings.GetDisplayName(action) + " stays on " + KeyBindings.KeyLabel(key) + ".";
                    return;
                }

                GameAction? other = KeyBindings.ActionUsing(key, action);
                if (other.HasValue)
                {
                    pendingAction = action;
                    pendingKey = key;
                    pendingOther = other.Value;
                    Message = KeyBindings.KeyLabel(key) + " is already used by " + KeyBindings.GetDisplayName(other.Value) +
                        ". Swap them, so " + KeyBindings.GetDisplayName(other.Value) + " moves to " + KeyBindings.GetKeyLabel(action) + "?";
                    return;
                }

                KeyBindings.SetKey(action, key);
                Message = KeyBindings.GetDisplayName(action) + " set to " + KeyBindings.GetKeyLabel(action) + ".";
                return;
            }
        }

        public void CancelRebind()
        {
            rebinding = null;
            pendingAction = null;
        }

        private void BeginRebind(GameAction action)
        {
            pendingAction = null;
            rebinding = action;
            Message = "Press a key for " + KeyBindings.GetDisplayName(action) + ". Esc cancels.";
        }

        private void ConfirmSwap()
        {
            if (!pendingAction.HasValue) return;
            GameAction action = pendingAction.Value;
            pendingAction = null;
            KeyBindings.SetKey(action, pendingKey);
            Message = KeyBindings.GetDisplayName(action) + " set to " + KeyBindings.GetKeyLabel(action) + "; " +
                KeyBindings.GetDisplayName(pendingOther) + " moved to " + KeyBindings.GetKeyLabel(pendingOther) + ".";
        }

        private void CancelSwap()
        {
            if (!pendingAction.HasValue) return;
            GameAction action = pendingAction.Value;
            pendingAction = null;
            Message = KeyBindings.GetDisplayName(action) + " stays on " + KeyBindings.GetKeyLabel(action) + ".";
        }

        private void DrawRow(GameAction action, GUIStyle labelStyle, GUIStyle keyStyle)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(32f));
            GUILayout.Label(KeyBindings.GetDisplayName(action), labelStyle, GUILayout.ExpandWidth(true));
            string keyText = rebinding == action ? "Press a key..."
                : pendingAction == action ? KeyBindings.KeyLabel(pendingKey) + "?"
                : KeyBindings.GetKeyLabel(action);
            if (GUILayout.Button(keyText, keyStyle, GUILayout.Width(180f), GUILayout.Height(28f))) BeginRebind(action);
            GUILayout.EndHorizontal();
            HighlightLastRow();
        }

        private void HighlightLastRow()
        {
            if (RowHover == null || Event.current.type != EventType.Repaint) return;
            Rect row = GUILayoutUtility.GetLastRect();
            if (!row.Contains(Event.current.mousePosition)) return;
            Color colour = GUI.color;
            RowHover(row);
            GUI.color = colour;
        }

        private static void DrawGroupHeading(string title, GUIStyle style, bool spaceBefore)
        {
            if (spaceBefore) GUILayout.Space(10f);
            GUILayout.Label(title.ToUpperInvariant(), style, GUILayout.Height(24f));
        }

        private static bool IsSpellSlot(GameAction? action) => action.HasValue && System.Array.IndexOf(SpellSlotActions, action.Value) >= 0;

        private GUIStyle FallbackHeading(GUIStyle labelStyle)
        {
            if (fallbackHeading == null)
                fallbackHeading = new GUIStyle(labelStyle) { fontStyle = FontStyle.Bold, fontSize = Mathf.Max(12, labelStyle.fontSize - 4) };
            return fallbackHeading;
        }
    }
}
