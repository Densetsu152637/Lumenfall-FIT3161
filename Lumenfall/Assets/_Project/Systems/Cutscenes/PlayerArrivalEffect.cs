using System.Collections.Generic;
using Lumenfall.GridMovement;
using UnityEngine;

namespace Lumenfall.Cutscenes
{
    /// <summary>
    /// The opening arrival: the startup menu's dissolving panel becomes this effect's sparks, which drift outward as the
    /// menu's own dissolve does, then stream into the player's spawn. Each spark that lands feeds a glow and a column of
    /// light there. Once all have gathered and the glow has pulsed, <see cref="Reveal"/> (called by the cutscene behind
    /// a flash) shows the hidden player, who grows slightly to full size while sparks burst and motes rise around them.
    /// Sparks are screen-space IMGUI, drawn just behind the cutscene overlay; the glow, column, light and motes are
    /// world-space. Everything runs on unscaled time, so it starts while the menu still has the world paused.
    /// </summary>
    public sealed class PlayerArrivalEffect : MonoBehaviour
    {
        private const int SparkCount = 170;
        /// <summary>Every spark has landed by this long after the effect starts.</summary>
        private const float GatherSeconds = 2.6f;
        private const float HoldSeconds = 0.5f;
        private const float RevealFadeSeconds = 0.9f;
        private const float RiseSeconds = 0.55f;
        private const float MoteSeconds = 1.9f;
        /// <summary>If no cutscene ever reveals the player, the effect reveals them itself after this long.</summary>
        private const float MaxSeconds = 15f;
        private const float GroundGlowHeight = 0.9f;

        private static readonly Color Gold = new Color(0.96f, 0.80f, 0.45f);
        private static readonly Color Cream = new Color(0.97f, 0.93f, 0.85f);
        private static readonly Color Lavender = new Color(0.72f, 0.60f, 1f);

        private struct Spark
        {
            public Vector2 free, velocity, shown;
            public float homeStart, landAt, size, swirl;
            public Color colour;
            public bool star, landed;
        }

        private sealed class Mote
        {
            public Transform transform;
            public Renderer renderer;
            public Vector3 velocity;
            public float born, life, size;
            public Color colour;
            public bool rising;
        }

        private readonly List<Mote> motes = new List<Mote>();
        private readonly List<AudioClip> clips = new List<AudioClip>();
        private readonly System.Random random = new System.Random(1170);
        private Spark[] sparks;
        private int landedCount;
        private float gathered, started, gatheredAt = -1f, revealedAt = -1f, nextMote;
        private Transform player, glow, core, column;
        private Renderer glowRenderer, coreRenderer, columnRenderer;
        private Light glowLight;
        private AudioSource audioSource;
        private MaterialPropertyBlock block;
        private Vector3 playerScale = Vector3.one;
        private Vector2 target;

        /// <summary>True until every spark has landed and the glow has pulsed; the cutscene's flash waits for this.</summary>
        public bool IsGathering => revealedAt < 0f && (gatheredAt < 0f || Time.unscaledTime - gatheredAt < HoldSeconds);

        public bool IsRevealed => revealedAt >= 0f;

        /// <summary>
        /// Hides <paramref name="followed"/> and starts gathering light onto them. Sparks start across
        /// <paramref name="origin"/> (screen pixels, top-left origin), normally the menu panel; without one they start
        /// across the middle of the screen.
        /// </summary>
        public static PlayerArrivalEffect Begin(Transform followed, Rect? origin)
        {
            if (followed == null || !CutsceneArt.Ensure()) return null;
            GameObject host = new GameObject("[Player Arrival]");
            PlayerArrivalEffect effect = host.AddComponent<PlayerArrivalEffect>();
            effect.Setup(followed, origin ?? new Rect(Screen.width * 0.3f, Screen.height * 0.25f, Screen.width * 0.4f, Screen.height * 0.4f));
            return effect;
        }

