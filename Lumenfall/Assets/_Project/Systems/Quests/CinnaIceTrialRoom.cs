using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Lumenfall.Controls;
using Lumenfall.DataDriven;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Inventory;
using Lumenfall.Spells;

namespace Lumenfall.Quests
{
    /// <summary>
    /// Builds Cinna Cirra's ice trial (<c>C03.IceTrial</c>) for Cold Feet, Warm Idea from its area definition. Cinna is
    /// spawned from their catalog record and teaches Glaciate. The frost relic is the orientation relic the player carried
    /// in with Vis during Out of Alignment and set on the pedestal; <see cref="CinnaAlignmentRelic"/> draws it, and it only
    /// takes Glaciate once delivered. Casting Glaciate on it freezes the pool: its water stops blocking movement and every pool cell except the switches becomes GridWorld ice, so the
    /// player slides until they land on a switch or stop in front of a wall. Landing on a switch toggles it. With all
    /// twelve lit, landing on the seal in the east wall lights it, which completes the quest through its monitor.
    /// R resets the switches and returns the player to the pool entrance; the pool stays frozen. A second Glaciate on the
    /// relic thaws the pool again, moving the player out to the entrance if they are standing in it. The component sits on
    /// the area prefab root, so registration runs inside the loader's map rebuild and saved state restores normally.
    /// </summary>
    public sealed class CinnaIceTrialRoom : MonoBehaviour
    {
        public const string MapId = "C03.IceTrial";
        public const string ReturnPortalId = "P.ICETRIAL.CAMPUS";
        public const string QuestId = "cinna_cold_feet_warm_idea";
        public const string RelicId = "relic.cinna.frost";
        public const string FinalSwitchId = "switch.cinna.ice.final";
        public const string SwitchTag = "ice-switch";
        public const string FinalTag = "ice-final";
        public const string PoolType = "ice-pool";
        public const string EntranceType = "ice-entrance";
        /// <summary>Height of the pedestal top, where the delivered relic stands.</summary>
        public const float StandHeight = 0.6f;
        private const string TrialNpcId = "cinna_cirra_trial";
        private const string LessonFlag = "spell.lesson.Glaciate";
        private const string LearnedFlag = "spell.unlocked.Glaciate";
        private static readonly Color FloorColor = new Color(0.34f, 0.38f, 0.45f);
        private static readonly Color WallColor = new Color(0.58f, 0.7f, 0.82f);
        private static readonly Color WaterColor = new Color(0.1f, 0.3f, 0.55f);
        private static readonly Color IceColor = new Color(0.74f, 0.9f, 0.98f);
        private static readonly Color SwitchOffColor = new Color(0.22f, 0.27f, 0.36f);
        private static readonly Color SwitchOnColor = new Color(0.4f, 0.95f, 1f);
        private static readonly Color SealClosedColor = new Color(0.32f, 0.36f, 0.48f);
        private static readonly Color SealOpenColor = new Color(1f, 0.84f, 0.38f);
        private static readonly Color RelicBaseColor = new Color(0.5f, 0.5f, 0.56f);
        private static readonly Color StoneColor = new Color(0.55f, 0.55f, 0.6f);
        private static readonly Color PortalColor = new Color(0.62f, 0.86f, 1f);

        [Tooltip("Animator controller for Cinna's model, matching their campus placement.")]
        [SerializeField] private RuntimeAnimatorController cinnaAnimator;

        private readonly Dictionary<Vector2Int, string> switchCells = new Dictionary<Vector2Int, string>();
        private readonly Dictionary<string, GameObject> registeredViews = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        private readonly Dictionary<string, Renderer> switchRenderers = new Dictionary<string, Renderer>(StringComparer.Ordinal);
        private readonly List<Vector2Int> iceCells = new List<Vector2Int>();
        private Transform geometry;
        private GameObject portalObject;
        private Transform ring;
        private Renderer poolRenderer;
        private GridObstacle water;
        private Vector2Int finalCell;
        private Vector2Int entranceCell;
        private RectInt poolArea;
        private GridWorld world;
        private GridMover subscribedPlayer;
        private WorldStateService subscribedState;
        private bool frozenApplied;
        private string feedback;
        private float feedbackUntil;
        private GUIStyle titleStyle;
        private GUIStyle bodyStyle;
        private GUIStyle panelStyle;
        private Texture2D panelTexture;

