using System;
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
    public sealed class LayOfTheLandTests
    {
        private static NpcDefinition Record(string id)
        {
            NpcCatalogAsset catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<NpcCatalogAsset>("Assets/_Project/World/Areas/NpcCatalog.asset");
            Assert.That(catalog, Is.Not.Null);
            NpcDefinition record = Array.Find(catalog.npcs, npc => npc.id == id);
            Assert.That(record, Is.Not.Null, id);
            return record;
        }

        [Test]
        public void KitCallsOnArrivalThenGivesTheTourOnce()
        {
            GameEventRuntime runtime = new GameEventRuntime(new GameEventCatalog(Array.Empty<GameEventDatabaseDefinition>()));
            Assert.That(LayOfTheLandScene.ShouldCall(runtime), Is.False, "Not before Kepler has sent the player through.");
            Assert.That(LayOfTheLandScene.ShouldGiveTour(runtime), Is.False);

            runtime.SetFlag(LateToOrientationScene.MetFlag, true);
            Assert.That(LayOfTheLandScene.ShouldCall(runtime), Is.True);
            Assert.That(LayOfTheLandScene.ShouldGiveTour(runtime), Is.False, "The tour waits for the call.");

            runtime.SetFlag(LayOfTheLandScene.CalledFlag, true);
            Assert.That(LayOfTheLandScene.ShouldCall(runtime), Is.False);
            Assert.That(LayOfTheLandScene.ShouldGiveTour(runtime), Is.True);

            runtime.SetFlag(LayOfTheLandScene.MetFlag, true);
            Assert.That(LayOfTheLandScene.ShouldGiveTour(runtime), Is.False, "Only once.");
        }

        [Test]
        public void KitIsTheStudentAtTheEntranceAndBannWaitsByTheFountain()
        {
            NpcDefinition kit = Record(LayOfTheLandScene.KitNpcId);
            Assert.That(kit.displayName, Is.EqualTo("Kit"));
            Assert.That(kit.mapId, Is.EqualTo(LayOfTheLandScene.MapId));

            NpcDefinition bann = Record(LayOfTheLandScene.BannNpcId);
            Assert.That(bann.displayName, Is.EqualTo("Prof. Bann"));
            Assert.That(bann.mapId, Is.EqualTo(LayOfTheLandScene.MapId));
            Assert.That(new Vector2Int(bann.x, bann.y), Is.EqualTo(new Vector2Int(26, 42)));
            Assert.That(bann.prefab, Is.Not.Null);
            Assert.That(bann.prefab.name, Is.EqualTo("Teacher_03"));
            Assert.That(bann.layerSymbol, Is.Empty, "Bann is spawned by the story, not baked into the area.");
        }

        private static CutsceneDefinition Cutscene(string id)
        {
            TextAsset asset = Resources.Load<TextAsset>("Cutscenes/" + id);
            Assert.That(asset, Is.Not.Null, id);
            List<string> problems = new List<string>();
            CutsceneDefinition cutscene = CutsceneDefinition.Parse(asset.text, problems);
            Assert.That(problems, Is.Empty, id);
            Assert.That(cutscene.once && cutscene.unskippable, Is.True, id);
            return cutscene;
        }

        private static List<string> Order(CutsceneDefinition cutscene) => cutscene.steps.Where(step => step.Kind != CutsceneStepKind.Wait)
            .Select(step => step.Kind == CutsceneStepKind.Dialogue ? "dialogue:" + step.key : step.type + ":" + step.key).ToList();

        [Test]
        public void KitCallsThenThePlayerWalksOverThemselves()
        {
            // Neither cutscene walks the player: they answer the call and talk to Kit on their own.
            Assert.That(Order(Cutscene(LayOfTheLandScene.CallCutsceneId)),
                Is.EqualTo(new[] { "dialogue:kit_call", "flag:" + LayOfTheLandScene.CalledFlag }));
            Assert.That(Order(Cutscene(LayOfTheLandScene.TourCutsceneId)),
                Is.EqualTo(new[] { "dialogue:kit_lay_of_the_land", "flag:" + LayOfTheLandScene.MetFlag }));
        }

        [Test]
        public void KitSendsThePlayerToBannAtTheFountain()
        {
            TextAsset asset = Resources.Load<TextAsset>("GameEvents/campus_arrival");
            Assert.That(asset, Is.Not.Null);
            Assert.That(GameEventCatalog.TryParseJson(asset.text, asset.name, out GameEventDatabaseDefinition database, out string error), Is.True, error);
            GameEventCatalog catalog = new GameEventCatalog(new[] { database });
            Assert.That(catalog.IsValid, Is.True, string.Join("\n", catalog.ValidationMessages));

            string kitName = Record(LayOfTheLandScene.KitNpcId).displayName;
            foreach (string id in new[] { "kit_call", "kit_lay_of_the_land" })
            {
                Assert.That(catalog.TryGetDialogue(id, out DialogueDefinition dialogue), Is.True, id);
                foreach (DialogueNodeDefinition node in dialogue.nodes)
                {
                    if (PlayerProfile.IsPlayerSpeaker(node.speaker))
                        Assert.That(node.text, Does.StartWith("<i>").And.EndWith("</i>"), $"{id}/{node.id} is the player's inner voice.");
                    else
                        // Matching the catalog name is what gives Kit a portrait.
                        Assert.That(node.speaker, Is.EqualTo(kitName), $"{id}/{node.id}");
                }
            }

            catalog.TryGetDialogue("kit_lay_of_the_land", out DialogueDefinition lay);
            string all = string.Join(" ", lay.nodes.Select(node => node.text));
            Assert.That(all, Does.Contain("orientation ceremony"));
            Assert.That(all, Does.Contain("Bann").And.Contain("fountain"));

            Assert.That(catalog.TryGetDialogue(Record(LayOfTheLandScene.BannNpcId).dialogue.Single().dialogueId, out DialogueDefinition bann), Is.True,
                "Bann's catalog record must name her conversation.");
            Assert.That(bann.nodes[0].speaker, Is.EqualTo(Record(LayOfTheLandScene.BannNpcId).displayName));
        }

        [Test]
        public void BannSendsThePlayerToTheirDormThenOnlyRemindsThem()
        {
            TextAsset asset = Resources.Load<TextAsset>("GameEvents/campus_arrival");
            Assert.That(GameEventCatalog.TryParseJson(asset.text, asset.name, out GameEventDatabaseDefinition database, out string error), Is.True, error);
            GameEventRuntime runtime = new GameEventRuntime(new GameEventCatalog(new[] { database }));

            DialogueSession first = runtime.StartDialogue("bann_orientation");
            Assert.That(first, Is.Not.Null);
            Assert.That(first.Definition.id, Is.EqualTo("bann_orientation"));
            string said = string.Empty;
            for (int guard = 0; guard < 50 && first.IsActive && first.CurrentNode != null; guard++)
            {
                said += " " + first.CurrentNode.text;
                first.Continue();
            }
            Assert.That(said, Does.Contain("missed orientation"));
            Assert.That(said, Does.Contain("student guide").And.Contain("tomorrow"));
            Assert.That(said, Does.Contain("dorm").And.Contain("room"));
            Assert.That(said, Does.Contain("explor"));
            Assert.That(runtime.GetFlag("story.orientation.bann_met"), Is.True, "Hearing her out is recorded.");

            DialogueSession again = runtime.StartDialogue("bann_orientation");
            Assert.That(again.Definition.id, Is.EqualTo("bann_reminder"), "She does not repeat the whole speech.");
            Assert.That(again.CurrentNode.text, Does.Contain("dorm"));
        }

        [Test]
        public void KitsEverydayChatKnowsThePlayersNameAndOffersAReminderOrGoodbye()
        {
            TextAsset asset = Resources.Load<TextAsset>("GameEvents/quest_npc_dialogue");
            Assert.That(GameEventCatalog.TryParseJson(asset.text, asset.name, out GameEventDatabaseDefinition database, out string error), Is.True, error);
            GameEventCatalog catalog = new GameEventCatalog(new[] { database });
            Assert.That(catalog.TryGetDialogue("ambient_courtyard_student", out DialogueDefinition chat), Is.True);
            DialogueNodeDefinition greeting = chat.nodes.Single(node => node.id == chat.startNodeId);
            Assert.That(greeting.speaker, Is.EqualTo("Kit"));
            Assert.That(greeting.text, Does.Contain(PlayerProfile.Token));
            Assert.That(greeting.choices.Select(choice => choice.text), Is.EqualTo(new[]
            {
                "Can you remind me of the campus buildings and areas?", "Bye!"
            }));

            TextAsset arrivalAsset = Resources.Load<TextAsset>("GameEvents/campus_arrival");
            Assert.That(GameEventCatalog.TryParseJson(arrivalAsset.text, arrivalAsset.name,
                out GameEventDatabaseDefinition arrivalDatabase, out error), Is.True, error);
            GameEventCatalog arrivalCatalog = new GameEventCatalog(new[] { arrivalDatabase });
            Assert.That(arrivalCatalog.TryGetDialogue("kit_lay_of_the_land", out DialogueDefinition tour), Is.True);
            foreach ((string reminder, string arrival) in new[]
            {
                ("welcome", "courtyard"), ("academy", "main_building"), ("locked", "locked"), ("library", "library"),
                ("workshop", "workshop"), ("dormitory", "dormitory"), ("forest", "forest"), ("signs", "signs")
            })
                Assert.That(chat.nodes.Single(node => node.id == reminder).text.Replace("Sure! ", string.Empty),
                    Is.EqualTo(tour.nodes.Single(node => node.id == arrival).text), reminder);
        }
    }
}
