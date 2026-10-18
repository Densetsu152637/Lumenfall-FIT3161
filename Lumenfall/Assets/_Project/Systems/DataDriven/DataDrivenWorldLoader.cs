using System;
using System.Collections.Generic;
using UnityEngine;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Spells;
using Lumenfall.Quests;
using Lumenfall.Story;

namespace Lumenfall.DataDriven
{
    /// <summary>Instantiates inspector-authored world area and NPC assets.</summary>
    public sealed class DataDrivenWorldLoader : MonoBehaviour, ISaveStateProvider, ISaveStateValidator, ISaveStateRestorePhases
    {
        public static DataDrivenWorldLoader Instance { get; private set; }
        [SerializeField] private WorldAreaAsset startingArea;
        [SerializeField] private NpcCatalogAsset npcCatalog;
        [SerializeField] private WorldAreaAsset[] availableAreas = Array.Empty<WorldAreaAsset>();
        [SerializeField] private Transform contentRoot;
        [SerializeField] private GridWorld world;
        private readonly Dictionary<WorldAreaAsset, GameObject> areaInstances = new Dictionary<WorldAreaAsset, GameObject>();
        private readonly List<int> npcStateSignature = new List<int>();
        private GameSaveManager saveManager;
        private bool registeredSaveProvider;
        public MapDefinition LoadedMap { get; private set; }
        public NpcDatabaseDefinition LoadedNpcs { get; private set; }
        public WorldAreaAsset CurrentArea { get; private set; }
        /// The live instance of <see cref="CurrentArea"/>; a new object whenever an area is loaded or re-instantiated.
        public GameObject CurrentAreaRoot { get; private set; }
        public string SaveKey => "data-driven-world";

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            saveManager = GameSaveManager.Instance;
            registeredSaveProvider = saveManager != null && saveManager.RegisterProvider(this);
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            if (registeredSaveProvider && saveManager != null) saveManager.UnregisterProvider(this);
            Instance = null;
        }

        private void Start()
        {
            if (world == null) world = FindAnyObjectByType<GridWorld>();
            if (startingArea == null) { Debug.LogError("Assign a starting World Area asset in the Inspector.", this); return; }
            Load(startingArea, npcCatalog);
        }

        private void Update()
        {
            if (LoadedNpcs == null || GameEventManager.Instance == null) return;
            // Compared in place rather than as a rebuilt string, so the per-frame check does not allocate.
            NpcDefinition[] npcs = LoadedNpcs.npcs ?? Array.Empty<NpcDefinition>();
            bool changed = npcStateSignature.Count != npcs.Length * 2;
            if (changed)
            {
                npcStateSignature.Clear();
                for (int i = 0; i < npcs.Length * 2; i++) npcStateSignature.Add(-1);
            }
            for (int i = 0; i < npcs.Length; i++)
            {
                changed |= UpdateSignature(i * 2, npcs[i].showWhenQuestCompleted);
                changed |= UpdateSignature(i * 2 + 1, npcs[i].hideWhenQuestCompleted);
            }
            if (changed)
            {
                GameObject activeArea;
                if (CurrentArea != null && areaInstances.TryGetValue(CurrentArea, out activeArea))
                    foreach (DynamicNpc npc in activeArea.GetComponentsInChildren<DynamicNpc>(true)) npc.RefreshVisibility();
            }
        }

        private bool UpdateSignature(int index, string questId)
        {
            int status = (int)GameEventManager.Instance.Runtime.GetQuestStatus(questId);
            if (npcStateSignature[index] == status) return false;
            npcStateSignature[index] = status;
            return true;
        }

        public void Load(WorldAreaAsset area, NpcCatalogAsset catalog) => Load(area, catalog, true);

