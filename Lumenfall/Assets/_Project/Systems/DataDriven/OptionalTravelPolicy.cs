using System;
using Lumenfall.GameEvents;
using UnityEngine;

namespace Lumenfall.DataDriven
{
    [Serializable]
    public sealed class OptionalWaypointDefinition
    {
        public bool enabled;
        public string id;
        public string mapId;
        public string regionId;
        public string destinationId;
        public Vector2Int arrival;
        public string[] requiredFlags = Array.Empty<string>();
    }

    public static class OptionalTravelPolicy
    {
        public static OptionalWaypointDefinition[] DisabledChapterOneWaypoints()
        {
            return new[]
            {
                new OptionalWaypointDefinition { id = "D03", mapId = "B04.Ground", regionId = "D03",
                    destinationId = "W07", arrival = new Vector2Int(20, 3), requiredFlags = new[] { "chapter1.first_account" } },
                new OptionalWaypointDefinition { id = "W07", mapId = "B02.Ground", regionId = "W07",
                    destinationId = "D03", arrival = new Vector2Int(20, 8), requiredFlags = new[] { "chapter1.first_account" } }
            };
        }

        public static string VisitFlag(string waypointId) => "travel.waypoint.visited." + waypointId;
        public static bool GateAllows(PortalContentAsset portal, GameEventRuntime runtime)
        {
            if (portal == null) return false;
            if (!portal.reserved) return RequiredFlagsAllow(portal.requiredFlags, runtime);
            return portal.allowReservedTravel && !string.IsNullOrWhiteSpace(portal.allowedFlag) &&
                runtime != null && runtime.GetFlag(portal.allowedFlag) &&
                RequiredFlagsAllow(portal.requiredFlags, runtime);
        }
        public static bool RequiredFlagsAllow(string[] flags, GameEventRuntime runtime)
        {
            foreach (string flag in flags ?? Array.Empty<string>())
                if (runtime == null || string.IsNullOrWhiteSpace(flag) || !runtime.GetFlag(flag)) return false;
            return true;
        }
        public static bool WaypointAllows(OptionalWaypointDefinition source, OptionalWaypointDefinition destination,
            GameEventRuntime runtime)
        {
            if (source == null || destination == null || runtime == null || !source.enabled || !destination.enabled ||
                string.IsNullOrWhiteSpace(source.id) || string.IsNullOrWhiteSpace(destination.id) ||
                source.id == destination.id || source.destinationId != destination.id ||
                destination.destinationId != source.id || (source.requiredFlags?.Length ?? 0) == 0 ||
                (destination.requiredFlags?.Length ?? 0) == 0) return false;
            // The Chapter 1 pair is limited to safe alcoves and requires the real completed introduction.
            bool chapterPair = (source.id == "D03" && destination.id == "W07") ||
                (source.id == "W07" && destination.id == "D03");
            if (!chapterPair) return false;
            if (!runtime.GetFlag("chapter1.first_account") ||
                !runtime.HasEventTriggered("chapter1.first_account") ||
                !IsChapterEndpoint(source) || !IsChapterEndpoint(destination)) return false;
            return runtime.GetFlag(VisitFlag(source.id)) && runtime.GetFlag(VisitFlag(destination.id)) &&
                RequiredFlagsAllow(source.requiredFlags, runtime) && RequiredFlagsAllow(destination.requiredFlags, runtime);
        }
        private static bool IsChapterEndpoint(OptionalWaypointDefinition endpoint)
        {
            return endpoint.id == endpoint.regionId &&
                endpoint.mapId == (endpoint.id == "D03" ? "B04.Ground" : "B02.Ground");
        }
        public static bool RegionContains(MapDefinition map, string regionId, Vector2Int cell)
        {
            foreach (MapRegionDefinition region in map?.regions ?? Array.Empty<MapRegionDefinition>())
            {
                MapRectDefinition r = region?.rect;
                if (region?.id == regionId && region.walkable && r != null)
                    return cell.x >= r.x && cell.y >= r.y &&
                        cell.x < r.x + r.width && cell.y < r.y + r.height;
            }
            return false;
        }
    }
}
