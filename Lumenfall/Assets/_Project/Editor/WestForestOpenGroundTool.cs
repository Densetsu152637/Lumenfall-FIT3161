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
    /// Lets the player leave the West Forest's trails and walk among the trees. The trails were fenced by invisible
    /// blocker tiles (<c>T_tile*</c> from the original map, <c>T_ext*</c> from <see cref="WestForestExpansionAuthoring"/>),
    /// so the open grass beside them looked walkable but was not. This tool deactivates those fences (the runtime skips
    /// inactive obstacles, while the authoring tools still read them to find the trails) and blocks only what is really
    /// there, under <see cref="GeneratedRootName"/>:
    /// <list type="bullet">
    /// <item>the trunk cell of every painted tree, rock, stump and log (mushrooms stay walkable);</item>
    /// <item>the stream's water planes;</item>
    /// <item>the academy wall, its gate and pillars, and the bench.</item>
    /// </list>
    /// Trail cells, portals, the spawn and the forest's NPC cells are never blocked, and the run checks that every trail
    /// cell is still reachable from the spawn. Rerunning rebuilds the blockers from the current trees. To undo, delete
    /// <see cref="GeneratedRootName"/> and reactivate the fences. It writes the grid to Logs/forest-open-ground.csv.
    /// Run it from Lumenfall > World > Open West Forest Undergrowth, or in batch mode through <see cref="Apply"/>.
    /// </summary>
    public static class WestForestOpenGroundTool
    {
        private const string PrefabPath = "Assets/_Project/World/Areas/Prefabs/West Forest.prefab";
        private const string AreaPath = "Assets/_Project/World/Areas/westForest.asset";
        private const string NpcCatalogPath = "Assets/_Project/World/Areas/NpcCatalog.asset";
        private const string GeneratedRootName = "Open Forest Blockers";
        private static readonly RectInt Bounds = new RectInt(-27, -24, 65, 74);
        private static readonly string[] FenceNames = { "T_tile", "T_ext" };
        private static readonly string[] SolidModels = { "wall1", "Gate", "PillarFancy", "Park-Bench" };
        private const string StreamName = "Stream";
        private const string WalkThroughProp = "mushroom";

        /// <summary>
        /// Creating Library/ + this file name makes the next script reload run the tool once, unattended. It lives in
        /// Library rather than Temp because the Editor empties Temp when it starts, which would drop the request.
        /// </summary>
        public const string RequestFileName = "lumenfall-open-west-forest.request";

        [InitializeOnLoadMethod]
        private static void ApplyRequested()
        {
            string requestPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Library", RequestFileName);
            if (!File.Exists(requestPath)) return;
            // Prefab changes made during Play mode would not show until the next play, so wait for edit mode.
            void RunOnce()
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(requestPath)) return;
                EditorApplication.update -= RunOnce;
                File.Delete(requestPath);
                Apply();
            }
            EditorApplication.update += RunOnce;
        }

        [MenuItem("Lumenfall/World/Open West Forest Undergrowth")]
        public static void Apply() => Run(true);

        /// <summary>Reports what <see cref="Apply"/> would block, without changing anything.</summary>
        [MenuItem("Lumenfall/World/Preview Open West Forest Undergrowth")]
        public static void Preview() => Run(false);

        private static void Run(bool apply)
        {
            WorldAreaAsset area = AssetDatabase.LoadAssetAtPath<WorldAreaAsset>(AreaPath);
            if (area == null) throw new InvalidOperationException("Missing " + AreaPath);
            Vector2Int spawn = new Vector2Int(area.definition.spawn.x, area.definition.spawn.y);
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                List<GridObstacle> fences = root.GetComponentsInChildren<GridObstacle>(true)
                    .Where(o => FenceNames.Any(o.name.StartsWith) && !IsGenerated(o.transform)).ToList();
                HashSet<Vector2Int> portals = PortalCells(root);
                // The trails as the fences define them; they must all stay open and reachable.
                HashSet<Vector2Int> trails = Flood(spawn, new HashSet<Vector2Int>(fences.SelectMany(Cells)), portals);

                HashSet<Vector2Int> keepOpen = new HashSet<Vector2Int>(trails);
                keepOpen.UnionWith(portals);
                keepOpen.Add(spawn);
                NpcCatalogAsset catalog = AssetDatabase.LoadAssetAtPath<NpcCatalogAsset>(NpcCatalogPath);
                foreach (NpcDefinition npc in catalog != null ? catalog.npcs : Array.Empty<NpcDefinition>())
                    if (npc != null && string.Equals(npc.mapId, area.definition.id, StringComparison.OrdinalIgnoreCase))
                        keepOpen.Add(new Vector2Int(npc.x, npc.y));

                HashSet<Vector2Int> blocked = new HashSet<Vector2Int>();
                int trunks = AddTrunks(root, blocked), water = AddStream(root, blocked), solid = AddSolidModels(root, blocked);
                blocked.RemoveWhere(cell => keepOpen.Contains(cell) || !Bounds.Contains(cell));

                HashSet<Vector2Int> reachable = Flood(spawn, blocked, portals);
                int strandedTrail = trails.Count(cell => !reachable.Contains(cell));
                WriteReport(trails, blocked, reachable);
                string summary = $"West Forest open ground{(apply ? string.Empty : " (preview only)")}: {reachable.Count} walkable cells " +
                    $"(trails alone had {trails.Count}); {blocked.Count} blocked by {trunks} trees and rocks, {water} water cells and " +
                    $"{solid} wall, gate and bench cells; {fences.Count} trail fences {(apply ? "deactivated" : "to deactivate")}.";
                if (strandedTrail > 0)
                {
                    Debug.LogError(summary + $" {strandedTrail} trail cells would be cut off from the spawn, so nothing was changed.");
                    return;
                }
                Debug.Log(summary);
                if (!apply) return;

                foreach (GridObstacle fence in fences) fence.gameObject.SetActive(false);
                Transform previous = root.transform.Find(GeneratedRootName);
                if (previous != null) UnityEngine.Object.DestroyImmediate(previous.gameObject);
                Transform generated = new GameObject(GeneratedRootName).transform;
                generated.SetParent(root.transform, false);
                AddBlockers(generated, blocked);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static bool IsGenerated(Transform transform)
        {
            for (Transform t = transform; t != null; t = t.parent)
                if (t.name == GeneratedRootName) return true;
            return false;
        }

        private static Vector2Int CellAt(Vector3 position) => new Vector2Int(Mathf.RoundToInt(position.x), Mathf.RoundToInt(position.z));

        private static IEnumerable<Vector2Int> Cells(GridObstacle obstacle)
        {
            Vector2Int first = CellAt(obstacle.transform.position) + obstacle.FootprintOffset;
            for (int x = 0; x < obstacle.FootprintSize.x; x++)
                for (int z = 0; z < obstacle.FootprintSize.y; z++)
                    yield return first + new Vector2Int(x, z);
        }

        private static HashSet<Vector2Int> PortalCells(GameObject root)
        {
            HashSet<Vector2Int> cells = new HashSet<Vector2Int>();
            foreach (GridPortal portal in root.GetComponentsInChildren<GridPortal>(true))
            {
                Vector2Int centre = CellAt(portal.transform.position);
                for (int x = -portal.ExtendNegativeX; x <= portal.ExtendPositiveX; x++)
                    for (int z = -portal.ExtendNegativeZ; z <= portal.ExtendPositiveZ; z++)
                        cells.Add(centre + new Vector2Int(x, z));
            }
            return cells;
        }

        private static HashSet<Vector2Int> Flood(Vector2Int spawn, HashSet<Vector2Int> blocked, HashSet<Vector2Int> portals)
        {
            HashSet<Vector2Int> reached = new HashSet<Vector2Int> { spawn };
            Queue<Vector2Int> queue = new Queue<Vector2Int>();
            queue.Enqueue(spawn);
            Vector2Int[] steps = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
            while (queue.Count > 0)
            {
                Vector2Int cell = queue.Dequeue();
                foreach (Vector2Int step in steps)
                {
                    Vector2Int next = cell + step;
                    if (!Bounds.Contains(next) || reached.Contains(next) || (blocked.Contains(next) && !portals.Contains(next))) continue;
                    reached.Add(next);
                    queue.Enqueue(next);
                }
            }
            return reached;
        }

        // Every painted tree, rock, stump and log blocks the cell its trunk stands in; mushrooms are walked over.
        private static int AddTrunks(GameObject root, HashSet<Vector2Int> blocked)
        {
            int count = 0;
            foreach (Terrain terrain in root.GetComponentsInChildren<Terrain>(true))
            {
                TerrainData data = terrain.terrainData;
                if (data == null) continue;
                TreePrototype[] prototypes = data.treePrototypes;
                Vector3 origin = terrain.transform.position;
                foreach (TreeInstance tree in data.treeInstances)
                {
                    GameObject prefab = tree.prototypeIndex < prototypes.Length ? prototypes[tree.prototypeIndex].prefab : null;
                    if (prefab != null && prefab.name.ToLowerInvariant().Contains(WalkThroughProp)) continue;
                    Vector3 position = origin + Vector3.Scale(tree.position, data.size);
                    if (blocked.Add(CellAt(position))) count++;
                }
            }
            return count;
        }

        // The stream is drawn with flat planes; block every cell whose centre lies on one.
        private static int AddStream(GameObject root, HashSet<Vector2Int> blocked)
        {
            int count = 0;
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!filter.name.StartsWith(StreamName, StringComparison.Ordinal) || filter.sharedMesh == null) continue;
                Bounds local = filter.sharedMesh.bounds;
                Vector2[] corners = new[]
                {
                    new Vector3(local.min.x, 0f, local.min.z), new Vector3(local.max.x, 0f, local.min.z),
                    new Vector3(local.max.x, 0f, local.max.z), new Vector3(local.min.x, 0f, local.max.z),
                }.Select(c => filter.transform.TransformPoint(c)).Select(w => new Vector2(w.x, w.z)).ToArray();
                Vector2 min = corners.Aggregate(Vector2.Min), max = corners.Aggregate(Vector2.Max);
                for (int x = Mathf.FloorToInt(min.x); x <= Mathf.CeilToInt(max.x); x++)
                    for (int z = Mathf.FloorToInt(min.y); z <= Mathf.CeilToInt(max.y); z++)
                        if (InsideQuad(corners, new Vector2(x, z)) && blocked.Add(new Vector2Int(x, z))) count++;
            }
            return count;
        }

        private static bool InsideQuad(Vector2[] quad, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = quad.Length - 1; i < quad.Length; j = i++)
                if ((quad[i].y > p.y) != (quad[j].y > p.y) &&
                    p.x < (quad[j].x - quad[i].x) * (p.y - quad[i].y) / (quad[j].y - quad[i].y) + quad[i].x)
                    inside = !inside;
            return inside;
        }

        // The wall, gate, pillars and bench block every cell their models cover.
        private static int AddSolidModels(GameObject root, HashSet<Vector2Int> blocked)
        {
            int count = 0;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is ParticleSystemRenderer || !IsSolidModel(renderer.transform)) continue;
                Bounds bounds = renderer.bounds;
                const float Inset = 0.15f;
                for (int x = Mathf.RoundToInt(bounds.min.x + Inset); x <= Mathf.RoundToInt(bounds.max.x - Inset); x++)
                    for (int z = Mathf.RoundToInt(bounds.min.z + Inset); z <= Mathf.RoundToInt(bounds.max.z - Inset); z++)
                        if (blocked.Add(new Vector2Int(x, z))) count++;
            }
            return count;
        }

        private static bool IsSolidModel(Transform transform)
        {
            for (Transform t = transform; t != null; t = t.parent)
                if (SolidModels.Any(t.name.StartsWith)) return true;
            return false;
        }

        // One obstacle per run of blocked cells along a row, to keep the object count down.
        private static void AddBlockers(Transform parent, HashSet<Vector2Int> blocked)
        {
            foreach (IGrouping<int, Vector2Int> row in blocked.GroupBy(cell => cell.y).OrderBy(g => g.Key))
            {
                List<int> xs = row.Select(cell => cell.x).OrderBy(x => x).ToList();
                for (int i = 0; i < xs.Count;)
                {
                    int start = xs[i], width = 1;
                    while (i + width < xs.Count && xs[i + width] == start + width) width++;
                    GameObject blocker = new GameObject($"Open_{start}_{row.Key}");
                    blocker.transform.SetParent(parent, false);
                    blocker.transform.position = new Vector3(start, 0f, row.Key);
                    SerializedObject serialized = new SerializedObject(blocker.AddComponent<GridObstacle>());
                    serialized.FindProperty("useTransformAsAnchor").boolValue = true;
                    serialized.FindProperty("footprintOffset").vector2IntValue = Vector2Int.zero;
                    serialized.FindProperty("footprintSize").vector2IntValue = new Vector2Int(width, 1);
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    i += width;
                }
            }
        }

        private static void WriteReport(HashSet<Vector2Int> trails, HashSet<Vector2Int> blocked, HashSet<Vector2Int> reachable)
        {
            StringBuilder report = new StringBuilder("x,z,state\n");
            for (int x = Bounds.xMin; x < Bounds.xMax; x++)
                for (int z = Bounds.yMin; z < Bounds.yMax; z++)
                {
                    Vector2Int cell = new Vector2Int(x, z);
                    string state = blocked.Contains(cell) ? "blocked" : trails.Contains(cell) ? "trail" : reachable.Contains(cell) ? "open" : "unreached";
                    report.Append(x).Append(',').Append(z).Append(',').Append(state).Append('\n');
                }
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-groundReport");
            string path = index >= 0 && index + 1 < args.Length ? args[index + 1]
                : Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs", "forest-open-ground.csv");
            File.WriteAllText(path, report.ToString());
        }
    }
}
