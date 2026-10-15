using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Lumenfall.Controls;
using Lumenfall.GameEvents;
using Lumenfall.Accessibility;

namespace Lumenfall.Startup
{
    /// <summary>
    /// The startup menu's night-time look, packaged for in-game IMGUI menus (the Settings pop-out and the journal):
    /// generated textures, Nunito styles, themed controls and the ambient motes, wand cursor and click sparks. The themed
    /// props (relic switches, lantern, frost, seal, ribbons and page flips) live in MenuSkinProps.cs.
    /// Each host owns one skin, calls <see cref="Ensure"/> from OnGUI and <see cref="Dispose"/> when destroyed.
    /// Every draw is multiplied by <see cref="Fade"/> so a host can ease whole layers in and out.
    /// </summary>
    internal sealed partial class MenuSkin : IDisposable
    {
        private const int MoteCount = 18;

        private readonly List<Texture2D> textures = new List<Texture2D>();
        private MenuSparks sparks;
        private Texture2D wandCursor;
        private Vector2 lastTrail;
        private bool pointerKnown;
        private bool cursorShown;

        public float Fade { get; set; } = 1f;
        public Vector2 Parallax { get; private set; }
        /// <summary>0 when the player has turned on Reduce motion, otherwise 1. Hosts scale their rises and settles by it.</summary>
        public float Motion => GameSettingsPanel.ReduceMotion ? 0f : 1f;
        public bool IsReady => Title != null;

        public Texture2D Backdrop { get; private set; }
        public Texture2D Glow { get; private set; }
        public Texture2D Sparkle { get; private set; }
        private Texture2D knob, switchTrack;

        public GUIStyle Panel { get; private set; }
        public GUIStyle PanelTrim { get; private set; }
        public GUIStyle Card { get; private set; }
        /// <summary>Padded card for GUILayout lists, such as journal entries.</summary>
        public GUIStyle Entry { get; private set; }
        public GUIStyle Pill { get; private set; }
        public GUIStyle Title { get; private set; }
        public GUIStyle Heading { get; private set; }
        public GUIStyle Row { get; private set; }
        public GUIStyle Value { get; private set; }
        public GUIStyle Body { get; private set; }
        public GUIStyle Muted { get; private set; }
        public GUIStyle Small { get; private set; }
        public GUIStyle SmallRight { get; private set; }
        public GUIStyle SmallCentre { get; private set; }
        public GUIStyle Status { get; private set; }
        public GUIStyle EntryTitle { get; private set; }
        public GUIStyle Message { get; private set; }
        public GUIStyle TextButton { get; private set; }
        public GUIStyle Key { get; private set; }
        public GUIStyle Tab { get; private set; }
        public GUIStyle Nav { get; private set; }
        private GUIStyle sliderStyle, thumbStyle, scrollbarStyle, scrollThumbStyle;

