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
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Lumenfall.Tests.World
{
    public sealed class AuthoredWorldPlayModeTests
    {
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
        public IEnumerator OrioRuneHallPortalsUseLiveCellsAndRestoreSavedHallState()
        {
            yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
            yield return null;
            yield return DismissStartupMenu();
            GameEventRuntime events = GameEventManager.Instance.Runtime;
            events.ResetState();
            WorldStateService.Instance.ResetAllState();
            DataDrivenWorldLoader loader = DataDrivenWorldLoader.Instance;
            NpcCatalogAsset catalog = (NpcCatalogAsset)typeof(DataDrivenWorldLoader)
                .GetField("npcCatalog", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(loader);
            loader.Load(loader.GetArea(OrioDaisRoom.ForestMapId), catalog);
            events.StartQuest("orio_patch_notes");
            yield return null;

            GridPortal entrance = Object.FindObjectsByType<GridPortal>(FindObjectsInactive.Exclude)
                .Single(portal => portal.PortalId == "P.F04.ORIO.HALL");
            Assert.That(loader.SwitchArea(entrance.Content, Vector2Int.zero), Is.True);
            yield return null;
            Assert.That(loader.LoadedMap.id, Is.EqualTo(OrioDaisRoom.MapId));
            GridMover player = Object.FindAnyObjectByType<GridPlayerInput>().GetComponent<GridMover>();
            GridPortal exit = Object.FindObjectsByType<GridPortal>(FindObjectsInactive.Exclude)
                .Single(portal => portal.PortalId == "P.ORIO.HALL.F04");
            Assert.That(player.CurrentCell, Is.EqualTo(exit.GetArrivalCell(Vector2Int.zero)));
            string savedHall = loader.CaptureStateJson();
            Assert.That(loader.SwitchArea(exit.Content, Vector2Int.zero), Is.True);
            yield return null;
            Assert.That(loader.LoadedMap.id, Is.EqualTo(OrioDaisRoom.ForestMapId));
            Assert.That(player.CurrentCell, Is.EqualTo(entrance.GetArrivalCell(Vector2Int.zero)));
            Assert.That(loader.RestoreStateJson(savedHall), Is.True);
            yield return null;
            Assert.That(loader.LoadedMap.id, Is.EqualTo(OrioDaisRoom.MapId));
            Assert.That(loader.SwitchArea(exit.Content, Vector2Int.zero), Is.True);
            yield return null;
            Assert.That(loader.LoadedMap.id, Is.EqualTo(OrioDaisRoom.ForestMapId));
        }

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
                    .Select(portal => portal.Content).Where(content => content != null && !content.disabled).ToArray();
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
            Assert.That(activeRouteCount, Is.EqualTo(10), "All active forest, campus, and building portal edges should be traversable.");

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
        public IEnumerator IceTrialPortalSlidesFollowTheSketchRouteAndCompleteColdFeet()
        {
            yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
            yield return null;
            yield return DismissStartupMenu();
            // The startup menu may already have loaded the latest save as its backdrop; start from a clean world.
            GameEventManager.Instance.Runtime.ResetState();
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
            Assert.That(entrance.World.WorldToCell(entrance.transform.position), Is.EqualTo(new Vector2Int(47, 45)));
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

            Vector2Int[] route =
            {
                Vector2Int.up, Vector2Int.right, Vector2Int.left, Vector2Int.down, Vector2Int.right,
                Vector2Int.right, Vector2Int.down, Vector2Int.down, Vector2Int.right, Vector2Int.left,
                Vector2Int.up, Vector2Int.up, Vector2Int.right, Vector2Int.up, Vector2Int.right
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
            yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
            yield return null;
            yield return DismissStartupMenu();

            DataDrivenWorldLoader loader = DataDrivenWorldLoader.Instance;
            FieldInfo catalogField = typeof(DataDrivenWorldLoader).GetField("npcCatalog", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo areasField = typeof(DataDrivenWorldLoader).GetField("availableAreas", BindingFlags.Instance | BindingFlags.NonPublic);
            NpcCatalogAsset catalog = (NpcCatalogAsset)catalogField.GetValue(loader);
            WorldAreaAsset campus = ((WorldAreaAsset[])areasField.GetValue(loader)).Single(area => area.definition.id == "campus");
            GameEventRuntime events = GameEventManager.Instance.Runtime;
            events.ResetState();
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

            loader.Load(campus, catalog);
            yield return null;
            Assert.That(WorldStateService.Instance.TryGetObject(CinnaAlignmentRelic.RelicId, out _), Is.False, "The relic has left campus.");
            DynamicNpc terraceCinna = Object.FindObjectsByType<DynamicNpc>(FindObjectsInactive.Include)
                .First(npc => npc.Definition != null && npc.Definition.id == "cinna_cirra_orientation");
            Assert.That(terraceCinna.gameObject.activeSelf, Is.True);
            Vector2Int reportedFrom = player.CurrentCell;
            DialogueSession report = events.StartDialogue("cinna_out_of_alignment_dialogue");
            Assert.That(report.SelectChoice("report"), Is.True);
            report.Continue();
            yield return null;
            Assert.That(CinnaTrialDeparture.InProgress, Is.True, "Cinna leaving for the trial fades the screen.");
            float deadline = Time.realtimeSinceStartup + 5f;
            while (CinnaTrialDeparture.InProgress && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(CinnaTrialDeparture.InProgress, Is.False);
            Assert.That(events.GetQuestStatus("cinna_out_of_alignment"), Is.EqualTo(QuestStatus.Completed));
            Assert.That(loader.CurrentArea.definition.id, Is.EqualTo("campus"), "Only Cinna goes; the player stays on campus.");
            Assert.That(player.CurrentCell, Is.EqualTo(reportedFrom));
            Assert.That(terraceCinna.gameObject.activeSelf, Is.False, "Cinna has left the terrace.");

            GridPortal trialPortal = Object.FindObjectsByType<GridPortal>(FindObjectsInactive.Exclude)
                .Single(portal => portal.Content != null && portal.Content.id == CinnaIceTrialEntrance.PortalId);
            Assert.That(loader.SwitchArea(trialPortal.Content, Vector2Int.zero), Is.True);
            yield return null;
            DynamicNpc trialCinna = Object.FindObjectsByType<DynamicNpc>(FindObjectsInactive.Include)
                .First(npc => npc.Definition != null && npc.Definition.id == "cinna_cirra_trial");
            Assert.That(trialCinna.gameObject.activeSelf, Is.True, "Cinna is waiting in the trial lobby.");
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
            GridPortal campusArrival = Object.FindObjectsByType<GridPortal>(FindObjectsInactive.Exclude)
                .Single(portal => portal.Content == gate.Content.destination);
            // The authored forest gate centres arrivals; the reciprocal campus gate preserves offsets.
            Assert.That(player.CurrentCell, Is.EqualTo(campusArrival.CentreCell));

            foreach (Vector2Int entry in campusArrival.EnumeratePortalCells().ToArray())
            {
                GridPortal campusGate = Object.FindObjectsByType<GridPortal>(FindObjectsInactive.Exclude)
                    .Single(portal => portal.Content != null && portal.Content.id == "P.C00");
                Assert.That(campusGate.FootprintSize, Is.EqualTo(new Vector2Int(3, 1)));
                Assert.That(campusGate.World.GetPortalAt(entry), Is.SameAs(campusGate),
                    "All campus gate tiles must belong to the same reciprocal portal.");
                Assert.That(campusGate.World.IsCellBlocked(entry), Is.False);
                Assert.That(CanReach(campusGate.World, player.CurrentCell, entry), Is.True,
                    "All campus gate tiles must be reachable from the forest arrival.");
                Vector2Int entryOffset = entry - campusGate.CentreCell;
                Assert.That(player.TryTeleportTo(campusGate.World, entry), Is.True);
                Assert.That(campusGate.TryEnter(player), Is.True);
                Assert.That(loader.CurrentArea.definition.id, Is.EqualTo("westForest"));
                GridPortal forestReturn = Object.FindObjectsByType<GridPortal>(FindObjectsInactive.Exclude)
                    .Single(portal => portal.Content != null && portal.Content.id == "P.F00");
                Assert.That(player.CurrentCell, Is.EqualTo(forestReturn.GetArrivalCell(entryOffset)));
                Assert.That(forestReturn.Content.destination, Is.SameAs(campusGate.Content));
                Assert.That(player.TryTeleportTo(forestReturn.World, new Vector2Int(16, 48)), Is.True);
                Assert.That(forestReturn.TryEnter(player), Is.True);
                Assert.That(loader.CurrentArea.definition.id, Is.EqualTo("campus"));
                Assert.That(player.CurrentCell, Is.EqualTo(campusGate.CentreCell));
            }

            GridPortal b02Entrance = Object.FindObjectsByType<GridPortal>(FindObjectsInactive.Exclude)
                .Single(portal => portal.Content != null && portal.Content.id == "P.C02");
            Assert.That(CanReach(gate.World, player.CurrentCell, b02Entrance.FirstCell), Is.True,
                "The campus route from the forest arrival to B02 must be walkable.");
            Assert.That(player.TryTeleportTo(b02Entrance.World, b02Entrance.FirstCell), Is.True);
            Assert.That(b02Entrance.TryEnter(player), Is.True, "The B02 entrance must transfer the player.");
            Assert.That(loader.CurrentArea.definition.id, Is.EqualTo("B02.Ground"));
            GridPortal b02Exit = Object.FindObjectsByType<GridPortal>(FindObjectsInactive.Exclude)
                .Single(portal => portal.Content != null && portal.Content.id == "P.B02.00");
            Assert.That(player.CurrentCell, Is.EqualTo(b02Exit.GetArrivalCell(b02Entrance.FirstCell - b02Entrance.CentreCell)));
            Assert.That(CanReach(b02Exit.World, player.CurrentCell, new Vector2Int(12, 5)), Is.True,
                "The B02 landing must reach Stella's interaction side.");
            Assert.That(CanReach(b02Exit.World, player.CurrentCell, b02Exit.FirstCell), Is.True,
                "The B02 landing must reach its return portal.");
            Assert.That(player.TryTeleportTo(b02Exit.World, b02Exit.FirstCell), Is.True);
            Assert.That(b02Exit.TryEnter(player), Is.True, "The B02 exit must return the player to campus.");
            Assert.That(loader.CurrentArea.definition.id, Is.EqualTo("campus"));
            Assert.That(player.CurrentCell, Is.EqualTo(b02Entrance.GetArrivalCell(b02Exit.FirstCell - b02Exit.CentreCell)));
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
        public IEnumerator CampusPortalEntersB02FromEveryCell()
        {
            yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
            yield return null;

            DataDrivenWorldLoader loader = DataDrivenWorldLoader.Instance;
            FieldInfo catalogField = typeof(DataDrivenWorldLoader).GetField("npcCatalog", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo areasField = typeof(DataDrivenWorldLoader).GetField("availableAreas", BindingFlags.Instance | BindingFlags.NonPublic);
            NpcCatalogAsset catalog = (NpcCatalogAsset)catalogField.GetValue(loader);
            WorldAreaAsset[] availableAreas = (WorldAreaAsset[])areasField.GetValue(loader);
            WorldAreaAsset campus = availableAreas.Single(area => area.definition.id == "campus");

            loader.Load(campus, catalog);
            yield return null;
            GridPortal authoredEntrance = Object.FindObjectsByType<GridPortal>(FindObjectsInactive.Exclude)
                .Single(portal => portal.Content != null && portal.Content.id == "P.C02");
            Assert.That(authoredEntrance.FootprintSize, Is.EqualTo(new Vector2Int(1, 4)));
            foreach (Vector2Int entry in authoredEntrance.EnumeratePortalCells().ToArray())
            {
                loader.Load(campus, catalog);
                yield return null;
                GridPortal entrance = Object.FindObjectsByType<GridPortal>(FindObjectsInactive.Exclude)
                    .Single(portal => portal.Content != null && portal.Content.id == "P.C02");
                Assert.That(entrance.World.IsCellBlocked(entry), Is.False);

                GridMover player = Object.FindAnyObjectByType<GridMover>();
                Vector2Int entryOffset = entry - entrance.CentreCell;
                Assert.That(player.TryTeleportTo(entrance.World, entry), Is.True);
                Assert.That(entrance.TryEnter(player), Is.True, $"Campus B02 entrance cell {entry} should transition.");
                Assert.That(loader.CurrentArea.definition.id, Is.EqualTo("B02.Ground"));
                GridPortal exit = Object.FindObjectsByType<GridPortal>(FindObjectsInactive.Exclude)
                    .Single(portal => portal.Content == entrance.Content.destination);
                Assert.That(player.CurrentCell, Is.EqualTo(exit.GetArrivalCell(entryOffset)));
            }
        }
    }
}

