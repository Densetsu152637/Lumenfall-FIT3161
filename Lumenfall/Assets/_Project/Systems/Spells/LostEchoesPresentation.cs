using System.Collections.Generic;
using UnityEngine;
using Lumenfall.GridMovement;

namespace Lumenfall.Spells
{
    /// <summary>
    /// Code-built feedback for Lost Echoes, so the spell needs no art or audio assets: an expanding pulse on every cast,
    /// and a short glow, light beam and chime where something is revealed. The glow fades completely, so a revealed
    /// spot returns to its unfound look.
    /// </summary>
    public sealed class LostEchoesPresentation : MonoBehaviour
    {
        private static readonly Color EchoColor = new Color(0.72f, 0.62f, 1f);
        private const float PulseSeconds = 0.5f;
        private const float FlashSeconds = 1.4f;
        private static LostEchoesPresentation instance;

        private readonly List<Effect> effects = new List<Effect>();
        private SpellCaster caster;
        private WorldStateService subscribedState;
        private Material material;
        private Texture2D glowTexture;
        private AudioSource audioSource;
        private AudioClip pulseClip;
        private AudioClip revealClip;

        private sealed class Effect
        {
            public GameObject root;
            public float started;
            public float duration;
            public Vector3 startScale;
            public Vector3 endScale;
            public Renderer[] renderers;
            public float peakAlpha;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;
            GameObject host = new GameObject("[Lost Echoes Presentation]");
            instance = host.AddComponent<LostEchoesPresentation>();
            DontDestroyOnLoad(host);
        }

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
            pulseClip = BuildTone("LostEchoesPulse", 0.35f, 520f, 330f, 0f, 0.25f);
            revealClip = BuildTone("LostEchoesReveal", 1.1f, 880f, 880f, 1320f, 0.35f);
        }

        private void OnDestroy()
        {
            if (caster != null) caster.LostEchoesCast -= OnCast;
            if (subscribedState != null) subscribedState.ObjectRevealed -= OnRevealed;
            if (instance == this) instance = null;
            foreach (Effect effect in effects) if (effect.root != null) Destroy(effect.root);
            if (material != null) Destroy(material);
            if (glowTexture != null) Destroy(glowTexture);
        }

        private void Update()
        {
            Subscribe();
            UpdateEffects();
        }

        // The player's SpellCaster is added at runtime by the world loader, so both subscriptions are refreshed.
        private void Subscribe()
        {
            if (caster == null)
            {
                SpellCaster found = FindAnyObjectByType<SpellCaster>();
                if (found != null) { caster = found; caster.LostEchoesCast += OnCast; }
            }
            WorldStateService state = WorldStateService.Instance;
            if (state != subscribedState)
            {
                if (subscribedState != null) subscribedState.ObjectRevealed -= OnRevealed;
                subscribedState = state;
                if (state != null) state.ObjectRevealed += OnRevealed;
            }
        }

        private void OnCast(Vector2Int cell, int radius, int found)
        {
            if (caster == null || !EnsureMaterial()) return;
            GridWorld world = caster.GetComponent<GridMover>()?.World;
            if (world == null) return;
            float reach = (radius + 0.5f) * world.CellSize;
            GameObject pulse = new GameObject("Lost Echoes Pulse");
            pulse.transform.SetParent(transform, false);
            pulse.transform.SetPositionAndRotation(caster.transform.position + Vector3.up * 0.06f, world.transform.rotation);
            LineRenderer line = pulse.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = 4;
            // A diamond matches the Manhattan search range. Points are unit-sized in local XY; the 90-degree X rotation
            // below lays that plane flat on the ground and the transform scale grows it.
            line.SetPositions(new[] { Vector3.up, Vector3.right, Vector3.down, Vector3.left });
            line.widthMultiplier = 0.07f;
            line.startColor = line.endColor = EchoColor;
            line.alignment = LineAlignment.TransformZ;
            pulse.transform.Rotate(90f, 0f, 0f, Space.Self);
            AddEffect(pulse, PulseSeconds, Vector3.one * 0.2f, Vector3.one * reach, 1f);
            audioSource.PlayOneShot(pulseClip, found > 0 ? 0.35f : 0.45f);
        }