        /// <summary>Creates the art and styles on first use. Styles copy GUI.skin, so this must run inside OnGUI.</summary>
        public void Ensure()
        {
            if (IsReady) { ApplyAccessibility(); return; }
            Font font = Resources.Load<Font>("Dialogue/Nunito-Regular");
            Backdrop = Track(MenuArt.Backdrop(32));
            Glow = Track(MenuArt.Glow(64));
            Sparkle = Track(GameSaveOverlay.CreateSparkleTexture(64));
            knob = Track(MenuArt.Rounded(32, 32, 16f, Color.white, new Color(0.62f, 0.5f, 0.32f), 2f));
            switchTrack = Track(MenuArt.Rounded(60, 30, 15f, Color.white, Color.clear, 0f));
            wandCursor = Track(MenuArt.WandCursor());
            sparks = new MenuSparks(MoteCount);
            EnsureProps();

            Color goldTrim = MenuArt.WithAlpha(MenuArt.Gold, 0.32f);
            Panel = Sliced(MenuArt.Rounded(64, 64, 20f, MenuArt.PanelFill, MenuArt.Gold, 2f), 24);
            PanelTrim = Sliced(MenuArt.Rounded(64, 64, 15f, Color.clear, MenuArt.WithAlpha(MenuArt.Gold, 0.3f), 1f), 20);
            Card = Sliced(MenuArt.Rounded(32, 32, 12f, new Color(0.18f, 0.14f, 0.27f, 0.85f), goldTrim, 1.5f), 14);
            Entry = new GUIStyle(Card) { padding = new RectOffset(16, 16, 10, 12), margin = new RectOffset(0, 4, 0, 10) };
            Pill = Sliced(MenuArt.Rounded(20, 10, 5f, Color.white, Color.clear, 0f), 5);

            Title = Text(font, 18, TextAnchor.MiddleCenter, MenuArt.Gold);
            Title.fontStyle = FontStyle.Bold;
            Heading = Text(font, 15, TextAnchor.MiddleLeft, MenuArt.Gold);
            Heading.fontStyle = FontStyle.Bold;
            Row = Text(font, 19, TextAnchor.MiddleLeft, MenuArt.Cream);
            Value = Text(font, 19, TextAnchor.MiddleRight, MenuArt.Gold);
            Value.fontStyle = FontStyle.Bold;
            Body = Text(font, 17, TextAnchor.UpperLeft, MenuArt.Cream);
            Body.wordWrap = true;
            Muted = Text(font, 15, TextAnchor.UpperLeft, MenuArt.Muted);
            Muted.wordWrap = true;
            Small = Text(font, 13, TextAnchor.MiddleLeft, MenuArt.Muted);
            SmallRight = Text(font, 13, TextAnchor.MiddleRight, MenuArt.Muted);
            SmallCentre = Text(font, 14, TextAnchor.MiddleCenter, MenuArt.Muted);
            Status = Text(font, 14, TextAnchor.MiddleLeft, MenuArt.Muted);
            EntryTitle = Text(font, 18, TextAnchor.UpperLeft, MenuArt.Gold);
            EntryTitle.fontStyle = FontStyle.Bold;
            Message = Text(font, 16, TextAnchor.MiddleCenter, MenuArt.Ember);
            Message.wordWrap = true;
            Message.padding = new RectOffset(10, 10, 4, 4);

            TextButton = new GUIStyle(GUI.skin.button)
            {
                font = font,
                fontSize = 17,
                alignment = TextAnchor.MiddleCenter,
                border = new RectOffset(12, 12, 12, 12),
                padding = new RectOffset(12, 12, 6, 6),
                normal = { background = Track(MenuArt.Rounded(32, 32, 10f, new Color(0.2f, 0.16f, 0.3f, 0.95f), MenuArt.WithAlpha(MenuArt.Gold, 0.55f), 1.5f)), textColor = MenuArt.Cream },
                hover = { background = Track(MenuArt.Rounded(32, 32, 10f, new Color(0.28f, 0.21f, 0.4f, 0.98f), MenuArt.Gold, 2f)), textColor = MenuArt.Gold },
                active = { background = Track(MenuArt.Rounded(32, 32, 10f, new Color(0.13f, 0.1f, 0.2f, 1f), MenuArt.Gold, 2f)), textColor = MenuArt.Gold }
            };
            TextButton.focused.background = TextButton.normal.background;
            TextButton.focused.textColor = MenuArt.Cream;
            Key = new GUIStyle(TextButton) { fontSize = 16 };
            Tab = new GUIStyle(GUI.skin.label)
            {
                font = font,
                fontSize = 18,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(14, 14, 4, 4)
            };

            Nav = new GUIStyle(Tab) { fontSize = 17, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(0, 4, 2, 2) };

            sliderStyle = new GUIStyle();
            thumbStyle = new GUIStyle { fixedWidth = 24f };
            scrollbarStyle = new GUIStyle(GUI.skin.verticalScrollbar)
            {
                fixedWidth = 10f,
                border = new RectOffset(5, 5, 5, 5),
                margin = new RectOffset(6, 2, 2, 2),
                normal = { background = Track(MenuArt.Rounded(10, 20, 5f, MenuArt.WithAlpha(MenuArt.Ink, 0.7f), Color.clear, 0f)) }
            };
            scrollThumbStyle = new GUIStyle(GUI.skin.verticalScrollbarThumb)
            {
                fixedWidth = 10f,
                border = new RectOffset(5, 5, 5, 5),
                normal = { background = Track(MenuArt.Rounded(10, 20, 5f, MenuArt.WithAlpha(MenuArt.Gold, 0.75f), Color.clear, 0f)) }
            };
            ApplyAccessibility();
        }

        public void Dispose()
        {
            ShowWandCursor(false);
            foreach (Texture2D texture in textures)
            {
                if (texture == null) continue;
                if (Application.isPlaying) UnityEngine.Object.Destroy(texture);
                else UnityEngine.Object.DestroyImmediate(texture);
            }
            textures.Clear();
            sparks = null;
            Title = null;
        }

        // ---- Ambience: pointer parallax, wand cursor, trail and click sparks ----

