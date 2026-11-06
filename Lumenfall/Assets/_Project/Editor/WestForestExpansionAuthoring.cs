using System;
using System.Collections.Generic;
using System.Linq;
using Lumenfall.DataDriven;
using Lumenfall.GridMovement;
using UnityEditor;
using UnityEngine;
using Random = System.Random;

namespace Lumenfall.EditorTools
{
    /// <summary>
    /// Grows the West Forest outward without moving anything already in it. The hand-painted 38x50 terrain, its
    /// trees, NPCs, quest cells and portals keep their coordinates; the grid bounds extend into negative x and z
    /// instead. New ground comes from a surround terrain (<see cref="SurroundTerrainPath"/>) with a hole cut exactly
    /// over the original, painted with the original's terrain layers, grass, flowers and tree prototypes.
    /// Within 1.5 m of the original terrain it blends towards the original's painting so the join does not show.
    ///
    /// The new walkable space is a south trail from the arrival clearing to a hollow, a loop west and north to a
    /// waystone clearing, and the stone trail continuing west to the deep forest portal (P.F01), which moves to
    /// its new end. Every cell next to the walkable space is blocked, so the trees stay scenery. The stream and
    /// the academy wall continue into the new ground. Everything generated sits under <see cref="GeneratedRootName"/>
    /// and is rebuilt on each run, so the tool can be rerun after tuning the layout below.
    /// Run it from Lumenfall > World > Expand West Forest, or in batch mode through <see cref="Apply"/>.
    /// </summary>
    public static class WestForestExpansionAuthoring
    {
        private const string PrefabPath = "Assets/_Project/World/Areas/Prefabs/West Forest.prefab";
        private const string AreaPath = "Assets/_Project/World/Areas/westForest.asset";
        private const string SourceTerrainPath = "Assets/_Project/Art/Environment/Terrain/WestForestTerrainData.asset";
        private const string SurroundTerrainPath = "Assets/_Project/Art/Environment/Terrain/WestForestSurroundTerrainData.asset";
        private const string GeneratedRootName = "Forest Expansion";
        private const string DeepForestPortalName = "P.F01";
        private const int Seed = 3162;

        // The expanded grid: x -27..37, z -24..49. The original map was x 0..37, z 0..49.
        private static readonly RectInt Bounds = new RectInt(-27, -24, 65, 74);
        private static readonly RectInt OriginalBounds = new RectInt(0, 0, 38, 50);
        private static readonly Vector2Int DeepForestPortalCell = new Vector2Int(-27, 25);

        // The surround terrain is square so that its 0.2 m hole texels line up with the original terrain's edges,
        // which sit at x 0..38 and z -1..49 (40 and 39 m from the surround's corner).
        private static readonly Vector2 SurroundOrigin = new Vector2(-40f, -40f);
        private const float SurroundSize = 102.4f;
        private const int HeightResolution = 513;
        private const int AlphaResolution = 1024;
        private const int DetailResolution = 1024;
        private static readonly Rect OriginalFootprint = new Rect(0f, -1f, 38f, 50f);
        private const float SeamBlendDistance = 1.5f;

        // Terrain layers of the original terrain, in order.
        private const int DirtLayer = 0, GrassLayer = 1, StoneLayer = 2;

        // Tree prototype indices of the original terrain.
        private const int AppleTree = 0, GreenTree = 1, DeadFruitTree = 2, PearTree = 3, PlumTree = 4, PineTree = 5,
            PineLogs = 6, PineStump = 7, FruitLogs = 8, DeadPine = 9, Rock = 10, RiverRocks = 11, Mushroom = 12;

        private enum Surface { Dirt, Stone }

        private sealed class Trail
        {
            public readonly Vector2[] Points;
            public readonly float WalkHalfWidth;
            public readonly float PaintHalfWidth;
            public readonly Surface Surface;
            public readonly bool Walkable;

            public Trail(Surface surface, float walkHalfWidth, float paintHalfWidth, bool walkable, params Vector2[] points)
            {
                Surface = surface; WalkHalfWidth = walkHalfWidth; PaintHalfWidth = paintHalfWidth; Walkable = walkable; Points = points;
            }
        }

        private sealed class Clearing
        {
            public readonly string Id, Name;
            public readonly Vector2 Centre, Radii;
            public readonly Surface Surface;

            public Clearing(string id, string name, Vector2 centre, Vector2 radii, Surface surface)
            {
                Id = id; Name = name; Centre = centre; Radii = radii; Surface = surface;
            }

            public float NormalizedDistance(Vector2 p)
            {
                Vector2 d = p - Centre;
                return Mathf.Sqrt(d.x * d.x / (Radii.x * Radii.x) + d.y * d.y / (Radii.y * Radii.y));
            }
        }

        // The south trail leaves the arrival clearing where its old dirt track already ran off the map.
        private static readonly Trail[] Trails =
        {
            new Trail(Surface.Dirt, 1.55f, 1.7f, true, V(16f, 3f), V(16f, -1f), V(14.5f, -5f), V(12f, -9f), V(9.5f, -12.5f)),
            new Trail(Surface.Dirt, 1.55f, 1.7f, true, V(1.5f, -17.5f), V(-4f, -16.5f), V(-8.5f, -13f), V(-11.5f, -7.5f),
                V(-13f, -1.5f), V(-13.5f, 5f), V(-12.5f, 11f), V(-12f, 17f), V(-12f, 21.5f)),
            new Trail(Surface.Stone, 1.2f, 1.05f, true, V(5f, 24f), V(0f, 24f), V(-8f, 24.2f)),
            new Trail(Surface.Stone, 1.2f, 1.05f, true, V(-16f, 24.2f), V(-21f, 24f), V(-24f, 24f)),
            // Past the portal the stone trail carries on into the trees, unwalkable, towards the deep forest.
            new Trail(Surface.Stone, 0f, 0.95f, false, V(-24f, 24f), V(-31f, 24.4f), V(-41f, 25f)),
            // North of the gate the cobbles continue towards the campus.
            new Trail(Surface.Stone, 0f, 1.5f, false, V(17f, 48.5f), V(17f, 63f)),
        };

