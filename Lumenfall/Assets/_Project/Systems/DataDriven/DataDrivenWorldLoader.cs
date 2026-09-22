using System;
using System.Collections.Generic;
using UnityEngine;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Spells;

namespace Lumenfall.DataDriven
{
    /// Loads a map and NPC TextAsset at runtime. JSON is preferred; text maps use one object per line:
    /// `building,x,y,width,height,asset` and `portal,x,y,destinationScene,destinationPortalId`.
    public sealed class DataDrivenWorldLoader : MonoBehaviour, ISaveStateProvider
    {
        public static DataDrivenWorldLoader Instance { get; private set; }
        [SerializeField] private TextAsset map;
        [SerializeField] private TextAsset npcs;
        [SerializeField] private Transform contentRoot;
        [SerializeField] private GameObject defaultBuilding;
        [SerializeField] private GameObject defaultNpc;
        [SerializeField] private GridWorld world;
        private readonly List<GameObject> spawned = new List<GameObject>();
        private string npcStateSignature;
        public MapDefinition LoadedMap { get; private set; }
        public NpcDatabaseDefinition LoadedNpcs { get; private set; }

        public string SaveKey => "data-driven-world";
        private void Awake() { if (Instance != null && Instance != this) { Destroy(gameObject); return; } Instance = this; DontDestroyOnLoad(gameObject); GameSaveManager.Instance.RegisterProvider(this); }
        private void Start() { if (world == null) world = FindAnyObjectByType<GridWorld>(); if (map == null) map = Resources.Load<TextAsset>("Maps/campus") ?? Resources.Load<TextAsset>("Maps/map"); if (npcs == null) npcs = Resources.Load<TextAsset>("NPCs/npcs"); Load(map, npcs); }
        private void Update() { if (LoadedNpcs == null || GameEventManager.Instance == null) return; System.Text.StringBuilder state = new System.Text.StringBuilder(); foreach (NpcDefinition n in LoadedNpcs.npcs ?? Array.Empty<NpcDefinition>()) state.Append(GameEventManager.Instance.Runtime.GetQuestStatus(n.showWhenQuestCompleted)).Append(GameEventManager.Instance.Runtime.GetQuestStatus(n.hideWhenQuestCompleted)); string current = state.ToString(); if (npcStateSignature != current) { npcStateSignature = current; RebuildNpcs(); } }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap() { if (FindAnyObjectByType<DataDrivenWorldLoader>() == null) { GameObject go = new GameObject("[DataDrivenWorldLoader]"); go.AddComponent<DataDrivenWorldLoader>(); go.AddComponent<DialogueOverlay>(); } }
        public void Load(TextAsset mapAsset, TextAsset npcAsset)
        {
            if (world == null) world = FindAnyObjectByType<GridWorld>();
            Clear(); map = mapAsset; npcs = npcAsset;
            if (map != null)
            {
                if (TryParseMap(map, out MapDefinition parsedMap, out string mapError)) { LoadedMap = parsedMap; WorldStateService.Instance?.BeginMapRebuild(LoadedMap.id); WorldStateService.Instance?.RegisterPuzzleTargets(LoadedMap.puzzleTargets); if (world != null) world.ConfigureBounds(LoadedMap.width, LoadedMap.height); BuildMap(LoadedMap); ApplySpawn(LoadedMap); ValidatePortals(LoadedMap); WorldStateService.Instance?.EndMapRebuild(); }
                else Debug.LogError(mapError, this);
            }
            if (npcAsset != null)
            {
                if (TryParseNpcs(npcAsset, out NpcDatabaseDefinition parsedNpcs, out string npcError)) { LoadedNpcs = parsedNpcs; ValidateNpcLayers(LoadedNpcs); BuildNpcs(LoadedNpcs); }
                else Debug.LogError(npcError, this);
            }
        }
        public void Clear() { foreach (GameObject item in spawned) if (item != null) { item.SetActive(false); Destroy(item); } spawned.Clear(); LoadedMap = null; LoadedNpcs = null; }

        /// <summary>Restores the authored starting map and NPC set for a new game.</summary>
        public void ResetToStartingState()
        {
            WorldStateService.Instance?.ResetAllState();
            TextAsset startingMap = Resources.Load<TextAsset>("Maps/campus") ?? Resources.Load<TextAsset>("Maps/map");
            TextAsset startingNpcs = Resources.Load<TextAsset>("NPCs/npcs");
            Load(startingMap, startingNpcs);
        }

