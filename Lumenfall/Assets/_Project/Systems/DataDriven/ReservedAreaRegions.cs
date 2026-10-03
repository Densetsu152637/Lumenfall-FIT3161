using System;
using System.Collections.Generic;
using Lumenfall.GridMovement;
using UnityEngine;

namespace Lumenfall.DataDriven
{
    /// <summary>Enforces authored closed regions through the existing grid obstacle authority.</summary>
    [DisallowMultipleComponent]
    public sealed class ReservedAreaRegions : MonoBehaviour
    {
        [SerializeField] private WorldAreaAsset area;
        private readonly List<MapRegionDefinition> closedRegions = new List<MapRegionDefinition>();
        private GridWorld world;
        private bool initialized;
        private GridMover player;
        private GridPortal[] entrances = Array.Empty<GridPortal>();

        private void OnEnable()
        {
            if (initialized) return;
            world = GetComponentInParent<GridWorld>();
            if (world == null) world = FindAnyObjectByType<GridWorld>();
            if (area == null || area.definition == null || world == null)
            {
                Debug.LogError("Reserved area regions require an authored area and GridWorld.", this);
                return;
            }
            foreach (MapRegionDefinition region in area.definition.regions ?? Array.Empty<MapRegionDefinition>())
            {
                if (region == null || region.walkable || region.rect == null) continue;
                MapRectDefinition rect = region.rect;
                if (rect.width <= 0 || rect.height <= 0 || rect.x < 0 || rect.y < 0 ||
                    rect.x + rect.width > area.definition.width || rect.y + rect.height > area.definition.height)
                {
                    Debug.LogError($"Reserved region '{region.id}' has an invalid footprint.", this);
                    continue;
                }
                GameObject gate = new GameObject(region.id + " - Reserved");
                gate.SetActive(false);
                gate.transform.SetParent(transform, false);
                gate.transform.position = world.CellToWorld(new Vector2Int(rect.x, rect.y));
                gate.AddComponent<GridObstacle>().Configure(world, new Vector2Int(rect.width, rect.height));
                gate.SetActive(true);
                closedRegions.Add(region);
            }
            GridPlayerInput input = FindAnyObjectByType<GridPlayerInput>();
            player = input == null ? null : input.GetComponent<GridMover>();
            entrances = GetComponentsInChildren<GridPortal>(true);
            initialized = true;
        }

        private void OnGUI()
        {
            if (!initialized || world == null || Lumenfall.Startup.StartupMenu.IsOpen) return;
            if (player == null || player.World != world) return;
            foreach (GridPortal entrance in entrances)
            {
                if (entrance == null || entrance.Content == null || entrance.Content.reserved ||
                    entrance.Content.destination == null || entrance.Content.destination.area == null ||
                    entrance.Content.destination.area.definition == null) continue;
                Vector2Int delta = entrance.FirstCell - player.CurrentCell;
                if (Mathf.Abs(delta.x) + Mathf.Abs(delta.y) > 2) continue;
                string destination = entrance.Content.destination.area.definition.id;
                if (destination == "B01.Ground") destination = "B01 Reception";
                else if (destination == "B02.Ground") destination = "B02 Teaching Hall";
                else if (destination == "B03.Ground") destination = "B03 Shared Hall";
                else if (destination == "B04.Ground") destination = "B04 Sirius Dormitory";
                GUI.Box(new Rect(16, Screen.height - 62, Mathf.Min(520, Screen.width - 32), 44),
                    destination + "\nWalk onto the entrance to travel");
                return;
            }
            foreach (MapRegionDefinition region in closedRegions)
            {
                MapRectDefinition rect = region.rect;
                Vector2Int cell = player.CurrentCell;
                int dx = Mathf.Max(rect.x - cell.x, cell.x - (rect.x + rect.width - 1), 0);
                int dy = Mathf.Max(rect.y - cell.y, cell.y - (rect.y + rect.height - 1), 0);
                if (dx + dy > 2) continue;
                GUI.Box(new Rect(16, Screen.height - 62, Mathf.Min(520, Screen.width - 32), 44),
                    region.name + "\nClosed for Chapter 1");
                return;
            }
        }
    }
}
