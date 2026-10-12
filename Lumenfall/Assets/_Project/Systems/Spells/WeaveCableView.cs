using System;
using System.Collections.Generic;
using UnityEngine;
using Lumenfall.GameEvents;

namespace Lumenfall.Spells
{
    /// Presents Weave and Ampere state from WorldStateService: every connection is drawn as a sagging cable,
    /// live cables pulse with a spark travelling from the powered end, and powered relics glow. Authored
    /// weave-with pairs get a faint guide thread once Weave is learned so players can see what to join. While the
    /// player holds a circuit thread, it trails from its relic to the player with faint lines to every relic in reach.
    [DisallowMultipleComponent]
    public sealed class WeaveCableView : MonoBehaviour
    {
        private const float MinimumLivePower = 0.2f;
        private const int Segments = 20;
        private const float CableSag = 0.3f;
        private const string WeaveFlag = "spell.unlocked.Weave";
        private static readonly Color LiveColor = new Color(0.35f, 0.95f, 1f, 1f);
        private static readonly Color DeadColor = new Color(1f, 0.55f, 0.2f, 0.85f);
        private static readonly Color GuideColor = new Color(1f, 1f, 1f, 0.35f);
        private static readonly Color GlowColor = new Color(1f, 0.85f, 0.4f, 1f);
        private static readonly Color ThreadColor = new Color(0.75f, 1f, 1f, 0.95f);
        private static readonly Color ReachColor = new Color(0.75f, 1f, 1f, 0.22f);
        private const string ThreadKey = "thread:held";

