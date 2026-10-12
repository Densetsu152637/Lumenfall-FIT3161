using System;
using UnityEngine;
using Lumenfall.DataDriven;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;

namespace Lumenfall.Quests
{
    /// <summary>
    /// Temporary practice hall for Orio's rune dais, which needs more floor than the F04 glade offers. A portal replaces
    /// the old gear at the authored forest mechanism and a return portal in the hall leads back. Both use
    /// a <see cref="GridPortal"/> configured from portal content, so walking onto one
    /// travels through <see cref="DataDrivenWorldLoader.SwitchArea"/>. The hall and its portal content are built in
    /// code; the loader resolves the hall by <see cref="MapId"/>, so saves made inside it restore normally.
    /// </summary>
    public sealed class OrioDaisRoom : MonoBehaviour
    {
        public const string MapId = "orioDaisRoom";
        public const string ForestMapId = "westForest";
        public const string ForestPadId = "mechanism.orio.rotate";
        public const int Width = 13;
        public const int Height = 12;
        public static readonly Vector2Int DaisCentre = new Vector2Int(6, 7);
        public static readonly Vector2Int ReturnPortalCell = new Vector2Int(6, 1);
        // The portal sits beside Orio on the camera-facing east side, off the south approach used to talk to him.
        private const string OrioNpcId = "orio";
        private static readonly Vector2Int FallbackOrioCell = new Vector2Int(8, 23);
        private static readonly Vector2Int ForestPortalOffsetFromOrio = Vector2Int.right;
        private static readonly Vector2Int[] ForestArrivalOffsets =
        {
            Vector2Int.down, Vector2Int.left, Vector2Int.right, Vector2Int.up,
            new Vector2Int(-1, -1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(1, 1)
        };
        private const string QuestId = "orio_patch_notes";
        private const string ForestPortalId = "P.F04.ORIO.HALL";
        private const string HallPortalId = "P.ORIO.HALL.F04";

        private static readonly Color FloorColor = new Color(0.2f, 0.19f, 0.24f);
        private static readonly Color WallColor = new Color(0.3f, 0.28f, 0.34f);
        private static readonly Color PathColor = new Color(0.26f, 0.2f, 0.36f);
        private static readonly Color LanternColor = new Color(1f, 0.78f, 0.45f);

        private static OrioDaisRoom instance;
        private static WorldAreaAsset area;
        private static PortalContentAsset forestPortalContent;
        private static PortalContentAsset hallPortalContent;
        private GameObject forestPortal;
        private GameObject hallPortal;
        private Transform forestRing;
        private Transform hallRing;
        private string currentMapId;

        /// <summary>Runtime area for the hall; the loader resolves it by <see cref="MapId"/>, including when a save is loaded.</summary>
        public static WorldAreaAsset GetOrCreateArea()
        {
            if (area != null) return area;
            area = ScriptableObject.CreateInstance<WorldAreaAsset>();
            area.name = "Orio Rune Hall (runtime)";
            area.hideFlags = HideFlags.DontSave;
            area.definition = new MapDefinition
            {
                id = MapId, width = Width, height = Height,
                spawn = new SpawnDefinition { x = ReturnPortalCell.x, y = ReturnPortalCell.y + 1 }
            };
            area.areaPrefab = BuildHallTemplate();
            return area;
        }

        /// <summary>Called by the area loader on every map load, before EndMapRebuild.</summary>
        public static void RegisterForMap(string mapId, GridWorld world, GameObject areaRoot)
        {
            if (instance == null)
            {
                GameObject host = new GameObject("[Orio Dais Room]");
                instance = host.AddComponent<OrioDaisRoom>();
                DontDestroyOnLoad(host);
            }
            instance.currentMapId = mapId;
            if (world != null && EnsurePortalContent())
            {
                if (IsMap(mapId, ForestMapId)) instance.BindForest(world, areaRoot);
                else if (IsMap(mapId, MapId)) instance.hallRing = Bind(ref instance.hallPortal, "Orio Hall Return Portal", hallPortalContent, world, instance.transform, ReturnPortalCell);
            }
            instance.RefreshVisibility();
        }

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        private void Update()
        {
            RefreshVisibility();
            foreach (Transform ring in new[] { forestRing, hallRing })
                if (ring != null && ring.gameObject.activeInHierarchy) ring.Rotate(0f, 85f * Time.unscaledDeltaTime, 0f, Space.Self);
        }

        private void RefreshVisibility()
        {
            // The forest portal opens once Orio has explained the dais; the hall's return portal is always open.
            GameEventRuntime runtime = GameEventManager.Instance?.Runtime;
            bool forestOpen = IsMap(currentMapId, ForestMapId) && runtime != null && runtime.GetQuestStatus(QuestId) != QuestStatus.NotStarted;
            SetActive(forestPortal, forestOpen);
            SetActive(hallPortal, IsMap(currentMapId, MapId));
        }

        private void BindForest(GridWorld world, GameObject areaRoot)
        {
            // The authored mechanism keeps its placement and save identity but no longer shows its placeholder cube.
            if (areaRoot != null)
                foreach (WorldObjectRegistration registration in areaRoot.GetComponentsInChildren<WorldObjectRegistration>(true))
                    if (string.Equals(registration.ObjectId, ForestPadId, StringComparison.Ordinal))
                    {
                        Renderer placeholder = registration.GetComponent<Renderer>();
                        if (placeholder != null) placeholder.enabled = false;
                    }
            Vector2Int orio = OrioCell();
            Vector2Int cell = ChooseForestPortalCell(world, orio);
            forestRing = Bind(ref forestPortal, "Orio Forest Portal", forestPortalContent, world, transform, cell);
        }

        /// <summary>Orio's authored forest cell from the loaded NPC catalogue, so the portal follows him if he is moved.</summary>
        private static Vector2Int OrioCell()
        {
            foreach (NpcDefinition npc in DataDrivenWorldLoader.Instance?.LoadedNpcs?.npcs ?? Array.Empty<NpcDefinition>())
                if (npc != null && string.Equals(npc.id, OrioNpcId, StringComparison.Ordinal) && IsMap(npc.mapId, ForestMapId))
                    return new Vector2Int(npc.x, npc.y);
            return FallbackOrioCell;
        }

        // The live GridPortal transform owns placement. Returning onto its cell is safe because
        // GridMover suppresses re-entry until the player leaves the destination portal.
        private static Vector2Int ChooseForestPortalCell(GridWorld world, Vector2Int orio)
        {
            Vector2Int preferred = orio + ForestPortalOffsetFromOrio;
            if (world.IsCellAvailable(preferred)) return preferred;
            foreach (Vector2Int offset in ForestArrivalOffsets)
                if (preferred + offset != orio && world.IsCellAvailable(preferred + offset))
                    return preferred + offset;
            Debug.LogWarning($"No free cell beside Orio's forest portal at {preferred}; returning from the hall will fail.");
            return preferred;
        }

        /// <summary>Builds the portal once, then reconfigures its live position for the freshly loaded world.</summary>
        private static Transform Bind(ref GameObject portalObject, string name, PortalContentAsset content, GridWorld world, Transform parent, Vector2Int cell)
        {
            Transform ring;
            if (portalObject == null)
            {
                portalObject = new GameObject(name);
                portalObject.transform.SetParent(parent, false);
                portalObject.SetActive(false);
                portalObject.AddComponent<GridPortal>();
                ring = CreateRing(portalObject.transform);
            }
            else ring = portalObject.transform.Find("Portal ring");
            portalObject.transform.position = world.CellToWorld(cell, 0.03f);
            portalObject.GetComponent<GridPortal>().Configure(content, world);
            return ring;
        }

        /// <summary>
        /// The two portals point at each other. The destination GridPortal owns its arrival cell,
        /// and GridMover suppresses immediate re-entry until the player steps off it.
        /// </summary>
        private static bool EnsurePortalContent()
        {
            if (forestPortalContent != null) return true;
            WorldAreaAsset forest = DataDrivenWorldLoader.Instance?.GetArea(ForestMapId);
            if (forest == null) { Debug.LogError($"Orio's dais portal needs the '{ForestMapId}' area in the world loader."); return false; }
            forestPortalContent = CreatePortalContent(ForestPortalId, forest);
            hallPortalContent = CreatePortalContent(HallPortalId, GetOrCreateArea());
            forestPortalContent.destination = hallPortalContent;
            hallPortalContent.destination = forestPortalContent;
            return true;
        }

        private static PortalContentAsset CreatePortalContent(string id, WorldAreaAsset owner)
        {
            PortalContentAsset content = ScriptableObject.CreateInstance<PortalContentAsset>();
            content.name = id + " (runtime)";
            content.hideFlags = HideFlags.DontSave;
            content.id = id;
            content.area = owner;
            return content;
        }

        private static Transform CreateRing(Transform parent)
        {
            // Same ring as the Stella return portal so players recognise it as a way through.
            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            visual.name = "Portal ring";
            visual.transform.SetParent(parent, false);
            visual.transform.localPosition = new Vector3(0f, 0.08f, 0f);
            visual.transform.localScale = new Vector3(0.82f, 0.06f, 0.82f);
            Collider collider = visual.GetComponent<Collider>();
            if (collider != null) DestroyImmediate(collider);
            Renderer renderer = visual.GetComponent<Renderer>();
            renderer.material.color = new Color(0.36f, 0.82f, 1f);
            renderer.material.SetColor("_EmissionColor", new Color(0.12f, 0.62f, 1f) * 2.2f);
            renderer.material.EnableKeyword("_EMISSION");
            return visual.transform;
        }

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active) target.SetActive(active);
        }

