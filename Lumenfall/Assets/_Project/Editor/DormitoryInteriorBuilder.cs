using System;
using System.Collections.Generic;
using System.IO;
using Lumenfall.DataDriven;
using Lumenfall.GridMovement;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Lumenfall.EditorTools
{
    /// <summary>
    /// Builds the Dormitory interior (B01.Ground) into its area prefab: a lobby with a staffed reception counter, a
    /// hallway of resident-room doors and a resting area. Run it from Lumenfall > World > Build Dormitory Interior; it
    /// replaces everything in the prefab except the entrance portal. Cells are local (x, z) positions, one unit each.
    /// The entrance portal is the gap in the south wall at (18,1). The dormitory's people are placed in the prefab too,
    /// from their NPC catalog records (see <see cref="Residents"/>): the receptionists stand behind the counter at (8,8)
    /// and (12,8), where the player can talk to them across it from row 6, and Rowan waits in the lobby.
    /// </summary>
    public static class DormitoryInteriorBuilder
    {
        private const string PrefabPath = "Assets/_Project/World/Areas/Prefabs/Dormitory.prefab";
        private const string FloorMaterialPath = "Assets/_Project/Art/Environment/Walls/Materials/WoodFloor.mat";
        private const string WallMaterialPath = "Assets/_Project/Art/Environment/Walls/Wall_Wooden.mat";
        private const string DoorPath = "Assets/_Project/Art/Environment/Doors/Door.prefab";
        private const string FurnitureRoot = "Assets/_Project/Art/Environment/Furnitures/";
        private const string MaterialFolder = "Assets/_Project/Art/Environment/Materials/";
        // CC0 Kenney Furniture Kit (Assets/ThirdParty/KenneyFurnitureKit): the soft furnishings the fantasy pack lacks.
        private const string KitRoot = "Assets/ThirdParty/KenneyFurnitureKit/Models/FBX format/";
        private const string NpcCatalogPath = "Assets/_Project/World/Areas/NpcCatalog.asset";

        /// <summary>
        /// Catalog ids of the NPCs placed in the room. Each stands on the cell in their catalog record, with that record's
        /// name, model and dialogue, so moving one means changing the catalog and rebuilding.
        /// </summary>
        public static readonly string[] Residents = { "marigold", "thistle", "rowan" };

        // Height of the counter top, where its dressing is placed.
        // Low enough that the receptionists standing behind it stay in view.
        private const float CounterTop = 0.75f;

        private static readonly Color WarmLight = new Color(1f, 0.72f, 0.42f);

        /// <summary>Creating Temp/ + this file name makes the next script reload rebuild the interior once, unattended.</summary>
        public const string RequestFileName = "lumenfall-build-dormitory.request";

        [InitializeOnLoadMethod]
        private static void ApplyRequested()
        {
            string requestPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Temp", RequestFileName);
            if (!File.Exists(requestPath)) return;
            EditorApplication.delayCall += () =>
            {
                if (!File.Exists(requestPath)) return;
                File.Delete(requestPath);
                Apply();
            };
        }

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
                Transform rugs = Group(root.transform, "Rugs");
                Transform reception = Group(root.transform, "Reception");
                Transform furnishings = Group(root.transform, "Furnishings");
                Transform lighting = Group(root.transform, "Lighting");
                Transform interactions = Group(root.transform, "Room Doors");
                Transform people = Group(root.transform, "NPCs");

                // A 34 x 22 interior, matching the Library's usable authored footprint.
                Box("Wooden Floor", architecture, new Vector3(17.5f, -0.12f, 11.5f), new Vector3(34f, 0.2f, 22f), floor, false);
                BuildOuterWalls(architecture, wall);
                BuildReception(architecture, reception, wall);
                BuildHallway(architecture, furnishings, interactions, wall);
                BuildRugs(rugs);
                BuildLobby(furnishings);
                BuildRestingArea(furnishings);
                BuildLighting(lighting);
                BuildResidents(people);

                if (portal != null)
                {
                    // The entrance and exit is the gap in the south wall.
                    portal.name = "P.B01.00";
                    portal.transform.localPosition = new Vector3(18f, 0f, 1f);
                }
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();
                Debug.Log("Built the Dormitory: staffed reception, lobby, hallway of room doors and resting area.");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // ---- Zones ----

        // The receptionists and the lobby's resident, from their catalog records. Like the NPCs baked into the other area
        // prefabs, each is the record's model with a DynamicNpc holding the record, plus a one-cell blocker.
        private static void BuildResidents(Transform people)
        {
            NpcCatalogAsset catalog = AssetDatabase.LoadAssetAtPath<NpcCatalogAsset>(NpcCatalogPath);
            if (catalog == null) { Debug.LogWarning("Missing NPC catalog: " + NpcCatalogPath); return; }
            foreach (string id in Residents)
            {
                NpcDefinition record = Array.Find(catalog.npcs ?? Array.Empty<NpcDefinition>(), npc => npc != null && npc.id == id);
                if (record == null || record.prefab == null) { Debug.LogWarning("The NPC catalog has no '" + id + "' record with a prefab."); continue; }
                GameObject npc = (GameObject)PrefabUtility.InstantiatePrefab(record.prefab, people);
                npc.name = record.displayName;
                // Facing south, towards the lobby and the camera.
                npc.transform.SetLocalPositionAndRotation(new Vector3(record.x, 0f, record.y), Quaternion.Euler(0f, 180f, 0f));
                npc.AddComponent<DynamicNpc>().Configure(record, null);
                AddBlocker(npc.transform, record.displayName + " obstacle", Vector2Int.zero, Vector2Int.one);
            }
        }

        // The L-shaped counter faces the lobby. The receptionists stand behind it on row 8, and the wall behind them
        // (the hallway divider) carries a key rack and a banner, so the desk reads as the place keys come from.
        private static void BuildReception(Transform architecture, Transform reception, Material wall)
        {
            Box("Reception Counter", architecture, new Vector3(10f, CounterTop * 0.5f, 7f), new Vector3(8f, CounterTop, 1f), wall, true);
            Box("Reception Side Counter", architecture, new Vector3(6.5f, CounterTop * 0.5f, 8.5f), new Vector3(1f, CounterTop, 4f), wall, true);
            AddBlocker(architecture, "Reception Counter Blocker", new Vector2Int(6, 7), new Vector2Int(8, 1));
            AddBlocker(architecture, "Reception Side Blocker", new Vector2Int(6, 8), new Vector2Int(1, 3));

            Place(reception, "CandleStick_Triple.fbx", "Counter Candles", new Vector3(6.6f, CounterTop, 7f), 0f, 0.55f);
            Place(reception, "Book_Stack_1.fbx", "Guest Register", new Vector3(9.4f, CounterTop, 7.05f), 15f, 0.32f);
            Place(reception, "Scroll_1.fbx", "Room List", new Vector3(10.6f, CounterTop, 6.95f), 80f, 0.5f, false);
            Place(reception, "Key_Gold.fbx", "Spare Key", new Vector3(12.4f, CounterTop, 7f), 30f, 0.28f, false);
            Place(reception, "Pouch_Large.fbx", "Post Pouch", new Vector3(13.3f, CounterTop, 7.05f), 200f, 0.3f);

            // Behind the receptionists: a key rack between two shelves, under a dormitory banner.
            Place(reception, "Peg_Rack.fbx", "Key Rack", new Vector3(10f, 1.2f, 12.72f), 180f, 1.6f, false);
            Place(reception, "Key_Metal.fbx", "Rack Key A", new Vector3(9.4f, 0.98f, 12.62f), 0f, 0.22f, false);
            Place(reception, "Key_Metal.fbx", "Rack Key B", new Vector3(10.3f, 0.98f, 12.62f), 0f, 0.22f, false);
            // Bottom at 1.75 and 1.1 tall, so it clears the rack and stays under the 3-unit walls.
            Place(reception, "Banner_1.fbx", "Dormitory Banner", new Vector3(10f, 1.75f, 12.75f), 180f, 1.1f);
            Place(reception, "Bookcase_2.fbx", "Reception Bookcase", new Vector3(7.6f, 0f, 12.4f), 180f, 2.2f);
            Place(reception, "Cabinet.fbx", "Reception Cabinet East", new Vector3(13.2f, 0f, 12.3f), 180f, 1.4f);
            Place(reception, "BookGroup_Small_1.fbx", "Ledgers", new Vector3(13.2f, 1.4f, 12.2f), 180f, 0.3f);
            AddBlocker(reception, "Reception Bookcase Blocker", new Vector2Int(7, 12), new Vector2Int(2, 1));
            AddBlocker(reception, "Reception Cabinet Blocker", new Vector2Int(13, 12), new Vector2Int(1, 1));
        }

        // Resident-room fronts along row 18. Every door stays closed, and checking one says to come back later.
        private static void BuildHallway(Transform architecture, Transform furnishings, Transform interactions, Material wall)
        {
            Box("Hallway Divider Left", architecture, new Vector3(8f, 1.5f, 13f), new Vector3(12f, 3f, 0.35f), wall, false);
            Box("Hallway Divider Right", architecture, new Vector3(27f, 1.5f, 13f), new Vector3(14f, 3f, 0.35f), wall, false);
            AddBlocker(architecture, "Hallway Divider Left Blocker", new Vector2Int(2, 13), new Vector2Int(12, 1));
            AddBlocker(architecture, "Hallway Divider Right Blocker", new Vector2Int(20, 13), new Vector2Int(14, 1));

            int[] doorX = { 5, 11, 17, 23, 29 };
            for (int i = 0; i < doorX.Length; i++)
            {
                int x = doorX[i];
                Box("Room Wall " + (i + 1) + " Left", architecture, new Vector3(x - 2f, 1.5f, 18f), new Vector3(3f, 3f, 0.35f), wall, false);
                Box("Room Wall " + (i + 1) + " Right", architecture, new Vector3(x + 2f, 1.5f, 18f), new Vector3(3f, 3f, 0.35f), wall, false);
                AddBlocker(architecture, "Room Front Blocker " + (i + 1), new Vector2Int(Mathf.Max(1, x - 3), 18), new Vector2Int(i == 0 || i == doorX.Length - 1 ? 6 : 5, 1));
                GameObject door = Model(DoorPath, "Room Door " + (i + 1), interactions, new Vector3(x, 0f, 17.82f), Quaternion.identity, Vector3.one);
                if (door == null) door = Box("Room Door " + (i + 1), interactions, new Vector3(x, 1.1f, 17.82f), new Vector3(1.7f, 2.2f, 0.18f), wall, false);
                ConfigureRoomDoor(door);
            }

            // Potted plants and vases take turns between the doors; a cushioned bench and a coat stand wait by the west end.
            int[] between = { 8, 14, 20, 26 };
            for (int i = 0; i < between.Length; i++)
            {
                if (i % 2 == 0) Kit(furnishings, "pottedPlant", "Hallway Plant " + (i + 1), new Vector3(between[i], 0f, 17.25f), 30f * i, 1.1f);
                else Place(furnishings, "Vase_2.fbx", "Hallway Vase " + (i + 1), new Vector3(between[i], 0f, 17.25f), 20f * i, 0.8f);
                AddBlocker(furnishings, "Hallway Planter Blocker " + (i + 1), new Vector2Int(between[i], 17), Vector2Int.one);
            }
            Kit(furnishings, "benchCushion", "Hallway Bench", new Vector3(2.6f, 0f, 15.5f), 270f, 1.9f, false,
                RugMaterial("Dorm_Fabric_Navy", new Color(0.18f, 0.24f, 0.45f)));
            AddBlocker(furnishings, "Hallway Bench Blocker", new Vector2Int(2, 15), new Vector2Int(1, 2));
            Kit(furnishings, "coatRackStanding", "Hallway Coat Stand", new Vector3(3f, 0f, 17.1f), 0f, 1.8f);
            AddBlocker(furnishings, "Hallway Coat Stand Blocker", new Vector2Int(3, 17), Vector2Int.one);
        }

        // Rugs mark out the lobby, the lounge and the hallway. Rugs are flat, so nothing blocks on them. The hallway runner
        // is a long slab, because a model rug can only be scaled evenly.
        private static void BuildRugs(Transform rugs)
        {
            Material gold = RugMaterial("Dorm_Rug_Gold", new Color(0.78f, 0.6f, 0.28f));
            Material green = RugMaterial("Dorm_Rug_Green", new Color(0.2f, 0.38f, 0.3f));
            Material plum = RugMaterial("Dorm_Rug_Plum", new Color(0.36f, 0.2f, 0.42f));
            // In front of the counter and as wide as it (x 6 to 14), long side along the counter.
            Kit(rugs, "rugRectangle", "Lobby Rug", new Vector3(10f, 0f, 4.6f), 0f, 8f, false, plum, gold, true);
            // Centred under the lounge's coffee table.
            Kit(rugs, "rugRound", "Lounge Rug", new Vector3(25f, 0f, 8.8f), 0f, 5f, false, green, gold);
            Kit(rugs, "rugDoormat", "Entrance Mat", new Vector3(18f, 0f, 2.5f), 0f, 2.2f, false, null, null, true);
            Rug(rugs, "Hallway Runner", new Vector2(17f, 15.5f), new Vector2(26f, 1.6f), gold, green);
        }

        // The entrance is the gap in the south wall at (18,1). Potted plants frame it, and a small cabinet of pamphlets by
        // the west wall, watched over by a teddy bear, greets arrivals.
        private static void BuildLobby(Transform furnishings)
        {
            Kit(furnishings, "pottedPlant", "Entrance Plant West", new Vector3(16f, 0f, 2f), 0f, 1.2f);
            Kit(furnishings, "pottedPlant", "Entrance Plant East", new Vector3(20f, 0f, 2f), 90f, 1.2f);
            AddBlocker(furnishings, "Entrance Plant Pot West Blocker", new Vector2Int(16, 2), Vector2Int.one);
            AddBlocker(furnishings, "Entrance Plant Pot East Blocker", new Vector2Int(20, 2), Vector2Int.one);
            Place(furnishings, "Cabinet.fbx", "Welcome Cabinet", new Vector3(1.6f, 0f, 10.5f), 90f, 1.2f);
            Place(furnishings, "BookGroup_Medium_1.fbx", "Welcome Pamphlets", new Vector3(1.7f, 1.2f, 10.2f), 90f, 0.35f);
            Kit(furnishings, "bear", "Welcome Bear", new Vector3(1.7f, 1.2f, 10.9f), 90f, 0.4f);
            AddBlocker(furnishings, "Welcome Cabinet Blocker", new Vector2Int(2, 10), new Vector2Int(1, 2));
            Place(furnishings, "Bag.fbx", "Left Luggage", new Vector3(3.6f, 0f, 6.3f), 200f, 0.45f);
            AddBlocker(furnishings, "Left Luggage Blocker", new Vector2Int(4, 6), Vector2Int.one);
        }

        // A lounge by the east wall: a sofa against the hallway wall and an armchair either side, all facing a coffee table
        // on a round rug, with a floor lamp and a plant, with a bookcase and a cabinet against the wall too. The camera looks from
        // the south-east, so the sofa and the tall pieces stand against walls that face it.
        private static void BuildRestingArea(Transform furnishings)
        {
            // Sirius navy upholstery. Kenney models face south (-z) at yaw 0, so the sofa (yaw 0) faces south, the west
            // armchair (yaw 270) faces east and the east armchair (yaw 90) faces west, all towards the coffee table.
            Material navy = RugMaterial("Dorm_Fabric_Navy", new Color(0.18f, 0.24f, 0.45f));
            Kit(furnishings, "loungeSofa", "Lounge Sofa", new Vector3(25f, 0f, 11.1f), 0f, 2.8f, false, navy);
            Kit(furnishings, "loungeChair", "Lounge Armchair West", new Vector3(22.3f, 0f, 8.6f), 270f, 1.2f, false, navy);
            Kit(furnishings, "loungeChair", "Lounge Armchair East", new Vector3(27.7f, 0f, 8.6f), 90f, 1.2f, false, navy);
            GameObject coffeeTable = Kit(furnishings, "tableCoffee", "Coffee Table", new Vector3(25f, 0f, 8.4f), 0f, 1.8f, false);
            Kit(furnishings, "lampRoundFloor", "Lounge Floor Lamp", new Vector3(21.3f, 0f, 11.6f), 0f, 1.9f);
            Kit(furnishings, "pottedPlant", "Lounge Plant", new Vector3(30.3f, 0f, 11.6f), 45f, 1.2f);
            Place(furnishings, "Cabinet.fbx", "Lounge Cabinet", new Vector3(31.8f, 0f, 12.35f), 180f, 1.4f);
            Place(furnishings, "Bookcase_2.fbx", "Lounge Bookcase", new Vector3(27.6f, 0f, 12.4f), 180f, 2.4f);
            // The armchairs, coffee table and sofa block as one group, gaps included: the table reaches into row 9, and a
            // player standing between the pieces looked as if they were walking through the furniture.
            AddBlocker(furnishings, "Lounge Seating Blocker", new Vector2Int(22, 8), new Vector2Int(7, 4));
            AddBlocker(furnishings, "Lounge Floor Lamp Blocker", new Vector2Int(21, 11), Vector2Int.one);
            AddBlocker(furnishings, "Lounge Plant Blocker", new Vector2Int(30, 11), Vector2Int.one);
            AddBlocker(furnishings, "Lounge Cabinet Blocker", new Vector2Int(31, 12), new Vector2Int(2, 1));
            AddBlocker(furnishings, "Lounge Bookcase Blocker", new Vector2Int(27, 12), new Vector2Int(2, 1));

            // Something left on the coffee table by whoever was here last, and a pillow on the sofa.
            float top = TopOf(coffeeTable, 0.45f);
            Place(furnishings, "CandleStick.fbx", "Table Candle", new Vector3(25f, top, 8.4f), 0f, 0.35f);
            Place(furnishings, "Book_Stack_2.fbx", "Table Books", new Vector3(24.4f, top, 8.6f), 25f, 0.24f);
            Place(furnishings, "Mug.fbx", "Table Mug", new Vector3(25.6f, top, 8.2f), 0f, 0.14f);
            Kit(furnishings, "pillowBlue", "Sofa Pillow", new Vector3(24.1f, 0.55f, 11.15f), 160f, 0.55f, false);
            Place(furnishings, "BookGroup_Medium_2.fbx", "Bookcase Books", new Vector3(27.6f, 1.2f, 12.2f), 180f, 0.6f);
        }

        // Wall lanterns sit only on walls that face the camera (the hallway wall's south side and the west wall), so they
        // are seen from the front and none hangs in front of the view; each has a warm light. The three original ceiling
        // lamps keep the zones lit.
        private static void BuildLighting(Transform lighting)
        {
            // Only these two cast shadows. A point light needs six shadow maps, and every light casting them overflowed
            // URP's 2048 shadow atlas, so the rest light the room without shadows.
            AddLamp(lighting, "Reception Lamp", new Vector3(10f, 2.7f, 6f), 1.4f, 8f, true);
            AddLamp(lighting, "Lounge Lamp", new Vector3(25f, 2.7f, 8.5f), 1.4f, 8f, true);
            AddLamp(lighting, "Hallway Lamp", new Vector3(17f, 2.7f, 15.5f), 1.4f, 8f);
            AddLamp(lighting, "Hallway Lamp West", new Vector3(7f, 2.6f, 15.5f), 1f, 6f);
            AddLamp(lighting, "Hallway Lamp East", new Vector3(27f, 2.6f, 15.5f), 1f, 6f);
            AddLamp(lighting, "Entrance Lamp", new Vector3(18f, 2.4f, 2.5f), 0.9f, 5f);

            WallLantern(lighting, "Lantern West Wall", new Vector3(0.8f, 1.7f, 4.5f), 90f);
            WallLantern(lighting, "Lantern Divider West", new Vector3(4f, 1.7f, 12.75f), 180f);
            WallLantern(lighting, "Lantern Divider East A", new Vector3(24.5f, 1.7f, 12.75f), 180f);
            WallLantern(lighting, "Lantern Divider East B", new Vector3(29.8f, 1.7f, 12.75f), 180f);
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
            // Outside the entrance gap there is nothing to walk on, and a player who stepped past the exit at (18,1) missed
            // its prompt to leave, so the row beyond the south wall is blocked too.
            AddBlocker(parent, "Outside Entrance Blocker", new Vector2Int(0, 0), new Vector2Int(36, 1));
            AddBlocker(parent, "North Wall Blocker", new Vector2Int(1, 22), new Vector2Int(34, 1));
            AddBlocker(parent, "West Wall Blocker", new Vector2Int(1, 1), new Vector2Int(1, 22));
            AddBlocker(parent, "East Wall Blocker", new Vector2Int(34, 1), new Vector2Int(1, 22));
        }

        // ---- Pieces ----

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

        // A rug is a gold border slab with a coloured inner slab just above it, both a hair above the floor.
        private static void Rug(Transform parent, string name, Vector2 centre, Vector2 size, Material border, Material field)
        {
            Box(name + " Border", parent, new Vector3(centre.x, -0.01f, centre.y), new Vector3(size.x, 0.02f, size.y), border, false);
            Box(name, parent, new Vector3(centre.x, 0f, centre.y), new Vector3(size.x - 0.3f, 0.02f, size.y - 0.3f), field, false);
        }

        private static Material RugMaterial(string name, Color colour)
        {
            string path = MaterialFolder + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            material = new Material(shader) { name = name, color = colour };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", colour);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.1f);
            AssetDatabase.CreateAsset(material, path);
            return material;
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

        /// <summary>
        /// Places a furniture model at <paramref name="position"/>, turned to <paramref name="yaw"/> and scaled so its height
        /// (or, without <paramref name="scaleByHeight"/>, its largest side) is <paramref name="targetSize"/>, standing on
        /// <paramref name="position"/>.y. As in the Arcane Workshop layout tool, the model's own import rotation and scale
        /// are kept, and it is grounded by its rendered bottom because model pivots vary.
        /// </summary>
        private static GameObject Place(Transform parent, string file, string name, Vector3 position, float yaw, float targetSize, bool scaleByHeight = true)
            => PlaceModel(FurnitureRoot + file, parent, name, position, yaw, targetSize, scaleByHeight);

        /// <summary>
        /// Places a Kenney Furniture Kit model by name (without ".fbx"), as <see cref="Place"/> does, then centres it on
        /// <paramref name="position"/> by its footprint: Kenney models have their origin at a corner, so turning one would
        /// otherwise swing it away from its spot. <paramref name="fabric"/> and <paramref name="fabricStripe"/> replace the
        /// kit's coral "carpet" and "carpetDarker" materials. <paramref name="longSideAlongX"/> turns a rug so its long side
        /// runs east to west.
        /// </summary>
        private static GameObject Kit(Transform parent, string model, string name, Vector3 position, float yaw, float targetSize,
            bool scaleByHeight = true, Material fabric = null, Material fabricStripe = null, bool longSideAlongX = false)
        {
            GameObject placed = PlaceModel(KitRoot + model + ".fbx", parent, name, position, yaw, targetSize, scaleByHeight);
            if (placed == null) return null;
            Renderer[] renderers = placed.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return placed;
            if (longSideAlongX)
            {
                Bounds footprint = Encapsulate(renderers);
                if (footprint.size.z > footprint.size.x) placed.transform.rotation = Quaternion.Euler(0f, 90f, 0f) * placed.transform.rotation;
            }
            foreach (Renderer part in renderers)
            {
                Material[] materials = part.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    string material = materials[i] == null ? string.Empty : materials[i].name;
                    if (fabricStripe != null && material.StartsWith("carpetDarker", StringComparison.Ordinal)) materials[i] = fabricStripe;
                    else if (fabric != null && material == "carpet") materials[i] = fabric;
                }
                part.sharedMaterials = materials;
            }
            Bounds bounds = Encapsulate(renderers);
            placed.transform.position += new Vector3(position.x - bounds.center.x, 0f, position.z - bounds.center.z);
            return placed;
        }

        /// <summary>The height of a placed model's top, for setting things on it, or <paramref name="fallback"/> if it is missing.</summary>
        private static float TopOf(GameObject placed, float fallback)
        {
            Renderer[] renderers = placed == null ? Array.Empty<Renderer>() : placed.GetComponentsInChildren<Renderer>(true);
            return renderers.Length == 0 ? fallback : Encapsulate(renderers).max.y;
        }

        private static GameObject PlaceModel(string path, Transform parent, string name, Vector3 position, float yaw, float targetSize, bool scaleByHeight)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (source == null) { Debug.LogWarning("Missing Dormitory model: " + path); return null; }
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(source, parent);
            instance.name = name;
            Quaternion importedAxisCorrection = source.transform.localRotation;
            Vector3 importedScale = source.transform.localScale;
            instance.transform.localPosition = new Vector3(position.x, 0f, position.z);
            instance.transform.localRotation = Quaternion.Euler(0f, yaw, 0f) * importedAxisCorrection;
            instance.transform.localScale = importedScale;
            foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);

            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return instance;
            Bounds bounds = Encapsulate(renderers);
            float measured = scaleByHeight ? bounds.size.y : Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (measured > 0.001f) instance.transform.localScale = importedScale * (targetSize / measured);
            bounds = Encapsulate(renderers);
            instance.transform.position += Vector3.up * (position.y - bounds.min.y);
            return instance;
        }

        private static Bounds Encapsulate(Renderer[] renderers)
        {
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        private static void WallLantern(Transform parent, string name, Vector3 position, float yaw)
        {
            Place(parent, "Lantern_Wall.fbx", name, position, yaw, 0.6f);
            AddLamp(parent, name + " Light", position + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * 0.4f, 0.8f, 4f);
        }

        // The rooms behind the doors are not built yet.
        private const string RoomDoorText = "The devs are sleeping. Visit later!";

        private static void ConfigureRoomDoor(GameObject door)
        {
            BuildingSignInteraction interaction = door.AddComponent<BuildingSignInteraction>();
            SerializedObject serialized = new SerializedObject(interaction);
            serialized.FindProperty("buildingName").stringValue = RoomDoorText;
            serialized.FindProperty("panelTitle").stringValue = "Dormitory Room";
            serialized.FindProperty("promptLabel").stringValue = "Check";
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AddLamp(Transform parent, string name, Vector3 position, float intensity, float range, bool castShadows = false)
        {
            GameObject lamp = new GameObject(name);
            lamp.transform.SetParent(parent, false);
            lamp.transform.localPosition = position;
            Light light = lamp.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = WarmLight;
            light.intensity = intensity;
            light.range = range;
            light.shadows = castShadows ? LightShadows.Soft : LightShadows.None;
            if (!castShadows) return;
            // A point light renders six shadow maps. At URP's default High tier (1024) even one overflows the 2048
            // additional-light shadow atlas, so URP shrank them and warned; at Medium (512) both shadowed lamps fit.
            // The tier has no public setter, so it is written as the serialized field the Light inspector edits.
            SerializedObject data = new SerializedObject(light.GetUniversalAdditionalLightData());
            data.FindProperty("m_AdditionalLightsShadowResolutionTier").intValue = UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierMedium;
            data.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
