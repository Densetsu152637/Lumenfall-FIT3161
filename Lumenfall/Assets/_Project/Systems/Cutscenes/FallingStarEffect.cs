using System.Collections.Generic;
using UnityEngine;

namespace Lumenfall.Cutscenes
{
    /// <summary>
    /// The opening cutscene's falling star. It appears as a twinkle high in the top-left of the view, then dives along a
    /// curve onto the player, landing exactly <c>seconds</c> after launch with a burst of sparks. Everything is built at
    /// runtime: camera-facing glow quads, a trail, a point light that lights the trees on the way down, shed motes, and
    /// generated sounds. It has no colliders and touches no world state. It runs on unscaled time.
    /// </summary>
    public sealed class FallingStarEffect : MonoBehaviour
    {
        private const float MaxTwinkleSeconds = 1.2f;
        private const float TwinkleShare = 0.32f;
        private const float AfterglowSeconds = 1.1f;
        private static readonly Color Gold = new Color(1f, 0.84f, 0.48f);
        private static readonly Color Cream = new Color(1f, 0.97f, 0.88f);

        private sealed class Mote
        {
            public Transform transform;
            public Renderer renderer;
            public Vector3 velocity;
            public float born, life, size;
            public Color colour;
        }

        private readonly List<Mote> motes = new List<Mote>();
        private readonly List<AudioClip> clips = new List<AudioClip>();
        private readonly System.Random random = new System.Random(1729);
        private MaterialPropertyBlock block;
        private Transform target, core, halo;
        private Renderer coreRenderer, haloRenderer;
        private TrailRenderer trail;
        private Light glowLight;
        private AudioSource audioSource;
        private Vector3 twinklePoint, control, impact;
        private float seconds, twinkleSeconds, started, nextMote, haloScale;
        private bool landed;

        /// <summary>Starts a star that lands on <paramref name="target"/> after <paramref name="seconds"/>.</summary>
        public static FallingStarEffect Launch(Transform target, float seconds)
        {
            if (target == null || !CutsceneArt.Ensure()) return null;
            GameObject host = new GameObject("[Falling Star]");
            FallingStarEffect effect = host.AddComponent<FallingStarEffect>();
            effect.Begin(target, Mathf.Max(0.5f, seconds));
            return effect;
        }

        /// <summary>Removes the star, its sparks and its sound at once.</summary>
        public void Stop()
        {
            if (this != null) Destroy(gameObject);
        }

