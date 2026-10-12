using System;
using System.Collections.Generic;
using UnityEngine;
using Lumenfall.Controls;
using Lumenfall.Quests;

namespace Lumenfall.Startup
{
    /// <summary>
    /// In-game options in the startup menu's style, split into sections chosen from silk bookmark ribbons that hang from
    /// a spellbook spine: Gameplay (quest hints), Audio (master volume), Display (fullscreen, VSync), Controls (grouped
    /// key rebinding) and Accessibility (reduce motion). The Settings pop-out and the journal's Settings tab both draw it,
    /// so the two always match. Hosts call <see cref="Reset"/> on open, <see cref="Tick"/> from Update and
    /// <see cref="Draw"/> from OnGUI.
    /// </summary>
    internal sealed class OptionsPanel
    {
        private enum Section { Gameplay, Audio, Display, Controls, Accessibility }

        private static readonly string[] SectionLabels = { "Gameplay", "Audio", "Display", "Controls", "Accessibility" };

        // Silk colours, one per section, muted so they sit on the plum panel without shouting.
        private static readonly Color[] SilkColours =
        {
            new Color(0.25f, 0.55f, 0.55f), new Color(0.62f, 0.3f, 0.42f), new Color(0.7f, 0.5f, 0.22f),
            new Color(0.36f, 0.34f, 0.66f), new Color(0.38f, 0.55f, 0.36f)
        };

        // One rune per section, as line-segment pairs in a unit square (x right, y down), in the main menu's rune style:
        // a compass for Gameplay, a bell for Audio, a lantern for Display, a wand for Controls and an open hand for
        // Accessibility.
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

        private const float RibbonHeight = 36f;
        private const float RibbonGap = 8f;
        private const float RibbonPull = 16f;
        private const float SpineWidth = 12f;
        private const float SwitchRowHeight = 84f;
        private const float SectionFadeSeconds = 0.2f;
        private const float PageFlipSeconds = 0.4f;
        private const string ControlsPrompt = "Click an action, then press the key you want.";

        private readonly GameSettingsPanel settings = new GameSettingsPanel();
        private readonly float[] ribbonOut = new float[SectionLabels.Length];
        private Section section;
        private float sectionChangedAt = float.NegativeInfinity;
        private bool ribbonsPlaced;
        // Animated switch knobs. Hosts construct this in field initialisers, where PlayerPrefs is off limits, so Reset
        // snaps them to the saved values on open.
        private float hintsKnob, fullscreenKnob, vsyncKnob, motionKnob;
        private MenuSkin hoverSkin;
        private Action<Rect> rowHover;

        /// <summary>The journal turns pages between sections; the pop-out fades them instead.</summary>
        public bool FlipPages { get; set; }

        public bool IsRebinding => settings.IsRebinding;

        public void CancelRebind() => settings.CancelRebind();

        /// <summary>Call when the host opens: clears any old rebind state and snaps the switches to the saved values.</summary>
        public void Reset()
        {
            settings.CancelRebind();
            settings.Message = ControlsPrompt;
            hintsKnob = Target(QuestHintOverlay.HintsEnabled);
            fullscreenKnob = Target(GameSettingsPanel.Fullscreen);
            vsyncKnob = Target(GameSettingsPanel.VSync);
            motionKnob = Target(GameSettingsPanel.ReduceMotion);
            ribbonsPlaced = false;
        }

        public void Tick(float deltaTime)
        {
            settings.Tick();
            float step = GameSettingsPanel.ReduceMotion ? 1f : deltaTime * 6f;
            hintsKnob = Mathf.MoveTowards(hintsKnob, Target(QuestHintOverlay.HintsEnabled), step);
            fullscreenKnob = Mathf.MoveTowards(fullscreenKnob, Target(GameSettingsPanel.Fullscreen), step);
            vsyncKnob = Mathf.MoveTowards(vsyncKnob, Target(GameSettingsPanel.VSync), step);
            motionKnob = Mathf.MoveTowards(motionKnob, Target(GameSettingsPanel.ReduceMotion), step);
        }

        public void Draw(Rect area, MenuSkin skin)
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

            if (section == Section.Gameplay) DrawGameplay(body, skin);
            else if (section == Section.Audio) DrawAudio(body, skin);
            else if (section == Section.Display) DrawDisplay(body, skin);
            else if (section == Section.Controls) DrawControls(body, skin);
            else DrawAccessibility(body, skin);

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
                if (skin.RibbonButton(ribbon, SectionLabels[i], SilkColours[i], Runes[i], active)) SetSection((Section)i);
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
        }