        public void ShowWandCursor(bool show)
        {
            if (show && wandCursor != null && !cursorShown) Cursor.SetCursor(wandCursor, new Vector2(5f, 5f), CursorMode.Auto);
            else if (!show && cursorShown) Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            cursorShown = show && wandCursor != null;
        }

        /// <summary>Call every frame while the menu is open. Uses unscaled time so it also works while the game is paused.</summary>
        public void Tick(float deltaTime)
        {
            if (sparks == null) return;
            sparks.Tick(deltaTime);
            Mouse mouse = Mouse.current;
            if (mouse == null) return;
            if (GameSettingsPanel.ReduceMotion)
            {
                Parallax = Vector2.zero;
                pointerKnown = false;
                return;
            }
            Vector2 raw = mouse.position.ReadValue();
            Vector2 position = new Vector2(raw.x, Screen.height - raw.y);
            if (!pointerKnown)
            {
                lastTrail = position;
                pointerKnown = true;
            }
            Vector2 target = new Vector2(Mathf.Clamp(position.x / Mathf.Max(1f, Screen.width) * 2f - 1f, -1f, 1f),
                Mathf.Clamp(position.y / Mathf.Max(1f, Screen.height) * 2f - 1f, -1f, 1f));
            Parallax = Vector2.Lerp(Parallax, target, 1f - Mathf.Exp(-2.5f * deltaTime));

            float moved = Vector2.Distance(position, lastTrail);
            if (moved > 240f) lastTrail = position;
            while (moved >= 12f && moved <= 240f)
            {
                lastTrail = Vector2.MoveTowards(lastTrail, position, 12f);
                moved -= 12f;
                Color colour = sparks.Next() < 0.5f ? MenuArt.Gold : sparks.Next() < 0.5f ? MenuArt.Lavender : MenuArt.Cream;
                sparks.Emit(lastTrail + new Vector2(sparks.Range(-3f, 3f), sparks.Range(-3f, 3f)),
                    new Vector2(sparks.Range(-14f, 14f), sparks.Range(-6f, 22f)), sparks.Range(0.3f, 0.6f),
                    sparks.Range(3f, 6f), colour, sparks.Next() < 0.25f, 2.5f, 25f);
            }
            if (mouse.leftButton.wasPressedThisFrame) sparks.Burst(position, 12, 60f, 160f, 7f);
        }

        /// <summary>Clears trail and sparks so reopening the menu does not replay stale light.</summary>
        public void ResetAmbience()
        {
            sparks?.Clear();
            pointerKnown = false;
            lanternLast = -1f;
        }

        /// <summary>Dusk wash over the world plus drifting lantern motes. The world stays visible underneath.</summary>
        public void DrawBackdrop(float alpha, float now)
        {
            if (Event.current.type != EventType.Repaint || !IsReady) return;
            Draw(new Rect(0f, 0f, Screen.width, Screen.height), Backdrop, Color.white, alpha);
            if (Motion > 0f) sparks.DrawMotes(now, alpha * Fade, Parallax, Glow, Sparkle);
        }

        public void DrawSparks(float alpha)
        {
            if (Event.current.type != EventType.Repaint || sparks == null) return;
            sparks.DrawSparks(alpha, Glow, Sparkle);
        }

        // ---- Frame pieces ----

        /// <summary>Plum panel with a gold border, a thin inner trim and stars twinkling in the trim corners.</summary>
        public void PanelFrame(Rect panel, float now)
        {
            if (Event.current.type != EventType.Repaint) return;
            Draw(Expand(panel, 70f, 70f), Glow, MenuArt.Lavender, 0.22f);
            Box(panel, Panel, 1f);
            Rect trim = Expand(panel, -8f, -8f);
            Box(trim, PanelTrim, 1f);
            for (int i = 0; i < 4; i++)
            {
                Vector2 corner = new Vector2(i % 2 == 0 ? trim.x : trim.xMax, i < 2 ? trim.y : trim.yMax);
                float twinkle = 0.5f + 0.5f * Mathf.Sin(now * 1.7f + i * 1.9f);
                float size = 12f + 6f * twinkle;
                Draw(new Rect(corner.x - size * 0.5f, corner.y - size * 0.5f, size, size), Sparkle, MenuArt.Gold, 0.45f + 0.5f * twinkle);
            }
        }

        /// <summary>Gold heading with a soft pulsing lavender glow behind it.</summary>
        public void DrawTitle(Rect rect, string text, float now)
        {
            DrawTitle(rect, text, now, Title);
        }

