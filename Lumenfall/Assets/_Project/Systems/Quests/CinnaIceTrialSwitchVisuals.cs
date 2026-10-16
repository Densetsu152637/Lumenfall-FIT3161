using System.Collections.Generic;
using Lumenfall.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using Lumenfall.GridMovement;

namespace Lumenfall.Quests
{
    /// <summary>
    /// Draws the ice trial's switches as rune plates. Each is a stone disc with a cut-out rune ring that sits dark and frosted
    /// while off and glows cyan while lit. A lit switch sends a beam of light to the seal crystal outside the east wall,
    /// which brightens a step for every lit switch, along with a light beside it. When the seal opens live, the finale plays:
    /// the crystal flares and throws a burst of gold light and sparks, the beams pulse, and gold runs back from the seal
    /// across every switch and beam. A live toggle bounces the plate's rune and throws a small burst of frost; save restores and resets snap without
    /// effects. <see cref="CinnaIceTrialRoom"/> owns the rules and calls <see cref="SetLit"/> and <see cref="SetSeal"/>.
    /// </summary>
    public sealed class CinnaIceTrialSwitchVisuals : MonoBehaviour
    {
        private const float GlowDuration = 0.25f;
        private const float BeamDuration = 0.35f;
        private const float BounceDuration = 0.3f;
        private const float BounceScale = 0.2f;
        private const float BeamWidth = 0.035f;
        private const float BeamStartHeight = 0.12f;
        private const int BurstCount = 18;
        private static readonly Color RuneOffColor = new Color(0.32f, 0.38f, 0.48f);
        private static readonly Color RuneOnColor = new Color(0.45f, 0.95f, 1f);
        private static readonly Color SealOffColor = new Color(0.36f, 0.4f, 0.55f);
        private static readonly Color SealOpenColor = new Color(1f, 0.84f, 0.38f);
        private static readonly Color CrystalDimColor = new Color(0.36f, 0.5f, 0.68f);
        private const float RuneOnGlow = 2.2f;
        private const float SealOpenGlow = 2.6f;
        private const float FinaleDuration = 3f;
        private const float GoldWaveSpeed = 7f;
        private const int FinaleBurstCount = 70;

        private sealed class Plate
        {
            public Transform rune;
            public Renderer runeRenderer;
            public LineRenderer beam;
            public Vector3 beamStart;
            public float runeSize;
            public bool final;
            public bool lit;
            public float glow;
            public float beamLength;
            public float bounce = -1f;
        }

        private readonly Dictionary<string, Plate> plates = new Dictionary<string, Plate>();
        private readonly List<Renderer> crystalRenderers = new List<Renderer>();
        private MaterialPropertyBlock block;
        private Material beamMaterial;
        private ParticleSystem burst;
        private Vector3 beamTarget;
        private float sealProgress;
        private float sealShown;
        private bool sealOpen;
        private float time;
        private float finaleTime = -1f;
        private Light sealLight;

        /// <summary>Builds a rune plate for <paramref name="id"/> under <paramref name="root"/>, which sits on its cell.</summary>
        public void AddSwitch(string id, Transform root, bool final, CinnaIceTrialArt art, float cellSize)
        {
            if (block == null) Initialise(art);
            float diameter = (final ? 0.92f : 0.8f) * cellSize;
            // Tall enough to sit on the sunken ice and still stand a little proud of the floor.
            GameObject disc = Part(PrimitiveType.Cylinder, id + " plate", root, new Vector3(diameter, 0.065f, diameter), new Vector3(0f, 0.005f, 0f));
            disc.GetComponent<Renderer>().sharedMaterial = art.switchBaseMaterial;
            float runeSize = diameter * 0.92f;
            GameObject rune = Part(PrimitiveType.Quad, id + " rune", root, new Vector3(runeSize, runeSize, 1f), new Vector3(0f, 0.075f, 0f));
            rune.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Renderer runeRenderer = rune.GetComponent<Renderer>();
            runeRenderer.sharedMaterial = art.runeMaterial;
            runeRenderer.shadowCastingMode = ShadowCastingMode.Off;
            Plate plate = new Plate { rune = rune.transform, runeRenderer = runeRenderer, runeSize = runeSize, final = final,
                beamStart = root.position + Vector3.up * BeamStartHeight };
            if (!final) plate.beam = BuildBeam(id, root);
            // Until a seal crystal is set, the beams meet above the seal plate.
            else if (crystalRenderers.Count == 0) beamTarget = root.position + Vector3.up * 1.3f;
            plates[id] = plate;
            ApplyPlate(plate);
        }

