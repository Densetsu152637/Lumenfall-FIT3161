using Lumenfall.DataDriven;
using Lumenfall.Spells;
using NUnit.Framework;
using UnityEngine;

namespace Lumenfall.FeatureAcceptance
{
    [Category("PR04")]
    public sealed class Pr04WeaveNetworkAcceptanceTests
    {
        private GameObject host;
        private WorldStateService state;
        [SetUp] public void SetUp()
        {
            host = new GameObject("PR04 isolated state"); host.SetActive(false);
            state = host.AddComponent<WorldStateService>();
            state.BeginMapRebuild("weave-acceptance");
            Register("source", 0, false); Register("relay", 2, true); Register("sink", 4, true);
            state.EndMapRebuild();
            state.SetProperty("source", x => x.powered = true);
        }
        [TearDown] public void TearDown() { Object.DestroyImmediate(host); }
        private void Register(string id, int x, bool conductor) => state.RegisterObject(id, null, new Vector2Int(x, 0), Vector2Int.one, true, conductor ? new[] { "weave", "conductor" } : new[] { "ampere" });
        private bool Powered(string id) { Assert.That(state.TryGetObject(id, out WorldObjectSnapshot x), Is.True); return x.powered; }
        private void Connect() { Assert.That(state.AddConnection("source", "relay"), Is.True); Assert.That(state.AddConnection("relay", "sink"), Is.True); }

        [Test] public void RemovingUpstreamCableDepowersTheWholeDownstreamChain()
        {
            Connect();
            Assert.That(Powered("relay") && Powered("sink"), Is.True);
            Assert.That(state.RemoveConnection("relay", "source"), Is.True);
            Assert.That(Powered("source"), Is.True);
            Assert.That(Powered("relay") || Powered("sink"), Is.False);
            Assert.That(state.IsConnected("relay", "sink"), Is.True);
            Assert.That(state.GetConnectionEffectivePower("relay", "sink"), Is.Zero);
        }

        [Test] public void MovingAnEndpointPastStrengthLimitLeavesCableButStopsPropagation()
        {
            Connect();
            state.SetProperty("relay", x => x.cell = new Vector2Int(10, 0));
            Assert.That(state.IsConnected("source", "relay"), Is.True);
            Assert.That(state.GetConnectionStrength("source", "relay"), Is.LessThan(WorldStateService.MinimumCableStrength));
            Assert.That(Powered("relay") || Powered("sink"), Is.False);
            state.SetProperty("relay", x => x.cell = new Vector2Int(2, 0));
            Assert.That(Powered("relay") && Powered("sink"), Is.True);
        }

        [Test] public void DarkOrdinaryRelicCannotRelayPowerToAConductor()
        {
            state.RegisterObject("ordinary", null, Vector2Int.right, Vector2Int.one, true, new[] { "weave" });
            Assert.That(state.AddConnection("source", "ordinary"), Is.True);
            Assert.That(state.AddConnection("ordinary", "sink"), Is.True);
            Assert.That(Powered("ordinary") || Powered("sink"), Is.False);
            state.SetProperty("ordinary", x => x.powered = true);
            Assert.That(Powered("sink"), Is.True);
        }

        [Test] public void RequiredCableThresholdTracksLivePowerAndRestoresWithSave()
        {
            Connect();
            var target = new PuzzleTargetDefinition { objectIds = new[] { "sink" }, requiredPowered = true, requiredConnections = new[] { "source|relay", "relay|sink" }, minimumEffectivePower = 0.3f };
            Assert.That(state.EvaluatePuzzleTarget(target), Is.True);
            string saved = state.CaptureStateJson();
            state.SetProperty("source", x => x.powered = false);
            Assert.That(state.EvaluatePuzzleTarget(target), Is.False);
            Assert.That(state.RestoreStateJson(saved), Is.True);
            Assert.That(state.EvaluatePuzzleTarget(target), Is.True);
            target.minimumEffectivePower = 0.34f;
            Assert.That(state.EvaluatePuzzleTarget(target), Is.False);
        }
    }
}
