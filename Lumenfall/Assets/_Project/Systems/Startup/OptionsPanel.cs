using System;
using System.Collections.Generic;
using UnityEngine;
using Lumenfall.Accessibility;
using Lumenfall.Audio;
using Lumenfall.Controls;
using Lumenfall.Quests;

namespace Lumenfall.Startup
{
    /// <summary>
    /// In-game options in the startup menu's style, split into sections chosen from silk bookmark ribbons that hang from
    /// a spellbook spine: Gameplay (quest hints, sprint input), Audio (the four volume buses), Display (fullscreen, VSync),
    /// Graphics (antialiasing, ambient occlusion, shading quality, the recommended preset), Controls (grouped key
    /// rebinding) and Accessibility (menu and camera motion, contrast, dialogue reveal, text size). Each section ends with
    /// a reset that returns only that section's settings to their defaults; Controls uses the rebinding list's own reset.
    /// The Settings pop-out
    /// and the journal's Settings tab both draw it, so the two always match. Hosts call <see cref="Reset"/> on open,
    /// <see cref="Tick"/> from Update and <see cref="Draw"/> from OnGUI.
    /// </summary>
    /// <remarks>
    /// Keyboard and controller: Up/Down move between ribbons; Right or Enter steps into the chosen section's rows, where
    /// Up/Down move between rows (Up from the first row returns to the ribbons), Left/Right adjust the focused row and
    /// Enter toggles or presses it. The host owns Escape/B.
    /// </remarks>
    public sealed class OptionsPanel
    {
        public enum Section { Gameplay, Audio, Display, Graphics, Controls, Accessibility }

        private static readonly string[] SectionLabels = { "Gameplay", "Audio", "Display", "Graphics", "Controls", "Accessibility" };

        // Silk colours, one per section, muted so they sit on the plum panel without shouting.
        private static readonly Color[] SilkColours =
        {
            new Color(0.25f, 0.55f, 0.55f), new Color(0.62f, 0.3f, 0.42f), new Color(0.7f, 0.5f, 0.22f),
            new Color(0.24f, 0.42f, 0.68f), new Color(0.36f, 0.34f, 0.66f), new Color(0.38f, 0.55f, 0.36f)
        };