        // A portal or a loaded save places the player itself, so those loads skip the map's spawn point.
        private void Load(WorldAreaAsset area, NpcCatalogAsset catalog, bool placeAtSpawn)
        {
            if (area == null || area.areaPrefab == null) { Debug.LogError("Assign an authored area prefab to this World Area asset.", this); return; }
            if (world == null) world = FindAnyObjectByType<GridWorld>();
            LiftOffGrid(FindAnyObjectByType<GridMover>());
            if (CurrentArea != null && areaInstances.TryGetValue(CurrentArea, out GameObject previous)) previous.SetActive(false);
            CurrentArea = area;
            LoadedMap = area.definition;
            LoadedNpcs = catalog == null ? new NpcDatabaseDefinition() : new NpcDatabaseDefinition { npcs = catalog.npcs, layers = catalog.layers };
            WorldStateService.Instance?.BeginMapRebuild(LoadedMap.id);
            WorldStateService.Instance?.RegisterPuzzleTargets(LoadedMap.puzzleTargets);
            if (world != null) world.ConfigureBounds(LoadedMap.width, LoadedMap.height);
            if (!areaInstances.TryGetValue(area, out GameObject root))
            {
                Transform parent = contentRoot == null ? transform : contentRoot;
                root = Instantiate(area.areaPrefab, parent);
                root.name = area.definition.id + " (Authored Area)";
                root.SetActive(false);
                areaInstances.Add(area, root);
            }
            root.SetActive(true);
            CurrentAreaRoot = root;
            foreach (DynamicNpc npc in root.GetComponentsInChildren<DynamicNpc>(true)) npc.RefreshVisibility();
            // Cached story actors must match the imported runtime before a spawn or saved player cell is validated.
            if (LoadedMap.id == SettlingInScene.MapId)
                SettlingInScene.RefreshRowanPlacement(root, world, GameEventManager.Instance?.Runtime);
            ApplySpawn(LoadedMap, placeAtSpawn);
            StellaStringThemTogetherPuzzle.RegisterForMap(LoadedMap.id, world);
            CinnaAlignmentRelic.RegisterForMap(LoadedMap, world);
            OrioRuneDiscPuzzle.RegisterForMap(LoadedMap.id, world, root);
            MiscCollectibles.RegisterForMap(LoadedMap.id, world, root);
            if (LoadedMap.id == OrioDaisRoom.MapId || OrioDaisRoom.IsLegacyVisitActive) OrioDaisRoom.RegisterForMap(LoadedMap.id, world, root);
            SecretRooms.RegisterForMap(LoadedMap.id, world, root);
            WorldStateService.Instance?.EndMapRebuild();
        }

        public void Clear()
        {
            foreach (GameObject item in areaInstances.Values) if (item != null) { item.SetActive(false); Destroy(item); }
            areaInstances.Clear(); LoadedMap = null; LoadedNpcs = null; CurrentArea = null; CurrentAreaRoot = null;
        }

        public void ResetToStartingState()
        {
            WorldStateService.Instance?.ResetAllState();
            if (startingArea != null) Load(startingArea, npcCatalog);
        }

        [Serializable] private sealed class SavedWorldState { public string mapId; public int playerX; public int playerY; }
        public string CaptureStateJson()
        {
            GridMover player = FindAnyObjectByType<GridMover>();
            return JsonUtility.ToJson(new SavedWorldState { mapId = LoadedMap == null ? string.Empty : LoadedMap.id,
                playerX = player == null ? 0 : player.CurrentCell.x, playerY = player == null ? 0 : player.CurrentCell.y });
        }

        public bool ValidateStateJson(string json)
        {
            try
            {
                if (!GameSaveManager.IsValidPayloadObject(json, "mapId", "playerX", "playerY")) return false;
                SavedWorldState state = JsonUtility.FromJson<SavedWorldState>(json);
                WorldAreaAsset area = FindArea(state == null ? null : state.mapId);
                GridMover player = FindAnyObjectByType<GridMover>();
                GridWorld grid = world == null ? FindAnyObjectByType<GridWorld>() : world;
                return area != null && area.areaPrefab != null && area.definition != null && player != null && !player.IsMoving && grid != null &&
                    state.playerX >= 0 && state.playerY >= 0 && state.playerX < area.definition.width && state.playerY < area.definition.height;
            }
            catch { return false; }
        }

