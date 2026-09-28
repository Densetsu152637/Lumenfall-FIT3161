using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Lumenfall.DataDriven;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Lumenfall.Tests.World
{
    public sealed class AuthoredWorldPlayModeTests
    {
        [UnityTest]
        public IEnumerator SampleWorldLoadsRoutesRestoresAndRefreshesQuestGatedNpcs()
        {
            yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
            yield return null;

            DataDrivenWorldLoader loader = DataDrivenWorldLoader.Instance;
            Assert.That(loader, Is.Not.Null, "SampleScene should have a wired world loader.");
            Assert.That(loader.LoadedMap.id, Is.EqualTo("westForest"));
            GridMover player = Object.FindAnyObjectByType<GridMover>();
            Assert.That(player, Is.Not.Null, "SampleScene should have a player for portal and save checks.");

            GridPortal startingReturn = Object.FindObjectsByType<GridPortal>(FindObjectsInactive.Exclude)
                .Single(portal => portal.Content != null && portal.Content.id == "P.F03.A01");
            Assert.That(loader.SwitchArea(startingReturn.Content, Vector2Int.zero), Is.True,
                "The forest start should be able to reach campus.");
            Assert.That(loader.CurrentArea.definition.id, Is.EqualTo("campus"));

            GridPortal outward = Object.FindObjectsByType<GridPortal>(FindObjectsInactive.Exclude)
                .FirstOrDefault(portal => portal.Content != null && portal.Content.destination != null &&
                    portal.Content.destination.area != null && portal.Content.destination.area.definition.id == "westForest");
            Assert.That(outward, Is.Not.Null, "Campus should have an authored westForest portal.");
            Assert.That(loader.SwitchArea(outward.Content, Vector2Int.zero), Is.True,
                "The authored campus to westForest route should accept its migrated arrival cell.");
            Assert.That(loader.CurrentArea.definition.id, Is.EqualTo("westForest"));

            string savedWorld = loader.CaptureStateJson();
            GridPortal returnPortal = Object.FindObjectsByType<GridPortal>(FindObjectsInactive.Exclude)
                .FirstOrDefault(portal => portal.Content != null && portal.Content.destination != null &&
                    portal.Content.destination.area != null && portal.Content.destination.area.definition.id == "campus");
            Assert.That(returnPortal, Is.Not.Null, "westForest should have an authored return portal to campus.");
            Assert.That(loader.SwitchArea(returnPortal.Content, Vector2Int.zero), Is.True,
                "The authored westForest return route should accept its migrated arrival cell.");
            Assert.That(loader.CurrentArea.definition.id, Is.EqualTo("campus"));
            string legacySavedWorld = savedWorld.Replace("westForest", "eastForest");
            Assert.That(loader.RestoreStateJson(legacySavedWorld), Is.True, "A legacy eastForest save should migrate and restore the saved area and cell.");
            Assert.That(loader.CurrentArea.definition.id, Is.EqualTo("westForest"));

            FieldInfo catalogField = typeof(DataDrivenWorldLoader).GetField("npcCatalog", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo areasField = typeof(DataDrivenWorldLoader).GetField("availableAreas", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo startingAreaField = typeof(DataDrivenWorldLoader).GetField("startingArea", BindingFlags.Instance | BindingFlags.NonPublic);
            NpcCatalogAsset catalog = (NpcCatalogAsset)catalogField.GetValue(loader);
            WorldAreaAsset[] availableAreas = (WorldAreaAsset[])areasField.GetValue(loader);
            WorldAreaAsset startingArea = (WorldAreaAsset)startingAreaField.GetValue(loader);

            var migratedReturns = new[]
            {
                new { areaId = "B02.Ground", portalId = "P.B02.C05", arrival = new Vector2Int(11, 68) },
                new { areaId = "B03.Ground", portalId = "P.B03.C05", arrival = new Vector2Int(7, 38) },
                new { areaId = "B04.Ground", portalId = "P.B04.C05", arrival = new Vector2Int(47, 6) }
            };
            foreach (var expected in migratedReturns)
            {
                WorldAreaAsset interior = availableAreas.Single(area => area.definition.id == expected.areaId);
                loader.Load(interior, catalog);
                yield return null;
                GridPortal exit = Object.FindObjectsByType<GridPortal>(FindObjectsInactive.Exclude)
                    .First(portal => portal.Content != null && portal.Content.id == expected.portalId);
                Assert.That(exit.Content.destination.cell + exit.Content.arrivalOffset, Is.EqualTo(expected.arrival),
                    expected.areaId + " should use the nearest walkable campus approach cell.");
                Assert.That(loader.SwitchArea(exit.Content, Vector2Int.zero), Is.True,
                    expected.areaId + " return route should accept its corrected campus arrival cell.");
                Assert.That(loader.CurrentArea.definition.id, Is.EqualTo("campus"));
            }

            WorldAreaAsset[] allAreas = new[] { startingArea }.Concat(availableAreas).Distinct().ToArray();
            int activeRouteCount = 0;
            foreach (WorldAreaAsset area in allAreas)
            {
                PortalContentAsset[] activeRoutes = area.areaPrefab.GetComponentsInChildren<GridPortal>(true)
                    .Select(portal => portal.Content).Where(content => content != null && !content.reserved).ToArray();
                foreach (PortalContentAsset route in activeRoutes)
                {
                    Assert.That(route.destination, Is.Not.Null,
                        "Active route " + area.definition.id + "/" + route.id + " needs a direct destination.");
                    Assert.That(route.destination.area.areaPrefab.GetComponentsInChildren<GridPortal>(true)
                        .Any(portal => portal.Content == route.destination), Is.True,
                        "Destination portal " + route.destination.id + " must exist in its area prefab.");
                    loader.Load(area, catalog);
                    yield return null;
                    GridPortal livePortal = Object.FindObjectsByType<GridPortal>(FindObjectsInactive.Exclude)
                        .FirstOrDefault(portal => portal.Content == route);
                    Assert.That(livePortal, Is.Not.Null, "Area prefab did not activate portal " + route.id + ".");
                    Assert.That(loader.SwitchArea(route, Vector2Int.zero), Is.True,
                        area.definition.id + "/" + route.id + " should reach " + route.destination.area.definition.id + ".");
                    Assert.That(loader.CurrentArea, Is.EqualTo(route.destination.area));
                    activeRouteCount++;
                }
            }
            Assert.That(activeRouteCount, Is.EqualTo(9), "All active forest, campus, and building portal edges should be traversable.");

            WorldAreaAsset campus = allAreas.Single(area => area.definition.id == "campus");
            loader.Load(campus, catalog);
            yield return null;
            DynamicNpc orientation = Object.FindObjectsByType<DynamicNpc>(FindObjectsInactive.Include)
                .First(npc => npc.Definition != null && npc.Definition.id == "cinna_cirra_orientation");
            DynamicNpc trial = Object.FindObjectsByType<DynamicNpc>(FindObjectsInactive.Include)
                .First(npc => npc.Definition != null && npc.Definition.id == "cinna_cirra_trial");
            GameEventRuntime events = GameEventManager.Instance.Runtime;
            if (events.GetQuestStatus("cinna_out_of_alignment") == QuestStatus.NotStarted)
                Assert.That(events.StartQuest("cinna_out_of_alignment"), Is.True);
            Assert.That(events.CompleteQuest("cinna_out_of_alignment"), Is.True);
            yield return null;
            Assert.That(orientation.gameObject.activeSelf, Is.False,
                "The orientation NPC should hide after its quest completes.");
            Assert.That(trial.gameObject.activeSelf, Is.True,
                "The trial NPC should appear at its explicit authored coordinates.");

            loader.Load(availableAreas.Single(area => area.definition.id == "westForest"), catalog);
            yield return null;
            loader.Load(campus, catalog);
            yield return null;
            Assert.That(trial.gameObject.activeSelf, Is.True,
                "Quest-gated visibility should persist when revisiting an area.");
        }

        [UnityTest]
        public IEnumerator PlayerCanWalkThroughWestForestGateToCampus()
        {
            yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
            yield return null;

            DataDrivenWorldLoader loader = DataDrivenWorldLoader.Instance;
            Assert.That(loader, Is.Not.Null);
            FieldInfo catalogField = typeof(DataDrivenWorldLoader).GetField("npcCatalog", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo areasField = typeof(DataDrivenWorldLoader).GetField("availableAreas", BindingFlags.Instance | BindingFlags.NonPublic);
            NpcCatalogAsset catalog = (NpcCatalogAsset)catalogField.GetValue(loader);
            WorldAreaAsset[] availableAreas = (WorldAreaAsset[])areasField.GetValue(loader);
            loader.Load(availableAreas.Single(area => area.definition.id == "westForest"), catalog);
            yield return null;

            GridPortal gate = Object.FindObjectsByType<GridPortal>(FindObjectsInactive.Exclude)
                .Single(portal => portal.Content != null && portal.Content.id == "P.F03.A01");
            Assert.That(gate.Content.cell, Is.EqualTo(new Vector2Int(16, 48)));
            Assert.That(gate.Content.footprint, Is.EqualTo(new Vector2Int(3, 1)));
            foreach (Vector2Int gateCell in gate.EnumeratePortalCells())
            {
                Assert.That(gate.World.GetPortalAt(gateCell), Is.SameAs(gate));
                Assert.That(gate.World.IsCellBlocked(gateCell), Is.False,
                    $"WestForest gate cell {gateCell} must be enterable even when its threshold overlaps a baked obstacle.");
            }

            GridMover player = Object.FindAnyObjectByType<GridMover>();
            Assert.That(player.TryTeleportTo(gate.World, new Vector2Int(16, 47)), Is.True,
                "The player should be able to stand immediately before the westForest gate.");
            Assert.That(gate.World.CanTraverse(new Vector2Int(16, 47), new Vector2Int(16, 48)), Is.True,
                "The player should be able to step onto the westForest gate portal.");
            Assert.That(player.TryTeleportTo(gate.World, new Vector2Int(16, 48)), Is.True);
            Assert.That(gate.TryEnter(player), Is.True,
                "Entering the registered westForest gate should perform the area transition.");
            Assert.That(loader.CurrentArea.definition.id, Is.EqualTo("campus"));
            Assert.That(player.CurrentCell, Is.EqualTo(new Vector2Int(25, 6)));

            GridPortal b02Entrance = Object.FindObjectsByType<GridPortal>(FindObjectsInactive.Exclude)
                .Single(portal => portal.Content != null && portal.Content.id == "P.C05.B02");
            Assert.That(CanReach(gate.World, player.CurrentCell, b02Entrance.FirstCell), Is.True,
                "The campus route from the forest arrival to B02 must be walkable.");
            Assert.That(player.TryTeleportTo(b02Entrance.World, b02Entrance.FirstCell), Is.True);
            Assert.That(b02Entrance.TryEnter(player), Is.True, "The B02 entrance must transfer the player.");
            Assert.That(loader.CurrentArea.definition.id, Is.EqualTo("B02.Ground"));
            Assert.That(player.CurrentCell, Is.EqualTo(new Vector2Int(10, 5)));
            GridPortal b02Exit = Object.FindObjectsByType<GridPortal>(FindObjectsInactive.Exclude)
                .Single(portal => portal.Content != null && portal.Content.id == "P.B02.C05");
            Assert.That(CanReach(b02Exit.World, player.CurrentCell, new Vector2Int(12, 5)), Is.True,
                "The B02 landing must reach Stella's interaction side.");
            Assert.That(CanReach(b02Exit.World, player.CurrentCell, b02Exit.FirstCell), Is.True,
                "The B02 landing must reach its return portal.");
            Assert.That(player.TryTeleportTo(b02Exit.World, b02Exit.FirstCell), Is.True);
            Assert.That(b02Exit.TryEnter(player), Is.True, "The B02 exit must return the player to campus.");
            Assert.That(loader.CurrentArea.definition.id, Is.EqualTo("campus"));
            Assert.That(player.CurrentCell, Is.EqualTo(new Vector2Int(11, 68)));
        }

        private static bool CanReach(GridWorld world, Vector2Int from, Vector2Int destination)
        {
            var visited = new HashSet<Vector2Int> { from };
            var queue = new Queue<Vector2Int>();
            queue.Enqueue(from);
            while (queue.Count > 0)
            {
                Vector2Int cell = queue.Dequeue();
                if (cell == destination) return true;
                foreach (Vector2Int step in new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right })
                {
                    Vector2Int next = cell + step;
                    if (!visited.Contains(next) && world.CanTraverse(cell, next))
                    {
                        visited.Add(next);
                        queue.Enqueue(next);
                    }
                }
            }
            return false;
        }
    }
}