        private static WeaveCableView instance;
        private readonly Dictionary<string, Cable> cables = new Dictionary<string, Cable>(StringComparer.Ordinal);
        private readonly Dictionary<GameObject, Glow> glows = new Dictionary<GameObject, Glow>();
        private readonly List<CableSpec> specs = new List<CableSpec>();
        private readonly List<string> unused = new List<string>();
        private readonly List<WorldObjectSnapshot> circuitNodes = new List<WorldObjectSnapshot>();
        private SpellCaster caster;
        private WorldStateService subscribed;
        private bool dirty = true;
        private bool specsIncludeGuides;
        private Material lineMaterial;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)] private static void Bootstrap()
        {
            if (instance != null) return;
            GameObject host = new GameObject("[Weave Cable View]");
            instance = host.AddComponent<WeaveCableView>();
            DontDestroyOnLoad(host);
        }

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
            lineMaterial = new Material(shader) { name = "Weave Cable" };
        }

        private void OnDestroy()
        {
            Subscribe(null);
            if (instance == this) instance = null;
        }

        private void Subscribe(WorldStateService state)
        {
            if (subscribed == state) return;
            if (subscribed != null) { subscribed.StateChanged -= OnStateChanged; subscribed.MapRebuilt -= OnMapRebuilt; }
            subscribed = state;
            if (subscribed != null) { subscribed.StateChanged += OnStateChanged; subscribed.MapRebuilt += OnMapRebuilt; }
            dirty = true;
        }

        private void OnStateChanged(MapStateSnapshot _) { dirty = true; }
        private void OnMapRebuilt(MapRebuildEvent _) { dirty = true; }

        private void LateUpdate()
        {
            WorldStateService state = WorldStateService.Instance;
            Subscribe(state);
            if (state == null || !state.IsReady) { HideAll(); return; }
            bool weaveKnown = GameEventManager.Instance?.Runtime?.GetFlag(WeaveFlag) == true;
            // The flag is not part of world state, so learning Weave mid-map must also refresh the guides.
            if (dirty || weaveKnown != specsIncludeGuides) Rebuild(state, weaveKnown);
            Draw(state);
        }

        private void Rebuild(WorldStateService state, bool weaveKnown)
        {
            dirty = false;
            specsIncludeGuides = weaveKnown;
            specs.Clear();
            circuitNodes.Clear();
            foreach (string key in state.SnapshotConnections())
            {
                string[] ends = key.Split('|');
                if (ends.Length == 2) specs.Add(new CableSpec { key = key, a = ends[0], b = ends[1], guide = false });
            }
            WorldObjectSnapshot[] objects = state.SnapshotObjects();
            foreach (WorldObjectSnapshot item in objects)
            {
                if (HasPowerRole(item)) SetGlow(state, item.id, item.powered);
                if (item.spellCompatible && HasTag(item, "circuit")) circuitNodes.Add(item);
                if (!weaveKnown) continue;
                string partner = WeavePartner(item);
                // Each authored pair declares itself from both ends; draw one guide from the lower id.
                if (partner == null || string.CompareOrdinal(item.id, partner) > 0 || state.IsConnected(item.id, partner) ||
                    !state.TryGetObject(partner, out _)) continue;
                specs.Add(new CableSpec { key = "guide:" + item.id + "|" + partner, a = item.id, b = partner, guide = true });
            }
        }

        private void Draw(WorldStateService state)
        {
            unused.Clear();
            unused.AddRange(cables.Keys);
            float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * 5f);
            foreach (CableSpec spec in specs)
            {
                if (!TryGetAnchor(state, spec.a, out Vector3 from) || !TryGetAnchor(state, spec.b, out Vector3 to)) continue;
                Cable cable = GetCable(spec.key);
                unused.Remove(spec.key);
                bool live = !spec.guide && state.GetConnectionEffectivePower(spec.a, spec.b) >= MinimumLivePower;
                // Power flows out of the powered end so the spark shows which relic is feeding the cable.
                if (live && state.TryGetObject(spec.a, out WorldObjectSnapshot first) && !first.powered) { Vector3 swap = from; from = to; to = swap; }
                Color color = spec.guide ? GuideColor * new Color(1f, 1f, 1f, 0.6f + 0.4f * pulse) : live ? LiveColor * new Color(pulse, pulse, pulse, 1f) : DeadColor;
                float width = spec.guide ? 0.035f : live ? 0.09f : 0.06f;
                cable.Show(from, to, spec.guide ? -0.15f : CableSag, width, color, live);
            }
            DrawHeldThread(state, pulse);
            foreach (string key in unused) cables[key].Hide();
        }

        private void DrawHeldThread(WorldStateService state, float pulse)
        {
            if (caster == null) caster = FindAnyObjectByType<SpellCaster>();
            string held = caster == null ? null : caster.HeldThread;
            if (held == null || !TryGetAnchor(state, held, out Vector3 from) || !state.TryGetObject(held, out WorldObjectSnapshot origin)) return;
            GetCable(ThreadKey).Show(from, caster.transform.position + Vector3.up, -0.1f, 0.045f, ThreadColor * new Color(1f, 1f, 1f, pulse), false);
            unused.Remove(ThreadKey);
            foreach (WorldObjectSnapshot node in circuitNodes)
            {
                if (node.id == held || state.IsConnected(held, node.id) ||
                    1f / (1f + Vector2Int.Distance(origin.cell, node.cell)) < WorldStateService.MinimumCableStrength ||
                    !TryGetAnchor(state, node.id, out Vector3 to)) continue;
                string key = "reach:" + node.id;
                GetCable(key).Show(from, to, -0.2f, 0.025f, ReachColor, false);
                unused.Remove(key);
            }
        }

        private void HideAll()
        {
            foreach (Cable cable in cables.Values) cable.Hide();
        }

        private Cable GetCable(string key)
        {
            if (cables.TryGetValue(key, out Cable cable)) return cable;
            cable = new Cable(transform, key, lineMaterial);
            cables[key] = cable;
            return cable;
        }

        private static bool TryGetAnchor(WorldStateService state, string id, out Vector3 anchor)
        {
            anchor = default;
            if (!state.TryGetSceneObject(id, out GameObject sceneObject) || !sceneObject.activeInHierarchy) return false;
            Renderer renderer = sceneObject.GetComponentInChildren<Renderer>();
            anchor = renderer != null ? renderer.bounds.center + Vector3.up * renderer.bounds.extents.y : sceneObject.transform.position + Vector3.up * 0.5f;
            return true;
        }

        private static bool HasTag(WorldObjectSnapshot item, string tag) => Array.Exists(item.tags ?? Array.Empty<string>(), value => string.Equals(value, tag, StringComparison.OrdinalIgnoreCase));

        private static bool HasPowerRole(WorldObjectSnapshot item)
        {
            foreach (string tag in item.tags ?? Array.Empty<string>())
                if (string.Equals(tag, "ampere", StringComparison.OrdinalIgnoreCase) || string.Equals(tag, "weave", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string WeavePartner(WorldObjectSnapshot item)
        {
            foreach (string tag in item.tags ?? Array.Empty<string>())
                if (tag.StartsWith("weave-with:", StringComparison.OrdinalIgnoreCase)) return tag.Substring("weave-with:".Length);
            return null;
        }

        private void SetGlow(WorldStateService state, string id, bool powered)
        {
            if (!state.TryGetSceneObject(id, out GameObject sceneObject)) return;
            if (!glows.TryGetValue(sceneObject, out Glow glow))
            {
                Renderer renderer = sceneObject.GetComponentInChildren<Renderer>();
                if (renderer == null) return;
                glow = new Glow(sceneObject, renderer);
                glows[sceneObject] = glow;
            }
            glow.Apply(powered);
        }

        private struct CableSpec { public string key; public string a; public string b; public bool guide; }

        private sealed class Cable
        {
            private readonly LineRenderer line;
            private readonly Transform spark;
            private readonly Vector3[] points = new Vector3[Segments + 1];
    
            public Cable(Transform parent, string key, Material material)
            {
                GameObject host = new GameObject("Cable " + key);
                host.transform.SetParent(parent, false);
                line = host.AddComponent<LineRenderer>();
                line.sharedMaterial = material;
                line.useWorldSpace = true;
                line.positionCount = points.Length;
                line.numCapVertices = 4;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                GameObject sparkObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sparkObject.name = "Spark";
                Collider collider = sparkObject.GetComponent<Collider>();
                if (collider != null) Destroy(collider);
                sparkObject.transform.SetParent(host.transform, false);
                sparkObject.transform.localScale = Vector3.one * 0.18f;
                Renderer sparkRenderer = sparkObject.GetComponent<Renderer>();
                sparkRenderer.sharedMaterial = material;
                sparkRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                spark = sparkObject.transform;
            }

            public void Show(Vector3 from, Vector3 to, float sag, float width, Color color, bool live)
            {
                if (!line.gameObject.activeSelf) line.gameObject.SetActive(true);
                Vector3 control = (from + to) * 0.5f + Vector3.down * sag * 2f;
                for (int i = 0; i <= Segments; i++) points[i] = Bezier(from, control, to, i / (float)Segments);
                line.SetPositions(points);
                line.widthMultiplier = width;
                line.startColor = line.endColor = color;
                spark.gameObject.SetActive(live);
                if (live)
                {
                    float t = Mathf.Repeat(Time.time * 0.8f, 1f);
                    spark.position = Bezier(from, control, to, t);
                }
            }

            public void Hide()
            {
                if (line.gameObject.activeSelf) line.gameObject.SetActive(false);
            }

            private static Vector3 Bezier(Vector3 a, Vector3 control, Vector3 b, float t)
            {
                float u = 1f - t;
                return u * u * a + 2f * u * t * control + t * t * b;
            }
        }

        private sealed class Glow
        {
            private readonly Renderer renderer;
            private readonly Color baseColor;
            private readonly Light light;
            private bool? applied;

            public Glow(GameObject owner, Renderer renderer)
            {
                this.renderer = renderer;
                baseColor = renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty("_BaseColor")
                    ? renderer.sharedMaterial.GetColor("_BaseColor")
                    : renderer.sharedMaterial != null ? renderer.sharedMaterial.color : Color.white;
                GameObject lightObject = new GameObject("Power Glow");
                lightObject.transform.SetParent(owner.transform, false);
                lightObject.transform.localPosition = Vector3.up * 0.8f;
                light = lightObject.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = 3f;
                light.intensity = 2.5f;
                light.color = GlowColor;
                light.enabled = false;
            }

            public void Apply(bool powered)
            {
                if (applied == powered || renderer == null) return;
                applied = powered;
                Material material = renderer.material;
                Color color = powered ? Color.Lerp(baseColor, GlowColor, 0.75f) : baseColor;
                material.color = color;
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
                if (powered) { material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", GlowColor * 1.5f); }
                else { material.SetColor("_EmissionColor", Color.black); material.DisableKeyword("_EMISSION"); }
                if (light != null) light.enabled = powered;
            }
        }
    }
}
