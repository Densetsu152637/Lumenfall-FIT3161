using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Lumenfall.DataDriven;
using Lumenfall.GridMovement;
using UnityEditor;
using UnityEngine;

namespace Lumenfall.EditorTools
{
    /// <summary>
    /// Opens up the West Forest's painted trees along its trails. Playtesters found the forest hard to explore: canopies
    /// hung over the narrow trails, hid the player, and made the trail edges feel like invisible walls. The trails and
    /// their blockers are unchanged; only terrain trees move out of the way:
    /// <list type="bullet">
    /// <item>A tree whose canopy reaches over a walkable cell is removed: it roofs the trail and hides the player. The
    /// canopy is taken as <see cref="CanopyRadius"/> times the tree's width scale.</item>
    /// <item>Within <see cref="NearTrailReach"/> of the trails, trees are thinned so no two trunks are closer than
    /// <see cref="MinimumSpacing"/> (scaled by their size); the larger tree of a crowded pair is kept. The edges stay
    /// lined with trees, but as separate trees instead of one solid canopy.</item>
    /// </list>
    /// Rocks, stumps, logs and mushrooms are left alone, and so is the deeper forest. Walkable cells are those reachable
    /// from the spawn, as in <see cref="WestForestExpansionAuthoring"/>. The tool is safe to rerun: a second run finds
    /// nothing left to clear at the same settings. It writes every tree and its fate to Logs/forest-tree-spacing.csv.
    /// Run it from Lumenfall > World > Space Out West Forest Trees, or in batch mode through <see cref="Apply"/>.
    /// </summary>
    public static class ForestTreeSpacingTool
    {
        private const string PrefabPath = "Assets/_Project/World/Areas/Prefabs/West Forest.prefab";
        private const string AreaPath = "Assets/_Project/World/Areas/westForest.asset";
        private static readonly RectInt Bounds = new RectInt(-27, -24, 65, 74);

        /// <summary>The canopy radius of a tree at its normal size, in metres; the forest's trees are about 3.6 m across.</summary>
        private const float CanopyRadius = 1.8f;
        /// <summary>How far from the trails the thinning reaches, in metres (one cell is one metre).</summary>
        private const float NearTrailReach = 3f;
        /// <summary>The least distance between two trunks near the trails, for trees at their normal size.</summary>
        private const float MinimumSpacing = 3.4f;

        private static readonly string[] PropNames = { "rock", "stump", "log", "mushroom" };

        [MenuItem("Lumenfall/World/Space Out West Forest Trees")]
        public static void Apply() => Run(true);

        /// <summary>Reports what <see cref="Apply"/> would change, and the walkable cells, without changing anything.</summary>
        public static void Preview() => Run(false);

