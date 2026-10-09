using Lumenfall.Spells;
using NUnit.Framework;
using UnityEngine;

namespace Lumenfall.FeatureAcceptance
{
    [Category("PR05")]
    public sealed class Pr05AlignmentResetAcceptanceTests
    {
        private GameObject host;
        private WorldStateService state;
        [SetUp] public void SetUp()
        {
            host = new GameObject("PR05 isolated state"); host.SetActive(false);
            state = host.AddComponent<WorldStateService>();
            state.BeginMapRebuild("campus");
            Register("first", 270); Register("second", 180);
            state.EndMapRebuild();
        }
        [TearDown] public void TearDown() { Object.DestroyImmediate(host); }
        private void Register(string id, float angle) => state.RegisterObject(id, null, Vector2Int.zero, Vector2Int.one, true, new[] { "vis", "vis-target:90" }, angle);
        private WorldObjectSnapshot Snapshot(string id) { Assert.That(state.TryGetObject(id, out WorldObjectSnapshot s), Is.True); return s; }

        [Test] public void ResetOneMarkerPreservesOtherMarkersAndNonAlignmentProperties()
        {
            state.SetProperty("first", x => { x.aligned = true; x.rotation = 90; x.powered = true; x.frozen = true; });
            state.SetProperty("second", x => { x.aligned = true; x.rotation = 90; });
            Assert.That(state.ResetAlignmentObject("first"), Is.True);
            WorldObjectSnapshot first = Snapshot("first");
            Assert.That(first.rotation, Is.EqualTo(270));
            Assert.That(first.aligned, Is.False);
            Assert.That(first.powered && first.frozen, Is.True);
            Assert.That(Snapshot("second").rotation, Is.EqualTo(90));
            Assert.That(Snapshot("second").aligned, Is.True);
        }

        [Test] public void RestoreUsesSavedAlignmentButResetUsesAuthoredAngle()
        {
            state.SetProperty("first", x => { x.aligned = true; x.rotation = 90; });
            string saved = state.CaptureStateJson();
            state.ResetAlignmentObject("first");
            Assert.That(state.RestoreStateJson(saved), Is.True);
            Assert.That(Snapshot("first").aligned, Is.True);
            Assert.That(state.ResetAlignmentObject("first"), Is.True);
            Assert.That(Snapshot("first").rotation, Is.EqualTo(270));
        }

        [Test] public void RejectedAndAlreadyResetMarkersEmitNoStateChanges()
        {
            int changes = 0; state.StateChanged += _ => changes++;
            Assert.That(state.ResetAlignmentObject("first"), Is.False);
            Assert.That(state.ResetAlignmentObject("missing"), Is.False);
            Assert.That(state.ResetAlignmentObject(null), Is.False);
            Assert.That(changes, Is.Zero);
            state.SetProperty("first", x => x.aligned = true);
            Assert.That(state.ResetAlignmentObject("first"), Is.True, "Alignment alone requires resetting even at the authored angle.");
            Assert.That(changes, Is.EqualTo(2));
        }

        [Test] public void SameMarkerIdOnAnotherMapHasItsOwnResetAngle()
        {
            state.SetProperty("first", x => { x.aligned = true; x.rotation = 90; });
            state.BeginMapRebuild("other"); Register("first", 180); state.EndMapRebuild();
            state.SetProperty("first", x => { x.aligned = true; x.rotation = 90; });
            Assert.That(state.ResetAlignmentObject("first"), Is.True);
            Assert.That(Snapshot("first").rotation, Is.EqualTo(180));
            state.BeginMapRebuild("campus"); Register("first", 270); state.EndMapRebuild();
            Assert.That(Snapshot("first").aligned, Is.True);
            Assert.That(state.ResetAlignmentObject("first"), Is.True);
            Assert.That(Snapshot("first").rotation, Is.EqualTo(270));
        }
    }
}
