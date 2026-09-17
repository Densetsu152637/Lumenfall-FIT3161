using System;
using System.Collections.Generic;
using UnityEngine;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;

namespace Lumenfall.DataDriven
{
    /// Loads a map and NPC TextAsset at runtime. JSON is preferred; text maps use one object per line:
    /// `building,x,y,width,height,asset` and `portal,x,y,destinationScene,destinationPortalId`.
    public sealed class DataDrivenWorldLoader : MonoBehaviour
    {
        public static DataDrivenWorldLoader Instance { get; private set; }
        [SerializeField] private TextAsset map;
        [SerializeField] private TextAsset npcs;
        [SerializeField] private Transform contentRoot;
        [SerializeField] private GameObject defaultBuilding;
        [SerializeField] private GameObject defaultNpc;
        [SerializeField] private GridWorld world;
        private readonly List<GameObject> spawned = new List<GameObject>();
        public MapDefinition LoadedMap { get; private set; }
        public NpcDatabaseDefinition LoadedNpcs { get; private set; }

        private void Awake() { if (Instance != null && Instance != this) { Destroy(gameObject); return; } Instance = this; DontDestroyOnLoad(gameObject); }
        private void Start() { if (world == null) world = FindAnyObjectByType<GridWorld>(); if (map == null) map = Resources.Load<TextAsset>("Maps/campus") ?? Resources.Load<TextAsset>("Maps/map"); if (npcs == null) npcs = Resources.Load<TextAsset>("NPCs/npcs"); Load(map, npcs); }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap() { if (FindAnyObjectByType<DataDrivenWorldLoader>() == null) { GameObject go = new GameObject("[DataDrivenWorldLoader]"); go.AddComponent<DataDrivenWorldLoader>(); go.AddComponent<DialogueOverlay>(); } }
        public void Load(TextAsset mapAsset, TextAsset npcAsset)
        {
            if (world == null) world = FindAnyObjectByType<GridWorld>();
            Clear(); map = mapAsset; npcs = npcAsset;
            if (map != null)
            {
                if (TryParseMap(map, out MapDefinition parsedMap, out string mapError)) { LoadedMap = parsedMap; if (world != null) world.ConfigureBounds(LoadedMap.width, LoadedMap.height); ApplySpawn(LoadedMap); BuildMap(LoadedMap); }
                else Debug.LogError(mapError, this);
            }
            if (npcAsset != null)
            {
                if (TryParseNpcs(npcAsset, out NpcDatabaseDefinition parsedNpcs, out string npcError)) { LoadedNpcs = parsedNpcs; BuildNpcs(LoadedNpcs); }
                else Debug.LogError(npcError, this);
            }
        }
        public void Clear() { foreach (GameObject item in spawned) if (item != null) { item.SetActive(false); Destroy(item); } spawned.Clear(); LoadedMap = null; LoadedNpcs = null; }
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
            { GameObject prefab = LoadPrefab(item.asset, defaultBuilding); GameObject go = Instantiate(prefab, world != null ? world.CellToWorld(new Vector2Int(item.x, item.y), item.elevation) : transform.position, Quaternion.Euler(0, item.rotation, 0), root); go.name = string.IsNullOrWhiteSpace(item.id) ? item.type : item.id; if (item.blocks && world != null) { GridObstacle obstacle = go.GetComponent<GridObstacle>() ?? go.AddComponent<GridObstacle>(); obstacle.Configure(world, new Vector2Int(item.width, item.height)); } spawned.Add(go); }
            foreach (PortalDefinition item in definition.portals ?? Array.Empty<PortalDefinition>())
            { GameObject go = new GameObject(string.IsNullOrWhiteSpace(item.id) ? "Portal" : item.id); go.transform.SetParent(root); go.transform.position = world != null ? world.CellToWorld(new Vector2Int(item.x, item.y)) : transform.position; GridPortal portal = go.AddComponent<GridPortal>(); portal.Configure(item.id, item.width, item.height, item.destinationScene, item.destinationPortalId, new Vector2Int(item.arrivalOffsetX, item.arrivalOffsetY)); spawned.Add(go); }
        }
        private void ApplySpawn(MapDefinition definition)
        {
            if (world == null || definition == null || definition.spawn == null) return;
            GridMover player = FindAnyObjectByType<GridMover>();
            if (player != null && !player.TryTeleportTo(world, new Vector2Int(definition.spawn.x, definition.spawn.y)))
                Debug.LogWarning($"Map '{definition.id}' spawn ({definition.spawn.x},{definition.spawn.y}) is unavailable; keeping player position.", this);
        }
        private void AddTileObjects(IEnumerable<MapObjectDefinition> items, Transform root)
        { foreach (MapObjectDefinition item in items) { GameObject prefab = LoadPrefab(item.asset, defaultBuilding); if (prefab == null) continue; GameObject go = Instantiate(prefab, world != null ? world.CellToWorld(new Vector2Int(item.x, item.y)) : transform.position, Quaternion.identity, root); go.name = item.type + "_tile_" + item.x + "_" + item.y; if (world != null) { GridObstacle obstacle = go.GetComponent<GridObstacle>() ?? go.AddComponent<GridObstacle>(); obstacle.Configure(world, new Vector2Int(1, 1)); } spawned.Add(go); } }
        private static IEnumerable<MapObjectDefinition> ParseTileObjects(TextAsset asset, int width, int height)
        { string[] rows = (asset.text ?? string.Empty).Replace("\r", "").Split('\n'); for (int y = 0; y < rows.Length && y < height; y++) { if (rows[y].Length != width) Debug.LogWarning($"Tile file '{asset.name}' row {y} has length {rows[y].Length}; expected {width}; clamping safely."); int limit = Mathf.Min(width, rows[y].Length); for (int x = 0; x < limit; x++) if (rows[y][x] == 'W' || rows[y][x] == 'B' || rows[y][x] == 'T') yield return new MapObjectDefinition { type = rows[y][x].ToString(), x = x, y = y, blocks = true }; } }
        private void AddBuilding(BuildingPlacementDefinition item, string id, Transform root) { if (item == null) return; GameObject prefab = LoadPrefab(id, defaultBuilding); GameObject go = Instantiate(prefab, world != null ? world.CellToWorld(new Vector2Int(item.x, item.y)) : transform.position, Quaternion.identity, root); go.name = id; if (world != null) { GridObstacle obstacle = go.GetComponent<GridObstacle>() ?? go.AddComponent<GridObstacle>(); obstacle.Configure(world, new Vector2Int(item.width, item.height)); } spawned.Add(go); }
        private void BuildNpcs(NpcDatabaseDefinition definition)
        { Transform root = contentRoot == null ? transform : contentRoot; foreach (NpcDefinition item in definition.npcs ?? Array.Empty<NpcDefinition>()) { if (!string.IsNullOrWhiteSpace(item.mapId) && !string.Equals(item.mapId, LoadedMap == null ? null : LoadedMap.id, StringComparison.OrdinalIgnoreCase)) continue; GameObject prefab = LoadPrefab(item.asset, defaultNpc); Vector3 position = world != null ? world.CellToWorld(new Vector2Int(item.x, item.y)) : transform.position; GameObject go = Instantiate(prefab, position, Quaternion.identity, root); go.name = string.IsNullOrWhiteSpace(item.displayName) ? item.id : item.displayName; DynamicNpc npc = go.GetComponent<DynamicNpc>() ?? go.AddComponent<DynamicNpc>(); npc.Configure(item, SelectDialogue(item)); spawned.Add(go); } }
        private static GameObject LoadPrefab(string path, GameObject fallback) {
            if (string.IsNullOrWhiteSpace(path)) return fallback ?? CreateRuntimeFallback("unresolved");
            GameObject found = Resources.Load<GameObject>(path);
            if (found != null) return found;
            found = Resources.Load<GameObject>("Buildings/" + path) ?? Resources.Load<GameObject>("NPCs/" + path);
            // Data files may contain authoring asset keys; resolve only registered Resources prefabs.
            string key = path.Replace("\\", "/");
            return Resources.Load<GameObject>(key.TrimStart('/')) ?? fallback ?? CreateRuntimeFallback(path);
        }
        private static GameObject CreateRuntimeFallback(string key)
        { GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = "RuntimeAsset_" + key; go.transform.localScale = new Vector3(1f, 0.5f, 1f); return go; }
        public static bool TryParseMap(TextAsset asset, out MapDefinition result, out string error) { result = null; error = null; if (asset == null) { error = "Map asset is null."; return false; } try { string source = asset.text ?? string.Empty; bool manifest = source.IndexOf("lumenfall.tile-layout", StringComparison.OrdinalIgnoreCase) >= 0 || source.IndexOf("\"maps\"", StringComparison.OrdinalIgnoreCase) >= 0; if (manifest) { MapManifestDefinition parsedManifest = JsonUtility.FromJson<MapManifestDefinition>(source); result = ManifestToMap(parsedManifest); return result != null; } result = JsonUtility.FromJson<MapDefinition>(source); if (result == null) throw new Exception("empty definition"); return true; } catch { try { result = ParseTextMap(asset.text); } catch (Exception ex) { error = $"Could not parse map '{asset.name}': {ex.Message}"; return false; } return result != null || (error = $"Could not parse map '{asset.name}'.") == null; } }
        private static MapDefinition ManifestToMap(MapManifestDefinition manifest) { if (manifest == null) return null; MapDefinition map = new MapDefinition { id = "manifest" }; List<PortalDefinition> portals = new List<PortalDefinition>(); foreach (MapAreaDefinition area in manifest.areas ?? Array.Empty<MapAreaDefinition>()) foreach (ManifestPortalDefinition item in area.portals ?? Array.Empty<ManifestPortalDefinition>()) if (item.cell != null) portals.Add(new PortalDefinition { id = item.id, x = item.cell.x, y = item.cell.y, destinationScene = item.destination == null ? "" : item.destination.mapId, destinationPortalId = item.destination == null ? "" : item.destination.regionId, arrivalOffsetX = item.destination == null || item.destination.cell == null ? 0 : item.destination.cell.x, arrivalOffsetY = item.destination == null || item.destination.cell == null ? 0 : item.destination.cell.y }); map.portals = portals.ToArray(); return map; }
        private static MapDefinition ParseTextMap(string text) { MapDefinition map = new MapDefinition(); List<MapObjectDefinition> objects = new List<MapObjectDefinition>(); List<PortalDefinition> portals = new List<PortalDefinition>(); string[] lines = (text ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries); for (int y = 0; y < lines.Length; y++) { string raw = lines[y]; string[] p = raw.Split(','); if (p.Length >= 3 && !raw.TrimStart().StartsWith("#")) { if (p[0].Trim().Equals("portal", StringComparison.OrdinalIgnoreCase)) portals.Add(new PortalDefinition { id = p.Length > 5 ? p[5].Trim() : "", x = int.Parse(p[1]), y = int.Parse(p[2]), destinationScene = p.Length > 3 ? p[3].Trim() : "", destinationPortalId = p.Length > 4 ? p[4].Trim() : "" }); else objects.Add(new MapObjectDefinition { type = p[0].Trim(), x = int.Parse(p[1]), y = int.Parse(p[2]), width = p.Length > 3 ? int.Parse(p[3]) : 1, height = p.Length > 4 ? int.Parse(p[4]) : 1, asset = p.Length > 5 ? p[5].Trim() : "" }); continue; } for (int x = 0; x < raw.Length; x++) if (raw[x] == 'W' || raw[x] == 'B' || raw[x] == 'T') objects.Add(new MapObjectDefinition { type = raw[x].ToString(), x = x, y = y, asset = "", blocks = true }); } map.width = lines.Length == 0 ? 0 : lines[0].Length; map.height = lines.Length; map.objects = objects.ToArray(); map.portals = portals.ToArray(); return objects.Count == 0 && portals.Count == 0 ? null : map; }
        public static bool TryParseNpcs(TextAsset asset, out NpcDatabaseDefinition result, out string error) { result = null; error = null; try { result = JsonUtility.FromJson<NpcDatabaseDefinition>(asset.text); return result != null; } catch (Exception ex) { error = $"Could not parse NPC asset '{asset.name}': {ex.Message}"; return false; } }
        private static string SelectDialogue(NpcDefinition npc) { List<string> options = new List<string>(npc.dialogueIds ?? Array.Empty<string>()); foreach (NpcDialogueDefinition item in npc.dialogue ?? Array.Empty<NpcDialogueDefinition>()) { bool questOk = string.IsNullOrWhiteSpace(item.questId) || (GameEventManager.Instance != null && GameEventManager.Instance.Runtime.GetQuestStatus(item.questId).ToString().Equals(item.requiredQuestStatus, StringComparison.OrdinalIgnoreCase)); if (questOk) for (int i = 0; i < Mathf.Max(1, item.weight); i++) options.Add(item.dialogueId); } return options.Count == 0 ? null : options[UnityEngine.Random.Range(0, options.Count)]; }
    }
}
