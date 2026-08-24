using Lumenfall.GameEvents;
using NUnit.Framework;
using UnityEngine;

namespace Lumenfall.Tests.GameEvents
{
    public sealed class GameEventRuntimeTests
    {
        [Test]
        public void NonRepeatableEvent_OnlyTriggersOnce()
        {
            GameEventRuntime runtime = CreateRuntime(new GameEventDatabaseDefinition
            {
                events = new[]
                {
                    new GameEventDefinition
                    {
                        id = "first_visit",
                        actions = new[]
                        {
                            new GameActionDefinition { type = "SetFlag", key = "visited", boolValue = true }
                        }
                    }
                }
            });

            Assert.That(runtime.TriggerEvent("first_visit").Succeeded, Is.True);
            Assert.That(runtime.GetFlag("visited"), Is.True);
            Assert.That(runtime.TriggerEvent("first_visit").Status,
                Is.EqualTo(GameEventTriggerStatus.AlreadyTriggered));
        }

        [Test]
        public void QuestLifecycleEvent_RunsWhenQuestCompletes()
        {
            GameEventRuntime runtime = CreateRuntime(new GameEventDatabaseDefinition
            {
                quests = new[]
                {
                    new QuestDefinition { id = "find_lantern", onCompletedEventId = "lantern_found" }
                },
                events = new[]
                {
                    new GameEventDefinition
                    {
                        id = "lantern_found",
                        actions = new[]
                        {
                            new GameActionDefinition { type = "SetFlag", key = "gate_open", boolValue = true }
                        }
                    }
                }
            });

            Assert.That(runtime.StartQuest("find_lantern"), Is.True);
            Assert.That(runtime.CompleteQuest("find_lantern"), Is.True);
            Assert.That(runtime.GetQuestStatus("find_lantern"), Is.EqualTo(QuestStatus.Completed));
            Assert.That(runtime.GetFlag("gate_open"), Is.True);
        }

        [Test]
        public void DialogueChoiceConditions_ExposeOnlyCurrentQuestBranch()
        {
            DialogueChoiceDefinition accept = new DialogueChoiceDefinition
            {
                id = "accept",
                text = "I will help.",
                nextNodeId = "accepted",
                conditions = new[]
                {
                    new GameConditionDefinition
                    {
                        type = "QuestStatus", key = "help_elara", value = "NotStarted"
                    }
                },
                actions = new[]
                {
                    new GameActionDefinition { type = "StartQuest", key = "help_elara" }
                }
            };

            GameEventRuntime runtime = CreateRuntime(new GameEventDatabaseDefinition
            {
                quests = new[] { new QuestDefinition { id = "help_elara" } },
                dialogues = new[]
                {
                    new DialogueDefinition
                    {
                        id = "elara",
                        startNodeId = "hello",
                        nodes = new[]
                        {
                            new DialogueNodeDefinition
                            {
                                id = "hello",
                                text = "Can you help?",
                                choices = new[] { accept }
                            },
                            new DialogueNodeDefinition { id = "accepted", text = "Thank you." }
                        }
                    }
                }
            });

            DialogueSession session = runtime.StartDialogue("elara");

            Assert.That(session.AvailableChoices.Count, Is.EqualTo(1));
            Assert.That(session.SelectChoice("accept"), Is.True);
            Assert.That(session.CurrentNode.id, Is.EqualTo("accepted"));
            Assert.That(runtime.GetQuestStatus("help_elara"), Is.EqualTo(QuestStatus.Active));
        }

        [Test]
        public void ExportAndImport_RestoresGlobalState()
        {
            GameEventDatabaseDefinition database = new GameEventDatabaseDefinition
            {
                quests = new[] { new QuestDefinition { id = "quest" } },
                events = new[] { new GameEventDefinition { id = "once" } }
            };
            GameEventRuntime source = CreateRuntime(database);
            source.SetFlag("door", true);
            source.SetCounter("petals", 3);
            source.StartQuest("quest");
            source.TriggerEvent("once");

            GameEventRuntime restored = CreateRuntime(database);
            Assert.That(restored.ImportStateJson(source.ExportStateJson()), Is.True);

            Assert.That(restored.GetFlag("door"), Is.True);
            Assert.That(restored.GetCounter("petals"), Is.EqualTo(3));
            Assert.That(restored.GetQuestStatus("quest"), Is.EqualTo(QuestStatus.Active));
            Assert.That(restored.HasEventTriggered("once"), Is.True);
        }

        [Test]
        public void Catalog_RejectsBrokenDialogueLinks()
        {
            GameEventCatalog catalog = new GameEventCatalog(new[]
            {
                new GameEventDatabaseDefinition
                {
                    dialogues = new[]
                    {
                        new DialogueDefinition
                        {
                            id = "broken",
                            startNodeId = "missing",
                            nodes = new[] { new DialogueNodeDefinition { id = "only_node" } }
                        }
                    }
                }
            });

            Assert.That(catalog.IsValid, Is.False);
        }

        [Test]
        public void ShippedSampleJson_ParsesAndHasValidReferences()
        {
            TextAsset sample = Resources.Load<TextAsset>("GameEvents/sample_events");
            Assert.That(sample, Is.Not.Null);
            Assert.That(
                GameEventCatalog.TryParseJson(
                    sample.text,
                    sample.name,
                    out GameEventDatabaseDefinition database,
                    out string error),
                Is.True,
                error);

            GameEventCatalog catalog = new GameEventCatalog(new[] { database });
            Assert.That(catalog.IsValid, Is.True, string.Join("\n", catalog.ValidationMessages));
            Assert.That(catalog.Events.ContainsKey("talk_to_elara"), Is.True);
            Assert.That(catalog.Quests.ContainsKey("moonpetal_remedy"), Is.True);
            Assert.That(catalog.Dialogues.ContainsKey("elara_conversation"), Is.True);
        }

        [Test]
        public void ShippedSample_CompletesNpcDialogueAndQuestFlow()
        {
            TextAsset sample = Resources.Load<TextAsset>("GameEvents/sample_events");
            Assert.That(
                GameEventCatalog.TryParseJson(
                    sample.text,
                    sample.name,
                    out GameEventDatabaseDefinition database,
                    out string error),
                Is.True,
                error);
            GameEventRuntime runtime = CreateRuntime(database);

            Assert.That(runtime.TriggerEvent("talk_to_elara").Succeeded, Is.True);
            Assert.That(runtime.ActiveDialogue.SelectChoice("accept_quest"), Is.True);
            Assert.That(runtime.GetQuestStatus("moonpetal_remedy"), Is.EqualTo(QuestStatus.Active));
            Assert.That(runtime.GetFlag("has_active_quest"), Is.True);

            for (int i = 0; i < 3; i++)
            {
                Assert.That(runtime.TriggerEvent("collect_moonpetal").Succeeded, Is.True);
            }

            Assert.That(runtime.TriggerEvent("turn_in_moonpetals").Succeeded, Is.True);
            Assert.That(runtime.GetQuestStatus("moonpetal_remedy"), Is.EqualTo(QuestStatus.Completed));
            Assert.That(runtime.GetFlag("has_active_quest"), Is.False);
            Assert.That(runtime.ActiveDialogue.Definition.id, Is.EqualTo("elara_quest_complete"));
        }

        private static GameEventRuntime CreateRuntime(GameEventDatabaseDefinition database)
        {
            return new GameEventRuntime(new GameEventCatalog(new[] { database }));
        }
    }
}
