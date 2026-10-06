using System;
using UnityEngine;
using Lumenfall.Controls;

namespace Lumenfall.Spells
{
    /// Draws the spell wheel as an animated rune circle. It blooms open and dissolves on close, two rune rings turn
    /// in opposite directions, each spell floats in an orb tinted by its element, a beam swings to the selection and
    /// motes drift up through the circle. Hovering an orb, or the sector it sits in, previews that spell in the core.
    /// Textures are generated on the first draw and released by Dispose.
    internal sealed class SpellWheelView : IDisposable
    {
        private const float OpenSeconds = 0.34f;
        private const float CloseSeconds = 0.2f;
        private const float RippleSeconds = 0.55f;
        private const int MoteCount = 22;
        private static readonly int SpellCount = Enum.GetValues(typeof(SpellId)).Length;
        private static readonly Color Veil = new Color(0.02f, 0.01f, 0.07f, 0.5f);
        private static readonly Color Plate = new Color(0.05f, 0.04f, 0.13f, 0.86f);
        private static readonly Color OrbFill = new Color(0.08f, 0.06f, 0.17f, 0.95f);
        private static readonly Color RuneColor = new Color(0.78f, 0.72f, 1f, 0.6f);
        private static readonly Color MutedText = new Color(0.8f, 0.78f, 0.92f, 1f);
        private readonly float[] hover = new float[SpellCount];
        private Texture2D glow, disc, ring, runes, sparkle, beam;
        private GUIStyle titleStyle, bodyStyle, smallStyle;
        private bool open, snapBeam;
        private float openedAt = -10f, closedAt = -10f, selectedAt = -10f, lastRepaint, beamAngle;
        private SpellId lastSelected;

        /// True while open, and briefly after closing so the wheel can fade out.
        public bool Visible => open || Time.unscaledTime - closedAt < CloseSeconds;

        public void Open()
        {
            if (open) return;
            open = true; snapBeam = true;
            openedAt = lastRepaint = Time.unscaledTime;
            Array.Clear(hover, 0, hover.Length);
        }
        public void Close() { if (!open) return; open = false; closedAt = Time.unscaledTime; }

        /// Runs one IMGUI pass and returns the spell clicked in this event, if any. Only repaint events draw.
        public SpellId? Draw(SpellId[] spells, SpellId selected, Func<SpellId, Texture2D> icon, string hint)
        {
            EnsureResources();
            Event current = Event.current;
            float now = Time.unscaledTime;
            float bloom, fade;
            if (open)
            {
                float t = Mathf.Clamp01((now - openedAt) / OpenSeconds);
                bloom = Mathf.LerpUnclamped(0.55f, 1f, EaseOutBack(t));
                fade = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t * 1.5f));
            }
            else
            {
                float t = Mathf.Clamp01((now - closedAt) / CloseSeconds);
                bloom = Mathf.Lerp(1f, 1.15f, t);
                fade = 1f - Mathf.SmoothStep(0f, 1f, t);
            }
            float ui = Mathf.Clamp(Screen.height / 900f, 0.8f, 1.6f);
            float s = ui * bloom;
            titleStyle.fontSize = bodyStyle.fontSize = Mathf.RoundToInt(15f * ui);
            smallStyle.fontSize = Mathf.RoundToInt(12f * ui);
            Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            int count = spells.Length;
            float step = Mathf.PI * 2f / count;
            float orbit = 120f * s, orbRadius = 30f * s, coreRadius = 60f * s;

            // The orb's whole sector is a target, so the pointer only needs to lean towards a spell.
            int hovered = -1;
            if (open)
            {
                Vector2 offset = current.mousePosition - center;
                float distance = offset.magnitude;
                if (distance > coreRadius && distance < orbit + orbRadius * 2.6f)
                    hovered = (Mathf.RoundToInt((Mathf.Atan2(offset.y, offset.x) + Mathf.PI * 0.5f) / step) % count + count) % count;
            }
            if (hovered >= 0 && current.type == EventType.MouseDown && current.button == 0) { current.Use(); return spells[hovered]; }
            if (current.type != EventType.Repaint) return null;

            float dt = Mathf.Clamp(now - lastRepaint, 0f, 0.1f);
            lastRepaint = now;
            if (selected != lastSelected) { lastSelected = selected; selectedAt = now; }
            int selectedIndex = Mathf.Max(0, Array.IndexOf(spells, selected));
            SpellId focus = hovered >= 0 ? spells[hovered] : selected;
            Color focusColor = ElementColor(focus), selectedColor = ElementColor(selected);
            for (int i = 0; i < count; i++)
            {
                int id = (int)spells[i];
                hover[id] = Mathf.MoveTowards(hover[id], i == hovered ? 1f : 0f, dt * 7f);
            }
            float targetAngle = (selectedIndex * step - Mathf.PI * 0.5f) * Mathf.Rad2Deg;
            if (snapBeam) { beamAngle = targetAngle; snapBeam = false; }
            else beamAngle += Mathf.DeltaAngle(beamAngle, targetAngle) * (1f - Mathf.Exp(-dt * 12f));
            float pulse = 0.5f + 0.5f * Mathf.Sin(now * 5f);
            Color previousColor = GUI.color;
            Matrix4x4 previousMatrix = GUI.matrix;

