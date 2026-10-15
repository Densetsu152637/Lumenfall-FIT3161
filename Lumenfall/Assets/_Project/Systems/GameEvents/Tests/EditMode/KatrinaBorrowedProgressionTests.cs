using System;
using System.Linq;
using Lumenfall.GameEvents;
using NUnit.Framework;
using UnityEngine;

namespace Lumenfall.Tests.GameEvents
{
    public sealed class KatrinaBorrowedProgressionTests
    {
        private const string Quest = "katrina_something_borrowed";
        private const string Item = "inventory.keyitem.katrina-keepsake";
        private const string Spell = "spell.unlocked.LostEchoes";
        private const string Lesson = "spell.lesson.LostEchoes";

        [Test]
        public void LessonRequiresTerminalContinueAndActiveQuest()
        {
            GameEventRuntime runtime = AuthoredRuntime();
            DialogueSession session = runtime.StartDialogue(Quest + "_dialogue");
            Assert.That(runtime.GetFlag(Spell), Is.False);
            session.SelectChoice("leave");
            Assert.That(runtime.GetFlag(Spell), Is.False);
            session = runtime.StartDialogue(Quest + "_dialogue");
            session.SelectChoice("accept");
            Assert.That(runtime.GetFlag(Spell), Is.False);
            Assert.That(runtime.GetFlag(Item), Is.False);
            session.End();
            Assert.That(runtime.GetFlag(Spell), Is.False);
            session = runtime.StartDialogue(Quest + "_dialogue");
            Assert.That(session.SelectChoice("active"), Is.True);
            Assert.That(session.SelectChoice("library"), Is.True);
            EditModeLifecycle.FinishDialogue(session);
            Assert.That(runtime.GetFlag(Lesson), Is.True, "The lesson teaches Lost Echoes for the search.");
            Assert.That(runtime.GetFlag(Spell), Is.False, "Lost Echoes is only learned when the quest completes.");
            Assert.That(runtime.GetFlag(Item), Is.False, "Teaching cannot recover the item.");
        }

        [Test]
        public void FindingAllThreeAnchorsAllowsReturningThemAndTeachesTheSpell()
        {
            GameEventRuntime runtime = AuthoredRuntime();
            runtime.StartQuest(Quest);
            DialogueSession session = runtime.StartDialogue(Quest + "_dialogue");
            Assert.That(session.AvailableChoices.Any(c => c.id == "report"), Is.False, "Nothing to return yet.");
            session.End();
            Assert.That(runtime.TriggerEvent("found_katrina_library_item").Succeeded, Is.True);
            Assert.That(runtime.GetFlag(Item), Is.False);
            Assert.That(runtime.TriggerEvent("found_katrina_garden_item").Succeeded, Is.True);
            Assert.That(runtime.GetFlag(Item), Is.False);
            Assert.That(runtime.TriggerEvent("found_katrina_stream_item").Succeeded, Is.True);
            Assert.That(runtime.GetFlag(Item), Is.True);
            Assert.That(runtime.GetQuestStatus(Quest), Is.EqualTo(QuestStatus.Active), "Finding is not completion.");
            Assert.That(runtime.GetFlag(Spell), Is.False);
            session = runtime.StartDialogue(Quest + "_dialogue");
            Assert.That(session.AvailableChoices.Any(c => c.id == "active"), Is.False);
            session.SelectChoice("report");
            Assert.That(runtime.GetQuestStatus(Quest), Is.EqualTo(QuestStatus.Completed));
            Assert.That(runtime.GetFlag(Spell), Is.True, "Returning the keepsake teaches Lost Echoes.");
        }

        [Test]
        public void CompletionAwardsItemWithoutUiExactlyOnceAndRoundTrips()
        {
            GameEventRuntime runtime = AuthoredRuntime();
            runtime.StartQuest(Quest);
            int itemNotifications = 0;
            runtime.FlagChanged += (key, value) => { if (key == Item && value) itemNotifications++; };
            runtime.SetFlag(Item, true);
            Assert.That(runtime.TriggerEvent("complete_" + Quest).Succeeded, Is.True);
            Assert.That(runtime.TriggerEvent("complete_" + Quest).Succeeded, Is.False);
            Assert.That(runtime.CompleteQuest(Quest), Is.False);
            Assert.That(itemNotifications, Is.EqualTo(1));
            GameEventRuntime restored = AuthoredRuntime();
            Assert.That(restored.ImportStateJson(runtime.ExportStateJson(false)), Is.True);
            Assert.That(restored.GetFlag(Item), Is.True);
            Assert.That(restored.GetQuestStatus(Quest), Is.EqualTo(QuestStatus.Completed));
        }

        [Test]
        public void LegacyCompletedSaveRecoversMissingItemButActiveSaveDoesNot()
        {
            GameEventRuntime runtime = AuthoredRuntime();
            string completed = "{\"quests\":[{\"questId\":\"" + Quest + "\",\"status\":\"Completed\"}]}";
            bool itemPresentWhenRestored = false;
            runtime.StateRestored += () => itemPresentWhenRestored = runtime.GetFlag(Item);
            Assert.That(runtime.ImportStateJson(completed), Is.True);
            Assert.That(itemPresentWhenRestored, Is.True);
            Assert.That(runtime.GetFlag(Item), Is.True);
            Assert.That(runtime.ImportStateJson(completed.Replace("Completed", "Active")), Is.True);
            Assert.That(runtime.GetFlag(Item), Is.False);
            Assert.That(runtime.GetFlag(Spell), Is.False);
        }

