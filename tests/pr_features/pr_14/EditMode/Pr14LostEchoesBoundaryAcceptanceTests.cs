using Lumenfall.Spells;
using NUnit.Framework;
using UnityEngine;

namespace Lumenfall.FeatureAcceptance
{
    [Category("PR14")]
    public sealed class Pr14LostEchoesBoundaryAcceptanceTests
    {
        private GameObject host;
        private WorldStateService state;
        [SetUp] public void SetUp()
        {
            host = new GameObject("PR14 isolated state"); host.SetActive(false);
            state = host.AddComponent<WorldStateService>();
            state.BeginMapRebuild("echo-acceptance");
            Register("hidden", new Vector2Int(2, 2));
            state.EndMapRebuild();
        }
        [TearDown] public void TearDown() { Object.DestroyImmediate(host); }
        private void Register(string id, Vector2Int cell) => state.RegisterObject(id, null, cell, Vector2Int.one, true, new[] { "hidden", "pickup" });
        private WorldObjectSnapshot Snapshot(string id) { Assert.That(state.TryGetObject(id, out WorldObjectSnapshot s), Is.True); return s; }

        [Test] public void RevealUsesInclusiveManhattanRadiusAndRejectsNegativeRadius()
        {
            int reveals = 0; state.ObjectRevealed += _ => reveals++;
            Assert.That(state.TryRevealObject("hidden", Vector2Int.zero, 3), Is.False);
            Assert.That(state.TryRevealObject("hidden", new Vector2Int(2, 2), -1), Is.False);
            Assert.That(Snapshot("hidden").revealed, Is.False);
            Assert.That(state.TryRevealObject("hidden", Vector2Int.zero, 4), Is.True);
            Assert.That(reveals, Is.EqualTo(1));
        }

        [Test] public void CollectFromSameCellWithZeroRadiusEmitsRevealBeforeCollection()
        {
            var events = new System.Collections.Generic.List<string>();
            state.ObjectRevealed += _ => events.Add("reveal");
            state.ObjectCollected += s => { events.Add("collect"); Assert.That(s.revealed && s.collected, Is.True); };
            Assert.That(state.TryCollectObject("hidden", new Vector2Int(2, 2), 0), Is.True);
            Assert.That(events, Is.EqualTo(new[] { "reveal", "collect" }));
            Assert.That(state.TryCollectObject("hidden", new Vector2Int(2, 2), 0), Is.False);
            Assert.That(state.TryFindNearestConcealed(Vector2Int.zero, 20, out _, out _), Is.False);
            Assert.That(events.Count, Is.EqualTo(2));
        }

        [Test] public void NegativeSonarRangeAndUnknownCollectionDoNotChangeHiddenState()
        {
            int notifications = 0;
            state.ObjectRevealed += _ => notifications++;
            state.ObjectCollected += _ => notifications++;
            Assert.That(state.TryFindNearestConcealed(new Vector2Int(2, 2), -1, out _, out _), Is.False);
            Assert.That(state.TryCollectObject("missing", Vector2Int.zero, 10), Is.False);
            Assert.That(state.TryCollectObject("hidden", new Vector2Int(2, 2), -1), Is.False);
            Assert.That(Snapshot("hidden").revealed || Snapshot("hidden").collected, Is.False);
            Assert.That(notifications, Is.Zero);
        }

        [Test] public void SamePickupIdOnDifferentMapsRetainsIndependentCollection()
        {
            Assert.That(state.TryCollectObject("hidden", new Vector2Int(2, 2), 0), Is.True);
            state.BeginMapRebuild("second-map"); Register("hidden", Vector2Int.zero); state.EndMapRebuild();
            Assert.That(Snapshot("hidden").collected, Is.False);
            Assert.That(state.TryFindNearestConcealed(Vector2Int.zero, 0, out WorldObjectSnapshot nearest, out int distance), Is.True);
            Assert.That(nearest.id, Is.EqualTo("hidden")); Assert.That(distance, Is.Zero);
            state.BeginMapRebuild("echo-acceptance"); Register("hidden", new Vector2Int(2, 2)); state.EndMapRebuild();
            Assert.That(Snapshot("hidden").collected, Is.True);
            Assert.That(state.TryFindNearestConcealed(Vector2Int.zero, 10, out _, out _), Is.False);
        }
    }
}