        private void OnEnable()
        {
            DataDrivenWorldLoader loader = DataDrivenWorldLoader.Instance;
            MapDefinition map = loader == null ? null : loader.LoadedMap;
            if (map == null || !string.Equals(map.id, MapId, StringComparison.OrdinalIgnoreCase)) return;
            world = FindAnyObjectByType<GridWorld>();
            if (world == null) return;
            if (geometry == null) Build(map, loader.CurrentArea, loader.LoadedNpcs);
            RegisterTrialObjects(map);
            Subscribe();
            ApplyState(false);
        }

        private void OnDisable()
        {
            if (subscribedState != null) { subscribedState.StateChanged -= OnStateChanged; subscribedState.MapRebuilt -= OnMapRebuilt; }
            subscribedState = null;
            if (subscribedPlayer != null) subscribedPlayer.MoveCompleted -= OnPlayerMoveCompleted;
            subscribedPlayer = null;
            // The GridWorld is shared by every area, so the pool's ice must not outlive this one.
            SetIce(false);
        }

        private void OnDestroy()
        {
            if (panelTexture != null) Destroy(panelTexture);
        }

        private void Update()
        {
            if (geometry == null) return;
            Subscribe();
            if (ring != null && portalObject != null && portalObject.activeSelf) ring.Rotate(0f, 85f * Time.unscaledDeltaTime, 0f, Space.Self);
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.rKey.wasPressedThisFrame) ResetTrial();
        }

        // The relic and the switches are registered each time the area activates so the loader's pending save
        // restore applies to them; walls, water and Cinna only need their GridObstacles.
        private void RegisterTrialObjects(MapDefinition map)
        {
            WorldStateService state = WorldStateService.Instance;
            if (state == null) return;
            foreach (MapObjectDefinition item in map.objects ?? Array.Empty<MapObjectDefinition>())
                if (item != null && registeredViews.TryGetValue(item.id ?? string.Empty, out GameObject view))
                    state.RegisterObject(item.id, view, new Vector2Int(item.x, item.y), new Vector2Int(Mathf.Max(1, item.width), Mathf.Max(1, item.height)),
                        item.compatibleWithSpells && (item.id != RelicId || RelicDelivered()), item.tags);
        }

        /// <summary>True once the orientation relic has been carried in and set on the pedestal.</summary>
        public static bool RelicDelivered()
        {
            GameEventRuntime runtime = GameEventManager.Instance?.Runtime;
            return runtime != null && runtime.GetFlag(SpellCaster.VisDeliveredFlagPrefix + CinnaAlignmentRelic.RelicId);
        }

        private void Subscribe()
        {
            WorldStateService state = WorldStateService.Instance;
            if (state != null && state != subscribedState)
            {
                if (subscribedState != null) { subscribedState.StateChanged -= OnStateChanged; subscribedState.MapRebuilt -= OnMapRebuilt; }
                subscribedState = state;
                state.StateChanged += OnStateChanged;
                state.MapRebuilt += OnMapRebuilt;
            }

            GridPlayerInput input = FindAnyObjectByType<GridPlayerInput>();
            GridMover player = input == null ? null : input.GetComponent<GridMover>();
            if (player == null || player == subscribedPlayer) return;
            if (subscribedPlayer != null) subscribedPlayer.MoveCompleted -= OnPlayerMoveCompleted;
            subscribedPlayer = player;
            player.MoveCompleted += OnPlayerMoveCompleted;
        }

        // MapRebuilt runs after a save restore and before the rebuild's StateChanged, so a pool that was already frozen
        // is applied silently and only a live Glaciate cast announces the freeze.
        private void OnMapRebuilt(MapRebuildEvent rebuild) { ApplyState(false); }

        private void OnStateChanged(MapStateSnapshot snapshot) { ApplyState(true); }

