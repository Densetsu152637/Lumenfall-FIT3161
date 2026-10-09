using System;
using UnityEngine;
using Lumenfall.Accessibility;
using Lumenfall.Audio;
using Lumenfall.Controls;
using Lumenfall.Quests;

namespace Lumenfall.Startup
{
    /// <summary>Shared settings content for the startup menu, pause menu and journal.</summary>
    public sealed class OptionsPanel
    {
        public enum Tab { Main, Graphics }
        private static readonly string[] Tabs = { "Main", "Graphics" };
        private static readonly string[] MainLabels = { "Quest hints", "Master volume", "Music volume", "Sound effects volume", "Ambience volume", "Text size", "Dialogue speed", "Instant reveal", "Reduced camera motion", "High contrast", "Sprint input", "Reduce menu motion", "Controls" };
        private static readonly string[] GraphicsLabels = { "Fullscreen", "VSync", "Antialiasing", "Ambient occlusion", "Shading quality", "Use Recommended" };
        private static readonly string[] AaLabels = { "Off", "FXAA", "SMAA" };
        private readonly GameSettingsPanel settings = new GameSettingsPanel();
        private readonly UiNavigationInput navigation = new UiNavigationInput();
        private Vector2 scroll;
        private int focus;
        private bool scrollToFocus;
        private bool controlsOpen;
        public Tab SelectedTab { get; private set; }
        public bool IsRebinding => settings.IsRebinding;
        public int FocusedControl => focus;
        public void CancelRebind() => settings.CancelRebind();

        public void Reset()
        {
            settings.CancelRebind();
            settings.Message = "Click an action, then press a key.";
            controlsOpen = false;
            SelectedTab = Tab.Main;
            focus = 0;
            scroll = Vector2.zero;
            scrollToFocus = true;
        }

        public void SelectTab(Tab next)
        {
            if (!Enum.IsDefined(typeof(Tab), next) || next == SelectedTab) return;
            settings.CancelRebind();
            controlsOpen = false;
            SelectedTab = next;
            focus = (int)next;
            scroll = Vector2.zero;
            scrollToFocus = true;
        }

        public void Tick(float deltaTime)
        {
            bool rebinding = settings.IsRebinding;
            settings.Tick();
            if (rebinding) return;
            navigation.Read();
            HandleNavigation(navigation.Vertical, navigation.Horizontal, navigation.Submit);
        }

        public void HandleNavigation(int vertical, int horizontal, bool submit)
        {
            int count = controlsOpen ? 3 : 2 + (SelectedTab == Tab.Main ? MainLabels.Length : GraphicsLabels.Length);
            if (vertical != 0) { focus = (focus + Math.Sign(vertical) + count) % count; scrollToFocus = true; }
            if (focus < 2)
            {
                if (horizontal != 0) { focus = (focus + Math.Sign(horizontal) + 2) % 2; SelectTab((Tab)focus); }
                else if (submit) SelectTab((Tab)focus);
                return;
            }
            if (horizontal != 0 || submit) Adjust(focus - 2, horizontal != 0 ? Math.Sign(horizontal) : 1);
        }

        private void Adjust(int row, int direction)
        {
            if (controlsOpen) { controlsOpen = false; focus = 14; return; }
            if (SelectedTab == Tab.Main)
            {
                AccessibilitySnapshot a = AccessibilitySettings.Current;
                if (row == 0) QuestHintOverlay.SetHintsEnabled(!QuestHintOverlay.HintsEnabled);
                else if (row <= 4) { AudioBus bus = (AudioBus)(row - 1); AudioMixSettings.SetVolume(bus, AudioMixSettings.GetVolume(bus) + direction * 0.05f); }
                else if (row <= 10)
                {
                    AccessibilityOption option = (AccessibilityOption)(row - 5);
                    float value = row == 5 ? a.TextScale + direction * 0.125f : row == 6 ? a.CharactersPerSecond + direction * 10f
                        : row == 7 ? (a.InstantReveal ? 0f : 1f) : row == 8 ? (a.ReducedMotion ? 0f : 1f)
                        : row == 9 ? (a.HighContrast ? 0f : 1f) : (a.HoldToSprint ? 0f : 1f);
                    AccessibilitySettings.Update(a.With(option, value));
                }
                else if (row == 11) GameSettingsPanel.ReduceMotion = !GameSettingsPanel.ReduceMotion;
                else { controlsOpen = true; focus = 2; settings.CancelRebind(); }
                return;
            }
            if (row == 0) { if (!Application.isEditor) GameSettingsPanel.Fullscreen = !GameSettingsPanel.Fullscreen; return; }
            GraphicsOptions graphics = GraphicsOptions.Instance;
            if (graphics == null) return;
            GraphicsPreferences current = graphics.Preferences;
            if (row == 5) { graphics.SetPreferences(graphics.RecommendedPreferences); return; }
            if ((row == 2 || row == 4) && !graphics.SupportsUrpOptions || row == 3 && !graphics.SupportsAmbientOcclusion) return;
            graphics.SetPreferences(new GraphicsPreferences(row == 1 ? !current.VSync : current.VSync,
                row == 2 ? (GraphicsAntialiasing)(((int)current.Antialiasing + direction + 3) % 3) : current.Antialiasing,
                row == 3 ? !current.AmbientOcclusion : current.AmbientOcclusion,
                row == 4 ? (ShadingQuality)(((int)current.Shading + direction + 3) % 3) : current.Shading));
        }