        private static void Run(bool apply)
        {
            WorldAreaAsset area = AssetDatabase.LoadAssetAtPath<WorldAreaAsset>(AreaPath);
            if (area == null) throw new InvalidOperationException("Missing " + AreaPath);
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            StringBuilder report = new StringBuilder("terrain,state,x,z,prototype,scale\n");
            try
            {
                HashSet<Vector2Int> walkable = FloodFromSpawn(root, new Vector2Int(area.definition.spawn.x, area.definition.spawn.y));
                int overTrail = 0, thinned = 0, kept = 0;
                foreach (Terrain terrain in root.GetComponentsInChildren<Terrain>(true))
                {
                    TerrainData data = terrain.terrainData;
                    if (data == null) continue;
                    Vector3 origin = terrain.transform.position;
                    TreePrototype[] prototypes = data.treePrototypes;
                    TreeInstance[] trees = data.treeInstances;
                    Vector2 Position(TreeInstance tree) =>
                        new Vector2(origin.x + tree.position.x * data.size.x, origin.z + tree.position.z * data.size.z);
                    bool IsProp(TreeInstance tree)
                    {
                        GameObject prefab = tree.prototypeIndex < prototypes.Length ? prototypes[tree.prototypeIndex].prefab : null;
                        string name = prefab == null ? string.Empty : prefab.name.ToLowerInvariant();
                        return PropNames.Any(name.Contains);
                    }

                    List<TreeInstance> result = new List<TreeInstance>();
                    List<(Vector2 position, float scale)> placed = new List<(Vector2, float)>();
                    // Larger trees first, so a crowded pair keeps the bigger one.
                    foreach (TreeInstance tree in trees.OrderByDescending(t => t.widthScale))
                    {
                        Vector2 p = Position(tree);
                        string state = "kept";
                        if (!IsProp(tree))
                        {
                            float clearance = DistanceToWalkable(walkable, p);
                            if (clearance < CanopyRadius * tree.widthScale) state = "over-trail";
                            else if (clearance < NearTrailReach && placed.Any(other =>
                                         (other.position - p).magnitude < MinimumSpacing * Mathf.Max(0.6f, (other.scale + tree.widthScale) * 0.5f)))
                                state = "thinned";
                        }
                        report.AppendLine($"{terrain.name},{state},{p.x:F2},{p.y:F2},{tree.prototypeIndex},{tree.widthScale:F2}");
                        if (state == "over-trail") { overTrail++; continue; }
                        if (state == "thinned") { thinned++; continue; }
                        kept++;
                        result.Add(tree);
                        if (!IsProp(tree)) placed.Add((p, tree.widthScale));
                    }
                    if (!apply || result.Count == trees.Length) continue;
                    data.SetTreeInstances(result.ToArray(), true);
                    EditorUtility.SetDirty(data);
                }
                if (apply) AssetDatabase.SaveAssets();
                foreach (Vector2Int cell in walkable) report.AppendLine($"grid,walkable,{cell.x},{cell.y},-1,0");
                File.WriteAllText(ReportPath(), report.ToString());
                Debug.Log($"West Forest trees{(apply ? string.Empty : " (preview only)")}: {overTrail} removed from over the trails, {thinned} thinned near them, {kept} kept " +
                          $"({walkable.Count} walkable cells).");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // Logs/ survives the editor quitting, unlike Temp/; batch runs can pass -treeReport <path> instead.
        private static string ReportPath()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-treeReport");
            if (index >= 0 && index + 1 < args.Length) return args[index + 1];
            return Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs", "forest-tree-spacing.csv");
        }

        private static HashSet<Vector2Int> FloodFromSpawn(GameObject root, Vector2Int spawn)
        {
            HashSet<Vector2Int> blocked = new HashSet<Vector2Int>();
            foreach (GridObstacle obstacle in root.GetComponentsInChildren<GridObstacle>(true))
            {
                Vector3 position = obstacle.transform.position;
                Vector2Int anchor = new Vector2Int(Mathf.RoundToInt(position.x), Mathf.RoundToInt(position.z));
                for (int x = 0; x < obstacle.FootprintSize.x; x++)
                    for (int z = 0; z < obstacle.FootprintSize.y; z++)
                        blocked.Add(anchor + obstacle.FootprintOffset + new Vector2Int(x, z));
            }
            HashSet<Vector2Int> portals = new HashSet<Vector2Int>();
            foreach (GridPortal portal in root.GetComponentsInChildren<GridPortal>(true))
            {
                Vector3 position = portal.transform.position;
                Vector2Int centre = new Vector2Int(Mathf.RoundToInt(position.x), Mathf.RoundToInt(position.z));
                for (int x = -portal.ExtendNegativeX; x <= portal.ExtendPositiveX; x++)
                    for (int z = -portal.ExtendNegativeZ; z <= portal.ExtendPositiveZ; z++)
                        portals.Add(centre + new Vector2Int(x, z));
            }

            HashSet<Vector2Int> walkable = new HashSet<Vector2Int> { spawn };
            Queue<Vector2Int> queue = new Queue<Vector2Int>();
            queue.Enqueue(spawn);
            Vector2Int[] steps = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
            while (queue.Count > 0)
            {
                Vector2Int cell = queue.Dequeue();
                foreach (Vector2Int step in steps)
                {
                    Vector2Int next = cell + step;
                    if (!Bounds.Contains(next) || walkable.Contains(next)) continue;
                    if (blocked.Contains(next) && !portals.Contains(next)) continue;
                    walkable.Add(next);
                    queue.Enqueue(next);
                }
            }
            return walkable;
        }

        private static float DistanceToWalkable(HashSet<Vector2Int> walkable, Vector2 p)
        {
            float best = float.MaxValue;
            Vector2Int near = new Vector2Int(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y));
            int reach = Mathf.CeilToInt(NearTrailReach) + 1;
            for (int dx = -reach; dx <= reach; dx++)
                for (int dz = -reach; dz <= reach; dz++)
                {
                    Vector2Int cell = near + new Vector2Int(dx, dz);
                    if (walkable.Contains(cell)) best = Mathf.Min(best, Vector2.Distance(p, cell));
                }
            return best;
        }
    }
}
