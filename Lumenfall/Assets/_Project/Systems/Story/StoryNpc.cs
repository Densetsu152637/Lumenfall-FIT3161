using System;
using Lumenfall.DataDriven;
using Lumenfall.GridMovement;
using Lumenfall.Startup;
using UnityEngine;

namespace Lumenfall.Story
{
    /// <summary>
    /// A character who only exists for part of the story, spawned at runtime from their NPC catalog record (prefab, map
    /// and cell) rather than baked into an area prefab, in the way the ice trial spawns its Cinna. The scene that owns
    /// them calls <see cref="Keep"/> every frame with whether the story wants them present. If their cell turns out to be
    /// blocked (a tree, a wall, a fountain) or is a doorway, they stand on the nearest free cell instead.
    /// </summary>
    internal sealed class StoryNpc
    {
        /// <summary>How far, in cells, to look for somewhere to stand when the authored cell cannot be used.</summary>
        public const int StandingSearchRadius = 4;

        private readonly string npcId;
        private readonly Quaternion facing;
        private GameObject instance;
        private GameObject area;
        private bool warnedMissingRecord;

        public StoryNpc(string npcId, float facingDegrees = 180f)
        {
            this.npcId = npcId;
            facing = Quaternion.Euler(0f, facingDegrees, 0f);
        }

        public GameObject Instance => instance;
        public bool IsPresent => instance != null;

        /// <summary>
        /// Spawns the character on their map when <paramref name="wanted"/>, and removes them otherwise. They are also
        /// removed while the startup menu is open, because saves load then and their blocker could sit on a saved player
        /// cell, and they wait for a player standing on their cell to step off it.
        /// </summary>
        public void Keep(bool wanted)
        {
            DataDrivenWorldLoader loader = DataDrivenWorldLoader.Instance;
            NpcDefinition record = loader == null ? null : Array.Find(loader.LoadedNpcs?.npcs ?? Array.Empty<NpcDefinition>(),
                npc => npc != null && npc.id == npcId);
            bool onTheirMap = record != null && loader.LoadedMap != null &&
                string.Equals(loader.LoadedMap.id, record.mapId, StringComparison.OrdinalIgnoreCase);
            if (!wanted || StartupMenu.IsOpen || !onTheirMap)
            {
                Remove();
                if (wanted && loader != null && loader.LoadedNpcs != null && record == null) WarnMissing();
                return;
            }

            GameObject current = loader.CurrentAreaRoot;
            if (current == null || (instance != null && area == current)) return;
            Remove();

            GridMover player = Player();
            GridWorld world = player != null && player.World != null ? player.World : UnityEngine.Object.FindAnyObjectByType<GridWorld>();
            if (record.prefab == null || world == null)
            {
                WarnMissing();
                return;
            }

            Vector2Int cell = new Vector2Int(record.x, record.y);
            if (player != null && (player.CurrentCell == cell || player.DestinationCell == cell)) return;
            if (!TryFindStandingCell(world, cell, out cell))
            {
                if (!warnedMissingRecord) Debug.LogWarning($"'{npcId}' has nowhere to stand near ({record.x},{record.y}) on {record.mapId}.");
                warnedMissingRecord = true;
                return;
            }

            // A child of the area object, so they switch off (with their blocker) whenever another area is loaded.
            instance = UnityEngine.Object.Instantiate(record.prefab, current.transform);
            instance.name = record.displayName;
            instance.transform.position = world.CellToWorld(cell);
            instance.transform.rotation = facing;
            instance.AddComponent<DynamicNpc>().Configure(record, null);
            GameObject blocker = new GameObject(record.displayName + " obstacle");
            blocker.transform.SetParent(instance.transform, false);
            blocker.AddComponent<GridObstacle>().Configure(world, Vector2Int.one);
            area = current;
        }

        /// <summary>
        /// The authored cell if it is free and not a doorway, otherwise the nearest such cell, searching outward ring by ring.
        /// </summary>
        internal static bool TryFindStandingCell(GridWorld world, Vector2Int wanted, out Vector2Int found)
        {
            for (int radius = 0; radius <= StandingSearchRadius; radius++)
                for (int dx = -radius; dx <= radius; dx++)
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != radius) continue;
                        Vector2Int cell = wanted + new Vector2Int(dx, dy);
                        if (!world.IsCellAvailable(cell) || world.GetPortalAt(cell) != null) continue;
                        found = cell;
                        return true;
                    }
            found = wanted;
            return false;
        }

        public void Remove()
        {
            if (instance != null) UnityEngine.Object.Destroy(instance);
            instance = null;
            area = null;
        }

        private void WarnMissing()
        {
            if (warnedMissingRecord) return;
            warnedMissingRecord = true;
            Debug.LogError($"The NPC catalog has no '{npcId}' record with a prefab, so this story character cannot appear.");
        }

        internal static GridMover Player()
        {
            GridPlayerInput input = UnityEngine.Object.FindAnyObjectByType<GridPlayerInput>();
            return input == null ? null : input.GetComponent<GridMover>();
        }
    }
}