        private static readonly Clearing[] Clearings =
        {
            new Clearing("F08", "Mossy hollow", V(7f, -16.5f), V(6.5f, 4.5f), Surface.Dirt),
            new Clearing("F10", "Waystone clearing", V(-12f, 24.5f), V(4.5f, 3.5f), Surface.Stone),
        };

        // Stream continuations: each chain starts at the open end of an existing segment and is a list of
        // (heading change in degrees, length in metres).
        private static readonly (string source, Vector2 end, Vector2 heading, (float turn, float length)[] segments)[] StreamChains =
        {
            ("Stream", V(36.63f, 12.49f), V(0.5503f, -0.8350f), new[] { (-8f, 15f), (14f, 15f) }),
            ("Stream (2)", V(38.74f, 35.76f), V(0.7990f, 0.6013f), new[] { (-22f, 10f), (-12f, 10f) }),
        };
        private const float StreamClearance = 1.9f;

        // The academy wall continues along z 47.75 past both ends of the original run.
        private static readonly float[] WallExtensionX = { -3.25f, -7f, -10.75f, 35.75f, 39.5f, 43.25f };

        // The north lawn behind the gate stays open ground.
        private static readonly Rect NorthLawn = new Rect(9f, 49f, 17f, 20f);

        // Detail coverage probability per detail layer (grass v1, grass v2, poppy, river rocks, small poppy, high grass),
        // measured on the original terrain by surface (dirt, grass, stone) and distance to walkable ground
        // (on it, within 2.5 m, further).
        private static readonly float[,,] DetailCoverage =
        {
            { { 0.01f, 0.06f, 0.03f, 0.08f, 0f, 0.23f }, { 0.04f, 0.05f, 0.08f, 0.13f, 0f, 0.19f }, { 0.02f, 0.31f, 0.01f, 0.28f, 0f, 0f } },
            { { 0.17f, 0.55f, 0.20f, 0.22f, 0.04f, 0.57f }, { 0.18f, 0.82f, 0.27f, 0.12f, 0.08f, 0.76f }, { 0.10f, 0.86f, 0.05f, 0.08f, 0.02f, 0.20f } },
            { { 0f, 0f, 0f, 0.07f, 0f, 0.09f }, { 0f, 0f, 0.11f, 0.03f, 0f, 0.04f }, { 0f, 0f, 0.09f, 0f, 0f, 0f } },
        };
        // Brush-blob size per detail layer, as a noise frequency in cycles per metre.
        private static readonly float[] DetailFrequency = { 0.3f, 0.12f, 0.35f, 0.5f, 0.4f, 0.25f };

        private static Vector2 V(float x, float z) => new Vector2(x, z);

        [MenuItem("Lumenfall/World/Expand West Forest")]
        public static void Apply()
        {
            WorldAreaAsset area = AssetDatabase.LoadAssetAtPath<WorldAreaAsset>(AreaPath);
            TerrainData source = AssetDatabase.LoadAssetAtPath<TerrainData>(SourceTerrainPath);
            if (area == null || source == null) throw new InvalidOperationException("Could not load the westForest area or its terrain.");

            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                Terrain originalTerrain = root.GetComponentsInChildren<Terrain>(true).First(t => t.terrainData == source);
                // The walk map is read while an earlier run's boundary obstacles still enclose it.
                bool[,] reached = FloodFromSpawn(root, area.definition);
                bool[,] walkable = AddDesignedCells(reached);
                Transform oldGenerated = root.transform.Find(GeneratedRootName);
                if (oldGenerated != null) UnityEngine.Object.DestroyImmediate(oldGenerated.gameObject);
                MovePortal(root);

                Transform generated = new GameObject(GeneratedRootName).transform;
                generated.SetParent(root.transform, false);

                int removed = RemoveObstaclesOnWalkable(root, walkable);
                int blockers = AddBoundaryObstacles(root, generated, walkable);
                List<(Vector2 a, Vector2 b)> stream = ExtendStream(root, generated);
                ExtendWall(root, generated);

                int clearedTrees = PaintOriginalAlongNewTrails(source);
                TerrainData surround = BuildSurroundTerrain(source, originalTerrain, walkable, stream);
                Terrain terrain = UnityEngine.Object.Instantiate(originalTerrain.gameObject, generated).GetComponent<Terrain>();
                terrain.name = "West Forest Surround Terrain";
                terrain.terrainData = surround;
                TerrainCollider collider = terrain.GetComponent<TerrainCollider>();
                if (collider != null) collider.terrainData = surround;
                terrain.transform.position = new Vector3(SurroundOrigin.x, originalTerrain.transform.position.y, SurroundOrigin.y);

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                UpdateDefinition(area);
                AssetDatabase.SaveAssets();
                Debug.Log($"West Forest expanded to {Bounds.width}x{Bounds.height} cells from ({Bounds.xMin},{Bounds.yMin}): " +
                          $"{CountWalkable(walkable)} walkable cells, {surround.treeInstanceCount} new trees, " +
                          $"{removed} original obstacles opened, {blockers} boundary obstacles, {clearedTrees} original trees cleared from the new trail.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void MovePortal(GameObject root)
        {
            GridPortal portal = root.GetComponentsInChildren<GridPortal>(true).FirstOrDefault(p => p.name == DeepForestPortalName);
            if (portal == null) throw new InvalidOperationException("West Forest has no " + DeepForestPortalName + " portal.");
            Vector3 position = portal.transform.position;
            portal.transform.position = new Vector3(DeepForestPortalCell.x, position.y, DeepForestPortalCell.y);
        }

        // ---------------------------------------------------------------- walkable cells and obstacles

        /// <summary>
        /// The original map's walkable cells: those reachable from the spawn within the original bounds. On a rerun
        /// the earlier boundary obstacles keep this the same, apart from new trail cells inside the original bounds.
        /// </summary>
        private static bool[,] FloodFromSpawn(GameObject root, MapDefinition definition)
        {
            HashSet<Vector2Int> blocked = new HashSet<Vector2Int>(root.GetComponentsInChildren<GridObstacle>(true).SelectMany(ObstacleCells));
            HashSet<Vector2Int> portals = PortalCells(root);
            bool[,] walkable = new bool[Bounds.width, Bounds.height];

            Vector2Int spawn = new Vector2Int(definition.spawn.x, definition.spawn.y);
            Queue<Vector2Int> queue = new Queue<Vector2Int>();
            queue.Enqueue(spawn);
            SetWalkable(walkable, spawn);
            Vector2Int[] steps = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
            while (queue.Count > 0)
            {
                Vector2Int cell = queue.Dequeue();
                foreach (Vector2Int step in steps)
                {
                    Vector2Int next = cell + step;
                    if (!OriginalBounds.Contains(next) || IsWalkable(walkable, next)) continue;
                    if (blocked.Contains(next) && !portals.Contains(next)) continue;
                    SetWalkable(walkable, next);
                    queue.Enqueue(next);
                }
            }
            return walkable;
        }

        private static bool[,] AddDesignedCells(bool[,] reached)
        {
            bool[,] walkable = (bool[,])reached.Clone();
            for (int x = Bounds.xMin; x < Bounds.xMax; x++)
                for (int z = Bounds.yMin; z < Bounds.yMax; z++)
                    if (IsDesignedWalkable(new Vector2(x, z))) walkable[x - Bounds.xMin, z - Bounds.yMin] = true;

            // Rounding the shapes to cells leaves one-cell nubs at their edges; trim new cells with a single way in.
            Vector2Int[] steps = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
            for (bool trimmed = true; trimmed;)
            {
                trimmed = false;
                for (int x = Bounds.xMin; x < Bounds.xMax; x++)
                    for (int z = Bounds.yMin; z < Bounds.yMax; z++)
                    {
                        Vector2Int cell = new Vector2Int(x, z);
                        if (!IsWalkable(walkable, cell) || IsWalkable(reached, cell) || cell == DeepForestPortalCell) continue;
                        if (steps.Count(step => IsWalkable(walkable, cell + step)) >= 2) continue;
                        walkable[x - Bounds.xMin, z - Bounds.yMin] = false;
                        trimmed = true;
                    }
            }
            return walkable;
        }

        private static bool IsDesignedWalkable(Vector2 p)
        {
            foreach (Trail trail in Trails)
                if (trail.Walkable && DistanceToPolyline(p, trail.Points) <= trail.WalkHalfWidth) return true;
            foreach (Clearing clearing in Clearings)
                if (clearing.NormalizedDistance(p) <= 1f) return true;
            return false;
        }

        private static IEnumerable<Vector2Int> ObstacleCells(GridObstacle obstacle)
        {
            Vector3 p = obstacle.transform.position;
            Vector2Int first = new Vector2Int(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.z)) + obstacle.FootprintOffset;
            for (int x = 0; x < obstacle.FootprintSize.x; x++)
                for (int z = 0; z < obstacle.FootprintSize.y; z++)
                    yield return first + new Vector2Int(x, z);
        }

        private static HashSet<Vector2Int> PortalCells(GameObject root)
        {
            HashSet<Vector2Int> cells = new HashSet<Vector2Int>();
            foreach (GridPortal portal in root.GetComponentsInChildren<GridPortal>(true))
            {
                Vector3 p = portal.transform.position;
                Vector2Int centre = new Vector2Int(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.z));
                for (int x = -portal.ExtendNegativeX; x <= portal.ExtendPositiveX; x++)
                    for (int z = -portal.ExtendNegativeZ; z <= portal.ExtendPositiveZ; z++)
                        cells.Add(centre + new Vector2Int(x, z));
            }
            return cells;
        }