        // One rune per section, as line-segment pairs in a unit square (x right, y down), in the main menu's rune style:
        // a compass for Gameplay, a bell for Audio, a lantern for Display, a cut prism for Graphics, a wand for Controls and
        // an open hand for Accessibility.
        private static readonly Vector2[][] Runes =
        {
            Join(Polygon(0.95f, 8), new[]
            {
                new Vector2(0f, -0.75f), new Vector2(0.25f, 0f), new Vector2(0.25f, 0f), new Vector2(0f, 0.75f),
                new Vector2(0f, 0.75f), new Vector2(-0.25f, 0f), new Vector2(-0.25f, 0f), new Vector2(0f, -0.75f)
            }),
            new[]
            {
                new Vector2(0f, -1f), new Vector2(0f, -0.8f), new Vector2(-0.45f, -0.4f), new Vector2(-0.2f, -0.8f),
                new Vector2(-0.2f, -0.8f), new Vector2(0.2f, -0.8f), new Vector2(0.2f, -0.8f), new Vector2(0.45f, -0.4f),
                new Vector2(-0.45f, -0.4f), new Vector2(-0.6f, 0.5f), new Vector2(0.45f, -0.4f), new Vector2(0.6f, 0.5f),
                new Vector2(-0.75f, 0.5f), new Vector2(0.75f, 0.5f), new Vector2(0f, 0.55f), new Vector2(0f, 0.85f)
            },
            new[]
            {
                new Vector2(0f, -1f), new Vector2(0f, -0.85f), new Vector2(-0.3f, -0.85f), new Vector2(0.3f, -0.85f),
                new Vector2(-0.3f, -0.85f), new Vector2(-0.5f, -0.6f), new Vector2(0.3f, -0.85f), new Vector2(0.5f, -0.6f),
                new Vector2(-0.5f, -0.6f), new Vector2(0.5f, -0.6f), new Vector2(-0.45f, -0.6f), new Vector2(-0.45f, 0.65f),
                new Vector2(0.45f, -0.6f), new Vector2(0.45f, 0.65f), new Vector2(-0.6f, 0.65f), new Vector2(0.6f, 0.65f),
                new Vector2(-0.15f, 0.25f), new Vector2(0f, -0.2f), new Vector2(0f, -0.2f), new Vector2(0.15f, 0.25f)
            },
            new[]
            {
                new Vector2(-0.5f, -0.6f), new Vector2(0.5f, -0.6f), new Vector2(0.5f, -0.6f), new Vector2(0.85f, -0.2f),
                new Vector2(0.85f, -0.2f), new Vector2(0f, 0.95f), new Vector2(0f, 0.95f), new Vector2(-0.85f, -0.2f),
                new Vector2(-0.85f, -0.2f), new Vector2(-0.5f, -0.6f), new Vector2(-0.85f, -0.2f), new Vector2(0.85f, -0.2f),
                new Vector2(-0.2f, -0.6f), new Vector2(-0.35f, -0.2f), new Vector2(0.2f, -0.6f), new Vector2(0.35f, -0.2f),
                new Vector2(-0.35f, -0.2f), new Vector2(0f, 0.95f), new Vector2(0.35f, -0.2f), new Vector2(0f, 0.95f)
            },
            new[]
            {
                new Vector2(-0.7f, 0.8f), new Vector2(0.4f, -0.3f), new Vector2(0.6f, -0.9f), new Vector2(0.6f, -0.2f),
                new Vector2(0.25f, -0.55f), new Vector2(0.95f, -0.55f), new Vector2(0.38f, -0.78f), new Vector2(0.82f, -0.32f)
            },
            new[]
            {
                new Vector2(-0.45f, -0.3f), new Vector2(-0.45f, -0.85f), new Vector2(-0.15f, -0.3f), new Vector2(-0.15f, -1f),
                new Vector2(0.15f, -0.3f), new Vector2(0.15f, -0.95f), new Vector2(0.45f, -0.3f), new Vector2(0.45f, -0.75f),
                new Vector2(-0.45f, -0.3f), new Vector2(-0.45f, 0.5f), new Vector2(-0.45f, 0.5f), new Vector2(0f, 0.9f),
                new Vector2(0f, 0.9f), new Vector2(0.45f, 0.5f), new Vector2(0.45f, 0.5f), new Vector2(0.45f, -0.3f),
                new Vector2(-0.45f, 0.15f), new Vector2(-0.85f, -0.25f)
            }
        };

        private static readonly string[] AntialiasingLabels = { "Off", "FXAA", "SMAA" };
        private static readonly string[] ShadingLabels = { "Low", "Medium", "High" };
        private static readonly string[] SprintLabels = { "Hold", "Toggle" };
        private static readonly string[] TextSizeLabels = { "100%", "125%", "150%", "175%" };

        private const float RibbonHeight = 36f;
        private const float RibbonGap = 8f;
        private const float RibbonPull = 16f;
        private const float SpineWidth = 12f;
        private const float SwitchRowHeight = 84f;
        private const float SectionFadeSeconds = 0.2f;
        private const float PageFlipSeconds = 0.4f;
        private const float PickerWidth = 240f;
        private const float DisabledAlpha = 0.45f;
        private const float MinimumReadSpeed = 10f;
        private const float MaximumReadSpeed = 100f;
        private const string ControlsPrompt = "Click an action, then press the key you want.";
        private const string Unavailable = "Not available with this renderer";

        private enum RowKind { Switch, Slider, Choice, Button }

        /// <summary>One option row. The getters read the live preference each frame, so the panel never caches values.</summary>
        private sealed class Row
        {
            public RowKind Kind;
            public string Title;
            public Func<string> Description;
            public Func<bool> Enabled = () => true;
            // Switch
            public Func<bool> On;
            public Action<bool> SetOn;
            public float Knob;
            public bool SparkPending;
            // Slider, 0 to 1
            public Func<float> Value;
            public Action<float> SetValue;
            public Func<string> ValueLabel;
            public float Step = 0.05f;
            public bool Lantern;
            // Choice
            public string[] Choices;
            public Func<int> Choice;
            public Action<int> SetChoice;
            // Button
            public string ButtonLabel;
            public Action Press;
        }

