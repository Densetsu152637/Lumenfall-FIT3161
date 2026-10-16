using System.Collections.Generic;
using System.Linq;
using Lumenfall.DataDriven;
using Lumenfall.GameEvents;
using Lumenfall.Spells;
using Lumenfall.Story;
using NUnit.Framework;
using UnityEngine;

namespace Lumenfall.Tests.GameEvents
{
    public sealed class SecretsTests
    {
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

        private static string[] AuthoredMaps() => UnityEditor.AssetDatabase.FindAssets("t:WorldAreaAsset")
            .Select(guid => UnityEditor.AssetDatabase.LoadAssetAtPath<WorldAreaAsset>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid)))
            .Where(area => area != null && area.definition != null).Select(area => area.definition.id).ToArray();

        private static DialogueNodeDefinition Node(GameEventCatalog events, string dialogueId, string nodeId)
        {
            Assert.That(events.TryGetDialogueNode(dialogueId, nodeId, out DialogueNodeDefinition node), Is.True, dialogueId + "/" + nodeId);
            return node;
        }

        [Test]
        public void KnockingOutPiOnTheDormDoorsOpensTheDevsRoom()
        {
            Assert.That(DormKnockSecret.DoorNumber("Room Door 3"), Is.EqualTo(3));
            Assert.That(DormKnockSecret.DoorNumber("Sign-Library"), Is.EqualTo(0));
            List<int> knocks = new List<int>();
            Assert.That(DormKnockSecret.Knock(knocks, 1), Is.False);
            Assert.That(DormKnockSecret.Knock(knocks, 3), Is.False);
            Assert.That(DormKnockSecret.Knock(knocks, 1), Is.False);
            Assert.That(DormKnockSecret.Knock(knocks, 4), Is.True, "Only the latest three knocks count.");
            knocks.Clear();
            foreach (int door in new[] { 3, 1, 5 }) Assert.That(DormKnockSecret.Knock(knocks, door), Is.False);
        }

        [Test]
        public void StellaHintsAtTheKnock()
        {
            Assert.That(Node(Events(), "intro_stella", "dormmates").text, Does.Contain("pi"));
        }

        [Test]
        public void EachRightStepFromTheLandingTileCountsAndAWrongOneStartsOver()
        {
            Vector2Int cell = LanternBearerSecret.StartCell;
            int done = 0;
            foreach (Vector2Int step in LanternBearerSecret.Steps)
            {
                done = LanternBearerSecret.Advance(done, cell, step, out bool wrong);
                Assert.That(wrong, Is.False);
                cell += step;
            }
            Assert.That(done, Is.EqualTo(LanternBearerSecret.Steps.Length), "The whole shape is walked.");

            Assert.That(LanternBearerSecret.Advance(0, LanternBearerSecret.StartCell + Vector2Int.right, Vector2Int.up, out bool elsewhere), Is.EqualTo(0),
                "The walk only begins on the landing tile.");
            Assert.That(elsewhere, Is.False, "Walking about elsewhere is not a wrong step.");
            Assert.That(LanternBearerSecret.Advance(2, cell, Vector2Int.left, out bool broken), Is.EqualTo(0));
            Assert.That(broken, Is.True, "A wrong step part-way through thuds and starts over.");
            Assert.That(LanternBearerSecret.Advance(LanternBearerSecret.Steps.Length, cell, Vector2Int.up, out bool after), Is.EqualTo(0));
            Assert.That(after, Is.True, "Stepping off the finished shape breaks it too.");
            Assert.That(LanternBearerSecret.StoneCells.Length, Is.EqualTo(7), "A stone to start on and one for each step.");
            Assert.That(LanternBearerSecret.StoneCells.Last(), Is.EqualTo(cell), "The last stone is where the walk ends.");
            Assert.That(Enumerable.Range(0, 7).Select(LanternBearerSecret.StoneId), Is.Unique);
        }

        [Test]
        public void TheWalkStartsJustUpThePathFromWhereThePlayerWakes()
        {
            WorldAreaAsset forest = UnityEditor.AssetDatabase.FindAssets("t:WorldAreaAsset")
                .Select(guid => UnityEditor.AssetDatabase.LoadAssetAtPath<WorldAreaAsset>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid)))
                .Single(area => area != null && area.definition != null && area.definition.id == LanternBearerSecret.MapId);
            Vector2Int spawn = new Vector2Int(forest.definition.spawn.x, forest.definition.spawn.y);
            Assert.That(LanternBearerSecret.StartCell.x, Is.LessThan(spawn.x), "The stones are near where the player wakes, a place they have already been.");
        }

        [Test]
        public void LunasConstellationIsTheShapeToWalk()
        {
            string line = Node(Events(), "chat_luna", "t3").text;
            string shape = string.Join(", ", LanternBearerSecret.Steps.Select(step => step == Vector2Int.up ? "up" : "right"));
            Assert.That(line, Does.Contain(shape));
            Assert.That(Node(Events(), "chat_moss", "t3").text, Does.Contain("places you've already been"), "Moss hints that the stars are somewhere familiar.");
            Assert.That(line, Does.Contain("lowest star"), "Luna says where the shape starts.");
        }

        [Test]
        public void TheDogNoticesOnTheTenthTryThenVisAndRotaraMoveIt()
        {
            GameEventRuntime runtime = new GameEventRuntime(Events());
            for (int i = 1; i < 4; i++)
                Assert.That(AnnoyingDogGate.ReplyToTry(runtime, out bool early), Is.Null, "The usual notice, try " + i);
            for (int i = 4; i < AnnoyingDogGate.AwareAfterTries; i++)
                Assert.That(AnnoyingDogGate.ReplyToTry(runtime, out bool stirring), Does.StartWith("A dog obstructs the way.").And.Not.Contain("aware"),
                    "The notice hints that persistence pays, try " + i);
            Assert.That(AnnoyingDogGate.ReplyToTry(runtime, out bool opens), Does.Contain("aware").And.Contain("lift"), "Waking hints at Vis.");
            Assert.That(opens, Is.False);
            Assert.That(runtime.GetFlag(AnnoyingDogGate.AwareFlag), Is.True);
            runtime.SetFlag(AnnoyingDogGate.LiftedFlag, true);
            Assert.That(AnnoyingDogGate.ReplyToTry(runtime, out opens), Does.Contain("floats").And.Contain("turn"), "Lifted hints at Rotara.");
            Assert.That(opens, Is.False);
            runtime.SetFlag(AnnoyingDogGate.MovedFlag, true);
            AnnoyingDogGate.ReplyToTry(runtime, out opens);
            Assert.That(opens, Is.True);
        }

        [Test]
        public void TheDogsRoomCannotBeEnteredTwice()
        {
            GameEventRuntime runtime = new GameEventRuntime(Events());
            runtime.SetFlag(AnnoyingDogGate.AwareFlag, true);
            runtime.SetFlag(AnnoyingDogGate.LiftedFlag, true);
            runtime.SetFlag(AnnoyingDogGate.MovedFlag, true);
            AnnoyingDogGate.ReplyToTry(runtime, out bool opens);
            Assert.That(opens, Is.True);
            Assert.That(SecretRooms.HasVisited(runtime, SecretRooms.DogRoomId), Is.False);
            runtime.SetFlag(SecretRooms.VisitedFlag(SecretRooms.DogRoomId), true);
            Assert.That(AnnoyingDogGate.ReplyToTry(runtime, out opens), Does.Contain("back into the way"));
            Assert.That(opens, Is.False, "Once visited, the gate stays shut.");
        }

        [Test]
        public void EveryRoomHasItsOwnVisitedFlag()
        {
            Assert.That(SecretRooms.All.Select(room => SecretRooms.VisitedFlag(room.Id)), Is.Unique);
            Assert.That(SecretRooms.HasVisited(null, SecretRooms.DevRoomId), Is.False);
        }

        [Test]
        public void NickIsTheHalfAwakeDev()
        {
            Assert.That(Node(Events(), "secret_dev", "found").speaker, Is.EqualTo("Nick"));
        }

        [Test]
        public void EveryDevAtADeskHasSomethingToSay()
        {
            GameEventCatalog events = Events();
            Assert.That(DevChibis.All.Select(look => look.Id), Is.Unique);
            foreach (DevChibis.Look look in DevChibis.All)
            {
                Assert.That(events.TryGetDialogue(look.DialogueId, out DialogueDefinition dialogue), Is.True, look.Id);
                Assert.That(dialogue.nodes.All(node => node.speaker == look.Name || node.speaker == "Choice" || string.IsNullOrEmpty(node.speaker)), Is.True,
                    look.DialogueId + " should be spoken by " + look.Name);
            }
        }


        [Test]
        public void TheStaffClosetNeedsAllThreeSpellsInTheRightPlaces()
        {
            GameEventRuntime runtime = new GameEventRuntime(Events());
            StaffClosetSecret.Lock fountain = StaffClosetSecret.Locks.Single(item => item.Spell == SpellId.Glaciate);
            Assert.That(fountain.Answers(SpellId.Glaciate, "campus", new Vector2Int(26, 42)), Is.True, "Glaciate from beside the fountain.");
            Assert.That(fountain.Answers(SpellId.Glaciate, "campus", new Vector2Int(10, 10)), Is.False, "Not from across campus.");
            Assert.That(fountain.Answers(SpellId.Rotara, "campus", new Vector2Int(26, 42)), Is.False, "Only Glaciate.");
            Assert.That(StaffClosetSecret.Locks.Select(item => item.Flag), Is.Unique);
            string[] maps = AuthoredMaps();
            foreach (StaffClosetSecret.Lock item in StaffClosetSecret.Locks)
            {
                Assert.That(maps, Has.Member(item.MapId), item.Flag);
                Assert.That(StaffClosetSecret.AllUnlocked(runtime), Is.False);
                runtime.SetFlag(item.Flag, true);
            }
            Assert.That(StaffClosetSecret.AllUnlocked(runtime), Is.True);
        }

        [Test]
        public void JunosGossipGoesRoundTheThreeSpellSpotsInTurn()
        {
            GameEventRuntime runtime = new GameEventRuntime(Events());
            string[] expected = { "fountain", "table", "Voltaire", "fountain" };
            foreach (string place in expected)
            {
                DialogueSession chat = runtime.StartDialogue("chat_juno");
                DialogueChoiceDefinition[] gossip = chat.AvailableChoices.Where(choice => choice.text == "Got any gossip?").ToArray();
                Assert.That(gossip.Length, Is.EqualTo(1), "Exactly one piece of gossip is on offer at a time.");
                Assert.That(chat.SelectChoice(gossip[0].id), Is.True);
                Assert.That(chat.CurrentNode.text, Does.Contain(place));
                chat.End();
            }
        }

        [Test]
        public void OnePuzzleEarlyIsFineButEveryPuzzleBeforeTheDormBreaksTheStory()
        {
            GameEventRuntime runtime = new GameEventRuntime(Events());
            runtime.StartQuest("stella_first_light");
            runtime.CompleteQuest("stella_first_light");
            Assert.That(SequenceBreakEnding.SolvedEverythingFirst(runtime), Is.False, "Solving one puzzle early is allowed.");
            foreach (string quest in SettlingInScene.ChapterQuestIds)
            {
                if (runtime.GetQuestStatus(quest) == QuestStatus.NotStarted) runtime.StartQuest(quest);
                if (runtime.GetQuestStatus(quest) == QuestStatus.Active) runtime.CompleteQuest(quest);
            }
            Assert.That(SequenceBreakEnding.SolvedEverythingFirst(runtime), Is.True, "Every puzzle, and never a step inside the dorm.");
            runtime.SetFlag(SequenceBreakEnding.DormVisitedFlag, true);
            Assert.That(SequenceBreakEnding.SolvedEverythingFirst(runtime), Is.False, "Having been to the dorm at all rules it out.");
            Assert.That(Events().TryGetDialogue(SequenceBreakEnding.DialogueId, out _), Is.True);
        }

        [Test]
        public void FinishingASecretWithoutItsHintsMeansYouKnowTooMuch()
        {
            GameEventCatalog events = Events();
            GameEventRuntime runtime = new GameEventRuntime(events);
            string[] secrets = { SecretRooms.DevRoomId, SequenceBreakEnding.LanternBearerSecretId, SecretRooms.ClosetId };
            foreach (string secret in secrets)
            {
                Assert.That(SequenceBreakEnding.KnowsTooMuch(runtime, secret), Is.True, secret + " without its hints.");
                foreach (string hint in SequenceBreakEnding.HintsFor(secret)) runtime.SetFlag(hint, true);
                Assert.That(SequenceBreakEnding.KnowsTooMuch(runtime, secret), Is.False, secret + " once every hint has been heard.");
            }
            Assert.That(SequenceBreakEnding.KnowsTooMuch(new GameEventRuntime(events), SecretRooms.DogRoomId), Is.False,
                "The dog's hints are the gate's own notices.");
            Assert.That(events.TryGetDialogue(SequenceBreakEnding.KnowledgeDialogueId, out _), Is.True);

            // Each hint line records that it was heard.
            (string dialogue, string node, string flag)[] lines =
            {
                ("chat_luna", "t3", "hint.heard.lantern_shape"), ("chat_moss", "t3", "hint.heard.lantern_place"),
                ("chat_prof_voltaire", "t2", "hint.heard.closet_shelf"), ("chat_juno", "gossip_fountain", "hint.heard.closet_gossip"),
                ("chat_juno", "gossip_table", "hint.heard.closet_gossip"), ("chat_juno", "gossip_library", "hint.heard.closet_gossip"),
            };
            foreach ((string dialogue, string node, string flag) in lines)
                Assert.That(Node(events, dialogue, node).onEnterActions.Any(action => action.type == "SetFlag" && action.key == flag && action.boolValue), Is.True,
                    dialogue + "/" + node + " should set " + flag);
        }

        [Test]
        public void EverySecretRoomHasAWayInAWayOutAndSomewhereToReturnTo()
        {
            string[] maps = AuthoredMaps();
            Assert.That(SecretRooms.All.Count(), Is.EqualTo(3));
            foreach (SecretRooms.Room room in SecretRooms.All)
            {
                RectInt bounds = new RectInt(0, 0, room.Width, room.Height);
                Assert.That(bounds.Contains(room.Arrival) && bounds.Contains(room.Exit), Is.True, room.Id);
                Assert.That(room.Arrival, Is.Not.EqualTo(room.Exit), room.Id + ": arriving must not leave at once.");
                Assert.That(maps, Has.Member(room.ReturnMapId), room.Id);
                Assert.That(maps, Has.No.Member(room.Id), "A secret room's id must not clash with an authored map.");
            }
        }

        [Test]
        public void EverySecretGiftIsACurioTheGiverHandsOverOnce()
        {
            GameEventCatalog events = Events();
            GameEventRuntime runtime = new GameEventRuntime(events);
            string[] givers = { "secret_dev", "secret_dog", "secret_janitor", "secret_lantern_spirit" };
            List<string> given = new List<string>();
            foreach (string giver in givers)
            {
                Assert.That(events.TryGetDialogue(giver, out DialogueDefinition dialogue), Is.True, giver);
                DialogueChoiceDefinition take = dialogue.nodes.SelectMany(node => node.choices).Single(choice => choice.id == "take");
                string key = take.actions.Single(action => action.type == "TriggerEvent").key.Substring(MiscCollectibles.FoundPrefix.Length);
                given.Add(key);
                Assert.That(MiscCollectibles.TryGetText(runtime, key, out string name, out string description), Is.True, key);
                Assert.That(description, Is.Not.Empty, key);

                // Their chat afterwards offers it again only until it has been taken.
                string person = dialogue.metadata.Single(entry => entry.key == "npcId").value;
                DialogueNodeDefinition hello = Node(events, NpcIntroductions.ChatDialogueId(person), "hello");
                DialogueChoiceDefinition again = hello.choices.Single(choice => choice.id == "take");
                Assert.That(again.conditions.Single().key, Is.EqualTo(MiscCollectibles.FlagFor(key)));
                Assert.That(again.conditions.Single().negate, Is.True);
            }
            Assert.That(given, Is.EquivalentTo(MiscCollectibles.SecretRewards));

            const string coffee = "cold_cup_of_coffee";
            Assert.That(runtime.TriggerEvent(MiscCollectibles.FoundId(coffee)).Succeeded, Is.True);
            Assert.That(MiscCollectibles.IsFound(runtime, coffee), Is.True);
            Assert.That(MiscCollectibles.JournalKeys, Has.Member(coffee));
        }
    }
}
