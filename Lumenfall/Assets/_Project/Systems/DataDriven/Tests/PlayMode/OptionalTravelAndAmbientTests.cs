using System;
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
    public sealed class OptionalTravelAndAmbientTests
    {
        private static GameEventRuntime Runtime()
        {
            return new GameEventRuntime(new GameEventCatalog(new[] {
                new GameEventDatabaseDefinition { events = new[] {
                    new GameEventDefinition { id = "chapter1.first_account" } } } }));
        }
        private static NpcDefinition Ambient()
        {
            return new NpcDefinition { id = "ambient_fixture", ambientOnly = true,
                ambientSchedule = new AmbientNpcScheduleDefinition { enabled = true, phaseCounterKey = "ambient.phase",
                    stops = new[] { new AmbientNpcStopDefinition { x = 1 }, new AmbientNpcStopDefinition { x = 2 },
                        new AmbientNpcStopDefinition { x = 3 } } } };
        }

        [TestCase("stella")] [TestCase("cinna_cirra_orientation")] [TestCase("cinna_cirra_trial")]
        [TestCase("orio")] [TestCase("katrina")] [TestCase("receptionist")]
        public void PrincipalActorsCannotBeOptedIntoAmbientSchedules(string id)
        {
            NpcDefinition npc = Ambient(); npc.id = id;
            Assert.That(AmbientNpcPolicy.TryGetStop(npc, Runtime(), out _), Is.False);
        }

        [TestCase(-1, 3)] [TestCase(int.MinValue, 2)] [TestCase(int.MaxValue, 2)]
        public void PhaseCounterNormalizesAndRestores(int phase, int expectedX)
        {
            GameEventRuntime runtime = Runtime(); runtime.SetCounter("ambient.phase", phase);
            GameEventRuntime restored = Runtime(); Assert.That(restored.ImportStateJson(runtime.ExportStateJson()), Is.True);
            Assert.That(AmbientNpcPolicy.TryGetStop(Ambient(), restored, out AmbientNpcStopDefinition stop), Is.True);
            Assert.That(stop.x, Is.EqualTo(expectedX));
        }

        [Test]
        public void DisabledOrQuestBoundNpcNeverSchedules()
        {
            NpcDefinition npc = Ambient(); npc.ambientSchedule.enabled = false;
            Assert.That(AmbientNpcPolicy.TryGetStop(npc, Runtime(), out _), Is.False);
            npc.ambientSchedule.enabled = true; npc.dialogue = new[] { new NpcDialogueDefinition { questId = "quest" } };
            Assert.That(AmbientNpcPolicy.TryGetStop(npc, Runtime(), out _), Is.False);
        }

        [Test]
        public void ShippedAmbientLinesRemainQuestNeutral()
        {
            var asset = Resources.Load<TextAsset>("GameEvents/quest_npc_dialogue");
            Assert.That(GameEventCatalog.TryParseJson(asset.text, asset.name, out var database, out _), Is.True);
            var ambient = database.dialogues.Where(d => d.id.StartsWith("ambient_", StringComparison.Ordinal)).ToArray();
            Assert.That(ambient.Length, Is.GreaterThan(0));
            foreach (var dialogue in ambient) Assert.That(AmbientNpcPolicy.IsQuestNeutral(dialogue), Is.True, dialogue.id);
        }

        [Test]
        public void AmbientRejectsNodeChoiceAndTerminalRewards()
        {
            var node = new DialogueNodeDefinition { id = "start" };
            var dialogue = new DialogueDefinition { id = "neutral", startNodeId = "start", nodes = new[] { node } };
            Assert.That(AmbientNpcPolicy.IsQuestNeutral(dialogue), Is.True);
            node.onEnterActions = new[] { new GameActionDefinition { type = "SetFlag", key = "quest", boolValue = true } };
            Assert.That(AmbientNpcPolicy.IsQuestNeutral(dialogue), Is.False);
            node.onEnterActions = Array.Empty<GameActionDefinition>();
            node.choices = new[] { new DialogueChoiceDefinition { actions = new[] { new GameActionDefinition { type = "CompleteQuest" } } } };
            Assert.That(AmbientNpcPolicy.IsQuestNeutral(dialogue), Is.False);
            node.choices = Array.Empty<DialogueChoiceDefinition>();
            dialogue.metadata = new[] { new GameMetadataEntry { key = "spellReward", value = "Ampere" } };
            Assert.That(AmbientNpcPolicy.IsQuestNeutral(dialogue), Is.False);
            dialogue.metadata[0].key = "keyItemReward";
            Assert.That(AmbientNpcPolicy.IsQuestNeutral(dialogue), Is.False);
        }

        [Test]
        public void AmbientWeightsSelectOnlySafeEntriesWithoutDuplicatingLargeWeights()
        {
            var safe = new DialogueDefinition { id = "safe", startNodeId = "start",
                nodes = new[] { new DialogueNodeDefinition { id = "start" } } };
            var other = new DialogueDefinition { id = "other", startNodeId = "start",
                nodes = new[] { new DialogueNodeDefinition { id = "start" } } };
            var unsafeLine = new DialogueDefinition { id = "unsafe", startNodeId = "start",
                metadata = new[] { new GameMetadataEntry { key = "keyItemReward" } }, nodes = safe.nodes };
            var runtime = new GameEventRuntime(new GameEventCatalog(new[] { new GameEventDatabaseDefinition {
                dialogues = new[] { safe, other, unsafeLine } } }));
            var npc = Ambient(); npc.dialogueIds = new[] { "unsafe", "safe" };
            npc.dialogue = new[] { new NpcDialogueDefinition { dialogueId = "other", weight = 3 } };
            Assert.That(AmbientNpcPolicy.SelectDialogue(npc, runtime, 0), Is.EqualTo("safe"));
            Assert.That(AmbientNpcPolicy.SelectDialogue(npc, runtime, 0.3), Is.EqualTo("other"));
            Assert.That(AmbientNpcPolicy.SelectDialogue(npc, runtime, 1), Is.EqualTo("other"));
        }

        [Test]
        public void ReservedGateRequiresExplicitToggleAndBothFlagChecks()
        {
            var gate = ScriptableObject.CreateInstance<PortalContentAsset>();
            try {
                var runtime = Runtime(); gate.reserved = true; gate.allowedFlag = "gate.north";
                runtime.SetFlag("gate.north", true);
                Assert.That(OptionalTravelPolicy.GateAllows(gate, runtime), Is.False);
                gate.allowReservedTravel = true;
                Assert.That(OptionalTravelPolicy.GateAllows(gate, runtime), Is.True);
                gate.requiredFlags = new[] { "extra" };
                Assert.That(OptionalTravelPolicy.GateAllows(gate, runtime), Is.False);
                runtime.SetFlag("extra", true);
                Assert.That(OptionalTravelPolicy.GateAllows(gate, runtime), Is.True);
                gate.allowedFlag = "";
                Assert.That(OptionalTravelPolicy.GateAllows(gate, runtime), Is.False);
            } finally { UnityEngine.Object.DestroyImmediate(gate); }
        }

        [Test]
        public void WaypointsRequireBothVisitsAndRealIntroductionAndSurviveRestore()
        {
            var pair = OptionalTravelPolicy.DisabledChapterOneWaypoints();
            var runtime = Runtime();
            runtime.SetFlag("chapter1.first_account", true);
            runtime.TriggerEvent("chapter1.first_account");
            runtime.SetFlag(OptionalTravelPolicy.VisitFlag("D03"), true);
            runtime.SetFlag(OptionalTravelPolicy.VisitFlag("W07"), true);
            Assert.That(OptionalTravelPolicy.WaypointAllows(pair[0], pair[1], runtime), Is.False);
            pair[0].enabled = pair[1].enabled = true;
            Assert.That(OptionalTravelPolicy.WaypointAllows(pair[0], pair[1], runtime), Is.True);
            var restored = Runtime(); restored.ImportStateJson(runtime.ExportStateJson());
            Assert.That(OptionalTravelPolicy.WaypointAllows(pair[0], pair[1], restored), Is.True);
            restored.SetFlag(OptionalTravelPolicy.VisitFlag("W07"), false);
            Assert.That(OptionalTravelPolicy.WaypointAllows(pair[0], pair[1], restored), Is.False);
            restored.SetFlag(OptionalTravelPolicy.VisitFlag("W07"), true);
            pair[1].regionId = "D08";
            Assert.That(OptionalTravelPolicy.WaypointAllows(pair[0], pair[1], restored), Is.False);
        }


        [UnityTest]
        public IEnumerator WaypointApiRejectsUnavailableAndBlockedArrivalThenRestoresExactSource()
        {
            yield return SceneManager.LoadSceneAsync("SampleScene");
            yield return null;
            var loader = DataDrivenWorldLoader.Instance;
            var player = DataDrivenWorldLoader.FindPlayerMover();
            var manager = GameEventManager.Instance;
            var runtimeProperty = typeof(GameEventManager).GetProperty("Runtime");
            var previousRuntime = manager.Runtime;
            var fields = BindingFlags.Instance | BindingFlags.NonPublic;
            var areasField = typeof(DataDrivenWorldLoader).GetField("availableAreas", fields);
            var catalog = (NpcCatalogAsset)typeof(DataDrivenWorldLoader).GetField("npcCatalog", fields).GetValue(loader);
            var areas = (WorldAreaAsset[])areasField.GetValue(loader);
            var dorm = areas.Single(a => a.definition.id == "B04.Ground");
            var classroom = areas.Single(a => a.definition.id == "B02.Ground");
            var pair = OptionalTravelPolicy.DisabledChapterOneWaypoints();
            pair[0].enabled = pair[1].enabled = true;
            typeof(DataDrivenWorldLoader).GetField("optionalWaypoints", fields).SetValue(loader, pair);
            var runtime = Runtime(); runtime.SetFlag("chapter1.first_account", true);
            runtime.TriggerEvent("chapter1.first_account");
            runtimeProperty.SetValue(manager, runtime);
            GameObject blocker = null;
            try {
                loader.Load(dorm, catalog);
                Assert.That(player.TryTeleportTo(player.World, pair[0].arrival), Is.True);
                yield return null; // Only actual player presence records the visit.
                Assert.That(runtime.GetFlag(OptionalTravelPolicy.VisitFlag("D03")), Is.True);
                Assert.That(loader.TryWaypointTravel("D03"), Is.False, "One endpoint is insufficient.");
                loader.Load(classroom, catalog);
                Assert.That(player.TryTeleportTo(player.World, pair[1].arrival), Is.True);
                yield return null;
                Assert.That(runtime.GetFlag(OptionalTravelPolicy.VisitFlag("W07")), Is.True);
                var previousCell = player.CurrentCell;
                areasField.SetValue(loader, areas.Where(a => a != dorm).ToArray());
                Assert.That(loader.TryWaypointTravel("W07"), Is.False);
                Assert.That(loader.CurrentArea, Is.SameAs(classroom));
                Assert.That(player.CurrentCell, Is.EqualTo(previousCell));
                areasField.SetValue(loader, areas);
                var instances = (System.Collections.Generic.Dictionary<WorldAreaAsset, GameObject>)
                    typeof(DataDrivenWorldLoader).GetField("areaInstances", fields).GetValue(loader);
                blocker = new GameObject("Waypoint blocked-arrival fixture");
                blocker.transform.SetParent(instances[dorm].transform);
                blocker.transform.position = player.World.CellToWorld(pair[0].arrival);
                blocker.AddComponent<GridObstacle>();
                Assert.That(loader.TryWaypointTravel("W07"), Is.False);
                Assert.That(loader.CurrentArea, Is.SameAs(classroom));
                Assert.That(player.CurrentCell, Is.EqualTo(previousCell));
                UnityEngine.Object.DestroyImmediate(blocker); blocker = null;
                Assert.That(loader.TryWaypointTravel("W07"), Is.True);
                Assert.That(loader.CurrentArea, Is.SameAs(dorm));
                Assert.That(player.CurrentCell, Is.EqualTo(pair[0].arrival));
                pair[1].arrival = Vector2Int.zero;
                Assert.That(loader.TryWaypointTravel("D03"), Is.False, "Cannot land outside the safe alcove.");
                Assert.That(loader.CurrentArea, Is.SameAs(dorm));
            } finally {
                if (blocker != null) UnityEngine.Object.DestroyImmediate(blocker);
                areasField.SetValue(loader, areas);
                runtimeProperty.SetValue(manager, previousRuntime);
            }
        }

        [UnityTest]
        public IEnumerator AmbientNavigationRetriesOccupancyAndReevaluatesSavedPhaseAfterReenable()
        {
            yield return SceneManager.LoadSceneAsync("SampleScene");
            yield return null;
            var runtime = GameEventManager.Instance.Runtime;
            var npc = UnityEngine.Object.FindObjectsByType<DynamicNpc>(FindObjectsInactive.Exclude)
                .Single(n => n.Id == "campus_student");
            var original = npc.Definition;
            var originalDialogue = npc.DialogueId;
            var player = DataDrivenWorldLoader.FindPlayerMover();
            var start = player.World.WorldToCell(npc.transform.position);
            var destination = start + Vector2Int.right;
            var occupant = new GameObject("Ambient occupancy fixture");
            occupant.transform.position = player.World.CellToWorld(destination);
            occupant.AddComponent<GridMover>();
            int failures = 0;
            try {
                var definition = new NpcDefinition { id = "ambient_fixture", mapId = "campus", ambientOnly = true,
                    ambientSchedule = new AmbientNpcScheduleDefinition { enabled = true, phaseCounterKey = "ambient.fixture.phase",
                        stops = new[] { new AmbientNpcStopDefinition { x = destination.x, y = destination.y },
                            new AmbientNpcStopDefinition { x = start.x, y = start.y } } } };
                npc.Configure(definition, null);
                var navigator = npc.GetComponent<GridNpcNavigator>();
                navigator.PathFailed += (_, __) => failures++;
                Assert.That(DataDrivenWorldLoader.FindPlayerMover(), Is.SameAs(player));
                yield return new WaitForSeconds(0.7f);
                Assert.That(failures, Is.GreaterThan(0));
                Assert.That(npc.GetComponent<GridMover>().CurrentCell, Is.EqualTo(start));
                UnityEngine.Object.DestroyImmediate(occupant); occupant = null;
                yield return new WaitForSeconds(0.9f);
                Assert.That(npc.GetComponent<GridMover>().CurrentCell, Is.EqualTo(destination));
                runtime.SetCounter("ambient.fixture.phase", 1);
                string saved = runtime.ExportStateJson();
                npc.gameObject.SetActive(false);
                runtime.SetCounter("ambient.fixture.phase", 0);
                Assert.That(runtime.ImportStateJson(saved), Is.True);
                npc.gameObject.SetActive(true);
                yield return new WaitForSeconds(0.9f);
                Assert.That(npc.GetComponent<GridMover>().CurrentCell, Is.EqualTo(start));
            } finally {
                if (occupant != null) UnityEngine.Object.DestroyImmediate(occupant);
                UnityEngine.Object.DestroyImmediate(npc.GetComponent<AmbientNpcSchedule>());
                UnityEngine.Object.DestroyImmediate(npc.GetComponent<GridNpcNavigator>());
                UnityEngine.Object.DestroyImmediate(npc.GetComponent<GridMover>());
                npc.Configure(original, originalDialogue);
            }
        }

        [UnityTest]
        public IEnumerator UnavailableAndBlockedPortalTravelPreservesSourceAreaAndCell()
        {
            yield return SceneManager.LoadSceneAsync("SampleScene");
            yield return null;
            var loader = DataDrivenWorldLoader.Instance;
            var player = DataDrivenWorldLoader.FindPlayerMover();
            var source = UnityEngine.Object.FindObjectsByType<GridPortal>(FindObjectsInactive.Exclude)
                .First(p => p.Content != null && p.Content.destination?.area?.definition.id == "eastForest");
            Assert.That(player.TryTeleportTo(source.World, source.FirstCell), Is.True);
            var originalArea = loader.CurrentArea; var originalCell = player.CurrentCell;
            bool originalReserved = source.Content.reserved;
            try {
                source.Content.reserved = true;
                Assert.That(loader.SwitchArea(source.Content, Vector2Int.zero), Is.False);
                source.Content.reserved = originalReserved;
                Assert.That(loader.SwitchArea(source.Content, new Vector2Int(99999, 99999)), Is.False);
                Assert.That(loader.CurrentArea, Is.SameAs(originalArea));
                Assert.That(player.CurrentCell, Is.EqualTo(originalCell));
                var unavailable = ScriptableObject.CreateInstance<WorldAreaAsset>();
                var originalDestinationArea = source.Content.destination.area;
                try {
                    source.Content.destination.area = unavailable;
                    Assert.That(loader.SwitchArea(source.Content, Vector2Int.zero), Is.False);
                    Assert.That(loader.CurrentArea, Is.SameAs(originalArea));
                    Assert.That(player.CurrentCell, Is.EqualTo(originalCell));
                } finally {
                    source.Content.destination.area = originalDestinationArea;
                    UnityEngine.Object.DestroyImmediate(unavailable);
                }
            } finally { source.Content.reserved = originalReserved; }
        }
    }
}
