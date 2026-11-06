using System.Collections.Generic;
using Lumenfall.DataDriven;
using Lumenfall.GridMovement;
using Lumenfall.Spells;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Lumenfall.Tests.GameEvents
{
    /// <summary>
    /// Fixes made by hand in area prefabs: blockers that match what the player sees, blockers that follow the
    /// character they belong to, and props that should not be there. Cells are read the way GridObstacle registers them
    /// (one unit per cell, areas at the origin).
    /// </summary>
    public sealed class AuthoredLayoutTests
    {
        private const string Areas = "Assets/_Project/World/Areas/Prefabs/";

        [Test]
        public void TheDormitoryPartitionAndCabinetCannotBeWalkedThrough()
        {
            HashSet<Vector2Int> blocked = BlockedCells(Load("Dormitory.prefab"));
            Assert.That(blocked, Has.Member(new Vector2Int(14, 13)), "Reception Cabinet East.");
            Assert.That(blocked, Has.Member(new Vector2Int(15, 13)), "The partition's half-covered end.");
            for (int x = 16; x <= 19; x++)
                Assert.That(blocked, Has.No.Member(new Vector2Int(x, 13)), "The hallway doorway stays open at x " + x);
        }

        [Test]
        public void TheWorkshopGyroscopeStellaAndCandelabraAreWhereTheyBelong()
        {
            GameObject workshop = Load("Arcane Workshop.prefab");
            HashSet<Vector2Int> blocked = BlockedCells(workshop);
            Assert.That(Cell(Find(workshop, "Stella")), Is.EqualTo(new Vector2Int(19, 10)));
            Assert.That(blocked, Has.Member(new Vector2Int(19, 10)), "Stella blocks her own tile.");
            Assert.That(Cell(Find(workshop, "magical_gyroscope")), Is.EqualTo(new Vector2Int(19, 14)));
            Assert.That(blocked, Has.Member(new Vector2Int(19, 14)), "The gyroscope blocks its own tile.");
            foreach (Vector2Int old in new[] { new Vector2Int(16, 15), new Vector2Int(19, 15), new Vector2Int(19, 16) })
                Assert.That(blocked, Has.No.Member(old), "No blocker is left where the gyroscope or Stella used to be: " + old);
            Assert.That(Find(workshop, "Teacher Table Candles").activeSelf, Is.False, "The candelabra outside the east wall is hidden.");
        }

        [Test]
        public void TheTerraceCinnasBlockerLeavesWithCinna()
        {
            Transform cinna = Find(Load("campus.prefab"), "Cinna Cirra (1)").transform;
            bool found = false;
            foreach (GridObstacle obstacle in cinna.GetComponentsInChildren<GridObstacle>(true))
                found |= Cells(obstacle).Contains(new Vector2Int(22, 51));
            Assert.That(found, Is.True, "The blocker on Cinna's terrace tile is a child of Cinna, so hiding Cinna frees the tile.");
        }

        [Test]
        public void TheExpandedForestReachesEveryPortalAndOpensTheGroundBetweenItsTrees()
        {
            GameObject forest = Load("West Forest.prefab");
            MapDefinition map = AssetDatabase.LoadAssetAtPath<WorldAreaAsset>("Assets/_Project/World/Areas/westForest.asset").definition;
            RectInt bounds = new RectInt(map.boundsMinX, map.boundsMinY, map.width, map.height);
            Assert.That(bounds.xMin < 0 && bounds.yMin < 0, Is.True, "The forest grows west and south, so the original cells keep their coordinates.");

            HashSet<Vector2Int> blocked = BlockedCells(forest);
            HashSet<Vector2Int> portals = new HashSet<Vector2Int>();
            foreach (GridPortal portal in forest.GetComponentsInChildren<GridPortal>(true))
                for (int x = -portal.ExtendNegativeX; x <= portal.ExtendPositiveX; x++)
                    for (int z = -portal.ExtendNegativeZ; z <= portal.ExtendPositiveZ; z++)
                        portals.Add(Cell(portal.gameObject) + new Vector2Int(x, z));
            Vector2Int spawn = new Vector2Int(map.spawn.x, map.spawn.y);
            HashSet<Vector2Int> reached = new HashSet<Vector2Int> { spawn };
            Queue<Vector2Int> queue = new Queue<Vector2Int>(reached);
            while (queue.Count > 0)
            {
                Vector2Int cell = queue.Dequeue();
                foreach (Vector2Int next in new[] { cell + Vector2Int.up, cell + Vector2Int.down, cell + Vector2Int.left, cell + Vector2Int.right })
                    if (bounds.Contains(next) && (!blocked.Contains(next) || portals.Contains(next)) && reached.Add(next)) queue.Enqueue(next);
            }

            Assert.That(Cell(Find(forest, "P.F01")), Is.EqualTo(new Vector2Int(-27, 25)), "The deep forest portal ends the longer stone trail.");
            foreach (Vector2Int cell in new[] { new Vector2Int(17, 48), new Vector2Int(-27, 25) })
                Assert.That(reached, Has.Member(cell), "Portal cell " + cell);
            foreach (Vector2Int cell in new[] { new Vector2Int(8, 23), new Vector2Int(11, 3), new Vector2Int(24, 6), new Vector2Int(27, 26), new Vector2Int(18, 22) })
                Assert.That(reached, Has.Member(cell), "Story cell " + cell + " is still on the walkable paths.");
            foreach (Vector2Int cell in new[] { new Vector2Int(7, -16), new Vector2Int(-12, 26), new Vector2Int(-13, 0) })
                Assert.That(reached, Has.Member(cell), "New ground at " + cell + " joins the paths.");

            Transform orio = Find(forest, "Orio").transform;
            Assert.That(Cell(orio.gameObject), Is.EqualTo(new Vector2Int(-12, 24)), "Orio waits in the Waystone clearing.");
            bool orioBlocks = false;
            foreach (GridObstacle obstacle in orio.GetComponentsInChildren<GridObstacle>(true))
                orioBlocks |= Cells(obstacle).Contains(new Vector2Int(-12, 24));
            Assert.That(orioBlocks, Is.True, "Orio's blocker belongs to Orio, so it follows him if he moves.");
            Assert.That(reached, Has.Member(new Vector2Int(-11, 24)), "The cell east of Orio stays free for his dais portal.");

            // Playtesters found the open grass beside the trails fenced off by invisible tiles. The fences are switched off
            // (kept, so the authoring tools can still find the trails), and only trees, rocks, water and walls block.
            HashSet<Vector2Int> fences = new HashSet<Vector2Int>(), trails = new HashSet<Vector2Int> { spawn };
            foreach (GridObstacle obstacle in forest.GetComponentsInChildren<GridObstacle>(true))
            {
                bool fence = obstacle.name.StartsWith("T_tile") || obstacle.name.StartsWith("T_ext");
                if (fence) Assert.That(ActiveInPrefab(obstacle.transform), Is.False, "Trail fence " + obstacle.name + " is switched off.");
                // The trails are what could be walked before: inside the fences and clear of every other blocker then.
                if (fence || (obstacle.enabled && ActiveInPrefab(obstacle.transform) && !Under(obstacle.transform, "Open Forest Blockers")))
                    fences.UnionWith(Cells(obstacle));
            }
            Queue<Vector2Int> along = new Queue<Vector2Int>(trails);
            while (along.Count > 0)
            {
                Vector2Int cell = along.Dequeue();
                foreach (Vector2Int next in new[] { cell + Vector2Int.up, cell + Vector2Int.down, cell + Vector2Int.left, cell + Vector2Int.right })
                    if (bounds.Contains(next) && (!fences.Contains(next) || portals.Contains(next)) && trails.Add(next)) along.Enqueue(next);
            }
            Assert.That(reached.Count, Is.GreaterThan(trails.Count * 3), "The ground between the trees opens up well beyond the trails.");
            foreach (Vector2Int cell in trails)
                Assert.That(reached, Has.Member(cell), "Every trail cell is still walkable: " + cell);

            HashSet<Vector2Int> leftOpen = new HashSet<Vector2Int>(trails);
            leftOpen.UnionWith(portals);
            foreach (NpcDefinition npc in AssetDatabase.LoadAssetAtPath<NpcCatalogAsset>("Assets/_Project/World/Areas/NpcCatalog.asset").npcs)
                if (npc != null && npc.mapId == map.id) leftOpen.Add(new Vector2Int(npc.x, npc.y));
            int trunks = 0;
            foreach (Terrain terrain in forest.GetComponentsInChildren<Terrain>(true))
            {
                TerrainData data = terrain.terrainData;
                foreach (TreeInstance tree in data.treeInstances)
                {
                    GameObject prototype = data.treePrototypes[tree.prototypeIndex].prefab;
                    if (prototype != null && prototype.name.ToLowerInvariant().Contains("mushroom")) continue;
                    Vector3 position = terrain.transform.position + Vector3.Scale(tree.position, data.size);
                    Vector2Int trunk = new Vector2Int(Mathf.RoundToInt(position.x), Mathf.RoundToInt(position.z));
                    if (!bounds.Contains(trunk) || leftOpen.Contains(trunk)) continue;
                    trunks++;
                    Assert.That(blocked, Has.Member(trunk), "A painted tree or rock cannot be walked through: " + trunk);
                }
            }
            Assert.That(trunks, Is.GreaterThan(100), "The forest's trees were all checked.");
        }

        [Test]
        public void TheRuneDaisSocketsStandFarEnoughApartToTellWhichOneIsCast()
        {
            // Playtesters could not tell which close-set socket Rotara would turn, so the dais was spread out.
            GameObject deepForest = Load("Deep Forest.prefab");
            string[] sockets = Lumenfall.Quests.OrioRuneDiscPuzzle.SocketIds;
            for (int i = 0; i < sockets.Length; i++)
                for (int j = i + 1; j < sockets.Length; j++)
                {
                    Vector2Int a = Cell(Find(deepForest, sockets[i])), b = Cell(Find(deepForest, sockets[j]));
                    Assert.That(Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y), Is.GreaterThanOrEqualTo(2),
                        sockets[i] + " and " + sockets[j] + " are not side by side.");
                }
        }

        [Test]
        public void ThePlazaLampsBlockWhereTheyStandAndEveryCampusDoorStaysReachable()
        {
            // The plaza's hand-placed tiles sat beside some lamps, so players walked through the lamp and bumped into air.
            GameObject campus = Load("campus.prefab");
            HashSet<Vector2Int> blocked = BlockedCells(campus);
            int lamps = 0;
            foreach (Transform part in campus.GetComponentsInChildren<Transform>(true))
            {
                if (!part.name.StartsWith("Park-Lamp6") || PrefabUtility.GetNearestPrefabInstanceRoot(part.gameObject) != part.gameObject) continue;
                lamps++;
                Assert.That(blocked, Has.Member(Cell(part.gameObject)), part.name + " cannot be walked through.");
            }
            Assert.That(lamps, Is.GreaterThanOrEqualTo(8), "The fountain's ring of lamps was checked.");

            HashSet<Vector2Int> doors = new HashSet<Vector2Int>();
            foreach (GridPortal portal in campus.GetComponentsInChildren<GridPortal>(true))
                if (portal.Content != null && !portal.Content.blocked && !portal.Content.disabled) doors.Add(Cell(portal.gameObject));
            Vector2Int forestGate = Cell(Find(campus, "P.C00"));
            HashSet<Vector2Int> reached = new HashSet<Vector2Int> { forestGate };
            Queue<Vector2Int> queue = new Queue<Vector2Int>(reached);
            RectInt bounds = new RectInt(0, 0, 54, 92);
            while (queue.Count > 0)
            {
                Vector2Int cell = queue.Dequeue();
                foreach (Vector2Int next in new[] { cell + Vector2Int.up, cell + Vector2Int.down, cell + Vector2Int.left, cell + Vector2Int.right })
                    if (bounds.Contains(next) && (!blocked.Contains(next) || doors.Contains(next)) && reached.Add(next)) queue.Enqueue(next);
            }
            foreach (Vector2Int door in doors)
                Assert.That(reached, Has.Member(door), "The open campus door at " + door + " can be walked to from the forest gate.");
            Assert.That(reached, Has.Member(Cell(Find(campus, "P.C04"))), "The Library door in particular.");
        }

        [Test]
        public void RecolouringTheForgottenManInTheEditorLeavesTheSharedMaterialsAlone()
        {
            // The baker builds him in the editor: his colours must go onto copies, never onto the model's own materials.
            GameObject model = Lumenfall.Story.SecretRoomArt.Load()?.devGlasses;
            Assert.That(model, Is.Not.Null, "SecretRoomArt's short-haired base model.");
            var before = new Dictionary<Material, Color>();
            foreach (Renderer part in model.GetComponentsInChildren<Renderer>(true))
                foreach (Material material in part.sharedMaterials)
                    if (material != null && material.HasProperty("_BaseColor")) before[material] = material.GetColor("_BaseColor");

            GameObject man = Lumenfall.Story.DevChibis.Create(model, Lumenfall.Story.DevChibis.OldMan, null);
            try
            {
                foreach (Renderer part in man.GetComponentsInChildren<Renderer>(true))
                    foreach (Material material in part.sharedMaterials)
                        if (material != null && material.name.Contains("_Hair"))
                            Assert.That(EditorUtility.IsPersistent(material), Is.False, part.name + " is recoloured on a copy.");
                foreach (KeyValuePair<Material, Color> original in before)
                    Assert.That(original.Key.GetColor("_BaseColor"), Is.EqualTo(original.Value), original.Key.name + " is left as it was.");
            }
            finally { Object.DestroyImmediate(man); }
        }

        [Test]
        public void AmpereEffectsArePlayedAndClearedSoTheyCanFireAgain()
        {
            WorldStateService previous = WorldStateService.Instance;
            GameObject world = new GameObject("world"), host = new GameObject("ampere");
            try
            {
                WorldStateService state = world.AddComponent<WorldStateService>();
                EditModeLifecycle.SetWorldInstance(state);
                state.BeginMapRebuild("B02.Ground");
                state.EndMapRebuild();
                AmperePresentation presentation = host.AddComponent<AmperePresentation>();
                EditModeLifecycle.Call(presentation, "Update");
                int fired = 0;
                state.EffectQueued += _ => fired++;
                state.QueueEffect("stella-relic:1:on");
                state.QueueEffect("stella-relic:1:on");
                Assert.That(fired, Is.EqualTo(2), "A played effect is acknowledged, so the same resonance can flash again.");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(world);
                EditModeLifecycle.SetWorldInstance(previous);
            }
        }

        private static GameObject Load(string prefab)
        {
            GameObject area = AssetDatabase.LoadAssetAtPath<GameObject>(Areas + prefab);
            Assert.That(area, Is.Not.Null, prefab);
            return area;
        }

        private static GameObject Find(GameObject root, string name)
        {
            foreach (Transform part in root.GetComponentsInChildren<Transform>(true))
                if (part.name == name) return part.gameObject;
            Assert.Fail(name + " is missing from " + root.name);
            return null;
        }

        private static Vector2Int Cell(GameObject item) =>
            new Vector2Int(Mathf.RoundToInt(item.transform.position.x), Mathf.RoundToInt(item.transform.position.z));

        private static HashSet<Vector2Int> BlockedCells(GameObject root)
        {
            HashSet<Vector2Int> cells = new HashSet<Vector2Int>();
            foreach (GridObstacle obstacle in root.GetComponentsInChildren<GridObstacle>(true))
                if (obstacle.enabled && ActiveInPrefab(obstacle.transform)) cells.UnionWith(Cells(obstacle));
            return cells;
        }

        private static HashSet<Vector2Int> Cells(GridObstacle obstacle)
        {
            SerializedObject serialized = new SerializedObject(obstacle);
            Vector2Int anchor = serialized.FindProperty("useTransformAsAnchor").boolValue
                ? Cell(obstacle.gameObject) : serialized.FindProperty("anchorCell").vector2IntValue;
            HashSet<Vector2Int> cells = new HashSet<Vector2Int>();
            for (int x = 0; x < obstacle.FootprintSize.x; x++)
                for (int y = 0; y < obstacle.FootprintSize.y; y++)
                    cells.Add(anchor + obstacle.FootprintOffset + new Vector2Int(x, y));
            return cells;
        }

        private static bool Under(Transform part, string ancestor)
        {
            for (Transform current = part; current != null; current = current.parent)
                if (current.name == ancestor) return true;
            return false;
        }

        // Prefab assets are not in a scene, so activeInHierarchy does not apply; walk the parents instead.
        private static bool ActiveInPrefab(Transform part)
        {
            for (Transform current = part; current != null; current = current.parent)
                if (!current.gameObject.activeSelf) return false;
            return true;
        }
    }
}
