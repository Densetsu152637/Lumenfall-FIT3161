using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Lumenfall.GridMovement;

namespace Lumenfall.Quests
{
    /// <summary>
    /// Blends Cinna's ice trial between a thawed stone hall and a frozen ice cavern. The room is built thawed: stone floor
    /// and walls, rippling water in a sunken basin and lit braziers. A live Glaciate on the relic sends a frost front out
    /// from the pedestal. As it passes:
    /// <list type="bullet">
    /// <item>ice spreads across the pool and a frost dusting over the floor, both revealed by the Ice Trial Frost shader
    /// with a ragged, glowing edge;</item>
    /// <item>ice climbs the walls and crystal spikes and icicles grow along them;</item>
    /// <item>the crystal banks and snowy rocks grow out of the ground;</item>
    /// <item>the brazier flames go out, frost crowns grow in their place, and warm light gives way to cold light;</item>
    /// <item>snowfall, pool mist, ice glints and floating motes start;</item>
    /// <item>a room-wide cold colour grade fades in;</item>
    /// <item>the ice reflection probe re-renders once the front has finished.</item>
    /// </list>
    /// A thaw sends the same front out again in reverse. Map loads and save restores snap to the end state.
    /// </summary>
    /// <remarks>
    /// <see cref="CinnaIceTrialRoom"/> registers the pieces it builds at runtime; the baked dressing is found by the names in
    /// <see cref="CinnaIceTrialArt"/>. Everything it touches belongs to the area, except the quality setting for real-time
    /// reflection probes, which it turns on while the room is active and restores when it is not.
    /// </remarks>
    public sealed class CinnaIceTrialAmbience : MonoBehaviour
    {
        /// <summary>How fast the frost front travels, in cells per second.</summary>
        public const float FrontSpeed = 9f;
        /// <summary>Distance behind the front over which one piece grows in, in cells.</summary>
        private const float GrowWidth = 3f;
        private const float FlameThreshold = 0.35f;
        private const float WaterDrift = 0.035f;
        private static readonly int RevealCenterId = Shader.PropertyToID("_RevealCenter");
        private static readonly int RevealRadiusId = Shader.PropertyToID("_RevealRadius");
        private static readonly int RevealDirectionId = Shader.PropertyToID("_RevealDirection");
        private static readonly int BaseMapStId = Shader.PropertyToID("_BaseMap_ST");

        private enum Kind { Pop, Rise, Light }

        private sealed class Piece
        {
            public Kind kind;
            public Transform transform;
            public Vector3 fullScale;
            public Vector3 basePosition;
            public Light light;
            public float intensity;
            public bool cold;
            public float distance;
        }

        private sealed class Brazier
        {
            public ParticleSystem flame;
            public float distance;
        }

        private readonly List<Piece> pieces = new List<Piece>();
        private readonly List<Brazier> braziers = new List<Brazier>();
        private readonly List<Renderer> surfaces = new List<Renderer>();
        private readonly List<ParticleSystem> weather = new List<ParticleSystem>();
        private readonly List<Renderer> water = new List<Renderer>();
        private readonly List<Vector4> waterTiling = new List<Vector4>();
        private MaterialPropertyBlock block;
        private GridWorld world;
        private Vector3 origin;
        private Volume grade;
        private ReflectionProbe probe;
        private bool realtimeProbesBefore;
        private bool realtimeProbesChanged;
        private float farthest;
        private float front;
        private float drift;
        private bool frozen;
        private bool started;
        private bool animating;

        /// <summary>Raised when a live freeze (true) or thaw (false) starts spreading from the relic.</summary>
        public event Action<bool> FrontStarted;

        /// <summary>Sets the grid and the cell the frost spreads from (the relic's pedestal).</summary>
        public void Configure(GridWorld grid, Vector2Int originCell)
        {
            world = grid;
            origin = grid.CellToWorld(originCell);
        }

        /// <summary>World position the frost spreads from.</summary>
        public Vector3 Origin => origin;

        /// <summary>True while the room shows its frozen look, or is animating toward it.</summary>
        public bool Frozen => frozen;

        /// <summary>Registers a surface drawn with the Ice Trial Frost shader; its reveal follows the front.</summary>
        public void AddRevealSurface(Renderer surface) => surfaces.Add(surface);

        /// <summary>Registers open water whose normal map drifts slowly while it is not frozen over.</summary>
        public void AddWater(Renderer surface, Vector2 tiling)
        {
            water.Add(surface);
            waterTiling.Add(new Vector4(tiling.x, tiling.y, 0f, 0f));
        }

        /// <summary>Registers a piece that grows from nothing as the front passes <paramref name="position"/>.</summary>
        public void AddGrowth(Transform piece, Vector3 position) => AddPiece(Kind.Pop, piece, position);

        /// <summary>Registers an ice shell around a wall that rises from its base as the front passes its nearest point.</summary>
        public void AddWallShell(Transform shell, Vector3 nearestPoint)
        {
            Piece piece = AddPiece(Kind.Rise, shell, nearestPoint);
            piece.basePosition = shell.position;
        }