        /// <summary>Removes original obstacles that would block the new walkable cells; the boundary pass re-blocks their other cells.</summary>
        private static int RemoveObstaclesOnWalkable(GameObject root, bool[,] walkable)
        {
            HashSet<Vector2Int> portals = PortalCells(root);
            int removed = 0;
            foreach (GridObstacle obstacle in root.GetComponentsInChildren<GridObstacle>(true))
            {
                if (!ObstacleCells(obstacle).Any(cell => IsWalkable(walkable, cell) && !portals.Contains(cell))) continue;
                UnityEngine.Object.DestroyImmediate(obstacle.gameObject);
                removed++;
            }
            return removed;
        }

        /// <summary>Blocks every in-bounds cell touching the walkable space (diagonals included), merged into rectangles.</summary>
        private static int AddBoundaryObstacles(GameObject root, Transform generated, bool[,] walkable)
        {
            HashSet<Vector2Int> blocked = new HashSet<Vector2Int>(root.GetComponentsInChildren<GridObstacle>(true).SelectMany(ObstacleCells));
            HashSet<Vector2Int> portals = PortalCells(root);
            bool[,] ring = new bool[Bounds.width, Bounds.height];
            for (int x = Bounds.xMin; x < Bounds.xMax; x++)
                for (int z = Bounds.yMin; z < Bounds.yMax; z++)
                {
                    Vector2Int cell = new Vector2Int(x, z);
                    if (IsWalkable(walkable, cell) || blocked.Contains(cell) || portals.Contains(cell)) continue;
                    bool touches = false;
                    for (int dx = -1; dx <= 1 && !touches; dx++)
                        for (int dz = -1; dz <= 1 && !touches; dz++)
                            touches = IsWalkable(walkable, cell + new Vector2Int(dx, dz));
                    ring[x - Bounds.xMin, z - Bounds.yMin] = touches;
                }

            Transform parent = new GameObject("Obstacles").transform;
            parent.SetParent(generated, false);
            int count = 0;
            for (int z = 0; z < Bounds.height; z++)
                for (int x = 0; x < Bounds.width; x++)
                {
                    if (!ring[x, z]) continue;
                    int width = 1;
                    while (x + width < Bounds.width && ring[x + width, z]) width++;
                    int height = 1;
                    while (z + height < Bounds.height && Enumerable.Range(x, width).All(i => ring[i, z + height])) height++;
                    for (int i = x; i < x + width; i++)
                        for (int j = z; j < z + height; j++) ring[i, j] = false;

                    Vector2Int cell = new Vector2Int(x + Bounds.xMin, z + Bounds.yMin);
                    GameObject blocker = new GameObject($"T_ext_{cell.x}_{cell.y}");
                    blocker.transform.SetParent(parent, false);
                    blocker.transform.position = new Vector3(cell.x, 0f, cell.y);
                    SerializedObject serialized = new SerializedObject(blocker.AddComponent<GridObstacle>());
                    serialized.FindProperty("useTransformAsAnchor").boolValue = true;
                    serialized.FindProperty("footprintOffset").vector2IntValue = Vector2Int.zero;
                    serialized.FindProperty("footprintSize").vector2IntValue = new Vector2Int(width, height);
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    count++;
                }
            return count;
        }

