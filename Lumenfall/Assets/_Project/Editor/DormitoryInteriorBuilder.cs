using System;
using System.Collections.Generic;
using Lumenfall.DataDriven;
using Lumenfall.GridMovement;
using UnityEditor;
using UnityEngine;

namespace Lumenfall.EditorTools
{
    public static class DormitoryInteriorBuilder
    {
        private const string PrefabPath = "Assets/_Project/World/Areas/Prefabs/Dormitory.prefab";
        private const string FloorMaterialPath = "Assets/_Project/Art/Environment/Walls/Materials/WoodFloor.mat";
        private const string WallMaterialPath = "Assets/_Project/Art/Environment/Walls/Wall_Wooden.mat";
        private const string DoorPath = "Assets/_Project/Art/Environment/Doors/Door.prefab";
        private const string FurnitureRoot = "Assets/_Project/Art/Environment/Furnitures/";

        [MenuItem("Lumenfall/World/Build Dormitory Interior")]
        public static void Apply()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                GridPortal portal = root.GetComponentInChildren<GridPortal>(true);
                for (int i = root.transform.childCount - 1; i >= 0; i--)
                {
                    Transform child = root.transform.GetChild(i);
                    if (portal != null && (child == portal.transform || portal.transform.IsChildOf(child))) continue;
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                }

                Material floor = AssetDatabase.LoadAssetAtPath<Material>(FloorMaterialPath);
                Material wall = AssetDatabase.LoadAssetAtPath<Material>(WallMaterialPath);
                Transform architecture = Group(root.transform, "Architecture");
                Transform furnishings = Group(root.transform, "Furnishings");
                Transform interactions = Group(root.transform, "Room Doors");

                // A 34 x 22 interior, matching the Library's usable authored footprint.
                Box("Wooden Floor", architecture, new Vector3(17.5f, -0.12f, 11.5f), new Vector3(34f, 0.2f, 22f), floor, false);
                BuildOuterWalls(architecture, wall);

                // Reception desk and the rear hallway separating public space from resident rooms.
                Box("Reception Counter", architecture, new Vector3(10f, 0.55f, 7f), new Vector3(8f, 1.1f, 1f), wall, true);
                Box("Reception Side Counter", architecture, new Vector3(6.5f, 0.55f, 8.5f), new Vector3(1f, 1.1f, 4f), wall, true);
                AddBlocker(architecture, "Reception Counter Blocker", new Vector2Int(6, 7), new Vector2Int(8, 1));
                AddBlocker(architecture, "Reception Side Blocker", new Vector2Int(6, 8), new Vector2Int(1, 3));
                Box("Hallway Divider Left", architecture, new Vector3(8f, 1.5f, 13f), new Vector3(12f, 3f, 0.35f), wall, false);
                Box("Hallway Divider Right", architecture, new Vector3(27f, 1.5f, 13f), new Vector3(14f, 3f, 0.35f), wall, false);
                AddBlocker(architecture, "Hallway Divider Left Blocker", new Vector2Int(2, 13), new Vector2Int(12, 1));
                AddBlocker(architecture, "Hallway Divider Right Blocker", new Vector2Int(20, 13), new Vector2Int(14, 1));

                // Resident-room fronts. Every doorway remains physically closed; one offers a clear placeholder response.
                int[] doorX = { 5, 11, 17, 23, 29 };
                for (int i = 0; i < doorX.Length; i++)
                {
                    int x = doorX[i];
                    Box("Room Wall " + (i + 1) + " Left", architecture, new Vector3(x - 2f, 1.5f, 18f), new Vector3(3f, 3f, 0.35f), wall, false);
                    Box("Room Wall " + (i + 1) + " Right", architecture, new Vector3(x + 2f, 1.5f, 18f), new Vector3(3f, 3f, 0.35f), wall, false);
                    AddBlocker(architecture, "Room Front Blocker " + (i + 1), new Vector2Int(Mathf.Max(1, x - 3), 18), new Vector2Int(i == 0 || i == doorX.Length - 1 ? 6 : 5, 1));
                    GameObject door = Model(DoorPath, "Room Door " + (i + 1), interactions, new Vector3(x, 0f, 17.82f), Quaternion.identity, Vector3.one);
                    if (door == null) door = Box("Room Door " + (i + 1), interactions, new Vector3(x, 1.1f, 17.82f), new Vector3(1.7f, 2.2f, 0.18f), wall, false);
                    if (i == 2) ConfigureNotReadyDoor(door);
                }

                // Resting area beside reception: two benches around a table, chairs, storage and warm lamps.
                Model(FurnitureRoot + "Bench.fbx", "Resting Bench West", furnishings, new Vector3(22f, 0f, 6f), Quaternion.Euler(0f, 90f, 0f), Vector3.one);
                Model(FurnitureRoot + "Bench.fbx", "Resting Bench East", furnishings, new Vector3(29f, 0f, 6f), Quaternion.Euler(0f, -90f, 0f), Vector3.one);
                Model(FurnitureRoot + "Table_Large.fbx", "Resting Table", furnishings, new Vector3(25.5f, 0f, 6f), Quaternion.identity, Vector3.one);
                Model(FurnitureRoot + "Chair_1.fbx", "Resting Chair North", furnishings, new Vector3(25.5f, 0f, 8.3f), Quaternion.Euler(0f, 180f, 0f), Vector3.one);
                Model(FurnitureRoot + "Chair_1.fbx", "Resting Chair South", furnishings, new Vector3(25.5f, 0f, 3.8f), Quaternion.identity, Vector3.one);
                Model(FurnitureRoot + "Cabinet.fbx", "Resting Cabinet", furnishings, new Vector3(31.5f, 0f, 10.5f), Quaternion.Euler(0f, -90f, 0f), Vector3.one);
                Model(FurnitureRoot + "Shelf_Simple.fbx", "Reception Shelf", furnishings, new Vector3(3f, 0f, 10.5f), Quaternion.Euler(0f, 90f, 0f), Vector3.one);
                AddBlocker(furnishings, "Resting Bench West Blocker", new Vector2Int(21, 6), new Vector2Int(2, 1));
                AddBlocker(furnishings, "Resting Bench East Blocker", new Vector2Int(29, 6), new Vector2Int(2, 1));
                AddBlocker(furnishings, "Resting Table Blocker", new Vector2Int(24, 5), new Vector2Int(3, 3));
                AddBlocker(furnishings, "Resting Cabinet Blocker", new Vector2Int(31, 10), new Vector2Int(2, 1));

