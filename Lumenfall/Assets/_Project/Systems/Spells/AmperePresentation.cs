using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lumenfall.Spells
{
    /// <summary>
    /// Feedback for Ampere, built in code so it needs no art: a crackling bolt leaps from the player's hand to the object
    /// they cast at, which flashes with light. When a cast changes other objects too (Stella's obelisks resonate), smaller
    /// arcs jump on from the target to each of them. It plays the <c>lightning:</c> and <c>stella-relic:</c> effects the
    /// world queues for Ampere and acknowledges them, so the same effect can play again the next time.
    /// </summary>
    public sealed class AmperePresentation : MonoBehaviour
    {
        private const string LightningPrefix = "lightning:";
        private const string StellaRelicPrefix = "stella-relic:";
        private const string StellaRelicIdPrefix = "stella.string-together.relic.";
        private const float BoltSeconds = 0.45f;
        private const float ArcDelay = 0.12f;
        private const float FlashSeconds = 0.6f;
        private const int BoltSegments = 10;
        private static readonly Color BoltColor = new Color(1f, 0.9f, 0.45f);
        private static readonly Color CoreColor = new Color(1f, 1f, 0.92f);
        private static AmperePresentation instance;

        private sealed class Bolt
        {
            public LineRenderer glow;
            public LineRenderer core;
            public Light flash;
            public Vector3 from;
            public Vector3 to;
            public float startsAt;
            public float width;
        }

        private readonly List<Bolt> bolts = new List<Bolt>();
        private readonly List<string> queuedTargets = new List<string>();
        private SpellCaster caster;
        private WorldStateService subscribedState;
        private Material material;
        private int clearedVersion = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;
            GameObject host = new GameObject("[Ampere Presentation]");
            instance = host.AddComponent<AmperePresentation>();
            DontDestroyOnLoad(host);
        }

        private void OnDestroy()
        {
            if (caster != null) caster.CastCompleted -= OnCastCompleted;
            if (subscribedState != null) subscribedState.EffectQueued -= OnEffectQueued;
            foreach (Bolt bolt in bolts) DestroyBolt(bolt);
            if (material != null) Destroy(material);
            if (instance == this) instance = null;
        }

        private void Update()
        {
            Subscribe();
            ClearStaleRelicEffects();
            // Effects queued without a cast this frame (for example by a mechanism) still flash where they happened.
            foreach (string id in queuedTargets) PlayArc(null, id, 0f);
            queuedTargets.Clear();
            Animate();
        }

        // The player's SpellCaster and the world state are created at runtime, so both subscriptions are refreshed.
        private void Subscribe()
        {
            if (caster == null)
            {
                SpellCaster found = FindAnyObjectByType<SpellCaster>();
                if (found != null) { caster = found; caster.CastCompleted += OnCastCompleted; }
            }
            WorldStateService state = WorldStateService.Instance;
            if (state == subscribedState) return;
            if (subscribedState != null) subscribedState.EffectQueued -= OnEffectQueued;
            subscribedState = state;
            if (state != null) state.EffectQueued += OnEffectQueued;
        }

        // Before this played them, Ampere's effects were queued and never acknowledged, and a key still queued cannot be
        // queued again. Saves (and each map's archive) can hold Stella's, so they are cleared whenever a map has loaded.
        private void ClearStaleRelicEffects()
        {
            WorldStateService state = subscribedState;
            if (state == null || !state.IsReady || state.Version == clearedVersion) return;
            clearedVersion = state.Version;
            for (int i = 1; i <= 5; i++)
            {
                state.AcknowledgeEffect(StellaRelicPrefix + i + ":on");
                state.AcknowledgeEffect(StellaRelicPrefix + i + ":off");
            }
        }

        private void OnEffectQueued(string key)
        {
            string id = null;
            if (key.StartsWith(LightningPrefix, StringComparison.Ordinal))
                id = key.Substring(LightningPrefix.Length, Math.Max(0, key.LastIndexOf(':') - LightningPrefix.Length));
            else if (key.StartsWith(StellaRelicPrefix, StringComparison.Ordinal))
                id = StellaRelicIdPrefix + key.Substring(StellaRelicPrefix.Length).Split(':')[0];
            if (id == null) return;
            subscribedState?.AcknowledgeEffect(key);
            if (!queuedTargets.Contains(id)) queuedTargets.Add(id);
        }

        private void OnCastCompleted(SpellId spell, string targetId, bool changed)
        {
            if (spell != SpellId.Ampere || !changed || string.IsNullOrEmpty(targetId) || caster == null) return;
            // A bolt from the hand to the target, then arcs on from the target to everything else the cast changed.
            Vector3 hand = caster.transform.position + Vector3.up * 1.05f + caster.transform.forward * 0.25f;
            if (!TryGetTop(targetId, out Vector3 target)) return;
            AddBolt(hand, target, 0f, 0.12f, true);
            float delay = ArcDelay;
            foreach (string id in queuedTargets)
            {
                if (id == targetId) continue;
                PlayArc(target, id, delay);
                delay += ArcDelay * 0.5f;
            }
            queuedTargets.Clear();
        }

        private void PlayArc(Vector3? from, string id, float delay)
        {
            if (!TryGetTop(id, out Vector3 to)) return;
            if (from.HasValue) AddBolt(from.Value, to, delay, 0.07f, true);
            else AddBolt(to + Vector3.up * 1.4f, to, delay, 0.05f, true);
        }

        private bool TryGetTop(string id, out Vector3 position)
        {
            position = default;
            if (subscribedState == null || !subscribedState.TryGetSceneObject(id, out GameObject target)) return false;
            Renderer shape = target.GetComponentInChildren<Renderer>();
            position = shape != null ? new Vector3(shape.bounds.center.x, shape.bounds.max.y * 0.8f + shape.bounds.min.y * 0.2f, shape.bounds.center.z)
                : target.transform.position + Vector3.up;
            return true;
        }

        private void AddBolt(Vector3 from, Vector3 to, float delay, float width, bool flash)
        {
            if (!EnsureMaterial()) return;
            Bolt bolt = new Bolt { from = from, to = to, startsAt = Time.unscaledTime + delay, width = width };
            bolt.glow = Line("Ampere Bolt Glow", width * 3.2f, BoltColor);
            bolt.core = Line("Ampere Bolt", width, CoreColor);
            if (flash)
            {
                GameObject light = new GameObject("Ampere Flash");
                light.transform.SetParent(transform, false);
                light.transform.position = to;
                bolt.flash = light.AddComponent<Light>();
                bolt.flash.type = LightType.Point;
                bolt.flash.color = BoltColor;
                bolt.flash.range = 3.5f;
                bolt.flash.intensity = 0f;
                bolt.flash.shadows = LightShadows.None;
            }
            bolts.Add(bolt);
        }

        private LineRenderer Line(string name, float width, Color colour)
        {
            GameObject part = new GameObject(name);
            part.transform.SetParent(transform, false);
            LineRenderer line = part.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.positionCount = BoltSegments + 1;
            line.widthMultiplier = width;
            line.startColor = line.endColor = colour;
            line.numCapVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;
            return line;
        }

        private void Animate()
        {
            float now = Time.unscaledTime;
            for (int i = bolts.Count - 1; i >= 0; i--)
            {
                Bolt bolt = bolts[i];
                float age = now - bolt.startsAt;
                if (age < 0f) continue;
                if (age > Mathf.Max(BoltSeconds, FlashSeconds)) { DestroyBolt(bolt); bolts.RemoveAt(i); continue; }
                float fade = 1f - Mathf.Clamp01(age / BoltSeconds);
                bool visible = fade > 0f;
                bolt.glow.enabled = bolt.core.enabled = visible;
                if (visible)
                {
                    // A new jagged path every frame makes the bolt crackle.
                    Vector3 across = Vector3.Cross(bolt.to - bolt.from, Vector3.up).normalized;
                    for (int s = 0; s <= BoltSegments; s++)
                    {
                        float t = s / (float)BoltSegments;
                        float jitter = s == 0 || s == BoltSegments ? 0f : (UnityEngine.Random.value - 0.5f) * 0.45f * Mathf.Sin(t * Mathf.PI);
                        Vector3 point = Vector3.Lerp(bolt.from, bolt.to, t) + across * jitter + Vector3.up * jitter * 0.6f;
                        bolt.glow.SetPosition(s, point);
                        bolt.core.SetPosition(s, point);
                    }
                    bolt.glow.widthMultiplier = bolt.width * 3.2f * fade;
                    bolt.core.widthMultiplier = bolt.width * (0.6f + 0.4f * fade);
                    Color glow = BoltColor; glow.a = 0.45f * fade;
                    Color core = CoreColor; core.a = fade;
                    bolt.glow.startColor = bolt.glow.endColor = glow;
                    bolt.core.startColor = bolt.core.endColor = core;
                }
                if (bolt.flash != null) bolt.flash.intensity = 6f * Mathf.Pow(1f - Mathf.Clamp01(age / FlashSeconds), 2f);
            }
        }

        private static void DestroyBolt(Bolt bolt)
        {
            if (bolt.glow != null) Destroy(bolt.glow.gameObject);
            if (bolt.core != null) Destroy(bolt.core.gameObject);
            if (bolt.flash != null) Destroy(bolt.flash.gameObject);
        }

        // Sprites/Default is unlit and transparent, so the bolt glows the same in any lighting.
        private bool EnsureMaterial()
        {
            if (material != null) return true;
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) return false;
            material = new Material(shader) { name = "Ampere Bolt" };
            return true;
        }
    }
}
