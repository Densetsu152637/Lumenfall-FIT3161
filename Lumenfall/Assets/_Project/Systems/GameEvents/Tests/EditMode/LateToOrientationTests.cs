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

        private static string Label(CutsceneStep step) => step.Kind == CutsceneStepKind.Dialogue ? "dialogue:" + step.key : step.type;

        [Test]
        public void SceneStartsNearTheGateAndNotOnTheTrailBehind()
        {
            Assert.That(LateToOrientationScene.HasReachedTrigger(new Vector2Int(21, 5)), Is.False, "Not at the opening spawn.");
            Assert.That(LateToOrientationScene.HasReachedTrigger(new Vector2Int(18, 22)), Is.False, "Not at the old fork: the dark walk comes first.");
            Assert.That(LateToOrientationScene.HasReachedTrigger(new Vector2Int(17, 40)), Is.False);
            Assert.That(LateToOrientationScene.HasReachedTrigger(new Vector2Int(17, 41)), Is.True);
            Assert.That(LateToOrientationScene.HasReachedTrigger(new Vector2Int(34, 47)), Is.True, "The forest's other campus portals are covered too.");
            Assert.That(LateToOrientationScene.HasReachedTrigger(new Vector2Int(-12, 44)), Is.True, "Walking round through the trees still meets Kepler.");
            Assert.That(LateToOrientationScene.LateStudentStart.y, Is.LessThan(LateToOrientationScene.TriggerRow),
                "The late student runs up the trail from behind.");
        }

        [Test]
        public void KeplerKeepsTheGateWithNothingElseToSay()
        {
            NpcDefinition kepler = Kepler();
            Assert.That(kepler.displayName, Is.EqualTo("Prof. Kepler"));
            Assert.That(kepler.mapId, Is.EqualTo(LateToOrientationScene.MapId));
            Assert.That(new Vector2Int(kepler.x, kepler.y), Is.EqualTo(new Vector2Int(19, 46)));
            Assert.That(kepler.prefab, Is.Not.Null);
            Assert.That(kepler.prefab.name, Is.EqualTo("Teacher_01"));
            Assert.That(kepler.dialogue, Is.Empty, "Kepler only speaks in the gate scene.");
            Assert.That(kepler.layerSymbol, Is.Empty, "Kepler is spawned by the scene, not baked into the area.");
            Assert.That(new Vector2Int(kepler.x, kepler.y).y, Is.GreaterThanOrEqualTo(LateToOrientationScene.TriggerRow));
        }

        [Test]
        public void KeplerAsksForTheLetterThenItBlowsInAndThePlayerGivesTheirName()
        {
            CutsceneDefinition cutscene = Cutscene();
            Assert.That(cutscene.once, Is.True);
            Assert.That(cutscene.unskippable, Is.True);
            List<string> order = cutscene.steps.Select(Label).ToList();
            Assert.That(order.Where(step => step.StartsWith("dialogue:")), Is.EqualTo(new[]
            {
                "dialogue:kepler_far_call", "dialogue:kepler_late", "dialogue:kepler_letter_caught",
                "dialogue:kepler_cant_read", "dialogue:kepler_get_in"
            }));
            Assert.That(cutscene.steps.Any(step => step.Kind == CutsceneStepKind.NpcLeave), Is.False, "No queue goes through ahead of the player.");

            // The player is walked to stand in front of wherever Kepler stands, and later onto the gate.
            CutsceneStep[] walks = cutscene.steps.Where(step => step.Kind == CutsceneStepKind.Walk).ToArray();
            Assert.That(walks.Length, Is.EqualTo(2));
            Assert.That(walks[0].key, Is.EqualTo(LateToOrientationScene.KeplerNpcId));
            Assert.That(walks[0].Cell, Is.EqualTo(LateToOrientationScene.BeforeKepler));
            Assert.That(walks[1].Cell, Is.EqualTo(LateToOrientationScene.GateCell));
            Assert.That(walks[1].value, Is.True, "Hurried through: the last walk runs.");

            // The late student only appears once they start running, trips, and their letter flies to the player and on to Kepler.
            int runs = cutscene.steps.ToList().FindIndex(step => step.Kind == CutsceneStepKind.Flag && step.key == LateToOrientationScene.LateStudentRunsFlag);
            int run = Array.FindIndex(cutscene.steps, step => step.Kind == CutsceneStepKind.NpcWalk && step.key == LateToOrientationScene.LateStudentId);
            int trip = Array.FindIndex(cutscene.steps, step => step.Kind == CutsceneStepKind.NpcPose && step.text == "Tripping");
            CutsceneStep[] letters = cutscene.steps.Where(step => step.Kind == CutsceneStepKind.Letter).ToArray();
            Assert.That(runs, Is.EqualTo(order.IndexOf("dialogue:kepler_late") + 1), "The late student runs up the moment the player has no letter.");
            Assert.That(runs, Is.LessThan(run));
            Assert.That(run, Is.LessThan(trip));
            Assert.That(letters.Length, Is.EqualTo(2));
            Assert.That(letters[0].key, Is.EqualTo(LateToOrientationScene.LateStudentId));
            Assert.That(letters[0].value, Is.True, "Into the player's hands.");
            Assert.That(letters[1].key, Is.EqualTo(LateToOrientationScene.KeplerNpcId));
            Assert.That(letters[1].value, Is.False, "Handed to Kepler.");
            Assert.That(Array.IndexOf(cutscene.steps, letters[0]), Is.GreaterThan(trip));

            // She speaks only in bubbles on the move: while she runs, and her cry as she falls, never once she is still.
            CutsceneStep[] says = cutscene.steps.Where(step => step.Kind == CutsceneStepKind.Say).ToArray();
            Assert.That(says, Is.Not.Empty);
            Assert.That(says.All(step => step.key == LateToOrientationScene.LateStudentId), Is.True);
            Assert.That(says.Any(step => step.text.Contains("late")), Is.True, "She says she is late.");
            int arrive = Array.FindLastIndex(cutscene.steps, step => step.Kind == CutsceneStepKind.NpcWalk && step.key == LateToOrientationScene.LateStudentId);
            foreach (CutsceneStep say in says)
            {
                int at = Array.IndexOf(cutscene.steps, say);
                Assert.That(at < arrive ? say.value : say.seconds <= 1f && say.background, Is.True,
                    $"'{say.text}' lasts only while she runs, or is a quick cry as she falls.");
                Assert.That(at, Is.LessThan(Array.IndexOf(cutscene.steps, letters[0])), $"'{say.text}' comes before she has stopped.");
            }
            Assert.That(cutscene.steps.Any(step => step.Kind == CutsceneStepKind.Dialogue && step.key.StartsWith("late_student")), Is.False,
                "No dialogue box for her: nothing waits for a click while she runs.");

            // Kepler cannot read it, so the player gives their name, and is then sent through.
            Assert.That(order.IndexOf("name"), Is.GreaterThan(order.IndexOf("dialogue:kepler_cant_read")));
            Assert.That(order.IndexOf("name"), Is.LessThan(order.IndexOf("dialogue:kepler_get_in")));
            CutsceneStep met = cutscene.steps.Single(step => step.Kind == CutsceneStepKind.Flag && step.key == LateToOrientationScene.MetFlag);
            Assert.That(met.value, Is.True);
            CutsceneStep enter = cutscene.steps.Single(step => step.Kind == CutsceneStepKind.Enter);
            Assert.That(enter.Cell, Is.EqualTo(LateToOrientationScene.GateCell));
            Assert.That(Array.IndexOf(cutscene.steps, met), Is.LessThan(Array.IndexOf(cutscene.steps, enter)));
            Assert.That(cutscene.steps.Any(step => step.Kind == CutsceneStepKind.Caption), Is.False, "No countdown and nothing written over the black.");
        }

        [Test]
        public void ConversationsAreValidAndKeplerSaysThePlayerIsLateAndAsksForTheirLetter()
        {
            TextAsset asset = Resources.Load<TextAsset>("GameEvents/orientation_gate");
            Assert.That(asset, Is.Not.Null);
            Assert.That(GameEventCatalog.TryParseJson(asset.text, asset.name, out GameEventDatabaseDefinition database, out string error), Is.True, error);
            GameEventCatalog catalog = new GameEventCatalog(new[] { database });
            Assert.That(catalog.IsValid, Is.True, string.Join("\n", catalog.ValidationMessages));

            string keplerName = Kepler().displayName;
            foreach (string id in new[] { "kepler_far_call", "kepler_late", "kepler_letter_caught", "kepler_cant_read", "kepler_get_in" })
            {
                Assert.That(catalog.TryGetDialogue(id, out DialogueDefinition dialogue), Is.True, id);
                foreach (DialogueNodeDefinition node in dialogue.nodes)
                {
                    Assert.That(node.text.Length, Is.LessThanOrEqualTo(130), $"{id}/{node.id}");
                    if (PlayerProfile.IsPlayerSpeaker(node.speaker))
                        Assert.That(node.text, Does.StartWith("<i>").And.EndWith("</i>"), $"{id}/{node.id} is the player's inner voice.");
                    else if (id != "kepler_far_call")
                        // Matching the catalog name is what gives Kepler a portrait.
                        Assert.That(node.speaker, Is.EqualTo(keplerName), $"{id}/{node.id}");
                    Assert.That(node.choices, Is.Empty, $"{id}/{node.id} moves straight on.");
                }
            }

            catalog.TryGetDialogue("kepler_late", out DialogueDefinition late);
            Assert.That(late.nodes.Any(node => node.speaker == keplerName && node.text.Contains("missed orientation")), Is.True);
            Assert.That(late.nodes.Any(node => node.speaker == keplerName && node.text.Contains("letter of admission")), Is.True);
            Assert.That(catalog.TryGetDialogue("gate_countdown", out _), Is.False, "There is no countdown to closing.");
            catalog.TryGetDialogue("kepler_get_in", out DialogueDefinition getIn);
            Assert.That(getIn.nodes[0].text, Does.Contain(PlayerProfile.Token), "Kepler marks the player down by the name they gave.");
            Assert.That(catalog.TryGetDialogue(LateToOrientationScene.LateStudentChatDialogue, out _), Is.True);

        }

        [Test]
        public void TheLateStudentIsAStoryExtraNotACatalogCharacter()
        {
            NpcCatalogAsset catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<NpcCatalogAsset>("Assets/_Project/World/Areas/NpcCatalog.asset");
            Assert.That(catalog, Is.Not.Null);
            foreach (string id in new[] { LateToOrientationScene.LateStudentId, LateToOrientationScene.LateStudentAfterId })
                Assert.That(catalog.npcs.Any(npc => npc.id == id), Is.False, $"{id} would need an introduction and a journal entry.");
            GateCast cast = GateCast.Load();
            Assert.That(cast, Is.Not.Null, "Resources/GateCast is missing.");
            Assert.That(cast.lateStudent, Is.Not.Null);

            NpcDefinition after = LateToOrientationScene.Record(LateToOrientationScene.LateStudentAfterId, LateToOrientationScene.LateStudentName,
                cast.lateStudent, LateToOrientationScene.LateStudentAfterCell, LateToOrientationScene.LateStudentChatDialogue);
            Assert.That(after.mapId, Is.EqualTo(LateToOrientationScene.MapId));
            Assert.That(after.dialogueIds, Is.EqualTo(new[] { LateToOrientationScene.LateStudentChatDialogue }));
            Assert.That(LateToOrientationScene.LateStudentAfterCell, Is.Not.EqualTo(LateToOrientationScene.GateCell));
        }
    }
}
