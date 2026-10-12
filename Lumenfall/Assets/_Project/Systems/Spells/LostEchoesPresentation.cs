using System;
using System.Collections.Generic;
using UnityEngine;
using Lumenfall.GridMovement;

namespace Lumenfall.Spells
{
    /// <summary>
    /// Feedback for Lost Echoes, built in code so the spell needs no art assets:
    /// <list type="bullet">
    /// <item>Casting: the player flares (point light, hand glow, brief cast pose) and a diamond pulse expands.</item>
    /// <item>Sonar: if something hidden remains within <see cref="SonarRange"/> cells, a quieter echo of the pulse returns,
    /// sooner and louder the closer it is. It gives distance only, never direction, so the player still searches.</item>
    /// <item>Reveal: motes gather into the spot, then a glow and light beam fade out in the object's kind colour.</item>
    /// </list>
    /// Sounds load from <c>Resources/Audio/LostEchoes/{Pulse,Echo,Gather,Reveal}</c> when present and otherwise fall back
    /// to generated tones with a natural echo tail.
    /// </summary>
    public sealed class LostEchoesPresentation : MonoBehaviour
    {
        public const int SonarRange = 8;
        private static readonly Color EchoColor = new Color(0.72f, 0.62f, 1f);
        private static readonly Color ItemColor = new Color(1f, 0.82f, 0.4f);
        private static readonly Color PassageColor = new Color(0.45f, 0.75f, 1f);
        private static readonly Color MemoryColor = new Color(0.8f, 0.55f, 1f);
        // The player controller comes from a demo whose attack_01 chains on through attack, hide and death states, so the
        // pose is held briefly, never extended by repeated casts, and always handed back to the default idle state.
        private const string CastPoseState = "Base Layer.attack_01";
        private const string IdleState = "Base Layer.idle_01";
        private const float PoseSeconds = 0.35f;
        private const float PulseSeconds = 0.5f;
        private const float GatherSeconds = 0.55f;
        private const float FlashSeconds = 1.4f;
        private const int MoteCount = 16;
        private static LostEchoesPresentation instance;

        private readonly List<Effect> effects = new List<Effect>();
        private readonly List<ScheduledSound> sounds = new List<ScheduledSound>();
        private SpellCaster caster;
        private WorldStateService subscribedState;
        private Material material;
        private Texture2D glowTexture;
        private AudioSource audioSource;
        private AudioClip pulseClip, echoClip, gatherClip, revealClip;
        private Animator poseAnimator;
        private float poseReturnAt = -1f;
        private float poseStartedAt;

        private sealed class Effect
        {
            public GameObject root;
            public float started;
            public float delay;
            public float duration;
            public Action<Effect, float> animate;
            // Keep binding identity after Unity makes a destroyed target compare equal to null.
            public bool boundToTarget;
            public Transform boundTarget;
            public readonly List<Material> materials = new List<Material>();
            public readonly List<Transform> parts = new List<Transform>();
            public readonly List<Vector3> origins = new List<Vector3>();
            public Vector3 target;
        }

        private struct ScheduledSound { public float at; public AudioClip clip; public float volume; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;
            GameObject host = new GameObject("[Lost Echoes Presentation]");
            instance = host.AddComponent<LostEchoesPresentation>();
            DontDestroyOnLoad(host);
        }

        /// <summary>Reveal colour from tags: <c>echo:passage</c> blue, <c>echo:memory</c> violet, <c>echo:item</c> or <c>pickup</c> gold.</summary>
        public static Color KindColour(WorldObjectSnapshot target)
        {
            string[] tags = target?.tags ?? Array.Empty<string>();
            bool Has(string tag) => Array.Exists(tags, value => string.Equals(value, tag, StringComparison.OrdinalIgnoreCase));
            if (Has("echo:passage")) return PassageColor;
            if (Has("echo:memory")) return MemoryColor;
            if (Has("echo:item") || Has(WorldStateService.PickupTag)) return ItemColor;
            return EchoColor;
        }

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
            pulseClip = LoadClip("Pulse") ?? BuildTone("LostEchoesPulse", 0.35f, 520f, 330f, 0f, 0.25f);
            echoClip = LoadClip("Echo") ?? pulseClip;
            gatherClip = LoadClip("Gather") ?? BuildTone("LostEchoesGather", 0.5f, 300f, 700f, 0f, 0.12f);
            revealClip = LoadClip("Reveal") ?? BuildTone("LostEchoesReveal", 1.1f, 880f, 880f, 1320f, 0.35f);
        }

        private static AudioClip LoadClip(string name) => Resources.Load<AudioClip>("Audio/LostEchoes/" + name);