        /// <summary>Shows the player now with the burst of light, if they are still hidden.</summary>
        public void Reveal()
        {
            if (revealedAt >= 0f) return;
            revealedAt = Time.unscaledTime;
            ShowPlayer();
            if (player != null) player.localScale = playerScale * 0.9f;
            for (int i = 0; i < sparks.Length; i++) sparks[i].landed = true;
            coreRenderer.enabled = false;
            audioSource.PlayOneShot(Keep(RevealClip()), 0.7f);
            Vector3 centre = GroundPoint();
            for (int i = 0; i < 36; i++)
            {
                float angle = i / 36f * Mathf.PI * 2f + Range(-0.08f, 0.08f);
                Vector3 outward = new Vector3(Mathf.Cos(angle), Range(0.1f, 0.9f), Mathf.Sin(angle)) * Range(1.8f, 3.6f);
                SpawnMote(centre, outward, Range(0.6f, 1.1f), Range(0.1f, 0.22f), i % 3 == 0 ? Cream : i % 3 == 1 ? Gold : Lavender);
            }
        }

        /// <summary>Ends the effect at once, leaving the player visible at their normal size.</summary>
        public void Stop()
        {
            ShowPlayer();
            RestoreScale();
            if (this != null) Destroy(gameObject);
        }

        private void Setup(Transform followed, Rect origin)
        {
            player = followed;
            playerScale = followed.localScale;
            started = Time.unscaledTime;
            block = new MaterialPropertyBlock();
            HidePlayer();

            target = TargetOnScreen();
            sparks = new Spark[SparkCount];
            for (int i = 0; i < sparks.Length; i++)
            {
                // The same start as the menu's dissolve: points across the panel, pushed away from its centre.
                Vector2 point = new Vector2(Range(origin.xMin, origin.xMax), Range(origin.yMin, origin.yMax));
                Vector2 away = point - origin.center;
                Vector2 direction = away.sqrMagnitude < 1f ? Vector2.up : away.normalized;
                float homeStart = Range(0.2f, 0.85f);
                sparks[i] = new Spark
                {
                    free = point,
                    shown = point,
                    velocity = direction * Range(60f, 240f) + Vector2.down * Range(10f, 60f),
                    homeStart = homeStart,
                    landAt = Mathf.Max(homeStart + 0.9f, GatherSeconds - Range(0f, 0.7f)),
                    size = Range(6f, 16f),
                    swirl = Range(-1f, 1f) * Range(40f, 150f),
                    colour = Next() < 0.5f ? Gold : Next() < 0.6f ? Lavender : Cream,
                    star = Next() < 0.3f
                };
            }

            glow = CutsceneArt.Quad(transform, "Arrival Glow", CutsceneArt.GlowMaterial, out glowRenderer);
            core = CutsceneArt.Quad(transform, "Arrival Spark", CutsceneArt.SparkleMaterial, out coreRenderer);
            column = CutsceneArt.Quad(transform, "Arrival Light Column", CutsceneArt.ColumnMaterial, out columnRenderer);
            Tint(glowRenderer, Gold, 0f);
            Tint(coreRenderer, Cream, 0f);
            Tint(columnRenderer, Cream, 0f);

            GameObject lightObject = new GameObject("Arrival Light");
            lightObject.transform.SetParent(transform, false);
            glowLight = lightObject.AddComponent<Light>();
            glowLight.type = LightType.Point;
            glowLight.color = Gold;
            glowLight.shadows = LightShadows.None;
            glowLight.range = 5f;
            glowLight.intensity = 0f;

            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
            audioSource.PlayOneShot(Keep(GatherClip(GatherSeconds + HoldSeconds)), 0.5f);
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            float t = now - started;
            float deltaTime = Time.unscaledDeltaTime;
            target = TargetOnScreen();
            if (revealedAt < 0f)
            {
                PlayerVisibility.Refresh();
                UpdateSparks(t, deltaTime);
                if (gatheredAt < 0f && (landedCount >= sparks.Length || t > GatherSeconds + 0.2f)) gatheredAt = now;
                if (t > MaxSeconds) Reveal();
            }

            Vector3 centre = GroundPoint();
            glow.position = centre;
            core.position = centre;
            column.position = centre + Vector3.up * 2.4f;
            glowLight.transform.position = centre + Vector3.up * 0.3f;

            if (revealedAt < 0f)
            {
                // Builds as sparks land, then pulses while it waits for the flash.
                gathered = Mathf.MoveTowards(gathered, landedCount / (float)sparks.Length, deltaTime * 1.5f);
                float pulse = gatheredAt < 0f ? 0f : 0.5f + 0.5f * Mathf.Sin((now - gatheredAt) * 14f);
                float level = gathered + 0.15f * pulse;
                glow.localScale = Vector3.one * Mathf.Lerp(0.2f, 1.5f, level);
                core.localScale = Vector3.one * Mathf.Lerp(0.15f, 0.7f, level);
                column.localScale = new Vector3(Mathf.Lerp(0.4f, 0.9f, level), 4.8f, 1f);
                Tint(glowRenderer, Gold, Mathf.Clamp01(level));
                Tint(coreRenderer, Cream, Mathf.Clamp01(level * 1.2f));
                Tint(columnRenderer, Cream, 0.35f * level);
                glowLight.intensity = 3.5f * level;
            }
            else
            {
                float after = Mathf.Clamp01((now - revealedAt) / RevealFadeSeconds);
                glow.localScale = Vector3.one * Mathf.Lerp(2.6f, 3.4f, after);
                column.localScale = new Vector3(Mathf.Lerp(1.4f, 0.6f, after), Mathf.Lerp(4.8f, 7f, after), 1f);
                Tint(glowRenderer, Cream, 0.9f * (1f - after));
                Tint(columnRenderer, Cream, 0.9f * (1f - after) * (1f - after));
                glowLight.intensity = Mathf.Lerp(7f, 0f, after);
                glowLight.range = Mathf.Lerp(7f, 4f, after);

                // The player settles to full size as the light clears.
                float rise = Mathf.Clamp01((now - revealedAt) / RiseSeconds);
                if (player != null) player.localScale = playerScale * Mathf.Lerp(0.9f, 1f, 1f - (1f - rise) * (1f - rise));

                if (now - revealedAt < MoteSeconds && now >= nextMote)
                {
                    nextMote = now + 0.05f;
                    float angle = Range(0f, Mathf.PI * 2f);
                    float radius = Range(0.15f, 0.7f);
                    Vector3 start = (player != null ? player.position : centre) + new Vector3(Mathf.Cos(angle) * radius, Range(0.1f, 1.2f), Mathf.Sin(angle) * radius);
                    SpawnMote(start, new Vector3(0f, Range(0.5f, 1.1f), 0f), Range(1.0f, 1.7f), Range(0.06f, 0.14f), Next() < 0.6f ? Gold : Lavender, true);
                }
                if (after >= 1f && now - revealedAt > MoteSeconds && motes.Count == 0) Destroy(gameObject);
            }

            TickMotes(deltaTime);
        }

