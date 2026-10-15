using System;
using System.Collections.Generic;
using System.Linq;
using Lumenfall.DataDriven;
using Lumenfall.GameEvents;
using Lumenfall.Player;
using Lumenfall.Story;
using NUnit.Framework;
using UnityEngine;

namespace Lumenfall.Tests.GameEvents
{
    public sealed class NpcIntroductionsTests
    {
        // Their first meeting is scripted, so it is written into that scene rather than into introductions.json.
        private static readonly Dictionary<string, (string dialogue, string node)> ScriptedMeetings =
            new Dictionary<string, (string, string)>
            {
                { "prof_kepler", ("kepler_late", "late") },
                { "campus_student", ("kit_lay_of_the_land", "intro") }
            };

        private static NpcDefinition[] Records()
        {
            NpcCatalogAsset catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<NpcCatalogAsset>("Assets/_Project/World/Areas/NpcCatalog.asset");
            Assert.That(catalog, Is.Not.Null);
            return catalog.npcs;
        }

        private static GameEventCatalog Events()
        {
            List<GameEventDatabaseDefinition> databases = new List<GameEventDatabaseDefinition>();
            foreach (TextAsset asset in Resources.LoadAll<TextAsset>("GameEvents"))
            {
                Assert.That(GameEventCatalog.TryParseJson(asset.text, asset.name, out GameEventDatabaseDefinition database, out string error), Is.True, error);
                databases.Add(database);
            }
            return new GameEventCatalog(databases);
        }

        private static IEnumerable<string> People() =>
            Records().Select(npc => NpcIntroductions.PersonId(npc.id)).Distinct()
                .Where(person => !NpcIntroductions.AlwaysKnown.Contains(person) && !ScriptedMeetings.ContainsKey(person));

        [Test]
        public void TheHeadmistressIsTheOnlyOneKnownFromTheStart()
        {
            Assert.That(NpcIntroductions.AlwaysKnown, Is.EquivalentTo(new[] { "prof_bann" }));
        }

        [Test]
        public void EveryCinnaRecordIsOnePerson()
        {
            Assert.That(NpcIntroductions.PersonId("cinna_cirra_orientation"), Is.EqualTo("cinna_cirra"));
            Assert.That(NpcIntroductions.PersonId("cinna_cirra_trial"), Is.EqualTo("cinna_cirra"));
            Assert.That(NpcIntroductions.MetFlag("cinna_cirra_trial"), Is.EqualTo("npc.met.cinna_cirra"));
            Assert.That(NpcIntroductions.PersonId("vex"), Is.EqualTo("vex"));
        }

        [Test]
        public void EveryoneElseIntroducesThemselvesAndAsksThePlayersName()
        {
            GameEventCatalog events = Events();
            foreach (string person in People())
            {
                string id = NpcIntroductions.IntroDialogueId(person);
                Assert.That(events.TryGetDialogue(id, out DialogueDefinition intro), Is.True, $"'{person}' has no {id} dialogue.");
                DialogueNodeDefinition first = intro.nodes.Single(node => node.id == intro.startNodeId);
                Assert.That(first.onEnterActions.Any(action => action.type == "SetFlag" && action.key == NpcIntroductions.MetFlag(person) && action.boolValue),
                    Is.True, $"{id} should mark {person} as met on the line where they give their name.");

                string name = Records().First(npc => NpcIntroductions.PersonId(npc.id) == person).displayName;
                string firstName = name.Split(' ').Last();
                Assert.That(first.text, Does.Contain(firstName), $"{id} should say their name.");
                Assert.That(intro.nodes.SelectMany(node => node.choices).Any(choice => choice.text.Contains(PlayerProfile.Token)), Is.True,
                    $"{id} should let the player give their name.");
                Assert.That(intro.nodes.Any(node => node.text.Contains(PlayerProfile.Token)), Is.True, $"{id} should use the player's name once given.");
                Assert.That(intro.nodes.All(node => !PlayerProfile.IsPlayerSpeaker(node.speaker)), Is.True,
                    $"{id}: the player answers through a choice, not a line of inner voice.");
            }
        }