                AddLamp(furnishings, "Reception Lamp", new Vector3(10f, 2.7f, 6f));
                AddLamp(furnishings, "Resting Lamp", new Vector3(25.5f, 2.7f, 6f));
                AddLamp(furnishings, "Hallway Lamp", new Vector3(17f, 2.7f, 15.5f));

                if (portal != null)
                {
                    portal.name = "P.B01.00";
                    portal.transform.localPosition = new Vector3(2f, 0f, 2f);
                }
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();
                Debug.Log("Built Dormitory reception, hallway, locked rooms and resting area.");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void BuildOuterWalls(Transform parent, Material material)
        {
            Box("South Wall Left", parent, new Vector3(9f, 1.5f, 0.5f), new Vector3(16f, 3f, 0.5f), material, false);
            Box("South Wall Right", parent, new Vector3(27f, 1.5f, 0.5f), new Vector3(14f, 3f, 0.5f), material, false);
            Box("North Wall", parent, new Vector3(17.5f, 1.5f, 22.5f), new Vector3(34f, 3f, 0.5f), material, false);
            Box("West Wall", parent, new Vector3(0.5f, 1.5f, 11.5f), new Vector3(0.5f, 3f, 22f), material, false);
            Box("East Wall", parent, new Vector3(34.5f, 1.5f, 11.5f), new Vector3(0.5f, 3f, 22f), material, false);
            AddBlocker(parent, "South Wall Left Blocker", new Vector2Int(1, 1), new Vector2Int(16, 1));
            AddBlocker(parent, "South Wall Right Blocker", new Vector2Int(20, 1), new Vector2Int(15, 1));
            AddBlocker(parent, "North Wall Blocker", new Vector2Int(1, 22), new Vector2Int(34, 1));
            AddBlocker(parent, "West Wall Blocker", new Vector2Int(1, 1), new Vector2Int(1, 22));
            AddBlocker(parent, "East Wall Blocker", new Vector2Int(34, 1), new Vector2Int(1, 22));
        }

        private static Transform Group(Transform parent, string name)
        {
            GameObject group = new GameObject(name);
            group.transform.SetParent(parent, false);
            return group.transform;
        }

        private static GameObject Box(string name, Transform parent, Vector3 position, Vector3 scale, Material material, bool keepCollider)
        {
            GameObject item = GameObject.CreatePrimitive(PrimitiveType.Cube);
            item.name = name;
            item.transform.SetParent(parent, false);
            item.transform.localPosition = position;
            item.transform.localScale = scale;
            if (material != null) item.GetComponent<Renderer>().sharedMaterial = material;
            if (!keepCollider) UnityEngine.Object.DestroyImmediate(item.GetComponent<Collider>());
            return item;
        }

        private static void AddBlocker(Transform parent, string name, Vector2Int cell, Vector2Int footprint)
        {
            GameObject host = new GameObject(name);
            host.transform.SetParent(parent, false);
            host.transform.localPosition = new Vector3(cell.x, 0f, cell.y);
            GridObstacle obstacle = host.AddComponent<GridObstacle>();
            SerializedObject serialized = new SerializedObject(obstacle);
            serialized.FindProperty("useTransformAsAnchor").boolValue = true;
            serialized.FindProperty("footprintSize").vector2IntValue = footprint;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static GameObject Model(string path, string name, Transform parent, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (source == null) { Debug.LogWarning("Missing Dormitory model: " + path); return null; }
            GameObject item = (GameObject)PrefabUtility.InstantiatePrefab(source);
            item.name = name;
            item.transform.SetParent(parent, false);
            item.transform.localPosition = position;
            item.transform.localRotation = rotation;
            item.transform.localScale = scale;
            return item;
        }

        private static void ConfigureNotReadyDoor(GameObject door)
        {
            BuildingSignInteraction interaction = door.AddComponent<BuildingSignInteraction>();
            SerializedObject serialized = new SerializedObject(interaction);
            serialized.FindProperty("buildingName").stringValue = "Room's not ready";
            serialized.FindProperty("panelTitle").stringValue = "Dormitory Room";
            serialized.FindProperty("promptLabel").stringValue = "Check";
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AddLamp(Transform parent, string name, Vector3 position)
        {
            GameObject lamp = new GameObject(name);
            lamp.transform.SetParent(parent, false);
            lamp.transform.localPosition = position;
            Light light = lamp.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.72f, 0.42f);
            light.intensity = 1.4f;
            light.range = 8f;
            light.shadows = LightShadows.Soft;
        }
    }
}