        private void ApplyState(bool announce)
        {
            WorldStateService state = WorldStateService.Instance;
            if (state == null || geometry == null) return;
            bool frozen = state.TryGetObject(RelicId, out WorldObjectSnapshot relic) && relic.frozen;
            if (announce && frozen && !frozenApplied) ShowFeedback("The pool freezes over. Step onto the ice from the gap in the south wall.");
            // A thaw puts the water back; anyone still in the pool steps out to the entrance first so they are not left
            // standing inside the water's blocker.
            if (!frozen && frozenApplied)
            {
                bool movedOut = subscribedPlayer != null && poolArea.Contains(subscribedPlayer.CurrentCell) && subscribedPlayer.TryTeleportTo(world, entranceCell);
                if (announce) ShowFeedback(movedOut ? "The pool thaws, and you step back out to the entrance." : "The pool thaws back to water.");
            }
            frozenApplied = frozen;
            if (water != null && water.enabled == frozen) water.enabled = !frozen;
            SetIce(frozen);
            if (poolRenderer != null) poolRenderer.material.color = frozen ? IceColor : WaterColor;
            // Delivery happens while the room is loaded, so the relic starts taking Glaciate as soon as it lands.
            if (relic != null && !relic.spellCompatible && RelicDelivered()) state.SetProperty(RelicId, x => x.spellCompatible = true);
            foreach (KeyValuePair<string, Renderer> pair in switchRenderers)
            {
                bool lit = state.TryGetObject(pair.Key, out WorldObjectSnapshot item) && item.powered;
                bool final = pair.Key == FinalSwitchId;
                SetColor(pair.Value, lit ? (final ? SealOpenColor : SwitchOnColor) : (final ? SealClosedColor : SwitchOffColor), lit);
            }
        }

        private void SetIce(bool frozen)
        {
            if (world == null) return;
            foreach (Vector2Int cell in iceCells) world.SetIce(cell, frozen);
        }

        private void OnPlayerMoveCompleted(GridMover player, Vector2Int cell)
        {
            string landed = switchCells.TryGetValue(cell, out string switchId) ? switchId : cell == finalCell ? FinalSwitchId : null;
            string message = Land(WorldStateService.Instance, landed);
            if (message != null) ShowFeedback(message);
        }

        /// <summary>
        /// Applies the trial rules when the player stops on a switch or the seal (<paramref name="landedId"/>), and
        /// returns the feedback line, or null when nothing applies. A switch toggles while the pool is frozen and the
        /// seal is closed; the seal opens only when every switch is lit, and the switches then hold until a reset.
        /// </summary>
        public static string Land(WorldStateService state, string landedId)
        {
            if (landedId == null || state == null || !state.IsReady || !string.Equals(state.CurrentMapId, MapId, StringComparison.OrdinalIgnoreCase) ||
                !state.TryGetObject(RelicId, out WorldObjectSnapshot relic) || !relic.frozen) return null;
            bool sealOpen = state.TryGetObject(FinalSwitchId, out WorldObjectSnapshot seal) && seal.powered;
            int litCount, total;
            if (landedId == FinalSwitchId)
            {
                CountSwitches(state, out litCount, out total);
                if (total == 0 || litCount < total) return $"The seal stays cold: {litCount} of {total} switches lit.";
                if (!sealOpen) state.SetProperty(FinalSwitchId, x => x.powered = true);
                return "The seal blazes open. The trial is complete.";
            }
            if (sealOpen || !state.TryGetObject(landedId, out WorldObjectSnapshot item) || !HasTag(item, SwitchTag)) return null;
            bool lit = !item.powered;
            state.SetProperty(landedId, x => x.powered = lit);
            CountSwitches(state, out litCount, out total);
            return lit ? $"Switch lit ({litCount} of {total})." : $"Switch dimmed ({litCount} of {total} lit).";
        }

        /// <summary>Turns every switch and the seal off and returns the player to the pool entrance.</summary>
        public bool ResetTrial()
        {
            WorldStateService state = WorldStateService.Instance;
            GameEventRuntime runtime = GameEventManager.Instance?.Runtime;
            if (Time.timeScale <= 0f || InventoryUiState.IsOpen || runtime?.ActiveDialogue != null || subscribedPlayer == null ||
                subscribedPlayer.IsMoving || !ResetSwitches(state)) return false;
            subscribedPlayer.TryTeleportTo(world, entranceCell);
            ShowFeedback("Trial reset. Every switch is dark again.");
            return true;
        }

        /// <summary>Turns every switch and the seal off; the pool stays frozen. False when the trial is not frozen.</summary>
        public static bool ResetSwitches(WorldStateService state)
        {
            if (state == null || !state.IsReady || !string.Equals(state.CurrentMapId, MapId, StringComparison.OrdinalIgnoreCase) ||
                !state.TryGetObject(RelicId, out WorldObjectSnapshot relic) || !relic.frozen) return false;
            foreach (WorldObjectSnapshot item in state.SnapshotObjects())
                if ((HasTag(item, SwitchTag) || HasTag(item, FinalTag)) && item.powered) state.SetProperty(item.id, x => x.powered = false);
            return true;
        }

