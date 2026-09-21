using System.Collections.Generic;
using Lumenfall.GameEvents;
using NUnit.Framework;
using UnityEngine;

namespace Lumenfall.Tests.GameEvents
{
    public sealed class QuestDialogueLifecycleTests
    {
        private static readonly string[] QuestIds =
        {
            "stella_first_light", "stella_string_them_together", "cinna_out_of_alignment",
            "cinna_cold_feet_warm_idea", "orio_patch_notes", "katrina_something_borrowed"
        };

        [Test]
        public void EveryQuestDialogueTraversesNotStartedActiveAndCompletedBranches()
        {
            GameEventRuntime runtime = CreateRuntime();
            foreach (string questId in QuestIds)
            {
                DialogueSession initial = runtime.StartDialogue(questId + "_dialogue");
                Assert.That(ChoiceIds(initial), Does.Contain("accept"), questId);
                Assert.That(ChoiceIds(initial), Does.Not.Contain("active"), questId);
                Assert.That(ChoiceIds(initial), Does.Not.Contain("done"), questId);

                Assert.That(initial.SelectChoice("accept"), Is.True, questId);
                Assert.That(runtime.GetQuestStatus(questId), Is.EqualTo(QuestStatus.Active), questId);

                DialogueSession active = runtime.StartDialogue(questId + "_dialogue");
                Assert.That(ChoiceIds(active), Does.Contain("active"), questId);
                Assert.That(ChoiceIds(active), Does.Not.Contain("accept"), questId);

                Assert.That(runtime.TriggerEvent("complete_" + questId).Succeeded, Is.True, questId);
                DialogueSession completed = runtime.StartDialogue(questId + "_dialogue");
                Assert.That(ChoiceIds(completed), Does.Contain("done"), questId);
                Assert.That(ChoiceIds(completed), Does.Not.Contain("accept"), questId);
                Assert.That(runtime.TriggerEvent("complete_" + questId).Status,
                    Is.EqualTo(GameEventTriggerStatus.AlreadyTriggered), questId);
            }
        }

        [Test]
        public void DependentQuestAcceptChoiceRemainsUnavailableUntilPrerequisiteCompletes()
        {
            GameEventRuntime runtime = CreateRuntime();
            DialogueSession stellaSecond = runtime.StartDialogue("stella_string_them_together_dialogue");
            Assert.That(ChoiceIds(stellaSecond), Does.Not.Contain("accept"));
            Assert.That(runtime.GetQuestStatus("stella_string_them_together"), Is.EqualTo(QuestStatus.NotStarted));

            StartAndComplete(runtime, "stella_first_light");
            stellaSecond = runtime.StartDialogue("stella_string_them_together_dialogue");
            Assert.That(stellaSecond.SelectChoice("accept"), Is.True);
            Assert.That(runtime.GetQuestStatus("stella_string_them_together"), Is.EqualTo(QuestStatus.Active));

            GameEventRuntime cinnaRuntime = CreateRuntime();
            DialogueSession cinnaSecond = cinnaRuntime.StartDialogue("cinna_cold_feet_warm_idea_dialogue");
            Assert.That(ChoiceIds(cinnaSecond), Does.Not.Contain("accept"));
            StartAndComplete(cinnaRuntime, "cinna_out_of_alignment");
            cinnaSecond = cinnaRuntime.StartDialogue("cinna_cold_feet_warm_idea_dialogue");
            Assert.That(cinnaSecond.SelectChoice("accept"), Is.True);
        }

        private static GameEventRuntime CreateRuntime()
        {
            TextAsset asset = Resources.Load<TextAsset>("GameEvents/quest_npc_dialogue");
            Assert.That(asset, Is.Not.Null);
            Assert.That(GameEventCatalog.TryParseJson(asset.text, asset.name,
                out GameEventDatabaseDefinition database, out string error), Is.True, error);
            GameEventCatalog catalog = new GameEventCatalog(new[] { database });
            Assert.That(catalog.IsValid, Is.True, string.Join("\n", catalog.ValidationMessages));
            return new GameEventRuntime(catalog);
        }

        private static void StartAndComplete(GameEventRuntime runtime, string questId)
        {
            DialogueSession dialogue = runtime.StartDialogue(questId + "_dialogue");
            Assert.That(dialogue.SelectChoice("accept"), Is.True, questId);
            Assert.That(runtime.TriggerEvent("complete_" + questId).Succeeded, Is.True, questId);
        }

        private static List<string> ChoiceIds(DialogueSession session)
        {
            List<string> result = new List<string>();
            foreach (DialogueChoiceDefinition choice in session.AvailableChoices) result.Add(choice.id);
            return result;
        }
    }
}