        private static bool IsWalkable(bool[,] walkable, Vector2Int cell) =>
            Bounds.Contains(cell) && walkable[cell.x - Bounds.xMin, cell.y - Bounds.yMin];

        private static void SetWalkable(bool[,] walkable, Vector2Int cell) => walkable[cell.x - Bounds.xMin, cell.y - Bounds.yMin] = true;

        private static int CountWalkable(bool[,] walkable) => walkable.Cast<bool>().Count(w => w);

        // ---------------------------------------------------------------- stream and wall

        private static List<(Vector2 a, Vector2 b)> ExtendStream(GameObject root, Transform generated)
        {
            Transform group = root.transform.Find("Stream");
            if (group == null) throw new InvalidOperationException("West Forest has no Stream group.");
            Transform parent = new GameObject("Stream").transform;
            parent.SetParent(generated, false);
            List<(Vector2, Vector2)> axes = new List<(Vector2, Vector2)>();
            // The original segments count too, so new trees stay off the water where it crosses the old edge.
            foreach (Transform original in group)
            {
                Vector3 along = original.right * original.lossyScale.x * 5f;
                axes.Add((new Vector2(original.position.x - along.x, original.position.z - along.z),
                    new Vector2(original.position.x + along.x, original.position.z + along.z)));
            }
            foreach (var chain in StreamChains)
            {
                Transform template = group.Cast<Transform>().First(child => child.name == chain.source);
                Vector2 start = chain.end, heading = chain.heading.normalized;
                int index = 0;
                foreach (var (turn, length) in chain.segments)
                {
                    heading = Rotate(heading, turn);
                    // Each segment overlaps the previous end slightly so the water has no gap.
                    Vector2 from = start - heading * 0.5f, to = from + heading * length;
                    Transform segment = UnityEngine.Object.Instantiate(template, parent);
                    segment.name = $"{chain.source} extension {++index}";
                    Vector2 centre = (from + to) * 0.5f;
                    segment.position = new Vector3(centre.x, template.position.y, centre.y);
                    segment.rotation = Quaternion.Euler(0f, -Mathf.Atan2(heading.y, heading.x) * Mathf.Rad2Deg, 0f);
                    segment.localScale = new Vector3(length / 10f, 1f, template.lossyScale.z);
                    axes.Add((from, to));
                    start = to;
                }
            }
            return axes;
        }

        private static void ExtendWall(GameObject root, Transform generated)
        {
            Transform walls = root.transform.Find("Walls");
            Transform template = walls == null ? null : walls.Cast<Transform>().FirstOrDefault(child => child.name.StartsWith("wall1"));
            if (template == null) throw new InvalidOperationException("West Forest has no wall1 segments to extend.");
            GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(template.gameObject);
            Transform parent = new GameObject("Walls").transform;
            parent.SetParent(generated, false);
            foreach (float x in WallExtensionX)
            {
                GameObject wall = source != null
                    ? (GameObject)PrefabUtility.InstantiatePrefab(source, parent)
                    : UnityEngine.Object.Instantiate(template.gameObject, parent);
                wall.name = "wall1 extension " + x.ToString("0.##");
                wall.transform.SetPositionAndRotation(new Vector3(x, template.position.y, template.position.z), template.rotation);
                wall.transform.localScale = template.lossyScale;
            }
        }

        // ---------------------------------------------------------------- terrain

        private static TerrainData BuildSurroundTerrain(TerrainData source, Terrain originalTerrain, bool[,] walkable,
            List<(Vector2 a, Vector2 b)> stream)
        {
            TerrainData data = AssetDatabase.LoadAssetAtPath<TerrainData>(SurroundTerrainPath);
            if (data == null)
            {
                data = new TerrainData();
                AssetDatabase.CreateAsset(data, SurroundTerrainPath);
            }

            data.heightmapResolution = HeightResolution;
            data.size = new Vector3(SurroundSize, source.size.y, SurroundSize);
            data.alphamapResolution = AlphaResolution;
            data.baseMapResolution = source.baseMapResolution;
            data.SetDetailResolution(DetailResolution, source.detailResolutionPerPatch);
            data.SetDetailScatterMode(source.detailScatterMode);
            data.terrainLayers = source.terrainLayers;
            data.detailPrototypes = source.detailPrototypes;
            data.treePrototypes = source.treePrototypes;
            data.RefreshPrototypes();

            Vector2 originalOrigin = new Vector2(originalTerrain.transform.position.x, originalTerrain.transform.position.z);
            if (originalOrigin != OriginalFootprint.position)
                throw new InvalidOperationException($"The original forest terrain moved to {originalOrigin}; update OriginalFootprint.");

            data.SetHeights(0, 0, BlendSeamHeights(source));
            float[,] distance = WalkableDistanceField(walkable);
            float[,,] alpha = PaintAlphamap(source, walkable, stream);
            data.SetAlphamaps(0, 0, alpha);
            PaintDetails(data, source, alpha, distance);
            data.SetHoles(0, 0, BuildHoles(data.holesResolution));
            data.SetTreeInstances(PlaceTrees(source, walkable, stream).ToArray(), true);
            EditorUtility.SetDirty(data);
            return data;
        }

        /// <summary>Flat ground that rises to meet the original terrain's few centimetres of relief at its edge.</summary>
        private static float[,] BlendSeamHeights(TerrainData source)
        {
            float[,] heights = new float[HeightResolution, HeightResolution];
            float texel = SurroundSize / (HeightResolution - 1);
            for (int z = 0; z < HeightResolution; z++)
                for (int x = 0; x < HeightResolution; x++)
                {
                    Vector2 p = SurroundOrigin + new Vector2(x * texel, z * texel);
                    float outside = DistanceOutsideOriginal(p, out Vector2 edge);
                    if (outside >= SeamBlendDistance) continue;
                    float edgeHeight = source.GetInterpolatedHeight(
                        (edge.x - OriginalFootprint.xMin) / OriginalFootprint.width, (edge.y - OriginalFootprint.yMin) / OriginalFootprint.height);
                    heights[z, x] = Mathf.Lerp(edgeHeight / source.size.y, 0f, Mathf.SmoothStep(0f, 1f, outside / SeamBlendDistance));
                }
            return heights;
        }

