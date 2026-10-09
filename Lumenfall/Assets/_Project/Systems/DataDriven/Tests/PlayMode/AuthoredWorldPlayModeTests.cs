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
        [UnityTearDown]
        public IEnumerator UnloadWorld()
        {
            Scene scene = SceneManager.GetSceneByName("SampleScene");
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
        }

        [UnityTest]
        public IEnumerator SampleWorldLoadsRoutesRestoresAndRefreshesQuestGatedNpcs()
        {
            // Keep the test runner alive and isolate quest assertions from the menu's saved backdrop.
            yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Additive);
            SceneManager.SetActiveScene(SceneManager.GetSceneByName("SampleScene"));
            yield return null;
            GameEventManager.Instance.ReloadDefinitions();

            DataDrivenWorldLoader loader = DataDrivenWorldLoader.Instance;
            Assert.That(loader, Is.Not.Null, "SampleScene should have a wired world loader.");
            Assert.That(loader.LoadedMap.id, Is.EqualTo("campus"));
            GridMover player = Object.FindAnyObjectByType<GridMover>();
            Assert.That(player, Is.Not.Null, "SampleScene should have a player for portal and save checks.");

            GridPortal outward = Object.FindObjectsByType<GridPortal>(FindObjectsInactive.Exclude)
                .FirstOrDefault(portal => portal.Content != null && portal.Content.destination != null &&
                    portal.Content.destination.area != null && portal.Content.destination.area.definition.id == "eastForest");
            Assert.That(outward, Is.Not.Null, "Campus should have an authored eastForest portal.");
            Assert.That(loader.SwitchArea(outward.Content, Vector2Int.zero), Is.True,
                "The authored campus to eastForest route should accept its migrated arrival cell.");
            Assert.That(loader.CurrentArea.definition.id, Is.EqualTo("eastForest"));

            string savedWorld = loader.CaptureStateJson();
            GridPortal returnPortal = Object.FindObjectsByType<GridPortal>(FindObjectsInactive.Exclude)
                .FirstOrDefault(portal => portal.Content != null && portal.Content.destination != null &&
                    portal.Content.destination.area != null && portal.Content.destination.area.definition.id == "campus");
            Assert.That(returnPortal, Is.Not.Null, "eastForest should have an authored return portal to campus.");
            Assert.That(loader.SwitchArea(returnPortal.Content, Vector2Int.zero), Is.True,
                "The authored eastForest return route should accept its migrated arrival cell.");
            Assert.That(loader.CurrentArea.definition.id, Is.EqualTo("campus"));
            Assert.That(loader.RestoreStateJson(savedWorld), Is.True, "World save should restore the saved area and cell.");
            Assert.That(loader.CurrentArea.definition.id, Is.EqualTo("eastForest"));

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

            loader.Load(availableAreas.Single(area => area.definition.id == "eastForest"), catalog);
            yield return null;
            loader.Load(campus, catalog);
            yield return null;
            Assert.That(trial.gameObject.activeSelf, Is.True,
                "Quest-gated visibility should persist when revisiting an area.");
        }
    }
}