        [Test]
        public void GenericLessonRejectsMissingUnknownInactiveOrCompletedQuestGate()
        {
            foreach (string gate in new[] { null, "unknown", "lesson" })
            {
                GameEventRuntime runtime = Fixture(gate);
                Finish(runtime);
                Assert.That(runtime.GetFlag(Spell), Is.False, gate);
            }
            GameEventRuntime completed = Fixture("lesson");
            completed.StartQuest("lesson");
            completed.CompleteQuest("lesson");
            Finish(completed);
            Assert.That(completed.GetFlag(Spell), Is.False);
        }

        [Test]
        public void GenericLessonGrantsCommaSemicolonListsAndPersistsFlags()
        {
            GameEventRuntime runtime = Fixture("lesson", reward: "lostechoes; Gravitate,Rotara;Invalid");
            runtime.StartQuest("lesson");
            Finish(runtime);
            Assert.That(runtime.GetFlag(Spell), Is.True);
            Assert.That(runtime.GetFlag("spell.unlocked.Gravitate"), Is.True);
            Assert.That(runtime.GetFlag("spell.unlocked.Rotara"), Is.True);
            Assert.That(runtime.GetFlag("spell.unlocked.Invalid"), Is.False);
            GameEventRuntime restored = Fixture("lesson");
            restored.ImportStateJson(runtime.ExportStateJson(false));
            Assert.That(restored.GetFlag(Spell), Is.True);
        }

        [Test]
        public void GenericLessonRejectsUnfinishedMalformedAndUnknownPrerequisites()
        {
            foreach (string requirement in new[] { "prerequisite:Completed", "missing:Completed", "prerequisite", "prerequisite:99", "prerequisite:Completed;" })
            {
                GameEventRuntime runtime = Fixture("lesson", requiresQuest: requirement);
                runtime.StartQuest("lesson");
                Finish(runtime);
                Assert.That(runtime.GetFlag(Spell), Is.False, requirement);
            }
            GameEventRuntime valid = Fixture("lesson", requiresQuest: "prerequisite:Completed", explicitPrerequisite: "prerequisite");
            valid.StartQuest("prerequisite"); valid.CompleteQuest("prerequisite"); valid.StartQuest("lesson");
            Finish(valid);
            Assert.That(valid.GetFlag(Spell), Is.True);
            foreach (string prerequisite in new[] { "missing", "prerequisite;", "prerequisite" })
            {
                GameEventRuntime invalid = Fixture("lesson", explicitPrerequisite: prerequisite);
                invalid.StartQuest("lesson"); Finish(invalid);
                Assert.That(invalid.GetFlag(Spell), Is.False, prerequisite);
            }
        }

        [Test]
        public void ExistingFirstLightStillTeachesAfterTerminalLesson()
        {
            GameEventRuntime runtime = AuthoredRuntime();
            DialogueSession lesson = runtime.StartDialogue("stella_first_light_dialogue");
            lesson.SelectChoice("accept");
            Assert.That(runtime.GetFlag("spell.unlocked.Ampere"), Is.False);
            EditModeLifecycle.FinishDialogue(lesson);
            Assert.That(runtime.GetFlag("spell.unlocked.Ampere"), Is.True);
        }

        private static void Finish(GameEventRuntime runtime) { runtime.StartDialogue("lesson_dialogue").Continue(); }
        private static GameMetadataEntry Meta(string key, string value) => new GameMetadataEntry { key = key, value = value };
        private static GameEventRuntime Fixture(string gate, string reward = "LostEchoes", string requiresQuest = null, string explicitPrerequisite = null)
        {
            var definition = new GameEventDatabaseDefinition
            {
                quests = new[] { new QuestDefinition { id = "lesson", metadata = new[] { Meta("requiresQuest", requiresQuest) } }, new QuestDefinition { id = "prerequisite" } },
                dialogues = new[] { new DialogueDefinition { id = "lesson_dialogue", startNodeId = "terminal",
                    metadata = new[] { Meta("spellReward", reward), Meta("spellRewardQuestId", gate), Meta("spellRewardPrerequisiteQuestIds", explicitPrerequisite) },
                    nodes = new[] { new DialogueNodeDefinition { id = "terminal", text = "Lesson finished." } } } }
            };
            return new GameEventRuntime(new GameEventCatalog(new[] { definition }));
        }
        private static GameEventRuntime AuthoredRuntime()
        {
            TextAsset asset = Resources.Load<TextAsset>("GameEvents/quest_npc_dialogue");
            Assert.That(GameEventCatalog.TryParseJson(asset.text, asset.name, out GameEventDatabaseDefinition database, out string error), Is.True, error);
            return new GameEventRuntime(new GameEventCatalog(new[] { database }));
        }
    }
}
