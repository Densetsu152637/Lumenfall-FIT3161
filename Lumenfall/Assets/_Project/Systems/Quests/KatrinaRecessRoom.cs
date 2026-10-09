using System;
using System.Collections.Generic;
using UnityEngine;
using Lumenfall.DataDriven;
using Lumenfall.GridMovement;
using Lumenfall.Spells;

namespace Lumenfall.Quests
{
    /// <summary>
    /// Builds Katrina's garden recess (<c>C04.Recess</c>) from its area definition and registers its hidden objects
    /// for Lost Echoes. Katrina herself stands on campus. The landing stone is a two-way portal back to campus. It
    /// sits on the area prefab root so registration runs inside the loader's map rebuild and saved reveal state is
    /// restored like any other authored object.
    /// </summary>
    public sealed class KatrinaRecessRoom : MonoBehaviour
    {
        public const string MapId = "C04.Recess";
        public const string ReturnPortalId = "P.RECESS.CAMPUS";
        private const string HiddenTag = "hidden";
        private static readonly Color FloorColor = new Color(0.22f, 0.31f, 0.2f);
        private static readonly Color HedgeColor = new Color(0.15f, 0.36f, 0.19f);
        private static readonly Color PlanterColor = new Color(0.42f, 0.28f, 0.16f);
        private static readonly Color FoliageColor = new Color(0.2f, 0.46f, 0.23f);
        private static readonly Color StoneColor = new Color(0.55f, 0.55f, 0.6f);
        private static readonly Color PortalColor = new Color(0.68f, 0.58f, 1f);

        private readonly Dictionary<string, GameObject> hiddenObjects = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        private Transform geometry;
        private GameObject portalObject;
        private Transform ring;
        private Vector2Int landingCell;
        private GridWorld world;

        private void OnEnable()
        {
            DataDrivenWorldLoader loader = DataDrivenWorldLoader.Instance;
            MapDefinition map = loader == null ? null : loader.LoadedMap;
            if (map == null || !string.Equals(map.id, MapId, StringComparison.OrdinalIgnoreCase)) return;
            world = FindAnyObjectByType<GridWorld>();
            if (world == null) return;
            if (geometry == null) Build(map, loader.CurrentArea);
            RegisterEchoObjects(map);
        }

        private void Update()
        {
            if (ring != null && portalObject != null && portalObject.activeSelf) ring.Rotate(0f, 85f * Time.unscaledDeltaTime, 0f, Space.Self);
        }

        // Hidden objects (any object tagged hidden) are registered each time the area activates so the loader's
        // pending save restore applies to them; blocking objects only need their GridObstacle.
        private void RegisterEchoObjects(MapDefinition map)
        {
            WorldStateService state = WorldStateService.Instance;
            if (state == null) return;
            foreach (MapObjectDefinition item in map.objects ?? Array.Empty<MapObjectDefinition>())
                if (item != null && hiddenObjects.TryGetValue(item.id ?? string.Empty, out GameObject view))
                    state.RegisterObject(item.id, view, new Vector2Int(item.x, item.y), new Vector2Int(Mathf.Max(1, item.width), Mathf.Max(1, item.height)), item.compatibleWithSpells, item.tags);
        }