        private void OnDestroy()
        {
            if (caster != null) caster.LostEchoesCast -= OnCast;
            if (subscribedState != null) subscribedState.ObjectRevealed -= OnRevealed;
            if (instance == this) instance = null;
            foreach (Effect effect in effects) DestroyEffect(effect);
            if (material != null) Destroy(material);
            if (glowTexture != null) Destroy(glowTexture);
        }

        private void Update()
        {
            Subscribe();
            UpdateSounds();
            UpdatePose();
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
            PlayerFlare(world);
            Pulse(world, radius);
            audioSource.PlayOneShot(pulseClip, found > 0 ? 0.35f : 0.45f);

            // Sonar: the nearest thing still hidden answers the pulse. Closer means a sooner, louder echo.
            WorldStateService state = WorldStateService.Instance;
            if (state == null || !state.TryFindNearestConcealed(cell, SonarRange, out _, out int distance)) return;
            float closeness = 1f - Mathf.Clamp01((distance - 1f) / (SonarRange - 1f));
            float delay = Mathf.Lerp(0.95f, 0.18f, closeness);
            float volume = Mathf.Lerp(0.1f, 0.4f, closeness);
            Schedule(echoClip, delay, volume);
            Schedule(echoClip, delay * 2f, volume * 0.45f);
        }

        private void PlayerFlare(GridWorld world)
        {
            Transform player = caster.transform;
            float cellSize = world.CellSize;
            // A brief point light around the player.
            GameObject flare = new GameObject("Lost Echoes Cast Light");
            flare.transform.SetParent(transform, false);
            Light light = flare.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = EchoColor;
            light.range = cellSize * 3.5f;
            light.intensity = 0f;
            AddEffect(flare, 0f, 0.7f, (effect, t) =>
            {
                effect.root.transform.position = player.position + Vector3.up * 1f;
                light.intensity = 2.5f * Mathf.Sin(t * Mathf.PI);
            }, player);

            // A glow at hand height in front of the player.
            GameObject hand = Billboard("Lost Echoes Hand Glow", EchoColor, out Material handMaterial);
            Effect handEffect = AddEffect(hand, 0f, 0.6f, (effect, t) =>
            {
                effect.root.transform.position = player.position + Vector3.up * 0.9f + player.forward * 0.35f;
                FaceCamera(effect.root.transform);
                effect.root.transform.localScale = Vector3.one * cellSize * (0.25f + 0.45f * Mathf.Sin(t * Mathf.PI));
                SetAlpha(handMaterial, Mathf.Sin(t * Mathf.PI));
            }, player);
            handEffect.materials.Add(handMaterial);

            // A short cast pose. A cast during an active pose only flares; it never restarts or extends the pose.
            Animator animator = player.GetComponentInChildren<Animator>();
            int pose = Animator.StringToHash(CastPoseState);
            if (poseReturnAt < 0f && animator != null && animator.isActiveAndEnabled &&
                animator.HasState(0, pose) && animator.HasState(0, Animator.StringToHash(IdleState)))
            {
                animator.CrossFadeInFixedTime(pose, 0.08f, 0);
                poseAnimator = animator;
                poseStartedAt = Time.unscaledTime;
                poseReturnAt = poseStartedAt + PoseSeconds;
            }
        }

        private void UpdatePose()
        {
            if (poseReturnAt < 0f) return;
            bool expired = Time.unscaledTime >= poseReturnAt;
            // Hand back early if the animator has already moved on from the pose into the demo chain. The crossfade
            // only takes effect on the animator's next update, so the check waits briefly after the pose starts.
            bool leftPose = Time.unscaledTime - poseStartedAt > 0.12f && poseAnimator != null && poseAnimator.isActiveAndEnabled && !poseAnimator.IsInTransition(0) &&
                poseAnimator.GetCurrentAnimatorStateInfo(0).fullPathHash != Animator.StringToHash(CastPoseState);
            if (!expired && !leftPose) return;
            // idle_01 is the default state; its Speed transition takes over at once if the player is walking.
            if (poseAnimator != null && poseAnimator.isActiveAndEnabled)
                poseAnimator.CrossFadeInFixedTime(Animator.StringToHash(IdleState), 0.15f, 0);
            poseReturnAt = -1f;
            poseAnimator = null;
        }

