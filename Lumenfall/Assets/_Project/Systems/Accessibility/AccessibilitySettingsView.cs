using System;
using UnityEngine;

namespace Lumenfall.Accessibility
{
    /// <summary>Reusable settings panel; production UI may supply its audio authority through the callback.</summary>
    public sealed class AccessibilitySettingsView
    {
        private Vector2 scroll;
        private int focus;
        private bool audioVisible;
        private bool closeRequested;
        private bool scrollToFocus = true;
        private static readonly string[] OptionIds = { "text_size", "read_speed", "instant_reveal", "reduced_motion", "high_contrast", "hold_sprint" };
        private static readonly string[] OptionFallbacks = { "Text size", "Dialogue speed", "Instant reveal", "Reduced camera motion", "High contrast", "Sprint input" };
        public event Action<int> AudioAdjustRequested;
        public event Action AudioResetRequested;
        public void ResetFocus() { focus = 0; scroll = Vector2.zero; closeRequested = false; scrollToFocus = true; }

        public void HandleNavigation(int vertical, int horizontal, bool submit)
        {
            if (vertical != 0)
            {
                scrollToFocus = true;
                focus = (focus + Math.Sign(vertical) + 9) % 9;
                if (!audioVisible && focus == 6) focus = vertical > 0 ? 7 : 5;
            }
            if (focus < 6 && (horizontal != 0 || submit))
            {
                int direction = horizontal;
                if (direction == 0 && focus >= 2)
                {
                    AccessibilitySnapshot current = AccessibilitySettings.Current;
                    bool enabled = focus == 2 ? current.InstantReveal : focus == 3 ? current.ReducedMotion : focus == 4 ? current.HighContrast : current.HoldToSprint;
                    direction = enabled ? -1 : 1;
                }
                Adjust((AccessibilityOption)focus, direction == 0 ? 1 : direction);
            }
            else if (focus == 6 && horizontal != 0) AudioAdjustRequested?.Invoke(horizontal);
            else if (focus == 7 && submit) Reset();
            else if (focus == 8 && submit) closeRequested = true;
        }

