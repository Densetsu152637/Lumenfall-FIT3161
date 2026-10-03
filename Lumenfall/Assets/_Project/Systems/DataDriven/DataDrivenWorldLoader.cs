using System;
using System.Collections.Generic;
using UnityEngine;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Spells;
using Lumenfall.Quests;

namespace Lumenfall.DataDriven
{
    /// <summary>Instantiates inspector-authored world area and NPC assets.</summary>
    public sealed class DataDrivenWorldLoader : MonoBehaviour, ISaveStateProvider
    {
        public static DataDrivenWorldLoader Instance { get; private set; }
        [SerializeField] private WorldAreaAsset startingArea;
        [SerializeField] private NpcCatalogAsset npcCatalog;
        [SerializeField] private WorldAreaAsset[] availableAreas = Array.Empty<WorldAreaAsset>();
        [SerializeField] private Transform contentRoot;
        [SerializeField] private GridWorld world;
        private readonly Dictionary<WorldAreaAsset, GameObject> areaInstances = new Dictionary<WorldAreaAsset, GameObject>();
        private string npcStateSignature;
        private bool changingArea;
        private GameSaveManager saveManager;
        private bool registeredSaveProvider;
        public MapDefinition LoadedMap { get; private set; }
        public NpcDatabaseDefinition LoadedNpcs { get; private set; }
        public WorldAreaAsset CurrentArea { get; private set; }
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
            if (registeredSaveProvider && saveManager != null) saveManager.UnregisterProvider(SaveKey);
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            if (CurrentArea != null) return; // A save or explicit Load may arrive before Start.
            if (world == null) world = FindAnyObjectByType<GridWorld>();
            if (startingArea == null) { Debug.LogError("Assign a starting World Area asset in the Inspector.", this); return; }
            Load(startingArea, npcCatalog);
        }

        private void Update()
        {
            if (LoadedNpcs == null || GameEventManager.Instance == null) return;
            System.Text.StringBuilder state = new System.Text.StringBuilder();
            foreach (NpcDefinition n in LoadedNpcs.npcs ?? Array.Empty<NpcDefinition>())
                state.Append(GameEventManager.Instance.Runtime.GetQuestStatus(n.showWhenQuestCompleted))
                    .Append(GameEventManager.Instance.Runtime.GetQuestStatus(n.hideWhenQuestCompleted));
            string current = state.ToString();
            if (npcStateSignature != current)
            {
                npcStateSignature = current;
                GameObject activeArea;
                if (CurrentArea != null && areaInstances.TryGetValue(CurrentArea, out activeArea))
                    foreach (DynamicNpc npc in activeArea.GetComponentsInChildren<DynamicNpc>(true)) npc.RefreshVisibility();
            }
        }

        public void Load(WorldAreaAsset area, NpcCatalogAsset catalog)
        {
            GridMover player = FindPlayer();
            Vector2Int? spawn = area != null && area.definition != null && area.definition.spawn != null
                ? new Vector2Int(area.definition.spawn.x, area.definition.spawn.y)
                : (Vector2Int?)null;
            if (!TryChangeArea(area, catalog, player, spawn, null, Vector2Int.zero, Vector2Int.zero))
                Debug.LogWarning("The authored area could not load; keeping the previous area and player position.", this);
        }

        public void Clear()
        {
            foreach (GameObject item in areaInstances.Values) if (item != null) { item.SetActive(false); Destroy(item); }
            areaInstances.Clear(); LoadedMap = null; LoadedNpcs = null; CurrentArea = null; npcStateSignature = null;
        }

        public void ResetToStartingState()
        {
            TryResetToStartingState();
        }

        public bool TryResetToStartingState()
        {
            if (startingArea == null || startingArea.definition == null || startingArea.definition.spawn == null) return false;
            GridMover player = FindPlayer();
            if (player == null || player.IsMoving) return false;
            SpawnDefinition spawn = startingArea.definition.spawn;
            return TryChangeArea(startingArea, npcCatalog, player, new Vector2Int(spawn.x, spawn.y),
                null, Vector2Int.zero, Vector2Int.zero, true);
        }

        [Serializable] private sealed class SavedWorldState { public string mapId; public int playerX; public int playerY; }
        public string CaptureStateJson()
        {
            GridMover player = FindPlayer();
            return JsonUtility.ToJson(new SavedWorldState { mapId = LoadedMap == null ? string.Empty : LoadedMap.id,
                playerX = player == null ? 0 : player.CurrentCell.x, playerY = player == null ? 0 : player.CurrentCell.y });
        }

