using UnityEngine;
using UnityEngine.Rendering;
using Lumenfall.GridMovement;

namespace Lumenfall.Quests
{
    /// <summary>
    /// Moment-to-moment effects for Cinna's ice trial:
    /// <list type="bullet">
    /// <item>the Glaciate impact when the pool freezes or thaws: a flash of light at the relic, a frost ring along the
    /// floor, a burst of frost and, for a freeze, a short camera shake;</item>
    /// <item>frost spray from the player's feet while they slide, and a puff where the slide stops;</item>
    /// <item>the player's breath misting every few seconds while the room is frozen.</item>
    /// </list>
    /// The shake offsets the camera only while it renders, so the camera's own smoothing never sees it.
    /// </summary>
    public sealed class CinnaIceTrialEffects : MonoBehaviour
    {
        private const float FlashDuration = 0.45f;
        private const float FlashIntensity = 9f;
        private const float RingDuration = 0.9f;
        private const float RingStartRadius = 0.3f;
        private const float RingEndRadius = 9f;
        private const float RingWidth = 0.18f;
        private const int RingSegments = 64;
        private const float ShakeDuration = 0.35f;
        private const float ShakeStrength = 0.09f;
        private const float SprayPerSecond = 70f;
        private const float BreathInterval = 2.6f;
        private const float BreathHeight = 0.62f;
        private static readonly Color FreezeColor = new Color(0.72f, 0.92f, 1f);
        private static readonly Color ThawColor = new Color(1f, 0.82f, 0.6f);

        private Light flash;
        private LineRenderer ring;
        private Material ringMaterial;
        private ParticleSystem frost;
        private ParticleSystem mist;
        private GridMover player;
        private CinnaIceTrialAmbience ambience;
        private Vector3 origin;
        private Color impactColor;
        private float flashTime = -1f;
        private float ringTime = -1f;
        private float shakeTime = -1f;
        private float sprayDebt;
        private float breathTimer;
        private Camera shakenCamera;
        private Vector3 shakeOffset;

        /// <summary>Builds the effect objects and listens to the ambience for freezes and thaws.</summary>
        public void Configure(CinnaIceTrialAmbience source, Vector3 relicPosition, Material sparkle, Material mistMaterial)
        {
            ambience = source;
            origin = relicPosition;
            if (ambience != null) ambience.FrontStarted += PlayImpact;

            GameObject flashHost = new GameObject("Glaciate Flash");
            flashHost.transform.SetParent(transform, false);
            flashHost.transform.position = relicPosition + Vector3.up * 1f;
            flash = flashHost.AddComponent<Light>();
            flash.type = LightType.Point;
            flash.range = 7f;
            flash.shadows = LightShadows.None;
            flash.enabled = false;

            GameObject ringHost = new GameObject("Glaciate Ring");
            ringHost.transform.SetParent(transform, false);
            ringHost.transform.SetPositionAndRotation(relicPosition + Vector3.up * 0.06f, Quaternion.Euler(90f, 0f, 0f));
            ring = ringHost.AddComponent<LineRenderer>();
            Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
            if (shader != null) ring.sharedMaterial = ringMaterial = new Material(shader) { name = "Glaciate Ring" };
            ring.useWorldSpace = false;
            ring.loop = true;
            ring.alignment = LineAlignment.TransformZ;
            ring.positionCount = RingSegments;
            ring.shadowCastingMode = ShadowCastingMode.Off;
            ring.receiveShadows = false;
            ring.enabled = false;

            frost = BuildParticles("Ice Trial Frost", sparkle, 0.04f, 0.1f, 0.4f, 0.9f, 0.4f);
            mist = BuildParticles("Ice Trial Mist", mistMaterial, 0.18f, 0.35f, 0.9f, 1.6f, -0.05f);
        }

        /// <summary>Follows <paramref name="mover"/> for slide spray and breath.</summary>
        public void TrackPlayer(GridMover mover)
        {
            if (player == mover) return;
            if (player != null) player.SlideEnded -= OnSlideEnded;
            player = mover;
            if (player != null) player.SlideEnded += OnSlideEnded;
        }

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += ApplyShake;
            RenderPipelineManager.endCameraRendering += RemoveShake;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= ApplyShake;
            RenderPipelineManager.endCameraRendering -= RemoveShake;
            RemoveShake(default, shakenCamera);
            flashTime = ringTime = shakeTime = -1f;
            if (flash != null) flash.enabled = false;
            if (ring != null) ring.enabled = false;
        }

        private void OnDestroy()
        {
            if (ambience != null) ambience.FrontStarted -= PlayImpact;
            if (player != null) player.SlideEnded -= OnSlideEnded;
            if (ringMaterial != null) Destroy(ringMaterial);
        }

