using System.Collections;
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
            Assert.That(loader.LoadedMap.id, Is.EqualTo("campus"));
            GridMover player = Object.FindAnyObjectByType<GridMover>();
            Assert.That(player, Is.Not.Null, "SampleScene should have a player for portal and save checks.");

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
                new { areaId = "B02.Ground", portalId = "P.B02.C05", arrival = new Vector2Int(23, 11) },
                new { areaId = "B03.Ground", portalId = "P.B03.C05", arrival = new Vector2Int(53, 7) },
                new { areaId = "B04.Ground", portalId = "P.B04.C05", arrival = new Vector2Int(85, 47) }
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
            Assert.That(activeRouteCount, Is.EqualTo(10), "All legacy active portal edges should be present and traversable.");

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
        }

        [UnityTest]
        public IEnumerator ThreeWideCampusPortalEntersB02FromEveryCell()
        {
            yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
            yield return null;

            DataDrivenWorldLoader loader = DataDrivenWorldLoader.Instance;
            FieldInfo catalogField = typeof(DataDrivenWorldLoader).GetField("npcCatalog", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo areasField = typeof(DataDrivenWorldLoader).GetField("availableAreas", BindingFlags.Instance | BindingFlags.NonPublic);
            NpcCatalogAsset catalog = (NpcCatalogAsset)catalogField.GetValue(loader);
            WorldAreaAsset[] availableAreas = (WorldAreaAsset[])areasField.GetValue(loader);
            WorldAreaAsset campus = availableAreas.Single(area => area.definition.id == "campus");

            for (int x = 19; x <= 21; x++)
            {
                loader.Load(campus, catalog);
                yield return null;
                GridPortal entrance = Object.FindObjectsByType<GridPortal>(FindObjectsInactive.Exclude)
                    .Single(portal => portal.Content != null && portal.Content.id == "P.C05.B02");
                Assert.That(entrance.Content.cell, Is.EqualTo(new Vector2Int(19, 46)));
                Assert.That(entrance.Content.footprint, Is.EqualTo(new Vector2Int(3, 1)));
                Assert.That(entrance.World.IsCellBlocked(new Vector2Int(x, 46)), Is.False);

                GridMover player = Object.FindAnyObjectByType<GridMover>();
                Assert.That(player.TryTeleportTo(entrance.World, new Vector2Int(x, 46)), Is.True);
                Assert.That(entrance.TryEnter(player), Is.True, $"Campus B02 entrance cell ({x},46) should transition.");
                Assert.That(loader.CurrentArea.definition.id, Is.EqualTo("B02.Ground"));
                Assert.That(player.CurrentCell, Is.EqualTo(new Vector2Int(8, 3)));
            }
        }
    }
}

