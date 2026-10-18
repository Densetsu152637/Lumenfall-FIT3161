using System.Collections.Generic;
using UnityEngine;
using Lumenfall.Controls;
using Lumenfall.DataDriven;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Inventory;
using Lumenfall.Cutscenes;
using Lumenfall.Startup;

namespace Lumenfall.Spells
{
    /// <summary>
    /// Lets the player pick up world objects tagged <c>pickup</c> with the talk key, including hidden ones: Lost Echoes
    /// only helps locate them. Visible (or revealed) pickups get a soft pulsing glow and an on-screen prompt; a concealed
    /// pickup shows neither, so nothing gives it away, but the talk key still picks it up. Once collected it leaves the world and its <c>on-pickup:&lt;eventId&gt;</c> hooks fire. NPCs in
    /// talk range keep priority on the talk key.
    /// </summary>
    public sealed class WorldPickupInteraction : MonoBehaviour
    {
        private const int PickupRadius = 1;
        private const float NpcTalkRange = 2.01f;
        private static readonly Color MarkerColor = new Color(1f, 0.85f, 0.45f);
        // How tall, in cells, the column of light over a revealed pickup rises.
        private const float BeamHeight = 2.4f;
        // The halo is a deep violet, which contrasts with green grass and grey stone; the core is a warm white.
        private static readonly Color HaloColor = new Color(0.42f, 0.2f, 0.85f);
        private static readonly Color CoreColor = new Color(1f, 0.95f, 0.78f);
        private static WorldPickupInteraction instance;

        private readonly Dictionary<string, GameObject> markers = new Dictionary<string, GameObject>();
        private readonly List<string> staleMarkers = new List<string>();
        private Material markerMaterial;
        private Texture2D glowTexture;
        private AudioSource audioSource;
        private AudioClip pickupClip;
        private string promptTarget;
        private bool promptVisible;
        private Vector3 promptPosition;
        private GridPlayerInput playerInput;
        private WorldObjectSnapshot[] cachedObjects;
        private WorldStateService cachedState;
        private int cachedVersion;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;
            GameObject host = new GameObject("[World Pickup Interaction]");
            instance = host.AddComponent<WorldPickupInteraction>();
            DontDestroyOnLoad(host);
        }

        private void Awake()
        {
            // Drawn with GUI only, never GUILayout, so skip the per-frame layout pass and its allocations.
            useGUILayout = false;
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
            // A real clip at Resources/Audio/Pickup replaces the generated blip.
            pickupClip = Resources.Load<AudioClip>("Audio/Pickup") ?? BuildPickupClip();
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
            foreach (GameObject marker in markers.Values) if (marker != null) Destroy(marker);
            if (markerMaterial != null) Destroy(markerMaterial);
            if (glowTexture != null) Destroy(glowTexture);
        }

        private void Update()
        {
            promptTarget = null;
            WorldStateService state = WorldStateService.Instance;
            if (playerInput == null || !playerInput.gameObject.activeInHierarchy) playerInput = FindAnyObjectByType<GridPlayerInput>();
            GridMover player = playerInput == null ? null : playerInput.GetComponent<GridMover>();
            GridWorld world = player == null ? null : player.World;
            if (state == null || !state.IsReady || world == null) { ClearMarkers(); return; }

            // Cloning every world object each frame fed the GC; re-snapshot only when the world state has moved on.
            if (cachedObjects == null || cachedState != state || cachedVersion != state.Version)
            {
                cachedObjects = state.SnapshotObjects();
                cachedState = state;
                cachedVersion = state.Version;
            }
            WorldObjectSnapshot[] objects = cachedObjects;
            UpdateMarkers(objects, state.CurrentMapId, world, player);

            bool blocked = Time.timeScale <= 0f || InventoryUiState.IsOpen || DialogueOverlay.IsNpcInteractionBlocked || CutscenePlayer.BlocksInput ||
                GridPortal.IsPlayerAtInteractivePortal || GameEventManager.Instance?.Runtime?.ActiveDialogue != null ||
                IsNpcInTalkRange(player.transform.position);
            if (blocked) return;
            WorldObjectSnapshot nearest = NearestCollectable(objects, state.CurrentMapId, player.CurrentCell);
            promptTarget = nearest?.id;
            // Only a visible or revealed pickup is announced; Lost Echoes is how a concealed one is found.
            promptVisible = nearest != null && !WorldStateService.IsConcealed(nearest);
            if (nearest != null)
                promptPosition = world.CellToWorld(nearest.cell, world.WorldToElevation(player.transform.position));
            if (promptTarget != null && KeyBindings.WasPressed(GameAction.Talk) &&
                state.TryCollectObject(promptTarget, player.CurrentCell, PickupRadius))
            {
                Lumenfall.Audio.AudioPlayback.PlayOneShot(audioSource, pickupClip, 0.6f);
                promptTarget = null;
            }
        }

