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
    public sealed class LateToOrientationTests
    {
        private static CutsceneDefinition Cutscene()
        {
            TextAsset asset = Resources.Load<TextAsset>("Cutscenes/" + LateToOrientationScene.CutsceneId);
            Assert.That(asset, Is.Not.Null);
            List<string> problems = new List<string>();
            CutsceneDefinition definition = CutsceneDefinition.Parse(asset.text, problems);
            Assert.That(problems, Is.Empty);
            return definition;
        }

        private static NpcDefinition Kepler()
        {
            NpcCatalogAsset catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<NpcCatalogAsset>("Assets/_Project/World/Areas/NpcCatalog.asset");
            Assert.That(catalog, Is.Not.Null);
            NpcDefinition kepler = Array.Find(catalog.npcs, npc => npc.id == LateToOrientationScene.KeplerNpcId);
            Assert.That(kepler, Is.Not.Null, "The NPC catalog needs a prof_kepler record.");
            return kepler;
        }

        [Test]
        public void SceneStartsOnTheTriggerRow()
        {
            Assert.That(LateToOrientationScene.HasReachedTrigger(new Vector2Int(21, 5)), Is.False, "Not at the opening spawn.");
            Assert.That(LateToOrientationScene.HasReachedTrigger(new Vector2Int(18, 21)), Is.False);
            Assert.That(LateToOrientationScene.HasReachedTrigger(new Vector2Int(18, 22)), Is.True);
            Assert.That(LateToOrientationScene.HasReachedTrigger(new Vector2Int(15, 30)), Is.True);
        }

        [Test]
        public void KeplerIsATeacherWaitingByTheForestGateWithNothingElseToSay()
        {
            NpcDefinition kepler = Kepler();
            Assert.That(kepler.displayName, Is.EqualTo("Prof. Kepler"));
            Assert.That(kepler.mapId, Is.EqualTo(LateToOrientationScene.MapId));
            Assert.That(new Vector2Int(kepler.x, kepler.y), Is.EqualTo(new Vector2Int(19, 46)));
            Assert.That(kepler.prefab, Is.Not.Null);
            Assert.That(kepler.prefab.name, Is.EqualTo("Teacher_01"));
            Assert.That(kepler.dialogue, Is.Empty, "Kepler only speaks in the gate scene.");
            Assert.That(kepler.layerSymbol, Is.Empty, "Kepler is spawned by the scene, not baked into the area.");
        }

        [Test]
        public void SceneCallsWalksAsksForANameAndWalksThroughTheGate()
        {
            CutsceneDefinition cutscene = Cutscene();
            Assert.That(cutscene.once, Is.True);
            Assert.That(cutscene.unskippable, Is.True);

            List<string> order = cutscene.steps.Select(step => step.Kind == CutsceneStepKind.Dialogue ? "dialogue:" + step.key : step.type)
                .Where(step => step != "wait" && step != "fade").ToList();
            Assert.That(order, Is.EqualTo(new[]
            {
                "dialogue:kepler_far_call", "walk", "dialogue:kepler_late", "name", "dialogue:kepler_get_in", "flag", "walk", "enter"
            }));

            CutsceneStep[] walks = cutscene.steps.Where(step => step.Kind == CutsceneStepKind.Walk).ToArray();
            NpcDefinition kepler = Kepler();
            // The player is walked to stand two cells in front of Kepler, one back from the cell beside him.
            Assert.That(walks[0].Cell, Is.EqualTo(new Vector2Int(kepler.x, kepler.y - 2)));
            CutsceneStep enter = cutscene.steps.Single(step => step.Kind == CutsceneStepKind.Enter);
            Assert.That(enter.Cell, Is.EqualTo(walks[1].Cell), "The player walks onto the gate before going through it.");
            Assert.That(enter.Cell, Is.EqualTo(new Vector2Int(17, 48)), "The west-forest gate to campus (portal P.F00).");

            CutsceneStep flag = cutscene.steps.Single(step => step.Kind == CutsceneStepKind.Flag);
            Assert.That(flag.key, Is.EqualTo(LateToOrientationScene.MetFlag));
            Assert.That(flag.value, Is.True);
        }

        [Test]
        public void ConversationsAreValidAndKeplerMarksThePlayerDownByName()
        {
            TextAsset asset = Resources.Load<TextAsset>("GameEvents/orientation_gate");
            Assert.That(asset, Is.Not.Null);
            Assert.That(GameEventCatalog.TryParseJson(asset.text, asset.name, out GameEventDatabaseDefinition database, out string error), Is.True, error);
            GameEventCatalog catalog = new GameEventCatalog(new[] { database });
            Assert.That(catalog.IsValid, Is.True, string.Join("\n", catalog.ValidationMessages));

            string keplerName = Kepler().displayName;
            foreach (string id in new[] { "kepler_far_call", "kepler_late", "kepler_get_in" })
            {
                Assert.That(catalog.TryGetDialogue(id, out DialogueDefinition dialogue), Is.True, id);
                foreach (DialogueNodeDefinition node in dialogue.nodes)
                {
                    if (PlayerProfile.IsPlayerSpeaker(node.speaker))
                        Assert.That(node.text, Does.StartWith("<i>").And.EndWith("</i>"), $"{id}/{node.id} is the player's inner voice.");
                    else if (id != "kepler_far_call")
                        // Matching the catalog name is what gives Kepler his portrait.
                        Assert.That(node.speaker, Is.EqualTo(keplerName), $"{id}/{node.id}");
                    Assert.That(node.choices, Is.Empty, $"{id}/{node.id} moves straight on.");
                }
            }

            catalog.TryGetDialogue("kepler_get_in", out DialogueDefinition getIn);
            Assert.That(getIn.nodes[0].text, Does.Contain(PlayerProfile.Token));
        }
    }
}