        private readonly GameSettingsPanel settings = new GameSettingsPanel();
        private readonly UiNavigationInput navigation = new UiNavigationInput();
        private readonly Row[][] sections;
        private readonly float[] ribbonOut = new float[SectionLabels.Length];
        private Section section;
        private float sectionChangedAt = float.NegativeInfinity;
        private bool ribbonsPlaced;
        private Vector2 scroll;
        // -1 while the ribbons hold keyboard/controller focus, otherwise the focused row of the chosen section.
        private int focusRow = -1;
        // The focus glow only shows once keys or a controller have been used, so pointer players see no stray highlight.
        private bool focusShown;
        private bool scrollToFocus;
        private MenuSkin hoverSkin;
        private Action<Rect> rowHover;

        public OptionsPanel()
        {
            sections = new Row[SectionLabels.Length][];
            sections[(int)Section.Gameplay] = new[]
            {
                SwitchRow("Show quest hints", () => "Gentle nudges toward what to do next",
                    () => QuestHintOverlay.HintsEnabled, QuestHintOverlay.SetHintsEnabled),
                ChoiceRow("Sprint", () => "Hold the sprint key, or press it once to toggle", SprintLabels,
                    () => AccessibilitySettings.Current.HoldToSprint ? 0 : 1,
                    choice => UpdateAccessibility(AccessibilityOption.HoldToSprint, choice == 0 ? 1f : 0f)),
                ResetRow("gameplay", ResetGameplay)
            };
            // Ambience only gets a slider once the game has ambience to play.
            List<Row> audioRows = new List<Row>
            {
                VolumeRow("Master volume", AudioBus.Master),
                VolumeRow("Music", AudioBus.Music),
                VolumeRow("Sound effects", AudioBus.Sfx),
            };
            if (AudioFeedbackService.HasAmbience) audioRows.Add(VolumeRow("Ambience", AudioBus.Ambience));
            audioRows.Add(ResetRow("audio", ResetAudio));
            sections[(int)Section.Audio] = audioRows.ToArray();
            sections[(int)Section.Display] = new[]
            {
                SwitchRow("Fullscreen", () => Application.isEditor ? "Only applies in a built game" : "Fill the whole screen",
                    () => GameSettingsPanel.Fullscreen, value => GameSettingsPanel.Fullscreen = value),
                SwitchRow("VSync", () => "Match the monitor's refresh rate to avoid tearing",
                    () => GameSettingsPanel.VSync, value => GameSettingsPanel.VSync = value),
                ResetRow("display", ResetDisplay)
            };
            sections[(int)Section.Graphics] = new[]
            {
                WithEnabled(ChoiceRow("Antialiasing", () => UrpReady ? "Smooths jagged edges" : Unavailable, AntialiasingLabels,
                    () => Graphics == null ? 0 : (int)Graphics.Preferences.Antialiasing,
                    choice => SetGraphics(p => new GraphicsPreferences(p.VSync, (GraphicsAntialiasing)choice, p.AmbientOcclusion, p.Shading))),
                    () => UrpReady),
                WithEnabled(SwitchRow("Ambient occlusion", () => AmbientOcclusionReady ? "Soft contact shadows where surfaces meet" : Unavailable,
                    () => AmbientOcclusionReady && Graphics.Preferences.AmbientOcclusion,
                    value => SetGraphics(p => new GraphicsPreferences(p.VSync, p.Antialiasing, value, p.Shading))),
                    () => AmbientOcclusionReady),
                WithEnabled(ChoiceRow("Shading quality", () => UrpReady ? "Shadow range, shadow detail and level of detail" : Unavailable,
                    ShadingLabels, () => Graphics == null ? 2 : (int)Graphics.Preferences.Shading,
                    choice => SetGraphics(p => new GraphicsPreferences(p.VSync, p.Antialiasing, p.AmbientOcclusion, (ShadingQuality)choice))),
                    () => UrpReady),
                WithEnabled(new Row
                {
                    Kind = RowKind.Button,
                    Title = "Recommended for this PC",
                    Description = () => Graphics == null ? "Graphics options are not available"
                        : ShadingLabels[(int)Graphics.RecommendedPreferences.Shading] + " preset. Changes save automatically.",
                    ButtonLabel = "Use recommended",
                    Press = () => Graphics?.SetPreferences(Graphics.RecommendedPreferences)
                }, () => Graphics != null),
                WithEnabled(ResetRow("graphics", ResetGraphics), () => Graphics != null)
            };
            sections[(int)Section.Controls] = Array.Empty<Row>();
            sections[(int)Section.Accessibility] = new[]
            {
                SwitchRow("Reduce menu motion", () => "Calms drifting lights, parallax, camera drift, frost and page turns",
                    () => GameSettingsPanel.ReduceMotion, value => GameSettingsPanel.ReduceMotion = value),
                SwitchRow("Reduce camera motion", () => "The gameplay camera follows without easing",
                    () => AccessibilitySettings.Current.ReducedMotion,
                    value => UpdateAccessibility(AccessibilityOption.ReducedMotion, value ? 1f : 0f)),
                SwitchRow("High contrast", () => "Plain white text on dark panels",
                    () => AccessibilitySettings.Current.HighContrast,
                    value => UpdateAccessibility(AccessibilityOption.HighContrast, value ? 1f : 0f)),
                SwitchRow("Instant dialogue", () => "Show each line of dialogue at once",
                    () => AccessibilitySettings.Current.InstantReveal,
                    value => UpdateAccessibility(AccessibilityOption.InstantReveal, value ? 1f : 0f)),
                WithEnabled(new Row
                {
                    Kind = RowKind.Slider,
                    Title = "Dialogue speed",
                    Description = () => "Letters revealed each second",
                    Value = () => Mathf.InverseLerp(MinimumReadSpeed, MaximumReadSpeed, AccessibilitySettings.Current.CharactersPerSecond),
                    SetValue = value => UpdateAccessibility(AccessibilityOption.CharactersPerSecond,
                        Mathf.Round(Mathf.Lerp(MinimumReadSpeed, MaximumReadSpeed, value))),
                    ValueLabel = () => $"{AccessibilitySettings.Current.CharactersPerSecond:0} / s",
                    Step = 10f / (MaximumReadSpeed - MinimumReadSpeed)
                }, () => !AccessibilitySettings.Current.InstantReveal),
                ChoiceRow("Text size", () => "Menu and dialogue text", TextSizeLabels,
                    () => Mathf.Clamp(Mathf.RoundToInt((AccessibilitySettings.Current.TextScale - 1f) / 0.25f), 0, TextSizeLabels.Length - 1),
                    choice => UpdateAccessibility(AccessibilityOption.TextScale, 1f + choice * 0.25f)),
                ResetRow("accessibility", ResetAccessibility)
            };
        }