        private void UpdateSparks(float t, float deltaTime)
        {
            for (int i = 0; i < sparks.Length; i++)
            {
                Spark spark = sparks[i];
                if (spark.landed) continue;
                // Free drift with the menu dissolve's drag and lift, then an ever-stronger pull onto the spawn.
                spark.velocity *= Mathf.Exp(-1.2f * deltaTime);
                spark.velocity.y += -40f * deltaTime;
                spark.free += spark.velocity * deltaTime;
                float progress = Mathf.Clamp01((t - spark.homeStart) / (spark.landAt - spark.homeStart));
                float pull = progress * progress;
                Vector2 toTarget = target - spark.free;
                Vector2 across = toTarget.sqrMagnitude < 1f ? Vector2.zero : new Vector2(-toTarget.y, toTarget.x).normalized;
                spark.shown = Vector2.Lerp(spark.free, target, pull) + across * spark.swirl * Mathf.Sin(progress * Mathf.PI);
                if (progress >= 1f)
                {
                    spark.landed = true;
                    landedCount++;
                }
                sparks[i] = spark;
            }
        }

        // Drawn with GUI only, never GUILayout, so skip the per-frame layout pass and its allocations.
        private void Awake() => useGUILayout = false;

        private void OnGUI()
        {
            if (revealedAt >= 0f || sparks == null || Event.current.type != EventType.Repaint) return;
            // Just behind the cutscene overlay (-1000), so its flash covers them, and in front of the menu.
            GUI.depth = -999;
            Color previous = GUI.color;
            Texture glowTexture = CutsceneArt.ScreenGlow;
            Texture starTexture = CutsceneArt.ScreenSparkle;
            float t = Time.unscaledTime - started;
            for (int i = 0; i < sparks.Length; i++)
            {
                Spark spark = sparks[i];
                if (spark.landed) continue;
                float progress = Mathf.Clamp01((t - spark.homeStart) / (spark.landAt - spark.homeStart));
                float a = Mathf.Clamp01(t / 0.08f) * Mathf.Lerp(0.85f, 1f, progress);
                float size = spark.size * Mathf.Lerp(1f, 0.45f, progress * progress);
                Vector2 p = spark.shown;
                Draw(new Rect(p.x - size * 1.4f, p.y - size * 1.4f, size * 2.8f, size * 2.8f), glowTexture, spark.colour, 0.45f * a);
                if (spark.star) Draw(new Rect(p.x - size * 0.7f, p.y - size * 0.7f, size * 1.4f, size * 1.4f), starTexture, Cream, a);
                else Draw(new Rect(p.x - size * 0.3f, p.y - size * 0.3f, size * 0.6f, size * 0.6f), glowTexture, Cream, a);
            }
            GUI.color = previous;
        }

