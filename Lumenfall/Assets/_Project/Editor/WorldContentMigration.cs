using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Lumenfall.DataDriven;
using Lumenfall.GridMovement;

namespace Lumenfall.EditorTools
{
    /// <summary>One-time authoring migration from legacy Resources JSON/text into editable area prefabs.</summary>
    public static class WorldContentMigration
    {
        private const string AreaRoot = "Assets/_Project/World/Areas";
        private const string NpcFallbackPrefabPath = "Assets/_Project/World/NPCs/NpcPlaceholder.prefab";
        private const string ScenePath = "Assets/_Project/Scenes/SampleScene.unity";

        [Serializable] private sealed class LegacyMap
        {
            public int schemaVersion = 1; public string id; public int width; public int height; public string tileFile; public string portalGlyph;
            public SpawnDefinition spawn; public MapRegionDefinition[] regions; public LegacyBuildingPlacements buildingPlacements;
            [NonSerialized] public bool hasBuildingPlacements;
            public LegacyObject[] objects; public PuzzleTargetDefinition[] puzzleTargets; public LegacyPortal[] portals;
        }
        [Serializable] private sealed class LegacyBuildingPlacements { public BuildingPlacementDefinition B01, B02, B03, B04; }
        [Serializable] private sealed class LegacyObject
        {
            public string id; public string asset; public string type = "building"; public int x; public int y; public int width = 1; public int height = 1;
            public float elevation; public float rotation; public bool blocks = true; public string[] tags; public bool compatibleWithSpells = true;
        }
        [Serializable] private sealed class LegacyPortal
        {
            public string id; public int x; public int y; public int width = 1; public int height = 1;
            public string destinationScene; public string destinationPortalId; public int arrivalOffsetX; public int arrivalOffsetY; public bool reserved;
        }
        [Serializable] private sealed class LegacyNpcCatalog { public LegacyNpc[] npcs; public LegacyLayer[] layers; }
        [Serializable] private sealed class LegacyLayer { public string mapId; public string tileFile; public int width; public int height; }
        [Serializable] private sealed class LegacyNpc
        {
            public string id; public string displayName; public string asset; public string mapId; public int x; public int y; public string layerSymbol;
            public string showWhenQuestCompleted; public string hideWhenQuestCompleted; public string[] dialogueIds; public NpcDialogueDefinition[] dialogue;
        }

        [MenuItem("Lumenfall/World/Bake Legacy World Content")]
        public static void BakeFromMenu() => Bake();
        public static void BakeFromBatch() => Bake();

        private static void Bake()
        {
            Scene activeSceneBefore = SceneManager.GetActiveScene();
            Scene scene = default;
            bool openedSceneForMigration = false;
            try
            {
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    Scene loaded = SceneManager.GetSceneAt(i);
                    if (loaded.path != ScenePath) continue;
                    if (loaded.isDirty) throw new InvalidOperationException("SampleScene has unsaved edits; save or close it before running the migration.");
                    scene = loaded;
                    break;
                }
                if (!scene.IsValid()) { scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive); openedSceneForMigration = true; }
                SceneManager.SetActiveScene(scene);

                Directory.CreateDirectory(AreaRoot + "/Portals");
                Directory.CreateDirectory(AreaRoot + "/Prefabs");
                AssetDatabase.Refresh();
                GridWorld grid = null;
                foreach (GameObject sceneRoot in scene.GetRootGameObjects())
                { grid = sceneRoot.GetComponentInChildren<GridWorld>(true); if (grid != null) break; }
                if (grid == null) throw new InvalidOperationException("SampleScene has no GridWorld.");