            // Veil, aura, back plate and the two counter-turning rune rings.
            GUI.color = Faded(Veil, fade);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            DrawAt(glow, center, 330f * s, WithAlpha(focusColor, 0.28f * fade));
            DrawAt(disc, center, 172f * s, Faded(Plate, fade));
            DrawSpun(runes, center, 190f * s, now * 6f, Faded(RuneColor, fade));
            DrawSpun(runes, center, 84f * s, -now * 11f, WithAlpha(selectedColor, 0.45f * fade));
            DrawAt(ring, center, orbit + 2f * s, WithAlpha(RuneColor, 0.3f * fade));

            for (int k = 0; k < MoteCount; k++)
            {
                float seedA = Hash(k * 3 + 1), seedB = Hash(k * 3 + 2), seedC = Hash(k * 3 + 3);
                float cycle = Mathf.Repeat(now * (0.08f + 0.1f * seedB) + seedC, 1f);
                Vector2 mote = new Vector2(center.x + (seedA * 2f - 1f) * 175f * s + Mathf.Sin(now * 1.3f + k) * 8f * s, center.y + 175f * s - cycle * 350f * s);
                float twinkle = 0.6f + 0.4f * Mathf.Sin(now * (3f + seedB * 4f) + k);
                DrawAt(sparkle, mote, (3f + 5f * seedC) * s, WithAlpha(Color.Lerp(Color.white, selectedColor, seedA), Mathf.Sin(cycle * Mathf.PI) * 0.75f * twinkle * fade));
            }

            // The beam runs under the orbs, from the core to the selected spell.
            GUIUtility.RotateAroundPivot(beamAngle, center);
            GUI.color = WithAlpha(selectedColor, (0.55f + 0.25f * pulse) * fade);
            GUI.DrawTexture(new Rect(center.x + coreRadius * 0.8f, center.y - 9f * s, orbit - coreRadius * 0.8f, 18f * s), beam);
            GUI.matrix = previousMatrix;

            Vector2 selectedPosition = center;
            for (int i = 0; i < count; i++)
            {
                SpellId spell = spells[i];
                bool isSelected = spell == selected;
                float h = hover[(int)spell], lit = Mathf.Max(h, isSelected ? 1f : 0f);
                Color color = ElementColor(spell);
                float angle = i * step - Mathf.PI * 0.5f;
                Vector2 position = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (orbit + Mathf.Sin(now * 2.2f + i * 1.3f) * 3f * s);
                if (isSelected) selectedPosition = position;
                float r = orbRadius * (1f + 0.16f * h + (isSelected ? 0.08f : 0f));
                DrawAt(glow, position, r * 2.4f, WithAlpha(color, (0.25f + 0.45f * h + (isSelected ? 0.35f + 0.15f * pulse : 0f)) * fade));
                DrawAt(disc, position, r, Faded(OrbFill, fade));
                DrawAt(ring, position, r, WithAlpha(isSelected ? Color.Lerp(color, Color.white, 0.35f) : color, (isSelected ? 1f : 0.45f + 0.5f * h) * fade));
                Texture2D texture = icon(spell);
                float iconSize = r * 1.25f;
                GUI.color = new Color(1f, 1f, 1f, fade);
                if (texture != null) GUI.DrawTexture(new Rect(position.x - iconSize * 0.5f, position.y - iconSize * 0.5f, iconSize, iconSize), texture, ScaleMode.ScaleToFit, true);
                else Label(new Rect(position.x - r, position.y - r, r * 2f, r * 2f), DisplayName(spell).Substring(0, 1), titleStyle, WithAlpha(color, fade));
                if (isSelected)
                    for (int m = 0; m < 3; m++)
                    {
                        float orbitAngle = now * 2.4f + m * Mathf.PI * 2f / 3f;
                        DrawAt(sparkle, position + new Vector2(Mathf.Cos(orbitAngle), Mathf.Sin(orbitAngle)) * r * 1.3f, 6f * s, WithAlpha(Color.Lerp(color, Color.white, 0.5f), 0.9f * fade));
                    }
                Label(new Rect(position.x - 60f * s, position.y + r + 1f * s, 120f * s, 18f * s), DisplayName(spell), smallStyle, WithAlpha(Color.Lerp(MutedText, Color.white, lit), (0.7f + 0.3f * lit) * fade));
                string key = KeyBindings.GetKeyLabel(GameAction.Spell1 + (int)spell);
                if (!string.IsNullOrEmpty(key) && key.Length <= 3)
                {
                    Vector2 badge = position + new Vector2(r * 0.74f, -r * 0.74f);
                    DrawAt(disc, badge, 9f * s, Faded(OrbFill, fade));
                    DrawAt(ring, badge, 9f * s, WithAlpha(color, 0.8f * fade));
                    Label(new Rect(badge.x - 9f * s, badge.y - 9f * s, 18f * s, 18f * s), key, smallStyle, WithAlpha(Color.white, 0.9f * fade));
                }
            }

