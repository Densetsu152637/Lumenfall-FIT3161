using UnityEngine;
using Lumenfall.GameEvents;
using Lumenfall.Controls;

namespace Lumenfall.Startup
{
    /// <summary>
    /// Themed props for the in-game menus, tied to the game's magic: Ampere relic switches, the volume lantern, Glaciate
    /// frost for the pause, the hourglass, the wax seal, inked text, silk bookmark ribbons with runes, page flips and
    /// candle-flicker hovers. Every animation settles to its end state when Reduce motion is on.
    /// </summary>
    internal sealed partial class MenuSkin
    {
        // Warm candlelight used for every hover glow.
        public static readonly Color Candle = Color.Lerp(MenuArt.Ember, MenuArt.Gold, 0.4f);
        private static readonly Color SealWax = new Color(0.86f, 0.6f, 0.3f);
        private static readonly Color SocketColour = new Color(0.2f, 0.16f, 0.28f);
        private static readonly Color SmokeColour = new Color(0.72f, 0.7f, 0.8f);

        // Glints sit along the frost edges, in 0..1 coordinates of the frost rectangle.
        private static readonly Vector2[] FrostGlints =
        {
            new Vector2(0.04f, 0.12f), new Vector2(0.1f, 0.03f), new Vector2(0.03f, 0.55f), new Vector2(0.06f, 0.88f),
            new Vector2(0.2f, 0.96f), new Vector2(0.5f, 0.03f), new Vector2(0.78f, 0.04f), new Vector2(0.96f, 0.1f),
            new Vector2(0.97f, 0.45f), new Vector2(0.94f, 0.86f), new Vector2(0.82f, 0.97f), new Vector2(0.35f, 0.97f)
        };

        private Texture2D orb, lantern, frost, hourglass, waxSeal, ribbon, flat, line;
        private float lanternLast = -1f;
        private float lastSealBurst = float.NaN;

        private void EnsureProps()
        {
            orb = Track(MenuProps.Orb(32));
            lantern = Track(MenuProps.Lantern());
            hourglass = Track(MenuProps.Hourglass());
            waxSeal = Track(MenuProps.WaxSeal(48));
            ribbon = Track(MenuProps.Ribbon(96, 32, 10f));
            flat = Track(MenuProps.Flat());
            line = Track(MenuArt.Line());
        }

        // ---- Candlelight ----

        /// <summary>A gentle candle flicker between roughly 0.5 and 0.95; steady when Reduce motion is on.</summary>
        public float Flicker(float seed)
        {
            if (GameSettingsPanel.ReduceMotion) return 0.85f;
            float t = Time.unscaledTime;
            return 0.72f + 0.12f * Mathf.Sin(t * 9.1f + seed) + 0.07f * Mathf.Sin(t * 14.7f + seed * 2.3f) + 0.04f * Mathf.Sin(t * 23.3f + seed * 3.7f);
        }

        /// <summary>Warm, flickering hover glow behind (or softly over) a row or button.</summary>
        public void HoverGlow(Rect rect)
        {
            if (Event.current.type != EventType.Repaint) return;
            Draw(Expand(rect, 16f, 8f), Glow, Candle, 0.36f * Flicker(rect.x * 0.013f + rect.y * 0.029f));
        }

        // ---- Ampere relic switch ----

        /// <summary>
        /// An on/off switch drawn as a relic in a socket track. Off, the relic rests left, dim amber, with an idle amber
        /// thread; on, it slides right, cyan and humming, with a powered cyan thread behind it, like a weave cable.
        /// <paramref name="on"/> is the animated position from 0 to 1.
        /// </summary>
        public void Switch(Rect rect, float on)
        {
            if (Event.current.type != EventType.Repaint) return;
            Color idle = new Color(MenuArt.ThreadIdle.r * 0.62f, MenuArt.ThreadIdle.g * 0.55f, MenuArt.ThreadIdle.b * 0.5f);
            Color relic = Color.Lerp(idle, MenuArt.ThreadLive, on);
            Draw(rect, switchTrack, Color.Lerp(new Color(0.12f, 0.09f, 0.18f), new Color(0.08f, 0.2f, 0.26f), on), 1f);

            float left = rect.x + rect.height * 0.5f, right = rect.xMax - rect.height * 0.5f;
            float orbX = Mathf.Lerp(left, right, on);
            float y = rect.center.y;
            if (orbX - left > 1f) Draw(new Rect(left, y - 1.5f, orbX - left, 3f), flat, MenuArt.ThreadLive, 0.7f * on);
            if (right - orbX > 1f) Draw(new Rect(orbX, y - 1f, right - orbX, 2f), flat, MenuArt.ThreadIdle, 0.45f * (1f - on));

            float hum = Motion > 0f ? 0.82f + 0.18f * Mathf.Sin(Time.unscaledTime * 5.2f + rect.y * 0.05f) : 1f;
            Rect orbRect = new Rect(orbX - 11f, y - 11f, 22f, 22f);
            Draw(Expand(orbRect, 14f, 14f), Glow, relic, (0.15f + 0.5f * on) * hum);
            Draw(Expand(orbRect, 2f, 2f), knob, SocketColour, 1f);
            Draw(orbRect, orb, relic, 1f);
            if (on > 0.3f) Draw(new Rect(orbRect.x + 4f, orbRect.y + 3f, 7f, 7f), Glow, Color.white, 0.7f * on * hum);
        }