        public bool Draw(Rect panel, Action<GUIStyle> drawAudioControls = null)
        {
            audioVisible = drawAudioControls != null;
            AccessibilitySnapshot value = AccessibilitySettings.Current;
            int fontSize = Mathf.RoundToInt(18f * value.TextScale);
            GUIStyle label = new GUIStyle(GUI.skin.label) { fontSize = fontSize, wordWrap = true, normal = { textColor = Color.white } };
            GUIStyle button = new GUIStyle(GUI.skin.button) { fontSize = fontSize, wordWrap = true };
            Color previous = GUI.color;
            GUI.color = value.HighContrast ? Color.black : new Color(0.08f, 0.08f, 0.12f, 0.97f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = previous;
            float footerHeight = Mathf.Max(64f, fontSize * 2.4f);
            GUI.Label(new Rect(panel.x + 16f, panel.y + 10f, panel.width - 32f, 50f), Localization.Resolve("settings.title", "Settings"), label);
            Rect viewport = new Rect(panel.x + 12f, panel.y + 68f, panel.width - 24f, Mathf.Max(40f, panel.height - 68f - footerHeight));
            float rowHeight = Mathf.Max(88f, fontSize * 4f);
            for (int i = 0; i < 6; i++) rowHeight = Mathf.Max(rowHeight,
                label.CalcHeight(new GUIContent(OptionTitle(i) + ": " + ValueLabel(i, value)), viewport.width - 42f) + fontSize * 1.8f + 18f);
            float audioHeight = audioVisible ? Mathf.Max(130f, fontSize * 5f) : 0f;
            float resetY = rowHeight * 6f + audioHeight;
            if (scrollToFocus && Event.current.type == EventType.Layout && focus < 8)
            {
                scrollToFocus = false;
                float selectedY = focus < 6 ? focus * rowHeight : focus == 6 ? rowHeight * 6f : resetY;
                float selectedHeight = focus == 6 ? audioHeight : rowHeight;
                if (selectedY < scroll.y) scroll.y = selectedY;
                else if (selectedY + selectedHeight > scroll.y + viewport.height) scroll.y = selectedY + selectedHeight - viewport.height;
            }
            scroll = GUI.BeginScrollView(viewport, scroll, new Rect(0f, 0f, viewport.width - 22f, resetY + rowHeight));
            float width = viewport.width - 22f;
            for (int i = 0; i < 6; i++)
            {
                Rect row = new Rect(0f, i * rowHeight, width, rowHeight - 4f);
                Highlight(row, focus == i);
                string title = OptionTitle(i);
                string settingValue = ValueLabel(i, value);
                float controlsHeight = fontSize * 1.8f;
                float captionHeight = rowHeight - controlsHeight - 16f;
                GUI.Label(new Rect(10f, row.y + 4f, width - 20f, captionHeight), title + ": " + settingValue, label);
                float half = (width - 30f) * 0.5f;
                if (GUI.Button(new Rect(10f, row.y + captionHeight + 8f, half, controlsHeight), Localization.Resolve(i < 2 ? "settings.decrease" : i == 5 ? "settings.toggle" : "settings.off", i < 2 ? "Decrease" : i == 5 ? "Toggle" : "Off"), button)) { focus = i; Adjust((AccessibilityOption)i, -1); }
                if (GUI.Button(new Rect(20f + half, row.y + captionHeight + 8f, half, controlsHeight), Localization.Resolve(i < 2 ? "settings.increase" : i == 5 ? "settings.hold" : "settings.on", i < 2 ? "Increase" : i == 5 ? "Hold" : "On"), button)) { focus = i; Adjust((AccessibilityOption)i, 1); }
            }
            if (audioVisible)
            {
                Rect audio = new Rect(0f, rowHeight * 6f, width, audioHeight);
                Highlight(audio, focus == 6);
                GUILayout.BeginArea(new Rect(10f, audio.y + 6f, width - 20f, audio.height - 12f));
                drawAudioControls(label);
                GUILayout.EndArea();
            }
            Highlight(new Rect(0f, resetY, width, rowHeight - 4f), focus == 7);
            if (GUI.Button(new Rect(10f, resetY + 8f, width - 20f, rowHeight - 20f), Localization.Resolve("settings.reset", "Reset settings"), button)) { focus = 7; Reset(); }
            GUI.EndScrollView();
            Rect close = new Rect(panel.x + 16f, panel.yMax - footerHeight + 4f, panel.width - 32f, footerHeight - 16f);
            Highlight(close, focus == 8);
            bool clicked = GUI.Button(close, Localization.Resolve("settings.close", "Close"), button);
            bool result = clicked || closeRequested;
            closeRequested = false;
            return result;
        }
        private void Reset() { AccessibilitySettings.ResetDefaults(); AudioResetRequested?.Invoke(); }
        private static void Highlight(Rect row, bool selected)
        {
            if (!selected) return;
            Color previous = GUI.color;
            GUI.color = new Color(1f, 0.9f, 0.35f, 0.3f);
            GUI.DrawTexture(row, Texture2D.whiteTexture);
            GUI.color = previous;
        }
        private static void Adjust(AccessibilityOption option, int direction)
        {
            AccessibilitySnapshot value = AccessibilitySettings.Current;
            float next = option == AccessibilityOption.TextScale ? value.TextScale + direction * 0.125f :
                option == AccessibilityOption.CharactersPerSecond ? value.CharactersPerSecond + direction * 10f :
                option == AccessibilityOption.HoldToSprint ? (direction > 0 ? 1f : 0f) : direction > 0 ? 1f : 0f;
            AccessibilitySettings.Update(value.With(option, next));
        }
        private static string OptionTitle(int index)
        {
            return Localization.Resolve("settings." + OptionIds[index], OptionFallbacks[index]);
        }
        private static string ValueLabel(int index, AccessibilitySnapshot value)
        {
            if (index == 0) return Mathf.RoundToInt(value.TextScale * 100f) + "%";
            if (index == 1) return value.CharactersPerSecond.ToString("0") + " / s";
            if (index == 5) return Localization.Resolve(value.HoldToSprint ? "settings.hold" : "settings.toggle", value.HoldToSprint ? "Hold" : "Toggle");
            bool enabled = index == 2 ? value.InstantReveal : index == 3 ? value.ReducedMotion : index == 4 ? value.HighContrast : value.HoldToSprint;
            return Localization.Resolve(enabled ? "settings.on" : "settings.off", enabled ? "On" : "Off");
        }
    }
}