        /// <summary>
        /// The next step of the trial for the quest hint overlay, read only from live world state so it works without
        /// a room instance.
        /// </summary>
        public static string DescribeStep(WorldStateService state)
        {
            if (state == null || !state.TryGetObject(RelicId, out WorldObjectSnapshot relic)) return "Enter Cinna's ice trial.";
            if (!relic.frozen) return "Cast Glaciate on the relic on its stand beside the pool to freeze it.";
            if (state.TryGetObject(FinalSwitchId, out WorldObjectSnapshot seal) && seal.powered) return "The seal is open. Talk to Cinna Cirra.";
            CountSwitches(state, out int lit, out int total);
            return lit == total
                ? "Every switch is lit. Slide into the seal in the east wall."
                : $"Light every switch: {lit} of {total} lit. Landing on a lit switch dims it. Press R to reset.";
        }

        private static void CountSwitches(WorldStateService state, out int lit, out int total)
        {
            lit = 0; total = 0;
            foreach (WorldObjectSnapshot item in state.SnapshotObjects())
            {
                if (!HasTag(item, SwitchTag)) continue;
                total++;
                if (item.powered) lit++;
            }
        }

        private static bool HasTag(WorldObjectSnapshot item, string tag) =>
            item != null && Array.Exists(item.tags ?? Array.Empty<string>(), value => string.Equals(value, tag, StringComparison.OrdinalIgnoreCase));

        private static bool HasTag(MapObjectDefinition item, string tag) =>
            item != null && Array.Exists(item.tags ?? Array.Empty<string>(), value => string.Equals(value, tag, StringComparison.OrdinalIgnoreCase));

        private void ShowFeedback(string message) { feedback = message; feedbackUntil = Time.unscaledTime + 4f; }

        private void OnGUI()
        {
            if (geometry == null || Time.timeScale <= 0f || InventoryUiState.IsOpen) return;
            GameEventRuntime runtime = GameEventManager.Instance?.Runtime;
            if (runtime == null || runtime.ActiveDialogue != null) return;
            WorldStateService state = WorldStateService.Instance;
            if (state == null || !string.Equals(state.CurrentMapId, MapId, StringComparison.OrdinalIgnoreCase)) return;
            EnsureStyles();
            if (Time.unscaledTime < feedbackUntil && !string.IsNullOrEmpty(feedback))
                GUI.Box(new Rect(Screen.width * 0.5f - 240f, Screen.height - 120f, 480f, 42f), feedback);
            if (!runtime.GetFlag(LessonFlag) && !runtime.GetFlag(LearnedFlag)) return;

            string body;
            if (!state.TryGetObject(RelicId, out WorldObjectSnapshot relic) || !relic.frozen)
                body = $"1. Press {KeyBindings.GetKeyLabel(GameAction.Spell5)} to select Glaciate.\n" +
                       "2. Stand beside the relic on its stand and right-click (or press " + KeyBindings.GetKeyLabel(GameAction.Interact) + ") to freeze the pool.";
            else
                body = DescribeStep(state) + "\nSlides stop only on switches, walls or the entrance. Cast Glaciate on the relic again to thaw the pool.";
            float width = Mathf.Min(360f, Screen.width - 40f);
            float height = 40f + bodyStyle.CalcHeight(new GUIContent(body), width - 28f);
            Rect panel = new Rect(Screen.width - width - 20f, 20f, width, height);
            GUI.Box(panel, GUIContent.none, panelStyle);
            GUI.Label(new Rect(panel.x + 14f, panel.y + 8f, width - 28f, 24f), "Ice Trial", titleStyle);
            GUI.Label(new Rect(panel.x + 14f, panel.y + 34f, width - 28f, height - 40f), body, bodyStyle);
        }

        private void EnsureStyles()
        {
            if (panelStyle != null) return;
            panelTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            panelTexture.SetPixel(0, 0, new Color(0.06f, 0.09f, 0.13f, 0.84f));
            panelTexture.Apply();
            panelStyle = new GUIStyle(GUI.skin.box);
            panelStyle.normal.background = panelTexture;
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
            titleStyle.normal.textColor = new Color(0.7f, 0.92f, 1f);
            bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
            bodyStyle.normal.textColor = new Color(0.92f, 0.95f, 0.97f);
            Font font = Resources.Load<Font>("Dialogue/Nunito-Regular");
            if (font != null) { titleStyle.font = font; bodyStyle.font = font; }
        }

