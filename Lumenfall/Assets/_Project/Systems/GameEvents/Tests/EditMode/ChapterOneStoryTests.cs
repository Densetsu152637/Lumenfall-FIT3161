using System;
using System.Collections.Generic;
using System.Reflection;
using Lumenfall.DataDriven;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using NUnit.Framework;
using UnityEngine;

namespace Lumenfall.Tests.GameEvents
{
    public sealed class ChapterOneStoryTests
    {
        private static GameEventRuntime CreateRuntime()
        {
            var databases = new List<GameEventDatabaseDefinition>();
            foreach (string path in new[] { "GameEvents/chapter_one_story", "GameEvents/quest_npc_dialogue" })
            {
                TextAsset asset = Resources.Load<TextAsset>(path);
                Assert.That(asset, Is.Not.Null, path);
                Assert.That(GameEventCatalog.TryParseJson(asset.text, path, out var database, out string error), Is.True, error);
                databases.Add(database);
            }
            var catalog = new GameEventCatalog(databases);
            Assert.That(catalog.IsValid, Is.True, string.Join("\n", catalog.ValidationMessages));
            return new GameEventRuntime(catalog);
        }
        private static void Finish(GameEventRuntime runtime, string id)
        {
            DialogueSession dialogue = runtime.StartDialogue(id);
            Assert.That(dialogue, Is.Not.Null, id);
            int guard = 0;
            while (dialogue.IsActive && guard++ < 50)
            {
                if (dialogue.AvailableChoices.Count > 0)
                {
                    string choice = id == "chapter1.sleep_prompt" ? "sleep" : "done";
                    Assert.That(dialogue.SelectChoice(choice), Is.True, id);
                }
                else Assert.That(dialogue.Continue(), Is.True, id);
            }
            Assert.That(dialogue.IsCompleted, Is.True, id);
        }
        private static void FinishFriends(GameEventRuntime runtime)
        {
            foreach (string quest in new[] { "stella_first_light", "stella_string_them_together",
                "cinna_out_of_alignment", "cinna_cold_feet_warm_idea", "orio_patch_notes", "katrina_something_borrowed" })
            {
                Assert.That(runtime.StartQuest(quest), Is.True);
                Assert.That(runtime.TriggerEvent("complete_" + quest).Succeeded, Is.True);
                Finish(runtime, quest + "_dialogue");
            }
        }
        private static void ReachBedroom(GameEventRuntime runtime)
        {
            Finish(runtime, "chapter1.reception_first");
            FinishFriends(runtime);
            Finish(runtime, "chapter1.reception_handover");
            Finish(runtime, "chapter1.group");
            Assert.That(runtime.ChapterOne.TryEnterSuite(), Is.True);
            Finish(runtime, "chapter1.guide");
            Assert.That(runtime.ChapterOne.TryReachRoom(), Is.True);
        }

        [Test]
        public void PrematureAndDirectProgressEventsCannotGrantKeyOrCompleteChapter()
        {
            var runtime = CreateRuntime();
            foreach (string suffix in new[] { "first_account", "key_handover", "group_discussion",
                "suite_entered", "room_guidance", "room_arrival", "sleep", "complete" })
                Assert.That(runtime.TriggerEvent("chapter1." + suffix).Status, Is.EqualTo(GameEventTriggerStatus.ConditionsNotMet), suffix);
            Assert.That(runtime.StartDialogue("chapter1.reception_handover"), Is.Null);
            Assert.That(runtime.StartDialogue("chapter1.sleep_prompt"), Is.Null);
            Assert.That(runtime.ChapterOne.TryEnterSuite(), Is.False);
            Assert.That(runtime.ChapterOne.TrySleep(), Is.False);
            Assert.That(runtime.ChapterOne.TryWake(), Is.False);
            Assert.That(runtime.GetFlag(ChapterOneStory.KeyFlag), Is.False);
        }

        [Test]
        public void ClosedReceptionAndQuestDialoguesDoNotCountAsCompletedMeetings()
        {
            var runtime = CreateRuntime();
            runtime.StartDialogue("chapter1.reception_first").End();
            Assert.That(runtime.ChapterOne.Stage, Is.EqualTo(ChapterOneStage.Arrival));
            Finish(runtime, "chapter1.reception_first");
            runtime.StartDialogue("orio_patch_notes_dialogue").End();
            Assert.That(runtime.GetFlag("chapter1.met.orio"), Is.False);
        }

