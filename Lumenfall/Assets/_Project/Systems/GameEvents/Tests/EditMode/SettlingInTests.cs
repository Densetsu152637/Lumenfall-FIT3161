using System.Collections.Generic;
using System.Linq;
using Lumenfall.Cutscenes;
using Lumenfall.DataDriven;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Player;
using Lumenfall.Story;
using NUnit.Framework;
using UnityEngine;

namespace Lumenfall.Tests.GameEvents
{
    public sealed class SettlingInTests
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

        private static CutsceneDefinition Cutscene(string id)
        {
            TextAsset asset = Resources.Load<TextAsset>("Cutscenes/" + id);
            Assert.That(asset, Is.Not.Null, id);
            List<string> problems = new List<string>();
            CutsceneDefinition definition = CutsceneDefinition.Parse(asset.text, problems);
            Assert.That(problems, Is.Empty, id);
            return definition;
        }

        private static void CompleteChapterPuzzles(GameEventRuntime runtime)
        {
            foreach (string quest in SettlingInScene.ChapterQuestIds)
            {
                runtime.StartQuest(quest);
                runtime.CompleteQuest(quest);
            }
        }

        private static void AskEveryDormmate(GameEventRuntime runtime)
        {
            foreach ((string person, _) in SettlingInScene.Dormmates) runtime.SetFlag(SettlingInScene.KeyAskedFlag(person), true);
        }

        private static (bool turn, string dialogue, string cutscene) Turn(GameEventRuntime runtime, string person)
        {
            bool turn = SettlingInScene.TryGetStoryTurn(runtime, person, out string dialogue, out string cutscene);
            return (turn, dialogue, cutscene);
        }

        [Test]
        public void DormReceptionPartnersFaceEachOtherInTheirAuthoredPose()
        {
            GameObject dorm = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/World/Areas/Prefabs/Dormitory.prefab");
            DynamicNpc[] people = dorm.GetComponentsInChildren<DynamicNpc>(true);
            Transform rowan = people.Single(npc => npc.Definition.id == SettlingInScene.RowanNpcId).transform;
            Transform thistle = people.Single(npc => npc.Definition.id == SettlingInScene.ThistleNpcId).transform;
            Vector3 direction = thistle.position - rowan.position;
            direction.y = 0f;
            Assert.That(Vector3.Dot(rowan.forward, direction.normalized), Is.GreaterThan(0.99f));
            Assert.That(Vector3.Dot(thistle.forward, -direction.normalized), Is.GreaterThan(0.99f));
        }