        private void Build(MapDefinition map, WorldAreaAsset area, NpcDatabaseDefinition npcs)
        {
            geometry = new GameObject("Ice Trial Geometry").transform;
            geometry.SetParent(transform, false);
            float size = world.CellSize;
            Vector3 centre = (world.CellToWorld(Vector2Int.zero) + world.CellToWorld(new Vector2Int(map.width - 1, map.height - 1))) * 0.5f;
            Part(PrimitiveType.Cube, "Floor", centre + Vector3.down * 0.05f, new Vector3(map.width * size, 0.1f, map.height * size), FloorColor);

            List<MapObjectDefinition> switches = new List<MapObjectDefinition>();
            foreach (MapObjectDefinition item in map.objects ?? Array.Empty<MapObjectDefinition>())
            {
                if (item == null) continue;
                Vector2Int cell = new Vector2Int(item.x, item.y);
                Vector2Int footprint = new Vector2Int(Mathf.Max(1, item.width), Mathf.Max(1, item.height));
                if (string.Equals(item.type, PoolType, StringComparison.OrdinalIgnoreCase)) BuildPool(item.id, cell, footprint, size);
                else if (string.Equals(item.type, EntranceType, StringComparison.OrdinalIgnoreCase)) entranceCell = cell;
                else if (HasTag(item, SwitchTag) || HasTag(item, FinalTag)) switches.Add(item);
                else if (string.Equals(item.id, RelicId, StringComparison.Ordinal)) BuildRelic(item.id, cell, size);
                else if (item.blocks) BuildWall(item.id, cell, footprint, size);
            }
            // Switch cells are normal ground inside the pool, so slides stop on them.
            foreach (MapObjectDefinition item in switches)
            {
                Vector2Int cell = new Vector2Int(item.x, item.y);
                bool final = HasTag(item, FinalTag);
                if (final) finalCell = cell;
                else switchCells[cell] = item.id;
                iceCells.Remove(cell);
                BuildSwitch(item.id, cell, size, final);
            }

            BuildCinna(npcs);

            PortalContentAsset content = FindPortal(area, ReturnPortalId);
            if (content == null) { Debug.LogError($"Missing authored portal content '{ReturnPortalId}'.", this); return; }
            // A GridPortal's cell is its transform position; the landing stone sits on the trial spawn cell.
            Vector2Int landingCell = map.spawn != null ? new Vector2Int(map.spawn.x, map.spawn.y) : new Vector2Int(5, 0);
            Part(PrimitiveType.Cylinder, "Landing Stone", world.CellToWorld(landingCell, 0.02f), new Vector3(0.9f * size, 0.02f, 0.9f * size), StoneColor);
            // Built active inside the map rebuild so the loader finds it and suppresses re-entry on arrival.
            portalObject = new GameObject("Ice Trial Return Portal");
            portalObject.transform.SetParent(geometry, false);
            portalObject.transform.position = world.CellToWorld(landingCell, 0.03f);
            GridPortal portal = portalObject.AddComponent<GridPortal>();
            portal.Configure(content, world);
            GameObject visual = Part(PrimitiveType.Cylinder, "Return portal ring", Vector3.zero, new Vector3(0.82f * size, 0.06f, 0.82f * size), PortalColor, true);
            visual.transform.SetParent(portalObject.transform, false);
            visual.transform.localPosition = new Vector3(0f, 0.08f, 0f);
            ring = visual.transform;
        }

        private void BuildPool(string id, Vector2Int cell, Vector2Int footprint, float size)
        {
            Vector3 origin = world.CellToWorld(cell);
            Vector3 centre = origin + new Vector3((footprint.x - 1) * 0.5f * size, 0f, (footprint.y - 1) * 0.5f * size);
            poolRenderer = Part(PrimitiveType.Cube, id, centre + Vector3.up * 0.01f, new Vector3(footprint.x * size, 0.04f, footprint.y * size), WaterColor).GetComponent<Renderer>();
            poolArea = new RectInt(cell, footprint);
            for (int x = 0; x < footprint.x; x++)
                for (int y = 0; y < footprint.y; y++)
                    iceCells.Add(cell + new Vector2Int(x, y));
            // Open water blocks the whole pool, switches included, until the relic is frozen.
            GameObject blocker = new GameObject(id + " water");
            blocker.transform.SetParent(geometry, false);
            blocker.transform.position = origin;
            water = blocker.AddComponent<GridObstacle>();
            water.Configure(world, footprint);
        }