        /// <summary>Finds the baked dressing under <paramref name="dressing"/>.</summary>
        public void BindDressing(Transform dressing)
        {
            if (dressing == null) return;
            foreach (string group in new[] { CinnaIceTrialArt.CrystalGroupName, CinnaIceTrialArt.RockGroupName })
            {
                Transform parent = dressing.Find(group);
                if (parent == null) continue;
                foreach (Transform child in parent) AddPiece(Kind.Pop, child, child.position);
            }
            Transform brazierGroup = dressing.Find(CinnaIceTrialArt.BrazierGroupName);
            if (brazierGroup != null)
                foreach (Transform brazier in brazierGroup)
                {
                    float distance = Distance(brazier.position);
                    Transform flame = brazier.Find(CinnaIceTrialArt.FlameName);
                    if (flame != null && flame.TryGetComponent(out ParticleSystem particles))
                        braziers.Add(new Brazier { flame = particles, distance = distance });
                    Transform crown = brazier.Find(CinnaIceTrialArt.FrostCrownName);
                    if (crown != null) AddPiece(Kind.Pop, crown, brazier.position);
                    foreach (Light light in brazier.GetComponentsInChildren<Light>(true)) AddLight(light, false, brazier.position);
                }
            Transform coldLights = dressing.Find(CinnaIceTrialArt.ColdLightGroupName);
            if (coldLights != null)
                foreach (Light light in coldLights.GetComponentsInChildren<Light>(true)) AddLight(light, true, light.transform.position);
            foreach (string name in new[] { CinnaIceTrialArt.SnowfallName, CinnaIceTrialArt.MistName, CinnaIceTrialArt.GlintsName, CinnaIceTrialArt.MotesName })
            {
                ParticleSystem particles = FindParticles(dressing, name);
                if (particles != null) weather.Add(particles);
            }
            Transform gradeHost = dressing.Find(CinnaIceTrialArt.GradeName);
            if (gradeHost != null && gradeHost.TryGetComponent(out grade)) grade.weight = 0f;
            Transform probeHost = dressing.Find(CinnaIceTrialArt.ReflectionProbeName);
            if (probeHost != null) probeHost.TryGetComponent(out probe);
            EnableRealtimeProbes();
        }

        /// <summary>
        /// Moves the room toward the frozen or thawed look. With <paramref name="animate"/> the front spreads from the relic;
        /// without it the end state is applied at once.
        /// </summary>
        public void SetFrozen(bool value, bool animate)
        {
            // Already there, or already heading there.
            if (started && value == frozen) return;
            // The first state the room learns is how it was saved, so it is never animated.
            animate &= started;
            frozen = value;
            started = true;
            if (animate)
            {
                front = 0f;
                animating = true;
                if (frozen) StartWeather(false);
                else StopWeather(false);
                FrontStarted?.Invoke(frozen);
            }
            else
            {
                animating = false;
                front = float.MaxValue;
                if (frozen) StartWeather(true);
                else StopWeather(true);
            }
            Apply();
            if (!animating) RenderProbe();
        }

        // Particle systems stop while the area is inactive, so returning to the room finishes any front and restarts the
        // weather and flames for the current state.
        private void OnEnable()
        {
            if (!started) return;
            EnableRealtimeProbes();
            animating = false;
            front = float.MaxValue;
            if (frozen) StartWeather(true);
            Apply();
            RenderProbe();
        }

        private void OnDisable()
        {
            if (!realtimeProbesChanged) return;
            QualitySettings.realtimeReflectionProbes = realtimeProbesBefore;
            realtimeProbesChanged = false;
        }

        private void Update()
        {
            DriftWater(Time.deltaTime);
            if (!animating) return;
            front += FrontSpeed * Time.deltaTime;
            if (front >= farthest + GrowWidth)
            {
                front = float.MaxValue;
                animating = false;
                Apply();
                RenderProbe();
                return;
            }
            Apply();
        }