        /// <summary>Sets the crystal the beams meet at, and tints it with the seal's progress from now on.</summary>
        public void SetSealCrystal(GameObject crystal, float height)
        {
            crystalRenderers.Clear();
            crystalRenderers.AddRange(crystal.GetComponentsInChildren<Renderer>(true));
            beamTarget = crystal.transform.position + Vector3.up * height;
            if (sealLight == null)
            {
                GameObject host = new GameObject("Seal Crystal Light");
                host.transform.SetParent(transform, false);
                sealLight = host.AddComponent<Light>();
                sealLight.type = LightType.Point;
                sealLight.range = 5f;
                sealLight.shadows = LightShadows.None;
            }
            sealLight.transform.position = beamTarget + Vector3.down * 0.3f;
            ApplyCrystal();
        }

        /// <summary>Lights or dims a switch. A live change bounces the rune, bursts frost and grows or retracts its beam.</summary>
        public void SetLit(string id, bool lit, bool animate)
        {
            if (!plates.TryGetValue(id, out Plate plate)) return;
            bool changed = plate.lit != lit;
            plate.lit = lit;
            if (!animate)
            {
                plate.glow = plate.beamLength = lit ? 1f : 0f;
                plate.bounce = -1f;
            }
            else if (changed)
            {
                plate.bounce = 0f;
                if (burst != null)
                {
                    burst.transform.position = plate.rune.position;
                    ParticleSystem.MainModule main = burst.main;
                    main.startColor = lit ? (plate.final ? SealOpenColor : RuneOnColor) : Color.white;
                    burst.Emit(BurstCount);
                }
            }
            ApplyPlate(plate);
        }

        /// <summary>Sets the seal's progress (lit switches over total) and whether it is open.</summary>
        public void SetSeal(int lit, int total, bool open, bool animate)
        {
            sealProgress = total <= 0 ? 0f : (float)lit / total;
            if (open && !sealOpen && animate)
            {
                finaleTime = 0f;
                if (burst != null && beamTarget != Vector3.zero)
                {
                    burst.transform.position = beamTarget;
                    ParticleSystem.MainModule main = burst.main;
                    main.startColor = SealOpenColor;
                    burst.Emit(FinaleBurstCount);
                }
            }
            if (!open || !animate) finaleTime = -1f;
            sealOpen = open;
            if (!animate) sealShown = sealProgress;
            foreach (Plate plate in plates.Values) if (plate.beam != null) ApplyBeam(plate);
            ApplyCrystal();
        }

        private void Update()
        {
            time += Time.deltaTime;
            float step = Time.deltaTime;
            foreach (Plate plate in plates.Values)
            {
                float target = plate.lit ? 1f : 0f;
                plate.glow = Mathf.MoveTowards(plate.glow, target, step / GlowDuration);
                plate.beamLength = Mathf.MoveTowards(plate.beamLength, target, step / BeamDuration);
                if (plate.bounce >= 0f)
                {
                    plate.bounce += step;
                    if (plate.bounce >= BounceDuration) plate.bounce = -1f;
                }
                ApplyPlate(plate);
            }
            sealShown = Mathf.MoveTowards(sealShown, sealProgress, step / BeamDuration);
            if (finaleTime >= 0f) { finaleTime += step; if (finaleTime >= FinaleDuration) finaleTime = -1f; }
            ApplyCrystal();
        }

        private void ApplyPlate(Plate plate)
        {
            Color on = plate.final || Golden(plate) ? SealOpenColor : RuneOnColor;
            Color off = plate.final ? SealOffColor : RuneOffColor;
            Color color = Color.Lerp(off, on, plate.glow);
            float glow = plate.glow * (plate.final ? SealOpenGlow : RuneOnGlow);
            Tint(plate.runeRenderer, color, color * glow);
            float bounce = plate.bounce >= 0f ? Mathf.Sin(Mathf.PI * plate.bounce / BounceDuration) * BounceScale : 0f;
            float size = plate.runeSize * (1f + bounce);
            plate.rune.localScale = new Vector3(size, size, 1f);
            if (plate.beam != null) ApplyBeam(plate);
        }