        /// <summary>Gold heading using a caller-provided layout style, such as a wrapped left-aligned card title.</summary>
        public void DrawTitle(Rect rect, string text, float now, GUIStyle style)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(now * 1.6f);
            if (Event.current.type == EventType.Repaint)
                Draw(Expand(rect, -rect.width * 0.3f, 2f), Glow, MenuArt.Lavender, 0.2f + 0.12f * pulse);
            Label(new Rect(rect.x, rect.y + 2f, rect.width, rect.height), text, style, MenuArt.Ink, 0.6f);
            Label(rect, text, style, Color.Lerp(MenuArt.Gold, MenuArt.Cream, 0.15f * pulse));
        }

        /// <summary>A soft gold rule with a gently rocking star in the middle.</summary>
        public void Divider(float centreX, float y, float width, float now)
        {
            if (Event.current.type != EventType.Repaint) return;
            Draw(new Rect(centreX - width * 0.5f, y - 2f, width, 4f), Glow, MenuArt.Gold, 0.85f);
            for (int side = -1; side <= 1; side += 2)
            {
                float twinkle = 0.5f + 0.5f * Mathf.Sin(now * 2.2f + side);
                Draw(new Rect(centreX + side * width * 0.32f - 4f, y - 4f, 8f, 8f), Glow, MenuArt.Cream, 0.5f + 0.4f * twinkle);
            }
            Rect star = new Rect(centreX - 12f, y - 12f, 24f, 24f);
            Matrix4x4 matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Sin(now * 0.8f) * 12f * Motion, star.center);
            Draw(Expand(star, 10f, 10f), Glow, MenuArt.Gold, 0.5f);
            Draw(star, Sparkle, MenuArt.Gold, 1f);
            GUI.matrix = matrix;
        }

        /// <summary>Small gold section label with a short star rule after it.</summary>
        public void SectionHeading(Rect rect, string text)
        {
            Label(rect, text.ToUpperInvariant(), Heading, MenuArt.Gold);
            if (Event.current.type != EventType.Repaint) return;
            float textWidth = Heading.CalcSize(new GUIContent(text.ToUpperInvariant())).x;
            float x = rect.x + textWidth + 12f;
            if (rect.xMax - x < 24f) return;
            Draw(new Rect(x, rect.center.y - 6f, 12f, 12f), Sparkle, MenuArt.Gold, 0.8f);
            Draw(new Rect(x + 16f, rect.center.y - 1.5f, rect.xMax - x - 16f, 3f), Glow, MenuArt.Gold, 0.35f);
        }

        // ---- Controls ----

        /// <summary>Rounded text button that lifts slightly and glows lavender while hovered.</summary>
        public bool Button(Rect rect, string text)
        {
            bool hover = rect.Contains(Event.current.mousePosition);
            if (hover) HoverGlow(Expand(rect, 8f, 8f));
            Rect drawRect = hover ? new Rect(rect.x, rect.y - 2f, rect.width, rect.height) : rect;
            SetColor(Color.white, 1f);
            return GUI.Button(drawRect, text, TextButton);
        }

        /// <summary>A tab label: gold with a glowing underline when active, cream with a soft glow when hovered.</summary>
        public bool TabButton(Rect rect, string text, bool active)
        {
            bool hover = rect.Contains(Event.current.mousePosition);
            if (active && Event.current.type == EventType.Repaint) Draw(Expand(rect, 6f, -2f), Glow, MenuArt.Gold, 0.18f);
            else if (hover) HoverGlow(rect);
            Label(rect, text, Tab, active ? MenuArt.Gold : hover ? MenuArt.Cream : MenuArt.Muted);
            SetColor(Color.white, 1f);
            return GUI.Button(rect, GUIContent.none, GUIStyle.none);
        }

        /// <summary>Glowing underline with a star, drawn under the active tab. The host animates its position.</summary>
        public void TabUnderline(float centreX, float y, float width)
        {
            if (Event.current.type != EventType.Repaint) return;
            Draw(new Rect(centreX - width * 0.5f, y - 2f, width, 4f), Glow, MenuArt.Gold, 0.95f);
            Draw(new Rect(centreX - 7f, y - 7f, 14f, 14f), Sparkle, MenuArt.Gold, 1f);
        }

        /// <summary>Gold volume-style slider: a dark track, a lavender fill and a glowing gold knob.</summary>
        public float Slider(Rect rect, float value)
        {
            const float KnobSize = 24f;
            SetColor(Color.white, 1f);
            float next = GUI.HorizontalSlider(rect, value, 0f, 1f, sliderStyle, thumbStyle);

            Rect track = new Rect(rect.x + KnobSize * 0.5f, rect.center.y - 5f, rect.width - KnobSize, 10f);
            Box(track, Pill, 0.9f, MenuArt.Ink);
            float filled = track.width * next;
            if (filled >= 10f) Box(new Rect(track.x, track.y, filled, track.height), Pill, 0.95f, MenuArt.Lavender);

            float knobX = rect.x + KnobSize * 0.5f + next * (rect.width - KnobSize);
            Draw(new Rect(knobX - 20f, rect.center.y - 20f, 40f, 40f), Glow, MenuArt.Gold, 0.5f);
            Draw(new Rect(knobX - KnobSize * 0.5f, rect.center.y - KnobSize * 0.5f, KnobSize, KnobSize), knob, MenuArt.Gold, 1f);
            return next;
        }

        /// <summary>Swaps in the themed vertical scrollbar until disposed; wrap scroll views in a using block.</summary>
        public ScrollbarScope ThemedScrollbars() => new ScrollbarScope(scrollbarStyle, scrollThumbStyle);

        public readonly struct ScrollbarScope : IDisposable
        {
            private readonly GUIStyle previousBar;
            private readonly GUIStyle previousThumb;

            public ScrollbarScope(GUIStyle bar, GUIStyle thumb)
            {
                previousBar = GUI.skin.verticalScrollbar;
                previousThumb = GUI.skin.verticalScrollbarThumb;
                GUI.skin.verticalScrollbar = bar;
                GUI.skin.verticalScrollbarThumb = thumb;
            }

            public void Dispose()
            {
                GUI.skin.verticalScrollbar = previousBar;
                GUI.skin.verticalScrollbarThumb = previousThumb;
            }
        }

        // ---- Drawing helpers ----

        public void SetColor(Color tint, float alpha) => GUI.color = new Color(tint.r, tint.g, tint.b, tint.a * alpha * Fade);

        public void Draw(Rect rect, Texture texture, Color tint, float alpha)
        {
            if (texture == null) return;
            SetColor(tint, alpha);
            GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, true);
        }

        public void Box(Rect rect, GUIStyle style, float alpha) => Box(rect, style, alpha, Color.white);

        public void Box(Rect rect, GUIStyle style, float alpha, Color tint)
        {
            if (Event.current.type != EventType.Repaint) return;
            SetColor(tint, alpha);
            style.Draw(rect, GUIContent.none, false, false, false, false);
        }

        public void Label(Rect rect, string text, GUIStyle style, Color colour, float alpha = 1f)
        {
            SetColor(Color.white, alpha);
            style.normal.textColor = AccessibilitySettings.Current.HighContrast ? Color.white : colour;
            GUI.Label(rect, text, style);
        }

        private void ApplyAccessibility()
        {
            float scale = AccessibilitySettings.Current.TextScale;
            Row.fontSize = Mathf.RoundToInt(19f * scale);
            Body.fontSize = Mathf.RoundToInt(17f * scale);
            Small.fontSize = SmallRight.fontSize = Mathf.RoundToInt(13f * scale);
            SmallCentre.fontSize = Mathf.RoundToInt(14f * scale);
            Key.fontSize = Mathf.RoundToInt(16f * scale);
            TextButton.fontSize = Mathf.RoundToInt(17f * scale);
            bool contrast = AccessibilitySettings.Current.HighContrast;
            TextButton.normal.textColor = contrast ? Color.white : MenuArt.Cream;
            TextButton.hover.textColor = TextButton.active.textColor = contrast ? Color.white : MenuArt.Gold;
        }

        public static Rect Expand(Rect rect, float x, float y) => new Rect(rect.x - x, rect.y - y, rect.width + x * 2f, rect.height + y * 2f);

        public static float Smooth(float t) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));

        private static GUIStyle Text(Font font, int size, TextAnchor alignment, Color colour)
        {
            GUIStyle style = new GUIStyle(GUI.skin.label) { fontSize = size, alignment = alignment, wordWrap = false, normal = { textColor = colour } };
            if (font != null) style.font = font;
            return style;
        }

        private GUIStyle Sliced(Texture2D texture, int border)
        {
            return new GUIStyle { normal = { background = Track(texture) }, border = new RectOffset(border, border, border, border) };
        }

        private Texture2D Track(Texture2D texture)
        {
            if (texture != null && !textures.Contains(texture)) textures.Add(texture);
            return texture;
        }
    }
}