        [Test]
        public void ScriptedFirstMeetingsMarkThePersonMetWhereTheyGiveTheirName()
        {
            GameEventCatalog events = Events();
            foreach (KeyValuePair<string, (string dialogue, string node)> meeting in ScriptedMeetings)
            {
                Assert.That(events.TryGetDialogue(meeting.Value.dialogue, out DialogueDefinition dialogue), Is.True, meeting.Value.dialogue);
                DialogueNodeDefinition node = dialogue.nodes.Single(candidate => candidate.id == meeting.Value.node);
                Assert.That(node.onEnterActions.Any(action => action.type == "SetFlag" && action.key == NpcIntroductions.MetFlag(meeting.Key)), Is.True,
                    $"{meeting.Value.dialogue}/{meeting.Value.node} should mark {meeting.Key} as met.");
                Assert.That(events.TryGetDialogue(NpcIntroductions.IntroDialogueId(meeting.Key), out _), Is.False,
                    $"{meeting.Key} is introduced by their scene, so a second introduction would repeat it.");
            }
        }

        [Test]
        public void KitAsksForThePlayersNameBeforeTheTour()
        {
            Events().TryGetDialogue("kit_lay_of_the_land", out DialogueDefinition tour);
            List<string> order = new List<string>();
            string next = tour.startNodeId;
            while (!string.IsNullOrEmpty(next) && order.Count < 50)
            {
                order.Add(next);
                DialogueNodeDefinition node = tour.nodes.Single(candidate => candidate.id == next);
                next = node.choices.Length > 0 ? node.choices[0].nextNodeId : node.nextNodeId;
            }
            Assert.That(order.Take(4), Is.EqualTo(new[] { "intro", "ask", "pleased", "courtyard" }));
        }

        [Test]
        public void EveryoneIntroducedHasAGeneralConversationThatEndsOnlyWithGoodbye()
        {
            GameEventCatalog events = Events();
            foreach (string person in People())
            {
                string id = NpcIntroductions.ChatDialogueId(person);
                Assert.That(events.TryGetDialogue(id, out DialogueDefinition chat), Is.True, $"'{person}' has no {id} dialogue.");
                DialogueNodeDefinition hello = chat.nodes.Single(node => node.id == chat.startNodeId);
                Assert.That(hello.text, Does.Contain(PlayerProfile.Token), $"{id} should greet the player by name.");
                Assert.That(hello.choices.Length, Is.GreaterThanOrEqualTo(3), $"{id} should offer a few things to talk about.");

                Dictionary<string, DialogueNodeDefinition> nodes = chat.nodes.ToDictionary(node => node.id);
                // Each character says goodbye in their own words, so the goodbye is found by its id.
                DialogueChoiceDefinition bye = hello.choices.Single(choice => choice.id == "bye");
                // A quest topic hands over to the quest's own dialogue, and is only offered while that quest is under way.
                foreach (DialogueChoiceDefinition task in hello.choices.Where(choice => choice.actions.Length > 0))
                {
                    GameActionDefinition handOver = task.actions.Single();
                    Assert.That(handOver.type, Is.EqualTo("StartDialogue"), $"{id}/{task.id}");
                    Assert.That(events.TryGetDialogue(handOver.key, out _), Is.True, $"{id}/{task.id} starts {handOver.key}");
                    Assert.That(task.conditions.Single().value, Is.EqualTo("Active"), $"{id}/{task.id}");
                }
                foreach (DialogueChoiceDefinition topic in hello.choices.Where(choice => choice != bye && choice.actions.Length == 0))
                {
                    DialogueNodeDefinition answer = nodes[topic.nextNodeId];
                    DialogueNodeDefinition next = nodes[answer.nextNodeId];
                    Assert.That(next.choices.Select(choice => choice.text), Is.EqualTo(hello.choices.Select(choice => choice.text)),
                        $"{id}/{topic.id} should come back to the same topics.");
                }
                Assert.That(nodes[bye.nextNodeId].nextNodeId, Is.Empty, $"{id}: goodbye ends the conversation.");
            }
        }

        [Test]
        public void AnUnnamedPlayerIsAskedForTheirNameFirst()
        {
            TextAsset asset = Resources.Load<TextAsset>("Cutscenes/" + NpcIntroductions.AskNameCutsceneId);
            Assert.That(asset, Is.Not.Null);
            Lumenfall.Cutscenes.CutsceneDefinition definition = Lumenfall.Cutscenes.CutsceneDefinition.Parse(asset.text, new List<string>());
            Assert.That(definition.steps.Select(step => step.Kind), Is.EqualTo(new[] { Lumenfall.Cutscenes.CutsceneStepKind.Name }));
            Assert.That(definition.once, Is.False, "It can be needed more than once.");
        }
    }
}
