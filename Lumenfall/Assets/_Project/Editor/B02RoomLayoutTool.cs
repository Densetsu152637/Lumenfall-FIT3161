using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Lumenfall.EditorTools
{
    public static class B02RoomLayoutTool
    {
        private const string AreaPath = "Assets/_Project/World/Areas/Prefabs/Arcane Workshop.prefab";
        private const string FurniturePath = "Assets/_Project/Art/Environment/Furnitures/";
        private const string PuzzlePath = "Assets/_Project/Art/Environment/Puzzle Elements";
        private const string LayoutRootName = "[Arcane Workshop Furnishings]";
        private const string RequestFileName = "RunB02RoomLayout.once";
        private static readonly HashSet<string> ReportedModelBounds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static readonly Vector2Int[] RelicCells =
        {
            new Vector2Int(19, 19), new Vector2Int(26, 14), new Vector2Int(23, 6),
            new Vector2Int(15, 6), new Vector2Int(12, 14)
        };

        private static readonly Vector2Int[] RelayCells =
        {
            new Vector2Int(18, 7), new Vector2Int(21, 7), new Vector2Int(25, 9), new Vector2Int(26, 12),
            new Vector2Int(24, 17), new Vector2Int(21, 19), new Vector2Int(16, 18), new Vector2Int(13, 16),
            new Vector2Int(12, 11), new Vector2Int(12, 8)
        };

        [MenuItem("Lumenfall/World/Build B02 Magic Classroom")]
        public static void Execute()
        {
            EnsureFolder(PuzzlePath);
            GameObject relicPrefab = BuildRelicPrefab();
            GameObject relayPrefab = BuildRelayPrefab();
            GameObject root = PrefabUtility.LoadPrefabContents(AreaPath);
            try
            {
                Transform previous = root.transform.Find(LayoutRootName);
                if (previous != null) UnityEngine.Object.DestroyImmediate(previous.gameObject);

                Transform layout = NewRoot(LayoutRootName, root.transform);
                Transform puzzle = NewRoot("Puzzle Objects", layout);
                Transform workstations = NewRoot("Classroom Furniture", layout);
                Transform shelves = NewRoot("Shelves and Storage", layout);
                Transform dressing = NewRoot("Small Decorations", layout);

                PlacePuzzleObjects(relicPrefab, relayPrefab, puzzle);
                PlaceFurniture(workstations, shelves, dressing);
                DoubleDirectChildren(puzzle);
                DoubleDirectChildren(workstations);
                DoubleDirectChildren(shelves);
                DoubleDirectChildren(dressing);

                PrefabUtility.SaveAsPrefabAsset(root, AreaPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log("B02 magic classroom layout built successfully.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [InitializeOnLoadMethod]
        private static void ExecuteRequestedLayout()
        {
            string requestPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Temp", RequestFileName);
            if (!File.Exists(requestPath)) return;
            EditorApplication.delayCall += () =>
            {
                if (!File.Exists(requestPath)) return;
                File.Delete(requestPath);
                Execute();
            };
        }

        private static GameObject BuildRelicPrefab()
        {
            GameObject root = new GameObject("PF_Stella_CircuitRelic");
            GameObject core = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            core.name = "Glow Core";
            core.transform.SetParent(root.transform, false);
            core.transform.localPosition = new Vector3(0f, 0.72f, 0f);
            core.transform.localScale = new Vector3(0.42f, 0.42f, 0.42f);
            StripColliders(core);

            AddModel(root.transform, "BookStand.fbx", Vector3.zero, new Vector3(0f, 180f, 0f), 1.05f);
            AddModel(root.transform, "Chalice.fbx", new Vector3(0f, 0.78f, 0f), Vector3.zero, 0.52f);

            GameObject label = new GameObject("Number");
            label.transform.SetParent(root.transform, false);
            label.transform.localPosition = new Vector3(0f, 1.35f, -0.12f);
            TextMesh text = label.AddComponent<TextMesh>();
            text.text = "1";
            text.fontSize = 64;
            text.characterSize = 0.1f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = Color.white;

            string path = PuzzlePath + "/PF_Stella_CircuitRelic.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            return prefab;
        }

        private static GameObject BuildRelayPrefab()
        {
            GameObject root = new GameObject("PF_Stella_CircuitRelay");
            GameObject core = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            core.name = "Glow Core";
            core.transform.SetParent(root.transform, false);
            core.transform.localPosition = new Vector3(0f, 0.34f, 0f);
            core.transform.localScale = new Vector3(0.24f, 0.34f, 0.24f);
            StripColliders(core);
            AddModel(root.transform, "CandleStick_Stand.fbx", Vector3.zero, Vector3.zero, 0.72f);

            string path = PuzzlePath + "/PF_Stella_CircuitRelay.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            return prefab;
        }

        private static void PlacePuzzleObjects(GameObject relicPrefab, GameObject relayPrefab, Transform parent)
        {
            for (int i = 0; i < RelicCells.Length; i++)
            {
                GameObject relic = (GameObject)PrefabUtility.InstantiatePrefab(relicPrefab, parent);
                relic.name = "stella.string-together.relic." + (i + 1);
                relic.transform.localPosition = Cell(RelicCells[i], 0.04f);
                relic.transform.localRotation = Quaternion.Euler(0f, FaceCentre(RelicCells[i]), 0f);
                TextMesh label = relic.GetComponentInChildren<TextMesh>(true);
                if (label != null) label.text = (i + 1).ToString();
            }
            for (int i = 0; i < RelayCells.Length; i++)
            {
                GameObject relay = (GameObject)PrefabUtility.InstantiatePrefab(relayPrefab, parent);
                relay.name = "stella.circuit.relay." + (i + 1);
                relay.transform.localPosition = Cell(RelayCells[i], 0.04f);
                relay.SetActive(false);
            }
        }

        private static void PlaceFurniture(Transform workstations, Transform shelves, Transform dressing)
        {
            // Large classroom pieces stay against the walls and out of the puzzle ring and entrance aisle.
            Place(workstations, "Table_Large.fbx", "Teacher Demonstration Table", new Vector3(19f, 0.03f, 12f), 0f, 1.0f);
            Place(workstations, "Chair_1.fbx", "Teacher Chair", new Vector3(19f, 0.03f, 10.2f), 180f, 1.0f);
            Place(workstations, "Workbench_Drawers.fbx", "West Workbench", new Vector3(4.0f, 0.03f, 7f), 90f, 1.05f);
            Place(workstations, "Workbench.fbx", "East Workbench", new Vector3(31.5f, 0.03f, 8f), 270f, 1.05f);
            Place(workstations, "Cauldron.fbx", "Practice Cauldron", new Vector3(5.2f, 0.03f, 18.2f), 30f, 0.65f);
            Place(workstations, "BookStand.fbx", "Lectern", new Vector3(19f, 0.03f, 15.0f), 180f, 1.25f);

            Place(shelves, "Bookcase_2.fbx", "North Bookcase A", new Vector3(5f, 0.03f, 21.8f), 180f, 2.4f);
            Place(shelves, "Bookcase_2.fbx", "North Bookcase B", new Vector3(9f, 0.03f, 21.8f), 180f, 2.4f);
            Place(shelves, "Shelf_Arch.fbx", "East Potion Shelf", new Vector3(33.7f, 0.03f, 17f), 270f, 2.2f);
            Place(shelves, "Cabinet.fbx", "West Cabinet", new Vector3(2.2f, 0.03f, 12f), 90f, 1.45f);
            Place(shelves, "Chest_Wood.fbx", "Supply Chest", new Vector3(30.5f, 0.03f, 20.5f), 225f, 0.85f);

            Place(dressing, "Book_Stack_1.fbx", "Books on Teacher Table", new Vector3(18.2f, 1.05f, 12f), 20f, 0.45f, false);
            Place(dressing, "Scroll_1.fbx", "Lesson Scroll", new Vector3(19.4f, 1.06f, 12f), 345f, 0.5f, false);
            Place(dressing, "Potion_1.fbx", "Demonstration Potion A", new Vector3(20.2f, 1.06f, 12.2f), 0f, 0.28f, false);
            Place(dressing, "Potion_2.fbx", "Demonstration Potion B", new Vector3(20.55f, 1.06f, 11.85f), 0f, 0.26f, false);
            Place(dressing, "SmallBottles_1.fbx", "Potion Shelf Bottles", new Vector3(33.1f, 1.15f, 17f), 270f, 0.55f, false);
            Place(dressing, "BookGroup_Medium_1.fbx", "North Shelf Books A", new Vector3(5f, 1.15f, 21.25f), 180f, 0.65f, false);
            Place(dressing, "BookGroup_Medium_2.fbx", "North Shelf Books B", new Vector3(9f, 1.15f, 21.25f), 180f, 0.65f, false);
            Place(dressing, "CandleStick_Triple.fbx", "Teacher Table Candles", new Vector3(17.3f, 1.06f, 12.1f), 0f, 0.62f);
            Place(dressing, "Banner_1.fbx", "North Banner A", new Vector3(14f, 2.4f, 22.65f), 180f, 2.2f);
            Place(dressing, "Banner_2.fbx", "North Banner B", new Vector3(24f, 2.4f, 22.65f), 180f, 2.2f);
        }

        private static GameObject Place(Transform parent, string model, string name, Vector3 position, float yaw, float targetSize, bool scaleByHeight = true)
        {
            GameObject instance = AddModel(parent, model, position, new Vector3(0f, yaw, 0f), targetSize, scaleByHeight);
            if (instance != null) instance.name = name;
            return instance;
        }

        private static GameObject AddModel(Transform parent, string file, Vector3 localPosition, Vector3 localEuler, float targetSize, bool scaleByHeight = true)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(FurniturePath + file);
            if (source == null) throw new InvalidOperationException("Missing furniture model: " + file);
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(source, parent);
            // Imported FBX roots carry the authoring-tool-to-Unity axis conversion. Preserve it and apply
            // room-facing yaw outside that correction; replacing it with a plain Euler rotation makes the
            // whole model lie on its side.
            Quaternion importedAxisCorrection = source.transform.localRotation;
            Vector3 importedScale = source.transform.localScale;
            instance.transform.localPosition = new Vector3(localPosition.x, 0f, localPosition.z);
            instance.transform.localRotation = Quaternion.Euler(localEuler) * importedAxisCorrection;
            instance.transform.localScale = importedScale;
            StripColliders(instance);
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
                if (ReportedModelBounds.Add(file))
                    Debug.Log($"B02_MODEL_BOUNDS {file}: {bounds.size.x:F3} x {bounds.size.y:F3} x {bounds.size.z:F3}");
                float measuredSize = scaleByHeight ? bounds.size.y : Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
                if (measuredSize > 0.001f)
                {
                    float factor = targetSize / measuredSize;
                    instance.transform.localScale = Vector3.Scale(importedScale, Vector3.one * factor);

                    // Imported pivots vary between models. Ground by the rendered bottom instead of assuming
                    // that every FBX pivot is located at its feet.
                    bounds = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
                    instance.transform.position += Vector3.up * (localPosition.y - bounds.min.y);
                }
            }
            return instance;
        }

        private static void StripColliders(GameObject target)
        {
            foreach (Collider collider in target.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.DestroyImmediate(collider);
        }

        private static Vector3 Cell(Vector2Int cell, float height) => new Vector3(cell.x, height, cell.y);

        private static float FaceCentre(Vector2Int cell)
        {
            Vector3 direction = new Vector3(19f - cell.x, 0f, 12f - cell.y);
            return Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        }

        private static Transform NewRoot(string name, Transform parent)
        {
            GameObject result = new GameObject(name);
            result.transform.SetParent(parent, false);
            return result.transform;
        }

        private static void DoubleDirectChildren(Transform group)
        {
            for (int i = 0; i < group.childCount; i++)
                group.GetChild(i).localScale *= 2f;
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
