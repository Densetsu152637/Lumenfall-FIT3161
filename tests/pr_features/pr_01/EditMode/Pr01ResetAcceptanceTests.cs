using Lumenfall.DataDriven;
using Lumenfall.Spells;
using NUnit.Framework;
using UnityEngine;

namespace Lumenfall.FeatureAcceptance
{
    [Category("PR01")]
    public sealed class Pr01ResetAcceptanceTests
    {
        private GameObject host;
        private WorldStateService state;

        [SetUp] public void SetUp()
        {
            host = new GameObject("PR01 isolated state");
            host.SetActive(false); // Avoid singleton and save-provider registration.
            state = host.AddComponent<WorldStateService>();
            state.BeginMapRebuild("acceptance");
            state.RegisterObject("lamp", null, Vector2Int.zero, Vector2Int.one, true, new[] { "ampere" });
            state.RegisterAdditionalPuzzleTarget(new PuzzleTargetDefinition { id = "lamp-puzzle", objectIds = new[] { "lamp" }, requiredPowered = true });
            state.EndMapRebuild();
        }

        [TearDown] public void TearDown() { Object.DestroyImmediate(host); }

        [Test] public void ResetCompletionObserverSeesCommittedProgressAndFeedback()
        {
            state.SetProperty("lamp", x => x.powered = true);
            state.SetPuzzleProgress("lamp-puzzle", 2);
            int completed = 0;
            state.PuzzleResetCompleted += id =>
            {
                completed++;
                Assert.That(id, Is.EqualTo("lamp-puzzle"));
                Assert.That(state.GetPuzzleProgress(id), Is.Zero);
                Assert.That(state.GetObjectFeedback("lamp").State, Is.EqualTo(ObjectFeedbackState.Restored));
                Assert.That(state.GetObjectFeedback("lamp").IsSolved, Is.False);
            };
            Assert.That(state.ResetPuzzle("lamp-puzzle"), Is.True);
            Assert.That(completed, Is.EqualTo(1));
        }

        [Test] public void UnknownResetDoesNotConsumeQueuedEffectsOrChangeProgress()
        {
            state.SetPuzzleProgress("lamp-puzzle", 4);
            state.QueueEffect("lightning:lamp:on");
            int changed = 0, completed = 0;
            state.StateChanged += _ => changed++;
            state.PuzzleResetCompleted += _ => completed++;
            Assert.That(state.ResetPuzzle("absent"), Is.False);
            Assert.That(state.GetPuzzleProgress("lamp-puzzle"), Is.EqualTo(4));
            Assert.That(state.AcknowledgeEffect("lightning:lamp:on"), Is.True);
            Assert.That(changed, Is.Zero);
            Assert.That(completed, Is.Zero);
        }

        [Test] public void AcknowledgedEffectCanBeQueuedAgainButDuplicatePendingEffectCannot()
        {
            int queued = 0;
            state.EffectQueued += _ => queued++;
            state.QueueEffect("lightning:lamp:on");
            state.QueueEffect("lightning:lamp:on");
            state.QueueEffect(" ");
            Assert.That(queued, Is.EqualTo(1));
            Assert.That(state.AcknowledgeEffect("lightning:lamp:on"), Is.True);
            Assert.That(state.AcknowledgeEffect("lightning:lamp:on"), Is.False);
            state.QueueEffect("lightning:lamp:on");
            Assert.That(queued, Is.EqualTo(2));
        }

        [Test] public void InvalidProgressUpdatesPreserveTheLastAcceptedValue()
        {
            Assert.That(state.SetPuzzleProgress("lamp-puzzle", 3), Is.True);
            int changes = 0;
            state.StateChanged += _ => changes++;
            Assert.That(state.SetPuzzleProgress("lamp-puzzle", -1), Is.False);
            Assert.That(state.SetPuzzleProgress("absent", 3), Is.False);
            Assert.That(state.SetPuzzleProgress("lamp-puzzle", 3), Is.False);
            Assert.That(state.GetPuzzleProgress("lamp-puzzle"), Is.EqualTo(3));
            Assert.That(changes, Is.Zero);
        }
    }
}