        private static WorldObjectSnapshot NearestCollectable(WorldObjectSnapshot[] objects, string mapId, Vector2Int cell)
        {
            WorldObjectSnapshot best = null;
            int bestDistance = PickupRadius + 1;
            foreach (WorldObjectSnapshot item in objects)
            {
                if (!WorldStateService.IsCollectable(item) || !string.Equals(item.mapId, mapId, System.StringComparison.OrdinalIgnoreCase)) continue;
                int distance = Mathf.Abs(item.cell.x - cell.x) + Mathf.Abs(item.cell.y - cell.y);
                if (distance < bestDistance) { bestDistance = distance; best = item; }
            }
            return best;
        }

        // Mirrors DynamicNpc's talk range so the talk key has exactly one meaning at a time.
        private static bool IsNpcInTalkRange(Vector3 position)
        {
            foreach (DynamicNpc npc in DynamicNpc.Active)
                if (Vector3.Distance(position, npc.transform.position) <= NpcTalkRange) return true;
            return false;
        }

        private void UpdateMarkers(WorldObjectSnapshot[] objects, string mapId, GridWorld world, GridMover player)
        {
            if (!EnsureMaterial()) return;
            float ground = world.WorldToElevation(player.transform.position);
            float pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 3f);
            staleMarkers.Clear();
            staleMarkers.AddRange(markers.Keys);
            foreach (WorldObjectSnapshot item in objects)
            {
                // A concealed pickup gets no glow; Lost Echoes reveals it, and then the glow shows where it lies.
                if (!WorldStateService.IsCollectable(item) || WorldStateService.IsConcealed(item) ||
                    !string.Equals(item.mapId, mapId, System.StringComparison.OrdinalIgnoreCase)) continue;
                staleMarkers.Remove(item.id);
                if (!markers.TryGetValue(item.id, out GameObject marker) || marker == null)
                    markers[item.id] = marker = BuildMarker(item.id);
                PlaceMarker(marker, world.CellToWorld(item.cell, ground + 0.05f), world, pulse);
            }
            foreach (string id in staleMarkers)
            {
                DestroyMarker(markers[id]);
                markers.Remove(id);
            }
        }

        // A revealed pickup keeps its light until it is picked up: a glow on the floor, a beam rising from it that shows
        // above furniture and walls, and a warm point light on its surroundings. Each glow is a deep violet halo behind
        // a bright warm-white core, so it stands out on green grass and grey stone alike.
        private void PlaceMarker(GameObject marker, Vector3 position, GridWorld world, float pulse)
        {
            float cell = world.CellSize;
            marker.transform.position = position;
            Transform floorHalo = marker.transform.GetChild(0), floorCore = marker.transform.GetChild(1);
            Transform beamHalo = marker.transform.GetChild(2), beamCore = marker.transform.GetChild(3), glow = marker.transform.GetChild(4);
            Quaternion flat = world.transform.rotation * Quaternion.Euler(90f, 0f, 0f);
            floorHalo.SetPositionAndRotation(position, flat);
            floorHalo.localScale = Vector3.one * cell * (1.9f + 0.2f * pulse);
            SetColour(floorHalo, HaloColor, 0.55f + 0.15f * pulse);
            floorCore.SetPositionAndRotation(position + Vector3.up * 0.01f, flat);
            floorCore.localScale = Vector3.one * cell * (1.0f + 0.15f * pulse);
            SetColour(floorCore, CoreColor, 0.9f * pulse);
            // The beam turns to face the camera so it reads as a column of light from any angle.
            Camera view = Camera.main;
            Vector3 facing = view == null ? Vector3.forward : Vector3.ProjectOnPlane(view.transform.forward, Vector3.up);
            Quaternion upright = facing.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(facing, Vector3.up) : Quaternion.identity;
            beamHalo.SetPositionAndRotation(position + Vector3.up * BeamHeight * cell * 0.5f, upright);
            beamHalo.localScale = new Vector3(0.95f * cell, BeamHeight * cell * 1.05f, 1f);
            SetColour(beamHalo, HaloColor, 0.5f + 0.1f * pulse);
            beamCore.SetPositionAndRotation(position + Vector3.up * BeamHeight * cell * 0.5f, upright);
            beamCore.localScale = new Vector3(0.38f * cell, BeamHeight * cell, 1f);
            SetColour(beamCore, CoreColor, 0.7f * pulse);
            glow.localPosition = Vector3.up * 0.9f * cell;
            Light light = glow.GetComponent<Light>();
            light.range = 3.2f * cell;
            light.intensity = 1.1f + 0.5f * pulse;
        }

        private static void SetColour(Transform part, Color colour, float alpha)
        {
            colour.a = alpha;
            part.GetComponent<Renderer>().sharedMaterial.color = colour;
        }

        private GameObject BuildMarker(string id)
        {
            GameObject marker = new GameObject("Pickup marker " + id);
            marker.transform.SetParent(transform, false);
            // Halos draw before their cores, so the bright core always sits on top of the violet.
            (string name, int queue)[] parts = { ("Floor halo", 3000), ("Floor glow", 3001), ("Beam halo", 3002), ("Beam", 3003) };
            foreach ((string name, int queue) in parts)
            {
                GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = name;
                Destroy(quad.GetComponent<Collider>());
                quad.transform.SetParent(marker.transform, false);
                Renderer renderer = quad.GetComponent<Renderer>();
                renderer.sharedMaterial = new Material(markerMaterial) { mainTexture = glowTexture, color = CoreColor, renderQueue = queue };
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            GameObject glow = new GameObject("Light");
            glow.transform.SetParent(marker.transform, false);
            Light light = glow.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = MarkerColor;
            light.shadows = LightShadows.None;
            return marker;
        }

        private static void DestroyMarker(GameObject marker)
        {
            if (marker == null) return;
            foreach (Renderer renderer in marker.GetComponentsInChildren<Renderer>(true)) Destroy(renderer.sharedMaterial);
            Destroy(marker);
        }

        private void ClearMarkers()
        {
            foreach (GameObject marker in markers.Values) DestroyMarker(marker);
            markers.Clear();
        }

        private void OnGUI()
        {
            using (UiScale.Begin()) DrawGui();
        }

        private void DrawGui()
        {
            if (promptTarget == null || !promptVisible) return;
            HudSkin.PromptAbove(promptPosition + Vector3.up * 0.9f, KeyBindings.GetKeyLabel(GameAction.Talk), "Pick up");
        }

        // Sprites/Default is transparent, unlit, double-sided and supports a tint, which suits a ground glow.
        private bool EnsureMaterial()
        {
            if (markerMaterial != null) return true;
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) return false;
            markerMaterial = new Material(shader) { name = "Pickup Marker" };
            glowTexture = new Texture2D(64, 64, TextureFormat.RGBA32, false) { name = "Pickup Glow", wrapMode = TextureWrapMode.Clamp };
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

        // A quick rising two-note blip, generated so pickups need no audio asset.
        private static AudioClip BuildPickupClip()
        {
            const int sampleRate = 44100;
            const float seconds = 0.28f;
            int samples = Mathf.CeilToInt(seconds * sampleRate);
            float[] data = new float[samples];
            float phase = 0f;
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)samples;
                float frequency = t < 0.45f ? 660f : 990f;
                phase += 2f * Mathf.PI * frequency / sampleRate;
                float envelope = Mathf.Min(1f, t * 60f) * Mathf.Exp(-5f * t);
                data[i] = 0.3f * envelope * Mathf.Sin(phase);
            }
            AudioClip clip = AudioClip.Create("WorldPickup", samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
