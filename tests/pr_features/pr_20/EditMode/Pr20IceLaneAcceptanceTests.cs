using Lumenfall.Spells;
using NUnit.Framework;
using UnityEngine;

namespace Lumenfall.FeatureAcceptance
{
    [Category("PR20")]
    public sealed class Pr20IceLaneAcceptanceTests
    {
        private static IceSlideLane Lane()
        {
            Assert.That(IceSlideLane.TryParse(new[] { "glaciate", "slide-lane:20,34,24,34" }, out IceSlideLane lane), Is.True);
            return lane;
        }

        [Test] public void ParsedLaneEnumeratesOnlyTheFourOrderedTrapsAfterStart()
        {
            IceSlideLane lane = Lane();
            Assert.That(lane.Start, Is.EqualTo(new Vector2Int(20, 34)));
            Assert.That(lane.Goal, Is.EqualTo(new Vector2Int(24, 34)));
            Assert.That(lane.TrapCount, Is.EqualTo(4));
            var cells = new System.Collections.Generic.List<Vector2Int>();
            for (int i = 0; i < lane.TrapCount; i++) cells.Add(lane.TrapCell(i));
            Assert.That(cells, Is.EqualTo(new[] { new Vector2Int(21, 34), new Vector2Int(22, 34), new Vector2Int(23, 34), new Vector2Int(24, 34) }));
        }

        [Test] public void MaximumLaneLengthIsInclusiveAndDuplicateAuthoringIsRejected()
        {
            Assert.That(IceSlideLane.TryParse(new[] { "slide-lane:0,0,128,0" }, out IceSlideLane lane), Is.True);
            Assert.That(lane.TrapCount, Is.EqualTo(128));
            Assert.That(IceSlideLane.TryParse(new[] { "slide-lane:0,0,129,0" }, out _), Is.False);
            Assert.That(IceSlideLane.TryParse(new[] { "slide-lane:0,0,4,0", "SLIDE-LANE:0,0,4,0" }, out _), Is.False);
        }

        [Test] public void WideFootprintMustFitEntirelyBeforeTheGoal()
        {
            IceSlideLane lane = Lane();
            Assert.That(lane.ContainsFootprint(new Vector2Int(23, 34), new Vector2Int(2, 1)), Is.True);
            Assert.That(lane.ContainsFootprint(new Vector2Int(24, 34), new Vector2Int(2, 1)), Is.False);
            Assert.That(lane.ContainsFootprint(new Vector2Int(20, 34), new Vector2Int(1, 2)), Is.False);
            Assert.That(lane.ContainsFootprint(new Vector2Int(19, 34), Vector2Int.one), Is.False);
        }

        [Test] public void VisitProofRequiresPositionFrozenStateAndExactCountToAgree()
        {
            IceSlideLane lane = Lane();
            var state = new WorldObjectSnapshot { cell = new Vector2Int(22, 34), size = Vector2Int.one, visitedTrapCount = 2, frozen = true };
            Assert.That(lane.MatchesProgress(state), Is.True);
            state.frozen = false; Assert.That(lane.MatchesProgress(state), Is.False);
            state.frozen = true; state.visitedTrapCount = 3; Assert.That(lane.MatchesProgress(state), Is.False);
            state.visitedTrapCount = -1; Assert.That(lane.MatchesProgress(state), Is.False);
            state.visitedTrapCount = 5; Assert.That(lane.MatchesProgress(state), Is.False);
        }

        [Test] public void InterruptedProgressMustHaveNoVisitProofAndRemainInsideLane()
        {
            IceSlideLane lane = Lane();
            var state = new WorldObjectSnapshot { cell = new Vector2Int(22, 34), size = Vector2Int.one, slideInterrupted = true, visitedTrapCount = 0 };
            Assert.That(lane.MatchesProgress(state), Is.True);
            state.visitedTrapCount = 2; Assert.That(lane.MatchesProgress(state), Is.False);
            state.visitedTrapCount = 0; state.cell = new Vector2Int(22, 35); Assert.That(lane.MatchesProgress(state), Is.False);
        }
    }
}
