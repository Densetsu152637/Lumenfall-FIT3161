using System;
using UnityEngine;

namespace Lumenfall.DataDriven
{
    [Serializable] public sealed class MapDefinition { public int schemaVersion = 1; public string id; public int width; public int height; public string tileFile; public string portalGlyph; public SpawnDefinition spawn; public MapRegionDefinition[] regions = Array.Empty<MapRegionDefinition>(); public BuildingPlacementDefinition B01; public BuildingPlacementDefinition B02; public BuildingPlacementDefinition B03; public BuildingPlacementDefinition B04; public GameObject B01Prefab; public GameObject B02Prefab; public GameObject B03Prefab; public GameObject B04Prefab; public MapObjectDefinition[] objects = Array.Empty<MapObjectDefinition>(); public PuzzleTargetDefinition[] puzzleTargets = Array.Empty<PuzzleTargetDefinition>(); public PortalDefinition[] portals = Array.Empty<PortalDefinition>(); }
    [Serializable] public sealed class SpawnDefinition { public int x; public int y; }
    [Serializable] public sealed class MapRegionDefinition { public string id; public string name; public MapRectDefinition rect; public bool walkable; }
    [Serializable] public sealed class MapRectDefinition { public int x; public int y; public int width = 1; public int height = 1; }
    [Serializable] public sealed class BuildingPlacementDefinition { public int x; public int y; public int width = 1; public int height = 1; }
    [Serializable] public sealed class MapManifestDefinition { public string schema; public MapAreaDefinition[] areas = Array.Empty<MapAreaDefinition>(); }
    [Serializable] public sealed class MapAreaDefinition { public string id; public string mapId; public ManifestPortalDefinition[] portals = Array.Empty<ManifestPortalDefinition>(); }
    [Serializable] public sealed class ManifestPortalDefinition { public string id; public ManifestCellDefinition cell; public ManifestDestinationDefinition destination; public string state; }
    [Serializable] public sealed class ManifestDestinationDefinition { public string mapId; public string regionId; public ManifestCellDefinition cell; }
    [Serializable] public sealed class ManifestCellDefinition { public int x; public int y; }
    [Serializable] public sealed class MapObjectDefinition { public string id; public GameObject prefab; public string type = "building"; public int x; public int y; public int width = 1; public int height = 1; public float elevation; public float rotation; public bool blocks = true; public string[] tags = Array.Empty<string>(); public bool compatibleWithSpells = true; }
    [Serializable] public sealed class PortalDefinition { public string id; public int x; public int y; public int width = 1; public int height = 1; public PortalContentAsset destination; public Vector2Int arrivalOffset; public bool reserved; }
    [Serializable] public sealed class PuzzleTargetDefinition { public string id; public string[] objectIds = Array.Empty<string>(); public string[] requiredConnections = Array.Empty<string>(); public float minimumEffectivePower; public string conditionMode = "All"; public bool requiredPowered; public bool requiredConnected; public bool requiredRevealed; public bool requiredFrozen; public bool requiredAligned; public int requiredRotation = -1; public int requiredPositionX = int.MinValue; public int requiredPositionY = int.MinValue; }
    [Serializable] public sealed class NpcDatabaseDefinition { public NpcDefinition[] npcs = Array.Empty<NpcDefinition>(); public NpcLayerDefinition[] layers = Array.Empty<NpcLayerDefinition>(); }
    [Serializable] public sealed class NpcLayerDefinition { public string mapId; public string tileFile; public int width; public int height; [TextArea] public string[] rows = Array.Empty<string>(); }
    [Serializable] public sealed class NpcDefinition { public string id; public string displayName; public GameObject prefab; public string mapId; public int x; public int y; public string layerSymbol; public string showWhenQuestCompleted; public string hideWhenQuestCompleted; public string[] dialogueIds = Array.Empty<string>(); public NpcDialogueDefinition[] dialogue = Array.Empty<NpcDialogueDefinition>(); }
    [Serializable] public sealed class NpcDialogueDefinition { public string dialogueId; public string questId; public string requiredQuestStatus; public string prerequisiteQuestId; public string prerequisiteQuestStatus; public int weight = 1; }
}
