using System.Collections.Generic;
using System.Linq;
using Lumenfall.Cutscenes;
using Lumenfall.DataDriven;
using Lumenfall.GameEvents;
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
            Assert.That(SettlingInScene.ShouldThinkRoomIsReady(runtime), Is.False, "One puzzle is still unsolved.");
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
            foreach (string id in new[] { SettlingInScene.NotReadyDialogueId, "room_ready_thought", "dorm_room_ready", "chat_prof_bann" })
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
                "dialogue:dorm_room_ready", "flag:" + SettlingInScene.KeyFlag, "walk", "fade", "caption:End of Chapter 1",
                "flag:" + SettlingInScene.CompleteFlag, "fade"
            }));

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