        private void Apply()
        {
            foreach (Piece piece in pieces) ApplyPiece(piece, Progress(piece.distance));
            foreach (Brazier brazier in braziers)
            {
                if (brazier.flame == null) continue;
                bool lit = Progress(brazier.distance) < FlameThreshold;
                if (lit && !brazier.flame.isPlaying) brazier.flame.Play(true);
                else if (!lit && brazier.flame.isPlaying) brazier.flame.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
            ApplySurfaces();
            if (grade != null)
            {
                float overall = front >= float.MaxValue ? 1f : Mathf.Clamp01(front / Mathf.Max(1f, farthest));
                grade.weight = frozen ? overall : 1f - overall;
            }
        }

        private void ApplySurfaces()
        {
            if (surfaces.Count == 0) return;
            if (block == null) block = new MaterialPropertyBlock();
            bool visible = frozen || animating;
            float radius = front >= float.MaxValue ? 10000f : front * (world == null ? 1f : world.CellSize);
            foreach (Renderer surface in surfaces)
            {
                if (surface == null) continue;
                if (surface.enabled != visible) surface.enabled = visible;
                if (!visible) continue;
                surface.GetPropertyBlock(block);
                block.SetVector(RevealCenterId, origin);
                block.SetFloat(RevealRadiusId, radius);
                block.SetFloat(RevealDirectionId, frozen ? 1f : -1f);
                surface.SetPropertyBlock(block);
            }
        }

        // The water's normal map drifts diagonally so the open pool reads as moving water rather than a flat slab.
        private void DriftWater(float deltaTime)
        {
            if (water.Count == 0 || (frozen && !animating)) return;
            if (block == null) block = new MaterialPropertyBlock();
            drift = (drift + WaterDrift * deltaTime) % 1f;
            for (int i = 0; i < water.Count; i++)
            {
                if (water[i] == null) continue;
                water[i].GetPropertyBlock(block);
                Vector4 tiling = waterTiling[i];
                block.SetVector(BaseMapStId, new Vector4(tiling.x, tiling.y, drift, drift * 0.6f));
                water[i].SetPropertyBlock(block);
            }
        }

        // How frozen a piece at this distance is: 0 thawed, 1 frozen. The front sweeps outward in both directions, so a
        // thaw also starts at the relic.
        private float Progress(float distance)
        {
            float passed = front >= float.MaxValue ? 1f : Mathf.Clamp01((front - distance) / GrowWidth);
            return frozen ? passed : 1f - passed;
        }

        private static void ApplyPiece(Piece piece, float progress)
        {
            if (piece.kind == Kind.Light)
            {
                if (piece.light == null) return;
                piece.light.intensity = piece.intensity * (piece.cold ? progress : 1f - progress);
                piece.light.enabled = piece.light.intensity > 0.001f;
                return;
            }
            if (piece.transform == null) return;
            bool visible = progress > 0.001f;
            if (piece.transform.gameObject.activeSelf != visible) piece.transform.gameObject.SetActive(visible);
            if (!visible) return;
            if (piece.kind == Kind.Pop)
            {
                piece.transform.localScale = piece.fullScale * EaseOutBack(progress);
                return;
            }
            // A wall shell grows upward from its base: its height scales and its centre rises with it.
            float rise = EaseOutCubic(progress);
            Vector3 scale = piece.fullScale;
            scale.y *= rise;
            piece.transform.localScale = scale;
            piece.transform.position = piece.basePosition + Vector3.down * (piece.fullScale.y * (1f - rise) * 0.5f);
        }

        private void StartWeather(bool instant)
        {
            foreach (ParticleSystem particles in weather)
            {
                if (particles == null || particles.isEmitting) continue;
                // Prewarm only on a snap, so a live freeze starts with the first flakes rather than a full flurry. A system
                // still fading out after a thaw just resumes emitting.
                if (!particles.isPlaying)
                {
                    ParticleSystem.MainModule main = particles.main;
                    main.prewarm = instant;
                }
                particles.Play(true);
            }
        }

        private void StopWeather(bool instant)
        {
            foreach (ParticleSystem particles in weather)
                if (particles != null)
                    particles.Stop(true, instant ? ParticleSystemStopBehavior.StopEmittingAndClear : ParticleSystemStopBehavior.StopEmitting);
        }

        // Real-time probes are off in the project's quality levels; the room's probe renders only on request, so turning
        // the setting on while the room is active costs nothing elsewhere.
        private void EnableRealtimeProbes()
        {
            if (probe == null || realtimeProbesChanged) return;
            realtimeProbesBefore = QualitySettings.realtimeReflectionProbes;
            QualitySettings.realtimeReflectionProbes = true;
            realtimeProbesChanged = true;
        }

        private void RenderProbe()
        {
            if (probe != null && probe.isActiveAndEnabled) probe.RenderProbe();
        }

        private Piece AddPiece(Kind kind, Transform target, Vector3 position)
        {
            Piece piece = new Piece { kind = kind, transform = target, fullScale = target.localScale, distance = Distance(position) };
            pieces.Add(piece);
            farthest = Mathf.Max(farthest, piece.distance);
            return piece;
        }

        private void AddLight(Light light, bool cold, Vector3 position)
        {
            pieces.Add(new Piece { kind = Kind.Light, light = light, intensity = light.intensity, cold = cold, distance = Distance(position) });
            farthest = Mathf.Max(farthest, Distance(position));
        }

        private float Distance(Vector3 position)
        {
            Vector3 offset = position - origin;
            offset.y = 0f;
            return offset.magnitude / (world == null ? 1f : world.CellSize);
        }

        private static ParticleSystem FindParticles(Transform root, string name)
        {
            Transform child = root.Find(name);
            if (child == null || !child.TryGetComponent(out ParticleSystem particles)) return null;
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return particles;
        }

        private static float EaseOutCubic(float t) { float inverse = 1f - t; return 1f - inverse * inverse * inverse; }
        private static float EaseOutBack(float t) { const float c1 = 1.70158f, c3 = c1 + 1f; float s = t - 1f; return 1f + c3 * s * s * s + c1 * s * s; }
    }
}