            // A ripple spreads from a newly chosen spell.
            float ripple = (now - selectedAt) / RippleSeconds;
            if (ripple < 1f)
            {
                float eased = 1f - (1f - ripple) * (1f - ripple);
                DrawAt(ring, selectedPosition, orbRadius * Mathf.Lerp(1f, 2.4f, eased), WithAlpha(selectedColor, (1f - ripple) * fade));
                DrawAt(ring, center, coreRadius * Mathf.Lerp(1f, 1.6f, eased), WithAlpha(selectedColor, 0.6f * (1f - ripple) * fade));
            }

            // The core previews the hovered spell, or shows the selected one.
            DrawAt(glow, center, coreRadius * 1.9f, WithAlpha(focusColor, (0.45f + 0.15f * pulse) * fade));
            DrawAt(disc, center, coreRadius, new Color(0.06f, 0.05f, 0.14f, 0.97f * fade));
            DrawAt(ring, center, coreRadius, WithAlpha(focusColor, 0.9f * fade));
            Texture2D focusIcon = icon(focus);
            float focusSize = 46f * s * (1f + 0.04f * pulse);
            GUI.color = new Color(1f, 1f, 1f, fade);
            if (focusIcon != null) GUI.DrawTexture(new Rect(center.x - focusSize * 0.5f, center.y - 10f * s - focusSize * 0.5f, focusSize, focusSize), focusIcon, ScaleMode.ScaleToFit, true);
            Label(new Rect(center.x - coreRadius, center.y + 15f * s, coreRadius * 2f, 22f * s), DisplayName(focus), titleStyle, WithAlpha(Color.Lerp(focusColor, Color.white, 0.55f), fade));

