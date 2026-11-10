using System;
using System.Collections.Generic;
using System.Linq;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Player;
using Lumenfall.Story;
using NUnit.Framework;
using UnityEngine;

namespace Lumenfall.Tests.GameEvents
{
    public sealed class ForestNightTests
    {
        [Test]
        public void TheForestIsDarkestWhereThePlayerWakesAndLightensTowardsTheGate()
        {
            Assert.That(ForestNight.Darkness(5f), Is.EqualTo(1f));
            Assert.That(ForestNight.Darkness(ForestNight.DarkestRow), Is.EqualTo(1f));
            Assert.That(ForestNight.Darkness(ForestNight.GateRow), Is.EqualTo(0.4f).Within(1e-4f));
            Assert.That(ForestNight.Darkness(60f), Is.EqualTo(0.4f).Within(1e-4f), "Still night at the gate, just less of it.");
            float previous = 1f;
            for (int row = ForestNight.DarkestRow; row <= ForestNight.GateRow; row++)
            {
                Assert.That(ForestNight.Darkness(row), Is.LessThanOrEqualTo(previous + 1e-5f), $"row {row}");
                previous = ForestNight.Darkness(row);
            }
            Assert.That(ForestNight.WispGoal.y, Is.GreaterThanOrEqualTo(LateToOrientationScene.TriggerRow),
                "Following the wisp all the way starts the gate scene.");
            Assert.That(ForestNight.WispGoal.y, Is.LessThan(LateToOrientationScene.GateCell.y - 2), "It stops short of Kepler at the gate.");
        }

        [Test]
        public void ItIsNightOnlyInTheWestForestUntilThePlayerIsThroughTheGate()
        {
            GameEventRuntime runtime = new GameEventRuntime(new GameEventCatalog(Array.Empty<GameEventDatabaseDefinition>()));
            Assert.That(ForestNight.IsNight(runtime, ForestNight.MapId), Is.True);
            Assert.That(ForestNight.IsNight(runtime, "campus"), Is.False);
            Assert.That(ForestNight.IsNight(runtime, "deepForest"), Is.False);
            Assert.That(ForestNight.IsNight(null, ForestNight.MapId), Is.False);

            runtime.SetFlag(LateToOrientationScene.MetFlag, true);
            Assert.That(ForestNight.IsNight(runtime, ForestNight.MapId), Is.False, "Coming back later, the forest is as it always was.");
        }

        [Test]
        public void TheOrangeTreeStandsBesideTheWayNeverOnIt()
        {
            GameObject host = new GameObject("Test Grid");
            try
            {
                GridWorld world = host.AddComponent<GridWorld>();
                List<Vector2Int> way = Enumerable.Range(1, 14).Select(y => new Vector2Int(0, y)).ToList();
                Assert.That(ForestNight.TryBesideTheWay(world, way, ForestNight.OrangeTreeAlong, out Vector2Int cell), Is.True);
                Assert.That(way, Has.No.Member(cell));
                Assert.That(cell.y, Is.EqualTo(way[ForestNight.OrangeTreeAlong].y), "Level with the point along the way.");
                Assert.That(Mathf.Abs(cell.x), Is.InRange(2, 3), "A step or two off to the side, close enough to notice.");

                // A way that turns back on itself beside the tree's spot pushes it further out.
                way.Add(new Vector2Int(-2, ForestNight.OrangeTreeAlong + 1));
                way.Insert(ForestNight.OrangeTreeAlong + 2, new Vector2Int(-2, ForestNight.OrangeTreeAlong + 1));
                Assert.That(ForestNight.TryBesideTheWay(world, way, ForestNight.OrangeTreeAlong, out Vector2Int other), Is.True);
                Assert.That(way, Has.No.Member(other));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void TheOrangeIsAnAppleAndTheWakingThoughtsSeeTheLight()
        {
            List<GameEventDatabaseDefinition> databases = new List<GameEventDatabaseDefinition>();
            foreach (string name in new[] { "GameEvents/forest_walk", "GameEvents/opening_story" })
            {
                TextAsset asset = Resources.Load<TextAsset>(name);
                Assert.That(asset, Is.Not.Null, name);
                Assert.That(GameEventCatalog.TryParseJson(asset.text, asset.name, out GameEventDatabaseDefinition database, out string error), Is.True, error);
                databases.Add(database);
            }
            GameEventCatalog catalog = new GameEventCatalog(databases);
            Assert.That(catalog.IsValid, Is.True, string.Join("\n", catalog.ValidationMessages));

            Assert.That(catalog.TryGetDialogue(ForestNight.OrangeTreeDialogue, out DialogueDefinition tree), Is.True);
            foreach (DialogueNodeDefinition node in tree.nodes)
            {
                Assert.That(PlayerProfile.IsPlayerSpeaker(node.speaker), Is.True, node.id);
                Assert.That(node.text, Does.StartWith("<i>").And.EndWith("</i>"), node.id);
            }
            DialogueNodeDefinition spot = tree.nodes.Single(node => node.id == tree.startNodeId);
            Assert.That(spot.text, Does.Contain("orange"));
            string afterPicking = spot.choices.Single(choice => choice.id == "pick").nextNodeId;
            List<string> eaten = new List<string>();
            for (string next = afterPicking; !string.IsNullOrEmpty(next); next = tree.nodes.Single(node => node.id == next).nextNodeId)
                eaten.Add(tree.nodes.Single(node => node.id == next).text);
            Assert.That(eaten.Any(text => text.Contains("apple")), Is.True, "It's an apple.");

            catalog.TryGetDialogue("opening_awakening", out DialogueDefinition awakening);
            Assert.That(awakening.nodes.Any(node => node.text.Contains("light")), Is.True, "The wisp appears as they wake.");
            Assert.That(awakening.nodes.Any(node => node.text.Contains("horror movies")), Is.True, "As in the design document.");
        }
    }
}