        /// <summary>Sends a little spark along the track into the relic's new position when a switch is toggled.</summary>
        public void SwitchSpark(Rect rect, bool nowOn)
        {
            if (sparks == null || Motion <= 0f) return;
            float left = rect.x + rect.height * 0.5f, right = rect.xMax - rect.height * 0.5f;
            Vector2 from = new Vector2(nowOn ? left : right, rect.center.y);
            Vector2 to = new Vector2(nowOn ? right : left, rect.center.y);
            Color colour = nowOn ? MenuArt.ThreadLive : MenuArt.ThreadIdle;
            for (int i = 0; i < 10; i++)
            {
                Vector2 velocity = (to - from) / 0.22f * sparks.Range(0.8f, 1.1f) + new Vector2(sparks.Range(-20f, 20f), sparks.Range(-30f, 10f));
                sparks.Emit(from + new Vector2(sparks.Range(-2f, 2f), sparks.Range(-2f, 2f)), velocity, sparks.Range(0.25f, 0.4f),
                    sparks.Range(3f, 6f), colour, sparks.Next() < 0.3f, 3f, -10f);
            }
            sparks.Burst(to, 8, 30f, 90f, 5f);
        }

        // ---- Volume lantern ----

        /// <summary>The gold slider with a lantern at its end. The flame grows with the value and goes out in smoke at 0.</summary>
        public float LanternSlider(Rect rect, float value)
        {
            const float LanternWidth = 28f;
            float next = Slider(new Rect(rect.x, rect.y, rect.width - LanternWidth - 14f, rect.height), value);
            DrawLantern(new Rect(rect.xMax - LanternWidth, rect.center.y - 27f, LanternWidth, 42f), next);
            return next;
        }

        private void DrawLantern(Rect rect, float level)
        {
            // The flame sits in the middle of the glass (rows 13 to 37 of the 48-pixel lantern).
            Vector2 flame = new Vector2(rect.center.x, rect.y + rect.height * (28f / 48f));
            if (lanternLast > 0.001f && level <= 0.001f) Smoke(flame);
            lanternLast = level;
            if (Event.current.type != EventType.Repaint) return;

            bool lit = level > 0.001f;
            float flicker = Flicker(3.1f);
            float strength = Mathf.Lerp(0.35f, 1f, level);
            if (lit)
            {
                Draw(Expand(rect, 18f * strength, 14f * strength), Glow, MenuArt.Ember, 0.4f * strength * flicker);
                Rect glass = new Rect(rect.x + rect.width * 0.25f, rect.y + rect.height * (13f / 48f), rect.width * 0.5f, rect.height * 0.5f);
                Draw(glass, Glow, MenuArt.Ember, 0.55f * strength * flicker);
            }
            Draw(rect, lantern, MenuArt.Gold, lit ? 1f : 0.7f);
            if (!lit) return;
            float height = Mathf.Lerp(6f, 15f, level) * (0.88f + 0.16f * flicker);
            float width = height * 0.55f;
            Draw(new Rect(flame.x - width * 0.5f, flame.y - height * 0.75f, width, height), Glow, MenuArt.Ember, 0.95f);
            Draw(new Rect(flame.x - width * 0.22f, flame.y - height * 0.45f, width * 0.44f, height * 0.6f), Glow, MenuArt.Cream, 1f);
        }

        private void Smoke(Vector2 at)
        {
            if (sparks == null || Motion <= 0f) return;
            for (int i = 0; i < 7; i++)
            {
                sparks.Emit(at + new Vector2(sparks.Range(-2f, 2f), -4f - i * 2f), new Vector2(sparks.Range(-8f, 8f), sparks.Range(-38f, -22f)),
                    sparks.Range(0.9f, 1.4f), sparks.Range(6f, 11f), SmokeColour, false, 0.6f, -6f);
            }
        }

        // ---- Glaciate frost ----