        private void Pulse(GridWorld world, int radius)
        {
            float reach = (radius + 0.5f) * world.CellSize;
            GameObject pulse = new GameObject("Lost Echoes Pulse");
            pulse.transform.SetParent(transform, false);
            pulse.transform.SetPositionAndRotation(caster.transform.position + Vector3.up * 0.06f, world.transform.rotation);
            pulse.transform.Rotate(90f, 0f, 0f, Space.Self);
            LineRenderer line = pulse.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = 4;
            // A diamond matches the Manhattan search range. Points are unit-sized in local XY; the 90-degree X rotation
            // lays that plane flat on the ground and the transform scale grows it.
            line.SetPositions(new[] { Vector3.up, Vector3.right, Vector3.down, Vector3.left });
            line.widthMultiplier = 0.07f;
            line.alignment = LineAlignment.TransformZ;
            AddEffect(pulse, 0f, PulseSeconds, (effect, t) =>
            {
                float eased = 1f - (1f - t) * (1f - t);
                effect.root.transform.localScale = Vector3.one * Mathf.Lerp(0.2f, reach, eased);
                Color colour = EchoColor; colour.a = Mathf.Min(1f, t * 8f) * (1f - t);
                line.startColor = line.endColor = colour;
            });
        }

        private void OnRevealed(WorldObjectSnapshot revealed)
        {
            if (revealed == null || !EnsureMaterial()) return;
            GridWorld world = caster != null ? caster.GetComponent<GridMover>()?.World : FindAnyObjectByType<GridWorld>();
            if (world == null) return;
            float ground = caster != null ? world.WorldToElevation(caster.transform.position) : revealed.elevation;
            Vector3 spot = world.CellToWorld(revealed.cell, ground);
            Color colour = KindColour(revealed);
            Gather(world, spot, colour);
            audioSource.PlayOneShot(gatherClip, 0.4f);
            Flash(world, spot, colour, GatherSeconds * 0.85f);
            Schedule(revealClip, GatherSeconds * 0.8f, 0.6f);
        }

        // Motes pull in from around the spot and condense into it, so a find feels gathered rather than switched on.
        private void Gather(GridWorld world, Vector3 spot, Color colour)
        {
            GameObject root = new GameObject("Lost Echoes Gather");
            root.transform.SetParent(transform, false);
            Material moteMaterial = new Material(material) { mainTexture = glowTexture, color = colour };
            Effect effect = AddEffect(root, 0f, GatherSeconds, (e, t) =>
            {
                float pull = t * t;
                for (int i = 0; i < e.parts.Count; i++)
                {
                    Transform mote = e.parts[i];
                    mote.position = Vector3.Lerp(e.origins[i], e.target, pull);
                    FaceCamera(mote);
                    mote.localScale = Vector3.one * world.CellSize * Mathf.Lerp(0.22f, 0.1f, t);
                }
                SetAlpha(moteMaterial, Mathf.Min(1f, t * 4f));
            });
            effect.materials.Add(moteMaterial);
            effect.target = spot + Vector3.up * 0.3f;
            for (int i = 0; i < MoteCount; i++)
            {
                GameObject mote = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(mote.GetComponent<Collider>());
                mote.transform.SetParent(root.transform, false);
                mote.GetComponent<Renderer>().sharedMaterial = moteMaterial;
                float angle = i * Mathf.PI * 2f / MoteCount + UnityEngine.Random.Range(-0.2f, 0.2f);
                float distance = world.CellSize * UnityEngine.Random.Range(1.2f, 2.2f);
                Vector3 origin = spot + new Vector3(Mathf.Cos(angle) * distance, UnityEngine.Random.Range(0.2f, 1.2f), Mathf.Sin(angle) * distance);
                mote.transform.position = origin;
                effect.parts.Add(mote.transform);
                effect.origins.Add(origin);
            }
        }

        // The glow and light beam fade out completely, so a revealed spot returns to its unfound look.
        private void Flash(GridWorld world, Vector3 spot, Color colour, float delay)
        {
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
            Material glowMaterial = new Material(material) { mainTexture = glowTexture, color = colour };
            glow.GetComponent<Renderer>().sharedMaterial = glowMaterial;

            LineRenderer beam = flash.AddComponent<LineRenderer>();
            beam.sharedMaterial = material;
            beam.useWorldSpace = false;
            beam.positionCount = 2;
            beam.SetPositions(new[] { Vector3.zero, Vector3.up * 1.6f });
            beam.startWidth = size * 0.35f;
            beam.endWidth = 0f;
            Color top = colour; top.a = 0f;
            beam.endColor = top;

            Effect effect = AddEffect(flash, delay, FlashSeconds, (e, t) =>
            {
                float eased = 1f - (1f - t) * (1f - t);
                e.root.transform.localScale = Vector3.one * Mathf.Lerp(0.6f, 1.15f, eased);
                float alpha = 0.9f * Mathf.Min(1f, t * 8f) * (1f - t);
                SetAlpha(glowMaterial, alpha);
                Color bottom = colour; bottom.a = alpha;
                beam.startColor = bottom;
            });
            effect.materials.Add(glowMaterial);
        }

