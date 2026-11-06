using UnityEngine;

namespace Lumenfall.Quests
{
    /// <summary>
    /// Where the quest hint is leading: a cell in an area, a catalog NPC (wherever they stand), or just an area to travel
    /// to. <see cref="QuestGuidanceHud"/> turns it into the on-screen arrow and the map marker, routing through the doors
    /// between areas when the place is somewhere else.
    /// </summary>
    public readonly struct QuestWaypoint
    {
        /// <summary>The area the place is in; null for an NPC, whose area comes from the NPC catalog.</summary>
        public readonly string MapId;
        public readonly Vector2Int Cell;
        /// <summary>A catalog NPC record; their live position wins over the catalog cell in the current area.</summary>
        public readonly string NpcId;
        /// <summary>True when only the area matters, such as "travel to the Library"; there is no cell inside it.</summary>
        public readonly bool AreaOnly;
        public readonly string Label;

        private QuestWaypoint(string mapId, Vector2Int cell, string npcId, bool areaOnly, string label)
        {
            MapId = mapId;
            Cell = cell;
            NpcId = npcId;
            AreaOnly = areaOnly;
            Label = label;
        }

        public static QuestWaypoint AtCell(string mapId, Vector2Int cell, string label) => new QuestWaypoint(mapId, cell, null, false, label);
        public static QuestWaypoint AtNpc(string npcId) => new QuestWaypoint(null, default, npcId, false, null);
        public static QuestWaypoint InArea(string mapId, string label) => new QuestWaypoint(mapId, default, null, true, label);
    }
}