        /// <summary>
        /// Frost over the frozen world. <paramref name="amount"/> 0 to 1 creeps it in from the screen edges by drawing the
        /// vignette larger than the screen and shrinking it inward; glints twinkle along the edges.
        /// </summary>
        public void DrawFrost(float amount)
        {
            if (amount <= 0.001f || Event.current.type != EventType.Repaint || !IsReady) return;
            if (frost == null) frost = Track(MenuProps.Frost(256));
            float savedFade = Fade;
            Fade = 1f;
            float spread = (1f - amount) * 0.4f;
            Rect rect = new Rect(-UiScale.Width * spread, -UiScale.Height * spread, UiScale.Width * (1f + spread * 2f), UiScale.Height * (1f + spread * 2f));
            Draw(rect, frost, Color.white, 0.9f * Mathf.Clamp01(amount * 1.4f));

            float now = Time.unscaledTime;
            Color glint = new Color(0.9f, 0.97f, 1f);
            for (int i = 0; i < FrostGlints.Length; i++)
            {
                Vector2 point = new Vector2(Mathf.Lerp(rect.x, rect.xMax, FrostGlints[i].x), Mathf.Lerp(rect.y, rect.yMax, FrostGlints[i].y));
                float twinkle = Motion > 0f ? 0.5f + 0.5f * Mathf.Sin(now * 2.3f + i * 1.7f) : 0.7f;
                float size = 10f + 8f * twinkle;
                Draw(new Rect(point.x - size * 0.5f, point.y - size * 0.5f, size, size), Sparkle, glint, amount * (0.35f + 0.5f * twinkle));
            }
            Fade = savedFade;
        }

        // ---- Hourglass, wax seal and ink ----

        public void DrawHourglass(Rect rect)
        {
            if (Event.current.type != EventType.Repaint) return;
            Draw(Expand(rect, 8f, 6f), Glow, MenuArt.Gold, 0.25f);
            Draw(rect, hourglass, Color.white, 1f);
        }

        /// <summary>A wax seal pressed at <paramref name="stampedAt"/>: it drops in large, lands with a few sparks and glows.</summary>
        public void DrawSeal(Rect rect, float stampedAt)
        {
            if (Event.current.type != EventType.Repaint) return;
            float now = Time.unscaledTime;
            float t = Motion > 0f ? (now - stampedAt) / 0.28f : 1f;
            if (t >= 1f && lastSealBurst != stampedAt)
            {
                lastSealBurst = stampedAt;
                if (Motion > 0f) sparks?.Burst(rect.center, 10, 40f, 110f, 5f);
            }
            float since = now - stampedAt - 0.28f;
            if (since > 0f) Draw(Expand(rect, 14f, 14f), Glow, MenuArt.Gold, 0.7f * Mathf.Exp(-since * 2.5f));
            float scale = Mathf.Lerp(1.8f, 1f, Smooth(t));
            Rect pressed = Expand(rect, rect.width * (scale - 1f) * 0.5f, rect.height * (scale - 1f) * 0.5f);
            Draw(new Rect(pressed.x + 1f, pressed.y + 2f, pressed.width, pressed.height), waxSeal, MenuArt.Ink, 0.4f * Mathf.Clamp01(t));
            Draw(pressed, waxSeal, SealWax, Mathf.Clamp01(t * 1.6f));
        }

        /// <summary>
        /// Left-aligned text that writes itself in from left to right after <paramref name="inkedAt"/>, with a glowing nib,
        /// then dries from cream to <paramref name="dry"/>.
        /// </summary>
        public void InkLabel(Rect rect, string text, GUIStyle style, float inkedAt, Color dry)
        {
            float now = Time.unscaledTime;
            float duration = 0.25f + 0.025f * text.Length;
            float written = Motion > 0f ? Mathf.Clamp01((now - inkedAt) / duration) : 1f;
            float dried = Motion > 0f ? Smooth((now - inkedAt - duration) / 0.9f) : 1f;
            float textWidth = style.CalcSize(new GUIContent(text)).x;
            GUI.BeginGroup(new Rect(rect.x, rect.y, textWidth * written + 1f, rect.height));
            Label(new Rect(0f, 0f, rect.width, rect.height), text, style, Color.Lerp(MenuArt.Cream, dry, dried));
            GUI.EndGroup();
            if (written < 1f && Event.current.type == EventType.Repaint)
                Draw(new Rect(rect.x + textWidth * written - 4f, rect.center.y - 4f, 8f, 8f), Glow, MenuArt.Gold, 0.9f);
        }

        // ---- Spellbook ribbons and runes ----

        /// <summary>The spellbook spine the section ribbons hang from: a plum band with gold edges and stitching.</summary>
        public void Spine(Rect rect)
        {
            if (Event.current.type != EventType.Repaint) return;
            Box(rect, Pill, 1f, new Color(0.17f, 0.12f, 0.26f));
            Draw(new Rect(rect.x, rect.y + 4f, 1.5f, rect.height - 8f), flat, MenuArt.Gold, 0.55f);
            Draw(new Rect(rect.xMax - 1.5f, rect.y + 4f, 1.5f, rect.height - 8f), flat, MenuArt.Gold, 0.55f);
            for (float y = rect.y + 10f; y < rect.yMax - 6f; y += 14f)
                Draw(new Rect(rect.center.x - 2f, y - 2f, 4f, 4f), Glow, MenuArt.Gold, 0.8f);
        }