        /// <summary>The journal turns pages between sections; the pop-out fades them instead.</summary>
        public bool FlipPages { get; set; }

        public bool IsRebinding => settings.IsRebinding;
        public Section SelectedSection => section;
        /// <summary>-1 while the ribbons hold focus, otherwise the focused row of <see cref="SelectedSection"/>.</summary>
        public int FocusedRow => focusRow;

        public void CancelRebind() => settings.CancelRebind();

        /// <summary>Call when the host opens: clears any old rebind state and snaps the switches to the saved values.</summary>
        public void Reset()
        {
            settings.CancelRebind();
            settings.Message = ControlsPrompt;
            foreach (Row[] rows in sections)
                foreach (Row row in rows)
                    if (row.Kind == RowKind.Switch) { row.Knob = Target(row.On()); row.SparkPending = false; }
            ribbonsPlaced = false;
            focusRow = -1;
            focusShown = false;
            scroll = Vector2.zero;
        }

        public void Tick(float deltaTime)
        {
            bool rebinding = settings.IsRebinding;
            settings.Tick();
            float step = GameSettingsPanel.ReduceMotion ? 1f : deltaTime * 6f;
            foreach (Row[] rows in sections)
                foreach (Row row in rows)
                    if (row.Kind == RowKind.Switch) row.Knob = Mathf.MoveTowards(row.Knob, Target(row.On()), step);
            if (rebinding) return;
            navigation.Read();
            HandleNavigation(navigation.Vertical, navigation.Horizontal, navigation.Submit);
        }