        [Test]
        public void AllSixQuestsAndAllFourMeetingsAreRequiredForHandover()
        {
            var runtime = CreateRuntime();
            Finish(runtime, "chapter1.reception_first");
            foreach (var quest in runtime.Catalog.Quests.Values)
            { runtime.StartQuest(quest.id); runtime.TriggerEvent("complete_" + quest.id); }
            Assert.That(runtime.ChapterOne.Stage, Is.EqualTo(ChapterOneStage.FindingFriends));
            Assert.That(runtime.StartDialogue("chapter1.reception_handover"), Is.Null);
            foreach (string id in new[] { "stella_first_light", "cinna_out_of_alignment", "orio_patch_notes", "katrina_something_borrowed" })
                Finish(runtime, id + "_dialogue");
            Assert.That(runtime.ChapterOne.Stage, Is.EqualTo(ChapterOneStage.ReturnToReception));
            Finish(runtime, "chapter1.reception_handover");
            Assert.That(runtime.ChapterOne.HasKey, Is.True);
            Assert.That(runtime.ChapterOne.CanEnterSuite, Is.False);
            Assert.That(runtime.GetFlag("chapter1.mystery.unresolved"), Is.True);
            Assert.That(runtime.ChapterOne.TryEnterSuite(), Is.False);
            Finish(runtime, "chapter1.group");
            Assert.That(runtime.ChapterOne.CanEnterSuite, Is.True);
        }

        [Test]
        public void SleepDoesNotCompleteChapterUntilWakeAndEachCheckpointRestores()
        {
            var runtime = CreateRuntime();
            ReachBedroom(runtime);
            var restored = CreateRuntime();
            Assert.That(restored.ImportStateJson(runtime.ExportStateJson()), Is.True);
            Assert.That(restored.ChapterOne.Stage, Is.EqualTo(ChapterOneStage.ReadyForSleep));
            Assert.That(restored.ChapterOne.HasKey, Is.True);
            DialogueSession decline = restored.StartDialogue("chapter1.sleep_prompt");
            decline.SelectChoice("later");
            decline.Continue();
            Assert.That(restored.ChapterOne.Stage, Is.EqualTo(ChapterOneStage.ReadyForSleep));
            DialogueSession abandonedRest = restored.StartDialogue("chapter1.sleep_prompt");
            abandonedRest.SelectChoice("sleep");
            abandonedRest.End();
            Assert.That(restored.ChapterOne.Stage, Is.EqualTo(ChapterOneStage.ReadyForSleep));
            Finish(restored, "chapter1.sleep_prompt");
            Assert.That(restored.ChapterOne.Stage, Is.EqualTo(ChapterOneStage.Sleeping));
            Assert.That(restored.HasEventTriggered(ChapterOneStory.CompleteEventId), Is.False);
            var asleep = CreateRuntime();
            Assert.That(asleep.ImportStateJson(restored.ExportStateJson()), Is.True);
            Assert.That(asleep.ChapterOne.Stage, Is.EqualTo(ChapterOneStage.Sleeping));
            Assert.That(asleep.ChapterOne.TryWake(), Is.True);
            Assert.That(asleep.ChapterOne.TryWake(), Is.False);
            Assert.That(asleep.ChapterOne.Stage, Is.EqualTo(ChapterOneStage.Complete));
            Assert.That(asleep.GetFlag("chapter1.mystery.unresolved"), Is.True);
            var morning = CreateRuntime();
            morning.ImportStateJson(asleep.ExportStateJson());
            Assert.That(morning.HasEventTriggered(ChapterOneStory.CompleteEventId), Is.True);
        }

        [Test]
        public void ScopedPhysicalContextCanRejectDirectSleepAndWakeAtOtherwiseValidStage()
        {
            var runtime = CreateRuntime();
            ReachBedroom(runtime);
            Func<string, bool> noSleep = id => id != "chapter1.sleep";
            runtime.ChapterOne.AddTransitionPermission(noSleep);
            Assert.That(runtime.ChapterOne.TrySleep(), Is.False);
            Assert.That(runtime.ChapterOne.Stage, Is.EqualTo(ChapterOneStage.ReadyForSleep));
            runtime.ChapterOne.RemoveTransitionPermission(noSleep);
            Assert.That(runtime.ChapterOne.TrySleep(), Is.True);
            Func<string, bool> noWake = id => id != "chapter1.complete";
            runtime.ChapterOne.AddTransitionPermission(noWake);
            Assert.That(runtime.ChapterOne.TryWake(), Is.False);
            Assert.That(runtime.HasEventTriggered(ChapterOneStory.CompleteEventId), Is.False);
            runtime.ChapterOne.RemoveTransitionPermission(noWake);
            Assert.That(runtime.ChapterOne.TryWake(), Is.True);
        }

