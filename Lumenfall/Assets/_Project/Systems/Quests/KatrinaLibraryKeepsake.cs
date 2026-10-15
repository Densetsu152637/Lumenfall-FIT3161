using System;
using Lumenfall.DataDriven;
using Lumenfall.GridMovement;
using Lumenfall.Spells;
using UnityEngine;

namespace Lumenfall.Quests
{
    /// <summary>Registers one of Katrina's three Lost Echoes items at its authored prefab marker.</summary>
    public sealed class KatrinaQuestItemMarker : MonoBehaviour
    {
        private static readonly string[] ItemIds =
        {
            "katrina.item.library",
            "katrina.item.garden",
            "katrina.item.stream"
        };

        private void OnEnable()
        {
            if (!TryGetItemIndex(gameObject.name, out int index)) return;
            DataDrivenWorldLoader loader = DataDrivenWorldLoader.Instance;
            GridWorld world = FindAnyObjectByType<GridWorld>();
            WorldStateService state = WorldStateService.Instance;
            if (loader == null || loader.LoadedMap == null || world == null || state == null) return;

            string expectedMap = index == 0 ? "B04.Ground" : index == 1 ? "campus" : "westForest";
            if (!string.Equals(loader.LoadedMap.id, expectedMap, StringComparison.OrdinalIgnoreCase)) return;

            Vector2Int cell = world.WorldToCell(transform.position);
            if (!world.IsWithinBounds(cell))
            {
                Debug.LogError($"Katrina quest marker '{name}' resolves outside {expectedMap} at {cell}.", this);
                return;
            }

            state.RegisterObject(ItemIds[index], gameObject, cell, Vector2Int.one, true,
                new[] { "hidden", WorldStateService.PickupTag });
        }

        private static bool TryGetItemIndex(string markerName, out int index)
        {
            index = -1;
            if (markerName == "Kat_item_1") index = 0;
            else if (markerName == "Kat_item_2") index = 1;
            else if (markerName == "Kat_item_3") index = 2;
            return index >= 0;
        }
    }
}
