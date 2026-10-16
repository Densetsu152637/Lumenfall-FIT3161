using System;
using System.Collections.Generic;
using Lumenfall.GridMovement;
using UnityEngine;

namespace Lumenfall.Spells
{
    /// <summary>
    /// Feedback for Vis, built in code so it needs no art. Lifting: a wavy green tether reaches from the player's hand
    /// to the object, a spiral of light winds up around it, a ring spreads on the ground and a soft green glow pulses.
    /// Setting down: the tether reaches to the tile, the spiral winds down and a ring settles; delivering onto a stand
    /// pulses brighter. A cast that straightens an object gets the tether and a rising spiral. A refused placement blinks a
    /// small red ring on the tile. The carried object animates itself (see the relic's own presentation); this only draws
    /// the spell.
    /// </summary>
    public sealed class VisPresentation : MonoBehaviour
    {
        private const float TetherSeconds = 0.55f;
        private const float SpiralSeconds = 0.9f;
        private const float RingSeconds = 0.6f;
        private const int TetherPoints = 16;
        private const int SpiralPoints = 48;
        private const int RingPoints = 32;
        private static readonly Color VisColor = new Color(0.6f, 1f, 0.5f);
        private static readonly Color RefusedColor = new Color(1f, 0.45f, 0.4f);
        private static VisPresentation instance;

        private enum Shape { Tether, SpiralUp, SpiralDown, Ring }

        private sealed class Effect
        {
            public Shape shape;
            public LineRenderer line;
            public Light glow;
            public Vector3 from;
            public Vector3 at;
            public Color colour;
            public float startsAt;
            public float seconds;
            public float size;
        }

        private readonly List<Effect> effects = new List<Effect>();
        private SpellCaster caster;
        private Material material;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;
            GameObject host = new GameObject("[Vis Presentation]");
            instance = host.AddComponent<VisPresentation>();
            DontDestroyOnLoad(host);
        }

        private void OnDestroy()
        {
            Unsubscribe();
            foreach (Effect effect in effects) DestroyEffect(effect);
            if (material != null) Destroy(material);
            if (instance == this) instance = null;
        }

        private void Update()
        {
            // The player's SpellCaster is added at runtime, so the subscription is refreshed.
            if (caster == null)
            {
                SpellCaster found = FindAnyObjectByType<SpellCaster>();
                if (found != null)
                {
                    caster = found;
                    caster.VisLifted += OnLifted;
                    caster.VisPlaced += OnPlaced;
                    caster.VisPlaceRefused += OnRefused;
                    caster.CastCompleted += OnCastCompleted;
                }
            }
            Animate();
        }

        private void Unsubscribe()
        {
            if (caster == null) return;
            caster.VisLifted -= OnLifted;
            caster.VisPlaced -= OnPlaced;
            caster.VisPlaceRefused -= OnRefused;
            caster.CastCompleted -= OnCastCompleted;
            caster = null;
        }

        private void OnLifted(string id)
        {
            if (!TryGetObject(id, out Vector3 ground)) return;
            Tether(ground + Vector3.up * 0.6f);
            Add(Shape.SpiralUp, ground, VisColor, SpiralSeconds, 0.55f, true);
            Add(Shape.Ring, ground, VisColor, RingSeconds, 0.9f, false);
        }

        private void OnPlaced(string id, Vector2Int cell, bool delivered)
        {
            if (!TryGetCell(cell, out Vector3 ground)) return;
            Tether(ground + Vector3.up * 0.6f);
            Add(Shape.SpiralDown, ground, VisColor, SpiralSeconds, 0.55f, true);
            Add(Shape.Ring, ground, VisColor, RingSeconds * (delivered ? 1.4f : 1f), delivered ? 1.4f : 0.9f, delivered);
        }

        private void OnRefused(string id, Vector2Int cell, bool blocked)
        {
            if (TryGetCell(cell, out Vector3 ground)) Add(Shape.Ring, ground, RefusedColor, RingSeconds * 0.7f, 0.55f, false);
        }

        // Lifting and setting down have their own events; this covers a cast that straightens an object instead.
        private void OnCastCompleted(SpellId spell, string targetId, bool changed)
        {
            if (spell != SpellId.Vis || !changed || caster == null || targetId == caster.HeldRelic) return;
            if (!TryGetObject(targetId, out Vector3 ground)) return;
            Tether(ground + Vector3.up * 0.6f);
            Add(Shape.SpiralUp, ground, VisColor, SpiralSeconds, 0.5f, true);
        }

        private void Tether(Vector3 to)
        {
            Effect effect = Add(Shape.Tether, to, VisColor, TetherSeconds, 0.06f, false);
            if (effect != null) effect.from = Hand();
        }

        private Vector3 Hand() => caster == null ? Vector3.zero : caster.transform.position + Vector3.up * 1.05f + caster.transform.forward * 0.25f;

        private static bool TryGetObject(string id, out Vector3 ground)
        {
            ground = default;
            WorldStateService state = WorldStateService.Instance;
            if (state == null || !state.TryGetSceneObject(id, out GameObject target)) return false;
            ground = target.transform.position;
            return true;
        }