        private void Begin(Transform followed, float duration)
        {
            target = followed;
            seconds = duration;
            twinkleSeconds = Mathf.Min(MaxTwinkleSeconds, duration * TwinkleShare);
            started = Time.unscaledTime;
            block = new MaterialPropertyBlock();

            // Placed from the camera's view so the star always starts in the top-left of the screen. The camera looks down
            // on the forest, so the point is taken where that view ray is well above the player, over the canopy, rather
            // than at a fixed depth, where it would sit low among the trees and be hidden by them.
            Camera view = Camera.main;
            impact = followed.position + Vector3.up * 1.1f;
            if (view != null)
            {
                Ray ray = view.ViewportPointToRay(new Vector3(0.2f, 0.86f, 0f));
                float height = impact.y + 6f;
                float distance = ray.direction.y < -0.05f && ray.origin.y > height
                    ? (ray.origin.y - height) / -ray.direction.y : 18f;
                twinklePoint = ray.GetPoint(Mathf.Clamp(distance, 6f, 40f));
                control = Vector3.Lerp(twinklePoint, impact, 0.45f) + view.transform.right * 3f + Vector3.up * 2f;
            }
            else
            {
                twinklePoint = impact + new Vector3(-12f, 16f, 12f);
                control = Vector3.Lerp(twinklePoint, impact, 0.45f) + Vector3.up * 2f;
            }

            halo = CutsceneArt.Quad(transform, "Star Halo", CutsceneArt.GlowMaterial, out haloRenderer);
            core = CutsceneArt.Quad(transform, "Star Core", CutsceneArt.SparkleMaterial, out coreRenderer);
            Tint(haloRenderer, Gold, 0.7f);
            Tint(coreRenderer, Cream, 1f);

            GameObject trailObject = new GameObject("Star Trail");
            trailObject.transform.SetParent(transform, false);
            trail = trailObject.AddComponent<TrailRenderer>();
            trail.sharedMaterial = CutsceneArt.PlainMaterial;
            trail.time = 0.5f;
            trail.minVertexDistance = 0.05f;
            trail.numCapVertices = 4;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 0.55f), new Keyframe(1f, 0f));
            trail.colorGradient = new Gradient
            {
                colorKeys = new[] { new GradientColorKey(Cream, 0f), new GradientColorKey(Gold, 0.4f), new GradientColorKey(new Color(0.75f, 0.55f, 1f), 1f) },
                alphaKeys = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.6f, 0.4f), new GradientAlphaKey(0f, 1f) }
            };
            trail.emitting = false;

            GameObject lightObject = new GameObject("Star Light");
            lightObject.transform.SetParent(transform, false);
            glowLight = lightObject.AddComponent<Light>();
            glowLight.type = LightType.Point;
            glowLight.color = Gold;
            glowLight.shadows = LightShadows.None;
            glowLight.range = 6f;
            glowLight.intensity = 0.4f;

            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
            Lumenfall.Audio.AudioPlayback.PlayOneShot(audioSource, Keep(FallClip(duration - twinkleSeconds, twinkleSeconds)), 0.55f);

            Place(twinklePoint);
        }

        private void Update()
        {
            float t = Time.unscaledTime - started;
            if (target != null && !landed) impact = target.position + Vector3.up * 1.1f;

            if (t < twinkleSeconds)
            {
                // A distant twinkle: small, flickering and almost still.
                float flicker = 0.75f + 0.25f * Mathf.Sin(t * 17f) * Mathf.Sin(t * 5.3f);
                Place(twinklePoint + Vector3.up * Mathf.Sin(t * 2f) * 0.05f);
                core.localScale = Vector3.one * 0.5f * flicker;
                haloScale = 1.4f * flicker;
                glowLight.intensity = 0.4f;
            }
            else if (t < seconds)
            {
                float u = Mathf.Clamp01((t - twinkleSeconds) / Mathf.Max(0.01f, seconds - twinkleSeconds));
                // Accelerates the whole way down, as if it is being pulled in.
                float eased = Mathf.Pow(u, 2.2f);
                Vector3 position = Bezier(twinklePoint, control, impact, eased);
                Place(position);
                trail.emitting = true;
                core.localScale = Vector3.one * Mathf.Lerp(0.55f, 1.5f, eased);
                haloScale = Mathf.Lerp(1.8f, 5f, eased);
                glowLight.intensity = Mathf.Lerp(1f, 9f, eased);
                glowLight.range = Mathf.Lerp(6f, 16f, eased);
                if (Time.unscaledTime >= nextMote)
                {
                    nextMote = Time.unscaledTime + 0.025f;
                    Vector3 drift = new Vector3(Range(-0.4f, 0.4f), Range(-0.6f, 0.1f), Range(-0.4f, 0.4f));
                    SpawnMote(position, drift, Range(0.5f, 0.9f), Range(0.12f, 0.26f), Range(0f, 1f) < 0.6f ? Gold : Cream);
                }
            }
            else
            {
                if (!landed) Land();
                float after = Mathf.Clamp01((t - seconds) / AfterglowSeconds);
                haloScale = Mathf.Lerp(9f, 12f, after);
                Tint(haloRenderer, Cream, 0.9f * (1f - after));
                glowLight.intensity = Mathf.Lerp(14f, 0f, after);
                if (after >= 1f && motes.Count == 0) Destroy(gameObject);
            }

            TickMotes();
        }

        private void LateUpdate()
        {
            // Billboards face the camera after it has moved this frame.
            Camera view = Camera.main;
            if (view == null) return;
            Quaternion facing = view.transform.rotation;
            core.rotation = facing * Quaternion.Euler(0f, 0f, (Time.unscaledTime - started) * 40f);
            halo.rotation = facing;
            halo.localScale = Vector3.one * haloScale;
        }

        private void OnDestroy()
        {
            motes.Clear();
            foreach (AudioClip clip in clips)
                if (clip != null) Destroy(clip);
            clips.Clear();
        }

        private AudioClip Keep(AudioClip clip)
        {
            clips.Add(clip);
            return clip;
        }

        private void Land()
        {
            landed = true;
            Place(impact);
            trail.emitting = false;
            coreRenderer.enabled = false;
            Lumenfall.Audio.AudioPlayback.PlayOneShot(audioSource, Keep(ImpactClip()), 0.8f);
            for (int i = 0; i < 32; i++)
            {
                float angle = i / 32f * Mathf.PI * 2f + Range(-0.1f, 0.1f);
                Vector3 outward = new Vector3(Mathf.Cos(angle), Range(0.2f, 1.2f), Mathf.Sin(angle)) * Range(2.5f, 5.5f);
                SpawnMote(impact, outward, Range(0.7f, 1.4f), Range(0.15f, 0.32f), i % 3 == 0 ? Cream : Gold);
            }
        }

        private void Place(Vector3 position)
        {
            core.position = position;
            halo.position = position;
            trail.transform.position = position;
            glowLight.transform.position = position;
        }

        private void SpawnMote(Vector3 position, Vector3 velocity, float life, float size, Color colour)
        {
            Transform mote = CutsceneArt.Quad(transform, "Star Mote", CutsceneArt.GlowMaterial, out Renderer moteRenderer);
            mote.position = position;
            mote.localScale = Vector3.one * size;
            motes.Add(new Mote { transform = mote, renderer = moteRenderer, velocity = velocity, born = Time.unscaledTime, life = life, size = size, colour = colour });
        }

        private void TickMotes()
        {
            float now = Time.unscaledTime;
            float deltaTime = Time.unscaledDeltaTime;
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
                mote.velocity *= Mathf.Exp(-2.5f * deltaTime);
                mote.velocity += Vector3.down * 1.2f * deltaTime;
                mote.transform.position += mote.velocity * deltaTime;
                mote.transform.localScale = Vector3.one * mote.size * (1f - age * 0.6f);
                if (view != null) mote.transform.rotation = view.transform.rotation;
                Tint(mote.renderer, mote.colour, 1f - age * age);
            }
        }

        private void Tint(Renderer quadRenderer, Color colour, float alpha) => CutsceneArt.Tint(quadRenderer, block, colour, alpha);

        private float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);

        private static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float t)
        {
            float inverse = 1f - t;
            return inverse * inverse * a + 2f * inverse * t * b + t * t * c;
        }

        // A shimmering tone that rises and swells as the star dives, after a faint chime for the twinkle.
        private static AudioClip FallClip(float diveSeconds, float delaySeconds)
        {
            float total = Mathf.Max(0.5f, delaySeconds + diveSeconds);
            int count = Mathf.CeilToInt(total * CutsceneArt.SampleRate);
            float[] samples = new float[count];
            System.Random noise = new System.Random(7);
            float phase = 0f, smoothed = 0f;
            for (int i = 0; i < count; i++)
            {
                float time = i / (float)CutsceneArt.SampleRate;
                float chime = time < 1.2f ? Mathf.Sin(2f * Mathf.PI * 1568f * time) * Mathf.Exp(-time * 4f) * 0.12f : 0f;
                float u = Mathf.Clamp01((time - delaySeconds) / Mathf.Max(0.01f, diveSeconds));
                float frequency = 420f * Mathf.Pow(3.2f, u);
                phase += 2f * Mathf.PI * frequency / CutsceneArt.SampleRate;
                float tremolo = 0.7f + 0.3f * Mathf.Sin(time * 2f * Mathf.PI * 11f);
                float tone = Mathf.Sin(phase) * 0.6f + Mathf.Sin(phase * 1.5f) * 0.25f;
                smoothed = Mathf.Lerp(smoothed, (float)noise.NextDouble() * 2f - 1f, 0.08f + 0.3f * u);
                float swell = time < delaySeconds ? 0f : u * u;
                samples[i] = chime + (tone * tremolo * 0.22f + smoothed * 0.5f) * swell;
            }
            return CutsceneArt.Clip("Falling Star", samples);
        }

        // A soft thump, a hiss of sparks and a two-note chime.
        private static AudioClip ImpactClip()
        {
            int count = Mathf.CeilToInt(1.8f * CutsceneArt.SampleRate);
            float[] samples = new float[count];
            System.Random noise = new System.Random(11);
            float smoothed = 0f;
            for (int i = 0; i < count; i++)
            {
                float time = i / (float)CutsceneArt.SampleRate;
                float thump = Mathf.Sin(2f * Mathf.PI * (55f + 40f * Mathf.Exp(-time * 20f)) * time) * Mathf.Exp(-time * 5f);
                smoothed = Mathf.Lerp(smoothed, (float)noise.NextDouble() * 2f - 1f, 0.35f);
                float hiss = smoothed * Mathf.Exp(-time * 9f);
                float chime = (Mathf.Sin(2f * Mathf.PI * 1046.5f * time) + 0.6f * Mathf.Sin(2f * Mathf.PI * 1568f * time)) * Mathf.Exp(-time * 2.2f);
                samples[i] = thump * 0.7f + hiss * 0.45f + chime * 0.12f * Mathf.Clamp01(time * 30f);
            }
            return CutsceneArt.Clip("Falling Star Impact", samples);
        }
    }
}