        private void OnRevealed(WorldObjectSnapshot revealed)
        {
            if (revealed == null || !EnsureMaterial()) return;
            GridWorld world = caster != null ? caster.GetComponent<GridMover>()?.World : FindAnyObjectByType<GridWorld>();
            if (world == null) return;
            float ground = caster != null ? world.WorldToElevation(caster.transform.position) : revealed.elevation;
            Vector3 spot = world.CellToWorld(revealed.cell, ground);
            float size = world.CellSize;

            GameObject flash = new GameObject("Lost Echoes Reveal");
            flash.transform.SetParent(transform, false);
            flash.transform.SetPositionAndRotation(spot, world.transform.rotation);
            GameObject glow = GameObject.CreatePrimitive(PrimitiveType.Quad);
            glow.name = "Glow";
            Destroy(glow.GetComponent<Collider>());
            glow.transform.SetParent(flash.transform, false);
            glow.transform.localPosition = Vector3.up * 0.05f;
            glow.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            glow.transform.localScale = Vector3.one * size * 1.6f;
            // Each glow fades on its own material instance, destroyed with the effect.
            glow.GetComponent<Renderer>().sharedMaterial = new Material(material) { mainTexture = glowTexture, color = EchoColor };

            LineRenderer beam = flash.AddComponent<LineRenderer>();
            beam.sharedMaterial = material;
            beam.useWorldSpace = false;
            beam.positionCount = 2;
            beam.SetPositions(new[] { Vector3.zero, Vector3.up * 1.6f });
            beam.startWidth = size * 0.35f;
            beam.endWidth = 0f;
            Color top = EchoColor; top.a = 0f;
            beam.startColor = EchoColor;
            beam.endColor = top;

            AddEffect(flash, FlashSeconds, Vector3.one * 0.6f, Vector3.one * 1.15f, 0.9f);
            audioSource.PlayOneShot(revealClip, 0.6f);
        }

        private void AddEffect(GameObject root, float duration, Vector3 startScale, Vector3 endScale, float peakAlpha)
        {
            root.transform.localScale = startScale;
            effects.Add(new Effect
            {
                root = root, started = Time.unscaledTime, duration = duration,
                startScale = startScale, endScale = endScale,
                renderers = root.GetComponentsInChildren<Renderer>(), peakAlpha = peakAlpha
            });
        }

        private void UpdateEffects()
        {
            for (int i = effects.Count - 1; i >= 0; i--)
            {
                Effect effect = effects[i];
                float t = (Time.unscaledTime - effect.started) / effect.duration;
                if (effect.root == null || t >= 1f)
                {
                    if (effect.root != null) DestroyEffect(effect);
                    effects.RemoveAt(i);
                    continue;
                }
                float eased = 1f - (1f - t) * (1f - t);
                effect.root.transform.localScale = Vector3.LerpUnclamped(effect.startScale, effect.endScale, eased);
                // Quick fade in, then a long fade out so the spot settles back to its unfound look.
                float alpha = effect.peakAlpha * Mathf.Min(1f, t * 8f) * (1f - t);
                foreach (Renderer renderer in effect.renderers)
                {
                    if (renderer is LineRenderer line)
                    {
                        Color start = line.startColor; start.a = alpha;
                        Color end = line.endColor; end.a = line.loop ? alpha : 0f;
                        line.startColor = start; line.endColor = end;
                    }
                    else if (renderer != null && renderer.sharedMaterial != null && renderer.sharedMaterial != material)
                    {
                        Color colour = renderer.sharedMaterial.color; colour.a = alpha;
                        renderer.sharedMaterial.color = colour;
                    }
                }
            }
        }

        private static void DestroyEffect(Effect effect)
        {
            foreach (Renderer renderer in effect.renderers)
                if (renderer != null && !(renderer is LineRenderer) && renderer.sharedMaterial != null) Destroy(renderer.sharedMaterial);
            Destroy(effect.root);
        }

        // Sprites/Default is transparent, unlit, double-sided and uses vertex colours, which suits flat overlays.
        private bool EnsureMaterial()
        {
            if (material != null) return true;
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) return false;
            material = new Material(shader) { name = "Lost Echoes Overlay" };
            glowTexture = new Texture2D(64, 64, TextureFormat.RGBA32, false) { name = "Lost Echoes Glow", wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(32f, 32f)) / 32f;
                    float alpha = Mathf.Clamp01(1f - distance);
                    glowTexture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha * alpha));
                }
            glowTexture.Apply();
            return true;
        }

        // Short procedural tones keep the spell self-contained: a falling hum for the pulse and a bright two-note chime
        // for a reveal.
        private static AudioClip BuildTone(string name, float seconds, float startHz, float endHz, float harmonyHz, float volume)
        {
            const int sampleRate = 44100;
            int samples = Mathf.CeilToInt(seconds * sampleRate);
            float[] data = new float[samples];
            float phase = 0f, harmonyPhase = 0f;
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)samples;
                float frequency = Mathf.Lerp(startHz, endHz, t);
                phase += 2f * Mathf.PI * frequency / sampleRate;
                harmonyPhase += 2f * Mathf.PI * harmonyHz / sampleRate;
                float envelope = Mathf.Min(1f, t * 40f) * Mathf.Exp(-4f * t);
                float harmony = harmonyHz > 0f && t > 0.12f ? 0.6f * Mathf.Sin(harmonyPhase) * Mathf.Exp(-4f * (t - 0.12f)) : 0f;
                data[i] = volume * envelope * (Mathf.Sin(phase) + harmony);
            }
            AudioClip clip = AudioClip.Create(name, samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
