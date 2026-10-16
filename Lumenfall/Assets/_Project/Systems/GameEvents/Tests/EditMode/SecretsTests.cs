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
            Assert.That(AnnoyingDogGate.ReplyToTry(runtime, out bool early), Is.Null, "The usual notice on the first try.");
            HashSet<string> replies = new HashSet<string>();
            for (int i = 2; i < AnnoyingDogGate.AwareAfterTries; i++)
            {
                string reply = AnnoyingDogGate.ReplyToTry(runtime, out bool stirring);
                Assert.That(reply, Does.StartWith("A dog obstructs the way.").And.Not.Contain("aware"),
                    "The notice hints that persistence pays, try " + i);
                Assert.That(replies.Add(reply), Is.True, "Each try builds on the last rather than repeating it, try " + i);
            }
            Assert.That(replies, Has.Some.Contain("lifted"), "The stirring builds towards lifting the dog.");
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
            Assert.That(SequenceBreakEnding.HintsFor(SecretRooms.ForgottenRoomId), Is.Empty, "The egg room is outside the sequence break.");
            Assert.That(SequenceBreakEnding.KnowsTooMuch(new GameEventRuntime(events), SecretRooms.ForgottenRoomId), Is.False,
                "Following the white dog's trail without Miki's story never ends the game.");
            Assert.That(events.TryGetDialogue(SequenceBreakEnding.KnowledgeDialogueId, out _), Is.True);

            // Each hint line records that it was heard.
            (string dialogue, string node, string flag)[] lines =
            {
                ("chat_luna", "t3", "hint.heard.lantern_shape"), ("chat_moss", "t3", "hint.heard.lantern_place"),
                ("chat_juno", "gossip_library", "hint.heard.closet_shelf"), ("chat_juno", "gossip_fountain", "hint.heard.closet_gossip"),
                ("chat_juno", "gossip_table", "hint.heard.closet_gossip"), ("chat_juno", "gossip_library", "hint.heard.closet_gossip"),
            };
            foreach ((string dialogue, string node, string flag) in lines)
                Assert.That(Node(events, dialogue, node).onEnterActions.Any(action => action.type == "SetFlag" && action.key == flag && action.boolValue), Is.True,
                    dialogue + "/" + node + " should set " + flag);
            // Hints can be heard again: each is a topic on the NPC's repeating "more" menu, not a one-time line.
            foreach ((string dialogue, string node, string flag) in lines)
                Assert.That(Node(events, dialogue, "more").choices.Any(choice => choice.nextNodeId == node), Is.True,
                    dialogue + "/" + node + " should be askable again from the menu.");
            Assert.That(Node(events, "chat_stella", "t6").text, Does.Contain("knocking out pi"), "Stella repeats the dorm-door hint.");
            Assert.That(Node(events, "chat_stella", "more").choices.Any(choice => choice.nextNodeId == "t6"), Is.True);
            Assert.That(Node(events, "chat_prof_voltaire", "t2").text, Does.Not.Contain("shelf"), "Juno gives the closet's hints now.");
            // Prof. Bann's stubborn-dog line is only flavour: it records no hint, so it has no part in the sequence break.
            Assert.That(Node(events, "chat_prof_bann", "t5").text, Does.Contain("stubborn"));
            Assert.That(Node(events, "chat_prof_bann", "t5").onEnterActions ?? System.Array.Empty<GameActionDefinition>(), Is.Empty);
        }

        [Test]
        public void EverySecretRoomHasAWayInAWayOutAndSomewhereToReturnTo()
        {
            string[] maps = AuthoredMaps();
            Assert.That(SecretRooms.All.Count(), Is.EqualTo(4));
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
        public void TheWhiteDogsTrailLeadsThroughTheGateToTheForgottenRoom()
        {
            GameEventRuntime runtime = new GameEventRuntime(Events());
            void Visit(params string[] maps) { foreach (string map in maps) WhiteDogTrailSecret.OnMapLoaded(runtime, map); }
            int Step() => runtime.GetCounter(WhiteDogTrailSecret.StepCounter);

            // Miki tells the story after a few chats.
            DialogueNodeDefinition hi = Node(Events(), "secret_dev_curls", "hi");
            DialogueChoiceDefinition story = hi.choices.Single(choice => choice.id == "dog");
            Assert.That(story.conditions.Single().key, Is.EqualTo("secret.miki.chats"));
            Assert.That(hi.onEnterActions.Any(action => action.type == "AddCounter" && action.key == "secret.miki.chats"), Is.True);
            string told = string.Join(" ", new[] { "dog1", "dog2", "dog3" }.Select(id => Node(Events(), "secret_dev_curls", id).text));
            foreach (string place in new[] { "white dog", "forest", "ice trial", "dorm", "arcane workshop", "library", "gate" })
                Assert.That(told, Does.Contain(place), "Miki's story mentions " + place);

            // Campus is the hub and does not count; the trail is forest, ice trial, dorm, workshop, library.
            Visit("westForest", "campus", "C03.IceTrial", "campus", "B01.Ground", "B01.Ground", "B02.Ground");
            Assert.That(Step(), Is.EqualTo(4));
            Visit("B03.Ground");
            Assert.That(Step(), Is.Zero, "A wrong area loses the trail.");
            Assert.That(WhiteDogTrailSecret.AtTheGate(runtime), Is.False);
            Visit("C03.IceTrial");
            Assert.That(Step(), Is.Zero, "The trail starts in the forest.");
            Visit("deepForest", "westForest", "C03.IceTrial", "B01.Ground", "B04.Ground");
            Assert.That(Step(), Is.Zero, "Skipping the workshop loses the trail.");
            Visit("westForest", "C03.IceTrial", "B01.Ground", "B02.Ground", "B04.Ground", "campus");
            Assert.That(WhiteDogTrailSecret.AtTheGate(runtime), Is.True);

            runtime.SetFlag(SecretRooms.VisitedFlag(SecretRooms.ForgottenRoomId), true);
            Assert.That(WhiteDogTrailSecret.AtTheGate(runtime), Is.False, "The room opens once.");
            Assert.That(WhiteDogTrailSecret.TryFollowAtGate(runtime), Is.False);
            Assert.That(SequenceBreakEnding.KnowsTooMuch(runtime, SecretRooms.ForgottenRoomId), Is.False, "It needs no hints.");
            // The man inside is dressed as an old man and talks through his own dialogue.
            Assert.That(DevChibis.Dressed, Does.Contain(DevChibis.OldMan));
            Assert.That(DevChibis.OldMan.FullBeard, Is.True);
            Assert.That(Events().TryGetDialogue(DevChibis.OldMan.DialogueId, out _), Is.True);
            // He is baked into a prefab with saved materials, so the room does not dress him at runtime.
            GameObject baked = SecretRoomArt.Load()?.forgottenMan;
            Assert.That(baked, Is.Not.Null, "Open Unity once (or run Lumenfall/Secrets/Bake Forgotten Man) and commit the baked prefab.");
            Assert.That(baked.transform.Find("Dev Dressing"), Is.Not.Null, "The baked man is already dressed.");
            foreach (Renderer part in baked.GetComponentsInChildren<Renderer>(true))
                foreach (Material material in part.sharedMaterials)
                    if (material != null) Assert.That(UnityEditor.EditorUtility.IsPersistent(material), Is.True, part.name + " uses a saved material.");
            Assert.That(SecretRooms.TryGetRoom(SecretRooms.ForgottenRoomId, out SecretRooms.Room room) && room.ReturnCell == AnnoyingDogGate.GateCell + Vector2Int.down, Is.True,
                "Leaving the room returns the player to the north gate.");
        }

        [Test]
        public void EverySecretGiftIsACurioTheGiverHandsOverOnce()
        {
            GameEventCatalog events = Events();
            GameEventRuntime runtime = new GameEventRuntime(events);
            string[] givers = { "secret_dev", "secret_dog", "secret_janitor", "secret_lantern_spirit", "secret_forgotten_man" };
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
