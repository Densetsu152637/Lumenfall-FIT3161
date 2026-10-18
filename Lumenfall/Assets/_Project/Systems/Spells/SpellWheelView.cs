using System;
using UnityEngine;
using Lumenfall.Controls;

namespace Lumenfall.Spells
{
    /// Draws the spell wheel as an animated rune circle, and the selected-spell chip in the bottom-right corner while
    /// the wheel is closed. The wheel blooms open as the spell orbs orbit into place and swings them onward as it
    /// closes; a clicked spell flies into the core and bursts into sparks. Rune rings turn, motes drift, a beam
    /// tracks the selection and every orb carries its spell's own motion (lightning, frost, gear, pull, echo, thread,
    /// level). Hovering an orb, or the sector it sits in, previews that spell in the core until the player scrolls.
    /// Textures are generated on the first draw and released by Dispose.
    internal sealed class SpellWheelView : IDisposable
    {
        private const float OpenSeconds = 0.34f;
        private const float CloseSeconds = 0.3f;
        private const float CommitSeconds = 0.7f;
        private const float FlyShare = 0.4f;
        private const float OrbitSeconds = 0.6f;
        private const float OrbitStagger = 0.05f;
        private const float OpenSweep = 220f;
        private const float CloseSweep = 100f;
        private const float RippleSeconds = 0.55f;
        private const float ChipPopSeconds = 0.45f;
        private const float EchoLag = 0.15f;
        private const int MoteCount = 22;
        private const int BurstSparks = 16;
        private static readonly int SpellCount = Enum.GetValues(typeof(SpellId)).Length;
        private static readonly Color Veil = new Color(0.02f, 0.01f, 0.07f, 0.5f);
        private static readonly Color Plate = new Color(0.05f, 0.04f, 0.13f, 0.86f);
        private static readonly Color OrbFill = new Color(0.08f, 0.06f, 0.17f, 0.95f);
        private static readonly Color RuneColor = new Color(0.78f, 0.72f, 1f, 0.6f);
        private static readonly Color MutedText = new Color(0.8f, 0.78f, 0.92f, 1f);
        private static readonly Color Frost = new Color(0.85f, 0.97f, 1f, 1f);
        private readonly float[] hover = new float[SpellCount];
        private readonly float[] presences = new float[SpellCount];
        private readonly Vector2[] slots = new Vector2[SpellCount];
        private Texture2D glow, disc, ring, runes, sparkle, beam, line, gear, frame;
        private GUIStyle titleStyle, bodyStyle, smallStyle, chipNameStyle, chipHintStyle, chipKeyStyle;
        private bool open, snapBeam, committing, pointerParked, parkSet, chipShown;
        private float openedAt = -10f, closedAt = -10f, selectedAt = -10f, chipChangedAt = -10f, lastRepaint, beamAngle;
        private SpellId lastSelected, committed, chipSpell;
        private Vector2 parkedAt;

        /// True while open, and while the close or pick animation is still playing.
        public bool Visible => open || Time.unscaledTime - closedAt < (committing ? CommitSeconds : CloseSeconds);

        public void Open()
        {
            if (open) return;
            open = true; snapBeam = true; committing = false; pointerParked = false;
            openedAt = lastRepaint = Time.unscaledTime;
            Array.Clear(hover, 0, hover.Length);
        }
        /// Closes the wheel. Passing the picked spell plays the pick animation instead of the plain close.
        public void Close(SpellId? picked = null)
        {
            if (!open) return;
            open = false; closedAt = Time.unscaledTime;
            committing = picked.HasValue;
            if (picked.HasValue) committed = picked.Value;
        }
        /// After a scroll the core follows the selection rather than the pointer until the pointer moves.
        public void Scrolled() { pointerParked = true; parkSet = false; }

