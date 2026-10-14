using System.Collections.Generic;
using Lumenfall.GameEvents;
using NUnit.Framework;
using UnityEngine;

namespace Lumenfall.Tests.GameEvents
{
    public sealed class QuestDialogueContentTests
    {
        private static readonly string[] QuestIds =
        {
            "stella_first_light", "stella_string_them_together", "cinna_out_of_alignment",
            "cinna_cold_feet_warm_idea", "orio_patch_notes", "katrina_something_borrowed"
        };

        [Test]
        public void QuestDialoguePackage_HasAllLifecycleBranchesAndCompletionEvents()
        {
            TextAsset asset = Resources.Load<TextAsset>("GameEvents/quest_npc_dialogue");
            Assert.That(asset, Is.Not.Null);
            Assert.That(GameEventCatalog.TryParseJson(asset.text, asset.name,
                out GameEventDatabaseDefinition database, out string error), Is.True, error);
            GameEventCatalog catalog = new GameEventCatalog(new[] { database });
            Assert.That(catalog.IsValid, Is.True, string.Join("\n", catalog.ValidationMessages));

            foreach (string questId in QuestIds)
            {
                Assert.That(catalog.TryGetQuest(questId, out QuestDefinition quest), Is.True, questId);
                Assert.That(catalog.TryGetDialogue(questId + "_dialogue", out DialogueDefinition dialogue), Is.True, questId);
                Assert.That(dialogue.nodes.Length, Is.GreaterThanOrEqualTo(4), questId);
                Assert.That(dialogue.nodes[0].choices.Length, Is.GreaterThanOrEqualTo(4), questId);
                Assert.That(catalog.TryGetEvent("complete_" + questId, out GameEventDefinition completion), Is.True, questId);
                Assert.That(completion.repeatable, Is.False, questId);
            }
        }

        [Test]
        public void QuestDialoguePackage_PrerequisitesAndCompletionEventsFollowLifecycle()
        {
            TextAsset asset = Resources.Load<TextAsset>("GameEvents/quest_npc_dialogue");
            Assert.That(GameEventCatalog.TryParseJson(asset.text, asset.name,
                out GameEventDatabaseDefinition database, out string error), Is.True, error);
            GameEventRuntime runtime = new GameEventRuntime(new GameEventCatalog(new[] { database }));
            // This catalogue traversal isolates the world condition; OrioPatchNotesTests verifies the real resolver.
            runtime.RegisterConditionHandler("OrioMechanismAligned", (condition, current) => true);

            Assert.That(runtime.StartDialogue("stella_string_them_together_dialogue"), Is.Not.Null);
            Assert.That(ChoiceIds(runtime.ActiveDialogue), Does.Not.Contain("accept"));
            runtime.ActiveDialogue.End();
            AcceptAndComplete(runtime, "stella_first_light");
            Assert.That(runtime.StartDialogue("stella_string_them_together_dialogue"), Is.Not.Null);
            Assert.That(ChoiceIds(runtime.ActiveDialogue), Does.Contain("accept"));
            Assert.That(runtime.ActiveDialogue.SelectChoice("accept"), Is.True);
            Assert.That(runtime.GetQuestStatus("stella_string_them_together"), Is.EqualTo(QuestStatus.Active));
            Assert.That(runtime.TriggerEvent("complete_stella_string_them_together").Succeeded, Is.True);
            runtime.ActiveDialogue.End();
            Assert.That(runtime.StartDialogue("stella_string_them_together_dialogue"), Is.Not.Null);
            Assert.That(runtime.ActiveDialogue.SelectChoice("report"), Is.True);
            Assert.That(runtime.GetQuestStatus("stella_string_them_together"), Is.EqualTo(QuestStatus.Completed));

            AcceptAndComplete(runtime, "cinna_out_of_alignment");
            Assert.That(runtime.StartDialogue("cinna_cold_feet_warm_idea_dialogue"), Is.Not.Null);
            Assert.That(runtime.ActiveDialogue.SelectChoice("accept"), Is.True);
            Assert.That(runtime.TriggerEvent("complete_cinna_cold_feet_warm_idea").Succeeded, Is.True);
            Assert.That(runtime.StartDialogue("orio_patch_notes_dialogue"), Is.Not.Null);
            Assert.That(runtime.ActiveDialogue.SelectChoice("accept"), Is.True);
            runtime.ActiveDialogue.End();
            Assert.That(runtime.StartDialogue("orio_patch_notes_dialogue"), Is.Not.Null);
            Assert.That(runtime.ActiveDialogue.SelectChoice("report"), Is.True);
            Assert.That(runtime.GetQuestStatus("orio_patch_notes"), Is.EqualTo(QuestStatus.Completed));
            runtime.SetFlag("inventory.keyitem.katrina-keepsake", true);
            AcceptAndComplete(runtime, "katrina_something_borrowed");
            Assert.That(runtime.TriggerEvent("complete_katrina_something_borrowed").Status,
                Is.EqualTo(GameEventTriggerStatus.AlreadyTriggered));
        }

        [Test]
        public void CampusStudent_OffersAConciseCampusTourOrFriendlyFarewell()
        {
            TextAsset asset = Resources.Load<TextAsset>("GameEvents/quest_npc_dialogue");
            Assert.That(GameEventCatalog.TryParseJson(asset.text, asset.name,
                out GameEventDatabaseDefinition database, out string error), Is.True, error);
            GameEventRuntime runtime = new GameEventRuntime(new GameEventCatalog(new[] { database }));

            DialogueSession tour = runtime.StartDialogue("ambient_courtyard_student");
            Assert.That(ChoiceIds(tour), Is.EquivalentTo(new[] { "new", "familiar" }));
            Assert.That(tour.SelectChoice("new"), Is.True);
            string tourText = string.Empty;
            while (tour.IsActive && tour.CurrentNode != null)
            {
                tourText += " " + tour.CurrentNode.text;
                tour.Continue();
            }
            Assert.That(tourText, Does.Contain("Dormitory"));
            Assert.That(tourText, Does.Contain("Arcane Workshop"));
            Assert.That(tourText, Does.Contain("Main Academy Building"));
            Assert.That(tourText, Does.Contain("Library"));
            Assert.That(tourText, Does.Contain("forest"));

            DialogueSession farewell = runtime.StartDialogue("ambient_courtyard_student");
            Assert.That(farewell.SelectChoice("familiar"), Is.True);
            Assert.That(farewell.CurrentNode.text, Does.Contain("See you around"));
        }

        private static void AcceptAndComplete(GameEventRuntime runtime, string questId)
        {
            Assert.That(runtime.StartDialogue(questId + "_dialogue"), Is.Not.Null, questId);
            Assert.That(runtime.ActiveDialogue.SelectChoice("accept"), Is.True, questId);
            Assert.That(runtime.GetQuestStatus(questId), Is.EqualTo(QuestStatus.Active), questId);
            Assert.That(runtime.TriggerEvent("complete_" + questId).Succeeded, Is.True, questId);
            if (questId == "stella_first_light")
            {
                runtime.ActiveDialogue.End();
                Assert.That(runtime.StartDialogue(questId + "_dialogue"), Is.Not.Null, questId);
                Assert.That(runtime.ActiveDialogue.SelectChoice("report"), Is.True, questId);
            }
            Assert.That(runtime.GetQuestStatus(questId), Is.EqualTo(QuestStatus.Completed), questId);
        }

        private static List<string> ChoiceIds(DialogueSession session)
        {
            List<string> result = new List<string>();
            foreach (DialogueChoiceDefinition choice in session.AvailableChoices) result.Add(choice.id);
            return result;
        }
    }
}