        private static bool[,] BuildHoles(int resolution)
        {
            bool[,] holes = new bool[resolution, resolution];
            float texel = SurroundSize / resolution;
            for (int z = 0; z < resolution; z++)
                for (int x = 0; x < resolution; x++)
                {
                    Vector2 centre = SurroundOrigin + new Vector2((x + 0.5f) * texel, (z + 0.5f) * texel);
                    holes[z, x] = !OriginalFootprint.Contains(centre);
                }
            return holes;
        }

        private static Vector2 TexelCentre(int x, int z, int resolution) =>
            SurroundOrigin + new Vector2((x + 0.5f) / resolution * SurroundSize, (z + 0.5f) / resolution * SurroundSize);

        /// <summary>Distance from the footprint of the original terrain, and the nearest point on its edge.</summary>
        private static float DistanceOutsideOriginal(Vector2 p, out Vector2 edge)
        {
            edge = new Vector2(Mathf.Clamp(p.x, OriginalFootprint.xMin + 0.05f, OriginalFootprint.xMax - 0.05f),
                Mathf.Clamp(p.y, OriginalFootprint.yMin + 0.05f, OriginalFootprint.yMax - 0.05f));
            return OriginalFootprint.Contains(p) ? 0f : Vector2.Distance(p, edge);
        }

        private static float[,,] PaintAlphamap(TerrainData source, bool[,] walkable, List<(Vector2 a, Vector2 b)> stream)
        {
            float[,,] original = source.GetAlphamaps(0, 0, source.alphamapWidth, source.alphamapHeight);
            float[,,] alpha = new float[AlphaResolution, AlphaResolution, 3];
            for (int z = 0; z < AlphaResolution; z++)
                for (int x = 0; x < AlphaResolution; x++)
                {
                    Vector2 p = TexelCentre(x, z, AlphaResolution);
                    Vector3 weights = SurfaceWeights(p, stream);
                    float outside = DistanceOutsideOriginal(p, out Vector2 edge);
                    if (outside < SeamBlendDistance)
                    {
                        int ox = Mathf.Clamp((int)((edge.x - OriginalFootprint.xMin) / OriginalFootprint.width * source.alphamapWidth), 0, source.alphamapWidth - 1);
                        int oz = Mathf.Clamp((int)((edge.y - OriginalFootprint.yMin) / OriginalFootprint.height * source.alphamapHeight), 0, source.alphamapHeight - 1);
                        Vector3 seam = new Vector3(original[oz, ox, DirtLayer], original[oz, ox, GrassLayer], original[oz, ox, StoneLayer]);
                        weights = Vector3.Lerp(seam, weights, Mathf.SmoothStep(0f, 1f, outside / SeamBlendDistance));
                    }
                    float total = Mathf.Max(0.0001f, weights.x + weights.y + weights.z);
                    alpha[z, x, DirtLayer] = weights.x / total;
                    alpha[z, x, GrassLayer] = weights.y / total;
                    alpha[z, x, StoneLayer] = weights.z / total;
                }
            return alpha;
        }