        /// <summary>Applies one step of keyboard/controller navigation; exposed so tests can drive the panel directly.</summary>
        public void HandleNavigation(int vertical, int horizontal, bool submit)
        {
            if (vertical == 0 && horizontal == 0 && !submit) return;
            focusShown = true;
            Row[] rows = sections[(int)section];
            if (focusRow < 0)
            {
                if (vertical != 0) SetSection((Section)(((int)section + Math.Sign(vertical) + SectionLabels.Length) % SectionLabels.Length));
                else if ((horizontal > 0 || submit) && rows.Length > 0) { focusRow = 0; scrollToFocus = true; }
                return;
            }

            if (vertical != 0)
            {
                int next = focusRow + Math.Sign(vertical);
                focusRow = next < 0 ? -1 : Mathf.Min(next, rows.Length - 1);
                scrollToFocus = true;
                return;
            }
            Row row = rows[focusRow];
            if (!row.Enabled()) return;
            switch (row.Kind)
            {
                case RowKind.Switch:
                    bool on = row.On();
                    bool wanted = horizontal != 0 ? horizontal > 0 : !on;
                    if (wanted != on) { row.SetOn(wanted); row.SparkPending = true; }
                    break;
                case RowKind.Slider:
                    if (horizontal != 0) row.SetValue(Mathf.Clamp01(row.Value() + Math.Sign(horizontal) * row.Step));
                    break;
                case RowKind.Choice:
                    int count = row.Choices.Length;
                    int choice = horizontal != 0 ? Mathf.Clamp(row.Choice() + Math.Sign(horizontal), 0, count - 1) : (row.Choice() + 1) % count;
                    if (choice != row.Choice()) row.SetChoice(choice);
                    break;
                case RowKind.Button:
                    if (submit) row.Press();
                    break;
            }
        }

        /// <summary>Opens a section directly, as clicking its ribbon does.</summary>
        public void SelectSection(Section next) => SetSection(next);

        internal void Draw(Rect area, MenuSkin skin)
        {
            float navWidth = Mathf.Clamp(area.width * 0.27f, 200f, 230f);
            DrawRibbons(new Rect(area.x, area.y, navWidth, area.height), skin);
            if (Event.current.type == EventType.Repaint)
                skin.Draw(new Rect(area.x + navWidth + 12f, area.y, 2f, area.height), skin.Glow, MenuArt.Gold, 0.3f);

            float now = Time.unscaledTime;
            float fade = skin.Fade;
            Rect body = new Rect(area.x + navWidth + 30f, area.y, area.width - navWidth - 30f, area.height);
            Matrix4x4 saved = GUI.matrix;
            float flip = 1f;
            if (FlipPages)
            {
                // The journal turns a page from the spine side instead of fading.
                flip = skin.Motion > 0f ? Mathf.Clamp01((now - sectionChangedAt) / PageFlipSeconds) : 1f;
                saved = skin.BeginPageFlip(body, flip);
            }
            else
            {
                float shown = MenuSkin.Smooth((now - sectionChangedAt) / SectionFadeSeconds);
                skin.Fade = fade * shown;
                float offset = (1f - shown) * 6f * skin.Motion;
                body = new Rect(body.x, body.y + offset, body.width, body.height - offset);
            }

            if (section == Section.Controls) DrawControls(body, skin);
            else DrawRows(body, skin, sections[(int)section]);

            if (FlipPages) skin.EndPageFlip(body, flip, saved);
            skin.Fade = fade;
        }

        /// <summary>
        /// Section ribbons hang from a spine at the left. They are drawn first so the spine covers their ends, and the
        /// chosen ribbon pulls out further than the rest.
        /// </summary>
        private void DrawRibbons(Rect nav, MenuSkin skin)
        {
            float spineHeight = SectionLabels.Length * (RibbonHeight + RibbonGap) + RibbonGap;
            Rect spine = new Rect(nav.x, nav.y - 4f, SpineWidth, spineHeight);
            float baseWidth = nav.width - SpineWidth - RibbonPull - 4f;
            bool repaint = Event.current.type == EventType.Repaint;
            for (int i = 0; i < SectionLabels.Length; i++)
            {
                Rect hit = new Rect(spine.xMax, nav.y + i * (RibbonHeight + RibbonGap), baseWidth + RibbonPull, RibbonHeight);
                bool active = (int)section == i;
                if (repaint)
                {
                    float target = active ? 1f : hit.Contains(Event.current.mousePosition) ? 0.35f : 0f;
                    ribbonOut[i] = !ribbonsPlaced || skin.Motion <= 0f ? target
                        : Mathf.Lerp(ribbonOut[i], target, 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime));
                }
                Rect ribbon = new Rect(spine.x + 4f, hit.y, SpineWidth - 4f + baseWidth + RibbonPull * ribbonOut[i], RibbonHeight);
                if (active && focusShown && focusRow < 0) skin.HoverGlow(ribbon);
                if (skin.RibbonButton(ribbon, SectionLabels[i], SilkColours[i], Runes[i], active))
                {
                    SetSection((Section)i);
                    focusShown = false;
                }
            }
            if (repaint) ribbonsPlaced = true;
            skin.Spine(spine);
        }

