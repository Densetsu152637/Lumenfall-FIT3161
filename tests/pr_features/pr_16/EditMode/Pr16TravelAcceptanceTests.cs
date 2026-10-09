using Lumenfall.DataDriven;
using Lumenfall.GameEvents;
using NUnit.Framework;
using UnityEngine;
namespace Lumenfall.FeatureAcceptance
{
    [Category("PR16")]
    public sealed class Pr16TravelAcceptanceTests
    {
        private static GameEventRuntime Runtime() => new GameEventRuntime(new GameEventCatalog(new GameEventDatabaseDefinition[0]));
        [Test] public void FlagsFailClosedForBlankEntriesAndRequireEveryEntry()
        {
            var runtime = Runtime(); runtime.SetFlag("open", true);
            Assert.That(OptionalTravelPolicy.RequiredFlagsAllow(new[] { "open", " " }, runtime), Is.False);
            Assert.That(OptionalTravelPolicy.RequiredFlagsAllow(new[] { "open", "second" }, runtime), Is.False);
            runtime.SetFlag("second", true);
            Assert.That(OptionalTravelPolicy.RequiredFlagsAllow(new[] { "open", "second", "open" }, runtime), Is.True);
            Assert.That(OptionalTravelPolicy.RequiredFlagsAllow(new[] { "open" }, null), Is.False);
        }
        [Test] public void SafeRegionIncludesItsLowerEdgeExcludesUpperEdgeAndRejectsUnwalkableContent()
        {
            var region = new MapRegionDefinition { id = "alcove", walkable = true, rect = new MapRectDefinition { x = -2, y = 3, width = 3, height = 2 } };
            var map = new MapDefinition { regions = new[] { region } };
            Assert.That(OptionalTravelPolicy.RegionContains(map, "alcove", new Vector2Int(-2, 3)), Is.True);
            Assert.That(OptionalTravelPolicy.RegionContains(map, "alcove", new Vector2Int(0, 4)), Is.True);
            Assert.That(OptionalTravelPolicy.RegionContains(map, "alcove", new Vector2Int(1, 4)), Is.False);
            Assert.That(OptionalTravelPolicy.RegionContains(map, "alcove", new Vector2Int(0, 5)), Is.False);
            region.walkable = false;
            Assert.That(OptionalTravelPolicy.RegionContains(map, "alcove", new Vector2Int(-2, 3)), Is.False);
        }
        [Test] public void NullSelectedStopDoesNotFallbackToAnotherStopOrMutateThePhase()
        {
            var runtime = Runtime(); runtime.SetCounter("phase", 1);
            var npc = new NpcDefinition { id = "ambient_test", ambientOnly = true, ambientSchedule = new AmbientNpcScheduleDefinition { enabled = true, phaseCounterKey = "phase", stops = new[] { new AmbientNpcStopDefinition { x = 4 }, null } } };
            Assert.That(AmbientNpcPolicy.TryGetStop(npc, runtime, out var stop), Is.False);
            Assert.That(stop, Is.Null); Assert.That(runtime.GetCounter("phase"), Is.EqualTo(1));
            runtime.SetCounter("phase", 2);
            Assert.That(AmbientNpcPolicy.TryGetStop(npc, runtime, out stop), Is.True);
            Assert.That(stop.x, Is.EqualTo(4));
        }
        [Test] public void FutureMetadataAndChoiceConditionsCannotEnterAmbientDialoguePool()
        {
            var dialogue = new DialogueDefinition { nodes = new[] { new DialogueNodeDefinition { choices = new[] { new DialogueChoiceDefinition { conditions = new[] { new GameConditionDefinition { type = "Flag" } } } } } } };
            Assert.That(AmbientNpcPolicy.IsQuestNeutral(dialogue), Is.False);
            dialogue.nodes[0].choices = null;
            dialogue.nodes[0].metadata = new[] { new GameMetadataEntry { key = "futureReward" } };
            Assert.That(AmbientNpcPolicy.IsQuestNeutral(dialogue), Is.False);
            dialogue.nodes[0].metadata[0].key = "portrait";
            Assert.That(AmbientNpcPolicy.IsQuestNeutral(dialogue), Is.True);
        }
    }
}
