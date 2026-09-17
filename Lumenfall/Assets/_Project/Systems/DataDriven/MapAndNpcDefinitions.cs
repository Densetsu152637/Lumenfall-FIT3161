using System;
using UnityEngine;

namespace Lumenfall.DataDriven
{
    [Serializable] public sealed class MapDefinition { public int schemaVersion = 1; public string id; public int width; public int height; public string tileFile; public string portalGlyph; public SpawnDefinition spawn; public MapRegionDefinition[] regions = Array.Empty<MapRegionDefinition>(); public BuildingPlacementDefinition B01; public BuildingPlacementDefinition B02; public BuildingPlacementDefinition B03; public BuildingPlacementDefinition B04; public MapObjectDefinition[] objects = Array.Empty<MapObjectDefinition>(); public PortalDefinition[] portals = Array.Empty<PortalDefinition>(); }
    [Serializable] public sealed class SpawnDefinition { public int x; public int y; }
    [Serializable] public sealed class MapRegionDefinition { public string id; public string name; public MapRectDefinition rect; public bool walkable; }
    [Serializable] public sealed class MapRectDefinition { public int x; public int y; public int width = 1; public int height = 1; }
    [Serializable] public sealed class BuildingPlacementDefinition { public int x; public int y; public int width = 1; public int height = 1; }
    [Serializable] public sealed class MapManifestDefinition { public string schema; public MapAreaDefinition[] areas = Array.Empty<MapAreaDefinition>(); }
    [Serializable] public sealed class MapAreaDefinition { public string id; public string mapId; public ManifestPortalDefinition[] portals = Array.Empty<ManifestPortalDefinition>(); }
    [Serializable] public sealed class ManifestPortalDefinition { public string id; public ManifestCellDefinition cell; public ManifestDestinationDefinition destination; public string state; }
    [Serializable] public sealed class ManifestDestinationDefinition { public string mapId; public string regionId; public ManifestCellDefinition cell; }
    [Serializable] public sealed class ManifestCellDefinition { public int x; public int y; }
    [Serializable] public sealed class MapObjectDefinition { public string id; public string asset; public string type = "building"; public int x; public int y; public int width = 1; public int height = 1; public float elevation; public float rotation; public bool blocks = true; }
    [Serializable] public sealed class PortalDefinition { public string id; public int x; public int y; public int width = 1; public int height = 1; public string destinationScene; public string destinationPortalId; public int arrivalOffsetX; public int arrivalOffsetY; }
    [Serializable] public sealed class NpcDatabaseDefinition { public NpcDefinition[] npcs = Array.Empty<NpcDefinition>(); public NpcLayerDefinition[] layers = Array.Empty<NpcLayerDefinition>(); }
    [Serializable] public sealed class NpcLayerDefinition { public string mapId; public string tileFile; public int width; public int height; }
    [Serializable] public sealed class NpcDefinition { public string id; public string displayName; public string asset; public string mapId; public int x; public int y; public string layerSymbol; public string showWhenQuestCompleted; public string hideWhenQuestCompleted; public string[] dialogueIds = Array.Empty<string>(); public NpcDialogueDefinition[] dialogue = Array.Empty<NpcDialogueDefinition>(); }
    [Serializable] public sealed class NpcDialogueDefinition { public string dialogueId; public string questId; public string requiredQuestStatus; public string prerequisiteQuestId; public string prerequisiteQuestStatus; public int weight = 1; }
}
