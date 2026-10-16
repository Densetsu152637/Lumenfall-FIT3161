using System;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Spells;
using UnityEngine;

namespace Lumenfall.Story
{
    /// <summary>
    /// Curios hidden around the maps for the player to find with Lost Echoes, kept in the journal's Miscellaneous list.
    /// Each is a hidden pickup (see <see cref="WorldPickupInteraction"/>): Lost Echoes reveals it, the talk key picks it
    /// up, and its <c>found_misc_&lt;key&gt;</c> event (in <c>Resources/GameEvents/misc_items.json</c>) sets
    /// <c>inventory.misc.&lt;key&gt;</c> and opens its found dialogue. That dialogue holds the item's name and description,
    /// which the journal reads too, so its text lives in one place. Only where each item sits is set here.
    /// </summary>
    public static class MiscCollectibles
    {
        public const string FlagPrefix = "inventory.misc.";
        public const string ObjectIdPrefix = "misc.";
        public const string FoundPrefix = "found_misc_";
        private const string MarkerPrefix = "Misc Item ";

        public readonly struct Item
        {
            public readonly string Key;
            public readonly string MapId;
            public readonly Vector2Int Cell;

            public Item(string key, string mapId, int x, int y)
            {
                Key = key;
                MapId = mapId;
                Cell = new Vector2Int(x, y);
            }
        }

        /// <summary>Every curio, in the order the journal lists them, with the map and cell it is hidden at.</summary>
        public static readonly Item[] Items =
        {
            new Item("worn_bandana", "B01.Ground", 25, 6),
            new Item("straw_hat", "B01.Ground", 4, 16),
            new Item("bouncy_plush", "B01.Ground", 3, 4),
            new Item("rubber_ducky", "C03.IceTrial", 5, 6),
            new Item("ticking_pocket_watch", "B02.Ground", 23, 20),
            new Item("picnic_basket", "campus", 38, 42),
            new Item("tea_set_for_three", "campus", 17, 74),
            new Item("piece_of_parchment", "B04.Ground", 2, 5),
            new Item("good_witch_novel", "B04.Ground", 0, 14),
            new Item("faded_polaroid", "westForest", 24, 6),
        };

        /// <summary>
        /// Curios a secret hands over rather than hides (see <see cref="SecretRooms"/>). They have no map or cell; the
        /// giver's dialogue triggers the same <c>found_misc_&lt;key&gt;</c> event, so the journal lists them like the rest.
        /// </summary>
        public static readonly string[] SecretRewards =
        {
            "cold_cup_of_coffee", "slightly_warm_pebble", "annoying_dog_exe", "mop_for_your_troubles",
        };

        /// <summary>Every curio key, in the order the journal lists them: the hidden ones, then the secrets' rewards.</summary>
        public static System.Collections.Generic.IEnumerable<string> JournalKeys
        {
            get
            {
                foreach (Item item in Items) yield return item.Key;
                foreach (string key in SecretRewards) yield return key;
            }
        }

        public static string FlagFor(string key) => FlagPrefix + key;
        public static string FoundId(string key) => FoundPrefix + key;

        public static bool IsFound(GameEventRuntime runtime, string key) => runtime != null && runtime.GetFlag(FlagFor(key));

        /// <summary>The item's name and description, read from its found dialogue.</summary>
        public static bool TryGetText(GameEventRuntime runtime, string key, out string name, out string description)
        {
            name = null;
            description = null;
            if (runtime == null || !runtime.Catalog.TryGetDialogue(FoundId(key), out DialogueDefinition dialogue)) return false;
            foreach (GameMetadataEntry entry in dialogue.metadata ?? Array.Empty<GameMetadataEntry>())
                if (entry != null && entry.key == "itemName") name = entry.value;
            foreach (DialogueNodeDefinition node in dialogue.nodes ?? Array.Empty<DialogueNodeDefinition>())
                if (node != null && node.id == "description") description = node.text;
            return !string.IsNullOrWhiteSpace(name);
        }

        /// <summary>
        /// Registers this map's curios with the world state while the map is being rebuilt, so their saved reveal and
        /// pickup state comes back. One the player already has is left out.
        /// </summary>
        public static void RegisterForMap(string mapId, GridWorld world, GameObject areaRoot)
        {
            WorldStateService state = WorldStateService.Instance;
            GameEventRuntime runtime = GameEventManager.Instance != null ? GameEventManager.Instance.Runtime : null;
            if (state == null || world == null || areaRoot == null) return;
            foreach (Item item in Items)
            {
                if (!string.Equals(item.MapId, mapId, StringComparison.OrdinalIgnoreCase) || IsFound(runtime, item.Key)) continue;
                // An empty marker in the area, reused when the area is reloaded. Pickups draw their own glow once revealed.
                Transform marker = areaRoot.transform.Find(MarkerPrefix + item.Key);
                if (marker == null)
                {
                    marker = new GameObject(MarkerPrefix + item.Key).transform;
                    marker.SetParent(areaRoot.transform, false);
                }
                marker.position = world.CellToWorld(item.Cell);
                state.RegisterObject(ObjectIdPrefix + item.Key, marker.gameObject, item.Cell, Vector2Int.one, true,
                    new[] { "hidden", WorldStateService.PickupTag, "echo:item", WorldStateService.OnPickupTagPrefix + FoundId(item.Key) });
            }
        }
    }
}