        private void ApplyBeam(Plate plate)
        {
            bool visible = plate.beamLength > 0.001f && beamTarget != Vector3.zero;
            if (plate.beam.enabled != visible) plate.beam.enabled = visible;
            if (!visible) return;
            float length = EaseOutCubic(plate.beamLength);
            plate.beam.SetPosition(0, plate.beamStart);
            plate.beam.SetPosition(1, Vector3.Lerp(plate.beamStart, beamTarget, length));
            float pulse = finaleTime >= 0f ? 1f + 2.5f * Mathf.Exp(-finaleTime * 3f) : 1f;
            plate.beam.widthMultiplier = BeamWidth * pulse * (0.85f + 0.15f * Mathf.Sin(time * 6f + plate.beamStart.x * 1.7f + plate.beamStart.z));
            Color color = Golden(plate) ? SealOpenColor : RuneOnColor;
            color.a = 0.6f * length;
            plate.beam.startColor = color;
            color.a = 0.9f * length;
            plate.beam.endColor = color;
        }

        private void ApplyCrystal()
        {
            Color color = sealOpen ? SealOpenColor : Color.Lerp(CrystalDimColor, RuneOnColor, sealShown);
            float flare = finaleTime >= 0f ? 1f + 2.5f * Mathf.Exp(-finaleTime * 2.5f) : 1f;
            float glow = (sealOpen ? SealOpenGlow : 0.35f + 1.6f * sealShown) * flare;
            foreach (Renderer renderer in crystalRenderers) Tint(renderer, color, color * glow);
            if (sealLight == null) return;
            sealLight.color = color;
            sealLight.intensity = (sealOpen ? 1.8f : 0.2f + 1.2f * sealShown) * flare;
        }

        // After the seal opens, gold spreads back from the seal across the switches; it is already everywhere once the
        // finale has finished or when the open seal was restored.
        private bool Golden(Plate plate)
        {
            if (!sealOpen) return false;
            if (finaleTime < 0f) return true;
            return finaleTime * GoldWaveSpeed >= Vector3.Distance(plate.beamStart, beamTarget);
        }

        private void Tint(Renderer renderer, Color color, Color emission)
        {
            if (renderer == null) return;
            renderer.GetPropertyBlock(block);
            block.SetColor("_BaseColor", color);
            block.SetColor("_EmissionColor", emission);
            renderer.SetPropertyBlock(block);
        }

        private void Initialise(CinnaIceTrialArt art)
        {
            block = new MaterialPropertyBlock();
            Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
            if (shader != null) beamMaterial = new Material(shader) { name = "Ice Trial Beam" };
            if (art.sparkleMaterial == null) return;
            GameObject host = new GameObject("Switch Frost Burst");
            host.transform.SetParent(transform, false);
            burst = host.AddComponent<ParticleSystem>();
            burst.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = burst.main;
            main.playOnAwake = false;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
            main.gravityModifier = 0.6f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 200;
            ParticleSystem.EmissionModule emission = burst.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = burst.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.25f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            ParticleSystemRenderer renderer = host.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = art.sparkleMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        private LineRenderer BuildBeam(string id, Transform root)
        {
            GameObject host = new GameObject(id + " beam");
            host.transform.SetParent(root, false);
            LineRenderer beam = host.AddComponent<LineRenderer>();
            beam.sharedMaterial = beamMaterial;
            beam.useWorldSpace = true;
            beam.positionCount = 2;
            beam.numCapVertices = 2;
            beam.shadowCastingMode = ShadowCastingMode.Off;
            beam.receiveShadows = false;
            beam.enabled = false;
            return beam;
        }

        private void OnDestroy()
        {
            if (beamMaterial == null) return;
            if (Application.isPlaying) Destroy(beamMaterial);
            else DestroyImmediate(beamMaterial);
        }

        private static GameObject Part(PrimitiveType type, string name, Transform parent, Vector3 scale, Vector3 localPosition)
        {
            GameObject part = RuntimePrimitive.Create(type);
            part.name = name;
            Collider collider = part.GetComponent<Collider>();
            if (collider != null)
            {
                if (Application.isPlaying) Destroy(collider);
                else DestroyImmediate(collider);
            }
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = scale;
            return part;
        }

        private static float EaseOutCubic(float t) { float inverse = 1f - t; return 1f - inverse * inverse * inverse; }
    }
}