        private static bool IsMap(string mapId, string expected) => string.Equals(mapId, expected, StringComparison.OrdinalIgnoreCase);

        private static GameObject BuildHallTemplate()
        {
            // An inactive template: the loader instantiates and activates it like an authored area prefab.
            GameObject template = new GameObject("Orio Rune Hall");
            template.SetActive(false);
            DontDestroyOnLoad(template);
            Transform root = template.transform;

            // Area prefabs place a cell (x, y) at local (x, 0, y), and the map bounds keep the player inside the walls.
            Vector3 centre = new Vector3((Width - 1) * 0.5f, 0f, (Height - 1) * 0.5f);
            CreatePart(PrimitiveType.Cube, "Floor", root, centre + new Vector3(0f, -0.05f, 0f), new Vector3(Width, 0.1f, Height), FloorColor);
            CreatePart(PrimitiveType.Cube, "Path", root, new Vector3(ReturnPortalCell.x, 0.005f, (ReturnPortalCell.y + DaisCentre.y - 3.6f) * 0.5f),
                new Vector3(1.2f, 0.01f, DaisCentre.y - 3.6f - ReturnPortalCell.y), PathColor);
            const float wallHeight = 1.4f, thickness = 0.3f;
            CreatePart(PrimitiveType.Cube, "Wall North", root, new Vector3(centre.x, wallHeight * 0.5f, Height - 0.5f + thickness * 0.5f), new Vector3(Width + thickness * 2f, wallHeight, thickness), WallColor);
            CreatePart(PrimitiveType.Cube, "Wall South", root, new Vector3(centre.x, wallHeight * 0.5f, -0.5f - thickness * 0.5f), new Vector3(Width + thickness * 2f, wallHeight, thickness), WallColor);
            CreatePart(PrimitiveType.Cube, "Wall West", root, new Vector3(-0.5f - thickness * 0.5f, wallHeight * 0.5f, centre.z), new Vector3(thickness, wallHeight, Height), WallColor);
            CreatePart(PrimitiveType.Cube, "Wall East", root, new Vector3(Width - 0.5f + thickness * 0.5f, wallHeight * 0.5f, centre.z), new Vector3(thickness, wallHeight, Height), WallColor);
            foreach (Vector3 corner in new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(Width - 0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, Height - 0.5f), new Vector3(Width - 0.5f, 0f, Height - 0.5f) })
            {
                CreatePart(PrimitiveType.Cube, "Pillar", root, corner + new Vector3(0f, wallHeight * 0.6f, 0f), new Vector3(0.5f, wallHeight * 1.2f, 0.5f), WallColor);
                CreatePart(PrimitiveType.Sphere, "Lantern", root, corner + new Vector3(0f, wallHeight * 1.2f + 0.15f, 0f), new Vector3(0.3f, 0.3f, 0.3f), LanternColor);
            }
            return template;
        }

        private static void CreatePart(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 localScale, Color color)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
            // Immediate, because the hall template is instantiated in the same frame it is built.
            Collider collider = part.GetComponent<Collider>();
            if (collider != null) DestroyImmediate(collider);
            Renderer renderer = part.GetComponent<Renderer>();
            renderer.material.color = color;
            renderer.material.SetColor("_EmissionColor", color * (name == "Lantern" ? 0.8f : 0.18f));
            renderer.material.EnableKeyword("_EMISSION");
        }
    }
}