        private void BuildWall(string id, Vector2Int cell, Vector2Int footprint, float size)
        {
            Vector3 origin = world.CellToWorld(cell);
            Vector3 centre = origin + new Vector3((footprint.x - 1) * 0.5f * size, 0f, (footprint.y - 1) * 0.5f * size);
            Part(PrimitiveType.Cube, id, centre + Vector3.up * 0.45f, new Vector3(footprint.x * size, 0.9f, footprint.y * size), WallColor);
            GameObject blocker = new GameObject(id + " obstacle");
            blocker.transform.SetParent(geometry, false);
            blocker.transform.position = origin;
            blocker.AddComponent<GridObstacle>().Configure(world, footprint);
        }

        private void BuildSwitch(string id, Vector2Int cell, float size, bool final)
        {
            GameObject root = new GameObject(id);
            root.transform.SetParent(geometry, false);
            root.transform.position = world.CellToWorld(cell);
            float plate = final ? 0.9f : 0.7f;
            GameObject view = Part(PrimitiveType.Cube, id + " plate", Vector3.zero, new Vector3(plate * size, 0.08f, plate * size), final ? SealClosedColor : SwitchOffColor);
            view.transform.SetParent(root.transform, false);
            view.transform.localPosition = new Vector3(0f, 0.06f, 0f);
            registeredViews[id] = root;
            switchRenderers[id] = view.GetComponent<Renderer>();
        }

        private void BuildRelic(string id, Vector2Int cell, float size)
        {
            GameObject root = new GameObject(id);
            root.transform.SetParent(geometry, false);
            root.transform.position = world.CellToWorld(cell);
            GameObject pedestal = Part(PrimitiveType.Cylinder, id + " pedestal", Vector3.zero, new Vector3(0.6f * size, StandHeight * 0.5f, 0.6f * size), RelicBaseColor);
            pedestal.transform.SetParent(root.transform, false);
            pedestal.transform.localPosition = new Vector3(0f, StandHeight * 0.5f, 0f);
            root.AddComponent<GridObstacle>().Configure(world, Vector2Int.one);
            registeredViews[id] = root;
        }

        // Cinna is spawned from their catalog record (Cinna Cirra model, cell, quest-gated visibility and dialogue)
        // the way Katrina's model was placed in her garden recess.
        private void BuildCinna(NpcDatabaseDefinition npcs)
        {
            NpcDefinition cinna = Array.Find(npcs?.npcs ?? Array.Empty<NpcDefinition>(), npc =>
                npc != null && npc.id == TrialNpcId && string.Equals(npc.mapId, MapId, StringComparison.OrdinalIgnoreCase));
            if (cinna == null || cinna.prefab == null) { Debug.LogError($"NPC catalog has no '{TrialNpcId}' record with a prefab for {MapId}.", this); return; }
            Vector2Int cell = new Vector2Int(cinna.x, cinna.y);
            GameObject npc = Instantiate(cinna.prefab, geometry);
            npc.name = cinna.displayName;
            npc.transform.position = world.CellToWorld(cell);
            npc.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            Animator animator = npc.GetComponentInChildren<Animator>();
            if (animator != null && cinnaAnimator != null) animator.runtimeAnimatorController = cinnaAnimator;
            npc.AddComponent<DynamicNpc>().Configure(cinna, null);
            GameObject blocker = new GameObject(cinna.displayName + " obstacle");
            blocker.transform.SetParent(geometry, false);
            blocker.transform.position = world.CellToWorld(cell);
            blocker.AddComponent<GridObstacle>().Configure(world, Vector2Int.one);
        }

        private GameObject Part(PrimitiveType type, string name, Vector3 position, Vector3 scale, Color color, bool emissive = false)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = name;
            Collider collider = part.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            part.transform.SetParent(geometry, false);
            part.transform.position = position;
            part.transform.localScale = scale;
            Renderer renderer = part.GetComponent<Renderer>();
            if (renderer != null) SetColor(renderer, color, emissive);
            return part;
        }

        private static void SetColor(Renderer renderer, Color color, bool emissive)
        {
            renderer.material.color = color;
            if (emissive) { renderer.material.SetColor("_EmissionColor", color * 1.6f); renderer.material.EnableKeyword("_EMISSION"); }
            else renderer.material.DisableKeyword("_EMISSION");
        }

        private static PortalContentAsset FindPortal(WorldAreaAsset area, string id)
        {
            if (area != null)
                foreach (PortalContentAsset candidate in area.portals ?? Array.Empty<PortalContentAsset>())
                    if (candidate != null && string.Equals(candidate.id, id, StringComparison.Ordinal)) return candidate;
            return null;
        }
    }
}