        public bool PrepareRestoreStateJson(string json)
        {
            if (!ValidateStateJson(json)) return false;
            SavedWorldState state = JsonUtility.FromJson<SavedWorldState>(json);
            WorldAreaAsset area = FindArea(state.mapId);
            if (world == null) world = FindAnyObjectByType<GridWorld>();
            Load(area, npcCatalog, false);
            return CurrentArea == area;
        }

        public bool RestoreStateJson(string json)
        {
            if (!ValidateStateJson(json)) return false;
            SavedWorldState state = JsonUtility.FromJson<SavedWorldState>(json);
            WorldAreaAsset area = FindArea(state.mapId);
            if (CurrentArea != area && !PrepareRestoreStateJson(json)) return false;
            GridMover player = FindAnyObjectByType<GridMover>();
            if (CurrentArea != area || player == null || world == null) return false;
            Vector2Int savedCell = new Vector2Int(state.playerX, state.playerY);
            bool legacyRecess = string.Equals(state.mapId, "C04.Recess", StringComparison.OrdinalIgnoreCase);
            if (legacyRecess && area.definition.spawn != null)
            {
                savedCell = new Vector2Int(area.definition.spawn.x, area.definition.spawn.y);
                return TryRestoreLegacyArrival(player, savedCell);
            }
            return player.TryTeleportTo(world, savedCell);
        }

        // The retired Recess area migrated to the campus spawn, which is authored on blocked
        // border ground. Search outward only for this legacy migration, preserving normal saves.
        private bool TryRestoreLegacyArrival(GridMover player, Vector2Int origin)
        {
            Queue<Vector2Int> candidates = new Queue<Vector2Int>();
            HashSet<Vector2Int> visited = new HashSet<Vector2Int> { origin };
            candidates.Enqueue(origin);
            Vector2Int[] steps = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
            while (candidates.Count > 0)
            {
                Vector2Int candidate = candidates.Dequeue();
                if (world.IsWithinBounds(candidate) && world.GetPortalAt(candidate) == null && player.TryTeleportTo(world, candidate))
                    return true;
                foreach (Vector2Int step in steps)
                {
                    Vector2Int next = candidate + step;
                    if (!world.IsWithinBounds(next) || !visited.Add(next)) continue;
                    candidates.Enqueue(next);
                }
            }
            return false;
        }

        public bool SwitchArea(PortalContentAsset sourcePortal, Vector2Int sourceEntryOffset)
        {
            GridMover player = FindAnyObjectByType<GridMover>();
            if (player == null || world == null || sourcePortal == null || sourcePortal.destination == null || sourcePortal.destination.area == null) return false;
            PortalContentAsset destinationPortal = sourcePortal.destination;
            if (Array.IndexOf(availableAreas, destinationPortal.area) < 0 && destinationPortal.area != startingArea)
            { Debug.LogError($"Portal destination area '{destinationPortal.area.name}' is not in Available Areas.", this); return false; }
            Vector2Int previousCell = player.CurrentCell;
            WorldAreaAsset previousArea = CurrentArea;
            Load(destinationPortal.area, npcCatalog, false);
            GridPortal target = FindLivePortal(destinationPortal);
            if (target == null)
            {
                Debug.LogError($"Destination portal '{destinationPortal.id}' is not present in area '{destinationPortal.area.name}'.", this);
                if (previousArea != null) { Load(previousArea, npcCatalog, false); player.TryTeleportTo(world, previousCell); }
                return false;
            }
            // The live destination GridPortal is authoritative for its origin and footprint.
            // PortalContentAsset only supplies the connection.
            Vector2Int arrival = target.GetArrivalCell(sourceEntryOffset);
            if (player.TryTeleportTo(world, arrival, target)) return true;
            Debug.LogWarning($"Portal '{sourcePortal.id}' could not place the player at {arrival} in '{destinationPortal.area.definition.id}'; returning to the previous area.", this);
            if (previousArea != null) { Load(previousArea, npcCatalog, false); player.TryTeleportTo(world, previousCell); }
            return false;
        }