                string[] ids = { "campus", "eastForest", "B01.Ground", "B02.Ground", "B03.Ground", "B04.Ground" };
                Dictionary<string, LegacyMap> legacyMaps = new Dictionary<string, LegacyMap>(StringComparer.OrdinalIgnoreCase);
                Dictionary<string, WorldAreaAsset> areas = new Dictionary<string, WorldAreaAsset>(StringComparer.OrdinalIgnoreCase);
                foreach (string id in ids)
                {
                    string jsonPath = "Assets/Resources/Maps/" + id + ".json";
                    TextAsset json = AssetDatabase.LoadAssetAtPath<TextAsset>(jsonPath);
                    if (json == null) throw new FileNotFoundException("Missing legacy map " + jsonPath);
                    LegacyMap legacy = JsonUtility.FromJson<LegacyMap>(json.text);
                    if (legacy == null) throw new InvalidDataException("Could not parse " + jsonPath);
                    legacy.hasBuildingPlacements = json.text.IndexOf("\"buildingPlacements\"", StringComparison.Ordinal) >= 0;
                    if (!legacy.hasBuildingPlacements) legacy.buildingPlacements = null;
                    legacy.id = string.IsNullOrEmpty(legacy.id) ? id : legacy.id;
                    if (legacy.width <= 0 || legacy.height <= 0) throw new InvalidDataException($"Map {id} has invalid dimensions.");
                    legacyMaps[id] = legacy;
                    areas[id] = GetOrCreate<WorldAreaAsset>($"{AreaRoot}/{id}.asset");
                }

                ValidateLegacyBuildingPlacements(legacyMaps);

                LegacyNpcCatalog legacyNpcCatalog = JsonUtility.FromJson<LegacyNpcCatalog>(File.ReadAllText("Assets/Resources/NPCs/npcs.json"));
                NpcCatalogAsset npcCatalog = GetOrCreate<NpcCatalogAsset>($"{AreaRoot}/NpcCatalog.asset");
                GameObject npcFallback = EnsureNpcFallbackPrefab();
                npcCatalog.npcs = (legacyNpcCatalog.npcs ?? Array.Empty<LegacyNpc>()).Select(npc => ToNpc(npc, npcFallback)).ToArray();
                npcCatalog.layers = ReadNpcLayers(legacyNpcCatalog.layers);
                EditorUtility.SetDirty(npcCatalog);

                var portalAssets = new Dictionary<string, PortalContentAsset>(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, LegacyMap> pair in legacyMaps)
                {
                    foreach (LegacyPortal portal in pair.Value.portals ?? Array.Empty<LegacyPortal>())
                    {
                        string key = PortalKey(pair.Key, portal.id);
                        PortalContentAsset asset = GetOrCreate<PortalContentAsset>($"{AreaRoot}/Portals/{SafeName(pair.Key + "_" + portal.id)}.asset");
                        asset.id = portal.id; asset.area = areas[pair.Key]; asset.cell = new Vector2Int(portal.x, portal.y);
                        asset.footprint = new Vector2Int(Mathf.Max(1, portal.width), Mathf.Max(1, portal.height)); asset.reserved = portal.reserved;
                        EditorUtility.SetDirty(asset);
                        portalAssets[key] = asset;
                    }
                }