        [Test]
        public void CachedRowanAndHisBlockerFollowFreshAndRestoredStoryState()
        {
            GameObject area = new GameObject("cached-dorm");
            try
            {
                GridWorld world = area.AddComponent<GridWorld>();
                world.ConfigureBounds(40, 30);
                GameObject actor = new GameObject("Rowan");
                actor.transform.SetParent(area.transform);
                actor.transform.position = new Vector3(12f, 0.25f, 6f);
                DynamicNpc rowan = actor.AddComponent<DynamicNpc>();
                rowan.Configure(new NpcDefinition { id = SettlingInScene.RowanNpcId, x = 12, y = 6 }, null);
                GridObstacle blocker = actor.AddComponent<GridObstacle>();
                blocker.Configure(world, Vector2Int.one);
                GameEventRuntime late = new GameEventRuntime(Events());
                late.SetFlag(SettlingInScene.ThoughtFlag, true);
                SettlingInScene.RefreshRowanPlacement(area, world, late);
                Assert.That(world.WorldToCell(actor.transform.position), Is.EqualTo(SettlingInScene.RowanHomelessCell));
                Assert.That(world.IsCellBlocked(SettlingInScene.RowanHomelessCell), Is.True);
                Assert.That(world.IsCellBlocked(new Vector2Int(12, 6)), Is.False);

                // The same cached objects survive New Game and earlier-save restoration.
                GameEventRuntime fresh = new GameEventRuntime(Events());
                SettlingInScene.RefreshRowanPlacement(area, world, fresh);
                Assert.That(world.WorldToCell(actor.transform.position), Is.EqualTo(new Vector2Int(12, 6)));
                Assert.That(world.IsCellBlocked(SettlingInScene.RowanHomelessCell), Is.False);
                Assert.That(world.IsCellBlocked(new Vector2Int(12, 6)), Is.True);
                Assert.That(Turn(fresh, SettlingInScene.RowanNpcId).turn, Is.False);
                Assert.That(actor.transform.position.y, Is.EqualTo(0.25f));

                fresh.SetFlag(SettlingInScene.ThoughtFlag, true);
                SettlingInScene.RefreshRowanPlacement(area, world, fresh);
                SettlingInScene.RefreshRowanPlacement(area, world, fresh);
                Assert.That(world.WorldToCell(actor.transform.position), Is.EqualTo(SettlingInScene.RowanHomelessCell));
                Assert.That(Turn(fresh, SettlingInScene.RowanNpcId).dialogue, Is.EqualTo(SettlingInScene.RowanHomelessDialogueId));
                Assert.That(world.IsCellBlocked(new Vector2Int(12, 6)), Is.False);
            }
            finally { Object.DestroyImmediate(area); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RowanWaitsUntilThePlayerLeavesHisDesiredCell(bool homeless)
        {
            GameObject area = new GameObject("dorm-placement");
            try
            {
                GridWorld world = area.AddComponent<GridWorld>();
                world.ConfigureBounds(40, 30);
                GameObject actor = new GameObject("Rowan");
                actor.transform.SetParent(area.transform);
                Vector2Int waiting = new Vector2Int(12, 6);
                Vector2Int source = homeless ? waiting : SettlingInScene.RowanHomelessCell;
                Vector2Int target = homeless ? SettlingInScene.RowanHomelessCell : waiting;
                actor.transform.position = world.CellToWorld(source);
                actor.AddComponent<DynamicNpc>().Configure(new NpcDefinition { id = SettlingInScene.RowanNpcId, x = waiting.x, y = waiting.y }, null);
                GameObject playerObject = new GameObject("player");
                playerObject.transform.SetParent(area.transform);
                GridMover player = playerObject.AddComponent<GridMover>();
                Assert.That(player.TryTeleportTo(world, target), Is.True);
                GameEventRuntime runtime = new GameEventRuntime(Events());
                runtime.SetFlag(SettlingInScene.ThoughtFlag, homeless);
                Assert.That(SettlingInScene.RefreshRowanPlacement(area, world, runtime, player), Is.False);
                Assert.That(world.WorldToCell(actor.transform.position), Is.EqualTo(source));
                Assert.That(player.TryTeleportTo(world, new Vector2Int(1, 1)), Is.True);
                Assert.That(SettlingInScene.RefreshRowanPlacement(area, world, runtime, player), Is.True);
                Assert.That(world.WorldToCell(actor.transform.position), Is.EqualTo(target));
            }
            finally { Object.DestroyImmediate(area); }
        }

        [Test]
        public void TheStoryRunsFromTheHeadmistressToTheRoomKey()
        {
            GameEventRuntime runtime = new GameEventRuntime(Events());

            // Before the headmistress: Marigold is herself, and Bann keeps her orientation lines rather than chatting.
            Assert.That(Turn(runtime, SettlingInScene.ReceptionistNpcId).turn, Is.False);
            Assert.That(Turn(runtime, SettlingInScene.HeadmistressNpcId), Is.EqualTo((true, (string)null, (string)null)));

            runtime.SetFlag(SettlingInScene.BannMetFlag, true);
            Assert.That(Turn(runtime, SettlingInScene.ReceptionistNpcId), Is.EqualTo((true, SettlingInScene.NotReadyDialogueId, (string)null)));
            Assert.That(Turn(runtime, SettlingInScene.HeadmistressNpcId).turn, Is.True, "Bann still reminds the player about the dorm.");

            runtime.SetFlag(SettlingInScene.NotReadyFlag, true);
            Assert.That(Turn(runtime, SettlingInScene.ReceptionistNpcId).turn, Is.False, "Marigold chats while the player explores.");
            Assert.That(Turn(runtime, SettlingInScene.HeadmistressNpcId).turn, Is.False, "Bann chats once the player has been to the dorm.");
            Assert.That(SettlingInScene.ShouldThinkRoomIsReady(runtime), Is.False);

            CompleteChapterPuzzles(runtime);
            Assert.That(SettlingInScene.ShouldThinkRoomIsReady(runtime), Is.False, "Every dormmate has to be asked about the key first.");
            AskEveryDormmate(runtime);
            Assert.That(SettlingInScene.ShouldThinkRoomIsReady(runtime), Is.True);

            runtime.SetFlag(SettlingInScene.ThoughtFlag, true);
            Assert.That(SettlingInScene.ShouldThinkRoomIsReady(runtime), Is.False, "The thought happens once.");
            Assert.That(Turn(runtime, SettlingInScene.ReceptionistNpcId), Is.EqualTo((true, (string)null, SettlingInScene.EndingCutsceneId)));

            runtime.SetFlag(SettlingInScene.KeyFlag, true);
            runtime.SetFlag(SettlingInScene.CompleteFlag, true);
            Assert.That(Turn(runtime, SettlingInScene.ReceptionistNpcId).turn, Is.False, "No further story once the chapter is over.");
            Assert.That(Turn(runtime, "rowan"), Is.EqualTo((true, SettlingInScene.RowanHomelessDialogueId, (string)null)),
                "Rowan still has no room after the chapter.");
        }

        [Test]
        public void OnceThePuzzlesAreDoneRowanIsHomelessAndThistleTriesNotToLaugh()
        {
            GameEventRuntime runtime = new GameEventRuntime(Events());
            runtime.SetFlag(SettlingInScene.NotReadyFlag, true);
            Assert.That(Turn(runtime, SettlingInScene.RowanNpcId).turn, Is.False, "Before then, Rowan chats as usual.");
            Assert.That(Turn(runtime, SettlingInScene.ThistleNpcId).turn, Is.False);

            runtime.SetFlag(SettlingInScene.ThoughtFlag, true);
            Assert.That(Turn(runtime, SettlingInScene.RowanNpcId), Is.EqualTo((true, SettlingInScene.RowanHomelessDialogueId, (string)null)));
            Assert.That(Turn(runtime, SettlingInScene.ThistleNpcId), Is.EqualTo((true, SettlingInScene.ThistleLaughingDialogueId, (string)null)));
            Assert.That(SettlingInScene.TryGetPrelude(runtime, SettlingInScene.RowanNpcId, out _), Is.False, "The desk argument is over by then.");

            GameEventCatalog events = Events();
            Assert.That(events.TryGetDialogue(SettlingInScene.RowanHomelessDialogueId, out DialogueDefinition rowan), Is.True);
            Assert.That(rowan.nodes.Select(node => node.speaker), Is.All.EqualTo("Rowan"));
            Assert.That(rowan.nodes.Last().text, Does.Contain("homeless"));
            Assert.That(events.TryGetDialogue(SettlingInScene.ThistleLaughingDialogueId, out DialogueDefinition thistle), Is.True);
            Assert.That(thistle.nodes.Select(node => node.speaker), Is.EqualTo(new[] { "{player}", "Marigold" }),
                "Thistle says nothing: the player only thinks about him, and Marigold tells him off.");
        }

        [Test]
        public void RowanAndThistleArguePastTheDeskTheFirstTimeOnly()
        {
            GameEventRuntime runtime = new GameEventRuntime(Events());
            foreach (string person in SettlingInScene.NoRoomsPeople)
            {
                Assert.That(SettlingInScene.TryGetPrelude(runtime, person, out string prelude), Is.True, person);
                Assert.That(prelude, Is.EqualTo(SettlingInScene.NoRoomsDialogueId));
            }
            Assert.That(SettlingInScene.TryGetPrelude(runtime, SettlingInScene.ReceptionistNpcId, out _), Is.False,
                "Marigold, who hands out the keys, is not one of them.");

            Assert.That(Events().TryGetDialogue(SettlingInScene.NoRoomsDialogueId, out DialogueDefinition scene), Is.True);
            Assert.That(scene.nodes.Select(node => node.speaker), Is.EqualTo(new[] { "Rowan", "Thistle", "Rowan", "Marigold" }));
            DialogueNodeDefinition first = scene.nodes.Single(node => node.id == scene.startNodeId);
            Assert.That(first.onEnterActions.Any(action => action.key == SettlingInScene.NoRoomsFlag && action.boolValue), Is.True);

            runtime.SetFlag(SettlingInScene.NoRoomsFlag, true);
            Assert.That(SettlingInScene.TryGetPrelude(runtime, "rowan", out _), Is.False, "It plays once.");
        }

        [Test]
        public void TheThoughtWaitsForEveryChapterPuzzle()
        {
            GameEventRuntime runtime = new GameEventRuntime(Events());
            runtime.SetFlag(SettlingInScene.NotReadyFlag, true);
            foreach (string quest in SettlingInScene.ChapterQuestIds.Skip(1))
            {
                runtime.StartQuest(quest);
                runtime.CompleteQuest(quest);
            }
            AskEveryDormmate(runtime);
            Assert.That(SettlingInScene.ShouldThinkRoomIsReady(runtime), Is.False, "One puzzle is still unsolved.");
        }

        [Test]
        public void EachDormmateIsAskedAboutTheKeyOnceTheirLastFavourIsDone()
        {
            GameEventRuntime runtime = new GameEventRuntime(Events());
            const string stella = "stella";
            string last = SettlingInScene.LastQuestOf(stella);
            runtime.StartQuest("stella_first_light");
            runtime.CompleteQuest("stella_first_light");
            runtime.StartQuest(last);
            runtime.CompleteQuest(last);
            Assert.That(SettlingInScene.ShouldAskAboutKey(runtime, stella), Is.False, "Before Marigold sends them, there is no key to ask about.");

            runtime.SetFlag(SettlingInScene.NotReadyFlag, true);
            Assert.That(SettlingInScene.ShouldAskAboutKey(runtime, stella), Is.True);
            Assert.That(SettlingInScene.ShouldAskAboutKey(runtime, "orio"), Is.False, "Orio's favour is not done yet.");
            Assert.That(SettlingInScene.ShouldAskAboutKey(runtime, SettlingInScene.RowanNpcId), Is.False, "Rowan is not a dormmate.");
            Assert.That(Turn(runtime, stella).turn, Is.False, "Her thanks for the favour comes before the question.");

            runtime.SetFlag(NpcIntroductions.QuestThankedFlag(last), true);
            Assert.That(Turn(runtime, stella), Is.EqualTo((true, SettlingInScene.KeyAskDialogueId(stella), (string)null)));

            // Her answer marks her as asked, and she goes back to her usual conversation.
            DialogueSession ask = runtime.StartDialogue(SettlingInScene.KeyAskDialogueId(stella));
            Assert.That(ask, Is.Not.Null);
            Assert.That(ask.SelectChoice("ask"), Is.True);
            Assert.That(runtime.GetFlag(SettlingInScene.KeyAskedFlag(stella)), Is.True);
            Assert.That(SettlingInScene.DormmatesAsked(runtime), Is.EqualTo(1));
            Assert.That(Turn(runtime, stella).turn, Is.False);
        }

        [Test]
        public void TheQuestionFollowsStraightOnFromADormmatesWrapUp()
        {
            GameEventRuntime runtime = new GameEventRuntime(Events());
            runtime.SetFlag(SettlingInScene.NotReadyFlag, true);
            CompleteChapterPuzzles(runtime);

            // Cinna Cirra's last favour ends in the ice trial with a line of theirs, not in a conversation with them.
            DialogueSession solved = runtime.StartDialogue("cinna_ice_trial_solved");
            Assert.That(solved, Is.Not.Null);
            solved.Continue();
            Assert.That(solved.IsCompleted, Is.True);
            Assert.That(SettlingInScene.KeyAskAfter(runtime, solved), Is.EqualTo("cinna_cirra"));

            runtime.SetFlag(SettlingInScene.KeyAskedFlag("cinna_cirra"), true);
            Assert.That(SettlingInScene.KeyAskAfter(runtime, solved), Is.Null, "Once asked, never again.");
        }

        [Test]
        public void EveryDormmateHasAKeyQuestionThatSetsTheirFlag()
        {
            GameEventCatalog events = Events();
            foreach ((string person, string quest) in SettlingInScene.Dormmates)
            {
                Assert.That(events.TryGetQuest(quest, out _), Is.True, quest);
                Assert.That(SettlingInScene.ChapterQuestIds, Has.Member(quest));
                Assert.That(events.TryGetDialogue(SettlingInScene.KeyAskDialogueId(person), out DialogueDefinition ask), Is.True, person);
                Assert.That(ask.nodes.SelectMany(node => node.onEnterActions).Any(action =>
                    action.key == SettlingInScene.KeyAskedFlag(person) && action.boolValue), Is.True, person);
                Assert.That(ask.nodes.Any(node => node.text.Contains("Someone else")), Is.True, person + " sends the player on to someone else.");
            }
            // Marigold says there are four of them, without naming any.
            events.TryGetDialogue(SettlingInScene.NotReadyDialogueId, out DialogueDefinition notReady);
            string said = string.Join(" ", notReady.nodes.Where(node => node.speaker == "Marigold").Select(node => node.text));
            Assert.That(said, Does.Contain("four"));
            Assert.That(said, Does.Not.Contain("Stella").And.Not.Contain("Cinna").And.Not.Contain("Orio").And.Not.Contain("Katrina"));
        }

        [Test]
        public void EveryChapterPuzzleIsARealQuest()
        {
            GameEventCatalog events = Events();
            foreach (string quest in SettlingInScene.ChapterQuestIds)
                Assert.That(events.TryGetQuest(quest, out _), Is.True, quest);
        }

        [Test]
        public void TheStoryDialoguesExistAndThePlayerOnlyThinks()
        {
            GameEventCatalog events = Events();
            foreach (string id in new[] { SettlingInScene.NotReadyDialogueId, "room_ready_thought", "dorm_room_ready", "chat_prof_bann",
                "dorm_key_glitch_ask", "dorm_key_glitch" })
            {
                Assert.That(events.TryGetDialogue(id, out DialogueDefinition dialogue), Is.True, id);
                foreach (DialogueNodeDefinition node in dialogue.nodes)
                    if (PlayerProfile.IsPlayerSpeaker(node.speaker))
                        Assert.That(node.text, Does.StartWith("<i>").And.EndWith("</i>"), $"{id}/{node.id} is the player's inner voice.");
            }
            events.TryGetDialogue(SettlingInScene.NotReadyDialogueId, out DialogueDefinition notReady);
            Assert.That(notReady.nodes.SelectMany(node => node.onEnterActions).Any(action => action.key == SettlingInScene.NotReadyFlag && action.boolValue), Is.True);
        }

        [Test]
        public void TheEndingGivesTheKeyWalksToTheDoorAndShowsTheEndOfTheChapter()
        {
            CutsceneDefinition ending = Cutscene(SettlingInScene.EndingCutsceneId);
            Assert.That(ending.once, Is.True);
            List<string> steps = ending.steps.Select(step => step.Kind switch
            {
                CutsceneStepKind.Dialogue => "dialogue:" + step.key,
                CutsceneStepKind.Flag => "flag:" + step.key,
                CutsceneStepKind.Caption => "caption:" + step.text,
                _ => step.Kind.ToString().ToLowerInvariant()
            }).Where(step => step != "wait").ToList();
            Assert.That(steps, Is.EqualTo(new[]
            {
                "dialogue:dorm_key_glitch_ask", "music", "dialogue:dorm_key_glitch", "camera", "stare", "glitch", "fade",
                "aura", "stare", "camerarelease", "music", "glitch", "fade",
                "dialogue:dorm_room_ready", "flag:" + SettlingInScene.KeyFlag, "walk", "fade", "caption:End of Chapter 1",
                "flag:" + SettlingInScene.CompleteFlag, "fade"
            }));
            // The aura is Marigold's and first shows on her last line, "Why didn't you ask for it?", together with her
            // name glitching; it is gone before the reset.
            Assert.That(Events().TryGetDialogue("dorm_key_glitch", out DialogueDefinition glitch), Is.True);
            DialogueNodeDefinition why = glitch.nodes.Last();
            Assert.That(why.text, Does.Contain("Why didn't you ask for it?"));
            foreach (DialogueNodeDefinition node in glitch.nodes)
            {
                bool last = node == why;
                Assert.That(node.onEnterActions.Any(action => action.type == CutscenePlayer.AuraActionType), Is.EqualTo(last), node.id);
                Assert.That(node.metadata.GetValue("speakerGlitch") == "true", Is.EqualTo(last), node.id);
            }
            GameActionDefinition aura = why.onEnterActions.Single(action => action.type == CutscenePlayer.AuraActionType);
            Assert.That((aura.key, aura.value), Is.EqualTo((SettlingInScene.ReceptionistNpcId, "full")));
            CutsceneStep off = ending.steps.Single(step => step.Kind == CutsceneStepKind.Aura);
            Assert.That((off.key, off.text), Is.EqualTo((SettlingInScene.ReceptionistNpcId, "off")));
            Assert.That(ending.steps.Where(step => step.Kind == CutsceneStepKind.Glitch).Select(step => step.text),
                Is.EqualTo(new[] { "tear", "rewind" }));
            Assert.That(ending.steps.Single(step => step.Kind == CutsceneStepKind.Stare && step.value).seconds, Is.GreaterThanOrEqualTo(4f),
                "The stare is slow.");

            // The walk ends in front of the room door whose sign then says it is the player's.
            CutsceneStep walk = ending.steps.Single(step => step.Kind == CutsceneStepKind.Walk);
            GameObject dormitory = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/World/Areas/Prefabs/Dormitory.prefab");
            BuildingSignInteraction door = dormitory.GetComponentsInChildren<BuildingSignInteraction>(true).Single(sign => sign.name == SettlingInScene.RoomDoorName);
            Vector3 local = dormitory.transform.InverseTransformPoint(door.transform.position);
            Assert.That(walk.Cell, Is.EqualTo(new Vector2Int(Mathf.RoundToInt(local.x), Mathf.FloorToInt(local.z))));

            CutsceneDefinition thought = Cutscene(SettlingInScene.ThoughtCutsceneId);
            Assert.That(thought.once, Is.True);
            Assert.That(thought.steps.Any(step => step.Kind == CutsceneStepKind.Flag && step.key == SettlingInScene.ThoughtFlag), Is.True);
        }
    }
}