        /// <summary>
        /// A silk bookmark ribbon with a rune and a label. The active ribbon is brighter, its rune lights gold, and it has a
        /// soft gold glow; hovering adds candlelight. Returns true when clicked.
        /// </summary>
        public bool RibbonButton(Rect rect, string text, Color silk, Vector2[] rune, bool active)
        {
            bool hover = rect.Contains(Event.current.mousePosition);
            if (Event.current.type == EventType.Repaint)
            {
                if (active) Draw(Expand(rect, 10f, 6f), Glow, MenuArt.Gold, 0.22f);
                else if (hover) HoverGlow(rect);
                Color tint = active ? Color.Lerp(silk, Color.white, 0.14f) : new Color(silk.r * 0.8f, silk.g * 0.8f, silk.b * 0.8f);
                Draw(new Rect(rect.x + 2f, rect.y + 3f, rect.width, rect.height), ribbon, MenuArt.Ink, 0.45f);
                Draw(rect, ribbon, tint, 1f);
                DrawRune(rune, new Vector2(rect.x + 22f, rect.center.y), 8.5f, active ? MenuArt.Gold : MenuArt.Cream, active ? 1f : 0.65f, active);
            }
            Label(new Rect(rect.x + 40f, rect.y, rect.width - 58f, rect.height), text, Nav, active || hover ? MenuArt.Cream : MenuArt.WithAlpha(MenuArt.Cream, 0.8f));
            SetColor(Color.white, 1f);
            return GUI.Button(rect, GUIContent.none, GUIStyle.none);
        }

        /// <summary>Draws a rune given as line-segment pairs in a unit square (x right, y down) around <paramref name="centre"/>.</summary>
        public void DrawRune(Vector2[] segments, Vector2 centre, float scale, Color colour, float alpha, bool lit)
        {
            if (Event.current.type != EventType.Repaint || segments == null) return;
            if (lit) Draw(new Rect(centre.x - scale * 2f, centre.y - scale * 2f, scale * 4f, scale * 4f), Glow, MenuArt.Gold, 0.55f * Flicker(centre.y * 0.05f));
            for (int i = 0; i + 1 < segments.Length; i += 2)
                DrawLine(centre + segments[i] * scale, centre + segments[i + 1] * scale, 2f, colour, alpha);
        }

        private void DrawLine(Vector2 from, Vector2 to, float thickness, Color colour, float alpha)
        {
            Vector2 delta = to - from;
            float length = delta.magnitude;
            if (length < 0.01f) return;
            // Compose with the current matrix so runes stay put inside a page flip.
            Matrix4x4 matrix = GUI.matrix;
            GUI.matrix = matrix * Matrix4x4.TRS(from, Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg), Vector3.one) *
                Matrix4x4.TRS(-from, Quaternion.identity, Vector3.one);
            Draw(new Rect(from.x, from.y - thickness * 0.5f, length + 0.6f, thickness), line, colour, alpha);
            GUI.matrix = matrix;
        }

        // ---- Page flip ----

        /// <summary>
        /// Starts drawing a page that turns in from the spine on its left: the page is squeezed horizontally toward its left
        /// edge and opens out as <paramref name="progress"/> goes from 0 to 1. Pass the result to <see cref="EndPageFlip"/>.
        /// </summary>
        public Matrix4x4 BeginPageFlip(Rect page, float progress)
        {
            Matrix4x4 saved = GUI.matrix;
            if (progress >= 1f) return saved;
            float open = Mathf.Max(0.02f, Smooth(progress));
            Vector3 pivot = new Vector3(page.x, page.center.y, 0f);
            GUI.matrix = saved * Matrix4x4.TRS(pivot, Quaternion.identity, new Vector3(open, 1f, 1f)) * Matrix4x4.TRS(-pivot, Quaternion.identity, Vector3.one);
            return saved;
        }

        /// <summary>Restores the matrix and shades the turning page, with a bright edge where the paper catches the light.</summary>
        public void EndPageFlip(Rect page, float progress, Matrix4x4 saved)
        {
            GUI.matrix = saved;
            if (progress >= 1f || Event.current.type != EventType.Repaint) return;
            float open = Smooth(progress);
            float edge = page.x + page.width * open;
            Draw(new Rect(page.x, page.y, page.width * open, page.height), flat, MenuArt.Ink, 0.45f * (1f - open));
            Draw(new Rect(edge - 4f, page.y, 8f, page.height), Glow, MenuArt.Cream, 0.7f * (1f - open));
        }
    }
}