                int totalObjects = 0, totalNpcs = 0, totalPortals = 0, totalObstacles = 0;
                foreach (KeyValuePair<string, LegacyMap> pair in legacyMaps)
                {
                    string id = pair.Key; LegacyMap source = pair.Value; WorldAreaAsset area = areas[id];
                    MapDefinition definition = ToMapDefinition(source, id, portalAssets);
                    area.definition = definition;
                    area.portals = (source.portals ?? Array.Empty<LegacyPortal>())
                        .Select(p => portalAssets[PortalKey(id, p.id)]).ToArray();
                    TextAsset tileAsset = string.IsNullOrWhiteSpace(source.tileFile) ? null : AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Resources/Maps/" + Path.GetFileName(source.tileFile));
                    string[] tileRows = tileAsset == null ? Array.Empty<string>() : tileAsset.text.Replace("\r", "").Split('\n');
                    GameObject root = new GameObject(id + " Authored Area"); root.SetActive(false);
                    try
                    {
                        root.transform.position = Vector3.zero;
                        BuildTiles(root.transform, tileRows, source.width, source.height, grid, ref totalObstacles);
                        BuildBuilding(root.transform, definition.B01, definition.B01Prefab, "B01", grid);
                        BuildBuilding(root.transform, definition.B02, definition.B02Prefab, "B02", grid);
                        BuildBuilding(root.transform, definition.B03, definition.B03Prefab, "B03", grid);
                        BuildBuilding(root.transform, definition.B04, definition.B04Prefab, "B04", grid);
                        foreach (MapObjectDefinition item in definition.objects)
                        {
                            GameObject go = MakePlacedObject(item.prefab, string.IsNullOrEmpty(item.id) ? item.type + "_" + item.x + "_" + item.y : item.id, root.transform,
                                grid.CellToWorld(new Vector2Int(item.x, item.y), item.elevation), Quaternion.Euler(0, item.rotation, 0));
                            if (item.blocks) AddObstacle(go, grid, new Vector2Int(item.width, item.height), ref totalObstacles);
                            go.AddComponent<WorldObjectRegistration>().Configure(go.name, new Vector2Int(item.x, item.y), new Vector2Int(item.width, item.height), item.compatibleWithSpells, item.tags);
                            totalObjects++;
                        }
                        foreach (LegacyPortal p in source.portals ?? Array.Empty<LegacyPortal>())
                        {
                            PortalContentAsset data = portalAssets[PortalKey(id, p.id)];
                            if (p.reserved || (!string.IsNullOrEmpty(source.portalGlyph) && !HasGlyph(tileRows, p.x, p.y, source.portalGlyph))) continue;
                            GameObject go = new GameObject(p.id); go.transform.SetParent(root.transform, false);
                            go.transform.localPosition = root.transform.InverseTransformPoint(grid.CellToWorld(data.cell));
                            go.AddComponent<GridPortal>().Configure(data, null);
                            totalPortals++;
                        }
                        foreach (NpcDefinition npc in npcCatalog.npcs.Where(n => string.Equals(n.mapId, id, StringComparison.OrdinalIgnoreCase)))
                        {
                            Vector2Int cell = ResolveNpcCell(npc, npcCatalog.npcs, npcCatalog.layers);
                            GameObject go = MakePlacedObject(npc.prefab, npc.displayName, root.transform, grid.CellToWorld(cell), Quaternion.identity);
                            DynamicNpc dynamicNpc = go.GetComponent<DynamicNpc>() ?? go.AddComponent<DynamicNpc>();
                            dynamicNpc.Configure(npc, null);
                            totalNpcs++;
                        }
                        string prefabPath = $"{AreaRoot}/Prefabs/{id}.prefab";
                        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                        if (prefab == null) throw new IOException("Failed to save area prefab " + prefabPath);
                        ValidateAuthoredPrefab(prefab, definition, source, tileRows, npcCatalog.npcs, id);
                        area.areaPrefab = prefab;
                    }
                    finally { UnityEngine.Object.DestroyImmediate(root); }
                    EditorUtility.SetDirty(area);
                }

                foreach (KeyValuePair<string, LegacyMap> pair in legacyMaps)
                {
                    string fromId = pair.Key;
                    foreach (LegacyPortal portal in pair.Value.portals ?? Array.Empty<LegacyPortal>())
                    {
                        PortalContentAsset from = portalAssets[PortalKey(fromId, portal.id)];
                        if (string.IsNullOrWhiteSpace(portal.destinationScene) || string.IsNullOrWhiteSpace(portal.destinationPortalId)) continue;
                        PortalContentAsset to;
                        if (!portalAssets.TryGetValue(PortalKey(portal.destinationScene, portal.destinationPortalId), out to))
                        { Debug.LogWarning($"Unresolved portal route {fromId}/{portal.id} -> {portal.destinationScene}/{portal.destinationPortalId}"); continue; }
                        from.destination = to;
                        Vector2Int migratedArrival = GetMigratedArrivalCell(fromId, portal);
                        from.arrivalOffset = migratedArrival - to.cell;
                        EditorUtility.SetDirty(from);
                    }
                }