        private void LateUpdate()
        {
            Camera view = Camera.main;
            if (view == null) return;
            Quaternion facing = view.transform.rotation;
            glow.rotation = facing;
            core.rotation = facing * Quaternion.Euler(0f, 0f, (Time.unscaledTime - started) * 50f);
            // The column stands upright and only turns about the vertical to face the camera.
            Vector3 flat = Vector3.ProjectOnPlane(view.transform.forward, Vector3.up);
            if (flat.sqrMagnitude > 0.0001f) column.rotation = Quaternion.LookRotation(flat, Vector3.up);
        }

        private void OnDestroy()
        {
            ShowPlayer();
            RestoreScale();
            motes.Clear();
            foreach (AudioClip clip in clips)
                if (clip != null) Destroy(clip);
            clips.Clear();
        }

        private void HidePlayer() => PlayerVisibility.Hide(this);

        private void ShowPlayer() => PlayerVisibility.Show(this);

        private void RestoreScale()
        {
            if (player != null) player.localScale = playerScale;
        }

        private Vector3 GroundPoint() => player != null ? player.position + Vector3.up * GroundGlowHeight : transform.position;

        private Vector2 TargetOnScreen()
        {
            Camera view = Camera.main;
            if (view == null || player == null) return new Vector2(Screen.width / 2f, Screen.height / 2f);
            Vector3 screen = view.WorldToScreenPoint(GroundPoint());
            return new Vector2(screen.x, Screen.height - screen.y);
        }

        private void SpawnMote(Vector3 position, Vector3 velocity, float life, float size, Color colour, bool rising = false)
        {
            Transform mote = CutsceneArt.Quad(transform, "Arrival Mote", CutsceneArt.GlowMaterial, out Renderer moteRenderer);
            mote.position = position;
            mote.localScale = Vector3.one * size;
            motes.Add(new Mote
            {
                transform = mote, renderer = moteRenderer, velocity = velocity, born = Time.unscaledTime, life = life,
                size = size, colour = colour, rising = rising
            });
        }

        // Burst motes slow and fall a little; rising motes keep drifting up and sway.
        private void TickMotes(float deltaTime)
        {
            float now = Time.unscaledTime;
            Camera view = Camera.main;
            for (int i = motes.Count - 1; i >= 0; i--)
            {
                Mote mote = motes[i];
                float age = (now - mote.born) / mote.life;
                if (age >= 1f || mote.transform == null)
                {
                    if (mote.transform != null) Destroy(mote.transform.gameObject);
                    motes.RemoveAt(i);
                    continue;
                }
                bool rising = mote.rising;
                if (rising)
                {
                    mote.transform.position += (mote.velocity + Vector3.right * Mathf.Sin((now - mote.born) * 3f + i) * 0.15f) * deltaTime;
                }
                else
                {
                    mote.velocity *= Mathf.Exp(-3f * deltaTime);
                    mote.velocity += Vector3.down * 0.8f * deltaTime;
                    mote.transform.position += mote.velocity * deltaTime;
                }
                float size = mote.size;
                float twinkle = rising ? 0.7f + 0.3f * Mathf.Sin((now - mote.born) * 9f + i) : 1f;
                mote.transform.localScale = Vector3.one * size * (1f - age * 0.5f);
                if (view != null) mote.transform.rotation = view.transform.rotation;
                float fade = rising ? Mathf.Clamp01(age / 0.15f) * (1f - age) : 1f - age * age;
                Tint(mote.renderer, mote.colour, fade * twinkle);
            }
        }