        /// <summary>
        /// Takes the player straight to <paramref name="cell"/> on another map, without a portal: into a secret room, or
        /// back out of one. Falls back to the map's spawn, and stays put if neither cell is free.
        /// </summary>
        public bool TravelTo(string mapId, Vector2Int cell)
        {
            GridMover player = FindAnyObjectByType<GridMover>();
            WorldAreaAsset area = FindArea(mapId);
            if (world == null) world = FindAnyObjectByType<GridWorld>();
            if (player == null || world == null || area == null || player.IsMoving) return false;
            Vector2Int previousCell = player.CurrentCell;
            WorldAreaAsset previousArea = CurrentArea;
            Load(area, npcCatalog, false);
            if (player.TryTeleportTo(world, cell)) return true;
            SpawnDefinition spawn = area.definition.spawn;
            if (spawn != null && player.TryTeleportTo(world, new Vector2Int(spawn.x, spawn.y))) return true;
            Debug.LogWarning($"Could not place the player at {cell} in '{mapId}'; returning to the previous area.", this);
            if (previousArea != null) { Load(previousArea, npcCatalog, false); player.TryTeleportTo(world, previousCell); }
            return false;
        }

        public bool TryGetMapDefinition(string id, out MapDefinition definition)
        {
            WorldAreaAsset area = FindArea(id);
            definition = area == null ? null : area.definition;
            return definition != null;
        }

        /// <summary>Normalizes area ids retained by older saves.</summary>
        private static string NormalizeMapId(string id)
        {
            if (string.Equals(id, "eastForest", StringComparison.OrdinalIgnoreCase)) return "westForest";
            if (string.Equals(id, "C04.Recess", StringComparison.OrdinalIgnoreCase)) return "campus";
            return id;
        }

        /// <summary>Resolves authored areas and the legacy hall for saved games.</summary>
        public WorldAreaAsset GetArea(string id) => FindArea(id);

        private WorldAreaAsset FindArea(string id)
        {
            id = NormalizeMapId(id);
            if (startingArea != null && startingArea.definition.id == id) return startingArea;
            foreach (WorldAreaAsset area in availableAreas) if (area != null && area.definition.id == id) return area;
            // Resolve legacy saves without adding the superseded forest entrance.
            if (id == OrioDaisRoom.MapId) return OrioDaisRoom.GetOrCreateArea();
            // Secret rooms are built in code, like the legacy hall, so a save made in one restores.
            if (SecretRooms.TryGetArea(id, out WorldAreaAsset secret)) return secret;
            return null;
        }
        private GridPortal FindLivePortal(PortalContentAsset target)
        {
            if (CurrentArea != null && areaInstances.TryGetValue(CurrentArea, out GameObject root))
                foreach (GridPortal portal in root.GetComponentsInChildren<GridPortal>(true)) if (portal.Content == target) return portal;
            foreach (GridPortal portal in FindObjectsByType<GridPortal>(FindObjectsInactive.Exclude)) if (portal.Content == target) return portal;
            return null;
        }

        // The grid is shared by every area. While the next area's obstacles register, the player would still occupy their
        // old cell and be reported as blocked by whatever stands there in the new area, so every load takes them off the
        // grid; the spawn, the portal arrival or the saved cell then places them again.
        private void LiftOffGrid(GridMover player)
        {
            if (player != null && world != null && player.World == world && !player.IsMoving) world.UnregisterMover(player);
        }

        private void ApplySpawn(MapDefinition definition, bool placeAtSpawn)
        {
            if (world == null || definition == null || definition.spawn == null) return;
            GridMover player = FindAnyObjectByType<GridMover>();
            if (player != null && player.GetComponent<SpellCaster>() == null) player.gameObject.AddComponent<SpellCaster>();
            if (placeAtSpawn && player != null && !player.TryTeleportTo(world, new Vector2Int(definition.spawn.x, definition.spawn.y)))
            {
                Debug.LogWarning($"Map '{definition.id}' spawn is unavailable; keeping player position.", this);
                // Rejoin the grid where they stand, since the load took them off it.
                player.TryTeleportTo(world, player.CurrentCell);
            }
        }
    }
}