        private void Build(MapDefinition map, WorldAreaAsset area)
        {
            geometry = new GameObject("Recess Geometry").transform;
            geometry.SetParent(transform, false);
            float size = world.CellSize;
            Vector3 centre = (world.CellToWorld(Vector2Int.zero) + world.CellToWorld(new Vector2Int(map.width - 1, map.height - 1))) * 0.5f;
            Part(PrimitiveType.Cube, "Floor", centre + Vector3.down * 0.05f, new Vector3(map.width * size, 0.1f, map.height * size), FloorColor);
            float hedgeHeight = 1.1f;
            Part(PrimitiveType.Cube, "Hedge South", centre + new Vector3(0f, hedgeHeight * 0.5f, -(map.height + 1) * 0.5f * size), new Vector3((map.width + 2) * size, hedgeHeight, size), HedgeColor);
            Part(PrimitiveType.Cube, "Hedge North", centre + new Vector3(0f, hedgeHeight * 0.5f, (map.height + 1) * 0.5f * size), new Vector3((map.width + 2) * size, hedgeHeight, size), HedgeColor);
            Part(PrimitiveType.Cube, "Hedge West", centre + new Vector3(-(map.width + 1) * 0.5f * size, hedgeHeight * 0.5f, 0f), new Vector3(size, hedgeHeight, map.height * size), HedgeColor);
            Part(PrimitiveType.Cube, "Hedge East", centre + new Vector3((map.width + 1) * 0.5f * size, hedgeHeight * 0.5f, 0f), new Vector3(size, hedgeHeight, map.height * size), HedgeColor);

            foreach (MapObjectDefinition item in map.objects ?? Array.Empty<MapObjectDefinition>())
            {
                if (item == null) continue;
                Vector2Int cell = new Vector2Int(item.x, item.y);
                Vector2Int footprint = new Vector2Int(Mathf.Max(1, item.width), Mathf.Max(1, item.height));
                if (Array.Exists(item.tags ?? Array.Empty<string>(), tag => string.Equals(tag, HiddenTag, StringComparison.OrdinalIgnoreCase)))
                    hiddenObjects[item.id] = BuildHiddenMarker(item.id, cell);
                else if (item.blocks)
                    BuildPlanter(item.id, cell, footprint, size);
            }

            PortalContentAsset content = FindPortal(area, ReturnPortalId);
            if (content == null) { Debug.LogError($"Missing authored portal content '{ReturnPortalId}'.", this); return; }
            // A GridPortal's cell is its transform position; the landing stone sits on the recess spawn cell.
            landingCell = map.spawn != null ? new Vector2Int(map.spawn.x, map.spawn.y) : new Vector2Int(7, 1);
            Part(PrimitiveType.Cylinder, "Landing Stone", world.CellToWorld(landingCell, 0.02f), new Vector3(0.9f * size, 0.02f, 0.9f * size), StoneColor);
            // Built active inside the map rebuild so the loader finds it and suppresses re-entry on arrival.
            portalObject = new GameObject("Recess Return Portal");
            portalObject.transform.SetParent(geometry, false);
            portalObject.transform.position = world.CellToWorld(landingCell, 0.03f);
            GridPortal portal = portalObject.AddComponent<GridPortal>();
            portal.Configure(content, world);
            GameObject visual = Part(PrimitiveType.Cylinder, "Return portal ring", Vector3.zero, new Vector3(0.82f * size, 0.06f, 0.82f * size), PortalColor, true);
            visual.transform.SetParent(portalObject.transform, false);
            visual.transform.localPosition = new Vector3(0f, 0.08f, 0f);
            ring = visual.transform;
        }

        private GameObject BuildHiddenMarker(string id, Vector2Int cell)
        {
            // A bare marker: a hidden item is picked up as soon as Lost Echoes finds it, so the spot keeps its unfound
            // look and nothing appears on the map after the reveal.
            GameObject root = new GameObject(id);
            root.transform.SetParent(geometry, false);
            root.transform.position = world.CellToWorld(cell);
            return root;
        }

        private void BuildPlanter(string id, Vector2Int cell, Vector2Int footprint, float size)
        {
            Vector3 origin = world.CellToWorld(cell);
            Vector3 centre = origin + new Vector3((footprint.x - 1) * 0.5f * size, 0f, (footprint.y - 1) * 0.5f * size);
            GameObject box = Part(PrimitiveType.Cube, id, centre + Vector3.up * 0.25f, new Vector3(footprint.x * size * 0.92f, 0.5f, footprint.y * size * 0.92f), PlanterColor);
            Part(PrimitiveType.Cube, id + " foliage", centre + Vector3.up * 0.6f, new Vector3(footprint.x * size * 0.8f, 0.25f, footprint.y * size * 0.8f), FoliageColor);
            GameObject blocker = new GameObject(id + " obstacle");
            blocker.transform.SetParent(box.transform.parent, false);
            blocker.transform.position = origin;
            blocker.AddComponent<GridObstacle>().Configure(world, footprint);
        }

        private GameObject Part(PrimitiveType type, string name, Vector3 position, Vector3 scale, Color color, bool emissive = false)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = name;
            Collider collider = part.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            part.transform.SetParent(geometry, false);
            part.transform.position = position;
            part.transform.localScale = scale;
            Renderer renderer = part.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = color;
                if (emissive) { renderer.material.SetColor("_EmissionColor", color * 1.6f); renderer.material.EnableKeyword("_EMISSION"); }
            }
            return part;
        }

        private static PortalContentAsset FindPortal(WorldAreaAsset area, string id)
        {
            if (area != null)
                foreach (PortalContentAsset candidate in area.portals ?? Array.Empty<PortalContentAsset>())
                    if (candidate != null && string.Equals(candidate.id, id, StringComparison.Ordinal)) return candidate;
            return null;
        }
    }
}