                WireScene(scene, grid, areas, npcCatalog);
                AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
                ValidateSerializedContent(legacyMaps, areas, portalAssets, npcCatalog, scene, grid);
                if (activeSceneBefore.IsValid() && activeSceneBefore.isLoaded) SceneManager.SetActiveScene(activeSceneBefore);
                if (openedSceneForMigration) { EditorSceneManager.CloseScene(scene, true); openedSceneForMigration = false; }
                Debug.Log($"World migration complete: {areas.Count} area prefabs; {totalObjects} registered objects; {totalObstacles} obstacle footprints; {totalNpcs} NPCs; {totalPortals} active portals.");
            }
            catch (Exception exception)
            {
                if (activeSceneBefore.IsValid() && activeSceneBefore.isLoaded) SceneManager.SetActiveScene(activeSceneBefore);
                if (openedSceneForMigration && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                Debug.LogException(exception);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        private static T GetOrCreate<T>(string path) where T : ScriptableObject
        {
            T value = AssetDatabase.LoadAssetAtPath<T>(path);
            if (value == null) { value = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(value, path); }
            return value;
        }
        private static void ValidateLegacyBuildingPlacements(Dictionary<string, LegacyMap> maps)
        {
            foreach (KeyValuePair<string, LegacyMap> pair in maps)
            {
                LegacyMap map = pair.Value;
                LegacyBuildingPlacements placements = map.hasBuildingPlacements ? map.buildingPlacements : null;
                if (!map.hasBuildingPlacements && map.buildingPlacements != null)
                    throw new InvalidDataException($"Map {pair.Key} has defaulted building placements without a buildingPlacements JSON key.");
                if (placements == null) continue;
                ValidatePlacement(pair.Key, "B01", placements.B01);
                ValidatePlacement(pair.Key, "B02", placements.B02);
                ValidatePlacement(pair.Key, "B03", placements.B03);
                ValidatePlacement(pair.Key, "B04", placements.B04);
            }
        }
        private static void ValidatePlacement(string mapId, string buildingId, BuildingPlacementDefinition placement)
        {
            if (placement == null) throw new InvalidDataException($"Map {mapId} is missing nested placement {buildingId}.");
            if (placement.width <= 0 || placement.height <= 0)
                throw new InvalidDataException($"Map {mapId} has invalid dimensions for nested placement {buildingId}.");
        }
        private static void ValidateAuthoredPrefab(GameObject prefab, MapDefinition definition, LegacyMap source, string[] tileRows, NpcDefinition[] npcs, string mapId)
        {
            WorldObjectRegistration[] registrations = prefab.GetComponentsInChildren<WorldObjectRegistration>(true);
            int expectedBuildings = source.hasBuildingPlacements ? 4 : 0;
            int expectedRegistrations = definition.objects.Length + expectedBuildings;
            if (registrations.Length != expectedRegistrations)
                throw new InvalidDataException($"Area prefab {mapId} has {registrations.Length} object registrations; expected {expectedRegistrations}.");
            foreach (string id in new[] { "B01", "B02", "B03", "B04" })
            {
                BuildingPlacementDefinition expected = source.hasBuildingPlacements
                    ? id == "B01" ? source.buildingPlacements.B01 : id == "B02" ? source.buildingPlacements.B02 : id == "B03" ? source.buildingPlacements.B03 : source.buildingPlacements.B04
                    : null;
                WorldObjectRegistration actual = registrations.FirstOrDefault(item => item.ObjectId == id);
                if (expected == null && actual != null)
                    throw new InvalidDataException($"Area prefab {mapId} unexpectedly contains building {id}.");
                if (expected != null && (actual == null || actual.Cell != new Vector2Int(expected.x, expected.y) || actual.Size != new Vector2Int(expected.width, expected.height)))
                    throw new InvalidDataException($"Area prefab {mapId} has a missing or misplaced {id} building registration.");
            }
            int expectedNpcs = npcs.Count(npc => string.Equals(npc.mapId, mapId, StringComparison.OrdinalIgnoreCase));
            DynamicNpc[] actualNpcComponents = prefab.GetComponentsInChildren<DynamicNpc>(true);
            int actualNpcs = actualNpcComponents.Length;
            if (actualNpcs != expectedNpcs)
                throw new InvalidDataException($"Area prefab {mapId} has {actualNpcs} NPCs; expected {expectedNpcs}.");
            foreach (NpcDefinition expected in npcs.Where(npc => string.Equals(npc.mapId, mapId, StringComparison.OrdinalIgnoreCase)))
            {
                DynamicNpc actual = actualNpcComponents.FirstOrDefault(npc => npc.Definition != null && npc.Definition.id == expected.id);
                if (actual == null)
                    throw new InvalidDataException($"Area prefab {mapId} is missing NPC definition {expected.id}.");
                if (expected.prefab == null || actual.Definition.prefab != expected.prefab)
                    throw new InvalidDataException($"Area prefab {mapId} NPC {expected.id} has no matching serialized prefab reference.");
                if (actual.GetComponentInChildren<Renderer>(true) == null)
                    throw new InvalidDataException($"Area prefab {mapId} NPC {expected.id} has no visible renderer.");
            }

            int expectedPortals = (source.portals ?? Array.Empty<LegacyPortal>()).Count(portal => !portal.reserved &&
                (string.IsNullOrEmpty(source.portalGlyph) || HasGlyph(tileRows, portal.x, portal.y, source.portalGlyph)));
            GridPortal[] actualPortals = prefab.GetComponentsInChildren<GridPortal>(true);
            if (actualPortals.Length != expectedPortals || actualPortals.Any(portal => portal.Content == null))
                throw new InvalidDataException($"Area prefab {mapId} has invalid portal content references ({actualPortals.Length}; expected {expectedPortals}).");
        }

        private static void ValidateSerializedContent(Dictionary<string, LegacyMap> legacyMaps,
            Dictionary<string, WorldAreaAsset> areas, Dictionary<string, PortalContentAsset> portals,
            NpcCatalogAsset npcCatalog, Scene scene, GridWorld grid)
        {
            AssetDatabase.SaveAssets();
            NpcCatalogAsset savedCatalog = AssetDatabase.LoadAssetAtPath<NpcCatalogAsset>($"{AreaRoot}/NpcCatalog.asset");
            int expectedNpcCount = npcCatalog.npcs == null ? 0 : npcCatalog.npcs.Length;
            int expectedLayerCount = npcCatalog.layers == null ? 0 : npcCatalog.layers.Length;
            if (savedCatalog == null || savedCatalog.npcs == null || savedCatalog.npcs.Length != expectedNpcCount ||
                savedCatalog.layers == null || savedCatalog.layers.Length != expectedLayerCount)
                throw new InvalidDataException("NPC catalog data did not persist to its asset.");
            if (savedCatalog.npcs.Any(npc => npc == null || npc.prefab == null))
                throw new InvalidDataException("NPC catalog contains a definition without a serialized prefab reference.");

            foreach (KeyValuePair<string, LegacyMap> pair in legacyMaps)
            {
                WorldAreaAsset savedArea = AssetDatabase.LoadAssetAtPath<WorldAreaAsset>($"{AreaRoot}/{pair.Key}.asset");
                if (savedArea == null || savedArea.areaPrefab == null || savedArea.portals == null ||
                    savedArea.portals.Length != (pair.Value.portals ?? Array.Empty<LegacyPortal>()).Length)
                    throw new InvalidDataException($"Area asset {pair.Key} or its portal catalog did not persist correctly.");
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{AreaRoot}/Prefabs/{pair.Key}.prefab");
                if (prefab == null || savedArea.areaPrefab != prefab)
                    throw new InvalidDataException($"Area asset {pair.Key} has no direct reference to its saved prefab.");

                foreach (LegacyPortal expected in pair.Value.portals ?? Array.Empty<LegacyPortal>())
                {
                    PortalContentAsset actual;
                    if (!portals.TryGetValue(PortalKey(pair.Key, expected.id), out actual) || actual == null)
                        throw new InvalidDataException($"Missing portal content asset {pair.Key}/{expected.id}.");
                    if (actual.id != expected.id || actual.area != savedArea || actual.cell != new Vector2Int(expected.x, expected.y) || actual.reserved != expected.reserved)
                        throw new InvalidDataException($"Portal content asset {pair.Key}/{expected.id} did not persist its identity, area, cell, or reserved flag.");
                    if (!string.IsNullOrWhiteSpace(expected.destinationScene) && !string.IsNullOrWhiteSpace(expected.destinationPortalId))
                    {
                        PortalContentAsset target;
                        bool targetExists = portals.TryGetValue(PortalKey(expected.destinationScene, expected.destinationPortalId), out target);
                        if (targetExists && actual.destination != target)
                            throw new InvalidDataException($"Portal content asset {pair.Key}/{expected.id} lost its direct destination reference.");
                        if (targetExists && !expected.reserved && target.cell + actual.arrivalOffset != GetMigratedArrivalCell(pair.Key, expected))
                            throw new InvalidDataException($"Portal content asset {pair.Key}/{expected.id} has an incorrect arrival offset.");
                    }
                }
            }

            DataDrivenWorldLoader loader = null;
            foreach (GameObject sceneRoot in scene.GetRootGameObjects())
            { loader = sceneRoot.GetComponentInChildren<DataDrivenWorldLoader>(true); if (loader != null) break; }
            if (loader == null) throw new InvalidDataException("SampleScene loader was not saved.");
            SerializedObject serializedLoader = new SerializedObject(loader);
            if (serializedLoader.FindProperty("startingArea").objectReferenceValue != areas["campus"] ||
                serializedLoader.FindProperty("npcCatalog").objectReferenceValue != savedCatalog ||
                serializedLoader.FindProperty("world").objectReferenceValue != grid ||
                serializedLoader.FindProperty("contentRoot").objectReferenceValue == null ||
                serializedLoader.FindProperty("availableAreas").arraySize != areas.Count - 1)
                throw new InvalidDataException("SampleScene does not have all direct world loader references assigned.");
        }
        private static string PortalKey(string area, string id) => area + "/" + id;
        private static string SafeName(string value) => string.Concat(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == '/' || c == '\\' ? '_' : c));
        private static GameObject ResolvePrefab(string legacyPath)
        {
            if (string.IsNullOrWhiteSpace(legacyPath)) return null;
            string normalized = legacyPath.Replace('\\', '/').TrimStart('/');
            if (normalized.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) normalized = normalized.Substring(0, normalized.Length - 7);
            string[] paths = { "Assets/Resources/" + normalized + ".prefab", "Assets/Resources/Buildings/" + Path.GetFileName(normalized) + ".prefab", "Assets/Resources/NPCs/" + Path.GetFileName(normalized) + ".prefab" };
            foreach (string path in paths)
            {
                GameObject found = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (found != null) return found;
            }
            if (File.Exists(paths[0]))
                Debug.LogWarning($"Legacy prefab '{paths[0]}' cannot be imported as a GameObject (main type: {AssetDatabase.GetMainAssetTypeAtPath(paths[0])}); caller will use the explicit placeholder prefab.");
            return null;
        }
        private static NpcDefinition ToNpc(LegacyNpc item, GameObject fallbackPrefab)
        {
            GameObject prefab = ResolvePrefab(item.asset);
            if (prefab == null && !string.IsNullOrWhiteSpace(item.asset))
            {
                Debug.LogWarning($"Legacy prefab '{item.asset}' cannot be imported as a GameObject; using the editable placeholder prefab at '{NpcFallbackPrefabPath}'.");
                prefab = fallbackPrefab;
            }
            return new NpcDefinition
            {
                id = item.id, displayName = item.displayName, prefab = prefab, mapId = item.mapId, x = item.x, y = item.y,
                layerSymbol = item.layerSymbol, showWhenQuestCompleted = item.showWhenQuestCompleted, hideWhenQuestCompleted = item.hideWhenQuestCompleted,
                dialogueIds = item.dialogueIds ?? Array.Empty<string>(), dialogue = item.dialogue ?? Array.Empty<NpcDialogueDefinition>()
            };
        }
        private static GameObject EnsureNpcFallbackPrefab()
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(NpcFallbackPrefabPath);
            if (existing != null) return existing;
            Directory.CreateDirectory(Path.GetDirectoryName(NpcFallbackPrefabPath));
            GameObject placeholder = GameObject.CreatePrimitive(PrimitiveType.Cube);
            placeholder.name = "NPC Placeholder";
            try
            {
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(placeholder, NpcFallbackPrefabPath);
                if (saved == null) throw new IOException("Could not save the NPC placeholder prefab.");
                return saved;
            }
            finally { UnityEngine.Object.DestroyImmediate(placeholder); }
        }
        private static NpcLayerDefinition[] ReadNpcLayers(LegacyLayer[] layers)
        {
            return (layers ?? Array.Empty<LegacyLayer>()).Select(layer =>
            {
                TextAsset text = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Resources/NPCs/" + Path.GetFileName(layer.tileFile));
                return new NpcLayerDefinition { mapId = layer.mapId, tileFile = layer.tileFile, width = layer.width, height = layer.height,
                    rows = text == null ? Array.Empty<string>() : text.text.Replace("\r", "").Split('\n') };
            }).ToArray();
        }
        private static MapDefinition ToMapDefinition(LegacyMap source, string id, Dictionary<string, PortalContentAsset> portals)
        {
            return new MapDefinition
            {
                schemaVersion = source.schemaVersion, id = id, width = source.width, height = source.height, tileFile = source.tileFile, portalGlyph = source.portalGlyph,
                spawn = source.spawn, regions = source.regions ?? Array.Empty<MapRegionDefinition>(),
                B01 = source.hasBuildingPlacements && source.buildingPlacements != null ? source.buildingPlacements.B01 : null,
                B02 = source.hasBuildingPlacements && source.buildingPlacements != null ? source.buildingPlacements.B02 : null,
                B03 = source.hasBuildingPlacements && source.buildingPlacements != null ? source.buildingPlacements.B03 : null,
                B04 = source.hasBuildingPlacements && source.buildingPlacements != null ? source.buildingPlacements.B04 : null,
                B01Prefab = ResolvePrefab("Buildings/B01"), B02Prefab = ResolvePrefab("Buildings/B02"), B03Prefab = ResolvePrefab("Buildings/B03"), B04Prefab = ResolvePrefab("Buildings/B04"),
                objects = (source.objects ?? Array.Empty<LegacyObject>()).Select(o => new MapObjectDefinition { id=o.id, prefab=ResolvePrefab(o.asset), type=o.type, x=o.x, y=o.y, width=Mathf.Max(1,o.width), height=Mathf.Max(1,o.height), elevation=o.elevation, rotation=o.rotation, blocks=o.blocks, tags=o.tags ?? Array.Empty<string>(), compatibleWithSpells=o.compatibleWithSpells }).ToArray(),
                puzzleTargets = source.puzzleTargets ?? Array.Empty<PuzzleTargetDefinition>(),
                portals = (source.portals ?? Array.Empty<LegacyPortal>()).Select(p => new PortalDefinition { id=p.id, x=p.x, y=p.y, width=p.width, height=p.height, destination=ResolvePortal(portals, p.destinationScene, p.destinationPortalId), arrivalOffset = GetMigratedArrivalCell(id, p), reserved=p.reserved }).ToArray()
            };
        }
        private static Vector2Int GetMigratedArrivalCell(string sourceArea, LegacyPortal portal)
        {
            // The legacy indoor exits target cells inside their campus buildings. Keep the same route and place arrivals at the nearest free approach tile.
            if (sourceArea == "B02.Ground" && portal.id == "P.B02.C05") return new Vector2Int(23, 11);
            if (sourceArea == "B03.Ground" && portal.id == "P.B03.C05") return new Vector2Int(53, 7);
            if (sourceArea == "B04.Ground" && portal.id == "P.B04.C05") return new Vector2Int(85, 47);
            return new Vector2Int(portal.arrivalOffsetX, portal.arrivalOffsetY);
        }
        private static PortalContentAsset ResolvePortal(Dictionary<string, PortalContentAsset> portals, string area, string id)
        { PortalContentAsset value; return portals.TryGetValue(PortalKey(area, id), out value) ? value : null; }
        private static Vector2Int ResolveNpcCell(NpcDefinition npc, NpcDefinition[] allNpcs, NpcLayerDefinition[] layers)
        {
            int definitionsWithSameMarker = allNpcs.Count(other => other.mapId == npc.mapId && other.layerSymbol == npc.layerSymbol);
            if (!string.IsNullOrEmpty(npc.layerSymbol) && definitionsWithSameMarker == 1) foreach (NpcLayerDefinition layer in layers)
                if (layer.mapId == npc.mapId)
                {
                    Vector2Int match = new Vector2Int(-1,-1); int matches=0;
                    for (int y=0; y<layer.rows.Length; y++) { int x=layer.rows[y].IndexOf(npc.layerSymbol, StringComparison.Ordinal); if (x>=0) { match=new Vector2Int(x,y); matches++; } }
                    if (matches == 1) return match;
                }
            return new Vector2Int(npc.x,npc.y);
        }
        private static bool HasGlyph(string[] rows, int x, int y, string glyph) =>
            !string.IsNullOrEmpty(glyph) && y >= 0 && y < rows.Length && x >= 0 && x < rows[y].Length && rows[y][x] == glyph[0];
        private static void BuildTiles(Transform root, string[] rows, int width, int height, GridWorld grid, ref int obstacles)
        {
            bool[,] used = new bool[Mathf.Max(1,width),Mathf.Max(1,height)];
            for (int y=0; y<rows.Length && y<height; y++) for (int x=0; x<rows[y].Length && x<width; x++)
                if (!used[x,y] && (rows[y][x]=='W' || rows[y][x]=='B' || rows[y][x]=='T'))
                {
                    int w=1,h=1;
                    if (rows[y][x]=='T' && x+1<rows[y].Length && y+1<rows.Length && x+1<width && y+1<height && rows[y][x+1]=='T' && rows[y+1][x]=='T' && rows[y+1][x+1]=='T')
                    { w=h=2; used[x,y]=used[x+1,y]=used[x,y+1]=used[x+1,y+1]=true; }
                    GameObject go = MakePlacedObject(null, rows[y][x]+"_tile_"+x+"_"+y, root, grid.CellToWorld(new Vector2Int(x,y)), Quaternion.identity);
                    go.transform.localScale = new Vector3(1f,.5f,1f); AddObstacle(go,grid,new Vector2Int(w,h),ref obstacles);
                }
        }
        private static void BuildBuilding(Transform root, BuildingPlacementDefinition placement, GameObject prefab, string id, GridWorld grid)
        {
            if (placement == null || prefab == null) return;
            GameObject go = MakePlacedObject(prefab,id,root,grid.CellToWorld(new Vector2Int(placement.x,placement.y)),Quaternion.identity);
            int ignored=0; AddObstacle(go,grid,new Vector2Int(placement.width,placement.height),ref ignored);
            go.AddComponent<WorldObjectRegistration>().Configure(id,new Vector2Int(placement.x,placement.y),new Vector2Int(placement.width,placement.height),true,Array.Empty<string>());
        }
        private static void AddObstacle(GameObject go, GridWorld grid, Vector2Int size, ref int count)
        { GridObstacle obstacle = go.GetComponent<GridObstacle>() ?? go.AddComponent<GridObstacle>(); obstacle.Configure(null,size); count++; }
        private static GameObject MakePlacedObject(GameObject prefab, string name, Transform parent, Vector3 position, Quaternion rotation)
        {
            GameObject go = prefab == null ? GameObject.CreatePrimitive(PrimitiveType.Cube) : PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (go == null) go = UnityEngine.Object.Instantiate(prefab);
            go.name = name; go.transform.SetParent(parent,false); go.transform.localPosition = parent.InverseTransformPoint(position); go.transform.localRotation = rotation;
            return go;
        }
        private static void WireScene(Scene scene, GridWorld grid, Dictionary<string, WorldAreaAsset> areas, NpcCatalogAsset catalog)
        {
            DataDrivenWorldLoader loader = null;
            foreach (GameObject sceneRoot in scene.GetRootGameObjects())
            { loader = sceneRoot.GetComponentInChildren<DataDrivenWorldLoader>(true); if (loader != null) break; }
            if (loader == null) { GameObject host = new GameObject("World Content"); SceneManager.MoveGameObjectToScene(host,scene); loader=host.AddComponent<DataDrivenWorldLoader>(); }
            DialogueOverlay overlay = loader.GetComponent<DialogueOverlay>(); if (overlay == null) loader.gameObject.AddComponent<DialogueOverlay>();
            Transform content = loader.transform.Find("Authored Areas"); if (content == null) { GameObject root=new GameObject("Authored Areas"); root.transform.SetParent(loader.transform,false); content=root.transform; }
            SerializedObject so = new SerializedObject(loader);
            so.FindProperty("startingArea").objectReferenceValue = areas["campus"];
            so.FindProperty("npcCatalog").objectReferenceValue = catalog;
            SerializedProperty available = so.FindProperty("availableAreas"); available.arraySize = areas.Count - 1;
            int i=0; foreach (KeyValuePair<string,WorldAreaAsset> pair in areas) if (pair.Key!="campus") available.GetArrayElementAtIndex(i++).objectReferenceValue=pair.Value;
            so.FindProperty("contentRoot").objectReferenceValue=content; so.FindProperty("world").objectReferenceValue=grid;
            so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(loader); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        }
    }
}