        private void SetSection(Section next)
        {
            if (next == section) return;
            if (section == Section.Controls) settings.CancelRebind();
            section = next;
            sectionChangedAt = Time.unscaledTime;
            focusRow = -1;
            scroll = Vector2.zero;
        }

        /// <summary>
        /// The section's rows on one card, split by gold dividers. The card scrolls when the host's area is too short, for
        /// example with a large text size.
        /// </summary>
        private void DrawRows(Rect body, MenuSkin skin, Row[] rows)
        {
            float scale = Mathf.Max(1f, AccessibilitySettings.Current.TextScale);
            float rowHeight = SwitchRowHeight + 12f + (scale - 1f) * 40f;
            float contentHeight = rows.Length * rowHeight;
            bool scrolls = contentHeight > body.height;
            using (skin.ThemedScrollbars())
            {
                Rect area = body;
                if (scrolls)
                {
                    if (scrollToFocus && focusRow >= 0)
                    {
                        float top = focusRow * rowHeight;
                        if (top < scroll.y) scroll.y = top;
                        else if (top + rowHeight > scroll.y + body.height) scroll.y = top + rowHeight - body.height;
                    }
                    area = new Rect(0f, 0f, body.width - 18f, contentHeight);
                    scroll = GUI.BeginScrollView(body, scroll, area);
                }
                scrollToFocus = false;

                skin.Box(new Rect(area.x, area.y, area.width, contentHeight), skin.Card, 1f);
                for (int i = 0; i < rows.Length; i++)
                {
                    Rect row = new Rect(area.x, area.y + i * rowHeight, area.width, rowHeight);
                    if (i > 0 && Event.current.type == EventType.Repaint)
                        skin.Draw(new Rect(row.x + 22f, row.y - 2f, row.width - 44f, 4f), skin.Glow, MenuArt.Gold, 0.45f);
                    DrawRow(skin, rows[i], row, focusShown && i == focusRow, scale);
                }

                if (scrolls) GUI.EndScrollView();
            }
        }

        private static void DrawRow(MenuSkin skin, Row row, Rect rect, bool focused, float scale)
        {
            bool enabled = row.Enabled();
            float savedFade = skin.Fade;
            if (!enabled) skin.Fade *= DisabledAlpha;
            Rect hit = new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, rect.height - 12f);
            bool hovered = enabled && hit.Contains(Event.current.mousePosition);
            if (focused || hovered && row.Kind == RowKind.Switch) skin.HoverGlow(MenuSkin.Expand(hit, -12f, -4f));

            float x = rect.x + 22f;
            float width = rect.width - 44f;
            float titleHeight = 28f * scale;
            float controlY = rect.y + 22f + (titleHeight - 28f) * 0.5f;
            float textWidth = row.Kind == RowKind.Switch ? width - 76f
                : row.Kind == RowKind.Slider ? width
                : width - Mathf.Min(PickerWidth, width * 0.5f) - 12f;
            skin.Label(new Rect(x, rect.y + 14f, textWidth, titleHeight), row.Title, skin.Row, MenuArt.Cream);
            if (row.Kind != RowKind.Slider)
                skin.Label(new Rect(x, rect.y + 14f + titleHeight, textWidth, 40f), row.Description(), skin.Muted, MenuArt.Muted);