        /// Runs one IMGUI pass and returns the spell clicked in this event, if any. Only repaint events draw.
        public SpellId? Draw(SpellId[] spells, SpellId selected, Func<SpellId, Texture2D> icon, string hint)
        {
            EnsureResources();
            Event current = Event.current;
            float now = Time.unscaledTime;
            bool picking = committing && !open;
            float commitT = picking ? Mathf.Clamp01((now - closedAt) / CommitSeconds) : 0f;
            float burst = picking ? Mathf.InverseLerp(FlyShare, 1f, commitT) : 0f;
            float bloom, fade;
            if (open)
            {
                float t = Mathf.Clamp01((now - openedAt) / OpenSeconds);
                bloom = Mathf.LerpUnclamped(0.55f, 1f, EaseOutBack(t));
                fade = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t * 1.5f));
            }
            else if (picking)
            {
                bloom = Mathf.Lerp(1f, 1.12f, burst);
                fade = 1f - Mathf.SmoothStep(0f, 1f, burst);
            }
            else
            {
                float t = Mathf.Clamp01((now - closedAt) / CloseSeconds);
                bloom = Mathf.Lerp(1f, 1.15f, t);
                fade = 1f - Mathf.SmoothStep(0f, 1f, t);
            }
            float ui = UiScale();
            float s = ui * bloom;
            SetFontSizes(ui);
            Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            int count = spells.Length;
            float step = Mathf.PI * 2f / count;
            float orbit = 120f * s, orbRadius = 30f * s, coreRadius = 60f * s;

            // The orb's whole sector is a target, so the pointer only needs to lean towards a spell.
            int pointed = -1;
            if (open)
            {
                Vector2 offset = current.mousePosition - center;
                float distance = offset.magnitude;
                if (distance > coreRadius && distance < orbit + orbRadius * 2.6f)
                    pointed = (Mathf.RoundToInt((Mathf.Atan2(offset.y, offset.x) + Mathf.PI * 0.5f) / step) % count + count) % count;
            }
            if (pointed >= 0 && current.type == EventType.MouseDown && current.button == 0) { current.Use(); return spells[pointed]; }
            if (current.type != EventType.Repaint) return null;