        public string CaptureStateJson()
        {
            GridMover player = FindAnyObjectByType<GridMover>();
            return JsonUtility.ToJson(new SavedWorldState
            {
                mapId = LoadedMap == null ? string.Empty : LoadedMap.id,
                playerX = player == null ? 0 : player.CurrentCell.x,
                playerY = player == null ? 0 : player.CurrentCell.y
            });
        }

        public bool RestoreStateJson(string json)
        {
            SavedWorldState state = JsonUtility.FromJson<SavedWorldState>(json);
            if (state == null || string.IsNullOrWhiteSpace(state.mapId)) return false;
            TextAsset savedMap = Resources.Load<TextAsset>("Maps/" + state.mapId) ?? Resources.Load<TextAsset>("Maps/" + state.mapId + ".json");
            if (savedMap == null) return false;
            Load(savedMap, npcs == null ? Resources.Load<TextAsset>("NPCs/npcs") : npcs);
            GridMover player = FindAnyObjectByType<GridMover>();
            return player == null || world == null || player.TryTeleportTo(world, new Vector2Int(state.playerX, state.playerY));
        }

        [Serializable]
        private sealed class SavedWorldState
        {
            public string mapId;
            public int playerX;
            public int playerY;
        }
        /// Switches an area in the current scene. The player remains alive while generated content is rebuilt.
        public bool SwitchArea(string mapId, string portalId, Vector2Int arrivalCell)
        {
            GridMover player = FindAnyObjectByType<GridMover>();
            if (player == null || world == null) { Debug.LogWarning("Cannot switch area before player and GridWorld are ready.", this); return false; }
            TextAsset previousMap = map; Vector2Int previousCell = player.CurrentCell;
            TextAsset next = Resources.Load<TextAsset>("Maps/" + mapId);
            if (next == null) next = Resources.Load<TextAsset>("Maps/" + mapId + ".json");
            if (next == null) { Debug.LogWarning($"No runtime map asset registered for '{mapId}'.", this); return false; }
            Load(next, npcs);
            GridPortal target = GridPortal.FindById(portalId) ?? world.GetPortalAt(arrivalCell);
            if (player.TryTeleportTo(world, arrivalCell, target)) return true;
            if (previousMap != null) { Load(previousMap, npcs); player.TryTeleportTo(world, previousCell, GridPortal.FindById(portalId)); }
            return false;
        }
        public bool HasArea(string mapId) => Resources.Load<TextAsset>("Maps/" + mapId) != null || Resources.Load<TextAsset>("Maps/" + mapId + ".json") != null;
        private void BuildMap(MapDefinition definition)
        {
            Transform root = contentRoot == null ? transform : contentRoot;
            if (!string.IsNullOrWhiteSpace(definition.tileFile))
            {
                string tileKey = definition.tileFile.Replace("\\", "/");
                int slash = tileKey.LastIndexOf('/'); if (slash >= 0) tileKey = tileKey.Substring(slash + 1);
                if (tileKey.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) tileKey = tileKey.Substring(0, tileKey.Length - 4);
                TextAsset tiles = Resources.Load<TextAsset>("Maps/" + tileKey);
                if (tiles != null) AddTileObjects(ParseTileObjects(tiles, definition.width, definition.height), root);
                else Debug.LogWarning($"Map '{definition.id}' references missing tileFile '{definition.tileFile}'.", this);
            }
            AddBuilding(definition.B01, "B01", root); AddBuilding(definition.B02, "B02", root); AddBuilding(definition.B03, "B03", root); AddBuilding(definition.B04, "B04", root);
            foreach (MapObjectDefinition item in definition.objects ?? Array.Empty<MapObjectDefinition>())
            { GameObject prefab = LoadPrefab(item.asset, defaultBuilding); GameObject go = Instantiate(prefab, world != null ? world.CellToWorld(new Vector2Int(item.x, item.y), item.elevation) : transform.position, Quaternion.Euler(0, item.rotation, 0), root); go.name = string.IsNullOrWhiteSpace(item.id) ? item.type + "_" + item.x + "_" + item.y : item.id; if (item.blocks && world != null) { GridObstacle obstacle = go.GetComponent<GridObstacle>() ?? go.AddComponent<GridObstacle>(); obstacle.Configure(world, new Vector2Int(item.width, item.height)); } WorldStateService.Instance?.RegisterObject(go.name, go, new Vector2Int(item.x, item.y), new Vector2Int(item.width, item.height), item.compatibleWithSpells, item.tags); spawned.Add(go); }
            foreach (PortalDefinition item in definition.portals ?? Array.Empty<PortalDefinition>())
            { if (item.reserved) continue; if (!string.IsNullOrEmpty(definition.portalGlyph) && !HasTileGlyph(definition, item.x, item.y, definition.portalGlyph[0])) { Debug.LogWarning($"Map '{definition.id}' portal '{item.id}' does not bind to glyph '{definition.portalGlyph}' at ({item.x},{item.y}); skipped.", this); continue; } GameObject go = new GameObject(string.IsNullOrWhiteSpace(item.id) ? "Portal" : item.id); go.transform.SetParent(root); go.transform.position = world != null ? world.CellToWorld(new Vector2Int(item.x, item.y)) : transform.position; GridPortal portal = go.AddComponent<GridPortal>(); portal.Configure(item.id, item.width, item.height, item.destinationScene, item.destinationPortalId, new Vector2Int(item.arrivalOffsetX, item.arrivalOffsetY)); spawned.Add(go); }
        }
        private bool HasTileGlyph(MapDefinition definition, int x, int y, char glyph)
        { string key = definition.tileFile ?? definition.id; if (key.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) key = key.Substring(0, key.Length - 4); TextAsset asset = Resources.Load<TextAsset>("Maps/" + key); if (asset == null) return false; string[] rows = (asset.text ?? string.Empty).Replace("\r", "").Split('\n'); return y >= 0 && y < rows.Length && x >= 0 && x < rows[y].Length && rows[y][x] == glyph; }
        private void ValidatePortals(MapDefinition definition)
        {
            if (definition == null || world == null) return;
            if (!string.IsNullOrEmpty(definition.portalGlyph)) Debug.LogWarning($"Map '{definition.id}' declares portal glyph '{definition.portalGlyph}'; unresolved glyph cells are reported by the pending portal manifest.", this);
            foreach (PortalDefinition item in definition.portals ?? Array.Empty<PortalDefinition>())
            {
                Vector2Int cell = new Vector2Int(item.x, item.y);
                if (!world.IsWithinBounds(cell))
                    Debug.LogError($"Map '{definition.id}' portal '{item.id}' is outside bounds at {cell}.", this);
                else if (world.IsCellBlocked(cell))
                    Debug.LogWarning($"Map '{definition.id}' portal '{item.id}' is on a blocked cell at {cell}.", this);
                if (string.IsNullOrWhiteSpace(item.destinationScene) || string.IsNullOrWhiteSpace(item.destinationPortalId))
                    Debug.LogError($"Map '{definition.id}' portal '{item.id}' has an incomplete destination.", this);
            }
        }
        private void ApplySpawn(MapDefinition definition)
        {
            if (world == null || definition == null || definition.spawn == null) return;
            GridMover player = FindAnyObjectByType<GridMover>();
            if (player != null && player.GetComponent<SpellCaster>() == null) player.gameObject.AddComponent<SpellCaster>();
            if (player != null && !player.TryTeleportTo(world, new Vector2Int(definition.spawn.x, definition.spawn.y)))
                Debug.LogWarning($"Map '{definition.id}' spawn ({definition.spawn.x},{definition.spawn.y}) is unavailable; keeping player position.", this);
        }
        private void AddTileObjects(IEnumerable<MapObjectDefinition> items, Transform root)
        { foreach (MapObjectDefinition item in items) { GameObject prefab = LoadPrefab(item.asset, defaultBuilding); if (prefab == null) continue; GameObject go = Instantiate(prefab, world != null ? world.CellToWorld(new Vector2Int(item.x, item.y)) : transform.position, Quaternion.identity, root); go.name = item.type + "_tile_" + item.x + "_" + item.y; if (world != null) { GridObstacle obstacle = go.GetComponent<GridObstacle>() ?? go.AddComponent<GridObstacle>(); obstacle.Configure(world, new Vector2Int(Mathf.Max(1, item.width), Mathf.Max(1, item.height))); } WorldStateService.Instance?.RegisterObject(go.name, go, new Vector2Int(item.x, item.y), new Vector2Int(item.width, item.height), item.compatibleWithSpells, item.tags); spawned.Add(go); } }
        private static IEnumerable<MapObjectDefinition> ParseTileObjects(TextAsset asset, int width, int height)
        { string[] rows = (asset.text ?? string.Empty).Replace("\r", "").Split('\n'); bool[,] used = new bool[Mathf.Max(1, width), Mathf.Max(1, height)]; for (int y = 0; y < rows.Length && y < height; y++) { if (rows[y].Length != width) Debug.LogWarning($"Tile file '{asset.name}' row {y} has length {rows[y].Length}; expected {width}; clamping safely."); int limit = Mathf.Min(width, rows[y].Length); for (int x = 0; x < limit; x++) if (!used[x,y] && (rows[y][x] == 'W' || rows[y][x] == 'B' || rows[y][x] == 'T')) { int w = 1, h = 1; if (rows[y][x] == 'T' && x + 1 < limit && y + 1 < rows.Length && x + 1 < rows[y + 1].Length && rows[y][x + 1] == 'T' && rows[y + 1][x] == 'T' && rows[y + 1][x + 1] == 'T') { w = h = 2; used[x,y] = used[x+1,y] = used[x,y+1] = used[x+1,y+1] = true; } yield return new MapObjectDefinition { type = rows[y][x].ToString(), x = x, y = y, width = w, height = h, blocks = true }; } } }
        private void AddBuilding(BuildingPlacementDefinition item, string id, Transform root) { if (item == null) return; GameObject prefab = LoadPrefab(id, defaultBuilding); GameObject go = Instantiate(prefab, world != null ? world.CellToWorld(new Vector2Int(item.x, item.y)) : transform.position, Quaternion.identity, root); go.name = id; if (world != null) { GridObstacle obstacle = go.GetComponent<GridObstacle>() ?? go.AddComponent<GridObstacle>(); obstacle.Configure(world, new Vector2Int(item.width, item.height)); } WorldStateService.Instance?.RegisterObject(id, go, new Vector2Int(item.x, item.y), new Vector2Int(item.width, item.height), true, Array.Empty<string>()); spawned.Add(go); }
        private void RebuildNpcs() { for (int i = spawned.Count - 1; i >= 0; i--) { if (spawned[i] != null && spawned[i].GetComponent<DynamicNpc>() != null) { Destroy(spawned[i]); spawned.RemoveAt(i); } } BuildNpcs(LoadedNpcs); }
        private void BuildNpcs(NpcDatabaseDefinition definition)
        { Transform root = contentRoot == null ? transform : contentRoot; foreach (NpcDefinition item in definition.npcs ?? Array.Empty<NpcDefinition>()) { if (!string.IsNullOrWhiteSpace(item.mapId) && !string.Equals(item.mapId, LoadedMap == null ? null : LoadedMap.id, StringComparison.OrdinalIgnoreCase)) continue; if (!NpcIsVisible(item)) continue; Vector2Int cell = ResolveLayerCell(item); GameObject prefab = LoadPrefab(item.asset, defaultNpc); Vector3 position = world != null ? world.CellToWorld(cell) : transform.position; GameObject go = Instantiate(prefab, position, Quaternion.identity, root); go.name = string.IsNullOrWhiteSpace(item.displayName) ? item.id : item.displayName; DynamicNpc npc = go.GetComponent<DynamicNpc>() ?? go.AddComponent<DynamicNpc>(); npc.Configure(item, SelectDialogue(item)); spawned.Add(go); } }
        private bool NpcIsVisible(NpcDefinition item) { if (GameEventManager.Instance == null) return true; if (!string.IsNullOrWhiteSpace(item.showWhenQuestCompleted) && GameEventManager.Instance.Runtime.GetQuestStatus(item.showWhenQuestCompleted) != QuestStatus.Completed) return false; return string.IsNullOrWhiteSpace(item.hideWhenQuestCompleted) || GameEventManager.Instance.Runtime.GetQuestStatus(item.hideWhenQuestCompleted) != QuestStatus.Completed; }
        private Vector2Int ResolveLayerCell(NpcDefinition item)
        {
            Vector2Int fallback = new Vector2Int(item.x, item.y);
            if (string.IsNullOrWhiteSpace(item.layerSymbol) || LoadedNpcs == null || LoadedMap == null) return fallback;
            foreach (NpcLayerDefinition layer in LoadedNpcs.layers ?? Array.Empty<NpcLayerDefinition>())
            {
                if (!string.Equals(layer.mapId, LoadedMap.id, StringComparison.OrdinalIgnoreCase)) continue;
                string key = layer.tileFile.Replace("\\", "/"); if (key.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) key = key.Substring(0, key.Length - 4); int slash = key.LastIndexOf('/'); if (slash >= 0) key = key.Substring(slash + 1);
                TextAsset asset = Resources.Load<TextAsset>("NPCs/" + key); if (asset == null) continue;
                string[] rows = (asset.text ?? string.Empty).Replace("\r", "").Split('\n');
                for (int y = 0; y < rows.Length; y++) { int x = rows[y].IndexOf(item.layerSymbol, StringComparison.Ordinal); if (x >= 0) return new Vector2Int(x, y); }
            }
            return fallback;
        }
        private static GameObject LoadPrefab(string path, GameObject fallback) {
            if (string.IsNullOrWhiteSpace(path)) return fallback ?? CreateRuntimeFallback("unresolved");
            GameObject found = Resources.Load<GameObject>(path);
            if (found != null) return found;
            found = Resources.Load<GameObject>("Buildings/" + path) ?? Resources.Load<GameObject>("NPCs/" + path);
            // Data files may contain authoring asset keys; resolve only registered Resources prefabs.
            string key = path.Replace("\\", "/");
            return Resources.Load<GameObject>(key.TrimStart('/')) ?? fallback ?? CreateRuntimeFallback(path);
        }
        private void ValidateNpcLayers(NpcDatabaseDefinition definition)
        {
            foreach (NpcLayerDefinition layer in definition.layers ?? Array.Empty<NpcLayerDefinition>())
            {
                if (string.IsNullOrWhiteSpace(layer.mapId) || string.IsNullOrWhiteSpace(layer.tileFile)) continue;
                string layerKey = layer.tileFile.Replace("\\", "/");
                if (layerKey.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) layerKey = layerKey.Substring(0, layerKey.Length - 4);
                int slash = layerKey.LastIndexOf('/'); if (slash >= 0) layerKey = layerKey.Substring(slash + 1);
                TextAsset asset = Resources.Load<TextAsset>("NPCs/" + layerKey);
                if (asset == null) { Debug.LogError($"NPC layer '{layer.tileFile}' for map '{layer.mapId}' is missing.", this); continue; }
                string[] rows = (asset.text ?? string.Empty).Replace("\r", "").Split('\n');
                int expectedWidth = layer.width > 0 ? layer.width : (LoadedMap != null && layer.mapId == LoadedMap.id ? LoadedMap.width : 0);
                int expectedHeight = layer.height > 0 ? layer.height : (LoadedMap != null && layer.mapId == LoadedMap.id ? LoadedMap.height : 0);
                if (expectedWidth > 0 && expectedHeight > 0 && (rows.Length < expectedHeight || rows[0].Length != expectedWidth))
                    Debug.LogError($"NPC layer '{layer.tileFile}' shape {rows[0].Length}x{rows.Length} does not match {expectedWidth}x{expectedHeight}.", this);
            }
        }
        private static GameObject CreateRuntimeFallback(string key)
        { GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = "RuntimeAsset_" + key; go.transform.localScale = new Vector3(1f, 0.5f, 1f); return go; }
        public static bool TryParseMap(TextAsset asset, out MapDefinition result, out string error) { result = null; error = null; if (asset == null) { error = "Map asset is null."; return false; } try { string source = asset.text ?? string.Empty; bool manifest = source.IndexOf("lumenfall.tile-layout", StringComparison.OrdinalIgnoreCase) >= 0 || source.IndexOf("\"maps\"", StringComparison.OrdinalIgnoreCase) >= 0; if (manifest) { MapManifestDefinition parsedManifest = JsonUtility.FromJson<MapManifestDefinition>(source); result = ManifestToMap(parsedManifest); return result != null; } result = JsonUtility.FromJson<MapDefinition>(source); if (result == null) throw new Exception("empty definition"); return true; } catch { try { result = ParseTextMap(asset.text); } catch (Exception ex) { error = $"Could not parse map '{asset.name}': {ex.Message}"; return false; } return result != null || (error = $"Could not parse map '{asset.name}'.") == null; } }
        private static MapDefinition ManifestToMap(MapManifestDefinition manifest) { if (manifest == null) return null; MapDefinition map = new MapDefinition { id = "manifest" }; List<PortalDefinition> portals = new List<PortalDefinition>(); foreach (MapAreaDefinition area in manifest.areas ?? Array.Empty<MapAreaDefinition>()) foreach (ManifestPortalDefinition item in area.portals ?? Array.Empty<ManifestPortalDefinition>()) if (item.cell != null) portals.Add(new PortalDefinition { id = item.id, x = item.cell.x, y = item.cell.y, destinationScene = item.destination == null ? "" : item.destination.mapId, destinationPortalId = item.destination == null ? "" : item.destination.regionId, arrivalOffsetX = item.destination == null || item.destination.cell == null ? 0 : item.destination.cell.x, arrivalOffsetY = item.destination == null || item.destination.cell == null ? 0 : item.destination.cell.y }); map.portals = portals.ToArray(); return map; }
        private static MapDefinition ParseTextMap(string text) { MapDefinition map = new MapDefinition(); List<MapObjectDefinition> objects = new List<MapObjectDefinition>(); List<PortalDefinition> portals = new List<PortalDefinition>(); string[] lines = (text ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries); for (int y = 0; y < lines.Length; y++) { string raw = lines[y]; string[] p = raw.Split(','); if (p.Length >= 3 && !raw.TrimStart().StartsWith("#")) { if (p[0].Trim().Equals("portal", StringComparison.OrdinalIgnoreCase)) portals.Add(new PortalDefinition { id = p.Length > 5 ? p[5].Trim() : "", x = int.Parse(p[1]), y = int.Parse(p[2]), destinationScene = p.Length > 3 ? p[3].Trim() : "", destinationPortalId = p.Length > 4 ? p[4].Trim() : "" }); else objects.Add(new MapObjectDefinition { type = p[0].Trim(), x = int.Parse(p[1]), y = int.Parse(p[2]), width = p.Length > 3 ? int.Parse(p[3]) : 1, height = p.Length > 4 ? int.Parse(p[4]) : 1, asset = p.Length > 5 ? p[5].Trim() : "" }); continue; } for (int x = 0; x < raw.Length; x++) if (raw[x] == 'W' || raw[x] == 'B' || raw[x] == 'T') objects.Add(new MapObjectDefinition { type = raw[x].ToString(), x = x, y = y, asset = "", blocks = true }); } map.width = lines.Length == 0 ? 0 : lines[0].Length; map.height = lines.Length; map.objects = objects.ToArray(); map.portals = portals.ToArray(); return objects.Count == 0 && portals.Count == 0 ? null : map; }
        public static bool TryParseNpcs(TextAsset asset, out NpcDatabaseDefinition result, out string error) { result = null; error = null; try { result = JsonUtility.FromJson<NpcDatabaseDefinition>(asset.text); return result != null; } catch (Exception ex) { error = $"Could not parse NPC asset '{asset.name}': {ex.Message}"; return false; } }
        private static string SelectDialogue(NpcDefinition npc) { List<string> options = new List<string>(npc.dialogueIds ?? Array.Empty<string>()); foreach (NpcDialogueDefinition item in npc.dialogue ?? Array.Empty<NpcDialogueDefinition>()) { bool questOk = string.IsNullOrWhiteSpace(item.questId) || (GameEventManager.Instance != null && GameEventManager.Instance.Runtime.GetQuestStatus(item.questId).ToString().Equals(item.requiredQuestStatus, StringComparison.OrdinalIgnoreCase)); bool prerequisiteOk = string.IsNullOrWhiteSpace(item.prerequisiteQuestId) || (GameEventManager.Instance != null && GameEventManager.Instance.Runtime.GetQuestStatus(item.prerequisiteQuestId).ToString().Equals(item.prerequisiteQuestStatus, StringComparison.OrdinalIgnoreCase)); if (questOk && prerequisiteOk) for (int i = 0; i < Mathf.Max(1, item.weight); i++) options.Add(item.dialogueId); } return options.Count == 0 ? null : options[UnityEngine.Random.Range(0, options.Count)]; }
    }
}