        private bool TryGetCell(Vector2Int cell, out Vector3 ground)
        {
            ground = default;
            GridMover player = caster == null ? null : caster.GetComponent<GridMover>();
            if (player == null || player.World == null) return false;
            ground = player.World.CellToWorld(cell, player.World.WorldToElevation(player.transform.position));
            return true;
        }

        private Effect Add(Shape shape, Vector3 at, Color colour, float seconds, float size, bool glow)
        {
            if (!EnsureMaterial()) return null;
            GameObject part = new GameObject("Vis " + shape);
            part.transform.SetParent(transform, false);
            LineRenderer line = part.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.positionCount = shape == Shape.Tether ? TetherPoints : shape == Shape.Ring ? RingPoints + 1 : SpiralPoints;
            line.widthMultiplier = shape == Shape.Tether ? size : 0.05f;
            line.numCapVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;
            Effect effect = new Effect { shape = shape, line = line, at = at, colour = colour, startsAt = Time.unscaledTime, seconds = seconds, size = size };
            if (glow)
            {
                GameObject light = new GameObject("Vis Glow");
                light.transform.SetParent(part.transform, false);
                light.transform.position = at + Vector3.up * 0.8f;
                effect.glow = light.AddComponent<Light>();
                effect.glow.type = LightType.Point;
                effect.glow.color = colour;
                effect.glow.range = 3f;
                effect.glow.intensity = 0f;
                effect.glow.shadows = LightShadows.None;
            }
            effects.Add(effect);
            return effect;
        }

        private void Animate()
        {
            float now = Time.unscaledTime;
            for (int i = effects.Count - 1; i >= 0; i--)
            {
                Effect effect = effects[i];
                float t = Mathf.Clamp01((now - effect.startsAt) / effect.seconds);
                if (t >= 1f) { DestroyEffect(effect); effects.RemoveAt(i); continue; }
                float fade = 1f - t;
                LineRenderer line = effect.line;
                line.enabled = true;
                Color colour = effect.colour;
                colour.a = fade * (effect.shape == Shape.Ring ? 0.8f : 0.95f);
                line.startColor = line.endColor = colour;
                switch (effect.shape)
                {
                    case Shape.Tether: DrawTether(effect, now, fade); break;
                    case Shape.Ring: DrawRing(effect, t); break;
                    default: DrawSpiral(effect, t, now); break;
                }
                if (effect.glow != null) effect.glow.intensity = 3.5f * Mathf.Sin(t * Mathf.PI);
            }
        }

        // A gentle wave along the line from hand to target, travelling outward, so it reads as a pull rather than a bolt.
        private void DrawTether(Effect effect, float now, float fade)
        {
            Vector3 from = caster != null ? Hand() : effect.from;
            Vector3 across = Vector3.Cross(effect.at - from, Vector3.up).normalized;
            for (int p = 0; p < TetherPoints; p++)
            {
                float s = p / (float)(TetherPoints - 1);
                float wave = Mathf.Sin(s * Mathf.PI * 3f - now * 14f) * 0.12f * Mathf.Sin(s * Mathf.PI);
                effect.line.SetPosition(p, Vector3.Lerp(from, effect.at, s) + across * wave + Vector3.up * wave * 0.5f);
            }
            effect.line.widthMultiplier = effect.size * (0.4f + 0.6f * fade);
        }

        // A spreading circle on the ground.
        private void DrawRing(Effect effect, float t)
        {
            float radius = Mathf.Lerp(0.2f, effect.size, 1f - (1f - t) * (1f - t));
            for (int p = 0; p <= RingPoints; p++)
            {
                float angle = p / (float)RingPoints * Mathf.PI * 2f;
                effect.line.SetPosition(p, effect.at + new Vector3(Mathf.Cos(angle) * radius, 0.06f, Mathf.Sin(angle) * radius));
            }
        }

        // Three turns of light winding up (or down) around the object, turning as they go.
        private void DrawSpiral(Effect effect, float t, float now)
        {
            bool up = effect.shape == Shape.SpiralUp;
            float reach = Mathf.Clamp01(t * 1.6f);
            for (int p = 0; p < SpiralPoints; p++)
            {
                float s = p / (float)(SpiralPoints - 1) * reach;
                float height = (up ? s : 1f - s) * 1.6f;
                float angle = s * Mathf.PI * 6f + now * 4f;
                float radius = effect.size * (1f - 0.35f * s);
                effect.line.SetPosition(p, effect.at + new Vector3(Mathf.Cos(angle) * radius, 0.1f + height, Mathf.Sin(angle) * radius));
            }
        }

        private static void DestroyEffect(Effect effect)
        {
            if (effect.line != null) Destroy(effect.line.gameObject);
        }

        // Sprites/Default is unlit and transparent, so the light reads the same in any room.
        private bool EnsureMaterial()
        {
            if (material != null) return true;
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) return false;
            material = new Material(shader) { name = "Vis Light" };
            return true;
        }
    }
}