        private void DrawGameplay(Rect body, MenuSkin skin)
        {
            Rect card = new Rect(body.x, body.y, body.width, SwitchRowHeight + 12f);
            skin.Box(card, skin.Card, 1f);
            SwitchRow(skin, card, "Show quest hints", "Gentle nudges toward what to do next", hintsKnob,
                QuestHintOverlay.HintsEnabled, () => QuestHintOverlay.SetHintsEnabled(!QuestHintOverlay.HintsEnabled));
        }

        private void DrawAudio(Rect body, MenuSkin skin)
        {
            Rect card = new Rect(body.x, body.y, body.width, 112f);
            skin.Box(card, skin.Card, 1f);
            float x = card.x + 22f;
            float width = card.width - 44f;
            float volume = GameSettingsPanel.MasterVolume;
            skin.Label(new Rect(x, card.y + 14f, width, 30f), "Master volume", skin.Row, MenuArt.Cream);
            // The percentage stops short of the lantern at the end of the slider.
            skin.Label(new Rect(x, card.y + 14f, width - 42f, 30f), $"{Mathf.RoundToInt(volume * 100f)}%", skin.Value, MenuArt.Gold);
            float next = skin.LanternSlider(new Rect(x, card.y + 66f, width, 26f), volume);
            if (!Mathf.Approximately(next, volume)) GameSettingsPanel.MasterVolume = next;
        }

        private void DrawDisplay(Rect body, MenuSkin skin)
        {
            Rect card = new Rect(body.x, body.y, body.width, SwitchRowHeight * 2f + 24f);
            skin.Box(card, skin.Card, 1f);
            Rect first = new Rect(card.x, card.y, card.width, SwitchRowHeight + 12f);
            SwitchRow(skin, first, "Fullscreen", Application.isEditor ? "Only applies in a built game" : "Fill the whole screen",
                fullscreenKnob, GameSettingsPanel.Fullscreen, () => GameSettingsPanel.Fullscreen = !GameSettingsPanel.Fullscreen);
            if (Event.current.type == EventType.Repaint)
                skin.Draw(new Rect(card.x + 22f, first.yMax - 2f, card.width - 44f, 4f), skin.Glow, MenuArt.Gold, 0.45f);
            Rect second = new Rect(card.x, first.yMax, card.width, SwitchRowHeight + 12f);
            SwitchRow(skin, second, "VSync", "Match the monitor's refresh rate to avoid tearing", vsyncKnob,
                GameSettingsPanel.VSync, () => GameSettingsPanel.VSync = !GameSettingsPanel.VSync);
        }

        private void DrawAccessibility(Rect body, MenuSkin skin)
        {
            Rect card = new Rect(body.x, body.y, body.width, SwitchRowHeight + 12f);
            skin.Box(card, skin.Card, 1f);
            SwitchRow(skin, card, "Reduce motion", "Calms drifting lights, parallax, camera drift, frost and page turns", motionKnob,
                GameSettingsPanel.ReduceMotion, () => GameSettingsPanel.ReduceMotion = !GameSettingsPanel.ReduceMotion);
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

        /// <summary>
        /// A titled row with a description and an Ampere relic switch. The whole row toggles, so the target is easy to
        /// hit, and toggling sends a spark into the relic.
        /// </summary>
        private static void SwitchRow(MenuSkin skin, Rect row, string title, string description, float knob, bool current, Action toggle)
        {
            Rect hit = new Rect(row.x + 8f, row.y + 6f, row.width - 16f, row.height - 12f);
            if (hit.Contains(Event.current.mousePosition)) skin.HoverGlow(MenuSkin.Expand(hit, -12f, -4f));
            float x = row.x + 22f;
            float width = row.width - 44f;
            skin.Label(new Rect(x, row.y + 14f, width - 76f, 28f), title, skin.Row, MenuArt.Cream);
            skin.Label(new Rect(x, row.y + 42f, width - 76f, 40f), description, skin.Muted, MenuArt.Muted);
            Rect switchRect = new Rect(row.xMax - 22f - 60f, row.y + 22f, 60f, 30f);
            skin.Switch(switchRect, knob);
            skin.SetColor(Color.white, 1f);
            if (GUI.Button(hit, GUIContent.none, GUIStyle.none))
            {
                toggle();
                skin.SwitchSpark(switchRect, !current);
            }
        }

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