        private void Tint(Renderer quadRenderer, Color colour, float alpha) => CutsceneArt.Tint(quadRenderer, block, colour, alpha);

        private static void Draw(Rect rect, Texture texture, Color tint, float alpha)
        {
            if (texture == null || alpha <= 0.002f) return;
            GUI.color = new Color(tint.r, tint.g, tint.b, alpha);
            GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, true);
        }

        private AudioClip Keep(AudioClip clip)
        {
            clips.Add(clip);
            return clip;
        }

        private float Next() => (float)random.NextDouble();

        private float Range(float min, float max) => min + Next() * (max - min);

        // A soft major chord that swells as the light gathers, with small twinkles on top.
        private static AudioClip GatherClip(float seconds)
        {
            int count = Mathf.CeilToInt(seconds * CutsceneArt.SampleRate);
            float[] samples = new float[count];
            float[] chord = { 523.25f, 659.25f, 783.99f, 1046.5f };
            System.Random twinkles = new System.Random(5);
            float nextTwinkle = 0.1f, twinkleAt = -1f, twinkleFrequency = 2093f;
            for (int i = 0; i < count; i++)
            {
                float time = i / (float)CutsceneArt.SampleRate;
                float swell = Mathf.Clamp01(time / seconds);
                float pad = 0f;
                for (int n = 0; n < chord.Length; n++)
                    pad += Mathf.Sin(2f * Mathf.PI * chord[n] * time + n) * (1f - n * 0.18f);
                if (time >= nextTwinkle)
                {
                    twinkleAt = time;
                    twinkleFrequency = 1568f + (float)twinkles.NextDouble() * 1600f;
                    nextTwinkle = time + 0.08f + (float)twinkles.NextDouble() * 0.18f;
                }
                float twinkle = twinkleAt < 0f ? 0f : Mathf.Sin(2f * Mathf.PI * twinkleFrequency * (time - twinkleAt)) * Mathf.Exp(-(time - twinkleAt) * 18f);
                float edge = Mathf.Clamp01((seconds - time) / 0.15f);
                samples[i] = (pad * 0.06f * swell * swell + twinkle * 0.12f) * edge;
            }
            return CutsceneArt.Clip("Arrival Gather", samples);
        }

        // A rising three-note chime over a soft breath of air.
        private static AudioClip RevealClip()
        {
            int count = Mathf.CeilToInt(2.2f * CutsceneArt.SampleRate);
            float[] samples = new float[count];
            float[] notes = { 1046.5f, 1318.5f, 1568f };
            System.Random noise = new System.Random(3);
            float smoothed = 0f;
            for (int i = 0; i < count; i++)
            {
                float time = i / (float)CutsceneArt.SampleRate;
                float chime = 0f;
                for (int n = 0; n < notes.Length; n++)
                {
                    float local = time - n * 0.09f;
                    if (local > 0f) chime += Mathf.Sin(2f * Mathf.PI * notes[n] * local) * Mathf.Exp(-local * 2.4f);
                }
                smoothed = Mathf.Lerp(smoothed, (float)noise.NextDouble() * 2f - 1f, 0.05f);
                float breath = smoothed * Mathf.Exp(-time * 3f) * Mathf.Clamp01(time * 20f);
                samples[i] = chime * 0.22f + breath * 0.5f;
            }
            return CutsceneArt.Clip("Arrival Reveal", samples);
        }
    }
}
