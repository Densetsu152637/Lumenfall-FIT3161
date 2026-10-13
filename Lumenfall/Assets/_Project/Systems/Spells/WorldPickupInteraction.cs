using System.Collections.Generic;
using UnityEngine;
using Lumenfall.Controls;
using Lumenfall.DataDriven;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Inventory;
using Lumenfall.Cutscenes;

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
        private GUIStyle boxStyle;
        private GUIStyle keyStyle;
        private Texture2D keyTexture;
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
            if (keyTexture != null) Destroy(keyTexture);
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
                audioSource.PlayOneShot(pickupClip, 0.6f);
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
                    markers[item.id] = marker = BuildMarker(item.id, world.CellSize);
                marker.transform.SetPositionAndRotation(world.CellToWorld(item.cell, ground + 0.05f), world.transform.rotation * Quaternion.Euler(90f, 0f, 0f));
                marker.transform.localScale = Vector3.one * world.CellSize * (1.1f + 0.15f * pulse);
                Color colour = MarkerColor; colour.a = 0.55f * pulse;
                marker.GetComponent<Renderer>().sharedMaterial.color = colour;
            }
            foreach (string id in staleMarkers)
            {
                if (markers[id] != null) { Destroy(markers[id].GetComponent<Renderer>().sharedMaterial); Destroy(markers[id]); }
                markers.Remove(id);
            }
        }

        private GameObject BuildMarker(string id, float cellSize)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Quad);
            marker.name = "Pickup marker " + id;
            Destroy(marker.GetComponent<Collider>());
            marker.transform.SetParent(transform, false);
            marker.GetComponent<Renderer>().sharedMaterial = new Material(markerMaterial) { mainTexture = glowTexture, color = MarkerColor };
            return marker;
        }

        private void ClearMarkers()
        {
            foreach (GameObject marker in markers.Values)
                if (marker != null) { Destroy(marker.GetComponent<Renderer>().sharedMaterial); Destroy(marker); }
            markers.Clear();
        }

        private void OnGUI()
        {
            if (promptTarget == null || !promptVisible || Camera.main == null) return;
            Vector3 screen = Camera.main.WorldToScreenPoint(promptPosition + Vector3.up * 0.9f);
            if (screen.z <= 0f) return;
            if (boxStyle == null)
            {
                boxStyle = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontSize = 14 };
                boxStyle.normal.textColor = new Color(0.95f, 0.92f, 0.84f);
                keyTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                keyTexture.SetPixel(0, 0, new Color(0.93f, 0.78f, 0.36f, 1f));
                keyTexture.Apply();
                keyStyle = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontSize = 13 };
                keyStyle.normal.textColor = new Color(0.10f, 0.11f, 0.13f);
                keyStyle.normal.background = keyTexture;
            }
            Rect rect = new Rect(screen.x - 62f, Screen.height - screen.y - 17f, 124f, 34f);
            GUI.Box(rect, GUIContent.none, boxStyle);
            GUI.Label(new Rect(rect.x + 8f, rect.y + 5f, 24f, 24f), KeyBindings.GetKeyLabel(GameAction.Talk), keyStyle);
            GUI.Label(new Rect(rect.x + 37f, rect.y + 5f, 80f, 24f), "Pick up", boxStyle);
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