        [Test]
        public void ScopedGridGuardsRejectMovementPathfindingAndTeleportAndCleanUpIndependently()
        {
            GameObject host = new GameObject("chapter-grid-test");
            try
            {
                GridWorld grid = host.AddComponent<GridWorld>();
                grid.ConfigureBounds(36, 24);
                GameObject actor = new GameObject("player");
                actor.transform.SetParent(host.transform);
                GridMover player = actor.AddComponent<GridMover>();
                Func<Vector2Int, bool> suiteGuard = cell => cell.x < 26;
                Func<Vector2Int, bool> anotherGuard = cell => cell.y < 20;
                grid.AddCellPermission(suiteGuard);
                grid.AddCellPermission(anotherGuard);
                Assert.That(grid.IsCellBlocked(new Vector2Int(29, 8)), Is.True);
                Assert.That(grid.CanTraverse(new Vector2Int(25, 8), new Vector2Int(26, 8)), Is.False);
                Assert.That(player.TryTeleportTo(grid, new Vector2Int(29, 8)), Is.False);
                Assert.That(player.CurrentCell, Is.EqualTo(Vector2Int.zero));
                Assert.That(player.TryTeleportTo(grid, new Vector2Int(25, 8)), Is.True);
                Assert.That(player.TryMove(Vector2Int.right), Is.False);
                grid.RemoveCellPermission(suiteGuard);
                Assert.That(player.TryTeleportTo(grid, new Vector2Int(29, 8)), Is.True);
                Assert.That(grid.IsCellAvailable(new Vector2Int(10, 21)), Is.False, "Other owner remains registered.");
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }

        [Test]
        public void BridgeGuardsPrivateCellsBeforeSpawnAndRebindsWithoutLeaking()
        {
            GameEventManager.Instance.Runtime.ResetState();
            GameObject host = new GameObject("chapter-bridge-test");
            GameObject root = new GameObject("area");
            try
            {
                GridWorld grid = host.AddComponent<GridWorld>();
                grid.ConfigureBounds(36, 24);
                GameObject actor = new GameObject("player");
                actor.transform.SetParent(host.transform);
                GridMover player = actor.AddComponent<GridMover>();
                ChapterOneInteraction bridge = host.AddComponent<ChapterOneInteraction>();
                bridge.BindArea(new MapDefinition { id = "B04.Ground" }, root, grid);
                Assert.That(grid.IsCellBlocked(new Vector2Int(29, 8)), Is.True);
                Assert.That(player.TryTeleportTo(grid, new Vector2Int(29, 8)), Is.False);
                Assert.That(bridge.TryInteractBed(player), Is.False);
                Assert.That(GameEventManager.Instance.StartDialogue("chapter1.reception_first"), Is.Null, "Wrong map.");
                bridge.enabled = false;
                typeof(ChapterOneInteraction).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(bridge, null);
                Assert.That(grid.IsCellBlocked(new Vector2Int(29, 8)), Is.False);
                bridge.enabled = true;
                typeof(ChapterOneInteraction).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(bridge, null);
                Assert.That(grid.IsCellBlocked(new Vector2Int(29, 8)), Is.True);
                bridge.BindArea(new MapDefinition { id = "B01.Ground" }, root, grid);
                Assert.That(grid.IsCellBlocked(new Vector2Int(29, 8)), Is.False);
                Assert.That(GameEventManager.Instance.StartDialogue("chapter1.reception_first"), Is.Null, "Too far from reception.");
                Assert.That(player.TryTeleportTo(grid, new Vector2Int(13, 3)), Is.True);
                Assert.That(GameEventManager.Instance.StartDialogue("chapter1.reception_first"), Is.Not.Null);
                GameEventManager.Instance.Runtime.ActiveDialogue.End();
                GameEventManager.Instance.ReloadDefinitions();
                bridge.BindArea(new MapDefinition { id = "B04.Ground" }, root, grid);
                Assert.That(grid.IsCellBlocked(new Vector2Int(29, 8)), Is.True);
                Assert.That(GameEventManager.Instance.StartDialogue("chapter1.reception_first"), Is.Null, "New runtime has bridge context guard.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(host);
                GameEventManager.Instance.Runtime.ResetState();
            }
        }
    }
}
