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
    /// Rebuilds the campus collision around the Library and the fountain plaza from what is actually standing there.
    /// The plaza's blockers were hand-placed tiles, and several sat a tile away from the lamp, bench or tree they stood
    /// for, so players bumped into empty air beside a lamp and walked through the lamp itself. Within
    /// <see cref="Region"/> this tool deactivates the hand-placed decoration, tree and fountain tiles (keeping any of
    /// their cells outside the region blocked) and adds blockers under <see cref="GeneratedRootName"/>:
    /// <list type="bullet">
    /// <item>lamps, benches, tables, signs and the fountain block the cells their models cover, or the cell they stand
    /// in when they are thinner than a cell;</item>
    /// <item>trees block only their trunk cell, so the player can walk under the canopy;</item>
    /// <item>flowers, bushes and grass do not block.</item>
    /// </list>
    /// Buildings, the outer wall and NPC blockers are untouched, and portal and NPC cells are never blocked. The run
    /// refuses to change anything if a portal or any cell that was reachable before would be cut off. To undo, delete
    /// <see cref="GeneratedRootName"/> and reactivate the deactivated tiles. It writes the region to
    /// Logs/campus-plaza-ground.csv. Run it from Lumenfall > World > Rebuild Campus Plaza Collision.
    /// </summary>
    public static class CampusPlazaGroundTool
    {
        private const string PrefabPath = "Assets/_Project/World/Areas/Prefabs/campus.prefab";
        private const string NpcCatalogPath = "Assets/_Project/World/Areas/NpcCatalog.asset";
        private const string GeneratedRootName = "Plaza Blockers";
        /// <summary>The Library's front, the fountain plaza and the paths between them (cells x 19..50, z 37..56).</summary>
        private static readonly RectInt Region = new RectInt(19, 37, 32, 20);
        private static readonly RectInt Bounds = new RectInt(0, 0, 54, 92);
        /// <summary>Hand-placed blocker groups the tool replaces; buildings, the outer wall and NPCs are kept.</summary>
        private static readonly string[] ReplacedGroups = { "Decorations", "Trees", "Fountain" };
        private static readonly string[] TreeNames = { "tree", "sakura", "pine" };
        /// <summary>The scenery that stands in the way. Characters, puzzle objects and plants are left out.</summary>
        private static readonly string[] SceneryNames = { "park-", "sign-", "fountain", "lamp", "bench", "table" };
        /// <summary>No scenery prop covers more than this many cells across.</summary>
        private const int LargestProp = 6;
        /// <summary>Geometry starting above this height, in metres over the prop's base, overhangs rather than blocks.</summary>
        private const float WalkHeight = 1f;

        /// <summary>Creating Library/ + this file name makes the next script reload run the tool once, unattended.</summary>
        public const string RequestFileName = "lumenfall-campus-plaza.request";

        [InitializeOnLoadMethod]
        private static void ApplyRequested()
        {
            string requestPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Library", RequestFileName);
            if (!File.Exists(requestPath)) return;
            void RunOnce()
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(requestPath)) return;
                EditorApplication.update -= RunOnce;
                File.Delete(requestPath);
                Apply();
            }
            EditorApplication.update += RunOnce;
        }

        [MenuItem("Lumenfall/World/Rebuild Campus Plaza Collision")]
        public static void Apply() => Run(true);

        [MenuItem("Lumenfall/World/Preview Campus Plaza Collision")]
        public static void Preview() => Run(false);

        private static void Run(bool apply)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                Transform previous = root.transform.Find(GeneratedRootName);
                List<GridObstacle> all = root.GetComponentsInChildren<GridObstacle>(true)
                    .Where(o => previous == null || !o.transform.IsChildOf(previous)).ToList();
                List<GridObstacle> replaced = all.Where(o => IsReplacedGroup(o.transform) && Cells(o).Any(Region.Contains)).ToList();
                HashSet<Vector2Int> portals = PortalCells(root);

                // Collision as it stands, counting the replaced tiles as active even after a previous run deactivated them.
                HashSet<Vector2Int> before = new HashSet<Vector2Int>(all.Where(o => o.gameObject.activeInHierarchy || replaced.Contains(o)).SelectMany(Cells));
                HashSet<Vector2Int> kept = new HashSet<Vector2Int>(all.Where(o => !replaced.Contains(o) && o.gameObject.activeInHierarchy).SelectMany(Cells));

                HashSet<Vector2Int> keepOpen = new HashSet<Vector2Int>(portals);
                NpcCatalogAsset catalog = AssetDatabase.LoadAssetAtPath<NpcCatalogAsset>(NpcCatalogPath);
                foreach (NpcDefinition npc in catalog != null ? catalog.npcs : Array.Empty<NpcDefinition>())
                    if (npc != null && npc.mapId == "campus") keepOpen.Add(new Vector2Int(npc.x, npc.y));

                // The replaced tiles' cells outside the region stay blocked; inside it, blockers come from the props.
                HashSet<Vector2Int> added = new HashSet<Vector2Int>(replaced.SelectMany(Cells).Where(cell => !Region.Contains(cell)));
                StringBuilder report = new StringBuilder("x,z,state,source\n");
                foreach (KeyValuePair<Vector2Int, string> prop in PropCells(root))
                {
                    if (!Region.Contains(prop.Key) || keepOpen.Contains(prop.Key)) continue;
                    if (added.Add(prop.Key)) report.Append(prop.Key.x).Append(',').Append(prop.Key.y).Append(",prop,").Append(prop.Value).Append('\n');
                }
                HashSet<Vector2Int> after = new HashSet<Vector2Int>(kept);
                after.UnionWith(added);

                // Every portal and every cell that could be reached before must still be reachable, unless now blocked.
                // Start from the door that reaches the most of campus, so a gate onto a dead end cannot hide a problem.
                Vector2Int seed = portals.OrderByDescending(cell => Flood(cell, before, portals).Count).First();
                HashSet<Vector2Int> reachedBefore = Flood(seed, before, portals), reachedAfter = Flood(seed, after, portals);
                int cutOff = reachedBefore.Count(cell => !after.Contains(cell) && !reachedAfter.Contains(cell)) +
                             portals.Count(cell => reachedBefore.Contains(cell) && !reachedAfter.Contains(cell));
                for (int x = Region.xMin; x < Region.xMax; x++)
                    for (int z = Region.yMin; z < Region.yMax; z++)
                    {
                        Vector2Int cell = new Vector2Int(x, z);
                        string state = after.Contains(cell) ? before.Contains(cell) ? "still-blocked" : "newly-blocked"
                            : before.Contains(cell) ? "opened" : "open";
                        report.Append(x).Append(',').Append(z).Append(',').Append(state).Append(",\n");
                    }
                File.WriteAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs", "campus-plaza-ground.csv"), report.ToString());

                int opened = before.Count(cell => Region.Contains(cell) && !after.Contains(cell));
                int closed = after.Count(cell => Region.Contains(cell) && !before.Contains(cell));
                string summary = $"Campus plaza collision{(apply ? string.Empty : " (preview only)")}: {replaced.Count} hand-placed tiles replaced; " +
                    $"{opened} cells opened and {closed} newly blocked around the Library and fountain; {reachedAfter.Count} cells reachable (was {reachedBefore.Count}).";
                if (cutOff > 0)
                {
                    Debug.LogError(summary + $" {cutOff} reachable cells or portals would be cut off, so nothing was changed.");
                    return;
                }
                Debug.Log(summary);
                if (!apply) return;

                foreach (GridObstacle tile in replaced) tile.gameObject.SetActive(false);
                if (previous != null) UnityEngine.Object.DestroyImmediate(previous.gameObject);
                Transform generated = new GameObject(GeneratedRootName).transform;
                generated.SetParent(root.transform, false);
                AddBlockers(generated, added);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static bool IsReplacedGroup(Transform transform)
        {
            Transform parent = transform.parent;
            return parent != null && ReplacedGroups.Contains(parent.name);
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

        /// <summary>
        /// The cells each scenery prop stands on, named after the prop. A prop blocks the cells whose centres fall on its
        /// models' outline seen from above (their triangles projected onto the ground), so an angled bench covers the two
        /// or three cells under its seat rather than a square around it; a prop thinner than a cell, such as a lamp post,
        /// blocks the cell it stands in. Trees block only their trunk cell. Characters and puzzle objects are not scenery
        /// and keep their own blockers.
        /// </summary>
        internal static Dictionary<Vector2Int, string> PropCells(GameObject root)
        {
            Dictionary<Vector2Int, string> cells = new Dictionary<Vector2Int, string>();
            foreach (IGrouping<GameObject, MeshFilter> prop in root.GetComponentsInChildren<MeshFilter>(true)
                         .Where(f => f.sharedMesh != null && !UnderObstacles(f.transform) &&
                                     f.TryGetComponent(out MeshRenderer renderer) && renderer.enabled)
                         .GroupBy(f => PrefabUtility.GetNearestPrefabInstanceRoot(f.gameObject) ?? f.gameObject))
            {
                string name = prop.Key.name.ToLowerInvariant();
                bool tree = TreeNames.Any(name.Contains);
                if (!tree && !SceneryNames.Any(name.Contains)) continue;
                if (tree)
                {
                    cells[CellAt(prop.Key.transform.position)] = prop.Key.name;
                    continue;
                }
                HashSet<Vector2Int> covered = new HashSet<Vector2Int>();
                Vector2 sum = Vector2.zero;
                int count = 0;
                float ground = prop.Key.transform.position.y;
                foreach (MeshFilter part in prop)
                {
                    Vector3[] world = Array.ConvertAll(part.sharedMesh.vertices, v => part.transform.TransformPoint(v));
                    int[] triangles = part.sharedMesh.triangles;
                    foreach (Vector3 v in world) { sum += new Vector2(v.x, v.z); count++; }
                    // Only what stands in the lowest metre is in the way; a lamp's head or a sign's board overhangs.
                    for (int i = 0; i + 2 < triangles.Length; i += 3)
                    {
                        Vector3 a = world[triangles[i]], b = world[triangles[i + 1]], c = world[triangles[i + 2]];
                        if (Mathf.Min(a.y, b.y, c.y) - ground > WalkHeight) continue;
                        CoverTriangle(new Vector2(a.x, a.z), new Vector2(b.x, b.z), new Vector2(c.x, c.z), covered);
                    }
                }
                if (count == 0) continue;
                if (covered.Count == 0 || covered.Count > LargestProp * LargestProp) covered = new HashSet<Vector2Int> { CellAt(new Vector3(sum.x / count, 0f, sum.y / count)) };
                foreach (Vector2Int cell in covered) cells[cell] = prop.Key.name;
            }
            return cells;
        }

        // Adds every cell whose centre lies inside the triangle.
        private static void CoverTriangle(Vector2 a, Vector2 b, Vector2 c, HashSet<Vector2Int> covered)
        {
            int x0 = Mathf.CeilToInt(Mathf.Min(a.x, b.x, c.x)), x1 = Mathf.FloorToInt(Mathf.Max(a.x, b.x, c.x));
            int z0 = Mathf.CeilToInt(Mathf.Min(a.y, b.y, c.y)), z1 = Mathf.FloorToInt(Mathf.Max(a.y, b.y, c.y));
            if (x1 - x0 > LargestProp || z1 - z0 > LargestProp) return;
            float Side(Vector2 p, Vector2 q, Vector2 r) => (p.x - r.x) * (q.y - r.y) - (q.x - r.x) * (p.y - r.y);
            for (int x = x0; x <= x1; x++)
                for (int z = z0; z <= z1; z++)
                {
                    Vector2 p = new Vector2(x, z);
                    float d1 = Side(p, a, b), d2 = Side(p, b, c), d3 = Side(p, c, a);
                    bool negative = d1 < 0f || d2 < 0f || d3 < 0f, positive = d1 > 0f || d2 > 0f || d3 > 0f;
                    if (!(negative && positive)) covered.Add(new Vector2Int(x, z));
                }
        }

        private static bool UnderObstacles(Transform transform)
        {
            for (Transform t = transform; t != null; t = t.parent)
                if (t.name == "Obstacles" || t.name == GeneratedRootName) return true;
            return false;
        }

        private static HashSet<Vector2Int> Flood(Vector2Int seed, HashSet<Vector2Int> blocked, HashSet<Vector2Int> portals)
        {
            HashSet<Vector2Int> reached = new HashSet<Vector2Int> { seed };
            Queue<Vector2Int> queue = new Queue<Vector2Int>();
            queue.Enqueue(seed);
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

        // One obstacle per run of blocked cells along a row.
        private static void AddBlockers(Transform parent, HashSet<Vector2Int> blocked)
        {
            foreach (IGrouping<int, Vector2Int> row in blocked.GroupBy(cell => cell.y).OrderBy(g => g.Key))
            {
                List<int> xs = row.Select(cell => cell.x).OrderBy(x => x).ToList();
                for (int i = 0; i < xs.Count;)
                {
                    int start = xs[i], width = 1;
                    while (i + width < xs.Count && xs[i + width] == start + width) width++;
                    GameObject blocker = new GameObject($"Plaza_{start}_{row.Key}");
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
    }
}