        public bool RestoreStateJson(string json)
        {
            SavedWorldState state;
            try { state = JsonUtility.FromJson<SavedWorldState>(json); }
            catch (ArgumentException) { return false; }
            WorldAreaAsset area = FindArea(NormalizeMapId(state == null ? null : state.mapId));
            GridMover player = FindPlayer();
            if (area == null || player == null || player.IsMoving) return false;
            return TryChangeArea(area, npcCatalog, player, new Vector2Int(state.playerX, state.playerY),
                null, Vector2Int.zero, Vector2Int.zero);
        }

        public bool SwitchArea(PortalContentAsset sourcePortal, Vector2Int sourceEntryOffset)
        {
            if (world == null) world = FindAnyObjectByType<GridWorld>();
            GridMover player = FindPlayer();
            if (changingArea || player == null || !player.isActiveAndEnabled || player.IsMoving ||
                player.IsMovementLocked || player.World != world || CurrentArea == null ||
                sourcePortal == null || sourcePortal.reserved || sourcePortal.area != CurrentArea ||
                sourcePortal.destination == null || sourcePortal.destination.reserved) return false;

            GridPortal source = FindLivePortal(sourcePortal);
            if (source == null || !source.ContainsCell(player.CurrentCell) ||
                world.GetPortalAt(player.CurrentCell) != source) return false;
            PortalContentAsset destination = sourcePortal.destination;
            if (!IsAvailableArea(destination.area)) return false;

            // Callers cannot fabricate an entry offset different from the tile the player entered.
            Vector2Int actualOffset = player.CurrentCell - source.FirstCell;
            if (sourceEntryOffset != Vector2Int.zero && sourceEntryOffset != actualOffset) return false;
            return TryChangeArea(destination.area, npcCatalog, player, null, destination,
                sourceEntryOffset, sourcePortal.arrivalOffset);
        }

        private bool TryChangeArea(WorldAreaAsset area, NpcCatalogAsset catalog, GridMover player,
            Vector2Int? requestedCell, PortalContentAsset destination, Vector2Int entryOffset, Vector2Int arrivalOffset,
            bool resetState = false)
        {
            if (changingArea || area == null || area.areaPrefab == null || area.definition == null ||
                area.definition.width <= 0 || area.definition.height <= 0 ||
                string.IsNullOrWhiteSpace(area.definition.id) ||
                (player != null && (!player.isActiveAndEnabled || player.IsMoving))) return false;
            if (world == null) world = FindAnyObjectByType<GridWorld>();
            if (world == null || (player != null && player.World != world)) return false;

            WorldAreaAsset previousArea = CurrentArea;
            MapDefinition previousMap = LoadedMap;
            NpcDatabaseDefinition previousNpcs = LoadedNpcs;
            string previousSignature = npcStateSignature;
            bool previousRestricted = world.RestrictToBounds;
            Vector2Int previousBoundsMin = world.BoundsMin;
            Vector2Int previousBoundsSize = world.BoundsSize;
            Vector2Int previousCell = player == null ? Vector2Int.zero : player.CurrentCell;
            bool previousLocked = player != null && player.IsMovementLocked;
            WorldStateService state = WorldStateService.Instance;
            string previousStateMap = state == null ? null : state.CurrentMapId;
            bool previousStateReady = state != null && state.IsReady;
            areaInstances.TryGetValue(area, out GameObject previousCachedRoot);
            bool committed = false;
            changingArea = true;
            if (player != null) { player.SetMovementLocked(true); world.UnregisterMover(player); }
            try
            {
                ActivateArea(area, catalog, resetState);
                if (!resetState) state?.PrepareMapRebuild();
                GridPortal target = destination == null ? null : FindLivePortal(destination);
                if (destination != null && target == null) return false;
                Vector2Int cell = target == null
                    ? requestedCell ?? previousCell
                    : target.GetArrivalCell(entryOffset) + arrivalOffset;
                if (player != null)
                {
                    if (!world.IsCellAvailable(cell)) return false;
                    // Suppress whichever portal actually contains the arrival, including saved cells.
                    GridPortal arrivalPortal = world.GetPortalAt(cell);
                    if (!player.TryTeleportTo(world, cell, arrivalPortal)) return false;
                }
                committed = true;
                if (resetState)
                {
                    state?.ResetPreparedMap();
                    if (previousCachedRoot != null) Destroy(previousCachedRoot);
                    // Other cached areas contain mutable transforms from the old game as well.
                    GameObject startingRoot = areaInstances[area];
                    foreach (KeyValuePair<WorldAreaAsset, GameObject> cached in areaInstances)
                        if (cached.Key != area && cached.Value != null)
                        { cached.Value.SetActive(false); Destroy(cached.Value); }
                    areaInstances.Clear();
                    areaInstances.Add(area, startingRoot);
                }
                if (player != null && player.GetComponent<SpellCaster>() == null)
                    player.gameObject.AddComponent<SpellCaster>();
                StellaStringThemTogetherPuzzle.RegisterForMap(LoadedMap.id, world);
                state?.EndMapRebuild();
                return true;
            }
            finally
            {
                if (!committed)
                {
                    if (CurrentArea != null && areaInstances.TryGetValue(CurrentArea, out GameObject failed))
                    {
                        failed.SetActive(false);
                        if (resetState && failed != previousCachedRoot)
                        {
                            Destroy(failed);
                            if (previousCachedRoot != null) areaInstances[area] = previousCachedRoot;
                            else areaInstances.Remove(area);
                        }
                    }
                    CurrentArea = previousArea; LoadedMap = previousMap; LoadedNpcs = previousNpcs;
                    npcStateSignature = previousSignature;
                    world.RestoreBounds(previousRestricted, previousBoundsMin, previousBoundsSize);
                    state?.BeginMapRebuild(previousStateMap);
                    state?.RegisterPuzzleTargets(previousMap == null ? null : previousMap.puzzleTargets);
                    if (previousArea != null && areaInstances.TryGetValue(previousArea, out GameObject previous))
                    {
                        previous.SetActive(true);
                        RefreshNpcs(previous);
                    }
                    state?.PrepareMapRebuild();
                    state?.CancelMapRebuild(previousStateReady);
                    if (player != null && !world.TryRegisterMover(player, previousCell))
                        Debug.LogError("Could not restore player occupancy after rejected area travel.", this);
                }
                if (player != null) player.SetMovementLocked(previousLocked);
                changingArea = false;
            }
        }