        private GameObject Billboard(string name, Color colour, out Material instanceMaterial)
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(transform, false);
            instanceMaterial = new Material(material) { mainTexture = glowTexture, color = colour };
            quad.GetComponent<Renderer>().sharedMaterial = instanceMaterial;
            return quad;
        }

        private static void FaceCamera(Transform target)
        {
            Camera camera = Camera.main;
            if (camera != null) target.rotation = camera.transform.rotation;
        }

        private static void SetAlpha(Material target, float alpha)
        {
            Color colour = target.color; colour.a = Mathf.Clamp01(alpha); target.color = colour;
        }

        private Effect AddEffect(GameObject root, float delay, float duration, Action<Effect, float> animate, Transform boundTarget = null)
        {
            Effect effect = new Effect
            {
                root = root, started = Time.unscaledTime, delay = delay, duration = duration, animate = animate,
                boundToTarget = boundTarget != null, boundTarget = boundTarget
            };
            if (delay > 0f) root.SetActive(false);
            else animate(effect, 0f);
            effects.Add(effect);
            return effect;
        }

        private void UpdateEffects()
        {
            for (int i = effects.Count - 1; i >= 0; i--)
            {
                Effect effect = effects[i];
                float elapsed = Time.unscaledTime - effect.started - effect.delay;
                if (effect.root == null || (effect.boundToTarget && effect.boundTarget == null) || elapsed >= effect.duration)
                {
                    DestroyEffect(effect);
                    effects.RemoveAt(i);
                    continue;
                }
                if (elapsed < 0f) continue;
                if (!effect.root.activeSelf) effect.root.SetActive(true);
                effect.animate(effect, elapsed / effect.duration);
            }
        }

        private static void DestroyEffect(Effect effect)
        {
            foreach (Material owned in effect.materials) if (owned != null) Destroy(owned);
            if (effect.root != null) Destroy(effect.root);
        }

        private void Schedule(AudioClip clip, float delay, float volume)
        {
            if (clip != null) sounds.Add(new ScheduledSound { at = Time.unscaledTime + delay, clip = clip, volume = volume });
        }

        private void UpdateSounds()
        {
            for (int i = sounds.Count - 1; i >= 0; i--)
            {
                if (Time.unscaledTime < sounds[i].at) continue;
                audioSource.PlayOneShot(sounds[i].clip, sounds[i].volume);
                sounds.RemoveAt(i);
            }
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

        // Fallback tones used until real clips are placed in Resources/Audio/LostEchoes. Each gets a short echo tail
        // (decaying, slightly softened repeats) so it sounds less synthetic.
        private static AudioClip BuildTone(string name, float seconds, float startHz, float endHz, float harmonyHz, float volume)
        {
            const int sampleRate = 44100;
            const float tailDelay = 0.11f;
            const int repeats = 3;
            int dry = Mathf.CeilToInt(seconds * sampleRate);
            int offset = Mathf.CeilToInt(tailDelay * sampleRate);
            float[] source = new float[dry];
            float phase = 0f, harmonyPhase = 0f;
            for (int i = 0; i < dry; i++)
            {
                float t = i / (float)dry;
                float frequency = Mathf.Lerp(startHz, endHz, t);
                phase += 2f * Mathf.PI * frequency / sampleRate;
                harmonyPhase += 2f * Mathf.PI * harmonyHz / sampleRate;
                float envelope = Mathf.Min(1f, t * 40f) * Mathf.Exp(-4f * t);
                float harmony = harmonyHz > 0f && t > 0.12f ? 0.6f * Mathf.Sin(harmonyPhase) * Mathf.Exp(-4f * (t - 0.12f)) : 0f;
                source[i] = volume * envelope * (Mathf.Sin(phase) + harmony);
            }
            float[] data = new float[dry + offset * repeats];
            for (int i = 0; i < dry; i++) data[i] += source[i];
            for (int r = 1; r <= repeats; r++)
            {
                float gain = Mathf.Pow(0.38f, r);
                float previous = 0f;
                for (int i = 0; i < dry; i++)
                {
                    // A one-pole low-pass makes each repeat a little duller, like a real reflection.
                    previous = Mathf.Lerp(previous, source[i], 0.35f);
                    data[i + offset * r] += previous * gain;
                }
            }
            AudioClip clip = AudioClip.Create(name, data.Length, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
