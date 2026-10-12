using System.Linq;
using Lumenfall.GameEvents;
using NUnit.Framework;
using UnityEngine;

namespace Lumenfall.FeatureAcceptance
{
    [Category("PR03")]
    public sealed class Pr03FirstLightAcceptanceTests
    {
        private GameEventCatalog catalog;
        private GameEventRuntime runtime;
        private const string Quest = "stella_first_light";

        [SetUp] public void SetUp()
        {
            TextAsset asset = Resources.Load<TextAsset>("GameEvents/quest_npc_dialogue");
            Assert.That(asset, Is.Not.Null);
            Assert.That(GameEventCatalog.TryParseJson(asset.text, asset.name, out GameEventDatabaseDefinition data, out string error), Is.True, error);
            catalog = new GameEventCatalog(new[] { data });
            runtime = new GameEventRuntime(catalog); // Does not mutate the manager's live runtime.
        }

        private string[] Choices() => runtime.ActiveDialogue.AvailableChoices.Select(x => x.id).ToArray();

        [Test] public void LeavingInitialConversationDoesNotActivateOrCompleteTheLesson()
        {
            DialogueSession session = runtime.StartDialogue(Quest + "_dialogue");
            Assert.That(session.SelectChoice("leave"), Is.True);
            Assert.That(runtime.GetQuestStatus(Quest), Is.EqualTo(QuestStatus.NotStarted));
            Assert.That(runtime.TriggerEvent("complete_" + Quest).Succeeded, Is.False);
            runtime.StartDialogue(Quest + "_dialogue");
            Assert.That(Choices(), Does.Contain("accept"));
            Assert.That(Choices(), Does.Not.Contain("active"));
        }

        [Test] public void RestoredActiveLessonOffersReminderWithoutStartingAgain()
        {
            runtime.StartDialogue(Quest + "_dialogue").SelectChoice("accept");
            runtime.ActiveDialogue.End();
            var restored = new GameEventRuntime(catalog);
            Assert.That(restored.ImportStateJson(runtime.ExportStateJson(false)), Is.True);
            runtime = restored;
            runtime.StartDialogue(Quest + "_dialogue");
            Assert.That(Choices(), Does.Contain("active"));
            Assert.That(Choices(), Does.Not.Contain("accept"));
            Assert.That(runtime.GetQuestStatus(Quest), Is.EqualTo(QuestStatus.Active));
        }

        [Test] public void SavedCompletionKeepsEventConsumedAndUnlocksTheNextConversation()
        {
            runtime.StartDialogue(Quest + "_dialogue").SelectChoice("accept");
            runtime.ActiveDialogue.End();
            Assert.That(runtime.TriggerEvent("complete_" + Quest).Succeeded, Is.True);
            var restored = new GameEventRuntime(catalog);
            Assert.That(restored.ImportStateJson(runtime.ExportStateJson(false)), Is.True);
            Assert.That(restored.TriggerEvent("complete_" + Quest).Status, Is.EqualTo(GameEventTriggerStatus.AlreadyTriggered));
            restored.StartDialogue("stella_string_them_together_dialogue");
            Assert.That(restored.ActiveDialogue.AvailableChoices.Any(x => x.id == "accept"), Is.True);
            Assert.That(restored.GetQuestStatus("stella_string_them_together"), Is.EqualTo(QuestStatus.NotStarted));
        }

        [Test] public void ResetAfterCompletionRestoresFirstLightAndNextLessonGatesTogether()
        {
            runtime.StartQuest(Quest);
            runtime.TriggerEvent("complete_" + Quest);
            runtime.ResetState();
            Assert.That(runtime.GetQuestStatus(Quest), Is.EqualTo(QuestStatus.NotStarted));
            Assert.That(runtime.TriggerEvent("complete_" + Quest).Succeeded, Is.False);
            runtime.StartDialogue("stella_string_them_together_dialogue");
            Assert.That(Choices(), Does.Not.Contain("accept"));
            runtime.ActiveDialogue.End();
            runtime.StartDialogue(Quest + "_dialogue");
            Assert.That(Choices(), Does.Contain("accept"));
        }
    }
}