        private void ActivateArea(WorldAreaAsset area, NpcCatalogAsset catalog, bool freshInstance)
        {
            if (CurrentArea != null && areaInstances.TryGetValue(CurrentArea, out GameObject previous))
                previous.SetActive(false);
            CurrentArea = area; LoadedMap = area.definition;
            LoadedNpcs = catalog == null ? new NpcDatabaseDefinition() :
                new NpcDatabaseDefinition { npcs = catalog.npcs, layers = catalog.layers };
            npcStateSignature = null;
            WorldStateService.Instance?.BeginMapRebuild(LoadedMap.id);
            WorldStateService.Instance?.RegisterPuzzleTargets(LoadedMap.puzzleTargets);
            world.ConfigureBounds(LoadedMap.width, LoadedMap.height);
            if (!areaInstances.TryGetValue(area, out GameObject root) || root == null || freshInstance)
            {
                // An inactive parent prevents prefab OnEnable from leaking registrations before staging.
                GameObject staging = new GameObject("[Area staging]");
                staging.SetActive(false);
                try
                {
                    root = Instantiate(area.areaPrefab, staging.transform);
                    root.SetActive(false);
                    root.transform.SetParent(contentRoot == null ? transform : contentRoot, false);
                    root.name = area.definition.id + " (Authored Area)";
                    areaInstances[area] = root;
                }
                finally { Destroy(staging); }
            }
            root.SetActive(true);
            RefreshNpcs(root);
        }

        private static void RefreshNpcs(GameObject root)
        {
            foreach (DynamicNpc npc in root.GetComponentsInChildren<DynamicNpc>(true)) npc.RefreshVisibility();
        }

        private static GridMover FindPlayer()
        {
            GridPlayerInput input = FindAnyObjectByType<GridPlayerInput>();
            return input != null ? input.GetComponent<GridMover>() : FindAnyObjectByType<GridMover>();
        }

        private bool IsAvailableArea(WorldAreaAsset area)
        {
            return area != null && (area == startingArea ||
                Array.IndexOf(availableAreas ?? Array.Empty<WorldAreaAsset>(), area) >= 0);
        }

        private static string NormalizeMapId(string id)
        {
            return string.Equals(id, "eastForest", StringComparison.OrdinalIgnoreCase) ? "westForest" : id;
        }

        private WorldAreaAsset FindArea(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            if (startingArea != null && startingArea.definition != null && startingArea.definition.id == id) return startingArea;
            foreach (WorldAreaAsset area in availableAreas ?? Array.Empty<WorldAreaAsset>())
                if (area != null && area.definition != null && area.definition.id == id) return area;
            return null;
        }

        private GridPortal FindLivePortal(PortalContentAsset target)
        {
            if (target == null || target.reserved || target.area != CurrentArea ||
                CurrentArea == null || !areaInstances.TryGetValue(CurrentArea, out GameObject root) || root == null)
                return null;
            foreach (GridPortal portal in root.GetComponentsInChildren<GridPortal>(false))
                if (portal.Content == target && portal.isActiveAndEnabled && portal.World == world &&
                    world.GetPortalAt(portal.FirstCell) == portal) return portal;
            return null;
        }
    }
}
