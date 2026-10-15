using Lumenfall.DataDriven;
using Lumenfall.Spells;
using NUnit.Framework;
using UnityEngine;

namespace Lumenfall.Tests.GameEvents
{
    public sealed class WeaveNetworkTests
    {
        private GameObject host;
        private WorldStateService state;

        [SetUp]
        public void SetUp()
        {
            host = new GameObject("isolated weave network");
            host.SetActive(false);
            state = host.AddComponent<WorldStateService>();
            state.BeginMapRebuild("weave-network-test");
            Register("source", 0, false);
            Register("relay", 2, true);
            Register("sink", 4, true);
            state.EndMapRebuild();
            state.SetProperty("source", item => item.powered = true);
            Assert.That(state.AddConnection("source", "relay"), Is.True);
            Assert.That(state.AddConnection("relay", "sink"), Is.True);
        }

        [TearDown]
        public void TearDown() { Object.DestroyImmediate(host); }

        private void Register(string id, int x, bool conductor) => state.RegisterObject(
            id, null, new Vector2Int(x, 0), Vector2Int.one, true,
            conductor ? new[] { "weave", "conductor" } : new[] { "ampere" });

        private bool Powered(string id)
        {
            Assert.That(state.TryGetObject(id, out WorldObjectSnapshot item), Is.True);
            return item.powered;
        }

        [Test]
        public void MovingAnEndpointPastStrengthLimitLeavesCableButStopsPropagation()
        {
            Assert.That(Powered("relay") && Powered("sink"), Is.True);
            state.SetProperty("relay", item => item.cell = new Vector2Int(10, 0));
            Assert.That(state.IsConnected("source", "relay"), Is.True);
            Assert.That(state.GetConnectionStrength("source", "relay"), Is.LessThan(WorldStateService.MinimumCableStrength));
            Assert.That(Powered("relay") || Powered("sink"), Is.False);
            state.SetProperty("relay", item => item.cell = new Vector2Int(2, 0));
            Assert.That(Powered("relay") && Powered("sink"), Is.True);
        }

        [Test]
        public void RequiredCableThresholdTracksLivePowerAndRestoresWithSave()
        {
            var target = new PuzzleTargetDefinition
            {
                objectIds = new[] { "sink" }, requiredPowered = true,
                requiredConnections = new[] { "source|relay", "relay|sink" }, minimumEffectivePower = 0.3f
            };
            Assert.That(state.EvaluatePuzzleTarget(target), Is.True);
            string saved = state.CaptureStateJson();
            state.SetProperty("source", item => item.powered = false);
            Assert.That(state.EvaluatePuzzleTarget(target), Is.False);
            Assert.That(state.RestoreStateJson(saved), Is.True);
            Assert.That(state.EvaluatePuzzleTarget(target), Is.True);
            target.minimumEffectivePower = 0.34f;
            Assert.That(state.EvaluatePuzzleTarget(target), Is.False);
        }
    }
}
