using System;
using System.Collections.Generic;
using UnityEngine;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Spells;

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
        public MapDefinition LoadedMap { get; private set; }
        public NpcDatabaseDefinition LoadedNpcs { get; private set; }
        public WorldAreaAsset CurrentArea { get; private set; }
        public string SaveKey => "data-driven-world";

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            if (GameSaveManager.Instance != null) GameSaveManager.Instance.RegisterProvider(this);
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
            if (area == null || area.areaPrefab == null) { Debug.LogError("Assign an authored area prefab to this World Area asset.", this); return; }
            if (world == null) world = FindAnyObjectByType<GridWorld>();
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
            foreach (DynamicNpc npc in root.GetComponentsInChildren<DynamicNpc>(true)) npc.RefreshVisibility();
            ApplySpawn(LoadedMap);
            WorldStateService.Instance?.EndMapRebuild();
        }

        public void Clear()
        {
            foreach (GameObject item in areaInstances.Values) if (item != null) { item.SetActive(false); Destroy(item); }
            areaInstances.Clear(); LoadedMap = null; LoadedNpcs = null; CurrentArea = null;
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

        public bool RestoreStateJson(string json)
        {
            SavedWorldState state = JsonUtility.FromJson<SavedWorldState>(json);
            WorldAreaAsset area = FindArea(NormalizeMapId(state == null ? null : state.mapId));
            if (area == null) return false;
            Load(area, npcCatalog);
            GridMover player = FindAnyObjectByType<GridMover>();
            return player == null || world == null || player.TryTeleportTo(world, new Vector2Int(state.playerX, state.playerY));
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
            Load(destinationPortal.area, npcCatalog);
            GridPortal target = FindLivePortal(destinationPortal);
            Vector2Int arrival = destinationPortal.cell + sourcePortal.arrivalOffset + sourceEntryOffset;
            if (player.TryTeleportTo(world, arrival, target)) return true;
            if (previousArea != null) { Load(previousArea, npcCatalog); player.TryTeleportTo(world, previousCell); }
            return false;
        }

        private static string NormalizeMapId(string id)
        {
            return string.Equals(id, "eastForest", StringComparison.OrdinalIgnoreCase) ? "westForest" : id;
        }

        private WorldAreaAsset FindArea(string id)
        {
            if (startingArea != null && startingArea.definition.id == id) return startingArea;
            foreach (WorldAreaAsset area in availableAreas) if (area != null && area.definition.id == id) return area;
            return null;
        }
        private GridPortal FindLivePortal(PortalContentAsset target)
        {
            if (CurrentArea != null && areaInstances.TryGetValue(CurrentArea, out GameObject root))
                foreach (GridPortal portal in root.GetComponentsInChildren<GridPortal>(true)) if (portal.Content == target) return portal;
            foreach (GridPortal portal in FindObjectsByType<GridPortal>(FindObjectsInactive.Exclude)) if (portal.Content == target) return portal;
            return null;
        }

        private void ApplySpawn(MapDefinition definition)
        {
            if (world == null || definition == null || definition.spawn == null) return;
            GridMover player = FindAnyObjectByType<GridMover>();
            if (player != null && player.GetComponent<SpellCaster>() == null) player.gameObject.AddComponent<SpellCaster>();
            if (player != null && !player.TryTeleportTo(world, new Vector2Int(definition.spawn.x, definition.spawn.y)))
                Debug.LogWarning($"Map '{definition.id}' spawn is unavailable; keeping player position.", this);
        }
    }
}
