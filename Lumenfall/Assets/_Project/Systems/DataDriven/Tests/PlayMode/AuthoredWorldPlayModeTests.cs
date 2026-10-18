using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Lumenfall.DataDriven;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Quests;
using Lumenfall.Spells;
using Lumenfall.Startup;
using Lumenfall.Story;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Lumenfall.Tests.World
{
    public sealed class AuthoredWorldPlayModeTests
    {
        // Where Cinna stands on the C02 terrace and in the ice trial lobby (their NPC catalog cells).
        private static readonly Vector2Int TerraceCinnaCell = new Vector2Int(22, 51);
        private static readonly Vector2Int TrialCinnaCell = new Vector2Int(8, 1);

        private Scene runnerScene;
        private Scene hostScene;
        private Scene worldScene;
        private DataDrivenWorldLoader ownedLoader;
        private AudioListener[] originalListeners;
        private AudioListener[] ownedListeners;

        [UnitySetUp]
        public IEnumerator LoadWorld()
        {
            ownedLoader = null;
            ownedListeners = null;
            worldScene = default;
            runnerScene = SceneManager.GetActiveScene();
            originalListeners = Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include);
            // A retained empty scene lets teardown unload SampleScene even when it was the last world scene.
            hostScene = SceneManager.CreateScene("AuthoredWorldTests Host");
            SceneManager.SetActiveScene(hostScene);
            yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Additive);
            worldScene = SceneManager.GetSceneAt(SceneManager.sceneCount - 1);
            Assert.That(worldScene.name, Is.EqualTo("SampleScene"));
            Assert.That(worldScene, Is.Not.EqualTo(runnerScene));
            SceneManager.SetActiveScene(worldScene);
            yield return null;

            StartupMenu menu = Object.FindAnyObjectByType<StartupMenu>();
            if (menu != null)
            {
                float deadline = Time.realtimeSinceStartup + 5f;
                while (!StartupMenu.IsVisible && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(StartupMenu.IsVisible, Is.True, "Startup must finish before establishing the authored test world.");
            }

            ownedLoader = worldScene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<DataDrivenWorldLoader>(true)).Single();
            ownedListeners = worldScene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<AudioListener>(true)).ToArray();
            Assert.That(DataDrivenWorldLoader.Instance, Is.SameAs(ownedLoader));
            Assert.That(Object.FindObjectsByType<DataDrivenWorldLoader>(FindObjectsInactive.Include).Length, Is.EqualTo(1),
                "Each test must own exactly one world loader.");
            Assert.That(ownedListeners.Length, Is.EqualTo(1), "SampleScene should own one audio listener.");

            // Quest and world assertions exercise a fresh authored game, independently of menu backdrop state.
            GameEventManager.Instance.ReloadDefinitions();
            CompleteOnboarding(GameEventManager.Instance.Runtime);
            ownedLoader.ResetToStartingState();
            Assert.That(ownedLoader.LoadedMap.id, Is.EqualTo("westForest"));
        }

        [UnityTearDown]
        public IEnumerator UnloadWorld()
        {
            if (hostScene.IsValid() && hostScene.isLoaded) SceneManager.SetActiveScene(hostScene);
            if (worldScene.IsValid() && worldScene.isLoaded) yield return SceneManager.UnloadSceneAsync(worldScene);
            Assert.That(ownedLoader == null, Is.True, "The fixture-owned loader must be destroyed on unload.");
            Assert.That(DataDrivenWorldLoader.Instance == null, Is.True, "No world singleton may leak into the next test.");
            foreach (AudioListener listener in ownedListeners ?? new AudioListener[0])
                Assert.That(listener == null, Is.True, "The fixture-owned audio listener must be destroyed on unload.");
            CollectionAssert.AreEquivalent(originalListeners,
                Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include), "World unload must restore the original listeners.");

            Assert.That(runnerScene.IsValid() && runnerScene.isLoaded, Is.True, "Additive loading must retain the runner scene.");
            SceneManager.SetActiveScene(runnerScene);
            Assert.That(SceneManager.sceneCount, Is.GreaterThan(1), "Host cleanup must retain another ordinary scene.");
            if (hostScene.IsValid() && hostScene.isLoaded) yield return SceneManager.UnloadSceneAsync(hostScene);
        }

        // The startup menu pauses time (and loads a backdrop save) until the player presses Start. Tests that rely on
        // timed movement close it the way Start would, restoring the time scale.
        private static IEnumerator DismissStartupMenu()
        {
            for (StartupMenu menu = Object.FindAnyObjectByType<StartupMenu>(); menu != null; menu = Object.FindAnyObjectByType<StartupMenu>())
                Object.DestroyImmediate(menu.gameObject);
            yield return null;
            Assert.That(Time.timeScale, Is.GreaterThan(0f), "Gameplay time should run once the startup menu is closed.");
        }

        [UnityTest]
        public IEnumerator KatrinaAuthoredPickupsCreditBeforeMonitorAndMapTransition()
        {
            yield return DismissStartupMenu();
            GameEventRuntime events = GameEventManager.Instance.Runtime;
            events.ResetState();
            CompleteOnboarding(events);
            WorldStateService state = WorldStateService.Instance;
            state.ResetAllState();
            DataDrivenWorldLoader loader = DataDrivenWorldLoader.Instance;
            NpcCatalogAsset catalog = (NpcCatalogAsset)typeof(DataDrivenWorldLoader)
                .GetField("npcCatalog", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(loader);
            const string quest = "katrina_something_borrowed";
            events.StartQuest(quest);
            string[] maps = { "B04.Ground", "campus", "westForest" };
            string[] anchors = { "library", "garden", "stream" };
            int recoveryDialogues = 0;
            events.DialogueStarted += session =>
            {
                if (anchors.Any(anchor => session.Definition.id == "katrina_" + anchor + "_item_recovered"))
                    recoveryDialogues++;
            };

            // Deliberately do not yield between collection and changing maps: the polling monitor never gets a turn.
            for (int i = 0; i < maps.Length; i++)
            {
                loader.Load(loader.GetArea(maps[i]), catalog);
                Assert.That(state.TryGetObject("katrina.item." + anchors[i], out WorldObjectSnapshot item), Is.True,
                    "The authored marker must register through its real prefab and OnEnable.");
                string flag = "inventory.keyitem.katrina-" + anchors[i] + "-anchor";
                Assert.That(state.TryRevealObject(item.id, item.cell, 4), Is.True);
                Assert.That(events.GetFlag(flag), Is.False, "Reveal alone must not credit a pickup.");
                Assert.That(state.TryCollectObject(item.id, item.cell, 1), Is.True);
                Assert.That(events.GetFlag(flag), Is.True, "Credit must be synchronous with a valid pickup.");
                Assert.That(events.ActiveDialogue?.Definition.id, Is.EqualTo("katrina_" + anchors[i] + "_item_recovered"));
                Assert.That(state.TryCollectObject(item.id, item.cell, 1), Is.False, "A collected anchor cannot count twice.");
                events.ActiveDialogue.End();
                loader.Load(loader.GetArea(maps[(i + 1) % maps.Length]), catalog);
                Assert.That(events.GetFlag(flag), Is.True, "Leaving before a monitor tick must retain credit.");
                QuestPuzzleMonitor monitor = Object.FindAnyObjectByType<QuestPuzzleMonitor>();
                Assert.That(monitor, Is.Not.Null);
                Assert.That(monitor.EvaluatePuzzles(), Is.Zero, "The fallback monitor must not duplicate pickup narration.");
                Assert.That(events.ActiveDialogue, Is.Null);
            }
            Assert.That(recoveryDialogues, Is.EqualTo(3));
            Assert.That(events.GetFlag("inventory.keyitem.katrina-keepsake"), Is.True);
            DialogueSession report = events.StartDialogue(quest + "_dialogue");
            Assert.That(report.SelectChoice("report"), Is.True);
            Assert.That(events.GetQuestStatus(quest), Is.EqualTo(QuestStatus.Completed));
            report.End();
            string saved = events.ExportStateJson(false);
            events.ResetState();
            Assert.That(events.ImportStateJson(saved), Is.True);
            Assert.That(events.GetFlag("inventory.keyitem.katrina-library-anchor"), Is.True);
            Assert.That(events.GetFlag("inventory.keyitem.katrina-keepsake"), Is.True);
        }

        [UnityTest]
        public IEnumerator AuthoredDeepForestRouteAndLegacyHallSaveBothReturnSafely()
        {
            yield return DismissStartupMenu();
            GameEventRuntime events = GameEventManager.Instance.Runtime;
            events.ResetState();
            CompleteOnboarding(events);
            WorldStateService.Instance.ResetAllState();
            DataDrivenWorldLoader loader = DataDrivenWorldLoader.Instance;
            NpcCatalogAsset catalog = (NpcCatalogAsset)typeof(DataDrivenWorldLoader)
                .GetField("npcCatalog", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(loader);
            loader.Load(loader.GetArea("westForest"), catalog);
            events.StartQuest("orio_patch_notes");
            yield return null;
            Assert.That(Object.FindObjectsByType<GridPortal>(FindObjectsInactive.Exclude)
                .Any(portal => portal.PortalId == "P.F04.ORIO.HALL"), Is.False, "The obsolete entrance must stay retired.");
            GridPortal entrance = Object.FindObjectsByType<GridPortal>(FindObjectsInactive.Exclude)
                .Single(portal => portal.PortalId == "P.F01");
            Assert.That(loader.SwitchArea(entrance.Content, Vector2Int.zero), Is.True);
            Assert.That(loader.LoadedMap.id, Is.EqualTo(OrioRuneDiscPuzzle.PuzzleMapId));
            Assert.That(WorldStateService.Instance.TryGetObject(OrioRuneDiscPuzzle.AnchorId, out _), Is.True);
            GridPortal deepExit = Object.FindObjectsByType<GridPortal>(FindObjectsInactive.Exclude)
                .Single(portal => portal.PortalId == "P.DF00");
            Assert.That(loader.SwitchArea(deepExit.Content, Vector2Int.zero), Is.True);
            Assert.That(loader.LoadedMap.id, Is.EqualTo("westForest"));

            string legacyHall = "{\"mapId\":\"orioDaisRoom\",\"playerX\":6,\"playerY\":2}";
            Assert.That(loader.ValidateStateJson(legacyHall), Is.True);
            Assert.That(loader.PrepareRestoreStateJson(legacyHall), Is.True);
            Assert.That(loader.RestoreStateJson(legacyHall), Is.True);
            GridMover player = Object.FindAnyObjectByType<GridPlayerInput>().GetComponent<GridMover>();
            Assert.That(player.CurrentCell, Is.EqualTo(new Vector2Int(6, 2)));
            GridPortal legacyExit = Object.FindObjectsByType<GridPortal>(FindObjectsInactive.Exclude)
                .Single(portal => portal.PortalId == "P.ORIO.HALL.F04");
            Assert.That(loader.SwitchArea(legacyExit.Content, Vector2Int.zero), Is.True);
            Assert.That(loader.LoadedMap.id, Is.EqualTo("westForest"));
            Assert.That(Object.FindObjectsByType<GridPortal>(FindObjectsInactive.Exclude)
                .Any(portal => portal.PortalId == "P.F04.ORIO.HALL"), Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator CompletedGyroscopeStopsWhenItsLiveCircuitBreaksAndRestartsWhenReconnected()
        {
            yield return DismissStartupMenu();
            DataDrivenWorldLoader loader = DataDrivenWorldLoader.Instance;
            NpcCatalogAsset catalog = (NpcCatalogAsset)typeof(DataDrivenWorldLoader)
                .GetField("npcCatalog", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(loader);
            GameEventRuntime events = GameEventManager.Instance.Runtime;
            events.StartQuest("stella_first_light");
            events.CompleteQuest("stella_first_light");
            events.StartQuest("stella_string_them_together");
            loader.Load(loader.GetArea("B02.Ground"), catalog);
            yield return null;
            WorldStateService state = WorldStateService.Instance;
            List<string> nodes = new List<string> { "circuit.stella.link" };
            for (int i = 1; i <= 5; i++)
            {
                string id = "stella.string-together.relic." + i;
                nodes.Add(id);
                state.SetProperty(id, value => value.powered = true);
            }
            for (int i = 1; i <= 10; i++) nodes.Add("stella.circuit.relay." + i);
            // Use only legal authored edges; no teleports or direct conductor-power mutation.
            for (int i = 0; i < nodes.Count; i++)
                for (int j = i + 1; j < nodes.Count; j++)
                    if (StellaStringThemTogetherPuzzle.CanTie(nodes[i], nodes[j])) state.AddConnection(nodes[i], nodes[j]);
            Assert.That(state.AreInSameNetwork(nodes.ToArray()), Is.True);
            Assert.That(state.TryGetObject(nodes[0], out WorldObjectSnapshot item) && item.powered, Is.True);
            events.CompleteQuest("stella_string_them_together");
            yield return null;
            GameObject gyroscope = GameObject.Find("magical_gyroscope");
            Assert.That(gyroscope, Is.Not.Null);
            Transform[] movingParts = gyroscope.GetComponentsInChildren<Transform>(true).Skip(1).ToArray();
            Assert.That(movingParts.Length, Is.GreaterThan(0));
            Quaternion[] before = movingParts.Select(part => part.localRotation).ToArray();
            yield return new WaitForSeconds(0.17f);
            Assert.That(movingParts.Where((part, i) => Quaternion.Angle(before[i], part.localRotation) > 0.01f).Any(), Is.True,
                "The authored gyroscope's animated children must move while its circuit is live.");
            List<string> feeds = nodes.Skip(1).Where(id => state.IsConnected(nodes[0], id)).ToList();
            Assert.That(feeds.Count, Is.GreaterThan(0));
            foreach (string feed in feeds) state.RemoveConnection(nodes[0], feed);
            yield return null; // Stop/rewind is applied once before capturing the stationary pose.
            Assert.That(state.TryGetObject(nodes[0], out item) && item.powered, Is.False);
            before = movingParts.Select(part => part.localRotation).ToArray();
            yield return new WaitForSeconds(0.17f);
            Assert.That(movingParts.Where((part, i) => Quaternion.Angle(before[i], part.localRotation) > 0.01f).Any(), Is.False,
                "Completed quest history must not keep the actual animation spinning without power.");
            Assert.That(events.GetQuestStatus("stella_string_them_together"), Is.EqualTo(QuestStatus.Completed));
            Assert.That(state.AddConnection(nodes[0], feeds[0]), Is.True);
            yield return null;
            before = movingParts.Select(part => part.localRotation).ToArray();
            yield return new WaitForSeconds(0.17f);
            Assert.That(movingParts.Where((part, i) => Quaternion.Angle(before[i], part.localRotation) > 0.01f).Any(), Is.True,
                "Reconnecting live power resumes the authored animation.");
        }

        [UnityTest]
        public IEnumerator LegacyKatrinaRecessSaveMigratesToCampus()
        {
            yield return DismissStartupMenu();
            DataDrivenWorldLoader loader = DataDrivenWorldLoader.Instance;
            string legacyRecess = "{\"mapId\":\"C04.Recess\",\"playerX\":7,\"playerY\":1}";
            Assert.That(loader.ValidateStateJson(legacyRecess), Is.True);
            Assert.That(loader.PrepareRestoreStateJson(legacyRecess), Is.True);
            Assert.That(loader.RestoreStateJson(legacyRecess), Is.True);
            Assert.That(loader.LoadedMap.id, Is.EqualTo("campus"));
            Assert.That(loader.CurrentArea.definition.id, Is.EqualTo("campus"));
            GridMover player = Object.FindAnyObjectByType<GridPlayerInput>().GetComponent<GridMover>();
            Assert.That(player.CurrentCell, Is.Not.EqualTo(new Vector2Int(7, 1)), "The retired Recess coordinates must not be restored on campus.");
            Assert.That(player.CurrentCell, Is.Not.EqualTo(new Vector2Int(loader.LoadedMap.spawn.x, loader.LoadedMap.spawn.y)),
                "The blocked authored campus spawn must fall back to the nearest safe cell.");
            Assert.That(player.World.IsCellBlocked(player.CurrentCell), Is.False, "The restored player occupies walkable ground.");
            Assert.That(player.World.GetPortalAt(player.CurrentCell), Is.Null, "Legacy escape must not immediately trigger another portal.");
            GridPortal campusRoute = Object.FindObjectsByType<GridPortal>(FindObjectsInactive.Exclude).Single(portal => portal.PortalId == "P.C02");
            Assert.That(CanReach(player.World, player.CurrentCell, campusRoute.FirstCell), Is.True, "The legacy arrival must reach the ordinary campus routes.");
            Assert.That(Object.FindObjectsByType<GridPortal>(FindObjectsInactive.Exclude)
                .Any(portal => portal.Content?.destination?.area?.definition?.id == "C04.Recess"), Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator SampleWorldLoadsRoutesRestoresAndRefreshesQuestGatedNpcs()
        {

            DataDrivenWorldLoader loader = DataDrivenWorldLoader.Instance;
            Assert.That(loader, Is.Not.Null, "SampleScene should have a wired world loader.");
            Assert.That(loader.LoadedMap.id, Is.EqualTo("westForest"));
            GridMover player = Object.FindAnyObjectByType<GridMover>();
            Assert.That(player, Is.Not.Null, "SampleScene should have a player for portal and save checks.");

            GridPortal startingReturn = Object.FindObjectsByType<GridPortal>(FindObjectsInactive.Exclude)
                .Single(portal => portal.Content != null && portal.Content.id == "P.F00");
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
                new { areaId = "B02.Ground", portalId = "P.B02.00" },
                new { areaId = "B03.Ground", portalId = "P.B03.00" },
                new { areaId = "B04.Ground", portalId = "P.B04.00" }
            };
            foreach (var expected in migratedReturns)
            {
                WorldAreaAsset interior = availableAreas.Single(area => area.definition.id == expected.areaId);
                loader.Load(interior, catalog);
                yield return null;
                GridPortal exit = Object.FindObjectsByType<GridPortal>(FindObjectsInactive.Exclude)
                    .First(portal => portal.Content != null && portal.Content.id == expected.portalId);
                Assert.That(loader.SwitchArea(exit.Content, Vector2Int.zero), Is.True,
                    expected.areaId + " return route should accept its corrected campus arrival cell.");
                Assert.That(loader.CurrentArea.definition.id, Is.EqualTo("campus"));
            }

            WorldAreaAsset[] allAreas = new[] { startingArea }.Concat(availableAreas).Distinct().ToArray();
            int activeRouteCount = 0;
            foreach (WorldAreaAsset area in allAreas)
            {
                PortalContentAsset[] activeRoutes = area.areaPrefab.GetComponentsInChildren<GridPortal>(true)
                    // A blocked portal with no destination is a door that only answers with a notice, such as campus's
                    // forgotten west door and the north gate's dog.
                    .Select(portal => portal.Content)
                    .Where(content => content != null && !content.disabled && !(content.blocked && content.destination == null)).ToArray();
                foreach (PortalContentAsset route in activeRoutes)
                {
                    Assert.That(route.destination, Is.Not.Null,
                        "Active route " + area.definition.id + "/" + route.id + " needs a direct destination.");
                    Assert.That(route.destination.destination, Is.SameAs(route),
                        "Active route " + area.definition.id + "/" + route.id + " needs an exact return portal.");
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
            Assert.That(activeRouteCount, Is.EqualTo(14), "All active forest, campus, and building portal edges should be traversable.");

            WorldAreaAsset campus = allAreas.Single(area => area.definition.id == "campus");
            WorldAreaAsset iceTrial = allAreas.Single(area => area.definition.id == "C03.IceTrial");
            loader.Load(campus, catalog);
            yield return null;
            DynamicNpc orientation = Object.FindObjectsByType<DynamicNpc>(FindObjectsInactive.Include)
                .First(npc => npc.Definition != null && npc.Definition.id == "cinna_cirra_orientation");
            GameEventRuntime events = GameEventManager.Instance.Runtime;
            if (events.GetQuestStatus("cinna_out_of_alignment") == QuestStatus.NotStarted)
                Assert.That(events.StartQuest("cinna_out_of_alignment"), Is.True);
            Assert.That(events.CompleteQuest("cinna_out_of_alignment"), Is.True);
            yield return null;
            Assert.That(orientation.gameObject.activeSelf, Is.False,
                "The orientation NPC should hide after its quest completes.");

            // The trial Cinna is spawned by the ice trial room from their catalog record.
            loader.Load(iceTrial, catalog);
            yield return null;
            DynamicNpc trial = Object.FindObjectsByType<DynamicNpc>(FindObjectsInactive.Include)
                .First(npc => npc.Definition != null && npc.Definition.id == "cinna_cirra_trial");
            Assert.That(trial.gameObject.activeSelf, Is.True,
                "The trial NPC should appear in the ice trial once Out of Alignment is complete.");
            Assert.That(Object.FindAnyObjectByType<GridWorld>().WorldToCell(trial.transform.position), Is.EqualTo(new Vector2Int(8, 1)));

            loader.Load(availableAreas.Single(area => area.definition.id == "westForest"), catalog);
            yield return null;
            loader.Load(iceTrial, catalog);
            yield return null;
            Assert.That(trial.gameObject.activeSelf, Is.True,
                "Quest-gated visibility should persist when revisiting an area.");
        }

        [UnityTest]
        public IEnumerator IceTrialPortalSlidesFollowASolutionRouteAndCompleteColdFeet()
        {
            yield return DismissStartupMenu();
            // The startup menu may already have loaded the latest save as its backdrop; start from a clean world.
            GameEventManager.Instance.Runtime.ResetState();
            CompleteOnboarding(GameEventManager.Instance.Runtime);
            WorldStateService.Instance.ResetAllState();

            DataDrivenWorldLoader loader = DataDrivenWorldLoader.Instance;
            FieldInfo catalogField = typeof(DataDrivenWorldLoader).GetField("npcCatalog", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo areasField = typeof(DataDrivenWorldLoader).GetField("availableAreas", BindingFlags.Instance | BindingFlags.NonPublic);
            NpcCatalogAsset catalog = (NpcCatalogAsset)catalogField.GetValue(loader);
            WorldAreaAsset[] availableAreas = (WorldAreaAsset[])areasField.GetValue(loader);
            loader.Load(availableAreas.Single(area => area.definition.id == "campus"), catalog);
            yield return null;

            GridPortal entrance = Object.FindObjectsByType<GridPortal>(FindObjectsInactive.Exclude)
                .Single(portal => portal.Content != null && portal.Content.id == CinnaIceTrialEntrance.PortalId);
              Assert.That(entrance.World.WorldToCell(entrance.transform.position), Is.EqualTo(CinnaIceTrialEntrance.EntranceCell));
            // Out of Alignment delivers the relic to the pedestal; only then does it take Glaciate.
            GameEventManager.Instance.Runtime.SetFlag(SpellCaster.VisDeliveredFlagPrefix + CinnaAlignmentRelic.RelicId, true);
            Assert.That(loader.SwitchArea(entrance.Content, Vector2Int.zero), Is.True, "The campus portal should enter the ice trial.");
            Assert.That(loader.CurrentArea.definition.id, Is.EqualTo(CinnaIceTrialRoom.MapId));
            yield return null;

            GameEventRuntime events = GameEventManager.Instance.Runtime;
            if (events.GetQuestStatus("cinna_out_of_alignment") == QuestStatus.NotStarted) events.StartQuest("cinna_out_of_alignment");
            if (events.GetQuestStatus("cinna_out_of_alignment") == QuestStatus.Active) events.CompleteQuest("cinna_out_of_alignment");
            if (events.GetQuestStatus(CinnaIceTrialRoom.QuestId) == QuestStatus.NotStarted) events.StartQuest(CinnaIceTrialRoom.QuestId);
            Assert.That(events.GetQuestStatus(CinnaIceTrialRoom.QuestId), Is.EqualTo(QuestStatus.Active));
            events.SetFlag("spell.lesson.Glaciate", true);

            GridMover player = Object.FindAnyObjectByType<GridPlayerInput>().GetComponent<GridMover>();
            GridWorld world = player.World;
            Vector2Int entranceCell = new Vector2Int(5, 2);
            Assert.That(world.IsCellBlocked(entranceCell + Vector2Int.up), Is.True, "Open water blocks the pool before it freezes.");
            Assert.That(player.GetComponent<SpellCaster>().Cast(SpellId.Glaciate, CinnaIceTrialRoom.RelicId, Vector2Int.up), Is.True);
            Assert.That(world.IsCellBlocked(entranceCell + Vector2Int.up), Is.False, "Freezing the relic drains the water blocker.");
            Assert.That(world.IsIce(entranceCell + Vector2Int.up), Is.True);
            Assert.That(player.TryTeleportTo(world, entranceCell), Is.True);

            // One of the shortest solutions: every snow patch and pillar, all nine switches, then the seal.
            Vector2Int[] route =
            {
                Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left, Vector2Int.up, Vector2Int.right,
                Vector2Int.right, Vector2Int.up, Vector2Int.right, Vector2Int.up, Vector2Int.up, Vector2Int.left,
                Vector2Int.down, Vector2Int.left, Vector2Int.right, Vector2Int.down, Vector2Int.right
            };
            foreach (Vector2Int direction in route)
            {
                Vector2Int from = player.CurrentCell;
                Assert.That(player.TryMove(direction), Is.True, $"Slide {direction} from {from} was refused.");
                float deadline = Time.realtimeSinceStartup + 5f;
                while (player.IsMoving && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(player.IsMoving || player.IsSliding, Is.False, $"Slide {direction} from {from} did not stop.");
            }
            Assert.That(player.CurrentCell, Is.EqualTo(new Vector2Int(11, 8)), "The route ends on the seal.");

            WorldStateService state = WorldStateService.Instance;
            foreach (WorldObjectSnapshot item in state.SnapshotObjects())
                if (item.tags.Contains(CinnaIceTrialRoom.SwitchTag) || item.tags.Contains(CinnaIceTrialRoom.FinalTag))
                    Assert.That(item.powered, Is.True, item.id + " should be lit.");
            float wait = Time.realtimeSinceStartup + 2f;
            while (events.GetQuestStatus(CinnaIceTrialRoom.QuestId) != QuestStatus.Completed && Time.realtimeSinceStartup < wait) yield return null;
            Assert.That(events.GetQuestStatus(CinnaIceTrialRoom.QuestId), Is.EqualTo(QuestStatus.Completed));
            Assert.That(events.GetFlag("spell.unlocked.Glaciate"), Is.True, "Opening the seal teaches Glaciate.");

            // A second Glaciate thaws the pool; anyone standing in it steps out to the entrance first.
            events.ActiveDialogue?.End(); // Cinna's success line would block casting.
            yield return null; // Dialogue closing consumes input for the rest of its frame.
            Assert.That(OverlayInputGate.IsBlocked, Is.False, "The closed dialogue must release its input ownership on the next frame.");
            Vector2Int poolSwitch = new Vector2Int(3, 3);
            Assert.That(player.TryTeleportTo(world, poolSwitch), Is.True);
            Assert.That(player.GetComponent<SpellCaster>().Cast(SpellId.Glaciate, CinnaIceTrialRoom.RelicId, Vector2Int.up), Is.True);
            yield return null;
            Assert.That(state.TryGetObject(CinnaIceTrialRoom.RelicId, out WorldObjectSnapshot thawed) && thawed.frozen, Is.False);
            Assert.That(player.CurrentCell, Is.EqualTo(entranceCell), "Thawing moves the player out of the pool.");
            Assert.That(world.IsCellBlocked(entranceCell + Vector2Int.up), Is.True, "The water blocks the pool again.");
            Assert.That(world.IsIce(entranceCell + Vector2Int.up), Is.False);
        }

        [UnityTest]
        public IEnumerator VisCarriesTheRelicToTheTrialAndCinnaGoesAheadToMeetThePlayer()
        {
            yield return DismissStartupMenu();

            DataDrivenWorldLoader loader = DataDrivenWorldLoader.Instance;
            FieldInfo catalogField = typeof(DataDrivenWorldLoader).GetField("npcCatalog", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo areasField = typeof(DataDrivenWorldLoader).GetField("availableAreas", BindingFlags.Instance | BindingFlags.NonPublic);
            NpcCatalogAsset catalog = (NpcCatalogAsset)catalogField.GetValue(loader);
            WorldAreaAsset campus = ((WorldAreaAsset[])areasField.GetValue(loader)).Single(area => area.definition.id == "campus");
            GameEventRuntime events = GameEventManager.Instance.Runtime;
            events.ResetState();
            CompleteOnboarding(events);
            WorldStateService.Instance.ResetAllState();
            loader.Load(campus, catalog);
            yield return null;
            events.StartQuest("cinna_out_of_alignment");
            events.SetFlag("spell.unlocked.Vis", true);

            GridMover player = Object.FindAnyObjectByType<GridPlayerInput>().GetComponent<GridMover>();
            SpellCaster caster = player.GetComponent<SpellCaster>();
            GridWorld world = player.World;
            Vector2Int start = new Vector2Int(23, 53);
            Assert.That(WorldStateService.Instance.TryGetObject(CinnaAlignmentRelic.RelicId, out WorldObjectSnapshot relic) && relic.cell == start, Is.True,
                "The relic rests beside Cinna on the terrace.");
            Assert.That(world.IsCellBlocked(start), Is.True, "The resting relic blocks its tile.");
            Assert.That(player.TryTeleportTo(world, start + Vector2Int.up), Is.True, "The tile beside the relic is open ground.");
            Assert.That(caster.Cast(SpellId.Vis, CinnaAlignmentRelic.RelicId, Vector2Int.down), Is.True);
            yield return null;

            GridPortal entrance = Object.FindObjectsByType<GridPortal>(FindObjectsInactive.Exclude)
                .Single(portal => portal.Content != null && portal.Content.id == CinnaIceTrialEntrance.PortalId);
            Assert.That(loader.SwitchArea(entrance.Content, Vector2Int.zero), Is.True);
            yield return null;
            Assert.That(caster.HeldRelic, Is.EqualTo(CinnaAlignmentRelic.RelicId), "The relic is carried into the trial.");
            Assert.That(caster.TryPlaceHeldRelic(new Vector2Int(3, 1)), Is.True, "Vis sets the relic on the pedestal.");
            Assert.That(events.GetFlag(SpellCaster.VisDeliveredFlagPrefix + CinnaAlignmentRelic.RelicId), Is.True);
            Assert.That(WorldStateService.Instance.TryGetObject(CinnaIceTrialRoom.RelicId, out WorldObjectSnapshot frost) && frost.spellCompatible, Is.True,
                "The delivered relic takes Glaciate.");
            DynamicNpc hiddenTrialCinna = Object.FindObjectsByType<DynamicNpc>(FindObjectsInactive.Include)
                .First(npc => npc.Definition != null && npc.Definition.id == "cinna_cirra_trial");
            Assert.That(hiddenTrialCinna.gameObject.activeSelf, Is.False, "Cinna has not reached the trial yet.");
            Assert.That(world.IsCellBlocked(TrialCinnaCell), Is.False, "A hidden Cinna leaves no blocker in the trial lobby.");

            loader.Load(campus, catalog);
            yield return null;
            Assert.That(WorldStateService.Instance.TryGetObject(CinnaAlignmentRelic.RelicId, out _), Is.False, "The relic has left campus.");
            DynamicNpc terraceCinna = Object.FindObjectsByType<DynamicNpc>(FindObjectsInactive.Include)
                .First(npc => npc.Definition != null && npc.Definition.id == "cinna_cirra_orientation");
            Assert.That(terraceCinna.gameObject.activeSelf, Is.True);
            Assert.That(world.IsCellBlocked(TerraceCinnaCell), Is.True, "Cinna blocks their terrace tile while they stand on it.");
            Vector2Int reportedFrom = player.CurrentCell;
            DialogueSession report = events.StartDialogue("cinna_out_of_alignment_dialogue");
            Assert.That(report.SelectChoice("report"), Is.True);
            for (int step = 0; step < 64 && !report.IsCompleted; step++) Assert.That(report.Continue(), Is.True);
            Assert.That(report.IsCompleted, Is.True);
            yield return null;
            Assert.That(CinnaTrialDeparture.InProgress, Is.True, "Cinna leaving for the trial fades the screen.");
            float deadline = Time.realtimeSinceStartup + 5f;
            while (CinnaTrialDeparture.InProgress && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(CinnaTrialDeparture.InProgress, Is.False);
            Assert.That(events.GetQuestStatus("cinna_out_of_alignment"), Is.EqualTo(QuestStatus.Completed));
            Assert.That(loader.CurrentArea.definition.id, Is.EqualTo("campus"), "Only Cinna goes; the player stays on campus.");
            Assert.That(player.CurrentCell, Is.EqualTo(reportedFrom));
            Assert.That(terraceCinna.gameObject.activeSelf, Is.False, "Cinna has left the terrace.");
            Assert.That(world.IsCellBlocked(TerraceCinnaCell), Is.False, "Cinna's terrace tile is free once they leave.");

            GridPortal trialPortal = Object.FindObjectsByType<GridPortal>(FindObjectsInactive.Exclude)
                .Single(portal => portal.Content != null && portal.Content.id == CinnaIceTrialEntrance.PortalId);
            Assert.That(loader.SwitchArea(trialPortal.Content, Vector2Int.zero), Is.True);
            yield return null;
            DynamicNpc trialCinna = Object.FindObjectsByType<DynamicNpc>(FindObjectsInactive.Include)
                .First(npc => npc.Definition != null && npc.Definition.id == "cinna_cirra_trial");
            Assert.That(trialCinna.gameObject.activeSelf, Is.True, "Cinna is waiting in the trial lobby.");
            Assert.That(world.IsCellBlocked(TrialCinnaCell), Is.True, "Cinna blocks their tile in the trial lobby.");
        }

        [UnityTest]
        public IEnumerator PlayerCanWalkThroughWestForestGateToCampus()
        {
            DataDrivenWorldLoader loader = DataDrivenWorldLoader.Instance;
            Assert.That(loader, Is.Not.Null);
            FieldInfo catalogField = typeof(DataDrivenWorldLoader).GetField("npcCatalog", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo areasField = typeof(DataDrivenWorldLoader).GetField("availableAreas", BindingFlags.Instance | BindingFlags.NonPublic);
            NpcCatalogAsset catalog = (NpcCatalogAsset)catalogField.GetValue(loader);
            WorldAreaAsset[] availableAreas = (WorldAreaAsset[])areasField.GetValue(loader);
            loader.Load(availableAreas.Single(area => area.definition.id == "westForest"), catalog);
            yield return null;

            GridPortal gate = Object.FindObjectsByType<GridPortal>(FindObjectsInactive.Exclude)
                .Single(portal => portal.Content != null && portal.Content.id == "P.F00");
            Assert.That(gate.FootprintSize, Is.EqualTo(new Vector2Int(3, 1)));
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
            Assert.That(player.CurrentCell, Is.EqualTo(new Vector2Int(26, 3)));

            for (int offset = 0; offset < 3; offset++)
            {
                GridPortal campusGate = Object.FindObjectsByType<GridPortal>(FindObjectsInactive.Exclude)
                    .Single(portal => portal.Content != null && portal.Content.id == "P.C00");
                Vector2Int entry = new Vector2Int(25 + offset, 3);
                Assert.That(campusGate.CentreCell, Is.EqualTo(new Vector2Int(26, 3)));
                Assert.That(campusGate.FootprintSize, Is.EqualTo(new Vector2Int(3, 1)));
                Assert.That(campusGate.World.GetPortalAt(entry), Is.SameAs(campusGate),
                    "Every campus gate tile must belong to the same reciprocal portal.");
                Assert.That(campusGate.World.IsCellBlocked(entry), Is.False);
                Assert.That(CanReach(campusGate.World, player.CurrentCell, entry), Is.True,
                    "Every campus gate tile must be reachable from the forest arrival.");
                Assert.That(player.TryTeleportTo(campusGate.World, entry), Is.True);
                Assert.That(campusGate.TryEnter(player), Is.True);
                Assert.That(loader.CurrentArea.definition.id, Is.EqualTo("westForest"));
                Assert.That(player.CurrentCell, Is.EqualTo(new Vector2Int(16 + offset, 48)));
                GridPortal forestReturn = Object.FindObjectsByType<GridPortal>(FindObjectsInactive.Exclude)
                    .Single(portal => portal.Content != null && portal.Content.id == "P.F00");
                Assert.That(forestReturn.Content.destination, Is.SameAs(campusGate.Content));
                Assert.That(forestReturn.CentreCell, Is.EqualTo(new Vector2Int(17, 48)));
                Assert.That(player.TryTeleportTo(forestReturn.World, new Vector2Int(16 + offset, 48)), Is.True);
                Assert.That(forestReturn.TryEnter(player), Is.True);
                Assert.That(loader.CurrentArea.definition.id, Is.EqualTo("campus"));
                Assert.That(player.CurrentCell, Is.EqualTo(new Vector2Int(26, 3)));
            }

            GridPortal b02Entrance = Object.FindObjectsByType<GridPortal>(FindObjectsInactive.Exclude)
                .Single(portal => portal.Content != null && portal.Content.id == "P.C02");
            Assert.That(CanReach(gate.World, player.CurrentCell, b02Entrance.FirstCell), Is.True,
                "The campus route from the forest arrival to B02 must be walkable.");
            Assert.That(player.TryTeleportTo(b02Entrance.World, b02Entrance.FirstCell), Is.True);
            Assert.That(b02Entrance.TryEnter(player), Is.True, "The B02 entrance must transfer the player.");
            Assert.That(loader.CurrentArea.definition.id, Is.EqualTo("B02.Ground"));
            Assert.That(player.CurrentCell, Is.EqualTo(new Vector2Int(19, 2)));
            GridPortal b02Exit = Object.FindObjectsByType<GridPortal>(FindObjectsInactive.Exclude)
                .Single(portal => portal.Content != null && portal.Content.id == "P.B02.00");
            Assert.That(b02Exit.CentreCell, Is.EqualTo(new Vector2Int(19, 2)));
            Assert.That(b02Exit.FootprintSize, Is.EqualTo(new Vector2Int(3, 1)));
            Assert.That(b02Exit.Content.destination, Is.SameAs(b02Entrance.Content));
            DynamicNpc stella = Object.FindObjectsByType<DynamicNpc>(FindObjectsInactive.Exclude)
                .Single(npc => npc.Id == "stella");
            Assert.That(b02Exit.World.WorldToCell(stella.transform.position), Is.EqualTo(new Vector2Int(19, 10)),
                "Stella's authored prefab position, rather than the legacy catalog coordinates, defines her interaction side.");
            Vector2Int[] interactionCells = new[]
                { new Vector2Int(19, 9), new Vector2Int(18, 10), new Vector2Int(20, 10), new Vector2Int(19, 11) }
                .Where(cell => !b02Exit.World.IsCellBlocked(cell) && CanReach(b02Exit.World, player.CurrentCell, cell) &&
                    Vector3.Distance(b02Exit.World.CellToWorld(cell), stella.transform.position) <= 2.01f).ToArray();
            Assert.That(interactionCells, Is.Not.Empty, "The B02 landing must reach a walkable cell beside Stella within talk range.");
            Assert.That(player.TryTeleportTo(b02Exit.World, interactionCells[0]), Is.True);
            Assert.That(Vector3.Distance(player.transform.position, stella.transform.position), Is.LessThanOrEqualTo(2.01f));
            Assert.That(CanReach(b02Exit.World, player.CurrentCell, b02Exit.FirstCell), Is.True,
                "Stella's interaction side must reach the B02 return portal.");
            Assert.That(player.TryTeleportTo(b02Exit.World, b02Exit.FirstCell), Is.True);
            Assert.That(b02Exit.TryEnter(player), Is.True, "The B02 exit must return the player to campus.");
            Assert.That(loader.CurrentArea.definition.id, Is.EqualTo("campus"));
            Assert.That(player.CurrentCell, Is.EqualTo(new Vector2Int(18, 18)));
        }

        private static void CompleteOnboarding(GameEventRuntime runtime)
        {
            // Traversal tests start after the independently tested introductory story has finished.
            runtime.SetFlag(LateToOrientationScene.MetFlag, true);
            runtime.SetFlag(LayOfTheLandScene.CalledFlag, true);
            runtime.SetFlag(LayOfTheLandScene.MetFlag, true);
            runtime.SetFlag(SettlingInScene.BannMetFlag, true);
            runtime.SetFlag(SettlingInScene.NotReadyFlag, true);
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

        [UnityTest]
        public IEnumerator CampusB02PortalEntersFromEveryAuthoredCell()
        {
            DataDrivenWorldLoader loader = DataDrivenWorldLoader.Instance;
            FieldInfo catalogField = typeof(DataDrivenWorldLoader).GetField("npcCatalog", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo areasField = typeof(DataDrivenWorldLoader).GetField("availableAreas", BindingFlags.Instance | BindingFlags.NonPublic);
            NpcCatalogAsset catalog = (NpcCatalogAsset)catalogField.GetValue(loader);
            WorldAreaAsset[] availableAreas = (WorldAreaAsset[])areasField.GetValue(loader);
            WorldAreaAsset campus = availableAreas.Single(area => area.definition.id == "campus");

            for (int y = 17; y <= 19; y++)
            {
                loader.Load(campus, catalog);
                yield return null;
                GridPortal entrance = Object.FindObjectsByType<GridPortal>(FindObjectsInactive.Exclude)
                    .Single(portal => portal.Content != null && portal.Content.id == "P.C02");
                Assert.That(entrance.CentreCell, Is.EqualTo(new Vector2Int(18, 18)));
                Assert.That(entrance.FirstCell, Is.EqualTo(new Vector2Int(18, 17)));
                Assert.That(entrance.FootprintSize, Is.EqualTo(new Vector2Int(1, 3)));
                Vector2Int entry = new Vector2Int(18, y);
                Assert.That(entrance.World.GetPortalAt(entry), Is.SameAs(entrance));
                Assert.That(entrance.World.IsCellBlocked(entry), Is.False);

                GridMover player = Object.FindAnyObjectByType<GridMover>();
                Assert.That(player.TryTeleportTo(entrance.World, entry), Is.True);
                Assert.That(entrance.TryEnter(player), Is.True, $"Campus B02 entrance cell {entry} should transition.");
                Assert.That(loader.CurrentArea.definition.id, Is.EqualTo("B02.Ground"));
                Assert.That(player.CurrentCell, Is.EqualTo(new Vector2Int(19, 2)));
                GridPortal exit = Object.FindObjectsByType<GridPortal>(FindObjectsInactive.Exclude)
                    .Single(portal => portal.Content != null && portal.Content.id == "P.B02.00");
                Assert.That(exit.Content.destination, Is.SameAs(entrance.Content));
            }
        }
    }
}