        internal void Draw(Rect area, MenuSkin skin)
        {
            AccessibilitySnapshot a = AccessibilitySettings.Current;
            float tabWidth = (area.width - 12f) * 0.5f;
            for (int i = 0; i < 2; i++)
            {
                Rect tab = new Rect(area.x + i * (tabWidth + 12f), area.y, tabWidth, 42f);
                if (focus == i) skin.HoverGlow(tab);
                if (skin.Button(tab, ((int)SelectedTab == i ? "• " : "") + Localization.Resolve("settings.tab." + Tabs[i].ToLowerInvariant(), Tabs[i]))) { focus = i; SelectTab((Tab)i); }
            }
            Rect body = new Rect(area.x, area.y + 56f, area.width, Mathf.Max(50f, area.height - 56f));
            if (controlsOpen)
            {
                if (skin.Button(new Rect(body.x, body.y, body.width, 36f), "Back to Main settings")) { controlsOpen = false; focus = 14; }
                GUILayout.BeginArea(new Rect(body.x, body.y + 44f, body.width, Mathf.Max(60f, body.height - 44f)));
                settings.DrawControls(skin.Row, skin.Key, skin.Message, Mathf.Max(40f, body.height - 150f), skin.Heading);
                GUILayout.EndArea();
                return;
            }
            string[] labels = SelectedTab == Tab.Main ? MainLabels : GraphicsLabels;
            float labelHeight = Mathf.Max(28f, 28f * a.TextScale);
            float rowHeight = labelHeight + 58f;
            if (scrollToFocus)
            {
                float y = Mathf.Max(0, focus - 2) * rowHeight;
                if (y < scroll.y) scroll.y = y;
                else if (y + rowHeight > scroll.y + body.height) scroll.y = Mathf.Max(0f, y + rowHeight - body.height);
                scrollToFocus = false;
            }
            float width = body.width - 24f;
            using (skin.ThemedScrollbars())
            {
                scroll = GUI.BeginScrollView(body, scroll, new Rect(0f, 0f, width, labels.Length * rowHeight));
                for (int i = 0; i < labels.Length; i++)
                {
                    Rect row = new Rect(0f, i * rowHeight, width, rowHeight - 6f);
                    skin.Box(row, skin.Card, 1f);
                    if (focus == i + 2) skin.HoverGlow(row);
                    string value = Value(i, a);
                    skin.Label(new Rect(16f, row.y + 8f, width - 32f, labelHeight), Localization.Resolve("settings." + (SelectedTab == Tab.Main ? "main." : "graphics.") + i, labels[i]), skin.Row, MenuArt.Cream);
                    float valueY = row.y + labelHeight + 12f;
                    skin.Label(new Rect(70f, valueY, Mathf.Max(30f, width - 140f), 30f), value, skin.SmallCentre, MenuArt.Gold);
                    bool previous = GUI.enabled;
                    GUI.enabled = previous && Available(i);
                    if (skin.Button(new Rect(12f, valueY, 48f, 30f), "−")) { focus = i + 2; Adjust(i, -1); }
                    if (skin.Button(new Rect(width - 60f, valueY, 48f, 30f), "+")) { focus = i + 2; Adjust(i, 1); }
                    GUI.enabled = previous;
                }
                GUI.EndScrollView();
            }
        }

        private bool Available(int row)
        {
            if (SelectedTab == Tab.Main) return true;
            if (row == 0) return !Application.isEditor;
            GraphicsOptions g = GraphicsOptions.Instance;
            return g != null && (row != 2 && row != 4 || g.SupportsUrpOptions) && (row != 3 || g.SupportsAmbientOcclusion);
        }

        private string Value(int row, AccessibilitySnapshot a)
        {
            if (SelectedTab == Tab.Main)
            {
                if (row == 0) return OnOff(QuestHintOverlay.HintsEnabled);
                if (row <= 4) return Mathf.RoundToInt(AudioMixSettings.GetVolume((AudioBus)(row - 1)) * 100f) + "%";
                if (row == 5) return Mathf.RoundToInt(a.TextScale * 100f) + "%";
                if (row == 6) return a.CharactersPerSecond.ToString("0") + " / s";
                if (row == 7) return OnOff(a.InstantReveal);
                if (row == 8) return OnOff(a.ReducedMotion);
                if (row == 9) return OnOff(a.HighContrast);
                if (row == 10) return a.HoldToSprint ? "Hold" : "Toggle";
                if (row == 11) return OnOff(GameSettingsPanel.ReduceMotion);
                return "Edit key bindings";
            }
            if (row == 0) return Application.isEditor ? "Available in a built game" : OnOff(Screen.fullScreen);
            GraphicsOptions g = GraphicsOptions.Instance;
            if (g == null) return "Unavailable";
            GraphicsPreferences p = g.Preferences;
            if (row == 1) return OnOff(p.VSync);
            if (row == 2) return g.SupportsUrpOptions ? AaLabels[(int)p.Antialiasing] : "Unavailable";
            if (row == 3) return g.SupportsAmbientOcclusion ? OnOff(p.AmbientOcclusion) : "Unavailable";
            if (row == 4) return g.SupportsUrpOptions ? p.Shading.ToString() : "Unavailable";
            return g.RecommendedPreferences.Shading + " preset";
        }
        private static string OnOff(bool value) => Localization.Resolve(value ? "settings.on" : "settings.off", value ? "On" : "Off");
    }
}
