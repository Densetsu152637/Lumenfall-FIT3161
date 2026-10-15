using System;
using System.Linq;
using Lumenfall.DataDriven;
using Lumenfall.GridMovement;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Lumenfall.EditorTools
{
    public static class DeepForestAreaAuthoring
    {
        private const string SessionKey = "Lumenfall.DeepForestAreaAuthoring.Applied";
        private const string RotationMarker = "deep-forest-rotated-clockwise-180-v2";
        private const string AreaFolder = "Assets/_Project/World/Areas";
        private const string PrefabPath = AreaFolder + "/Prefabs/Deep Forest.prefab";
        private const string TerrainPath = "Assets/_Project/Art/Environment/Terrain/DeepForestTerrainData.asset";
        private const string AreaPath = AreaFolder + "/deepForest.asset";
        private const string ForestAreaPath = AreaFolder + "/westForest.asset";
        private const string ForestPortalPath = AreaFolder + "/Portals/ForestPortals/P.F01.asset";
        private const string DeepPortalFolder = AreaFolder + "/Portals/DeepForestPortals";
        private const string DeepPortalPath = DeepPortalFolder + "/P.DF00.asset";
        private const string ScenePath = "Assets/_Project/Scenes/SampleScene.unity";

        [InitializeOnLoadMethod]
        private static void ApplyOnceAfterImport()
        {
            if (Application.isBatchMode || SessionState.GetBool(SessionKey, false) ||
                AssetDatabase.LoadAssetAtPath<WorldAreaAsset>(AreaPath) != null) return;
            SessionState.SetBool(SessionKey, true);
            EditorApplication.delayCall += Apply;
        }

        [InitializeOnLoadMethod]
        private static void RotateOnceAfterImport()
        {
            if (Application.isBatchMode) return;
            AssetImporter importer = AssetImporter.GetAtPath(TerrainPath);
            if (importer == null || importer.userData.Contains(RotationMarker)) return;
            EditorApplication.delayCall += RotateTerrainClockwise90;
        }

        public static void Apply()
        {
            EnsureFolder(DeepPortalFolder);

            WorldAreaAsset forestArea = AssetDatabase.LoadAssetAtPath<WorldAreaAsset>(ForestAreaPath);
            if (forestArea == null) throw new InvalidOperationException("Could not load westForest area asset.");

            WorldAreaAsset deepArea = LoadOrCreate<WorldAreaAsset>(AreaPath);
            PortalContentAsset forestPortal = LoadOrCreate<PortalContentAsset>(ForestPortalPath);
            PortalContentAsset deepPortal = LoadOrCreate<PortalContentAsset>(DeepPortalPath);

            TerrainData terrainData = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainPath);
            if (terrainData == null)
            {
                terrainData = new TerrainData { heightmapResolution = 33 };
                AssetDatabase.CreateAsset(terrainData, TerrainPath);
            }
            terrainData.size = new Vector3(20f, 5f, 20f);
            terrainData.SetHeights(0, 0, new float[33, 33]);
            EditorUtility.SetDirty(terrainData);

            ConfigurePortal(forestPortal, "P.F01", forestArea, deepPortal);
            ConfigurePortal(deepPortal, "P.DF00", deepArea, forestPortal);

            GameObject deepRoot = new GameObject("Deep Forest");
            try
            {
                GameObject plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
                plane.name = "Plane";
                plane.transform.SetParent(deepRoot.transform, false);
                plane.transform.localPosition = new Vector3(10f, 0f, 10f);
                plane.transform.localScale = new Vector3(2f, 1f, 2f);

                GameObject terrainObject = Terrain.CreateTerrainGameObject(terrainData);
                terrainObject.name = "Blank Terrain";
                terrainObject.transform.SetParent(deepRoot.transform, false);
                terrainObject.transform.localPosition = new Vector3(0f, 0.02f, 0f);

                AddPortalObject(deepRoot.transform, deepPortal, new Vector3(1f, 0.03f, 10f));
                PrefabUtility.SaveAsPrefabAsset(deepRoot, PrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(deepRoot);
            }

            deepArea.definition = new MapDefinition
            {
                schemaVersion = 1,
                id = "deepForest",
                width = 20,
                height = 20,
                tileFile = string.Empty,
                portalGlyph = string.Empty,
                spawn = new SpawnDefinition { x = 3, y = 10 },
                regions = new[]
                {
                    new MapRegionDefinition
                    {
                        id = "DF00",
                        name = "Deep Forest",
                        rect = new MapRectDefinition { x = 0, y = 0, width = 20, height = 20 },
                        walkable = true
                    }
                }
            };
            deepArea.areaPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            deepArea.portals = new[] { deepPortal };
            EditorUtility.SetDirty(deepArea);

            AddPortalToArea(forestArea, forestPortal);
            AddPortalToWestForestPrefab(forestPortal);
            AddAreaToSampleScene(deepArea);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Deep Forest area, terrain, and connected P.F01/P.DF00 portals created successfully.");
        }

        [MenuItem("Lumenfall/World/Rotate Deep Forest Terrain 90 Clockwise")]
        public static void RotateTerrainClockwise90()
        {
            TerrainData terrain = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainPath);
            if (terrain == null) throw new InvalidOperationException("Could not load Deep Forest terrain data.");
            if (!Mathf.Approximately(terrain.size.x, terrain.size.z))
                throw new InvalidOperationException("Deep Forest terrain must be square for this rotation.");

            int heightResolution = terrain.heightmapResolution;
            terrain.SetHeights(0, 0, RotateClockwise(terrain.GetHeights(0, 0, heightResolution, heightResolution)));

            int alphaWidth = terrain.alphamapWidth;
            int alphaHeight = terrain.alphamapHeight;
            if (alphaWidth > 0 && alphaWidth == alphaHeight && terrain.alphamapLayers > 0)
                terrain.SetAlphamaps(0, 0,
                    RotateClockwise(terrain.GetAlphamaps(0, 0, alphaWidth, alphaHeight)));

            int detailResolution = terrain.detailResolution;
            for (int layer = 0; layer < terrain.detailPrototypes.Length && detailResolution > 0; layer++)
            {
                int[,] details = terrain.GetDetailLayer(0, 0, detailResolution, detailResolution, layer);
                terrain.SetDetailLayer(0, 0, layer, RotateClockwise(details));
            }

            int holesResolution = terrain.holesResolution;
            if (holesResolution > 0)
                terrain.SetHoles(0, 0,
                    RotateClockwise(terrain.GetHoles(0, 0, holesResolution, holesResolution)));

            TreeInstance[] trees = terrain.treeInstances;
            for (int i = 0; i < trees.Length; i++)
            {
                TreeInstance tree = trees[i];
                Vector3 position = tree.position;
                tree.position = new Vector3(position.z, position.y, 1f - position.x);
                tree.rotation = Mathf.Repeat(tree.rotation + Mathf.PI * 0.5f, Mathf.PI * 2f);
                trees[i] = tree;
            }
            terrain.treeInstances = trees;

            EditorUtility.SetDirty(terrain);
            AssetDatabase.SaveAssets();
            AssetImporter importer = AssetImporter.GetAtPath(TerrainPath);
            if (importer != null && !importer.userData.Contains(RotationMarker))
            {
                importer.userData = string.IsNullOrWhiteSpace(importer.userData)
                    ? RotationMarker
                    : importer.userData + ";" + RotationMarker;
                importer.SaveAndReimport();
            }
            AssetDatabase.Refresh();
            Debug.Log("Rotated Deep Forest TerrainData 90 degrees clockwise around Y.");
        }

        private static float[,] RotateClockwise(float[,] source)
        {
            int size = source.GetLength(0);
            if (size != source.GetLength(1)) throw new InvalidOperationException("Terrain data layer is not square.");
            float[,] result = new float[size, size];
            for (int z = 0; z < size; z++)
                for (int x = 0; x < size; x++)
                    result[z, x] = source[size - 1 - x, z];
            return result;
        }

        private static int[,] RotateClockwise(int[,] source)
        {
            int size = source.GetLength(0);
            if (size != source.GetLength(1)) throw new InvalidOperationException("Terrain detail layer is not square.");
            int[,] result = new int[size, size];
            for (int z = 0; z < size; z++)
                for (int x = 0; x < size; x++)
                    result[z, x] = source[size - 1 - x, z];
            return result;
        }

        private static bool[,] RotateClockwise(bool[,] source)
        {
            int size = source.GetLength(0);
            if (size != source.GetLength(1)) throw new InvalidOperationException("Terrain holes layer is not square.");
            bool[,] result = new bool[size, size];
            for (int z = 0; z < size; z++)
                for (int x = 0; x < size; x++)
                    result[z, x] = source[size - 1 - x, z];
            return result;
        }

        private static float[,,] RotateClockwise(float[,,] source)
        {
            int size = source.GetLength(0);
            if (size != source.GetLength(1)) throw new InvalidOperationException("Terrain alphamap is not square.");
            int layers = source.GetLength(2);
            float[,,] result = new float[size, size, layers];
            for (int z = 0; z < size; z++)
                for (int x = 0; x < size; x++)
                    for (int layer = 0; layer < layers; layer++)
                        result[z, x, layer] = source[size - 1 - x, z, layer];
            return result;
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static void ConfigurePortal(PortalContentAsset portal, string id, WorldAreaAsset area,
            PortalContentAsset destination)
        {
            portal.name = id;
            portal.id = id;
            portal.area = area;
            portal.disabled = false;
            portal.blocked = false;
            portal.destination = destination;
            EditorUtility.SetDirty(portal);
        }

        private static void AddPortalObject(Transform parent, PortalContentAsset content, Vector3 position)
        {
            GameObject portalObject = new GameObject(content.id);
            portalObject.transform.SetParent(parent, false);
            portalObject.transform.localPosition = position;
            GridPortal portal = portalObject.AddComponent<GridPortal>();
            SerializedObject serialized = new SerializedObject(portal);
            serialized.FindProperty("content").objectReferenceValue = content;
            serialized.FindProperty("preserveEntryOffset").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AddPortalToArea(WorldAreaAsset area, PortalContentAsset portal)
        {
            area.portals = (area.portals ?? Array.Empty<PortalContentAsset>())
                .Where(item => item != null && item.id != portal.id)
                .Concat(new[] { portal })
                .ToArray();
            EditorUtility.SetDirty(area);
        }

        private static void AddPortalToWestForestPrefab(PortalContentAsset portal)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(AreaFolder + "/Prefabs/West Forest.prefab");
            try
            {
                Transform existing = root.transform.Find("P.F01");
                if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);
                AddPortalObject(root.transform, portal, new Vector3(3f, 0.03f, 24f));
                PrefabUtility.SaveAsPrefabAsset(root, AreaFolder + "/Prefabs/West Forest.prefab");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void AddAreaToSampleScene(WorldAreaAsset deepArea)
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            DataDrivenWorldLoader loader = UnityEngine.Object.FindAnyObjectByType<DataDrivenWorldLoader>();
            if (loader == null) throw new InvalidOperationException("SampleScene has no DataDrivenWorldLoader.");
            SerializedObject serialized = new SerializedObject(loader);
            SerializedProperty areas = serialized.FindProperty("availableAreas");
            for (int i = 0; i < areas.arraySize; i++)
                if (areas.GetArrayElementAtIndex(i).objectReferenceValue == deepArea) return;
            areas.InsertArrayElementAtIndex(areas.arraySize);
            areas.GetArrayElementAtIndex(areas.arraySize - 1).objectReferenceValue = deepArea;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