        private void PlayImpact(bool freezing)
        {
            impactColor = freezing ? FreezeColor : ThawColor;
            flashTime = 0f;
            ringTime = 0f;
            if (freezing) shakeTime = 0f;
            Burst(frost, origin + Vector3.up * 0.8f, 40, impactColor);
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            AdvanceFlash(deltaTime);
            AdvanceRing(deltaTime);
            if (shakeTime >= 0f) { shakeTime += deltaTime; if (shakeTime >= ShakeDuration) shakeTime = -1f; }
            if (player == null) return;
            Vector3 feet = player.transform.position + Vector3.up * 0.05f;
            if (player.IsSliding)
            {
                sprayDebt += SprayPerSecond * deltaTime;
                int count = Mathf.FloorToInt(sprayDebt);
                sprayDebt -= count;
                if (count > 0) Burst(frost, feet, count, Color.white);
            }
            else sprayDebt = 0f;
            // Breath only mists in the cold.
            if (ambience == null || !ambience.Frozen) { breathTimer = 0f; return; }
            breathTimer += deltaTime;
            if (breathTimer < BreathInterval) return;
            breathTimer = 0f;
            Vector3 mouth = player.transform.position + Vector3.up * BreathHeight + player.transform.forward * 0.22f;
            Burst(mist, mouth, 3, new Color(1f, 1f, 1f, 0.5f));
        }

        /// <summary>A spray of frost and mist where the player goes over an edge into the chasm.</summary>
        public void PlayFall(Vector3 position)
        {
            Burst(frost, position + Vector3.up * 0.1f, 30, Color.white);
            Burst(mist, position, 4, new Color(1f, 1f, 1f, 0.5f));
        }

        private void OnSlideEnded(GridMover mover, Vector2Int cell)
        {
            Burst(frost, mover.transform.position + Vector3.up * 0.05f, 16, Color.white);
            Burst(mist, mover.transform.position + Vector3.up * 0.1f, 2, new Color(1f, 1f, 1f, 0.45f));
        }

        private void AdvanceFlash(float deltaTime)
        {
            if (flash == null || flashTime < 0f) return;
            flashTime += deltaTime;
            float u = Mathf.Clamp01(flashTime / FlashDuration);
            flash.enabled = u < 1f;
            flash.color = impactColor;
            flash.intensity = FlashIntensity * (1f - u) * (1f - u);
            if (u >= 1f) flashTime = -1f;
        }

        private void AdvanceRing(float deltaTime)
        {
            if (ring == null || ringTime < 0f) return;
            ringTime += deltaTime;
            float u = Mathf.Clamp01(ringTime / RingDuration);
            ring.enabled = u < 1f;
            float eased = 1f - (1f - u) * (1f - u) * (1f - u);
            float radius = Mathf.Lerp(RingStartRadius, RingEndRadius, eased);
            for (int i = 0; i < RingSegments; i++)
            {
                float angle = i * Mathf.PI * 2f / RingSegments;
                ring.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f));
            }
            ring.widthMultiplier = RingWidth * (1f - u);
            Color color = impactColor;
            color.a = 0.85f * (1f - u);
            ring.startColor = ring.endColor = color;
            if (u >= 1f) ringTime = -1f;
        }

        private void ApplyShake(ScriptableRenderContext context, Camera camera)
        {
            if (shakeTime < 0f || camera == null || camera.cameraType != CameraType.Game) return;
            float strength = ShakeStrength * (1f - shakeTime / ShakeDuration);
            shakeOffset = new Vector3(Mathf.PerlinNoise(shakeTime * 40f, 0.3f) - 0.5f, Mathf.PerlinNoise(0.7f, shakeTime * 40f) - 0.5f, 0f) * (2f * strength);
            shakeOffset = camera.transform.rotation * shakeOffset;
            camera.transform.position += shakeOffset;
            shakenCamera = camera;
        }

        private void RemoveShake(ScriptableRenderContext context, Camera camera)
        {
            if (camera == null || camera != shakenCamera) return;
            camera.transform.position -= shakeOffset;
            shakeOffset = Vector3.zero;
            shakenCamera = null;
        }

        private static void Burst(ParticleSystem particles, Vector3 position, int count, Color color)
        {
            if (particles == null || count <= 0) return;
            ParticleSystem.EmitParams emit = new ParticleSystem.EmitParams { position = position, applyShapeToPosition = true, startColor = color };
            particles.Emit(emit, count);
        }

        private ParticleSystem BuildParticles(string name, Material material, float minSize, float maxSize, float minLife, float maxLife, float gravity)
        {
            if (material == null) return null;
            GameObject host = new GameObject(name);
            host.transform.SetParent(transform, false);
            ParticleSystem particles = host.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = particles.main;
            main.playOnAwake = false;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(minLife, maxLife);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 400;
            ParticleSystem.EmissionModule emission = particles.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.15f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            ParticleSystem.ColorOverLifetimeModule colour = particles.colorOverLifetime;
            colour.enabled = true;
            Gradient fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            colour.color = fade;
            ParticleSystemRenderer renderer = host.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return particles;
        }
    }
}