        /// <summary>Dirt, grass and stone weights at a point: soft-edged trails with ragged margins, as if brushed.</summary>
        private static Vector3 SurfaceWeights(Vector2 p, List<(Vector2 a, Vector2 b)> stream)
        {
            float ragged = (Fbm(p, 0.55f, 11f) - 0.5f) * 0.9f;
            float patchy = Fbm(p, 0.35f, 23f);
            float dirt = 0f, stone = 0f;
            foreach (Trail trail in Trails)
            {
                float d = DistanceToPolyline(p, trail.Points);
                if (trail.Surface == Surface.Dirt)
                    dirt = Mathf.Max(dirt, DirtTrailWeight(trail, d, ragged));
                else
                {
                    stone = Mathf.Max(stone, 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(trail.PaintHalfWidth + ragged * 0.4f - 0.25f, trail.PaintHalfWidth + ragged * 0.4f + 0.25f, d)));
                    // A worn dirt margin either side of the stones, like the original trail.
                    dirt = Mathf.Max(dirt, 0.85f * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(trail.PaintHalfWidth + 0.2f + ragged, trail.PaintHalfWidth + 1.2f + ragged, d))));
                }
            }
            foreach (Clearing clearing in Clearings)
            {
                float e = clearing.NormalizedDistance(p) + ragged * 0.15f;
                float inside = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.75f, 1.15f, e));
                if (clearing.Surface == Surface.Stone)
                {
                    stone = Mathf.Max(stone, (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.6f, e))) * Mathf.Lerp(0.55f, 1f, patchy));
                    dirt = Mathf.Max(dirt, inside * 0.8f);
                }
                else
                    // Clearings are worn earth broken by grass, like the arrival clearing.
                    dirt = Mathf.Max(dirt, inside * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.62f, patchy)));
            }
            foreach (var (a, b) in stream)
            {
                float d = DistanceToSegment(p, a, b);
                dirt = Mathf.Max(dirt, 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.4f + ragged * 0.6f, 2.4f + ragged, d)));
            }
            dirt = Mathf.Min(dirt, 1f - stone);
            return new Vector3(dirt, Mathf.Max(0f, 1f - dirt - stone), stone);
        }

        private static float DirtTrailWeight(Trail trail, float distance, float ragged) =>
            1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(trail.PaintHalfWidth + ragged - 0.5f, trail.PaintHalfWidth + ragged + 0.7f, distance));

        /// <summary>
        /// Where a new dirt trail starts inside the original terrain, paints it there too, thins the grass on it,
        /// and clears original trees standing on the new trails. Painting only ever adds dirt, so reruns change nothing.
        /// </summary>
        private static int PaintOriginalAlongNewTrails(TerrainData source)
        {
            int width = source.alphamapWidth, height = source.alphamapHeight;
            float[,,] alpha = source.GetAlphamaps(0, 0, width, height);
            float[,] gained = new float[height, width];
            bool painted = false;
            for (int z = 0; z < height; z++)
                for (int x = 0; x < width; x++)
                {
                    Vector2 p = OriginalFootprint.position + new Vector2((x + 0.5f) / width * OriginalFootprint.width, (z + 0.5f) / height * OriginalFootprint.height);
                    float ragged = (Fbm(p, 0.55f, 11f) - 0.5f) * 0.9f;
                    float dirt = 0f;
                    foreach (Trail trail in Trails)
                        if (trail.Walkable && trail.Surface == Surface.Dirt)
                            dirt = Mathf.Max(dirt, DirtTrailWeight(trail, DistanceToPolyline(p, trail.Points), ragged));
                    float current = alpha[z, x, DirtLayer];
                    // Alphamaps are stored in 8 bits; the margin keeps a rerun from repainting rounded texels.
                    if (dirt <= current + 0.02f) continue;
                    float rest = alpha[z, x, GrassLayer] + alpha[z, x, StoneLayer];
                    float keep = rest <= 0f ? 0f : (1f - dirt) / rest;
                    alpha[z, x, DirtLayer] = dirt;
                    alpha[z, x, GrassLayer] *= keep;
                    alpha[z, x, StoneLayer] *= keep;
                    gained[z, x] = dirt - current;
                    painted = true;
                }
            if (painted)
            {
                source.SetAlphamaps(0, 0, alpha);
                // Grass and flowers give way to the dirt; high grass lingers at the margins and pebbles stay.
                float[] thinning = { 0.95f, 0.95f, 0.95f, 0f, 0.95f, 0.7f };
                int resolution = source.detailResolution;
                for (int layer = 0; layer < source.detailPrototypes.Length; layer++)
                {
                    float strength = thinning[Mathf.Min(layer, thinning.Length - 1)];
                    if (strength <= 0f) continue;
                    int[,] values = source.GetDetailLayer(0, 0, resolution, resolution, layer);
                    for (int z = 0; z < resolution; z++)
                        for (int x = 0; x < resolution; x++)
                        {
                            float gain = gained[z * height / resolution, x * width / resolution];
                            if (gain > 0f) values[z, x] = Mathf.RoundToInt(values[z, x] * (1f - Mathf.Clamp01(gain * 1.6f) * strength));
                        }
                    source.SetDetailLayer(0, 0, layer, values);
                }
            }

            List<TreeInstance> trees = source.treeInstances.ToList();
            int cleared = trees.RemoveAll(tree =>
            {
                Vector2 p = OriginalFootprint.position + new Vector2(tree.position.x * source.size.x, tree.position.z * source.size.z);
                Vector2Int near = new Vector2Int(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y));
                for (int dx = -1; dx <= 1; dx++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        Vector2Int cell = near + new Vector2Int(dx, dz);
                        if (IsDesignedWalkable(cell) && Vector2.Distance(p, cell) < 0.8f) return true;
                    }
                return false;
            });
            if (cleared > 0) source.SetTreeInstances(trees.ToArray(), true);
            if (painted || cleared > 0) EditorUtility.SetDirty(source);
            return cleared;
        }

        private static float[,] WalkableDistanceField(bool[,] walkable)
        {
            // Distance in cells from each cell to the nearest walkable cell, capped at 8.
            float[,] distance = new float[Bounds.width + 32, Bounds.height + 32];
            for (int x = 0; x < distance.GetLength(0); x++)
                for (int z = 0; z < distance.GetLength(1); z++)
                {
                    Vector2Int cell = new Vector2Int(x + Bounds.xMin - 16, z + Bounds.yMin - 16);
                    float best = 8f;
                    for (int dx = -8; dx <= 8; dx++)
                        for (int dz = -8; dz <= 8; dz++)
                            if (IsWalkable(walkable, cell + new Vector2Int(dx, dz))) best = Mathf.Min(best, Mathf.Sqrt(dx * dx + dz * dz));
                    distance[x, z] = best;
                }
            return distance;
        }

        private static float WalkableDistance(float[,] field, Vector2 p)
        {
            int x = Mathf.RoundToInt(p.x) - Bounds.xMin + 16, z = Mathf.RoundToInt(p.y) - Bounds.yMin + 16;
            if (x < 0 || z < 0 || x >= field.GetLength(0) || z >= field.GetLength(1)) return 8f;
            return field[x, z];
        }

        private static void PaintDetails(TerrainData data, TerrainData source, float[,,] alpha, float[,] distance)
        {
            int layers = data.detailPrototypes.Length;
            int sourceResolution = source.detailResolution;
            Random random = new Random(Seed);
            for (int layer = 0; layer < layers; layer++)
            {
                int[,] original = source.GetDetailLayer(0, 0, sourceResolution, sourceResolution, layer);
                float[] quantiles = NoiseQuantiles(layer, random);
                int[,] values = new int[DetailResolution, DetailResolution];
                for (int z = 0; z < DetailResolution; z++)
                    for (int x = 0; x < DetailResolution; x++)
                    {
                        Vector2 p = TexelCentre(x, z, DetailResolution);
                        float outside = DistanceOutsideOriginal(p, out Vector2 edge);
                        if (outside <= 0f) continue;

                        int ax = x * AlphaResolution / DetailResolution, az = z * AlphaResolution / DetailResolution;
                        int surface = alpha[az, ax, StoneLayer] > 0.5f ? 2 : alpha[az, ax, DirtLayer] > 0.5f ? 0 : 1;
                        float d = WalkableDistance(distance, p);
                        int band = d < 0.5f ? 0 : d <= 2.5f ? 1 : 2;
                        float coverage = DetailCoverage[surface, band, Mathf.Min(layer, 5)];
                        float generated = 0f;
                        if (coverage > 0f)
                        {
                            float threshold = quantiles[Mathf.Clamp((int)((1f - coverage) * (quantiles.Length - 1)), 0, quantiles.Length - 1)];
                            float noise = Fbm(p, DetailFrequency[Mathf.Min(layer, 5)], 101f + layer * 37f);
                            generated = 255f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(threshold - 0.03f, threshold + 0.03f, noise));
                        }
                        if (outside < SeamBlendDistance)
                        {
                            int ox = Mathf.Clamp((int)((edge.x - OriginalFootprint.xMin) / OriginalFootprint.width * sourceResolution), 0, sourceResolution - 1);
                            int oz = Mathf.Clamp((int)((edge.y - OriginalFootprint.yMin) / OriginalFootprint.height * sourceResolution), 0, sourceResolution - 1);
                            generated = Mathf.Lerp(original[oz, ox], generated, outside / SeamBlendDistance);
                        }
                        values[z, x] = Mathf.Clamp(Mathf.RoundToInt(generated), 0, 255);
                    }
                data.SetDetailLayer(0, 0, layer, values);
            }
        }

        /// <summary>Sorted samples of a layer's noise, so a coverage fraction maps to a threshold.</summary>
        private static float[] NoiseQuantiles(int layer, Random random)
        {
            float[] samples = new float[4096];
            for (int i = 0; i < samples.Length; i++)
            {
                Vector2 p = SurroundOrigin + new Vector2((float)random.NextDouble() * SurroundSize, (float)random.NextDouble() * SurroundSize);
                samples[i] = Fbm(p, DetailFrequency[Mathf.Min(layer, 5)], 101f + layer * 37f);
            }
            Array.Sort(samples);
            return samples;
        }

        // ---------------------------------------------------------------- trees and props

        private static List<TreeInstance> PlaceTrees(TerrainData source, bool[,] walkable, List<(Vector2 a, Vector2 b)> stream)
        {
            Random random = new Random(Seed);
            TreeInstance[] originalTrees = source.treeInstances;
            List<Vector2> occupied = originalTrees
                .Select(t => OriginalFootprint.position + new Vector2(t.position.x * source.size.x, t.position.z * source.size.z))
                .ToList();
            Color32[] tints = originalTrees.Select(t => t.color).ToArray();
            List<TreeInstance> trees = new List<TreeInstance>();
            SpatialHash hash = new SpatialHash(2.5f);
            foreach (Vector2 p in occupied) hash.Add(p);

            void Add(Vector2 p, int prototype, float scale)
            {
                hash.Add(p);
                trees.Add(new TreeInstance
                {
                    prototypeIndex = prototype,
                    position = new Vector3((p.x - SurroundOrigin.x) / SurroundSize, 0f, (p.y - SurroundOrigin.y) / SurroundSize),
                    widthScale = scale * (0.95f + (float)random.NextDouble() * 0.1f),
                    heightScale = scale,
                    rotation = (float)(random.NextDouble() * Math.PI * 2.0),
                    color = tints.Length == 0 ? (Color32)Color.white : tints[random.Next(tints.Length)],
                    lightmapColor = Color.white,
                });
            }

            float Clearance(Vector2 p)
            {
                float best = float.MaxValue;
                Vector2Int near = new Vector2Int(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y));
                for (int dx = -3; dx <= 3; dx++)
                    for (int dz = -3; dz <= 3; dz++)
                    {
                        Vector2Int cell = near + new Vector2Int(dx, dz);
                        if (IsWalkable(walkable, cell)) best = Mathf.Min(best, Vector2.Distance(p, cell));
                    }
                return best;
            }

            bool Allowed(Vector2 p, float pathClearance)
            {
                if (OriginalFootprint.Contains(p) || NorthLawn.Contains(p)) return false;
                if (p.x < SurroundOrigin.x + 0.5f || p.y < SurroundOrigin.y + 0.5f ||
                    p.x > SurroundOrigin.x + SurroundSize - 0.5f || p.y > SurroundOrigin.y + SurroundSize - 0.5f) return false;
                if (Clearance(p) < pathClearance) return false;
                foreach (var (a, b) in stream) if (DistanceToSegment(p, a, b) < StreamClearance) return false;
                foreach (Trail trail in Trails)
                    if (!trail.Walkable && DistanceToPolyline(p, trail.Points) < trail.PaintHalfWidth + 0.6f) return false;
                return true;
            }

            // Props first: rocks, stumps, fallen logs and mushrooms at the edges of the new trails and clearings.
            foreach (Clearing clearing in Clearings)
                for (int i = 0; i < 14; i++)
                {
                    float angle = (float)(random.NextDouble() * Math.PI * 2.0);
                    float reach = 1.05f + (float)random.NextDouble() * 0.35f;
                    Vector2 p = clearing.Centre + new Vector2(Mathf.Cos(angle) * clearing.Radii.x, Mathf.Sin(angle) * clearing.Radii.y) * reach;
                    if (!Allowed(p, 0.9f) || hash.AnyWithin(p, 1.6f)) continue;
                    AddProp(p, clearing.Surface == Surface.Stone);
                }
            foreach (Trail trail in Trails.Where(t => t.Walkable))
                foreach (Vector2 p in SampleAlongEdges(trail, 2.6f, random))
                    if (random.NextDouble() < 0.3 && Allowed(p, 0.9f) && !hash.AnyWithin(p, 1.6f)) AddProp(p, trail.Surface == Surface.Stone);

            void AddProp(Vector2 p, bool nearDeepForest)
            {
                double roll = random.NextDouble();
                if (roll < 0.3) Add(p, Rock, 1.3f + (float)random.NextDouble() * 0.45f);
                else if (roll < 0.48) Add(p, RiverRocks, 1.45f + (float)random.NextDouble() * 0.2f);
                else if (roll < 0.66) Add(p, PineStump, 1.2f + (float)random.NextDouble() * 0.7f);
                else if (roll < 0.8) Add(p, nearDeepForest ? PineLogs : FruitLogs, nearDeepForest ? 0.78f : 0.6f + (float)random.NextDouble() * 0.2f);
                else Add(p, Mushroom, 1f + (float)random.NextDouble() * 0.6f);
            }

            // The waystone clearing gets a ring of small stones, echoing the one where the player wakes.
            Clearing waystone = Clearings.First(c => c.Surface == Surface.Stone);
            for (int i = 0; i < 7; i++)
            {
                float angle = i * Mathf.PI * 2f / 7f + 0.3f;
                Add(waystone.Centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 1.6f, RiverRocks, 1.5f);
            }

            // Then trees, by Poisson-disc sampling: about one per 9 m2 of forest, as on the original terrain.
            const float spacing = 2.35f;
            List<Vector2> active = new List<Vector2>();
            Vector2 start = new Vector2(-20f, -20f);
            active.Add(start);
            while (active.Count > 0)
            {
                int index = random.Next(active.Count);
                Vector2 centre = active[index];
                bool placed = false;
                for (int attempt = 0; attempt < 24; attempt++)
                {
                    float angle = (float)(random.NextDouble() * Math.PI * 2.0);
                    float radius = spacing * (1f + (float)random.NextDouble());
                    Vector2 p = centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                    if (p.x < SurroundOrigin.x || p.y < SurroundOrigin.y || p.x > SurroundOrigin.x + SurroundSize || p.y > SurroundOrigin.y + SurroundSize) continue;
                    if (hash.AnyWithin(p, spacing)) continue;
                    // Sampling continues across the original footprint and the lawn so the disc spreads around them.
                    active.Add(p);
                    placed = true;
                    if (Allowed(p, 1.15f)) Add(p, PickTree(p, random), TreeScale(random));
                    else hash.Add(p);
                    break;
                }
                if (!placed) active.RemoveAt(index);
            }
            return trees;
        }

        /// <summary>Fruit trees like the original forest, turning to pines towards the deep forest in the west.</summary>
        private static int PickTree(Vector2 p, Random random)
        {
            float westness = Mathf.InverseLerp(-2f, -22f, p.x);
            if (random.NextDouble() < Mathf.Lerp(0.18f, 0.72f, westness))
                return random.NextDouble() < 0.9 ? PineTree : DeadPine;
            double roll = random.NextDouble() * 101.0;
            if (roll < 40) return GreenTree;
            if (roll < 72) return AppleTree;
            if (roll < 89) return PearTree;
            if (roll < 97) return PlumTree;
            return DeadFruitTree;
        }

        private static float TreeScale(Random random) =>
            random.NextDouble() < 0.1 ? 1.25f + (float)random.NextDouble() * 0.3f : 0.88f + (float)random.NextDouble() * 0.34f;

        private static IEnumerable<Vector2> SampleAlongEdges(Trail trail, float step, Random random)
        {
            for (int i = 0; i + 1 < trail.Points.Length; i++)
            {
                Vector2 a = trail.Points[i], b = trail.Points[i + 1];
                Vector2 along = (b - a).normalized, side = new Vector2(-along.y, along.x);
                for (float t = 0f; t < Vector2.Distance(a, b); t += step)
                {
                    float offset = trail.WalkHalfWidth + 1.3f + (float)random.NextDouble() * 1.1f;
                    yield return a + along * t + side * (random.NextDouble() < 0.5 ? offset : -offset);
                }
            }
        }

        private sealed class SpatialHash
        {
            private readonly float cell;
            private readonly Dictionary<Vector2Int, List<Vector2>> buckets = new Dictionary<Vector2Int, List<Vector2>>();

            public SpatialHash(float cellSize) { cell = cellSize; }

            private Vector2Int Key(Vector2 p) => new Vector2Int(Mathf.FloorToInt(p.x / cell), Mathf.FloorToInt(p.y / cell));

            public void Add(Vector2 p)
            {
                Vector2Int key = Key(p);
                if (!buckets.TryGetValue(key, out List<Vector2> list)) buckets[key] = list = new List<Vector2>();
                list.Add(p);
            }

            public bool AnyWithin(Vector2 p, float radius)
            {
                Vector2Int key = Key(p);
                int reach = Mathf.CeilToInt(radius / cell);
                for (int x = -reach; x <= reach; x++)
                    for (int z = -reach; z <= reach; z++)
                        if (buckets.TryGetValue(key + new Vector2Int(x, z), out List<Vector2> list))
                            foreach (Vector2 q in list)
                                if ((q - p).sqrMagnitude < radius * radius) return true;
                return false;
            }
        }

        // ---------------------------------------------------------------- area definition

        private static void UpdateDefinition(WorldAreaAsset area)
        {
            MapDefinition definition = area.definition;
            definition.boundsMinX = Bounds.xMin;
            definition.boundsMinY = Bounds.yMin;
            definition.width = Bounds.width;
            definition.height = Bounds.height;

            List<MapRegionDefinition> regions = (definition.regions ?? Array.Empty<MapRegionDefinition>())
                .Where(r => r != null && r.id != "F07" && r.id != "F08" && r.id != "F09" && r.id != "F10")
                .ToList();
            // The deep forest boundary follows its portal to the west end of the stone trail.
            MapRegionDefinition boundary = regions.FirstOrDefault(r => r.id == "F06");
            if (boundary != null) boundary.rect = new MapRectDefinition { x = -26, y = 21, width = 9, height = 7 };
            regions.Add(Region("F07", "Southern trail", 8, -13, 10, 14));
            foreach (Clearing clearing in Clearings)
                regions.Add(Region(clearing.Id, clearing.Name,
                    Mathf.FloorToInt(clearing.Centre.x - clearing.Radii.x), Mathf.FloorToInt(clearing.Centre.y - clearing.Radii.y),
                    Mathf.CeilToInt(clearing.Radii.x * 2f) + 1, Mathf.CeilToInt(clearing.Radii.y * 2f) + 1));
            regions.Add(Region("F09", "Fern loop", -15, -19, 17, 40));
            definition.regions = regions.ToArray();
            EditorUtility.SetDirty(area);
        }

        private static MapRegionDefinition Region(string id, string name, int x, int y, int width, int height) =>
            new MapRegionDefinition { id = id, name = name, rect = new MapRectDefinition { x = x, y = y, width = width, height = height }, walkable = true };

        // ---------------------------------------------------------------- geometry and noise

        private static Vector2 Rotate(Vector2 v, float degrees)
        {
            float r = degrees * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        private static float DistanceToPolyline(Vector2 p, Vector2[] points)
        {
            float best = float.MaxValue;
            for (int i = 0; i + 1 < points.Length; i++) best = Mathf.Min(best, DistanceToSegment(p, points[i], points[i + 1]));
            return best;
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
            return Vector2.Distance(p, a + ab * t);
        }

        private static float Fbm(Vector2 p, float frequency, float offset)
        {
            float x = p.x * frequency + offset, z = p.y * frequency + offset * 0.73f;
            return Mathf.PerlinNoise(x, z) * 0.6f + Mathf.PerlinNoise(x * 2.03f + 17f, z * 2.03f + 5f) * 0.3f +
                   Mathf.PerlinNoise(x * 4.1f + 31f, z * 4.1f + 11f) * 0.1f;
        }
    }
}