            if (pointerParked)
            {
                if (!parkSet) { parkedAt = current.mousePosition; parkSet = true; }
                else if ((current.mousePosition - parkedAt).sqrMagnitude > 25f) pointerParked = false;
            }
            int hovered = pointerParked ? -1 : pointed;
            float dt = Mathf.Clamp(now - lastRepaint, 0f, 0.1f);
            lastRepaint = now;
            if (selected != lastSelected) { lastSelected = selected; selectedAt = now; }
            int selectedIndex = Mathf.Max(0, Array.IndexOf(spells, selected));
            int committedIndex = picking ? Array.IndexOf(spells, committed) : -1;
            SpellId focus = hovered >= 0 ? spells[hovered] : selected;
            Color focusColor = ElementColor(focus), selectedColor = ElementColor(selected);
            for (int i = 0; i < count; i++)
            {
                int id = (int)spells[i];
                hover[id] = Mathf.MoveTowards(hover[id], i == hovered ? 1f : 0f, dt * 7f);
            }
            float fly = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(commitT / FlyShare));
            for (int i = 0; i < count; i++)
            {
                float offset = OrbitOffset(i, now, out float presence);
                if (i == committedIndex) { offset = 0f; presence = 1f - Mathf.InverseLerp(FlyShare, FlyShare + 0.12f, commitT); }
                slots[i] = center + Dir(i * step - Mathf.PI * 0.5f + offset * Mathf.Deg2Rad) * (orbit + Mathf.Sin(now * 2.2f + i * 1.3f) * 3f * s);
                if (i == committedIndex) slots[i] = Vector2.Lerp(slots[i], center, fly);
                presences[i] = presence;
            }
            float selectedOrbit = OrbitOffset(selectedIndex, now, out float selectedPresence);
            float targetAngle = (selectedIndex * step - Mathf.PI * 0.5f) * Mathf.Rad2Deg + selectedOrbit;
            // While the orbs are orbiting into or out of place the beam rides with the selected one.
            if (snapBeam || Mathf.Abs(selectedOrbit) > 0.01f) { beamAngle = targetAngle; snapBeam = false; }
            else beamAngle += Mathf.DeltaAngle(beamAngle, targetAngle) * (1f - Mathf.Exp(-dt * 12f));
            if (picking) selectedPresence = Mathf.Clamp01(1f - commitT * 4f);
            float pulse = 0.5f + 0.5f * Mathf.Sin(now * 5f);
            Color previousColor = GUI.color;
            Matrix4x4 previousMatrix = GUI.matrix;

            // Veil, aura, back plate and the two counter-turning rune rings.
            GUI.color = Faded(Veil, fade);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            DrawAt(glow, center, 330f * s, WithAlpha(focusColor, 0.28f * fade));
            DrawAt(disc, center, 172f * s, Faded(Plate, fade));
            DrawSpun(runes, center, 190f * s, now * 6f + selectedOrbit * 0.5f, Faded(RuneColor, fade));
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
            GUI.color = WithAlpha(selectedColor, (0.55f + 0.25f * pulse) * fade * selectedPresence);
            GUI.DrawTexture(new Rect(center.x + coreRadius * 0.8f, center.y - 9f * s, orbit - coreRadius * 0.8f, 18f * s), beam);
            GUI.matrix = previousMatrix;

            // Weave spins a thread of light to the next orb round the wheel, with a spark running along it.
            int weave = Array.IndexOf(spells, SpellId.Weave);
            if (weave >= 0 && count > 1)
            {
                int next = (weave + 1) % count;
                Vector2 from = slots[weave], to = slots[next], bend = Vector2.Lerp((from + to) * 0.5f, center, 0.3f);
                Color thread = ElementColor(SpellId.Weave);
                float threadAlpha = 0.45f * Mathf.Min(presences[weave], presences[next]) * fade;
                Vector2 previousPoint = from;
                for (int k = 1; k <= 12; k++)
                {
                    Vector2 point = Bezier(from, bend, to, k / 12f);
                    DrawLine(previousPoint, point, 2f * s, WithAlpha(thread, threadAlpha));
                    previousPoint = point;
                }
                DrawAt(sparkle, Bezier(from, bend, to, Mathf.Repeat(now * 0.7f, 1f)), 7f * s, WithAlpha(Color.Lerp(thread, Color.white, 0.5f), threadAlpha * 2f));
            }

            Vector2 selectedPosition = center;
            for (int i = 0; i < count; i++)
            {
                if (i == committedIndex) continue;
                SpellId spell = spells[i];
                bool isSelected = spell == selected;
                float h = hover[(int)spell], lit = Mathf.Max(h, isSelected ? 1f : 0f);
                float orbFade = fade * presences[i];
                Color color = ElementColor(spell);
                Vector2 position = slots[i];
                if (isSelected) selectedPosition = position;
                float r = orbRadius * (1f + 0.16f * h + (isSelected ? 0.08f : 0f));
                Vector2 echo = spell == SpellId.LostEchoes ? EchoOffset(i, step, center, orbit, position, now, r) : Vector2.zero;
                DrawOrb(spell, position, r, orbFade, 0.25f + 0.45f * h + (isSelected ? 0.35f + 0.15f * pulse : 0f), isSelected ? 1f : 0.45f + 0.5f * h, icon(spell), echo, now, s, i);
                if (isSelected)
                    for (int m = 0; m < 3; m++)
                        DrawAt(sparkle, position + Dir(now * 2.4f + m * Mathf.PI * 2f / 3f) * r * 1.3f, 6f * s, WithAlpha(Color.Lerp(color, Color.white, 0.5f), 0.9f * orbFade));
                Label(new Rect(position.x - 60f * s, position.y + r + 1f * s, 120f * s, 18f * s), DisplayName(spell), smallStyle, WithAlpha(Color.Lerp(MutedText, Color.white, lit), (0.7f + 0.3f * lit) * orbFade));
                string key = KeyBindings.GetKeyLabel(SpellCaster.KeyActionFor(spell));
                if (!string.IsNullOrEmpty(key) && key.Length <= 3)
                {
                    Vector2 badge = position + new Vector2(r * 0.74f, -r * 0.74f);
                    DrawAt(disc, badge, 9f * s, Faded(OrbFill, orbFade));
                    DrawAt(ring, badge, 9f * s, WithAlpha(color, 0.8f * orbFade));
                    Label(new Rect(badge.x - 9f * s, badge.y - 9f * s, 18f * s, 18f * s), key, smallStyle, WithAlpha(Color.white, 0.9f * orbFade));
                }
            }

            // A ripple spreads from a newly chosen spell.
            float ripple = (now - selectedAt) / RippleSeconds;
            if (ripple < 1f && !picking)
            {
                float eased = EaseOut(ripple);
                DrawAt(ring, selectedPosition, orbRadius * Mathf.Lerp(1f, 2.4f, eased), WithAlpha(selectedColor, (1f - ripple) * fade));
                DrawAt(ring, center, coreRadius * Mathf.Lerp(1f, 1.6f, eased), WithAlpha(selectedColor, 0.6f * (1f - ripple) * fade));
            }

            // The core previews the hovered spell, or shows the selected one.
            DrawAt(glow, center, coreRadius * 1.9f, WithAlpha(focusColor, (0.45f + 0.15f * pulse) * fade));
            DrawAt(disc, center, coreRadius, new Color(0.06f, 0.05f, 0.14f, 0.97f * fade));
            DrawAt(ring, center, coreRadius, WithAlpha(focusColor, 0.9f * fade));
            float focusSize = 46f * s * (1f + 0.04f * pulse);
            DrawIcon(icon(focus), new Vector2(center.x, center.y - 10f * s), focusSize * 0.5f, fade);
            Label(new Rect(center.x - coreRadius, center.y + 15f * s, coreRadius * 2f, 22f * s), DisplayName(focus), titleStyle, WithAlpha(Color.Lerp(focusColor, Color.white, 0.55f), fade));

            // A picked spell flies into the core over the top of it, then bursts.
            if (committedIndex >= 0)
            {
                Color color = ElementColor(committed);
                DrawOrb(committed, slots[committedIndex], orbRadius * (1f + 0.6f * fly), presences[committedIndex], 0.6f + 0.6f * fly, 1f, icon(committed), Vector2.zero, now, s, committedIndex);
                if (burst > 0f) DrawBurst(center, color, burst, s, 190f);
            }

            Label(new Rect(center.x - 220f * s, center.y + 196f * s, 440f * s, 22f * s), Description(focus), bodyStyle, WithAlpha(Color.Lerp(focusColor, Color.white, 0.6f), 0.95f * fade));
            Label(new Rect(center.x - 280f * s, center.y + 222f * s, 560f * s, 20f * s), hint, smallStyle, WithAlpha(MutedText, 0.8f * fade));
            GUI.color = previousColor;
            GUI.matrix = previousMatrix;
            return null;
        }

        /// Draws the selected spell as a small orb on a dark plate in the bottom-left corner, with the spell wheel key
        /// shown as a key-cap. It pops and sparks when the selection changes, whether from the number keys, the wheel
        /// or a scroll.
        public void DrawChip(SpellId spell, Func<SpellId, Texture2D> icon, string wheelKey)
        {
            if (Event.current.type != EventType.Repaint) return;
            EnsureResources();
            float now = Time.unscaledTime;
            if (!chipShown) { chipShown = true; chipSpell = spell; }
            else if (spell != chipSpell) { chipSpell = spell; chipChangedAt = now; }
            float ui = UiScale();
            SetFontSizes(ui);
            float pop = Mathf.Clamp01((now - chipChangedAt) / ChipPopSeconds), kick = (1f - pop) * (1f - pop);
            float baseRadius = 26f * ui, r = baseRadius * (1f + 0.3f * kick), margin = 22f * ui;
            Vector2 position = new Vector2(margin + baseRadius * 1.5f, Screen.height - margin - baseRadius * 1.5f);
            Color color = ElementColor(spell);
            Color previousColor = GUI.color;
            Matrix4x4 previousMatrix = GUI.matrix;

            string name = DisplayName(spell), key = string.IsNullOrEmpty(wheelKey) ? "?" : wheelKey;
            const string HintText = "Spell wheel";
            Vector2 nameSize = chipNameStyle.CalcSize(new GUIContent(name));
            Vector2 keySize = chipKeyStyle.CalcSize(new GUIContent(key));
            Vector2 hintSize = chipHintStyle.CalcSize(new GUIContent(HintText));
            float capWidth = Mathf.Max(keySize.x + 10f * ui, 20f * ui), capHeight = 19f * ui;
            float textLeft = position.x + baseRadius * 1.5f + 4f * ui;
            float contentWidth = Mathf.Max(nameSize.x, capWidth + 6f * ui + hintSize.x);
            Rect plate = new Rect(position.x, position.y - 25f * ui, textLeft - position.x + contentWidth + 16f * ui, 50f * ui);
            GUI.color = Color.white;
            GUI.DrawTexture(plate, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, new Color(0.05f, 0.04f, 0.13f, 0.82f), 0f, plate.height * 0.5f);
            GUI.DrawTexture(plate, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, WithAlpha(color, 0.45f + 0.4f * kick), 1.5f * ui, plate.height * 0.5f);

            Vector2 echo = new Vector2(Mathf.Sin(now * 1.7f), Mathf.Cos(now * 1.3f)) * r * 0.3f;
            DrawOrb(spell, position, r, 1f, 0.35f + 0.1f * Mathf.Sin(now * 2.5f) + 0.6f * kick, 1f, icon(spell), echo, now, ui, 99);
            if (pop < 1f) DrawBurst(position, color, pop, ui, 70f);

            Label(new Rect(textLeft, position.y - 22f * ui, contentWidth, 20f * ui), name, chipNameStyle, Color.Lerp(Color.Lerp(color, Color.white, 0.55f), Color.white, kick));
            Rect cap = new Rect(textLeft, position.y + 2f * ui, capWidth, capHeight);
            GUI.color = Color.white;
            GUI.DrawTexture(cap, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, new Color(0.2f, 0.17f, 0.36f, 0.98f), 0f, 4f * ui);
            GUI.DrawTexture(cap, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, Color.Lerp(color, Color.white, 0.3f), 1.5f * ui, 4f * ui);
            Label(cap, key, chipKeyStyle, Color.white);
            Label(new Rect(cap.xMax + 6f * ui, cap.y, hintSize.x + 4f * ui, capHeight), HintText, chipHintStyle, new Color(0.94f, 0.92f, 1f, 1f));
            GUI.color = previousColor;
            GUI.matrix = previousMatrix;
        }

        /// <summary>
        /// A card near the top of the screen announcing a newly learned spell: its orb bursting into view, the name in its
        /// colour and what it does. <paramref name="elapsed"/> runs from 0 to <paramref name="duration"/>; it eases in,
        /// holds and fades.
        /// </summary>
        public void DrawNewSpell(SpellId spell, Func<SpellId, Texture2D> icon, float elapsed, float duration)
        {
            if (Event.current.type != EventType.Repaint || elapsed < 0f || elapsed > duration) return;
            EnsureResources();
            float now = Time.unscaledTime;
            float ui = UiScale();
            SetFontSizes(ui);
            float appear = EaseOut(Mathf.Clamp01(elapsed / 0.4f));
            float alpha = Mathf.Min(appear, Mathf.Clamp01((duration - elapsed) / 0.6f));
            float pop = Mathf.Clamp01(elapsed / 0.8f);
            Color color = ElementColor(spell), bright = Color.Lerp(color, Color.white, 0.55f);
            Color previousColor = GUI.color;

            string heading = "New spell learned", name = DisplayName(spell), description = Description(spell);
            float r = 30f * ui * (0.6f + 0.4f * EaseOutBack(Mathf.Clamp01(elapsed / 0.5f)));
            float textWidth = Mathf.Max(chipNameStyle.CalcSize(new GUIContent(name)).x, chipHintStyle.CalcSize(new GUIContent(description)).x,
                chipHintStyle.CalcSize(new GUIContent(heading)).x);
            float width = 30f * ui * 3.2f + textWidth + 28f * ui, height = 84f * ui;
            Rect card = new Rect((Screen.width - width) * 0.5f, Screen.height * 0.16f - (1f - appear) * 18f * ui, width, height);
            GUI.color = Color.white;
            GUI.DrawTexture(card, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, WithAlpha(Plate, Plate.a * alpha), 0f, 16f * ui);
            GUI.DrawTexture(card, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, WithAlpha(color, (0.55f + 0.35f * (1f - pop)) * alpha), 1.5f * ui, 16f * ui);

            Vector2 orb = new Vector2(card.x + 30f * ui * 1.6f, card.center.y);
            Vector2 echo = new Vector2(Mathf.Sin(now * 1.7f), Mathf.Cos(now * 1.3f)) * r * 0.3f;
            DrawOrb(spell, orb, r, alpha, 0.5f + 0.1f * Mathf.Sin(now * 2.5f) + (1f - pop), 1f, icon(spell), echo, now, ui, 41);
            if (pop < 1f) DrawBurst(orb, color, pop, ui, 90f);

            float textLeft = card.x + 30f * ui * 3.2f;
            Label(new Rect(textLeft, card.y + 10f * ui, textWidth, 18f * ui), heading, chipHintStyle, WithAlpha(MutedText, alpha));
            Label(new Rect(textLeft, card.y + 30f * ui, textWidth, 22f * ui), name, chipNameStyle, WithAlpha(bright, alpha));
            Label(new Rect(textLeft, card.y + 54f * ui, textWidth, 20f * ui), description, chipHintStyle, WithAlpha(new Color(0.94f, 0.92f, 1f, 1f), alpha));
            GUI.color = previousColor;
        }

        public void Dispose()
        {
            foreach (Texture2D texture in new[] { glow, disc, ring, runes, sparkle, beam, line, gear, frame })
                if (texture != null) UnityEngine.Object.Destroy(texture);
            glow = disc = ring = runes = sparkle = beam = line = gear = frame = null;
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
                case SpellId.Rotara: return "Turn an object to face a new way.";
                case SpellId.Glaciate: return "Freeze an object and send it sliding.";
                case SpellId.Vis: return "Lift an object and carry it somewhere new.";
                case SpellId.LostEchoes: return "Listen for hidden things nearby.";
                default: return string.Empty;
            }
        }

        /// One spell orb: glow, disc, ring and icon, plus the spell's own motion. Ampere crackles with lightning,
        /// Glaciate grows frost, Rotara turns a gear a tooth at a time, Gravitate wobbles inside a pulling ring, Lost
        /// Echoes trails a ghost at the echo offset, Weave curls a thread round itself and Vis tilts a frame that
        /// snaps level. Seed staggers the motion between orbs.
        private void DrawOrb(SpellId spell, Vector2 position, float r, float alpha, float glowStrength, float ringStrength, Texture2D texture, Vector2 echo, float now, float s, int seed)
        {
            if (alpha <= 0f) return;
            Color color = ElementColor(spell), bright = Color.Lerp(color, Color.white, 0.55f);
            if (spell == SpellId.Gravitate) position += new Vector2(Mathf.Sin(now * 6.1f + seed), Mathf.Sin(now * 7.7f + seed * 2f)) * 1.6f * s;
            if (spell == SpellId.LostEchoes)
            {
                Vector2 ghost = position + echo;
                DrawAt(glow, ghost, r * 1.8f, WithAlpha(color, 0.2f * alpha));
                DrawAt(ring, ghost, r, WithAlpha(color, 0.35f * alpha));
                DrawIcon(texture, ghost, r * 0.625f, 0.3f * alpha);
            }
            DrawAt(glow, position, r * 2.4f, WithAlpha(color, glowStrength * alpha));
            if (spell == SpellId.Rotara)
            {
                const float Tick = 0.9f;
                float teeth = Mathf.Floor(now / Tick) + Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Mathf.Repeat(now, Tick) / Tick * 3f));
                DrawSpun(gear, position, r * 1.28f, teeth * 30f + seed * 7f, WithAlpha(color, 0.75f * alpha));
            }
            if (spell == SpellId.Gravitate)
                for (int k = 0; k < 2; k++)
                {
                    float cycle = Mathf.Repeat(now * 0.8f + k * 0.5f + seed * 0.3f, 1f);
                    DrawAt(ring, position, Mathf.Lerp(r * 1.75f, r * 1.02f, EaseOut(cycle)), WithAlpha(color, Mathf.Sin(cycle * Mathf.PI) * 0.6f * alpha));
                }
            DrawAt(disc, position, r, Faded(OrbFill, alpha));
            DrawAt(ring, position, r, WithAlpha(ringStrength >= 1f ? Color.Lerp(color, Color.white, 0.35f) : color, ringStrength * alpha));
            if (texture != null) DrawIcon(texture, position, r * 0.625f, alpha);
            else Label(new Rect(position.x - r, position.y - r, r * 2f, r * 2f), DisplayName(spell).Substring(0, 1), titleStyle, WithAlpha(color, alpha));
            switch (spell)
            {
                case SpellId.Ampere:
                {
                    int flicker = Mathf.FloorToInt(now / 0.09f);
                    for (int arc = 0; arc < 2; arc++)
                    {
                        if (Hash(flicker * 7 + arc * 13 + seed) < 0.35f) continue;
                        float start = Hash(flicker * 11 + arc * 5 + seed) * Mathf.PI * 2f;
                        Vector2 previous = position + Dir(start) * r * 1.08f;
                        for (int k = 1; k <= 4; k++)
                        {
                            float jitter = (Hash(flicker * 17 + arc * 3 + k + seed) - 0.5f) * 0.35f * r;
                            Vector2 point = position + Dir(start + 0.9f * k / 4f) * (r * 1.12f + jitter);
                            DrawLine(previous, point, 2.2f * s, WithAlpha(bright, 0.85f * alpha));
                            previous = point;
                        }
                    }
                    break;
                }
                case SpellId.Glaciate:
                    for (int k = 0; k < 6; k++)
                    {
                        Vector2 direction = Dir(k * Mathf.PI / 3f + now * 0.25f);
                        Vector2 tip = position + direction * r * (1.22f + 0.05f * Mathf.Sin(now * 3f + k));
                        DrawLine(position + direction * r * 0.98f, tip, 2f * s, WithAlpha(Frost, 0.7f * alpha));
                        DrawAt(sparkle, tip, 4.5f * s * (0.7f + 0.3f * Mathf.Sin(now * 4f + k * 1.7f)), WithAlpha(Frost, 0.9f * alpha));
                    }
                    break;
                case SpellId.Weave:
                {
                    float head = now * 2f + seed;
                    Vector2 previous = position + Dir(head - 2f) * r * 1.16f;
                    for (int k = 1; k <= 8; k++)
                    {
                        Vector2 point = position + Dir(head - 2f + 2f * k / 8f) * r * 1.16f;
                        DrawLine(previous, point, 1.6f * s, WithAlpha(color, 0.7f * alpha * k / 8f));
                        previous = point;
                    }
                    DrawAt(sparkle, previous, 6f * s, WithAlpha(bright, alpha));
                    break;
                }
                case SpellId.Vis:
                {
                    // The frame drifts crooked, then snaps level with a flash.
                    float cycle = Mathf.Repeat(now / 2.2f + seed * 0.37f, 1f);
                    float lean = (Mathf.FloorToInt(now / 2.2f + seed * 0.37f) % 2 == 0 ? 1f : -1f) * 14f;
                    float tilt = cycle < 0.75f ? Mathf.Sin(cycle / 0.75f * Mathf.PI * 0.5f) * lean : Mathf.Lerp(lean, 0f, Mathf.Clamp01((cycle - 0.75f) / 0.05f));
                    float flash = cycle >= 0.8f ? 1f - (cycle - 0.8f) / 0.2f : 0f;
                    DrawSpun(frame, position, r * 1.18f, tilt, WithAlpha(Color.Lerp(color, Color.white, flash * 0.6f), (0.55f + 0.45f * flash) * alpha));
                    break;
                }
            }
        }

        private void DrawBurst(Vector2 center, Color color, float t, float s, float reach)
        {
            float eased = EaseOut(t), alpha = 1f - t;
            DrawAt(glow, center, Mathf.Lerp(reach * 0.3f, reach * 0.85f, eased) * s, WithAlpha(color, 0.9f * alpha));
            DrawAt(ring, center, Mathf.Lerp(reach * 0.15f, reach * 0.9f, eased) * s, WithAlpha(Color.Lerp(color, Color.white, 0.4f), alpha));
            for (int k = 0; k < BurstSparks; k++)
            {
                Vector2 direction = Dir(k * Mathf.PI * 2f / BurstSparks + Hash(k) * 0.3f);
                float distance = Mathf.Lerp(reach * 0.05f, reach * (0.7f + 0.3f * Hash(k + 40)), eased) * s;
                DrawAt(sparkle, center + direction * distance, (reach / 21f) * (1f - 0.5f * t) * s, WithAlpha(Color.Lerp(color, Color.white, Hash(k + 80)), alpha));
            }
        }

        /// Lost Echoes' ghost sits where the orb was a moment ago, so it trails behind while orbiting and drifts at rest.
        private Vector2 EchoOffset(int index, float step, Vector2 center, float orbit, Vector2 position, float now, float r)
        {
            Vector2 lagged = center + Dir(index * step - Mathf.PI * 0.5f + OrbitOffset(index, now - EchoLag, out _) * Mathf.Deg2Rad) * orbit;
            return lagged - position + new Vector2(Mathf.Sin(now * 1.7f), Mathf.Cos(now * 1.3f)) * r * 0.3f;
        }

        /// Degrees the orb in slot index sits away from its resting angle. Opening, each orb sweeps clockwise around
        /// the centre into place, a little behind the one before it; closing, they all swing onward. Presence fades an
        /// orb in as its orbit begins, and out early when another orb was picked.
        private float OrbitOffset(int index, float now, out float presence)
        {
            if (!open)
            {
                float c = Mathf.Clamp01((now - closedAt) / CloseSeconds);
                presence = committing ? 1f - c : 1f;
                return c * c * CloseSweep;
            }
            float t = Mathf.Clamp01((now - openedAt - index * OrbitStagger) / OrbitSeconds);
            float remaining = 1f - t;
            presence = Mathf.Clamp01(t * 4f);
            return -remaining * remaining * remaining * OpenSweep;
        }

        private void EnsureResources()
        {
            if (titleStyle == null)
            {
                Font font = Resources.Load<Font>("Dialogue/Nunito-Regular");
                titleStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, clipping = TextClipping.Overflow, wordWrap = false };
                bodyStyle = new GUIStyle(titleStyle) { fontStyle = FontStyle.Italic };
                smallStyle = new GUIStyle(titleStyle) { fontStyle = FontStyle.Normal };
                chipNameStyle = new GUIStyle(titleStyle) { alignment = TextAnchor.MiddleLeft };
                chipHintStyle = new GUIStyle(smallStyle) { alignment = TextAnchor.MiddleLeft };
                chipKeyStyle = new GUIStyle(titleStyle);
                foreach (GUIStyle style in new[] { titleStyle, bodyStyle, smallStyle, chipNameStyle, chipHintStyle, chipKeyStyle })
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
            line = Generate(32, (u, v, r) => Mathf.Exp(-v * v * 5f));
            gear = Generate(128, GearAlpha);
            frame = Generate(128, (u, v, r) => (0.03f - Mathf.Abs(Mathf.Max(Mathf.Abs(u), Mathf.Abs(v)) - 0.9f)) * 64f);
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

        /// A twelve-tooth gear rim.
        private static float GearAlpha(float u, float v, float r)
        {
            float rim = (0.05f - Mathf.Abs(r - 0.82f)) * 64f;
            float tooth = Mathf.Clamp01((Mathf.Cos(Mathf.Atan2(v, u) * 12f) - 0.2f) * 6f);
            float teeth = r > 0.8f ? Mathf.Clamp01((0.97f - r) * 64f) * tooth : 0f;
            return Mathf.Max(rim, teeth);
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

        private void SetFontSizes(float ui)
        {
            titleStyle.fontSize = bodyStyle.fontSize = chipNameStyle.fontSize = Mathf.RoundToInt(15f * ui);
            smallStyle.fontSize = chipKeyStyle.fontSize = Mathf.RoundToInt(12f * ui);
            chipHintStyle.fontSize = Mathf.RoundToInt(13f * ui);
        }
        private static float UiScale() => Mathf.Clamp(Screen.height / 900f, 0.8f, 1.6f);
        private static void DrawAt(Texture2D texture, Vector2 center, float radius, Color color)
        { GUI.color = color; GUI.DrawTexture(new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f), texture); }
        private static void DrawSpun(Texture2D texture, Vector2 center, float radius, float degrees, Color color)
        { Matrix4x4 matrix = GUI.matrix; GUIUtility.RotateAroundPivot(degrees, center); DrawAt(texture, center, radius, color); GUI.matrix = matrix; }
        private void DrawLine(Vector2 from, Vector2 to, float width, Color color)
        {
            Vector2 delta = to - from;
            float length = delta.magnitude;
            if (length < 0.5f) return;
            Matrix4x4 matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, from);
            GUI.color = color;
            GUI.DrawTexture(new Rect(from.x, from.y - width * 0.5f, length, width), line);
            GUI.matrix = matrix;
        }
        private static void DrawIcon(Texture2D texture, Vector2 center, float halfSize, float alpha)
        {
            if (texture == null) return;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTexture(new Rect(center.x - halfSize, center.y - halfSize, halfSize * 2f, halfSize * 2f), texture, ScaleMode.ScaleToFit, true);
        }
        private static void Label(Rect rect, string text, GUIStyle style, Color color)
        {
            GUI.color = new Color(0f, 0f, 0f, color.a * 0.7f);
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1.5f, rect.width, rect.height), text, style);
            GUI.color = color;
            GUI.Label(rect, text, style);
        }
        private static Vector2 Dir(float radians) => new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        private static Vector2 Bezier(Vector2 a, Vector2 control, Vector2 b, float t) { float u = 1f - t; return u * u * a + 2f * u * t * control + t * t * b; }
        private static Color WithAlpha(Color color, float alpha) { color.a = alpha; return color; }
        private static Color Faded(Color color, float fade) { color.a *= fade; return color; }
        private static float Hash(int n) => Mathf.Repeat(Mathf.Sin(n * 12.9898f) * 43758.5453f, 1f);
        private static float EaseOut(float t) => 1f - (1f - t) * (1f - t);
        private static float EaseOutBack(float t) { float x = t - 1f; return 1f + 2.70158f * x * x * x + 1.70158f * x * x; }
    }
}