            Label(new Rect(center.x - 220f * s, center.y + 196f * s, 440f * s, 22f * s), Description(focus), bodyStyle, WithAlpha(Color.Lerp(focusColor, Color.white, 0.6f), 0.95f * fade));
            Label(new Rect(center.x - 280f * s, center.y + 222f * s, 560f * s, 20f * s), hint, smallStyle, WithAlpha(MutedText, 0.8f * fade));
            GUI.color = previousColor;
            return null;
        }

        public void Dispose()
        {
            foreach (Texture2D texture in new[] { glow, disc, ring, runes, sparkle, beam })
                if (texture != null) UnityEngine.Object.Destroy(texture);
            glow = disc = ring = runes = sparkle = beam = null;
        }

        public static string DisplayName(SpellId spell) => spell == SpellId.LostEchoes ? "Lost Echoes" : spell.ToString();

        private static Color ElementColor(SpellId spell)
        {
            switch (spell)
            {
                case SpellId.Ampere: return new Color(1f, 0.86f, 0.36f);
                case SpellId.Weave: return new Color(0.96f, 0.52f, 0.86f);
                case SpellId.Gravitate: return new Color(0.62f, 0.5f, 1f);
                case SpellId.Rotara: return new Color(1f, 0.62f, 0.32f);
                case SpellId.Glaciate: return new Color(0.55f, 0.85f, 1f);
                case SpellId.Vis: return new Color(0.6f, 1f, 0.5f);
                case SpellId.LostEchoes: return new Color(0.4f, 0.95f, 0.85f);
                default: return Color.white;
            }
        }

        private static string Description(SpellId spell)
        {
            switch (spell)
            {
                case SpellId.Ampere: return "Charge a relic with power, or drain it.";
                case SpellId.Weave: return "Spin a cable of light between relics.";
                case SpellId.Gravitate: return "Push an object one step the way you face.";
                case SpellId.Rotara: return "Turn a gear a quarter turn.";
                case SpellId.Glaciate: return "Freeze an object and send it sliding.";
                case SpellId.Vis: return "Set a crooked thing true.";
                case SpellId.LostEchoes: return "Listen for hidden things nearby.";
                default: return string.Empty;
            }
        }

        private void EnsureResources()
        {
            if (titleStyle == null)
            {
                Font font = Resources.Load<Font>("Dialogue/Nunito-Regular");
                titleStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, clipping = TextClipping.Overflow, wordWrap = false };
                bodyStyle = new GUIStyle(titleStyle) { fontStyle = FontStyle.Italic };
                smallStyle = new GUIStyle(titleStyle) { fontStyle = FontStyle.Normal };
                foreach (GUIStyle style in new[] { titleStyle, bodyStyle, smallStyle })
                {
                    style.normal.textColor = Color.white;
                    if (font != null) style.font = font;
                }
            }
            if (glow != null) return;
            glow = Generate(128, (u, v, r) => Mathf.Pow(Mathf.Clamp01(1f - r), 2.2f));
            disc = Generate(128, (u, v, r) => (1f - r) * 64f);
            ring = Generate(128, (u, v, r) => (0.05f - Mathf.Abs(r - 0.93f)) * 64f);
            runes = Generate(512, RuneAlpha);
            sparkle = Generate(64, (u, v, r) => Mathf.Max(Mathf.Pow(Mathf.Clamp01(1f - r * 1.8f), 2f),
                Mathf.Exp(-Mathf.Abs(v) * 28f) * Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(u)), 3f),
                Mathf.Exp(-Mathf.Abs(u) * 28f) * Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(v)), 3f)));
            beam = Generate(64, (u, v, r) => Mathf.Exp(-v * v * 9f) * Mathf.Clamp01((1f - Mathf.Abs(u)) * 5f));
        }

        /// Two guide circles with short ticks between them, an outlined diamond glyph every 45 degrees and a dot
        /// between each pair of glyphs.
        private static float RuneAlpha(float u, float v, float r)
        {
            const float Pixels = 256f;
            const float GlyphStep = Mathf.PI / 4f, TickStep = Mathf.PI / 24f, GlyphRadius = 0.915f;
            float alpha = Mathf.Max((0.006f - Mathf.Abs(r - 0.965f)) * Pixels, (0.004f - Mathf.Abs(r - 0.865f)) * Pixels);
            float theta = Mathf.Atan2(v, u);
            float glyphOffset = theta - Mathf.Round(theta / GlyphStep) * GlyphStep;
            float along = Mathf.Abs(glyphOffset * r), across = Mathf.Abs(r - GlyphRadius), manhattan = along + across;
            float diamond = Mathf.Clamp01((0.032f - manhattan) * Pixels) * (1f - Mathf.Clamp01((0.018f - manhattan) * Pixels));
            diamond = Mathf.Max(diamond, (0.007f - Mathf.Sqrt(along * along + across * across)) * Pixels);
            float tick = theta - Mathf.Round(theta / TickStep) * TickStep;
            if (r > 0.865f && r < 0.89f && along > 0.045f) alpha = Mathf.Max(alpha, (0.004f - Mathf.Abs(tick * r)) * Pixels);
            float dotOffset = theta - GlyphStep * 0.5f;
            dotOffset = (dotOffset - Mathf.Round(dotOffset / GlyphStep) * GlyphStep) * r;
            float dot = (0.008f - Mathf.Sqrt(dotOffset * dotOffset + across * across)) * Pixels;
            return Mathf.Max(alpha, diamond, dot);
        }

        private static Texture2D Generate(int size, Func<float, float, float, float> alpha)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { name = "SpellWheel", hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha(u, v, Mathf.Sqrt(u * u + v * v))) * 255f));
                }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static void DrawAt(Texture2D texture, Vector2 center, float radius, Color color)
        { GUI.color = color; GUI.DrawTexture(new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f), texture); }
        private static void DrawSpun(Texture2D texture, Vector2 center, float radius, float degrees, Color color)
        { Matrix4x4 matrix = GUI.matrix; GUIUtility.RotateAroundPivot(degrees, center); DrawAt(texture, center, radius, color); GUI.matrix = matrix; }
        private static void Label(Rect rect, string text, GUIStyle style, Color color)
        {
            GUI.color = new Color(0f, 0f, 0f, color.a * 0.7f);
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1.5f, rect.width, rect.height), text, style);
            GUI.color = color;
            GUI.Label(rect, text, style);
        }
        private static Color WithAlpha(Color color, float alpha) { color.a = alpha; return color; }
        private static Color Faded(Color color, float fade) { color.a *= fade; return color; }
        private static float Hash(int n) => Mathf.Repeat(Mathf.Sin(n * 12.9898f) * 43758.5453f, 1f);
        private static float EaseOutBack(float t) { float x = t - 1f; return 1f + 2.70158f * x * x * x + 1.70158f * x * x; }
    }
}