            switch (row.Kind)
            {
                case RowKind.Switch:
                {
                    // An Ampere relic switch; the whole row toggles, and toggling sends a spark into the relic.
                    Rect switchRect = new Rect(rect.xMax - 22f - 60f, controlY, 60f, 30f);
                    skin.Switch(switchRect, row.Knob);
                    skin.SetColor(Color.white, 1f);
                    if (enabled && GUI.Button(hit, GUIContent.none, GUIStyle.none))
                    {
                        row.SetOn(!row.On());
                        row.SparkPending = true;
                    }
                    if (row.SparkPending)
                    {
                        skin.SwitchSpark(switchRect, row.On());
                        row.SparkPending = false;
                    }
                    break;
                }
                case RowKind.Slider:
                {
                    // The value stops short of the lantern at the end of a volume slider.
                    skin.Label(new Rect(x, rect.y + 14f, width - (row.Lantern ? 42f : 0f), titleHeight), row.ValueLabel(), skin.Value, MenuArt.Gold);
                    float value = row.Value();
                    bool previous = GUI.enabled;
                    GUI.enabled = previous && enabled;
                    Rect slider = new Rect(x, rect.y + 24f + titleHeight + 16f, width, 26f);
                    float next = row.Lantern ? skin.LanternSlider(slider, value) : skin.Slider(slider, value);
                    GUI.enabled = previous;
                    if (enabled && !Mathf.Approximately(next, value)) row.SetValue(next);
                    break;
                }
                case RowKind.Choice:
                {
                    // A segmented pill picker: the chosen pill is lavender and the others glow like a candle when hovered.
                    const float Gap = 6f;
                    float pickerWidth = Mathf.Min(PickerWidth, width * 0.5f);
                    Rect picker = new Rect(rect.xMax - 22f - pickerWidth, controlY, pickerWidth, 34f);
                    int selected = row.Choice();
                    float segmentWidth = (picker.width - Gap * (row.Choices.Length - 1)) / row.Choices.Length;
                    for (int i = 0; i < row.Choices.Length; i++)
                    {
                        Rect segment = new Rect(picker.x + i * (segmentWidth + Gap), picker.y, segmentWidth, picker.height);
                        bool on = i == selected;
                        bool hover = enabled && !on && segment.Contains(Event.current.mousePosition);
                        if (hover) skin.HoverGlow(MenuSkin.Expand(segment, 4f, 4f));
                        skin.Box(segment, skin.Pill, on ? 0.95f : 0.85f, on ? MenuArt.Lavender : MenuArt.Ink);
                        skin.Label(segment, row.Choices[i], skin.SmallCentre, on ? MenuArt.Ink : hover ? MenuArt.Gold : MenuArt.Cream);
                        skin.SetColor(Color.white, 1f);
                        if (enabled && !on && GUI.Button(segment, GUIContent.none, GUIStyle.none)) row.SetChoice(i);
                    }
                    break;
                }
                case RowKind.Button:
                {
                    const float ButtonWidth = 180f;
                    Rect button = new Rect(rect.xMax - 22f - ButtonWidth, controlY - 6f, ButtonWidth, 46f);
                    bool previous = GUI.enabled;
                    GUI.enabled = previous && enabled;
                    if (skin.Button(button, row.ButtonLabel) && enabled) row.Press();
                    GUI.enabled = previous;
                    break;
                }
            }
            skin.Fade = savedFade;
        }

        private void DrawControls(Rect body, MenuSkin skin)
        {
            if (hoverSkin != skin)
            {
                hoverSkin = skin;
                rowHover = skin.HoverGlow;
            }
            settings.RowHover = rowHover;
            // GameSettingsPanel lays out the list, the swap prompt or reset button, and the message; 96 covers the last two.
            float listHeight = Mathf.Max(64f, body.height - 96f);
            using (skin.ThemedScrollbars())
            {
                skin.SetColor(Color.white, 1f);
                skin.Row.normal.textColor = MenuArt.Cream;
                skin.Heading.normal.textColor = MenuArt.Gold;
                skin.Message.normal.textColor = MenuArt.Ember;
                GUILayout.BeginArea(body);
                settings.DrawControls(skin.Row, skin.Key, skin.Message, listHeight, skin.Heading);
                GUILayout.EndArea();
            }
        }

        // ---- Rows ----

        private static Row SwitchRow(string title, Func<string> description, Func<bool> on, Action<bool> setOn) =>
            new Row { Kind = RowKind.Switch, Title = title, Description = description, On = on, SetOn = setOn };

        private static Row ChoiceRow(string title, Func<string> description, string[] choices, Func<int> choice, Action<int> setChoice) =>
            new Row { Kind = RowKind.Choice, Title = title, Description = description, Choices = choices, Choice = choice, SetChoice = setChoice };

        private static Row VolumeRow(string title, AudioBus bus) => new Row
        {
            Kind = RowKind.Slider,
            Title = title,
            Lantern = true,
            Value = () => AudioMixSettings.GetVolume(bus),
            // The master bus also drives AudioListener.volume, so it goes through GameSettingsPanel.
            SetValue = value =>
            {
                if (bus == AudioBus.Master) GameSettingsPanel.MasterVolume = value;
                else AudioMixSettings.SetVolume(bus, value);
            },
            ValueLabel = () => $"{Mathf.RoundToInt(AudioMixSettings.GetVolume(bus) * 100f)}%"
        };

        private static Row ResetRow(string section, Action reset) => new Row
        {
            Kind = RowKind.Button,
            Title = "Reset to defaults",
            Description = () => $"Only the {section} settings above",
            ButtonLabel = "Reset",
            Press = reset
        };

        // ---- Section resets: each touches only the settings its own section shows ----

        private static void ResetGameplay()
        {
            QuestHintOverlay.SetHintsEnabled(true);
            UpdateAccessibility(AccessibilityOption.HoldToSprint, AccessibilitySnapshot.Defaults.HoldToSprint ? 1f : 0f);
        }

        private static void ResetAudio()
        {
            AudioMixSettings.ResetDefaults();
            GameSettingsPanel.ApplySavedVolume();
        }

        private static void ResetDisplay()
        {
            // The project's player settings start in fullscreen window mode.
            GameSettingsPanel.Fullscreen = true;
            GameSettingsPanel.VSync = GraphicsPreferences.Defaults.VSync;
        }

        /// <summary>The recommended preset is the graphics default; VSync lives on the Display section, so it is kept.</summary>
        private static void ResetGraphics()
        {
            if (Graphics == null) return;
            GraphicsPreferences recommended = Graphics.RecommendedPreferences;
            SetGraphics(p => new GraphicsPreferences(p.VSync, recommended.Antialiasing, recommended.AmbientOcclusion, recommended.Shading));
        }

        /// <summary>Sprint is stored with the accessibility preferences but shown under Gameplay, so it is kept.</summary>
        private static void ResetAccessibility()
        {
            GameSettingsPanel.ReduceMotion = false;
            AccessibilitySettings.Update(new AccessibilitySnapshot(holdToSprint: AccessibilitySettings.Current.HoldToSprint));
        }

        private static Row WithEnabled(Row row, Func<bool> enabled)
        {
            row.Enabled = enabled;
            return row;
        }

        private static GraphicsOptions Graphics => GraphicsOptions.Instance;
        private static bool UrpReady => Graphics != null && Graphics.SupportsUrpOptions;
        private static bool AmbientOcclusionReady => Graphics != null && Graphics.SupportsAmbientOcclusion;

        private static void SetGraphics(Func<GraphicsPreferences, GraphicsPreferences> change)
        {
            GraphicsOptions graphics = Graphics;
            if (graphics != null) graphics.SetPreferences(change(graphics.Preferences));
        }

        private static void UpdateAccessibility(AccessibilityOption option, float value) =>
            AccessibilitySettings.Update(AccessibilitySettings.Current.With(option, value));

        private static float Target(bool on) => on ? 1f : 0f;

        private static Vector2[] Polygon(float radius, int sides)
        {
            Vector2[] segments = new Vector2[sides * 2];
            for (int i = 0; i < sides; i++)
            {
                float a = (i + 0.5f) / sides * Mathf.PI * 2f, b = (i + 1.5f) / sides * Mathf.PI * 2f;
                segments[i * 2] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
                segments[i * 2 + 1] = new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * radius;
            }
            return segments;
        }

        private static Vector2[] Join(Vector2[] first, Vector2[] second)
        {
            List<Vector2> joined = new List<Vector2>(first);
            joined.AddRange(second);
            return joined.ToArray();
        }
    }
}
